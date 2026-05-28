using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Supervisor.Configuration;

namespace Tinkwell.Firmwareless.Hosting.Supervisor;

public sealed class ProcessSupervisor : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ManagedProcess> _processes = new();
    private readonly SupervisorOptions _options;
    private readonly ILogger<ProcessSupervisor> _logger;

    public ProcessSupervisor(SupervisorOptions options, ILogger<ProcessSupervisor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IEnumerable<ManagedProcess> ActiveProcesses => _processes.Values;

    public Task SpawnRouterAsync(string pipeName, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.RouterExePath,
            Arguments = $"--pipe \"{pipeName}\" --tcp-port {_options.TcpPort}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        return SpawnInternalAsync("__router__", psi, ct);
    }

    public Task SpawnHostAsync(string hostId, string modulesPath, string routerPipeName, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.HostExePath,
            Arguments = $"--pipe \"{routerPipeName}\" --host-id \"{hostId}\" --modules \"{modulesPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        return SpawnInternalAsync(hostId, psi, ct);
    }

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _spawnLocks = new();

    private async Task SpawnInternalAsync(string id, ProcessStartInfo psi, CancellationToken ct)
    {
        var spawnLock = _spawnLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await spawnLock.WaitAsync(ct);
        try
        {
            if (_processes.ContainsKey(id))
            {
                _logger.LogWarning("Process {Id} already running, terminating first", id);
                await TerminateAsync(id, ct);
            }

            _logger.LogInformation("Spawning process {Id}: {Exe} {Args}", id, psi.FileName, psi.Arguments);

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start process for {id}");

            var managed = new ManagedProcess(id, process);
            _processes[id] = managed;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger.LogDebug("[{Id}:stdout] {Line}", id, e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger.LogWarning("[{Id}:stderr] {Line}", id, e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _ = MonitorProcessAsync(managed, psi, ct);
        }
        finally
        {
            spawnLock.Release();
        }
    }

    public async Task TerminateAsync(string id, CancellationToken ct = default)
    {
        if (!_processes.TryRemove(id, out var managed))
            return;

        managed.RestartEnabled = false;

        try
        {
            if (!managed.Process.HasExited)
            {
                managed.Process.Kill(entireProcessTree: true);
                try { await managed.Process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(_options.ShutdownGraceSeconds), ct); }
                catch (TimeoutException)
                {
                }
            }
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating process {Id}", id);
        }
        finally
        {
            managed.Process.Dispose();
        }

        _logger.LogInformation("Process {Id} terminated", id);
    }

    public async Task TerminateAllAsync(CancellationToken ct = default)
    {
        var tasks = _processes.Keys.Select(id => TerminateAsync(id, ct));
        await Task.WhenAll(tasks);
    }

    private async Task MonitorProcessAsync(ManagedProcess managed, ProcessStartInfo psi, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && managed.RestartEnabled)
        {
            try
            {
                await managed.Process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!managed.RestartEnabled)
                return;

            var exitCode = managed.Process.ExitCode;

            var now = DateTime.UtcNow;
            managed.RecordRestart(now);
            var recentCount = managed.GetRecentRestartCount();

            if (_options.MaxHostRestartsPerHour > 0 &&
                managed.Id != "__router__" &&
                recentCount >= _options.MaxHostRestartsPerHour)
            {
                _logger.LogCritical(
                    "Process {Id} has restarted {Count} times in the last hour. Circuit breaker open.",
                    managed.Id, recentCount);
                managed.RestartEnabled = false;
                return;
            }

            var delay = CalculateSigmoidDelay(recentCount);
            _logger.LogWarning(
                "Process {Id} exited with code {Code}. Restarting in {Delay:F1}s (attempt {Count})",
                managed.Id, exitCode, delay, recentCount);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                managed.Process.Dispose();
                var process = Process.Start(psi)
                    ?? throw new InvalidOperationException($"Failed to restart process for {managed.Id}");
                managed.Process = process;
                managed.StartTime = DateTime.UtcNow;

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        _logger.LogDebug("[{Id}:stdout] {Line}", managed.Id, e.Data);
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        _logger.LogWarning("[{Id}:stderr] {Line}", managed.Id, e.Data);
                    }
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart process {Id}", managed.Id);
                managed.RestartEnabled = false;
                _processes.TryRemove(managed.Id, out _);
                return;
            }
        }
    }

    private static double CalculateSigmoidDelay(int recentRestartCount)
    {
        const double cap = 60;
        const double k = 0.9;
        const double midpoint = 4;
        return Math.Clamp(Math.Round(cap / (1 + Math.Exp(-k * (recentRestartCount - midpoint))), 1), 1, cap);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, managed) in _processes)
        {
            managed.RestartEnabled = false;
            try
            {
                managed.Process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            managed.Process.Dispose();
        }
        _processes.Clear();
        return ValueTask.CompletedTask;
    }

    public sealed class ManagedProcess
    {
        public ManagedProcess(string id, Process process)
        {
            Id = id;
            Process = process;
            StartTime = DateTime.UtcNow;
        }

        private readonly object _lock = new();

        public string Id { get; }
        public Process Process { get; set; }
        public volatile bool RestartEnabled = true;
        public DateTime StartTime { get; set; }
        public int RestartCount;
        public bool Ready { get; set; }

        private readonly List<DateTime> _restartTimestamps = new();

        public void RecordRestart(DateTime now)
        {
            lock (_lock)
            {
                _restartTimestamps.RemoveAll(t => t < now - TimeSpan.FromHours(1));
                _restartTimestamps.Add(now);
                Interlocked.Increment(ref RestartCount);
            }
        }

        public int GetRecentRestartCount()
        {
            lock (_lock) { return _restartTimestamps.Count; }
        }
    }
}