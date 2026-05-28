using System.Runtime.InteropServices;

namespace Tinkwell.Runlet.Firmwareless.Provisioning.Configuration;

public sealed class ProvisioningOptions
{
    public string RegistryUrl { get; set; } = "";
    public string? RegistryApiKey { get; set; }
    public string Architecture { get; set; } =
        $"{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}-linux";
    public string FirmletBaseDir { get; set; } = "/var/lib/tinkwell/firmlets";
    public string ProxyAddress { get; set; } = "http://localhost:5000";
    public string AssetRegistryAddress { get; set; } = "http://localhost:5100";
    public int PendingRetryMinutes { get; set; } = 5;
    public int PendingTimeoutHours { get; set; } = 6;
}
