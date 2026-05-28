namespace Tinkwell.Runlet.Firmwareless.AssetRegistry.Data;

public sealed class CommandEntry
{
    public long Id { get; set; }
    public required Guid AssetId { get; set; }
    public required string CommandType { get; set; }
    public byte[] Payload { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
