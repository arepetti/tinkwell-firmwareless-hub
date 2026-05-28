using System.ComponentModel;
using System.Text.Json;
using Google.Protobuf;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;
using Tinkwell.Coap;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;
using Tw;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class AddSettings : HubSettings
{
    [Description("Path to the installed firmlet directory")]
    [CommandArgument(0, "<firmlet-dir>")]
    public string FirmletDir { get; set; } = "";

    [Description("Asset ID (GUID). Defaults to the directory name or auto-generated")]
    [CommandOption("--asset-id")]
    [DefaultValue(null)]
    public string? AssetId { get; set; }

    [Description("Vendor ID")]
    [CommandOption("--vendor")]
    [DefaultValue(0)]
    public int VendorId { get; set; }

    [Description("Product ID")]
    [CommandOption("--product")]
    [DefaultValue(0)]
    public int ProductId { get; set; }

    [Description("Also provision the device via CoAP before starting the host")]
    [CommandOption("--provision")]
    [DefaultValue(false)]
    public bool Provision { get; set; }

    [Description("Device address for CoAP provisioning (required with --provision)")]
    [CommandOption("--device-addr")]
    [DefaultValue(null)]
    public string? DeviceAddr { get; set; }
}

[CliCommand("hub", "add", Description = "Register an installed firmlet and start the WASM host")]
public sealed class AddCommand : AsyncCommand<AddSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context, AddSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);

        var firmletDir = Path.GetFullPath(settings.FirmletDir);
        if (!Directory.Exists(firmletDir))
        {
            output.WriteError($"Firmlet directory not found: {firmletDir}");
            return 1;
        }

        var assetId = ResolveAssetId(settings, firmletDir);
        if (assetId is null)
        {
            output.WriteError("Could not determine asset ID. Provide --asset-id or use a GUID-named directory.");
            return 1;
        }

        var (firmletName, firmletVersion) = ReadPackageMetadata(firmletDir);

        if (settings.Provision)
        {
            if (string.IsNullOrWhiteSpace(settings.DeviceAddr))
            {
                output.WriteError("--device-addr is required when --provision is set");
                return 1;
            }

            await output.RunWithStatusAsync("Provisioning device...", async () =>
            {
                await ProvisionDeviceAsync(settings.DeviceAddr, assetId.Value, ct);
            });

            output.WriteMarkup($"[green]Device provisioned[/] with asset ID [cyan]{assetId}[/]");
        }

        using var grpc = GrpcFactory.FromSettings(settings);

        await output.RunWithStatusAsync("Registering asset...", async () =>
        {
            var reply = await grpc.Assets.RegisterAssetAsync(new RegisterAssetRequest
            {
                Id = assetId.Value.ToString(),
                DisplayName = firmletName ?? assetId.Value.ToString(),
                VendorId = settings.VendorId,
                ProductId = settings.ProductId,
                FirmletName = firmletName ?? "",
                FirmletVersion = firmletVersion ?? "",
                CommunicationMode = "always-on",
            }, cancellationToken: ct);

            if (!reply.Success)
                throw new InvalidOperationException($"RegisterAsset failed: {reply.Error}");
        });

        await output.RunWithStatusAsync("Starting host...", async () =>
        {
            var reply = await grpc.Proxy.StartHostAsync(new StartHostRequest
            {
                AssetId = assetId.Value.ToString(),
                FirmletPath = firmletDir,
            }, cancellationToken: ct);

            if (!reply.Success)
                throw new InvalidOperationException($"StartHost failed: {reply.Error}");
        });

        output.WriteSuccess($"Asset [cyan]{assetId}[/] added and host started");
        return 0;
    }

    private static Guid? ResolveAssetId(AddSettings settings, string firmletDir)
    {
        if (!string.IsNullOrWhiteSpace(settings.AssetId))
            return Guid.TryParse(settings.AssetId, out var parsed) ? parsed : null;

        var dirName = Path.GetFileName(firmletDir);
        if (Guid.TryParse(dirName, out var fromDir))
            return fromDir;

        return Guid.NewGuid();
    }

    private static (string? Name, string? Version) ReadPackageMetadata(string firmletDir)
    {
        var metadataPath = Path.Combine(firmletDir, "package.json");
        if (!File.Exists(metadataPath))
            return (null, null);

        try
        {
            var json = File.ReadAllText(metadataPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            return (name, version);
        }
        catch
        {
            return (null, null);
        }
    }

    private static async Task ProvisionDeviceAsync(string deviceAddr, Guid assetId, CancellationToken ct)
    {
        var (host, port) = ParseDeviceEndpoint(deviceAddr);

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
        var resp = await CoapClient.SendAsync(
            host, port,
            "/tw/provision/hub",
            query: null,
            hubReq,
            hubOptions,
            ct);

        if ((resp.Code >> 5) != 2)
            throw new InvalidOperationException(
                $"POST /tw/provision/hub failed: {CoapCode.ToDisplayString(resp.Code)}");

        if (resp.Payload.Length > 0)
        {
            var reply = ProvisionReply.Parser.ParseFrom(resp.Payload);
            if (!reply.Success)
                throw new InvalidOperationException($"Hub provision rejected: {reply.Error}");
        }
    }

    private static (string Host, int Port) ParseDeviceEndpoint(string addr)
    {
        var s = addr.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "coap://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Invalid device address; expected host, host:port, or coap://host:port.");

        return (uri.Host, uri.Port > 0 ? uri.Port : 5683);
    }
}
