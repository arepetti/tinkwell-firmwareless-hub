namespace Tinkwell.Runlet.Firmwareless.CoAP.Configuration;

/// <summary>
/// UDP CoAP listener and upstream gRPC endpoints for the firmwareless LAN gateway.
/// </summary>
public sealed class CoapRunletOptions
{
    /// <summary>UDP port for the CoAP server (default CoAP is 5683/5684).</summary>
    public int Port { get; set; } = 5684;

    /// <summary>Base URL of the proxy runlet (gRPC), e.g. http://localhost:5000.</summary>
    public string ProxyAddress { get; set; } = "http://localhost:5000";

    /// <summary>Base URL of the asset registry runlet (gRPC), e.g. http://localhost:5100.</summary>
    public string AssetRegistryAddress { get; set; } = "http://localhost:5100";

    /// <summary>
    /// Base URL of the Tinkwell measures runlet (gRPC). If empty, measure
    /// bridging from device telemetry is disabled.
    /// </summary>
    public string MeasuresAddress { get; set; } = "";
}
