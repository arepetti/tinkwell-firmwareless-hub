using System.Buffers.Binary;
using System.Net.Sockets;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Runlet.Firmwareless.Proxy;

public sealed class TcpTunnel : IAsyncDisposable
{
    public const int HeaderSize = 4;
    public const int MaxMessageSize = 4 * 1024 * 1024;

    private readonly ILogger<TcpTunnel> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private TaskCompletionSource _disconnectTcs = CreateDisconnectTcs();

    public TcpTunnel(ILogger<TcpTunnel> logger)
    {
        _logger = logger;
    }

    public bool IsConnected => _client?.Connected == true && _stream is not null;

    /// <summary>Raised for every inbound frame (after successful parse). Handlers run sequentially.</summary>
    public event Func<IpcEnvelope, Task>? MessageReceived;

    /// <summary>Completes when the read loop exits (EOF, error, or dispose).</summary>
    public Task DisconnectedTask => _disconnectTcs.Task;

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await DisposeCoreAsync().ConfigureAwait(false);

        _disconnectTcs = CreateDisconnectTcs();
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var client = new TcpClient();
        await client.ConnectAsync(host, port, ct).ConfigureAwait(false);

        _client = client;
        _stream = client.GetStream();
        _logger.LogInformation("TCP tunnel connected to {Host}:{Port}", host, port);

        _readTask = RunReadLoopAsync(_readCts.Token);
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        if (_stream is null)
            throw new InvalidOperationException("TCP tunnel is not connected");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteFrameAsync(_stream, envelope, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Waits for the first inbound envelope matching <paramref name="predicate"/>, or until cancelled.
    /// </summary>
    public async Task<IpcEnvelope> WaitForAsync(
        Func<IpcEnvelope, bool> predicate,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<IpcEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Handler(IpcEnvelope env)
        {
            try
            {
                if (predicate(env))
                    tcs.TrySetResult(env);
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        MessageReceived += Handler;
        try
        {
            using var reg = cancellationToken.Register(static s => ((TaskCompletionSource<IpcEnvelope>)s!).TrySetCanceled(), tcs);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            MessageReceived -= Handler;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync().ConfigureAwait(false);
        if (!_disconnectTcs.Task.IsCompleted)
            _disconnectTcs.TrySetResult();
        _writeLock.Dispose();
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            _readCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_readTask is not null)
        {
            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Read task ended with exception");
            }
        }

        _readTask = null;

        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _client?.Dispose();
        _client = null;

        _readCts?.Dispose();
        _readCts = null;
    }

    private async Task RunReadLoopAsync(CancellationToken ct)
    {
        var stream = _stream;
        if (stream is null)
            return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var envelope = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
                if (envelope is null)
                    break;

                await RaiseMessageReceivedAsync(envelope).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TCP tunnel read loop failed");
        }
        finally
        {
            _logger.LogInformation("TCP tunnel read loop ended");
            _disconnectTcs.TrySetResult();
        }
    }

    private async Task RaiseMessageReceivedAsync(IpcEnvelope envelope)
    {
        var handler = MessageReceived;
        if (handler is null)
            return;

        foreach (var inv in handler.GetInvocationList().Cast<Func<IpcEnvelope, Task>>())
        {
            await inv(envelope).ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource CreateDisconnectTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WriteFrameAsync(Stream stream, IpcEnvelope envelope, CancellationToken ct)
    {
        var payload = envelope.ToByteArray();
        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, HeaderSize), (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderSize));

        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IpcEnvelope?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[HeaderSize];
        var read = await ReadExactAsync(stream, header, ct).ConfigureAwait(false);
        if (read < HeaderSize)
            return null;

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length > MaxMessageSize)
            throw new InvalidOperationException($"IPC message too large: {length} bytes (max {MaxMessageSize})");

        var payload = new byte[length];
        read = await ReadExactAsync(stream, payload, ct).ConfigureAwait(false);
        if (read < (int)length)
            throw new InvalidOperationException("IPC message truncated");

        return IpcEnvelope.Parser.ParseFrom(payload);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (n == 0)
                return offset;
            offset += n;
        }

        return offset;
    }
}