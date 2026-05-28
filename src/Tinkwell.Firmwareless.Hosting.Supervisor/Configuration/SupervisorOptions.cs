namespace Tinkwell.Firmwareless.Hosting.Supervisor.Configuration;

public sealed class SupervisorOptions
{
    public int TcpPort { get; set; } = 9500;
    public string HostExePath { get; set; } = "";
    public string RouterExePath { get; set; } = "";
    public string FirmletBaseDir { get; set; } = "/var/lib/tinkwell/firmlets";
    public int SystemTimeoutSeconds { get; set; } = 120;
    public int HealthIntervalSeconds { get; set; } = 30;
    public int RouterHeartbeatTimeoutSeconds { get; set; } = 15;
    public int RouterRestartWindowSeconds { get; set; } = 300;
    public int MaxRouterRestarts { get; set; } = 3;
    public int MaxHostRestartsPerHour { get; set; } = 10;
    public int StartupTimeoutSeconds { get; set; } = 30;
    public int ShutdownGraceSeconds { get; set; } = 10;
    public double CpuThresholdPercent { get; set; } = 90;
    public long MemoryThresholdBytes { get; set; } = 512 * 1024 * 1024;
}
