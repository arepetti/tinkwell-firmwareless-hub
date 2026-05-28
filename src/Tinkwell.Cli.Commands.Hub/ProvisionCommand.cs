using System.ComponentModel;
using Google.Protobuf;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;
using Tinkwell.Coap;
using Tw;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class ProvisionSettings : TwSettings
{
    [Description("Device address (host, host:port, or coap://host:port)")]
    [CommandOption("--device-addr")]
    public string DeviceAddr { get; set; } = "";
}

/// <summary>
/// Discovers a device via CoAP and assigns a hub GUID. Prints device
/// identity information so the operator can decide which firmlet to install.
/// Does NOT install a firmlet or start a WASM host.
/// </summary>
[CliCommand("hub", "provision", Description = "Provision a device via CoAP (discover identity, assign hub GUID)")]
public sealed class ProvisionCommand : AsyncCommand<ProvisionSettings>
{
    private static readonly ColumnDef<DeviceResult>[] Columns =
    [
        new("Asset ID", r => r.AssetId),
        new("Status", r => r.Status),
        new("Vendor", r => r.VendorId.ToString()),
        new("Vendor Name", r => r.VendorName, VerboseOnly: true),
        new("Product", r => r.ProductId.ToString()),
        new("Product Name", r => r.ProductName, VerboseOnly: true),
        new("Serial", r => r.SerialNumber.ToString(), VerboseOnly: true),
        new("FW Version", r => r.FwVersion),
        new("Device Name", r => r.DeviceName),
    ];

    public override async Task<int> ExecuteAsync(
        CommandContext context, ProvisionSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);

        if (string.IsNullOrWhiteSpace(settings.DeviceAddr))
        {
            output.WriteError("--device-addr is required");
            return 1;
        }

        var (host, port) = ParseEndpoint(settings.DeviceAddr);

        var info = await output.RunWithStatusAsync("Querying device...", async () =>
        {
            var resp = await CoapClient.SendAsync(
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

            if ((resp.Code >> 5) != 2)
                throw new InvalidOperationException(
                    $"GET /tw/provision/info failed: {CoapCode.ToDisplayString(resp.Code)}");

            return ProvisionInfo.Parser.ParseFrom(resp.Payload);
        });

        if (info.Device is null)
        {
            output.WriteError("Device returned ProvisionInfo without a Device payload");
            return 1;
        }

        var assetId = Guid.NewGuid();

        await output.RunWithStatusAsync("Assigning hub GUID...", async () =>
        {
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
        });

        var d = info.Device;
        var result = new DeviceResult
        {
            AssetId = assetId.ToString(),
            Status = info.Status,
            VendorId = d.VendorId,
            VendorName = d.VendorDisplayName,
            ProductId = d.ProductId,
            ProductName = d.ProductDisplayName,
            SerialNumber = d.SerialNumber,
            FwVersion = d.FwVersion,
            DeviceName = d.DeviceName,
        };

        output.WriteObject("Provisioned Device", Columns, result);
        return 0;
    }

    private static (string Host, int Port) ParseEndpoint(string addr)
    {
        var s = addr.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "coap://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Invalid device address; expected host, host:port, or coap://host:port.");

        return (uri.Host, uri.Port > 0 ? uri.Port : 5683);
    }

    private sealed class DeviceResult
    {
        public string AssetId { get; init; } = "";
        public string Status { get; init; } = "";
        public int VendorId { get; init; }
        public string VendorName { get; init; } = "";
        public int ProductId { get; init; }
        public string ProductName { get; init; } = "";
        public int SerialNumber { get; init; }
        public string FwVersion { get; init; } = "";
        public string DeviceName { get; init; } = "";
    }
}
