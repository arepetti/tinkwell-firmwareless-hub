using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Runlet.Firmwareless.Proxy.Configuration;

namespace Tinkwell.Runlet.Firmwareless.Proxy;

public sealed class TunnelService : BackgroundService
{
    private readonly TcpTunnel _tunnel;
    private readonly ProxyOptions _options;
    private readonly ILogger<TunnelService> _logger;

    public TunnelService(TcpTunnel tunnel, ProxyOptions options, ILogger<TunnelService> logger)
    {
        _tunnel = tunnel;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _tunnel
                    .ConnectAsync(_options.ProcessMonitorHost, _options.ProcessMonitorPort, stoppingToken)
                    .ConfigureAwait(false);

                try
                {
                    await _tunnel.DisconnectedTask.WaitAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TCP tunnel to process monitor lost; reconnecting in 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        await _tunnel.DisposeAsync().ConfigureAwait(false);
    }
}