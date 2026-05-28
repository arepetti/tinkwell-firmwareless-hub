using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Firmwareless.Hosting.Router.Ipc;

namespace Tinkwell.Firmwareless.Hosting.Router.Tcp;

public sealed class TcpBridge : IAsyncDisposable
{
    private readonly int _port;
    private readonly ILogger<TcpBridge> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private TcpListener? _listener;
    private volatile TcpClient? _client;
    private volatile NetworkStream? _stream;

    public event Func<IpcEnvelope, Task>? MessageReceived;
    public bool IsConnected => _client?.Connected ?? false;

    public TcpBridge(int port, ILogger<TcpBridge> logger)
    {
        _port = port;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _logger.LogInformation("TCP bridge listening on port {Port}", _port);

        _ = AcceptLoopAsync(ct);
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        var stream = _stream;
        if (stream is null)
            return;

        await _writeLock.WaitAsync(ct);
        try
        {
            await IpcProtocol.WriteAsync(stream, envelope, ct);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TCP bridge send failed");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _logger.LogInformation("Proxy runlet connected");

                CleanupCurrentClient();
                _client = client;
                _stream = client.GetStream();

                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TCP bridge accept/receive error, re-accepting");
            }
            finally
            {
                CleanupCurrentClient();
                _logger.LogInformation("Proxy runlet disconnected, waiting for reconnect");
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _stream is not null)
        {
            IpcEnvelope? envelope;
            try
            {
                envelope = await IpcProtocol.ReadAsync(_stream, ct);
            }
            catch (IpcFramingException ex)
            {
                _logger.LogWarning("Bad TCP frame from proxy: {Reason} -- skipping message", ex.Message);
                continue;
            }

            if (envelope is null)
                break;
            if (MessageReceived is not null)
                await MessageReceived.Invoke(envelope);
        }
    }

    private void CleanupCurrentClient()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
        }
        try
        {
            _client?.Dispose();
        }
        catch
        {
        }
        _stream = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        CleanupCurrentClient();
        _listener?.Stop();
        _writeLock.Dispose();
    }
}