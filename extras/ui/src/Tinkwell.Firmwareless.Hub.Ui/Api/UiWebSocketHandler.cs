using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hub.Ui.Bridge;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;
using Tinkwell.Firmwareless.Hub.Ui.Expressions;

namespace Tinkwell.Firmwareless.Hub.Ui.Api;

/// <summary>
/// Manages WebSocket connections from UI clients. Pushes property updates
/// (diffs) to all connected clients and receives user intents (set, action).
/// </summary>
public sealed class UiWebSocketHandler
{
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
    private readonly StoreBridge _store;
    private readonly CommandBridge _commands;
    private readonly UiDocument _document;
    private readonly ILogger<UiWebSocketHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public UiWebSocketHandler(
        StoreBridge store,
        CommandBridge commands,
        UiDocument document,
        ILogger<UiWebSocketHandler> logger)
    {
        _store = store;
        _commands = commands;
        _document = document;
        _logger = logger;
    }

    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString("N");
        _clients.TryAdd(clientId, socket);
        _logger.LogDebug("WebSocket client {ClientId} connected", clientId);

        try
        {
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleClientMessageAsync(json, cancellationToken);
                }
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            _logger.LogDebug("WebSocket client {ClientId} disconnected", clientId);

            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Broadcasts property updates to all connected WebSocket clients.
    /// Called by the bridge event handlers.
    /// </summary>
    public async Task BroadcastUpdatesAsync(IReadOnlyList<PropertyUpdate> updates)
    {
        if (updates.Count == 0 || _clients.IsEmpty)
            return;

        foreach (var update in updates)
        {
            var message = JsonSerializer.Serialize(new
            {
                type = "update",
                controlId = update.ControlId,
                property = update.Property,
                value = update.Value,
            }, JsonOptions);

            var bytes = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(bytes);

            var deadClients = new List<string>();

            foreach (var (id, ws) in _clients)
            {
                if (ws.State != WebSocketState.Open)
                {
                    deadClients.Add(id);
                    continue;
                }

                try
                {
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    deadClients.Add(id);
                }
            }

            foreach (var id in deadClients)
                _clients.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Sends a full tree refresh to all connected clients.
    /// </summary>
    public async Task BroadcastTreeAsync()
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "tree",
            tree = UiTreeSerializer.BuildTree(_document),
        }, JsonOptions);

        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var (_, ws) in _clients)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleClientMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var msgType = root.GetProperty("type").GetString();

            switch (msgType)
            {
                case "set":
                    await HandleSetAsync(root, cancellationToken);
                    break;

                case "action":
                    await HandleActionAsync(root, cancellationToken);
                    break;

                default:
                    _logger.LogDebug("Unknown WS message type: {Type}", msgType);
                    break;
            }
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle WebSocket message");
        }
    }

    private async Task HandleSetAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var controlId = root.GetProperty("controlId").GetString()!;
        var setting = root.GetProperty("setting").GetString()!;
        var value = root.GetProperty("value");

        var assetId = ExtractAssetId(controlId);
        if (assetId is null)
        {
            _logger.LogWarning("Cannot determine asset for control {ControlId}", controlId);
            return;
        }

        var stringValue = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.GetString() ?? "",
        };

        await _store.SetSettingAsync(assetId, setting, stringValue, cancellationToken);
    }

    private async Task HandleActionAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var controlId = root.GetProperty("controlId").GetString()!;

        var assetId = ExtractAssetId(controlId);
        if (assetId is null)
            return;

        if (root.TryGetProperty("command", out var cmdProp))
        {
            var command = cmdProp.GetString()!;
            await _commands.EnqueueCommandAsync(assetId, command, cancellationToken: cancellationToken);
        }
    }

    private static string? ExtractAssetId(string controlId)
    {
        var slashIndex = controlId.IndexOf('/');
        return slashIndex > 0 ? controlId[..slashIndex] : null;
    }
}