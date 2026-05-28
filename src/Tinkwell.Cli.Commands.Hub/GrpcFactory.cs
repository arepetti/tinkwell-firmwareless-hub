using Grpc.Net.Client;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;

namespace Tinkwell.Cli.Commands.Hub;

internal sealed class GrpcFactory : IDisposable
{
    private readonly GrpcChannel _proxyChannel;
    private readonly GrpcChannel _assetsChannel;

    public FirmwarelessProxy.FirmwarelessProxyClient Proxy { get; }
    public AssetRegistryService.AssetRegistryServiceClient Assets { get; }

    private GrpcFactory(GrpcChannel proxyChannel, GrpcChannel assetsChannel)
    {
        _proxyChannel = proxyChannel;
        _assetsChannel = assetsChannel;
        Proxy = new FirmwarelessProxy.FirmwarelessProxyClient(proxyChannel);
        Assets = new AssetRegistryService.AssetRegistryServiceClient(assetsChannel);
    }

    public static GrpcFactory FromSettings(HubSettings settings)
    {
        var proxyChannel = GrpcChannel.ForAddress(settings.ResolveProxyUrl());
        var assetsChannel = GrpcChannel.ForAddress(settings.ResolveAssetRegistryUrl());
        return new GrpcFactory(proxyChannel, assetsChannel);
    }

    public void Dispose()
    {
        _proxyChannel.Dispose();
        _assetsChannel.Dispose();
    }
}
