using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Firmwareless.Hosting.WasmHost.Ipc;

namespace Tinkwell.Firmwareless.Hosting.WasmHost.Measures;

public sealed class BuiltInMeasuresService
{
    public const string ServiceFullName = "tinkwell.hub.v1.MeasuresService";

    private readonly MeasureCache _cache;
    private readonly MeasureDefinitionLoader _definitions;
    private readonly IpcClient _ipc;
    private readonly ILogger _logger;

    public BuiltInMeasuresService(
        MeasureCache cache,
        MeasureDefinitionLoader definitions,
        IpcClient ipc,
        ILogger logger)
    {
        _cache = cache;
        _definitions = definitions;
        _ipc = ipc;
        _logger = logger;
    }

    public async Task<ServiceReply> HandleAsync(ServiceCall call, CancellationToken ct)
    {
        try
        {
            return call.MethodName switch
            {
                "WriteMeasure" => await HandleWriteAsync(call, ct),
                "ReadMeasure" => HandleRead(call),
                "ListMeasures" => HandleList(call),
                "GetDefinition" => HandleGetDefinition(call),
                _ => ErrorReply(call.CorrelationId, $"Unknown method: {call.MethodName}"),
            };
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in built-in measures service: {Method}", call.MethodName);
            return ErrorReply(call.CorrelationId, ex.Message);
        }
    }

    private async Task<ServiceReply> HandleWriteAsync(ServiceCall call, CancellationToken ct)
    {
        var name = ExtractNameField(call.Request);
        if (name is null)
            return ErrorReply(call.CorrelationId, "Missing measure name in request");

        var ipcWrite = new WriteMeasure
        {
            Name = name,
            TimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // Extract float_value (field 2, tag 0x11 = wire type 1 / double) from request
        // For prototype: parse the raw bytes for the value field.
        // Full implementation will use the generated FirmletWriteMeasureRequest type.
        var floatValue = ExtractDoubleField(call.Request, fieldNumber: 2);
        if (floatValue.HasValue)
        {
            ipcWrite.FloatValue = floatValue.Value;
            _cache.Set(name, floatValue.Value, "", ipcWrite.TimestampMs);
        }

        var reply = await _ipc.SendAndWaitAsync(
            new IpcEnvelope { WriteMeasure = ipcWrite },
            "measure",
            e => e.PayloadCase == IpcEnvelope.PayloadOneofCase.WriteMeasureReply,
            ct);

        return new ServiceReply
        {
            CorrelationId = call.CorrelationId,
            Success = reply?.WriteMeasureReply?.Success ?? true,
            Error = reply?.WriteMeasureReply?.Error ?? "",
        };
    }

    private ServiceReply HandleRead(ServiceCall call)
    {
        var name = ExtractNameField(call.Request);
        var cached = name is not null ? _cache.Get(name) : null;

        if (cached is null)
            return ErrorReply(call.CorrelationId, $"Measure '{name}' not found in cache");

        return new ServiceReply
        {
            CorrelationId = call.CorrelationId,
            Success = true,
        };
    }

    private ServiceReply HandleList(ServiceCall call)
    {
        var defs = _definitions.ListAll();
        _logger.LogDebug("ListMeasures: returning {Count} definitions", defs.Count);

        return new ServiceReply
        {
            CorrelationId = call.CorrelationId,
            Success = true,
        };
    }

    private ServiceReply HandleGetDefinition(ServiceCall call)
    {
        var name = ExtractNameField(call.Request);
        var def = name is not null ? _definitions.Find(name) : null;

        if (def is null)
            return ErrorReply(call.CorrelationId, $"Definition for '{name}' not found");

        return new ServiceReply
        {
            CorrelationId = call.CorrelationId,
            Success = true,
        };
    }

    internal static string? ExtractNameField(ByteString data)
    {
        if (data.IsEmpty)
            return null;
        try
        {
            var span = data.Span;
            int pos = 0;
            while (pos < span.Length)
            {
                var (fieldNumber, wireType, newPos) = ReadTag(span, pos);
                if (newPos < 0)
                    break;
                pos = newPos;

                if (fieldNumber == 1 && wireType == 2)
                {
                    var (len, lenEnd) = ReadVarint(span, pos);
                    if (lenEnd < 0 || pos + (int)len > span.Length)
                        break;
                    pos = lenEnd;
                    return System.Text.Encoding.UTF8.GetString(span.Slice(pos, (int)len));
                }

                pos = SkipField(span, pos, wireType);
                if (pos < 0)
                    break;
            }
        }
        catch
        {
        }
        return null;
    }

    private static double? ExtractDoubleField(ByteString data, int fieldNumber)
    {
        if (data.IsEmpty)
            return null;
        try
        {
            var span = data.Span;
            int pos = 0;
            while (pos < span.Length)
            {
                var (fn, wireType, newPos) = ReadTag(span, pos);
                if (newPos < 0)
                    break;
                pos = newPos;

                if (fn == fieldNumber && wireType == 1 && pos + 8 <= span.Length)
                    return BitConverter.ToDouble(span.Slice(pos, 8));

                pos = SkipField(span, pos, wireType);
                if (pos < 0)
                    break;
            }
        }
        catch
        {
        }
        return null;
    }

    private static (int fieldNumber, int wireType, int newPos) ReadTag(ReadOnlySpan<byte> span, int pos)
    {
        var (tag, newPos) = ReadVarint(span, pos);
        if (newPos < 0)
            return (0, 0, -1);
        return ((int)(tag >> 3), (int)(tag & 0x7), newPos);
    }

    private static (ulong value, int newPos) ReadVarint(ReadOnlySpan<byte> span, int pos)
    {
        ulong result = 0;
        int shift = 0;
        while (pos < span.Length)
        {
            byte b = span[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return (result, pos);
            shift += 7;
            if (shift >= 64)
                return (0, -1);
        }
        return (0, -1);
    }

    private static int SkipField(ReadOnlySpan<byte> span, int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: // varint
                var (_, newPos) = ReadVarint(span, pos);
                return newPos;
            case 1: // 64-bit
                return pos + 8 <= span.Length ? pos + 8 : -1;
            case 2: // length-delimited
                var (len, lenEnd) = ReadVarint(span, pos);
                return lenEnd >= 0 && lenEnd + (int)len <= span.Length ? lenEnd + (int)len : -1;
            case 5: // 32-bit
                return pos + 4 <= span.Length ? pos + 4 : -1;
            default:
                return -1;
        }
    }

    private static ServiceReply ErrorReply(string correlationId, string error) => new()
    {
        CorrelationId = correlationId,
        Success = false,
        Error = error,
    };
}