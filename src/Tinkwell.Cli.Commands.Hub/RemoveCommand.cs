using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class RemoveSettings : HubSettings
{
    [Description("Asset ID (GUID) to remove")]
    [CommandArgument(0, "<asset-id>")]
    public string AssetId { get; set; } = "";

    [Description("Keep firmlet files on disk (skip deletion)")]
    [CommandOption("--keep-files")]
    [DefaultValue(false)]
    public bool KeepFiles { get; set; }

    [Description("Base directory where firmlet directories are stored")]
    [CommandOption("--firmlet-base-dir")]
    [DefaultValue(null)]
    public string? FirmletBaseDir { get; set; }

    public string ResolveFirmletBaseDir()
    {
        if (!string.IsNullOrWhiteSpace(FirmletBaseDir))
            return FirmletBaseDir;

        var env = Environment.GetEnvironmentVariable("TW_FIRMLET_BASE_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tinkwell", "Firmlets");

        return "/var/lib/tinkwell/firmlets";
    }
}

[CliCommand("hub", "remove", Description = "Stop a host, unregister the asset, and optionally delete firmlet files")]
public sealed class RemoveCommand : AsyncCommand<RemoveSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, RemoveSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);

        if (!Guid.TryParse(settings.AssetId, out var assetId))
        {
            output.WriteError("Invalid asset ID (expected GUID)");
            return 1;
        }

        using var grpc = GrpcFactory.FromSettings(settings);

        await output.RunWithStatusAsync("Stopping host...", async () =>
        {
            var reply = await grpc.Proxy.StopHostAsync(new StopHostRequest
            {
                AssetId = assetId.ToString(),
                Reason = "remove",
            }, cancellationToken: ct);

            if (!reply.Success)
                output.WriteWarning($"StopHost: {reply.Error}");
        });

        await output.RunWithStatusAsync("Removing asset from registry...", async () =>
        {
            var reply = await grpc.Assets.RemoveAssetAsync(new RemoveAssetRequest
            {
                Id = assetId.ToString(),
            }, cancellationToken: ct);

            if (!reply.Success)
                throw new InvalidOperationException("RemoveAsset failed");
        });

        var baseDir = settings.ResolveFirmletBaseDir();

        await CancelPendingDownloadAsync(baseDir, assetId.ToString());

        if (!settings.KeepFiles)
        {
            var firmletDir = Path.Combine(baseDir, assetId.ToString());
            if (Directory.Exists(firmletDir))
            {
                Directory.Delete(firmletDir, recursive: true);
                output.WriteMarkup($"[dim]Deleted {firmletDir}[/]");
            }
        }

        output.WriteSuccess($"Asset [cyan]{assetId}[/] removed");
        return 0;
    }

    private static async Task CancelPendingDownloadAsync(string baseDir, string assetId)
    {
        var filePath = Path.Combine(baseDir, "pending-downloads.json");
        if (!File.Exists(filePath))
            return;

        try
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            };
            var json = await File.ReadAllTextAsync(filePath);
            var list = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? [];
            var filtered = list.Where(e =>
                !e.TryGetProperty("assetId", out var id) ||
                !string.Equals(id.GetString(), assetId, StringComparison.OrdinalIgnoreCase)).ToList();

            if (filtered.Count < list.Count)
                await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(filtered, opts));
        }
        catch
        {
        }
    }
}
