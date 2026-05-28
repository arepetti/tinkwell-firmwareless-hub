using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Firmwareless.Hosting.Router.Configuration;
using Tinkwell.Firmwareless.Hosting.Router.Ipc;
using Tinkwell.Firmwareless.Hosting.Router.Tcp;

namespace Tinkwell.Firmwareless.Hosting.Router;

public sealed class RouterService : BackgroundService
{
    private readonly IpcServer _ipc;
    private readonly TcpBridge _tcp;
    private readonly ServiceRegistry _services;
    private readonly RouterOptions _options;
    private readonly ILogger<RouterService> _logger;

    private readonly ConcurrentDictionary<string, string> _pendingCalls = new();

    public RouterService(
        IpcServer ipc, TcpBridge tcp, ServiceRegistry services,
        RouterOptions options, ILogger<RouterService> logger)
    {
        _ipc = ipc;
        _tcp = tcp;
        _services = services;
        _options = options;
        _logger = logger;

        _ipc.MessageReceived += OnPipeMessageAsync;
        _tcp.MessageReceived += OnTcpMessageAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Router starting, TCP port {Port}", _options.TcpPort);
        await _tcp.StartAsync(stoppingToken);

        _ipc.HostConnected += hostId =>
        {
            _logger.LogInformation("Host {HostId} pipe connected", hostId);
        };

        if (!string.IsNullOrEmpty(_options.SupervisorPipeName))
            _ = _ipc.AcceptConnectionAsync("__supervisor__", _options.SupervisorPipeName, stoppingToken);

        _logger.LogInformation("Router running");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public async Task AcceptHostAsync(string hostId, CancellationToken ct)
    {
        await _ipc.AcceptConnectionAsync(hostId, ct);
    }

    private async Task OnPipeMessageAsync(string hostId, IpcEnvelope envelope)
    {
        switch (envelope.PayloadCase)
        {
            case IpcEnvelope.PayloadOneofCase.RegisterClient:
                _logger.LogInformation("Host {HostId} registered", hostId);
                break;

            case IpcEnvelope.PayloadOneofCase.Ready:
                _logger.LogInformation("Host {HostId} ready, modules: {Modules}",
                    hostId, string.Join(", ", envelope.Ready.LoadedModules));
                break;

            case IpcEnvelope.PayloadOneofCase.Fatal:
                _logger.LogError("Host {HostId} fatal: {Message}", hostId, envelope.Fatal.Message);
                break;

            case IpcEnvelope.PayloadOneofCase.RegisterService:
                _services.Register(hostId, envelope.RegisterService);
                _logger.LogInformation("Service registered: {Name} by {HostId}",
                    envelope.RegisterService.FullName, hostId);
                break;

            case IpcEnvelope.PayloadOneofCase.FindService:
                var reg = _services.FindByFamily(envelope.FindService.FamilyName);
                await _ipc.SendAsync(hostId, new IpcEnvelope
                {
                    FindServiceReply = new FindServiceReply
                    {
                        Found = reg is not null,
                        FullName = reg?.FullName ?? "",
                        State = reg?.State ?? ServiceState.Defined,
                    }
                });
                break;

            case IpcEnvelope.PayloadOneofCase.ListServices:
                var reply = new ListServicesReply();
                reply.Services.AddRange(_services.ListAll());
                await _ipc.SendAsync(hostId, new IpcEnvelope { ListServicesReply = reply });
                break;

            case IpcEnvelope.PayloadOneofCase.ServiceExists:
                var (exists, state) = _services.ExistsWithState(envelope.ServiceExists.Name);
                await _ipc.SendAsync(hostId, new IpcEnvelope
                {
                    ServiceExistsReply = new ServiceExistsReply { Exists = exists, State = state }
                });
                break;

            case IpcEnvelope.PayloadOneofCase.ServiceCall:
                if (string.IsNullOrEmpty(envelope.ServiceCall.ServiceName) && string.IsNullOrEmpty(envelope.ServiceCall.CorrelationId))
                {
                    _logger.LogWarning("ServiceCall from {HostId} missing service_name and correlation_id", hostId);
                    break;
                }
                await HandleServiceCallAsync(hostId, envelope.ServiceCall);
                break;

            case IpcEnvelope.PayloadOneofCase.ServiceReply:
                if (_pendingCalls.TryRemove(envelope.ServiceReply.CorrelationId, out var callerHostId))
                {
                    await _ipc.SendAsync(callerHostId, envelope);
                }
                else
                {
                    _logger.LogWarning("ServiceReply with unknown correlation {Id} from {HostId}",
                        envelope.ServiceReply.CorrelationId, hostId);
                }
                break;

            case IpcEnvelope.PayloadOneofCase.SendCommand:
            case IpcEnvelope.PayloadOneofCase.WriteMeasure:
                envelope.AssetId = hostId;
                if (_tcp.IsConnected)
                    await _tcp.SendAsync(envelope);
                break;

            case IpcEnvelope.PayloadOneofCase.LogEntry:
                HandleLogEntry(hostId, envelope.LogEntry);
                envelope.AssetId = hostId;
                if (_tcp.IsConnected)
                    await _tcp.SendAsync(envelope);
                break;

            case IpcEnvelope.PayloadOneofCase.FirmletStateChanged:
                _logger.LogInformation("Host {HostId} state: {State}", hostId, envelope.FirmletStateChanged.State);
                if (_tcp.IsConnected)
                {
                    envelope.AssetId = hostId;
                    await _tcp.SendAsync(envelope);
                }
                break;

            case IpcEnvelope.PayloadOneofCase.HealthSnapshot:
                if (_tcp.IsConnected)
                    await _tcp.SendAsync(envelope);
                break;

            case IpcEnvelope.PayloadOneofCase.None:
                _logger.LogWarning("Empty IPC envelope (no payload) from {HostId}", hostId);
                break;

            default:
                _logger.LogWarning("Unhandled pipe message from {HostId}: {Case}", hostId, envelope.PayloadCase);
                break;
        }
    }

    private async Task OnTcpMessageAsync(IpcEnvelope envelope)
    {
        switch (envelope.PayloadCase)
        {
            case IpcEnvelope.PayloadOneofCase.StartHost:
                _logger.LogInformation("StartHost: asset={AssetId}", envelope.StartHost.AssetId);
                break;

            case IpcEnvelope.PayloadOneofCase.StopHost:
                var stopId = envelope.StopHost.AssetId;
                _logger.LogInformation("StopHost: asset={AssetId}", stopId);
                // hostId == assetId by convention; mark both to be safe
                _services.MarkUnavailable(stopId);
                if (_ipc.IsConnected(stopId))
                {
                    await _ipc.SendAsync(stopId, new IpcEnvelope
                    {
                        Shutdown = new Shutdown { Reason = "StopHost" }
                    });
                }
                break;

            case IpcEnvelope.PayloadOneofCase.SuspendFirmlet:
                if (!string.IsNullOrEmpty(envelope.AssetId) && _ipc.IsConnected(envelope.AssetId))
                    await _ipc.SendAsync(envelope.AssetId, envelope);
                break;

            case IpcEnvelope.PayloadOneofCase.ResumeFirmlet:
                if (!string.IsNullOrEmpty(envelope.AssetId) && _ipc.IsConnected(envelope.AssetId))
                    await _ipc.SendAsync(envelope.AssetId, envelope);
                break;

            case IpcEnvelope.PayloadOneofCase.SuspendAsset:
                var suspendId = envelope.SuspendAsset.AssetId;
                if (!string.IsNullOrEmpty(suspendId) && _ipc.IsConnected(suspendId))
                {
                    await _ipc.SendAsync(suspendId, new IpcEnvelope
                    {
                        AssetId = suspendId,
                        SuspendFirmlet = new SuspendFirmlet { Reason = envelope.SuspendAsset.Reason },
                    });
                }
                break;

            case IpcEnvelope.PayloadOneofCase.ResumeAsset:
                var resumeId = envelope.ResumeAsset.AssetId;
                if (!string.IsNullOrEmpty(resumeId) && _ipc.IsConnected(resumeId))
                {
                    await _ipc.SendAsync(resumeId, new IpcEnvelope
                    {
                        AssetId = resumeId,
                        ResumeFirmlet = new ResumeFirmlet(),
                    });
                }
                break;

            case IpcEnvelope.PayloadOneofCase.DeviceHeartbeat:
            case IpcEnvelope.PayloadOneofCase.DeviceTelemetry:
            case IpcEnvelope.PayloadOneofCase.SendCommandReply:
            case IpcEnvelope.PayloadOneofCase.WriteMeasureReply:
                if (!string.IsNullOrEmpty(envelope.AssetId) && _ipc.IsConnected(envelope.AssetId))
                    await _ipc.SendAsync(envelope.AssetId, envelope);
                break;

            default:
                _logger.LogWarning("Unhandled TCP message: {Case}", envelope.PayloadCase);
                break;
        }
    }

    private async Task HandleServiceCallAsync(string hostId, ServiceCall msg)
    {
        var target = _services.Find(msg.ServiceName, msg.ByFamily);
        if (target is null)
        {
            await _ipc.SendAsync(hostId, new IpcEnvelope
            {
                ServiceReply = new ServiceReply
                {
                    CorrelationId = msg.CorrelationId,
                    Success = false,
                    Error = $"Service '{msg.ServiceName}' not found",
                }
            });
            return;
        }

        _pendingCalls[msg.CorrelationId] = hostId;

        await _ipc.SendAsync(target.HostId, new IpcEnvelope
        {
            ServiceCall = new ServiceCall
            {
                CorrelationId = msg.CorrelationId,
                ServiceName = target.FullName,
                MethodName = msg.MethodName,
                Request = msg.Request,
                ByFamily = false,
                TimeoutMs = msg.TimeoutMs,
            }
        });
    }

    private void HandleLogEntry(string hostId, LogEntry msg)
    {
        var logLevel = msg.Level switch
        {
            0 => LogLevel.Trace, 1 => LogLevel.Debug, 2 => LogLevel.Information,
            3 => LogLevel.Warning, 4 => LogLevel.Error, _ => LogLevel.Critical,
        };
        _logger.Log(logLevel, "[firmlet:{HostId}:{Module}] {Message}", hostId, msg.Module, msg.Message);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Router stopping...");
        _ipc.MessageReceived -= OnPipeMessageAsync;
        _tcp.MessageReceived -= OnTcpMessageAsync;
        await base.StopAsync(ct);
    }
}
