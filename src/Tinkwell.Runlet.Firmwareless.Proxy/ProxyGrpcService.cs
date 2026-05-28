using Google.Protobuf;
using Grpc.Core;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;
using AssetRegistryClient = Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto.AssetRegistryService.AssetRegistryServiceClient;

namespace Tinkwell.Runlet.Firmwareless.Proxy;

public sealed class ProxyGrpcService : FirmwarelessProxy.FirmwarelessProxyBase
{
    private static readonly TimeSpan IpcTimeout = TimeSpan.FromMinutes(2);

    private readonly TcpTunnel _tunnel;
    private readonly AssetRegistryClient _assets;
    private readonly SemaphoreSlim _ipcRoundTripLock = new(1, 1);

    public ProxyGrpcService(TcpTunnel tunnel, AssetRegistryClient assets)
    {
        _tunnel = tunnel;
        _assets = assets;
    }

    public override async Task<ForwardToHostReply> ForwardToHost(ForwardToHostRequest request, ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
        {
            return new ForwardToHostReply
            {
                Success = false,
                Error = "TCP tunnel to process monitor is not connected",
            };
        }

        if (string.IsNullOrWhiteSpace(request.AssetId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "asset_id is required"));

        if (!await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unknown asset"));

        IpcEnvelope envelope;
        try
        {
            envelope = IpcEnvelope.Parser.ParseFrom(request.Envelope);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid envelope payload: " + ex.Message));
        }

        if (!string.IsNullOrEmpty(envelope.AssetId) &&
            !string.Equals(envelope.AssetId, request.AssetId, StringComparison.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Envelope asset_id does not match request"));
        }

        envelope.AssetId = request.AssetId;

        await _ipcRoundTripLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(IpcTimeout);

            var wait = _tunnel.WaitForAsync(
                e => MatchesAsset(e, request.AssetId) && IsLikelyUnaryResponse(e),
                cts.Token);

            await _tunnel.SendAsync(envelope, cts.Token).ConfigureAwait(false);
            var reply = await wait.ConfigureAwait(false);

            return new ForwardToHostReply
            {
                Success = true,
                ResponseEnvelope = ByteString.CopyFrom(reply.ToByteArray()),
            };
        }
        catch (OperationCanceledException)
        {
            return new ForwardToHostReply { Success = false, Error = "Timed out or cancelled waiting for host reply" };
        }
        finally
        {
            _ipcRoundTripLock.Release();
        }
    }

    public override async Task ForwardFromHost(
        ForwardFromHostRequest request,
        IServerStreamWriter<HostMessage> responseStream,
        ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
            throw new RpcException(new Status(StatusCode.Unavailable, "TCP tunnel to process monitor is not connected"));

        if (!string.IsNullOrEmpty(request.AssetId) &&
            !await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Unknown asset"));
        }

        async Task Handler(IpcEnvelope env)
        {
            var label = GetAssetLabel(env);
            if (!string.IsNullOrEmpty(request.AssetId) && !string.Equals(label, request.AssetId, StringComparison.Ordinal))
                return;

            await responseStream
                .WriteAsync(new HostMessage { AssetId = label, Envelope = ByteString.CopyFrom(env.ToByteArray()) })
                .ConfigureAwait(false);
        }

        _tunnel.MessageReceived += Handler;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or call cancelled
        }
        finally
        {
            _tunnel.MessageReceived -= Handler;
        }
    }

    public override async Task<StartHostReply> StartHost(StartHostRequest request, ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
            return new StartHostReply { Success = false, Error = "TCP tunnel to process monitor is not connected" };

        if (string.IsNullOrWhiteSpace(request.AssetId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "asset_id is required"));

        if (!await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
            return new StartHostReply { Success = false, Error = "Unknown asset" };

        var envelope = new IpcEnvelope
        {
            AssetId = request.AssetId,
            StartHost = new StartHost { AssetId = request.AssetId, FirmletPath = request.FirmletPath },
        };

        await _ipcRoundTripLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(IpcTimeout);

            var wait = _tunnel.WaitForAsync(
                e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.HostStarted &&
                     e.HostStarted.AssetId == request.AssetId,
                cts.Token);

            await _tunnel.SendAsync(envelope, cts.Token).ConfigureAwait(false);
            await wait.ConfigureAwait(false);

            return new StartHostReply { Success = true };
        }
        catch (OperationCanceledException)
        {
            return new StartHostReply { Success = false, Error = "Timed out or cancelled waiting for HostStarted" };
        }
        finally
        {
            _ipcRoundTripLock.Release();
        }
    }

    public override async Task<StopHostReply> StopHost(StopHostRequest request, ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
            return new StopHostReply { Success = false, Error = "TCP tunnel to process monitor is not connected" };

        if (string.IsNullOrWhiteSpace(request.AssetId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "asset_id is required"));

        if (!await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
            return new StopHostReply { Success = false, Error = "Unknown asset" };

        var envelope = new IpcEnvelope
        {
            AssetId = request.AssetId,
            StopHost = new StopHost { AssetId = request.AssetId, Reason = request.Reason },
        };

        await _ipcRoundTripLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(IpcTimeout);

            var wait = _tunnel.WaitForAsync(
                e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.HostStopped &&
                     e.HostStopped.AssetId == request.AssetId,
                cts.Token);

            await _tunnel.SendAsync(envelope, cts.Token).ConfigureAwait(false);
            await wait.ConfigureAwait(false);

            return new StopHostReply { Success = true };
        }
        catch (OperationCanceledException)
        {
            return new StopHostReply { Success = false, Error = "Timed out or cancelled waiting for HostStopped" };
        }
        finally
        {
            _ipcRoundTripLock.Release();
        }
    }

    public override async Task<Tinkwell.Runlet.Firmwareless.Proxy.Proto.WriteMeasureReply> WriteMeasure(
        WriteMeasureRequest request,
        ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
        {
            return new Tinkwell.Runlet.Firmwareless.Proxy.Proto.WriteMeasureReply
            {
                Success = false,
                Error = "TCP tunnel to process monitor is not connected",
            };
        }

        if (string.IsNullOrWhiteSpace(request.AssetId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "asset_id is required"));

        if (!await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
        {
            return new Tinkwell.Runlet.Firmwareless.Proxy.Proto.WriteMeasureReply
            {
                Success = false,
                Error = "Unknown asset",
            };
        }

        if (request.MeasureValueCase == WriteMeasureRequest.MeasureValueOneofCase.None)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, "Either float_value or quantity_value must be set"));
        }

        var measure = new WriteMeasure { Name = request.Name, TimestampMs = request.TimestampMs };
        switch (request.MeasureValueCase)
        {
            case WriteMeasureRequest.MeasureValueOneofCase.FloatValue:
                measure.FloatValue = request.FloatValue;
                break;
            case WriteMeasureRequest.MeasureValueOneofCase.QuantityValue:
                measure.QuantityValue = request.QuantityValue;
                break;
        }
        if (!string.IsNullOrEmpty(request.Unit))
            measure.Unit = request.Unit;

        var envelope = new IpcEnvelope
        {
            AssetId = request.AssetId,
            WriteMeasure = measure,
        };

        await _ipcRoundTripLock.WaitAsync(context.CancellationToken).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(IpcTimeout);

            var wait = _tunnel.WaitForAsync(
                e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.WriteMeasureReply && MatchesAsset(e, request.AssetId),
                cts.Token);

            await _tunnel.SendAsync(envelope, cts.Token).ConfigureAwait(false);
            var ipc = await wait.ConfigureAwait(false);

            return new Tinkwell.Runlet.Firmwareless.Proxy.Proto.WriteMeasureReply
            {
                Success = ipc.WriteMeasureReply.Success,
                Error = ipc.WriteMeasureReply.Error,
            };
        }
        catch (OperationCanceledException)
        {
            return new Tinkwell.Runlet.Firmwareless.Proxy.Proto.WriteMeasureReply
            {
                Success = false,
                Error = "Timed out or cancelled waiting for WriteMeasureReply",
            };
        }
        finally
        {
            _ipcRoundTripLock.Release();
        }
    }

    private async Task<bool> VerifyAssetExistsAsync(string assetId, CancellationToken ct)
    {
        var reply = await _assets.GetAssetAsync(new GetAssetRequest { Id = assetId }, cancellationToken: ct)
            .ConfigureAwait(false);
        return reply.Found;
    }

    private static string GetAssetLabel(IpcEnvelope e)
    {
        if (!string.IsNullOrEmpty(e.AssetId))
            return e.AssetId;

        return e.PayloadCase switch
        {
            IpcEnvelope.PayloadOneofCase.HostStarted => e.HostStarted.AssetId,
            IpcEnvelope.PayloadOneofCase.HostStopped => e.HostStopped.AssetId,
            IpcEnvelope.PayloadOneofCase.StartHost => e.StartHost.AssetId,
            IpcEnvelope.PayloadOneofCase.StopHost => e.StopHost.AssetId,
            _ => "",
        };
    }

    private static bool MatchesAsset(IpcEnvelope e, string assetId) =>
        string.Equals(GetAssetLabel(e), assetId, StringComparison.Ordinal);

    public override async Task<SuspendAssetReply> SuspendAsset(SuspendAssetRequest request, ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
            return new SuspendAssetReply { Success = false, Error = "TCP tunnel not connected" };

        if (string.IsNullOrWhiteSpace(request.AssetId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "asset_id is required"));

        if (!await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
            return new SuspendAssetReply { Success = false, Error = "Unknown asset" };

        var envelope = new IpcEnvelope
        {
            AssetId = request.AssetId,
            SuspendFirmlet = new SuspendFirmlet { Reason = request.Reason },
        };

        await _tunnel.SendAsync(envelope, context.CancellationToken).ConfigureAwait(false);
        return new SuspendAssetReply { Success = true };
    }

    public override async Task<ResumeAssetReply> ResumeAsset(ResumeAssetRequest request, ServerCallContext context)
    {
        if (!_tunnel.IsConnected)
            return new ResumeAssetReply { Success = false, Error = "TCP tunnel not connected" };

        if (string.IsNullOrWhiteSpace(request.AssetId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "asset_id is required"));

        if (!await VerifyAssetExistsAsync(request.AssetId, context.CancellationToken).ConfigureAwait(false))
            return new ResumeAssetReply { Success = false, Error = "Unknown asset" };

        var envelope = new IpcEnvelope
        {
            AssetId = request.AssetId,
            ResumeFirmlet = new ResumeFirmlet(),
        };

        await _tunnel.SendAsync(envelope, context.CancellationToken).ConfigureAwait(false);
        return new ResumeAssetReply { Success = true };
    }

    public override Task<GetHealthReply> GetHealth(GetHealthRequest request, ServerCallContext context)
    {
        var snapshot = _lastHealthSnapshot;
        if (snapshot is null)
            return Task.FromResult(new GetHealthReply());

        return Task.FromResult(MapSnapshot(snapshot));
    }

    public override async Task StreamHealth(
        StreamHealthRequest request,
        IServerStreamWriter<HealthSnapshotMessage> responseStream,
        ServerCallContext context)
    {
        async Task Handler(IpcEnvelope env)
        {
            if (env.PayloadCase != IpcEnvelope.PayloadOneofCase.HealthSnapshot)
                return;
            _lastHealthSnapshot = env.HealthSnapshot;

            var msg = new HealthSnapshotMessage { TimestampMs = env.HealthSnapshot.TimestampMs };
            if (env.HealthSnapshot.Router is not null)
                msg.Router = MapHostHealth(env.HealthSnapshot.Router);
            foreach (var h in env.HealthSnapshot.Hosts)
                msg.Hosts.Add(MapHostHealth(h));

            await responseStream.WriteAsync(msg).ConfigureAwait(false);
        }

        _tunnel.MessageReceived += Handler;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _tunnel.MessageReceived -= Handler;
        }
    }

    private volatile HealthSnapshot? _lastHealthSnapshot;

    private static GetHealthReply MapSnapshot(HealthSnapshot snapshot)
    {
        var reply = new GetHealthReply { TimestampMs = snapshot.TimestampMs };
        if (snapshot.Router is not null)
            reply.Router = MapHostHealth(snapshot.Router);
        foreach (var h in snapshot.Hosts)
            reply.Hosts.Add(MapHostHealth(h));
        return reply;
    }

    private static HostHealthInfo MapHostHealth(HostHealthReport r) => new()
    {
        AssetId = r.AssetId,
        FirmletName = r.FirmletName,
        FirmletState = r.FirmletState.ToString(),
        CpuPercent = r.CpuPercent,
        WorkingSetBytes = r.WorkingSetBytes,
        ThreadCount = r.ThreadCount,
        Status = r.Status,
        UptimeMs = r.UptimeMs,
        RestartCount = r.RestartCount,
    };

    public override async Task StreamLogs(
        StreamLogsRequest request,
        IServerStreamWriter<LogMessage> responseStream,
        ServerCallContext context)
    {
        var filterAssets = new HashSet<string>(request.AssetIds, StringComparer.OrdinalIgnoreCase);
        var allAssets = request.AllAssets;
        var systemOnly = !allAssets && filterAssets.Count == 0;

        async Task Handler(IpcEnvelope env)
        {
            if (env.PayloadCase != IpcEnvelope.PayloadOneofCase.LogEntry)
                return;

            var assetId = env.AssetId;
            var isSystem = string.IsNullOrEmpty(assetId) || assetId == "__supervisor__" || assetId == "__router__";

            if (systemOnly && !isSystem)
                return;
            if (!systemOnly && !allAssets && !filterAssets.Contains(assetId))
                return;

            var msg = new LogMessage
            {
                AssetId = assetId,
                Module = env.LogEntry.Module,
                Level = env.LogEntry.Level,
                Message = env.LogEntry.Message,
                TimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            await responseStream.WriteAsync(msg).ConfigureAwait(false);
        }

        _tunnel.MessageReceived += Handler;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _tunnel.MessageReceived -= Handler;
        }
    }

    private static bool IsLikelyUnaryResponse(IpcEnvelope e) =>
        e.PayloadCase switch
        {
            IpcEnvelope.PayloadOneofCase.ServiceReply => true,
            IpcEnvelope.PayloadOneofCase.SendCommandReply => true,
            IpcEnvelope.PayloadOneofCase.ReadSensorReply => true,
            IpcEnvelope.PayloadOneofCase.WriteMeasureReply => true,
            IpcEnvelope.PayloadOneofCase.FindServiceReply => true,
            IpcEnvelope.PayloadOneofCase.ListServicesReply => true,
            IpcEnvelope.PayloadOneofCase.ServiceExistsReply => true,
            IpcEnvelope.PayloadOneofCase.HostStarted => true,
            IpcEnvelope.PayloadOneofCase.HostStopped => true,
            _ => false,
        };
}
