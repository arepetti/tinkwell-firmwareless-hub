using System.ComponentModel;
using Spectre.Console.Cli;
using Tinkwell.Cli;

namespace Tinkwell.Cli.Commands.Hub;

public class HubSettings : TwSettings
{
    [Description("Proxy runlet gRPC address")]
    [CommandOption("--proxy-url")]
    [DefaultValue(null)]
    public string? ProxyUrl { get; set; }

    [Description("Asset registry runlet gRPC address")]
    [CommandOption("--asset-registry-url")]
    [DefaultValue(null)]
    public string? AssetRegistryUrl { get; set; }

    public string ResolveProxyUrl()
    {
        if (!string.IsNullOrWhiteSpace(ProxyUrl))
            return ProxyUrl;

        var env = Environment.GetEnvironmentVariable("TW_HUB_PROXY_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return "http://localhost:5000";
    }

    public string ResolveAssetRegistryUrl()
    {
        if (!string.IsNullOrWhiteSpace(AssetRegistryUrl))
            return AssetRegistryUrl;

        var env = Environment.GetEnvironmentVariable("TW_HUB_ASSET_REGISTRY_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return "http://localhost:5100";
    }
}
