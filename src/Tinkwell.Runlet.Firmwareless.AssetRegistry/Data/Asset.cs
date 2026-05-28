namespace Tinkwell.Runlet.Firmwareless.AssetRegistry.Data;

public enum CommunicationMode
{
    ServiceOnly,
    AlwaysOn,
    Mailbox,
}

public enum AssetState
{
    Created,
    Resolving,
    Downloading,
    Starting,
    Online,
    Offline,
    Error,
}

public sealed class Asset
{
    public required Guid Id { get; set; }
    public string? DisplayName { get; set; }

    public int? VendorId { get; set; }
    public int? ProductId { get; set; }
    public byte[]? Variant { get; set; }
    public string? FirmwareVersion { get; set; }

    public CommunicationMode CommunicationMode { get; set; } = CommunicationMode.ServiceOnly;

    public string? FirmletName { get; set; }
    public string? FirmletVersion { get; set; }
    public bool FirmletInitialized { get; set; }

    public AssetState State { get; set; } = AssetState.Created;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHeartbeat { get; set; }
    public string? LastError { get; set; }

    public bool HasDevice => VendorId.HasValue;
}
