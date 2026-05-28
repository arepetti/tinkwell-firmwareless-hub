using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmlets.Registry.Client;
using Tinkwell.Package;

namespace Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;

/// <summary>
/// Downloads compiled firmlet packages from the registry, verifies them,
/// and installs under the firmlet base directory keyed by asset GUID.
/// </summary>
public sealed class FirmletInstaller
{
    private readonly FirmletRegistryApiClient _registry;
    private readonly string _firmletBaseDir;
    private readonly string _architecture;
    private readonly byte[]? _registryPublicKey;
    private readonly ILogger<FirmletInstaller> _logger;

    public FirmletInstaller(
        FirmletRegistryApiClient registry,
        string firmletBaseDir,
        string architecture,
        byte[]? registryPublicKey,
        ILogger<FirmletInstaller> logger)
    {
        _registry = registry;
        _firmletBaseDir = firmletBaseDir;
        _architecture = architecture;
        _registryPublicKey = registryPublicKey;
        _logger = logger;
    }

    public string GetInstallPath(Guid assetId) =>
        Path.Combine(_firmletBaseDir, assetId.ToString());

    public async Task<string?> InstallAsync(
        Guid assetId,
        string firmletName,
        string? firmletVersion,
        CancellationToken ct = default)
    {
        var installDir = GetInstallPath(assetId);

        try
        {
            var versionSuffix = firmletVersion is not null ? $"&version={Uri.EscapeDataString(firmletVersion)}" : "";
            var downloadPath =
                $"/firmlets/{Uri.EscapeDataString(firmletName)}/download?arch={Uri.EscapeDataString(_architecture)}{versionSuffix}";

            _logger.LogInformation("Downloading firmlet {Name}@{Version} for asset {AssetId}",
                firmletName, firmletVersion ?? "latest", assetId);

            await using var stream = await _registry.DownloadAsync(downloadPath, ct);

            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);

            Directory.CreateDirectory(installDir);

            var tempZip = Path.Combine(Path.GetTempPath(), $"firmlet-{assetId}.zip");
            try
            {
                await using (var file = File.Create(tempZip))
                    await stream.CopyToAsync(file, ct);

                ZipFile.ExtractToDirectory(tempZip, installDir);
            }
            finally
            {
                File.Delete(tempZip);
            }

            if (_registryPublicKey is not null)
            {
                _logger.LogDebug("Verifying firmlet package integrity for {AssetId}", assetId);
                var package = new TwPackage();
                var verification = await package.VerifyAsync(installDir, new VerifyOptions
                {
                    TrustedKeys = new[] { _registryPublicKey },
                    RequireSignatures = true,
                });

                if (!verification.IsValid)
                {
                    var issues = string.Join("; ", verification.Issues.Select(i => i.Message));
                    _logger.LogError("Firmlet verification failed for {AssetId}: {Issues}",
                        assetId, issues);
                    Directory.Delete(installDir, recursive: true);
                    return null;
                }
            }

            _logger.LogInformation("Firmlet installed at {Path}", installDir);
            return installDir;
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install firmlet for asset {AssetId}", assetId);
            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);
            return null;
        }
    }

    public void Uninstall(Guid assetId)
    {
        var installDir = GetInstallPath(assetId);
        if (Directory.Exists(installDir))
        {
            Directory.Delete(installDir, recursive: true);
            _logger.LogInformation("Uninstalled firmlet for asset {AssetId}", assetId);
        }
    }
}