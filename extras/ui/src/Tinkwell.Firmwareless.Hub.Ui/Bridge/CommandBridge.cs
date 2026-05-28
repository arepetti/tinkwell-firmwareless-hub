using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;

namespace Tinkwell.Firmwareless.Hub.Ui.Bridge;

/// <summary>
/// Sends commands to firmlets via the AssetRegistry's EnqueueCommand RPC.
/// Used when a button control has a <c>command</c> property.
/// </summary>
public sealed class CommandBridge : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AssetRegistryService.AssetRegistryServiceClient _client;
    private readonly ILogger<CommandBridge> _logger;

    public CommandBridge(
        IOptions<BridgeOptions> options,
        ILogger<CommandBridge> logger)
    {
        _channel = GrpcChannel.ForAddress(options.Value.AssetRegistryAddress);
        _client = new AssetRegistryService.AssetRegistryServiceClient(_channel);
        _logger = logger;
    }

    public async Task<bool> EnqueueCommandAsync(
        string assetId, string commandType, byte[]? payload = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EnqueueCommandRequest
        {
            AssetId = assetId,
            CmdType = commandType,
        };

        if (payload is not null)
            request.Payload = ByteString.CopyFrom(payload);

        var reply = await _client.EnqueueCommandAsync(request, cancellationToken: cancellationToken);

        if (!reply.Success)
            _logger.LogWarning("EnqueueCommand failed for {AssetId}/{CommandType}", assetId, commandType);

        return reply.Success;
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }
}
