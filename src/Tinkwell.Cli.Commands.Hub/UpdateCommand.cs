using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;
using Tinkwell.Firmlets.Registry.Client;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class UpdateSettings : HubSettings
{
    [Description("Asset ID to update (omit for --all)")]
    [CommandArgument(0, "[asset-id]")]
    [DefaultValue(null)]
    public string? AssetId { get; set; }

    [Description("Update all assets that have a newer version available")]
    [CommandOption("--all")]
    [DefaultValue(false)]
    public bool All { get; set; }

    [Description("Only check for updates without installing")]
    [CommandOption("--check")]
    [DefaultValue(false)]
    public bool CheckOnly { get; set; }

    [Description("Registry URL")]
    [CommandOption("--registry-url")]
    [DefaultValue(null)]
    public string? RegistryUrl { get; set; }

    [Description("Hub API key for registry authentication")]
    [CommandOption("--api-key")]
    [DefaultValue(null)]
    public string? ApiKey { get; set; }

    [Description("Target architecture")]
    [CommandOption("--arch")]
    [DefaultValue(null)]
    public string? Architecture { get; set; }

    [Description("Base directory for firmlet installation")]
    [CommandOption("--firmlet-base-dir")]
    [DefaultValue(null)]
    public string? FirmletBaseDir { get; set; }

    public string ResolveRegistryUrl() =>
        !string.IsNullOrWhiteSpace(RegistryUrl) ? RegistryUrl
        : Environment.GetEnvironmentVariable("TW_FIRMLET_REGISTRY_URL") ?? "http://localhost:5200";

    public string ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey
        : Environment.GetEnvironmentVariable("TW_FIRMLET_HUB_API_KEY") ?? "";

    public string ResolveArchitecture() =>
        !string.IsNullOrWhiteSpace(Architecture) ? Architecture
        : $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}-linux";

    public string ResolveFirmletBaseDir()
    {
        if (!string.IsNullOrWhiteSpace(FirmletBaseDir))
            return FirmletBaseDir;
        var env = Environment.GetEnvironmentVariable("TW_FIRMLET_BASE_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tinkwell", "Firmlets")
            : "/var/lib/tinkwell/firmlets";
    }
}

[CliCommand("hub", "update", Description = "Check for and apply firmlet updates from the registry")]
public sealed class UpdateCommand : AsyncCommand<UpdateSettings>
{
    private static readonly ColumnDef<UpdateRow>[] Columns =
    [
        new("Asset ID", r => r.AssetId),
        new("Firmlet", r => r.FirmletName),
        new("Installed", r => r.CurrentVersion),
        new("Available", r => r.LatestVersion),
        new("Status", r => r.Status),
    ];

    public override async Task<int> ExecuteAsync(
        CommandContext context, UpdateSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);

        if (string.IsNullOrWhiteSpace(settings.AssetId) && !settings.All)
        {
            output.WriteError("Provide an <asset-id> or use --all");
            return 1;
        }

        using var grpc = GrpcFactory.FromSettings(settings);
        using var registry = new RegistryClientFactory(settings.ResolveRegistryUrl(), settings.ResolveApiKey());

        var assets = await GetTargetAssetsAsync(grpc, settings, ct);
        if (assets.Count == 0)
        {
            output.WriteMarkup("[dim]No assets with firmlet info found[/]");
            return 0;
        }

        var results = new List<UpdateRow>();
        foreach (var asset in assets)
        {
            var latest = await CheckLatestVersionAsync(registry.Client, asset.FirmletName, ct);
            if (latest is null || string.Equals(latest, asset.FirmletVersion, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new(asset.Id, asset.FirmletName, asset.FirmletVersion, latest ?? asset.FirmletVersion, "up to date"));
                continue;
            }

            if (settings.CheckOnly)
            {
                results.Add(new(asset.Id, asset.FirmletName, asset.FirmletVersion, latest, "update available"));
                continue;
            }

            var status = await ApplyUpdateAsync(grpc, registry.Client, asset, latest, settings, output, ct);
            results.Add(new(asset.Id, asset.FirmletName, asset.FirmletVersion, latest, status));
        }

        output.WriteTable("Update Results", Columns, results);
        return results.Any(r => r.Status == "failed") ? 1 : 0;
    }

    private static async Task<List<AssetInfo>> GetTargetAssetsAsync(
        GrpcFactory grpc, UpdateSettings settings, CancellationToken ct)
    {
        var reply = await grpc.Assets.ListAssetsAsync(new Google.Protobuf.WellKnownTypes.Empty(), cancellationToken: ct);
        var assets = reply.Assets.Where(a => !string.IsNullOrEmpty(a.FirmletName)).ToList();

        if (!string.IsNullOrWhiteSpace(settings.AssetId))
            assets = assets.Where(a => string.Equals(a.Id, settings.AssetId, StringComparison.OrdinalIgnoreCase)).ToList();

        return assets;
    }

    private static async Task<string?> CheckLatestVersionAsync(
        FirmletRegistryApiClient client, string firmletName, CancellationToken ct)
    {
        try
        {
            var info = await client.GetAsync($"firmlets/{Uri.EscapeDataString(firmletName)}", ct);

            return info.TryGetProperty("latestVersion", out var lv) ? lv.GetString()
                : info.TryGetProperty("version", out var v) ? v.GetString()
                : null;
        }
        catch
        {
        }
        return null;
    }

    private static async Task<string> ApplyUpdateAsync(
        GrpcFactory grpc, FirmletRegistryApiClient client, AssetInfo asset, string version,
        UpdateSettings settings, OutputContext output, CancellationToken ct)
    {
        try
        {
            var arch = settings.ResolveArchitecture();
            var downloadPath = $"firmlets/{Uri.EscapeDataString(asset.FirmletName)}/download?arch={Uri.EscapeDataString(arch)}&version={Uri.EscapeDataString(version)}";

            var baseDir = settings.ResolveFirmletBaseDir();
            var installDir = Path.Combine(baseDir, asset.Id);
            var tempZip = Path.Combine(Path.GetTempPath(), $"firmlet-update-{Guid.NewGuid():N}.zip");

            try
            {
                using var stream = await client.DownloadAsync(downloadPath, ct);
                await using var file = File.Create(tempZip);
                await stream.CopyToAsync(file, ct);

                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, recursive: true);
                Directory.CreateDirectory(installDir);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, installDir);
            }
            finally
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }

            await grpc.Assets.RegisterAssetAsync(new RegisterAssetRequest
            {
                Id = asset.Id,
                DisplayName = asset.DisplayName,
                VendorId = asset.VendorId,
                ProductId = asset.ProductId,
                FirmletName = asset.FirmletName,
                FirmletVersion = version,
                FirmletPath = installDir,
                CommunicationMode = asset.CommunicationMode,
            }, cancellationToken: ct);

            return "updated";
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            output.WriteWarning($"Update failed for {asset.Id}: {ex.Message}");
            return "failed";
        }
    }

    private sealed record UpdateRow(string AssetId, string FirmletName, string CurrentVersion, string LatestVersion, string Status);
}