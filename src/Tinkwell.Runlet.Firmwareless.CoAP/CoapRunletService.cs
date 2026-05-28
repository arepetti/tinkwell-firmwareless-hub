using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Coap;
using Tinkwell.Coap.Server;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.CoAP.Configuration;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;
using ProxyClient = Tinkwell.Runlet.Firmwareless.Proxy.Proto.FirmwarelessProxy.FirmwarelessProxyClient;
using AssetClient = Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto.AssetRegistryService.AssetRegistryServiceClient;

namespace Tinkwell.Runlet.Firmwareless.CoAP;

/// <summary>
/// Hosts the LAN CoAP server, forwards device traffic to the proxy runlet, and consults the asset registry.
/// </summary>
public sealed class CoapRunletService : BackgroundService
{
    private readonly CoapRunletOptions _options;
    private readonly SensorCache _sensorCache;
    private readonly MeasureBridge _measures;
    private readonly ILogger<CoapRunletService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private ProxyClient _proxy = null!;
    private AssetClient _assets = null!;

    public CoapRunletService(
        CoapRunletOptions options,
        SensorCache sensorCache,
        MeasureBridge measures,
        ILogger<CoapRunletService> logger,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _sensorCache = sensorCache;
        _measures = measures;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var proxyChannel = GrpcChannel.ForAddress(_options.ProxyAddress);
        using var assetChannel = GrpcChannel.ForAddress(_options.AssetRegistryAddress);

        _proxy = new ProxyClient(proxyChannel);
        _assets = new AssetClient(assetChannel);

        var server = new CoapServer(
            new CoapServerOptions { Port = _options.Port, Name = "coap-runlet" },
            _loggerFactory.CreateLogger<CoapServer>());

        server.MapPost("/hub/heartbeat", HandleHeartbeatAsync);
        server.MapPost("/hub/telemetry", HandleTelemetryAsync);

        _logger.LogInformation(
            "CoAP runlet listening on UDP port {Port}; proxy={Proxy}; assets={Assets}",
            _options.Port,
            _options.ProxyAddress,
            _options.AssetRegistryAddress);

        await server.RunAsync(stoppingToken);
    }

    private async Task<CoapResponse> HandleHeartbeatAsync(CoapRequest request, CancellationToken ct)
    {
        if (request.Payload.Length == 0)
            return CoapResponse.BadRequest("empty heartbeat payload");

        try
        {
            var deviceId = ExtractDeviceId(request.Payload);
            if (deviceId == Guid.Empty)
                deviceId = TryParseGuidFromQuery(request.Query);
            if (deviceId == Guid.Empty)
                return CoapResponse.BadRequest("missing device id");

            var assetId = deviceId.ToString();

            var assetReply = await _assets.GetAssetAsync(new GetAssetRequest { Id = assetId }, cancellationToken: ct);
            if (!assetReply.Found || assetReply.Asset is null)
            {
                _logger.LogWarning("Heartbeat from unknown asset {AssetId}", assetId);
                return CoapResponse.NotFound();
            }

            await _assets.RecordHeartbeatAsync(new RecordHeartbeatRequest { AssetId = assetId }, cancellationToken: ct);

            DeviceHeartbeat heartbeat;
            try
            {
                heartbeat = DeviceHeartbeat.Parser.ParseFrom(request.Payload.Span);
                if (heartbeat.DeviceId.Length == 0)
                    heartbeat.DeviceId = ByteString.CopyFrom(deviceId.ToByteArray());
            }
            catch (InvalidProtocolBufferException)
            {
                heartbeat = new DeviceHeartbeat { DeviceId = ByteString.CopyFrom(deviceId.ToByteArray()) };
            }

            var envelope = new IpcEnvelope
            {
                AssetId = assetId,
                DeviceHeartbeat = heartbeat,
            };

            var forward = await _proxy.ForwardToHostAsync(
                new ForwardToHostRequest
                {
                    AssetId = assetId,
                    Envelope = ByteString.CopyFrom(envelope.ToByteArray()),
                },
                cancellationToken: ct);

            if (!forward.Success)
                _logger.LogWarning("ForwardToHost failed for heartbeat {AssetId}: {Error}", assetId, forward.Error);

            var pendingReply = await _assets.GetPendingCommandCountAsync(
                new GetPendingCommandCountRequest { AssetId = assetId },
                cancellationToken: ct);

            var pending = (int)Math.Clamp(pendingReply.Count, 0, ushort.MaxValue);
            var replyPayload = new byte[2];
            replyPayload[0] = (byte)(pending & 0xFF);
            replyPayload[1] = (byte)((pending >> 8) & 0xFF);

            return CoapResponse.Content(replyPayload, CoapContentFormat.ApplicationOctetStream);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling heartbeat");
            return CoapResponse.InternalError("heartbeat processing failed");
        }
    }

    private async Task<CoapResponse> HandleTelemetryAsync(CoapRequest request, CancellationToken ct)
    {
        if (request.Payload.Length == 0)
            return CoapResponse.BadRequest("empty telemetry payload");

        try
        {
            var deviceId = ExtractDeviceId(request.Payload);
            if (deviceId == Guid.Empty)
                deviceId = TryParseGuidFromQuery(request.Query);
            if (deviceId == Guid.Empty)
            {
                _logger.LogWarning(
                    "Telemetry without device id from {Remote}; embed id in field 1 or add ?id= to the query string",
                    request.RemoteEndpoint);
                return CoapResponse.BadRequest("missing device id");
            }

            var assetId = deviceId.ToString();

            var assetReply = await _assets.GetAssetAsync(new GetAssetRequest { Id = assetId }, cancellationToken: ct);
            if (!assetReply.Found || assetReply.Asset is null)
            {
                _logger.LogWarning("Telemetry from unknown asset {AssetId}", assetId);
                return CoapResponse.NotFound();
            }

            DeviceTelemetry telemetry;
            try
            {
                telemetry = DeviceTelemetry.Parser.ParseFrom(request.Payload.Span);
            }
            catch (InvalidProtocolBufferException ex)
            {
                _logger.LogWarning(ex, "Invalid telemetry protobuf from {AssetId}", assetId);
                return CoapResponse.BadRequest("invalid telemetry payload");
            }

            foreach (var reading in telemetry.Readings)
                _sensorCache.Update(deviceId, reading.Name, reading.Value, reading.TimestampMs);

            if (_measures.IsEnabled)
            {
                await _measures.WriteTelemetryAsync(assetId, telemetry, ct);
                _logger.LogDebug("Bridged {Count} measures for asset {AssetId}", telemetry.Readings.Count, assetId);
            }

            var envelope = new IpcEnvelope
            {
                AssetId = assetId,
                DeviceTelemetry = telemetry,
            };

            var forward = await _proxy.ForwardToHostAsync(
                new ForwardToHostRequest
                {
                    AssetId = assetId,
                    Envelope = ByteString.CopyFrom(envelope.ToByteArray()),
                },
                cancellationToken: ct);

            if (!forward.Success)
                _logger.LogWarning("ForwardToHost failed for telemetry {AssetId}: {Error}", assetId, forward.Error);

            return CoapResponse.Changed();
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling telemetry");
            return CoapResponse.InternalError("telemetry processing failed");
        }
    }

    /// <summary>
    /// Extracts a 16-byte GUID from the first field of a protobuf payload.
    /// Field 1, wire type 2 (length-delimited), 16 bytes.
    /// </summary>
    private static Guid ExtractDeviceId(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        if (span.Length < 18)
            return Guid.Empty;

        // protobuf field 1 wire type 2 = tag 0x0A, length 16
        if (span[0] == 0x0A && span[1] == 0x10)
            return new Guid(span.Slice(2, 16));

        return Guid.Empty;
    }

    private static Guid TryParseGuidFromQuery(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return Guid.Empty;

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = part[..eq];
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);

            if (!key.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("asset_id", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("asset", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Guid.TryParse(value, out var g))
                return g;
        }

        return Guid.Empty;
    }
}