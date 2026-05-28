using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tinkwell.Firmwareless.Hub.Ui.Expressions;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;

namespace Tinkwell.Firmwareless.Hub.Ui.Bridge;

/// <summary>
/// Connects to the FirmwarelessProxy gRPC service to stream health snapshots
/// and feed measure values into the expression engine.
/// </summary>
public sealed class MeasureBridge : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly FirmwarelessProxy.FirmwarelessProxyClient _client;
    private readonly UiExpressionEngine _engine;
    private readonly ILogger<MeasureBridge> _logger;
    private CancellationTokenSource? _cts;
    private Task? _streamTask;

    public MeasureBridge(
        IOptions<BridgeOptions> options,
        UiExpressionEngine engine,
        ILogger<MeasureBridge> logger)
    {
        _channel = GrpcChannel.ForAddress(options.Value.ProxyAddress);
        _client = new FirmwarelessProxy.FirmwarelessProxyClient(_channel);
        _engine = engine;
        _logger = logger;
    }

    /// <summary>
    /// Fired when expression evaluation produces property updates.
    /// The API/WebSocket layer subscribes to this to push diffs to clients.
    /// </summary>
    public event Func<IReadOnlyList<PropertyUpdate>, Task>? OnUpdates;

    public void StartStreaming()
    {
        _cts = new CancellationTokenSource();
        _streamTask = StreamHealthLoopAsync(_cts.Token);
    }

    private async Task StreamHealthLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var call = _client.StreamHealth(new StreamHealthRequest(), cancellationToken: cancellationToken);
                await foreach (var snapshot in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    var allUpdates = new List<PropertyUpdate>();
                    foreach (var host in snapshot.Hosts)
                    {
                        var updates = await _engine.OnValueChangedAsync(
                            host.AssetId, "health-status", host.Status, cancellationToken);
                        allUpdates.AddRange(updates);

                        updates = await _engine.OnValueChangedAsync(
                            host.AssetId, "cpu-percent", host.CpuPercent, cancellationToken);
                        allUpdates.AddRange(updates);

                        updates = await _engine.OnValueChangedAsync(
                            host.AssetId, "working-set-bytes", (long)host.WorkingSetBytes, cancellationToken);
                        allUpdates.AddRange(updates);
                    }

                    if (allUpdates.Count > 0 && OnUpdates is not null)
                        await OnUpdates(allUpdates);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health stream disconnected, reconnecting in 5s");
                try { await Task.Delay(5000, cancellationToken); }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_streamTask is not null)
            {
                try { await _streamTask; }
                catch (OperationCanceledException)
                {
                }
            }
            _cts.Dispose();
        }
        _channel.Dispose();
    }
}