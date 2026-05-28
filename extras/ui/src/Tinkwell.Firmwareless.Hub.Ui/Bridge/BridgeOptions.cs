namespace Tinkwell.Firmwareless.Hub.Ui.Bridge;

/// <summary>
/// Configuration for gRPC bridge endpoints. Read from appsettings.json
/// or environment variables in standalone mode, or from runner config
/// in runlet mode.
/// </summary>
public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";

    public string ProxyAddress { get; set; } = "http://localhost:50051";
    public string AssetRegistryAddress { get; set; } = "http://localhost:50052";
    public string StateStoreAddress { get; set; } = "http://localhost:50053";
}
