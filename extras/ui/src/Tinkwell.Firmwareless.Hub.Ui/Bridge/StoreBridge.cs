using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;
using Tinkwell.Firmwareless.Hub.Ui.Expressions;
using Tinkwell.Runlet.Store.Grpc;

namespace Tinkwell.Firmwareless.Hub.Ui.Bridge;

/// <summary>
/// Connects to the StateStore gRPC service for reading, writing, and
/// watching asset settings. Feeds setting changes into the expression engine.
/// </summary>
public sealed class StoreBridge : IAsyncDisposable
{
    private const string SettingsNamespace = "settings";

    private readonly GrpcChannel _channel;
    private readonly StateStore.StateStoreClient _client;
    private readonly UiExpressionEngine _engine;
    private readonly ILogger<StoreBridge> _logger;
    private readonly List<CancellationTokenSource> _watchCts = [];
    private readonly List<Task> _watchTasks = [];

    public StoreBridge(
        IOptions<BridgeOptions> options,
        UiExpressionEngine engine,
        ILogger<StoreBridge> logger)
    {
        _channel = GrpcChannel.ForAddress(options.Value.StateStoreAddress);
        _client = new StateStore.StateStoreClient(_channel);
        _engine = engine;
        _logger = logger;
    }

    public event Func<IReadOnlyList<PropertyUpdate>, Task>? OnUpdates;

    public async Task<string?> GetSettingAsync(string assetId, string key,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAsync(new GetRequest
        {
            BucketId = assetId,
            KeyNamespace = SettingsNamespace,
            Key = key,
        }, cancellationToken: cancellationToken);

        return response.Value;
    }

    public async Task SetSettingAsync(string assetId, string key, string value,
        CancellationToken cancellationToken = default)
    {
        await _client.SetAsync(new SetRequest
        {
            BucketId = assetId,
            KeyNamespace = SettingsNamespace,
            Key = key,
            Value = value,
        }, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Loads all current settings for an asset from the store and feeds them
    /// into the expression engine context.
    /// </summary>
    public async Task LoadSettingsAsync(string assetId, IReadOnlyDictionary<string, SettingDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        foreach (var (key, def) in definitions)
        {
            try
            {
                var response = await _client.GetAsync(new GetRequest
                {
                    BucketId = assetId,
                    KeyNamespace = SettingsNamespace,
                    Key = key,
                }, cancellationToken: cancellationToken);

                var value = ParseSettingValue(response.Value, def);
                _engine.GetOrCreateContext(assetId).SetValue(key, value);
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Setting {Key} not found for asset {AssetId}, using default", key, assetId);
                if (def.Default is not null)
                    _engine.GetOrCreateContext(assetId).SetValue(key, def.Default);
            }
        }
    }

    /// <summary>
    /// Starts watching settings changes for the given asset.
    /// </summary>
    public void StartWatching(string assetId)
    {
        var cts = new CancellationTokenSource();
        _watchCts.Add(cts);
        _watchTasks.Add(WatchLoopAsync(assetId, cts.Token));
    }

    private async Task WatchLoopAsync(string assetId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var call = _client.Watch(new WatchRequest
                {
                    BucketId = assetId,
                    KeyNamespace = SettingsNamespace,
                }, cancellationToken: cancellationToken);

                await foreach (var evt in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (evt.EventType == EventType.Set)
                    {
                        var updates = await _engine.OnValueChangedAsync(
                            assetId, evt.Key, evt.Value, cancellationToken);

                        if (updates.Count > 0 && OnUpdates is not null)
                            await OnUpdates(updates);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watch stream for {AssetId} disconnected, reconnecting in 5s", assetId);
                try { await Task.Delay(5000, cancellationToken); }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static object? ParseSettingValue(string? raw, SettingDefinition def)
    {
        if (string.IsNullOrEmpty(raw))
            return def.Default;

        return def.Type switch
        {
            SettingType.Number when double.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) => d,
            SettingType.Bool when bool.TryParse(raw, out var b) => b,
            _ => raw
        };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var cts in _watchCts)
            await cts.CancelAsync();

        foreach (var task in _watchTasks)
        {
            try { await task; }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var cts in _watchCts)
            cts.Dispose();

        _channel.Dispose();
    }
}