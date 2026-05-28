using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Firmwareless.Hosting.Supervisor.Configuration;
using Tinkwell.Firmwareless.Hosting.Supervisor.Ipc;
using Tinkwell.Firmwareless.Hosting.Supervisor.Monitoring;

namespace Tinkwell.Firmwareless.Hosting.Supervisor;

public sealed class SupervisorService : BackgroundService
{
    private readonly ProcessSupervisor _supervisor;
    private readonly HostHealthCollector _healthCollector;
    private readonly SupervisorOptions _options;
    private readonly ILogger<SupervisorService> _logger;
    private readonly IpcClient _ipc = new();

    public SupervisorService(
        ProcessSupervisor supervisor,
        HostHealthCollector healthCollector,
        SupervisorOptions options,
        ILogger<SupervisorService> logger)
    {
        _supervisor = supervisor;
        _healthCollector = healthCollector;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Supervisor starting, TCP port {Port}", _options.TcpPort);

        var routerPipeName = $"tinkwell-router-{Guid.NewGuid():N}";

        await _supervisor.SpawnRouterAsync(routerPipeName, stoppingToken);
        _logger.LogInformation("Router spawned, pipe: {Pipe}", routerPipeName);

        await ConnectToRouterAsync(routerPipeName, stoppingToken);

        // TODO: spawn WasmHost processes based on firmlet discovery
        // TODO: monitor Router heartbeat, restart if needed with backoff

        _ = RunHealthCollectionLoopAsync(stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ConnectToRouterAsync(string pipeName, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(_options.StartupTimeoutSeconds);
        try
        {
            await _ipc.ConnectAsync(pipeName, timeout, ct);
            _logger.LogInformation("Connected to Router pipe: {Pipe}", pipeName);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Router pipe; health forwarding disabled");
        }
    }

    private async Task RunHealthCollectionLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.HealthIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var snapshot = _healthCollector.Collect();
                _logger.LogDebug("Health: {HostCount} hosts, router={RouterStatus}",
                    snapshot.Hosts.Count, snapshot.Router?.Status ?? "n/a");

                if (_ipc.IsConnected)
                    await _ipc.SendAsync(new IpcEnvelope { HealthSnapshot = snapshot }, ct);
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health collection failed");
            }
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Supervisor stopping...");
        await _ipc.DisposeAsync();
        await _supervisor.TerminateAllAsync(ct);
        await base.StopAsync(ct);
    }
}