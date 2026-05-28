using Google.Protobuf;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Firmwareless.Hosting.Router.Ipc;

public static class IpcProtocol
{
    public const int HeaderSize = 4;
    public const int MaxMessageSize = 4 * 1024 * 1024;

    public static byte[] Frame(IpcEnvelope envelope)
    {
        var payload = envelope.ToByteArray();
        var frame = new byte[HeaderSize + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, HeaderSize), payload.Length);
        payload.CopyTo(frame, HeaderSize);
        return frame;
    }

    public static async Task WriteAsync(Stream stream, IpcEnvelope envelope, CancellationToken ct = default)
    {
        var frame = Frame(envelope);
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// Reads and parses one length-prefixed IPC envelope. Returns <c>null</c>
    /// on clean EOF. Throws <see cref="IpcFramingException"/> if the frame is
    /// malformed (bad length, truncated) so the caller can decide whether to
    /// continue or disconnect.
    /// </summary>
    public static async Task<IpcEnvelope?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[HeaderSize];
        var bytesRead = await ReadExactAsync(stream, header, ct);
        if (bytesRead == 0)
            return null;
        if (bytesRead < HeaderSize)
            throw new IpcFramingException($"Incomplete header: got {bytesRead} of {HeaderSize} bytes");

        var length = BitConverter.ToInt32(header);
        if (length <= 0 || length > MaxMessageSize)
            throw new IpcFramingException($"Invalid message length: {length} (max {MaxMessageSize})");

        var payload = new byte[length];
        bytesRead = await ReadExactAsync(stream, payload, ct);
        if (bytesRead < length)
            throw new IpcFramingException($"Truncated message: got {bytesRead} of {length} bytes");

        try
        {
            return IpcEnvelope.Parser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new IpcFramingException($"Invalid protobuf payload ({length} bytes): {ex.Message}", ex);
        }
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                return offset;
            offset += read;
        }
        return offset;
    }
}

public sealed class IpcFramingException : Exception
{
    public IpcFramingException(string message) : base(message) { }
    public IpcFramingException(string message, Exception inner) : base(message, inner) { }
}
