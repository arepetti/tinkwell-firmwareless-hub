using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Microsoft.Extensions.DependencyInjection;
using Tinkwell.Runlet.Firmwareless.Proxy.Configuration;
using Tinkwell.Runlet.Store.Grpc;

namespace Tinkwell.Runlet.Firmwareless.Proxy;

/// <summary>
/// Writes HealthSnapshot data to the Tinkwell state store's _health bucket
/// using the same JSON schema as the platform's StoreHealthReportWriter, so
/// <c>tw runners health</c> can display firmwareless host metrics alongside
/// standard runner health.
/// </summary>
public sealed class HealthStoreWriter
{
    private const string HealthBucketId = "_health";

    private readonly StateStore.StateStoreClient? _store;
    private readonly ProxyOptions _options;
    private readonly ILogger<HealthStoreWriter> _logger;
    private bool _bucketConfigured;

    public HealthStoreWriter(ProxyOptions options, ILogger<HealthStoreWriter> logger, IServiceProvider sp)
    {
        _store = sp.GetService(typeof(StateStore.StateStoreClient)) as StateStore.StateStoreClient;
        _options = options;
        _logger = logger;
    }

    public async Task WriteAsync(HealthSnapshot snapshot, CancellationToken ct = default)
    {
        if (_store is null)
            return;

        await EnsureBucketAsync(ct);

        foreach (var host in snapshot.Hosts)
        {
            var key = $"firmwareless/{host.AssetId}";
            var json = SerializeAsHealthReport(host, snapshot.TimestampMs);
            await WriteEntryAsync(key, json, ct);
        }

        if (snapshot.Router is not null)
        {
            var json = SerializeAsHealthReport(snapshot.Router, snapshot.TimestampMs);
            await WriteEntryAsync("firmwareless/__router__", json, ct);
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketConfigured)
            return;
        try
        {
            await _store!.ConfigureBucketAsync(new ConfigureBucketRequest
            {
                BucketId = HealthBucketId,
                Discoverable = false,
            }, cancellationToken: ct);
            _bucketConfigured = true;
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to configure health bucket");
        }
    }

    private async Task WriteEntryAsync(string key, string json, CancellationToken ct)
    {
        try
        {
            await _store!.SetAsync(new SetRequest
            {
                BucketId = HealthBucketId,
                Key = key,
                Value = json,
                TtlSeconds = _options.HealthTtlSeconds,
            }, cancellationToken: ct);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write health for {Key}", key);
        }
    }

    /// <summary>
    /// Serializes a <see cref="HostHealthReport"/> into the same JSON shape
    /// that <c>tw runners health</c> expects: root <c>status</c>, nested
    /// <c>process</c> object, and ISO timestamp.
    /// </summary>
    private static string SerializeAsHealthReport(HostHealthReport report, ulong timestampMs)
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs).UtcDateTime;

        var obj = new
        {
            status = report.Status,
            process = new
            {
                cpuPercent = report.CpuPercent,
                workingSetBytes = (long)report.WorkingSetBytes,
                threadCount = report.ThreadCount,
                handleCount = 0,
            },
            checks = new Dictionary<string, object>
            {
                ["firmlet"] = new
                {
                    status = report.FirmletState == FirmletState.Running ? "healthy" : "degraded",
                    data = new
                    {
                        firmletName = report.FirmletName,
                        firmletState = report.FirmletState.ToString(),
                        uptimeMs = report.UptimeMs,
                        restartCount = report.RestartCount,
                    },
                },
            },
            timestamp,
        };

        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}