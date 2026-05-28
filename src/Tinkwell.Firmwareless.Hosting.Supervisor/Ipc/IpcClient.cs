using System.IO.Pipes;
using Google.Protobuf;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Firmwareless.Hosting.Supervisor.Ipc;

/// <summary>
/// Minimal named-pipe client that sends length-prefixed protobuf envelopes
/// to the Router process.
/// </summary>
internal sealed class IpcClient : IAsyncDisposable
{
    private const int HeaderSize = 4;

    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        _pipe = pipe;
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        var pipe = _pipe;
        if (pipe is null || !pipe.IsConnected)
            return;

        var payload = envelope.ToByteArray();
        var frame = new byte[HeaderSize + payload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, HeaderSize), payload.Length);
        payload.CopyTo(frame, HeaderSize);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(frame, ct).ConfigureAwait(false);
            await pipe.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync().ConfigureAwait(false);
            _pipe = null;
        }
        _writeLock.Dispose();
    }
}
