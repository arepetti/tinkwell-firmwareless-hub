using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tinkwell.Coap;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Provisioning.Configuration;
using Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;
using Tw;
using ProxyClient = Tinkwell.Runlet.Firmwareless.Proxy.Proto.FirmwarelessProxy.FirmwarelessProxyClient;
using AssetClient = Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto.AssetRegistryService.AssetRegistryServiceClient;

namespace Tinkwell.Runlet.Firmwareless.Provisioning;

/// <summary>
/// Orchestrates device onboarding: CoAP provisioning, firmlet resolution/install,
/// asset registration, and host lifecycle via the proxy runlet.
/// </summary>
public sealed class ProvisioningService
{
    private const int DefaultDeviceCoapPort = 5683;
    private const string DefaultCommunicationMode = "always-on";

    private readonly FirmletResolver _resolver;
    private readonly FirmletInstaller _installer;
    private readonly PendingDownloadStore _pendingStore;
    private readonly ProxyClient _proxy;
    private readonly AssetClient _assets;
    private readonly ProvisioningOptions _options;
    private readonly ILogger<ProvisioningService> _logger;

    public ProvisioningService(
        FirmletResolver resolver,
        FirmletInstaller installer,
        PendingDownloadStore pendingStore,
        ProxyClient proxy,
        AssetClient assets,
        IOptions<ProvisioningOptions> options,
        ILogger<ProvisioningService> logger)
    {
        _resolver = resolver;
        _installer = installer;
        _pendingStore = pendingStore;
        _proxy = proxy;
        _assets = assets;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Full flow: read device identity via CoAP, assign a hub GUID, resolve/install firmlet,
    /// register the asset, and start the WASM host.
    /// </summary>
    public async Task<Guid> ProvisionDeviceAsync(string deviceAddress, CancellationToken ct = default)
    {
        var (host, port) = ParseDeviceEndpoint(deviceAddress, DefaultDeviceCoapPort);

        _logger.LogInformation("Provisioning device at {Host}:{Port}", host, port);

        var infoResp = await CoapClient.SendAsync(
            host, port,
            "/tw/provision/info",
            query: null,
            new CoapClientRequest([])
            {
                Method = CoapMethod.Get,
                Accept = CoapContentFormat.ApplicationOctetStream,
            },
            new CoapClientRequestOptions { Timeout = TimeSpan.FromSeconds(15) },
            ct);

        if (!IsCoapSuccess(infoResp.Code))
            throw new InvalidOperationException(
                $"GET /tw/provision/info failed: {CoapCode.ToDisplayString(infoResp.Code)}");

        var provisionInfo = ProvisionInfo.Parser.ParseFrom(infoResp.Payload);
        if (provisionInfo.Device is null)
            throw new InvalidOperationException("Device returned ProvisionInfo without Device payload.");

        var deviceInfo = provisionInfo.Device;
        var assetId = Guid.NewGuid();

        var hubCmd = new HubProvisionCmd { Id = ByteString.CopyFrom(assetId.ToByteArray()) };
        var hubReq = new CoapClientRequest(
            hubCmd.ToByteArray(),
            CoapContentFormat.ApplicationOctetStream)
        {
            Method = CoapMethod.Post,
            Accept = CoapContentFormat.ApplicationOctetStream,
        };
        var hubOptions = new CoapClientRequestOptions
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        var hubResp = await CoapClient.SendAsync(
            host, port,
            "/tw/provision/hub",
            query: null,
            hubReq,
            hubOptions,
            ct);

        if (!IsCoapSuccess(hubResp.Code))
            throw new InvalidOperationException(
                $"POST /tw/provision/hub failed: {CoapCode.ToDisplayString(hubResp.Code)}");

        if (hubResp.Payload.Length > 0)
        {
            var reply = ProvisionReply.Parser.ParseFrom(hubResp.Payload);
            if (!reply.Success)
                throw new InvalidOperationException($"Hub provision rejected: {reply.Error}");
        }

        var variantBytes = deviceInfo.Variant.IsEmpty ? null : deviceInfo.Variant.ToByteArray();
        var fw = string.IsNullOrEmpty(deviceInfo.FwVersion) ? null : deviceInfo.FwVersion;

        var resolved = await _resolver.ResolveAsync(
            deviceInfo.VendorId,
            deviceInfo.ProductId,
            variantBytes,
            fw,
            ct);

        if (resolved is null)
            throw new InvalidOperationException("No firmlet could be resolved for this device.");

        var firmletPath = await _installer.InstallAsync(assetId, resolved.Name, resolved.Version, ct);
        if (firmletPath is null)
        {
            await _pendingStore.AddAsync(new PendingDownload
            {
                AssetId = assetId.ToString(),
                FirmletName = resolved.Name,
                FirmletVersion = resolved.Version,
                LastError = "Initial install failed; queued for retry",
            }, ct);
            _logger.LogWarning("Firmlet download failed for {AssetId}; added to pending queue", assetId);
            throw new InvalidOperationException("Firmlet download or verification failed. Queued for retry.");
        }

        try
        {
            await RegisterAssetCoreAsync(
                assetId,
                displayName: string.IsNullOrEmpty(deviceInfo.DeviceName)
                    ? assetId.ToString()
                    : deviceInfo.DeviceName,
                deviceInfo.VendorId,
                deviceInfo.ProductId,
                variantBytes,
                fw,
                resolved.Name,
                resolved.Version,
                firmletPath,
                ct);

            await StartHostCoreAsync(assetId, firmletPath, ct);
        }
        catch
        {
            _installer.Uninstall(assetId);
            throw;
        }

        return assetId;
    }

    /// <summary>
    /// Manually add an asset: install the named firmlet, register it, and start the host.
    /// </summary>
    public async Task AddAssetAsync(
        Guid id,
        int vendor,
        int product,
        string firmletName,
        string firmletVersion,
        CancellationToken ct = default)
    {
        var firmletPath = await _installer.InstallAsync(id, firmletName, firmletVersion, ct);
        if (firmletPath is null)
            throw new InvalidOperationException("Firmlet download or verification failed.");

        try
        {
            await RegisterAssetCoreAsync(
                id,
                displayName: id.ToString(),
                vendor,
                product,
                variant: null,
                firmwareVersion: null,
                firmletName,
                firmletVersion,
                firmletPath,
                ct);

            await StartHostCoreAsync(id, firmletPath, ct);
        }
        catch
        {
            _installer.Uninstall(id);
            throw;
        }
    }

    /// <summary>
    /// Stops the host, removes the asset from the registry, and deletes installed firmlet files.
    /// </summary>
    public async Task RemoveAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        var idStr = assetId.ToString();

        await _pendingStore.RemoveAsync(idStr, ct);

        var stopReply = await _proxy.StopHostAsync(
            new StopHostRequest { AssetId = idStr, Reason = "remove" },
            cancellationToken: ct).ResponseAsync;
        if (!stopReply.Success)
            _logger.LogWarning("StopHost for {AssetId} reported error: {Error}", assetId, stopReply.Error);

        var removeReply = await _assets.RemoveAssetAsync(
            new RemoveAssetRequest { Id = idStr },
            cancellationToken: ct).ResponseAsync;
        if (!removeReply.Success)
            throw new InvalidOperationException($"RemoveAsset failed for {assetId}.");

        _installer.Uninstall(assetId);
    }

    private async Task RegisterAssetCoreAsync(
        Guid assetId,
        string displayName,
        int vendorId,
        int productId,
        byte[]? variant,
        string? firmwareVersion,
        string firmletName,
        string firmletVersion,
        string firmletPath,
        CancellationToken ct)
    {
        var req = new RegisterAssetRequest
        {
            Id = assetId.ToString(),
            DisplayName = displayName,
            VendorId = vendorId,
            ProductId = productId,
            FirmwareVersion = firmwareVersion ?? "",
            CommunicationMode = DefaultCommunicationMode,
            FirmletName = firmletName,
            FirmletVersion = firmletVersion,
            FirmletPath = firmletPath,
        };

        if (variant is not null && variant.Length > 0)
            req.Variant = ByteString.CopyFrom(variant);

        var reply = await _assets.RegisterAssetAsync(req, cancellationToken: ct).ResponseAsync;
        if (!reply.Success)
            throw new InvalidOperationException($"RegisterAsset failed: {reply.Error}");
    }

    private async Task StartHostCoreAsync(Guid assetId, string firmletPath, CancellationToken ct)
    {
        var reply = await _proxy.StartHostAsync(
            new StartHostRequest
            {
                AssetId = assetId.ToString(),
                FirmletPath = firmletPath,
            },
            cancellationToken: ct).ResponseAsync;
        if (!reply.Success)
            throw new InvalidOperationException($"StartHost failed: {reply.Error}");
    }

    private static bool IsCoapSuccess(byte code) => (code >> 5) == 2;

    private static (string Host, int Port) ParseDeviceEndpoint(string deviceAddress, int defaultPort)
    {
        var s = deviceAddress.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "coap://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Invalid device address; expected host, host:port, or coap://host:port.", nameof(deviceAddress));

        var port = uri.Port > 0 ? uri.Port : defaultPort;
        return (uri.Host, port);
    }
}
