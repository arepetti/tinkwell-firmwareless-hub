using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.WasmHost.Ipc;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Firmwareless.Hosting.WasmHost;

/// <summary>
/// Host functions exported to WASM firmlets via WAMR native bindings.
///
/// These correspond 1:1 to the C-level host function API that firmlets can call.
/// Service registration is handled declaratively via services.tw -- there are no
/// registration host functions. Firmlets only need to export handler functions
/// with names matching their services.tw configuration.
/// </summary>
public sealed class HostFunctions
{
    private readonly IpcClient _ipc;
    private readonly ILogger _logger;
    private int _correlationCounter;

    public HostFunctions(IpcClient ipc, ILogger logger)
    {
        _ipc = ipc;
        _logger = logger;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    /// <summary>
    /// abort(msg, file, line, col) -- Required by AssemblyScript runtime.
    /// Logs the abort message and terminates the module.
    /// </summary>
    public void Abort(string? message, string? file, int line, int column)
    {
        _logger.LogCritical("WASM abort: {Message} at {File}:{Line}:{Column}",
            message, file, line, column);

        try
        {
            _ipc.SendAsync(new IpcEnvelope
            {
                Fatal = new Fatal
                {
                    Message = message ?? "abort() called",
                    Module = file ?? "unknown",
                }
            }).Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort: we're about to terminate regardless
        }

        Environment.Exit(1);
    }

    // ── Logging ─────────────────────────────────────────────────────────

    /// <summary>
    /// tw_log(level, msg) -- Logging routed to process monitor.
    /// </summary>
    public async Task LogAsync(int level, string message)
    {
        await _ipc.SendAsync(new IpcEnvelope
        {
            LogEntry = new LogEntry
            {
                Level = level,
                Message = message,
            }
        });
    }

    // ── Device interaction ──────────────────────────────────────────────

    /// <summary>
    /// tw_send_command(cmd_type, payload) -- Queue a command for the firmlet's own device.
    /// </summary>
    public async Task<bool> SendCommandAsync(string cmdType, byte[] payload)
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                SendCommand = new SendCommand
                {
                    CmdType = cmdType,
                    Payload = Google.Protobuf.ByteString.CopyFrom(payload),
                }
            },
            "cmd",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.SendCommandReply);

        return reply?.SendCommandReply?.Success ?? false;
    }

    /// <summary>
    /// tw_read_sensor(name) -- Read last-known sensor value from own device.
    /// </summary>
    public async Task<(bool Found, double Value, ulong TimestampMs)> ReadSensorAsync(string name)
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                ReadSensor = new ReadSensor { Name = name }
            },
            "sensor",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.ReadSensorReply);

        var r = reply?.ReadSensorReply;
        return r is not null ? (r.Found, r.Value, r.TimestampMs) : (false, 0, 0);
    }

    // ── Inter-firmlet service calls ─────────────────────────────────────

    /// <summary>
    /// tw_call_service(service, method, payload) -- RPC by full service name.
    /// </summary>
    public async Task<(bool Success, byte[] Response, string? Error)> CallServiceAsync(
        string serviceName, string methodName, byte[] payload, uint timeoutMs = 0)
    {
        var correlationId = Interlocked.Increment(ref _correlationCounter).ToString();

        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                ServiceCall = new ServiceCall
                {
                    CorrelationId = correlationId,
                    ServiceName = serviceName,
                    MethodName = methodName,
                    Request = Google.Protobuf.ByteString.CopyFrom(payload),
                    ByFamily = false,
                    TimeoutMs = timeoutMs,
                }
            },
            correlationId,
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.ServiceReply &&
                 e.ServiceReply.CorrelationId == correlationId);

        var r = reply?.ServiceReply;
        return r is not null
            ? (r.Success, r.Response.ToByteArray(), r.Error)
            : (false, [], "no reply");
    }

    /// <summary>
    /// tw_call_service_by_family(family, method, payload) -- RPC by family name.
    /// </summary>
    public async Task<(bool Success, byte[] Response, string? Error)> CallServiceByFamilyAsync(
        string familyName, string methodName, byte[] payload, uint timeoutMs = 0)
    {
        var correlationId = Interlocked.Increment(ref _correlationCounter).ToString();

        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                ServiceCall = new ServiceCall
                {
                    CorrelationId = correlationId,
                    ServiceName = familyName,
                    MethodName = methodName,
                    Request = Google.Protobuf.ByteString.CopyFrom(payload),
                    ByFamily = true,
                    TimeoutMs = timeoutMs,
                }
            },
            correlationId,
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.ServiceReply &&
                 e.ServiceReply.CorrelationId == correlationId);

        var r = reply?.ServiceReply;
        return r is not null
            ? (r.Success, r.Response.ToByteArray(), r.Error)
            : (false, [], "no reply");
    }

    // ── Service discovery ───────────────────────────────────────────────

    /// <summary>
    /// tw_find_service(family) -- Resolve family name to full service name.
    /// </summary>
    public async Task<string?> FindServiceAsync(string familyName)
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                FindService = new FindService { FamilyName = familyName }
            },
            "find",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.FindServiceReply);

        var r = reply?.FindServiceReply;
        return r is not null && r.Found ? r.FullName : null;
    }

    /// <summary>
    /// tw_list_services() -- List all registered services.
    /// </summary>
    public async Task<IReadOnlyList<(string FullName, string FamilyName)>> ListServicesAsync()
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope { ListServices = new ListServices() },
            "list",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.ListServicesReply);

        return reply?.ListServicesReply?.Services
            .Select(s => (s.FullName, s.FamilyName))
            .ToList() ?? [];
    }

    /// <summary>
    /// tw_service_exists(name) -- Check if a service is registered.
    /// </summary>
    public async Task<bool> ServiceExistsAsync(string name)
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                ServiceExists = new ServiceExistsReq { Name = name }
            },
            "exists",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.ServiceExistsReply);

        return reply?.ServiceExistsReply?.Exists ?? false;
    }

    // ── Measures ────────────────────────────────────────────────────────

    public async Task<bool> WriteMeasureFloatAsync(string name, double value)
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                WriteMeasure = new WriteMeasure
                {
                    Name = name,
                    FloatValue = value,
                    TimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }
            },
            "measure",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.WriteMeasureReply);

        return reply?.WriteMeasureReply?.Success ?? false;
    }

    public async Task<bool> WriteMeasureQuantityAsync(string name, string quantityStr)
    {
        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope
            {
                WriteMeasure = new WriteMeasure
                {
                    Name = name,
                    QuantityValue = quantityStr,
                    TimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                }
            },
            "measure",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.WriteMeasureReply);

        return reply?.WriteMeasureReply?.Success ?? false;
    }

    // ── Utilities ───────────────────────────────────────────────────────

    public long GetTimeMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
