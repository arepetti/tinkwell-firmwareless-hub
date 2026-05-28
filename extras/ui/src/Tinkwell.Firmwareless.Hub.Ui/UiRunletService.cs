using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hub.Ui.Api;
using Tinkwell.Firmwareless.Hub.Ui.Bridge;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;
using Tinkwell.Firmwareless.Hub.Ui.Expressions;

namespace Tinkwell.Firmwareless.Hub.Ui;

/// <summary>
/// Background service that orchestrates startup: discovers assets,
/// parses configs, initializes the expression engine, starts bridge
/// streams, and wires up WebSocket broadcasting.
/// </summary>
public sealed class UiRunletService : BackgroundService
{
    private readonly UiConfigDiscovery _discovery;
    private readonly UiExpressionEngine _engine;
    private readonly MeasureBridge _measureBridge;
    private readonly StoreBridge _storeBridge;
    private readonly UiWebSocketHandler _wsHandler;
    private readonly UiDocument _document;
    private readonly ILogger<UiRunletService> _logger;

    public UiRunletService(
        UiConfigDiscovery discovery,
        UiExpressionEngine engine,
        MeasureBridge measureBridge,
        StoreBridge storeBridge,
        UiWebSocketHandler wsHandler,
        UiDocument document,
        ILogger<UiRunletService> logger)
    {
        _discovery = discovery;
        _engine = engine;
        _measureBridge = measureBridge;
        _storeBridge = storeBridge;
        _wsHandler = wsHandler;
        _document = document;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Hub UI starting, discovering assets...");

        var discovered = await _discovery.DiscoverAndLoadAsync(stoppingToken);
        _document.Merge(discovered);

        _engine.RegisterDocument(_document);

        var assetIds = _document.Groups
            .Select(g => g.AssetId)
            .Concat(_document.Widgets.Select(w => w.AssetId))
            .Where(id => id is not null)
            .Distinct()
            .Cast<string>()
            .ToList();

        foreach (var assetId in assetIds)
        {
            await _storeBridge.LoadSettingsAsync(assetId, _document.Settings, stoppingToken);
            _storeBridge.StartWatching(assetId);
        }

        await _engine.EvaluateAllAsync(stoppingToken);

        _measureBridge.OnUpdates += updates => _wsHandler.BroadcastUpdatesAsync(updates);
        _storeBridge.OnUpdates += updates => _wsHandler.BroadcastUpdatesAsync(updates);
        _measureBridge.StartStreaming();

        _logger.LogInformation(
            "Hub UI ready: {Groups} groups, {Widgets} widgets, {Assets} assets",
            _document.Groups.Count, _document.Widgets.Count, assetIds.Count);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
