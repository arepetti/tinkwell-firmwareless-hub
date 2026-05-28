using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmlets.Registry.Client;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;

namespace Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;

/// <summary>
/// Checks the registry for newer firmlet versions compared to what is
/// installed on this hub. Used both at startup and by the CLI update command.
/// </summary>
public sealed class UpdateChecker
{
    private readonly FirmletRegistryApiClient _registry;
    private readonly AssetRegistryService.AssetRegistryServiceClient _assets;
    private readonly ILogger<UpdateChecker> _logger;

    public UpdateChecker(
        FirmletRegistryApiClient registry,
        AssetRegistryService.AssetRegistryServiceClient assets,
        ILogger<UpdateChecker> logger)
    {
        _registry = registry;
        _assets = assets;
        _logger = logger;
    }

    public async Task<List<UpdateAvailable>> CheckAllAsync(CancellationToken ct = default)
    {
        var updates = new List<UpdateAvailable>();
        var assetsReply = await _assets.ListAssetsAsync(new Google.Protobuf.WellKnownTypes.Empty(), cancellationToken: ct);

        foreach (var asset in assetsReply.Assets)
        {
            if (string.IsNullOrEmpty(asset.FirmletName))
                continue;

            var update = await CheckOneAsync(asset.Id, asset.FirmletName, asset.FirmletVersion, ct);
            if (update is not null)
                updates.Add(update);
        }

        return updates;
    }

    public async Task<UpdateAvailable?> CheckOneAsync(
        string assetId, string firmletName, string currentVersion, CancellationToken ct = default)
    {
        try
        {
            var info = await _registry.GetAsync($"/firmlets/{Uri.EscapeDataString(firmletName)}", ct);

            var latestVersion = info.TryGetProperty("latestVersion", out var lv) ? lv.GetString()
                : info.TryGetProperty("version", out var v) ? v.GetString()
                : null;

            if (latestVersion is null || string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                return null;

            _logger.LogInformation("Update available for {AssetId}: {Name} {Current} -> {Latest}",
                assetId, firmletName, currentVersion, latestVersion);

            return new UpdateAvailable(assetId, firmletName, currentVersion, latestVersion);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Version check failed for {FirmletName}", firmletName);
            return null;
        }
    }

    public sealed record UpdateAvailable(string AssetId, string FirmletName, string CurrentVersion, string LatestVersion);
}