using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmlets.Registry.Client;

namespace Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;

/// <summary>
/// Resolves the best firmlet for a device by querying the registry's
/// resolve endpoint with the device's identity.
/// </summary>
public sealed class FirmletResolver
{
    private readonly FirmletRegistryApiClient _registry;
    private readonly string _architecture;
    private readonly ILogger<FirmletResolver> _logger;

    public FirmletResolver(
        FirmletRegistryApiClient registry,
        string architecture,
        ILogger<FirmletResolver> logger)
    {
        _registry = registry;
        _architecture = architecture;
        _logger = logger;
    }

    public async Task<ResolvedFirmlet?> ResolveAsync(
        int vendorId,
        int productId,
        byte[]? variant,
        string? fwVersion,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Resolving firmlet for vendor={Vendor} product={Product} variant={Variant} fw={FwVersion}",
            vendorId, productId,
            variant is not null ? Convert.ToBase64String(variant) : "(none)",
            fwVersion ?? "(none)");

        try
        {
            var body = new
            {
                vendorId,
                productId,
                variant = variant is not null ? Convert.ToBase64String(variant) : null,
                fwVersion,
                arch = _architecture,
            };

            var result = await _registry.PostAsync("/firmlets/resolve", body, ct);

            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in result.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString();
                    var version = item.GetProperty("version").GetString();
                    var firmletId = item.GetProperty("firmletId").GetInt32();

                    if (name is not null && version is not null)
                    {
                        _logger.LogInformation("Resolved firmlet: {Name}@{Version} (id={Id})",
                            name, version, firmletId);
                        return new ResolvedFirmlet(firmletId, name, version);
                    }
                }
            }

            _logger.LogWarning("No compatible firmlet found");
            return null;
        }
        catch (FirmletRegistryException ex)
        {
            _logger.LogError(ex, "Registry resolve failed: {Status}", ex.StatusCode);
            return null;
        }
    }
}

public sealed record ResolvedFirmlet(int FirmletId, string Name, string Version);
