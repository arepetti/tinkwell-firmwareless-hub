using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;
using Tinkwell.Firmlets.Registry.Client;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class InstallSettings : HubSettings
{
    [Description("Firmlet ID from the registry")]
    [CommandArgument(0, "<firmlet-id>")]
    public string FirmletId { get; set; } = "";

    [Description("Target architecture (e.g. arm64-linux, x64-linux)")]
    [CommandOption("--arch")]
    [DefaultValue(null)]
    public string? Architecture { get; set; }

    [Description("Registry URL")]
    [CommandOption("--registry-url")]
    [DefaultValue(null)]
    public string? RegistryUrl { get; set; }

    [Description("Hub API key for registry authentication")]
    [CommandOption("--api-key")]
    [DefaultValue(null)]
    public string? ApiKey { get; set; }

    [Description("Save the compiled artifact to this path instead of installing")]
    [CommandOption("-o|--output")]
    [DefaultValue(null)]
    public string? OutputPath { get; set; }

    [Description("Asset ID (GUID). Auto-generated if not provided")]
    [CommandOption("--asset-id")]
    [DefaultValue(null)]
    public string? AssetId { get; set; }

    [Description("Base directory for firmlet installation")]
    [CommandOption("--firmlet-base-dir")]
    [DefaultValue(null)]
    public string? FirmletBaseDir { get; set; }

    public string ResolveRegistryUrl() =>
        !string.IsNullOrWhiteSpace(RegistryUrl) ? RegistryUrl
        : Environment.GetEnvironmentVariable("TW_FIRMLET_REGISTRY_URL") ?? "http://localhost:5200";

    public string ResolveApiKey() =>
        !string.IsNullOrWhiteSpace(ApiKey) ? ApiKey
        : Environment.GetEnvironmentVariable("TW_FIRMLET_HUB_API_KEY") ?? "";

    public string ResolveArchitecture() =>
        !string.IsNullOrWhiteSpace(Architecture) ? Architecture
        : $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}-linux";

    public string ResolveFirmletBaseDir()
    {
        if (!string.IsNullOrWhiteSpace(FirmletBaseDir))
            return FirmletBaseDir;
        var env = Environment.GetEnvironmentVariable("TW_FIRMLET_BASE_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tinkwell", "Firmlets")
            : "/var/lib/tinkwell/firmlets";
    }
}

/// <summary>
/// Downloads a compiled firmlet from the registry, waits for compilation if
/// needed, and installs it locally (register asset + start host).
/// </summary>
[CliCommand("hub", "install", Description = "Download and install a compiled firmlet from the registry")]
public sealed class InstallCommand : AsyncCommand<InstallSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, InstallSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);
        var arch = settings.ResolveArchitecture();
        var registryUrl = settings.ResolveRegistryUrl();
        var apiKey = settings.ResolveApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteError("Hub API key is required (--api-key or TW_FIRMLET_HUB_API_KEY)");
            return 1;
        }

        using var registry = new RegistryClientFactory(registryUrl, apiKey);
        var downloadPath = $"firmlets/{Uri.EscapeDataString(settings.FirmletId)}/download?arch={Uri.EscapeDataString(arch)}";

        string tempZip;
        try
        {
            tempZip = await DownloadWithCompilationWaitAsync(registry, downloadPath, output, ct);
        }
        catch (FirmletRegistryException ex)
        {
            output.WriteError($"Download failed: {ex.Message} (HTTP {(int)ex.StatusCode})");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            output.WriteError($"Download failed: {ex.Message}");
            return 1;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(settings.OutputPath))
            {
                File.Move(tempZip, settings.OutputPath, overwrite: true);
                output.WriteSuccess($"Saved to [cyan]{settings.OutputPath}[/]");
                return 0;
            }

            var assetId = ResolveAssetId(settings);
            var installDir = Path.Combine(settings.ResolveFirmletBaseDir(), assetId.ToString());

            await output.RunWithStatusAsync("Extracting firmlet...", () =>
            {
                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, recursive: true);
                Directory.CreateDirectory(installDir);
                ZipFile.ExtractToDirectory(tempZip, installDir);
                return Task.CompletedTask;
            });

            using var grpc = GrpcFactory.FromSettings(settings);

            await output.RunWithStatusAsync("Registering asset...", async () =>
            {
                var reply = await grpc.Assets.RegisterAssetAsync(new RegisterAssetRequest
                {
                    Id = assetId.ToString(),
                    DisplayName = settings.FirmletId,
                    FirmletName = settings.FirmletId,
                    CommunicationMode = "always-on",
                }, cancellationToken: ct);

                if (!reply.Success)
                    throw new InvalidOperationException($"RegisterAsset failed: {reply.Error}");
            });

            await output.RunWithStatusAsync("Starting host...", async () =>
            {
                var reply = await grpc.Proxy.StartHostAsync(new StartHostRequest
                {
                    AssetId = assetId.ToString(),
                    FirmletPath = installDir,
                }, cancellationToken: ct);

                if (!reply.Success)
                    throw new InvalidOperationException($"StartHost failed: {reply.Error}");
            });

            output.WriteSuccess($"Firmlet installed and running as asset [cyan]{assetId}[/]");
            return 0;
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    /// <summary>
    /// Downloads the compiled firmlet, polling if the registry returns 202
    /// (compilation in progress). Uses the raw HttpClient for the polling
    /// loop (needs status code inspection) and the typed client for job
    /// status checks.
    /// </summary>
    private static async Task<string> DownloadWithCompilationWaitAsync(
        RegistryClientFactory registry, string downloadPath, OutputContext output, CancellationToken ct)
    {
        const int maxPolls = 120;
        const int pollIntervalSeconds = 5;

        var http = registry.GetHttpClient();
        var url = AppendVersion(downloadPath);
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        for (var i=0; i < maxPolls && response.StatusCode == HttpStatusCode.Accepted; ++i)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var pollUrl = TryExtractPollUrl(response, body);

            if (i == 0)
                output.WriteMarkup("[dim]Compilation in progress, waiting...[/]");

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);

            if (pollUrl is not null)
            {
                try
                {
                    var jobStatus = await registry.Client.GetAsync(pollUrl, ct);
                    var status = jobStatus.TryGetProperty("status", out var s) ? s.GetString()
                        : jobStatus.TryGetProperty("statusName", out var sn) ? sn.GetString()
                        : null;

                    if (status is not null && status != "Pending" && status != "Compiling")
                    {
                        response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                        continue;
                    }
                }
                catch (FirmletRegistryException)
                {
                }
            }

            response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        if (response.StatusCode == HttpStatusCode.Accepted)
            throw new HttpRequestException("Compilation did not complete within the timeout period");

        response.EnsureSuccessStatusCode();

        var tempZip = Path.Combine(Path.GetTempPath(), $"firmlet-install-{Guid.NewGuid():N}.zip");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(tempZip);
        await stream.CopyToAsync(file, ct);
        return tempZip;
    }

    private static string AppendVersion(string path)
    {
        var separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}api-version=1.0";
    }

    private static string? TryExtractPollUrl(HttpResponseMessage response, string body)
    {
        if (response.Headers.Location is not null)
            return response.Headers.Location.ToString();

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("jobId", out var jobId))
                return $"firmlets/{jobId}/compilations/{jobId.GetInt32()}";
        }
        catch
        {
        }

        return null;
    }

    private static Guid ResolveAssetId(InstallSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AssetId) && Guid.TryParse(settings.AssetId, out var parsed))
            return parsed;
        return Guid.NewGuid();
    }
}
