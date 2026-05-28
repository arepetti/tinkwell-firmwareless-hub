using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Firmwareless.Hosting.Supervisor.Configuration;

namespace Tinkwell.Firmwareless.Hosting.Supervisor.Monitoring;

public sealed class HostHealthCollector
{
    private readonly ProcessSupervisor _supervisor;
    private readonly SupervisorOptions _options;
    private readonly ILogger<HostHealthCollector> _logger;
    private readonly Dictionary<string, ProcessSnapshot> _lastSnapshots = new(StringComparer.Ordinal);

    public HostHealthCollector(ProcessSupervisor supervisor, SupervisorOptions options, ILogger<HostHealthCollector> logger)
    {
        _supervisor = supervisor;
        _options = options;
        _logger = logger;
    }

    public HealthSnapshot Collect()
    {
        var now = DateTime.UtcNow;
        var snapshot = new HealthSnapshot
        {
            TimestampMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        foreach (var managed in _supervisor.ActiveProcesses)
        {
            try
            {
                var process = managed.Process;
                if (process.HasExited)
                    continue;

                process.Refresh();

                var previous = _lastSnapshots.GetValueOrDefault(managed.Id);
                var elapsed = previous is not null ? now - previous.Timestamp : TimeSpan.FromSeconds(_options.HealthIntervalSeconds);
                var prevCpu = previous?.CpuTime ?? TimeSpan.Zero;

                var cpuPercent = ResourceMetrics.GetCpuPercent(process, prevCpu, elapsed);
                var workingSet = ResourceMetrics.GetWorkingSetBytes(process);
                var threadCount = ResourceMetrics.GetThreadCount(process);
                var uptimeMs = (ulong)(now - managed.StartTime).TotalMilliseconds;

                var status = "healthy";
                if (cpuPercent > _options.CpuThresholdPercent)
                    status = "degraded";
                if (workingSet > _options.MemoryThresholdBytes)
                    status = "unhealthy";

                var report = new HostHealthReport
                {
                    AssetId = managed.Id,
                    FirmletName = managed.Id,
                    FirmletState = managed.Ready ? FirmletState.Running : FirmletState.Loading,
                    CpuPercent = cpuPercent,
                    WorkingSetBytes = (ulong)workingSet,
                    ThreadCount = threadCount,
                    Status = status,
                    UptimeMs = uptimeMs,
                    RestartCount = managed.RestartCount,
                };

                if (managed.Id == "__router__")
                    snapshot.Router = report;
                else
                    snapshot.Hosts.Add(report);

                _lastSnapshots[managed.Id] = new ProcessSnapshot(now, process.TotalProcessorTime);
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to collect metrics for process {Id}", managed.Id);
            }
        }

        return snapshot;
    }

    private sealed record ProcessSnapshot(DateTime Timestamp, TimeSpan CpuTime);
}