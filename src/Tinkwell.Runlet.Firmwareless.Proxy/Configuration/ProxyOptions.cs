namespace Tinkwell.Runlet.Firmwareless.Proxy.Configuration;

public sealed class ProxyOptions
{
    public string ProcessMonitorHost { get; set; } = "localhost";
    public int ProcessMonitorPort { get; set; } = 9500;
    public string AssetRegistryAddress { get; set; } = "http://localhost:5100";
    public string StateStoreAddress { get; set; } = "";
    public int HealthTtlSeconds { get; set; } = 120;

    public bool DockerManageContainer { get; set; } = true;
    public string DockerImage { get; set; } = "tinkwell-hub:latest";
    public string DockerContainerName { get; set; } = "tinkwell-hub";
    public long DockerMemoryLimitBytes { get; set; } = 512 * 1024 * 1024;
    public long DockerCpuNanos { get; set; } = 1_000_000_000;
    public string DockerFirmletVolume { get; set; } = "tinkwell-firmlets";
    public int DockerStopTimeoutSeconds { get; set; } = 15;
}
