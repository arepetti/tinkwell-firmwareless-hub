using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tinkwell.Runlet.Firmwareless.Provisioning.Configuration;

namespace Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;

/// <summary>
/// Background worker that periodically retries pending firmlet downloads.
/// </summary>
public sealed class PendingDownloadWorker : BackgroundService
{
    private readonly PendingDownloadStore _store;
    private readonly FirmletInstaller _installer;
    private readonly ProvisioningOptions _options;
    private readonly ILogger<PendingDownloadWorker> _logger;

    public PendingDownloadWorker(
        PendingDownloadStore store,
        FirmletInstaller installer,
        IOptions<ProvisioningOptions> options,
        ILogger<PendingDownloadWorker> logger)
    {
        _store = store;
        _installer = installer;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.PendingRetryMinutes);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RetryPendingAsync(stoppingToken);
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Pending download retry cycle failed");
            }
        }
    }

    private async Task RetryPendingAsync(CancellationToken ct)
    {
        var pending = await _store.ListAsync(ct);
        if (pending.Count == 0)
            return;

        var timeout = TimeSpan.FromHours(_options.PendingTimeoutHours);

        _logger.LogInformation("Retrying {Count} pending download(s)", pending.Count);

        foreach (var entry in pending)
        {
            if (DateTime.UtcNow - entry.CreatedUtc > timeout)
            {
                _logger.LogWarning(
                    "Pending download for {AssetId} ({FirmletName}) expired after {Hours}h -- removing",
                    entry.AssetId, entry.FirmletName, _options.PendingTimeoutHours);
                await _store.RemoveAsync(entry.AssetId, ct);
                continue;
            }

            try
            {
                var assetId = Guid.Parse(entry.AssetId);
                var result = await _installer.InstallAsync(assetId, entry.FirmletName, entry.FirmletVersion, ct);

                if (result is not null)
                {
                    _logger.LogInformation("Pending download completed for asset {AssetId}", entry.AssetId);
                    await _store.RemoveAsync(entry.AssetId, ct);
                }
                else
                {
                    await _store.UpdateAsync(entry.AssetId, e =>
                    {
                        e.AttemptCount++;
                        e.LastAttemptUtc = DateTime.UtcNow;
                        e.LastError = "Install returned null (download or verification failed)";
                    }, ct);
                }
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Retry failed for {AssetId}", entry.AssetId);
                await _store.UpdateAsync(entry.AssetId, e =>
                {
                    e.AttemptCount++;
                    e.LastAttemptUtc = DateTime.UtcNow;
                    e.LastError = ex.Message;
                }, ct);
            }
        }
    }
}