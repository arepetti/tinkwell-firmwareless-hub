using System.Collections.Concurrent;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Firmwareless.Hosting.Router.Ipc;

public sealed class IpcServer
{
    private readonly ConcurrentDictionary<string, HostConnection> _connections = new();
    private readonly ILogger<IpcServer> _logger;

    public event Func<string, IpcEnvelope, Task>? MessageReceived;
    public event Action<string>? HostConnected;

    public IpcServer(ILogger<IpcServer> logger) => _logger = logger;

    public string CreatePipeName(string hostId) => $"tinkwell-host-{hostId}";

    public bool IsConnected(string hostId) =>
        _connections.TryGetValue(hostId, out var conn) && conn.Connected;

    public Task AcceptConnectionAsync(string hostId, CancellationToken ct) =>
        AcceptConnectionAsync(hostId, CreatePipeName(hostId), ct);

    public async Task AcceptConnectionAsync(string hostId, string pipeName, CancellationToken ct)
    {
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        try
        {
            await pipe.WaitForConnectionAsync(ct);
            var conn = new HostConnection(hostId, pipe);
            _connections[hostId] = conn;
            _logger.LogDebug("Host {HostId} connected via pipe {Pipe}", hostId, pipeName);
            HostConnected?.Invoke(hostId);
            _ = ReceiveLoopAsync(conn, ct);
        }
        catch (OperationCanceledException)
        {
            await pipe.DisposeAsync();
        }
    }

    public async Task SendAsync(string hostId, IpcEnvelope envelope, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(hostId, out var conn) || !conn.Connected)
        {
            _logger.LogWarning("Cannot send to {HostId}: not connected", hostId);
            return;
        }
        await IpcProtocol.WriteAsync(conn.Pipe, envelope, ct);
    }

    public async Task BroadcastAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        foreach (var conn in _connections.Values.Where(c => c.Connected))
            await IpcProtocol.WriteAsync(conn.Pipe, envelope, ct);
    }

    private async Task ReceiveLoopAsync(HostConnection conn, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && conn.Connected)
            {
                IpcEnvelope? envelope;
                try
                {
                    envelope = await IpcProtocol.ReadAsync(conn.Pipe, ct);
                }
                catch (IpcFramingException ex)
                {
                    _logger.LogWarning("Bad IPC frame from {HostId}: {Reason} -- skipping message", conn.HostId, ex.Message);
                    continue;
                }

                if (envelope is null)
                    break;

                if (MessageReceived is not null)
                    await MessageReceived.Invoke(conn.HostId, envelope);
            }
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Pipe read error for {HostId}", conn.HostId);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, HostConnection>>)_connections)
                .Remove(new KeyValuePair<string, HostConnection>(conn.HostId, conn));
            await conn.Pipe.DisposeAsync();
            _logger.LogDebug("Host {HostId} disconnected", conn.HostId);
        }
    }

    private sealed class HostConnection(string hostId, NamedPipeServerStream pipe)
    {
        public string HostId { get; } = hostId;
        public NamedPipeServerStream Pipe { get; } = pipe;
        public bool Connected => Pipe.IsConnected;
    }
}