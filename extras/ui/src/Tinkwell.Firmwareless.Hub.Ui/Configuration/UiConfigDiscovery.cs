using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Tinkwell.Firmwareless.Hub.Ui.Bridge;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;

namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// Discovers assets via the AssetRegistry gRPC service and loads their
/// <c>ui.tw</c> and <c>settings.tw</c> configuration files.
/// </summary>
/// <remarks>
/// In the current PoC the AssetRegistry does not yet have
/// <c>GetAssetConfigFiles</c>. This discovery service uses a simulated
/// in-memory file provider for development. When the gRPC extension is
/// implemented, replace the <see cref="LoadAssetConfigAsync"/> body.
/// </remarks>
public sealed class UiConfigDiscovery
{
    private readonly GrpcChannel _channel;
    private readonly AssetRegistryService.AssetRegistryServiceClient _client;
    private readonly ILogger<UiConfigDiscovery> _logger;

    public UiConfigDiscovery(
        IOptions<BridgeOptions> options,
        ILogger<UiConfigDiscovery> logger)
    {
        _channel = GrpcChannel.ForAddress(options.Value.AssetRegistryAddress);
        _client = new AssetRegistryService.AssetRegistryServiceClient(_channel);
        _logger = logger;
    }

    /// <summary>
    /// Discovers all assets and parses their UI and settings configurations,
    /// merging everything into a single <see cref="UiDocument"/>.
    /// </summary>
    public async Task<UiDocument> DiscoverAndLoadAsync(CancellationToken cancellationToken = default)
    {
        var merged = new UiDocument();

        try
        {
            var reply = await _client.ListAssetsAsync(new Empty(), cancellationToken: cancellationToken);

            foreach (var asset in reply.Assets)
            {
                try
                {
                    var assetDoc = await LoadAssetConfigAsync(asset.Id, cancellationToken);
                    if (assetDoc is not null)
                        merged.Merge(assetDoc);
                }
                catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load UI config for asset {AssetId}", asset.Id);
                }
            }
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list assets from registry, starting with empty UI");
        }

        return merged;
    }

    private async Task<UiDocument?> LoadAssetConfigAsync(string assetId, CancellationToken cancellationToken)
    {
        // Placeholder: in the full implementation, call GetAssetConfigFiles
        // to fetch file content over gRPC and build an in-memory IFileProvider.
        // For now, return null (no asset-contributed UI).
        _logger.LogDebug("Config discovery for asset {AssetId} (placeholder -- gRPC extension pending)", assetId);
        await Task.CompletedTask;
        return null;
    }

    /// <summary>
    /// Loads a <see cref="UiDocument"/> from an in-memory file map.
    /// Used when config file content is available (e.g., from gRPC or tests).
    /// </summary>
    public static async Task<UiDocument> LoadFromFilesAsync(
        string assetId,
        IReadOnlyDictionary<string, string> files,
        CancellationToken cancellationToken = default)
    {
        var fileProvider = new InMemoryFileProvider(files);
        var doc = new UiDocument();

        if (files.ContainsKey("ui.tw"))
        {
            var parser = new UiConfigParser(assetId);
            var uiDoc = await parser.LoadAsync(fileProvider, "ui.tw", cancellationToken: cancellationToken);
            doc.Merge(uiDoc);
        }

        if (files.ContainsKey("settings.tw"))
        {
            var settingsParser = new SettingsParser();
            var settings = await settingsParser.LoadAsync(fileProvider, "settings.tw", cancellationToken: cancellationToken);
            foreach (var (key, def) in settings)
                doc.Settings.TryAdd(key, def);
        }

        return doc;
    }
}

/// <summary>
/// A minimal <see cref="IFileProvider"/> backed by an in-memory dictionary,
/// used to feed the standard parser pipeline when config files arrive over gRPC.
/// </summary>
public sealed class InMemoryFileProvider : IFileProvider
{
    private readonly IReadOnlyDictionary<string, string> _files;

    public InMemoryFileProvider(IReadOnlyDictionary<string, string> files)
    {
        _files = files;
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var normalized = subpath.TrimStart('/', '\\');
        if (_files.TryGetValue(normalized, out var content))
            return new InMemoryFileInfo(normalized, content);
        return new NotFoundFileInfo(normalized);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) =>
        NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) =>
        NullChangeToken.Singleton;
}

public sealed class InMemoryFileInfo : IFileInfo
{
    private readonly string _content;

    public InMemoryFileInfo(string name, string content)
    {
        Name = name;
        _content = content;
        Length = System.Text.Encoding.UTF8.GetByteCount(content);
    }

    public bool Exists => true;
    public long Length { get; }
    public string? PhysicalPath => null;
    public string Name { get; }
    public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
    public bool IsDirectory => false;

    public Stream CreateReadStream() =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_content));
}