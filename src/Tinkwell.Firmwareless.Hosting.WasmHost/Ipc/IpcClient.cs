using System.Collections.Concurrent;
using System.IO.Pipes;
using Google.Protobuf;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Firmwareless.Hosting.WasmHost.Ipc;

public sealed class IpcClient : IAsyncDisposable
{
    private const int HeaderSize = 4;
    private const int MaxMessageSize = 4 * 1024 * 1024;

    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope>> _pendingReplies = new();

    public event Func<IpcEnvelope, Task>? UnsolicitedMessage;

    public bool IsConnected => _pipe.IsConnected;

    public IpcClient(string pipeName)
    {
        _pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }

    public async Task ConnectAsync(int timeoutMs = 10_000, CancellationToken ct = default)
    {
        await _pipe.ConnectAsync(timeoutMs, ct);
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        var payload = envelope.ToByteArray();
        var frame = new byte[HeaderSize + payload.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderSize));

        await _writeLock.WaitAsync(ct);
        try
        {
            await _pipe.WriteAsync(frame, ct);
            await _pipe.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IpcEnvelope?> SendAndWaitAsync(
        IpcEnvelope request,
        string correlationId,
        Func<IpcEnvelope, bool> matchReply,
        CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies[correlationId] = tcs;

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        try
        {
            await SendAsync(request, ct);
            return await tcs.Task;
        }
        finally
        {
            _pendingReplies.TryRemove(correlationId, out _);
        }
    }

    public async Task<IpcEnvelope?> ReceiveAsync(CancellationToken ct = default)
    {
        await _readLock.WaitAsync(ct);
        try
        {
            return await ReadFrameAsync(ct);
        }
        finally
        {
            _readLock.Release();
        }
    }

    public async Task RunReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsConnected)
        {
            IpcEnvelope? envelope;
            await _readLock.WaitAsync(ct);
            try
            {
                envelope = await ReadFrameAsync(ct);
            }
            finally
            {
                _readLock.Release();
            }

            if (envelope is null)
                break;

            if (TryCompleteCorrelation(envelope))
                continue;

            if (UnsolicitedMessage is not null)
                await UnsolicitedMessage.Invoke(envelope);
        }
    }

    private bool TryCompleteCorrelation(IpcEnvelope envelope)
    {
        string? correlationId = envelope.PayloadCase switch
        {
            IpcEnvelope.PayloadOneofCase.ServiceReply => envelope.ServiceReply.CorrelationId,
            IpcEnvelope.PayloadOneofCase.SendCommandReply => "cmd",
            IpcEnvelope.PayloadOneofCase.ReadSensorReply => "sensor",
            IpcEnvelope.PayloadOneofCase.WriteMeasureReply => "measure",
            IpcEnvelope.PayloadOneofCase.FindServiceReply => "find",
            IpcEnvelope.PayloadOneofCase.ListServicesReply => "list",
            IpcEnvelope.PayloadOneofCase.ServiceExistsReply => "exists",
            _ => null,
        };

        if (correlationId is not null && _pendingReplies.TryRemove(correlationId, out var tcs))
        {
            tcs.TrySetResult(envelope);
            return true;
        }

        return false;
    }

    private async Task<IpcEnvelope?> ReadFrameAsync(CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        var bytesRead = await ReadExactAsync(header, ct);
        if (bytesRead < HeaderSize)
            return null;

        var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length > MaxMessageSize)
            throw new InvalidOperationException(
                $"IPC message too large: {length} bytes (max {MaxMessageSize})");

        var payload = new byte[length];
        bytesRead = await ReadExactAsync(payload, ct);
        if (bytesRead < (int)length)
            throw new InvalidOperationException("IPC message truncated");

        return IpcEnvelope.Parser.ParseFrom(payload);
    }

    private async Task<int> ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _pipe.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                return offset;
            offset += read;
        }
        return offset;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, tcs) in _pendingReplies)
            tcs.TrySetCanceled();
        _pendingReplies.Clear();

        await _pipe.DisposeAsync();
        _writeLock.Dispose();
        _readLock.Dispose();
    }
}
