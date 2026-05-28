using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;

/// <summary>
/// Persistent on-disk queue for firmlet downloads that failed due to
/// transient errors (network, compilation not ready). Stored as a JSON
/// array in <c>{firmletBaseDir}/pending-downloads.json</c>.
/// </summary>
public sealed class PendingDownloadStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public PendingDownloadStore(string firmletBaseDir)
    {
        _filePath = Path.Combine(firmletBaseDir, "pending-downloads.json");
    }

    public async Task<List<PendingDownload>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await ReadFileAsync(ct); }
        finally { _lock.Release(); }
    }

    public async Task AddAsync(PendingDownload entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadFileAsync(ct);
            list.RemoveAll(e => e.AssetId == entry.AssetId);
            list.Add(entry);
            await WriteFileAsync(list, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string assetId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadFileAsync(ct);
            if (list.RemoveAll(e => string.Equals(e.AssetId, assetId, StringComparison.OrdinalIgnoreCase)) > 0)
                await WriteFileAsync(list, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task UpdateAsync(string assetId, Action<PendingDownload> update, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await ReadFileAsync(ct);
            var entry = list.Find(e => string.Equals(e.AssetId, assetId, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                update(entry);
                await WriteFileAsync(list, ct);
            }
        }
        finally { _lock.Release(); }
    }

    private async Task<List<PendingDownload>> ReadFileAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return [];
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            return JsonSerializer.Deserialize<List<PendingDownload>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task WriteFileAsync(List<PendingDownload> list, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(list, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }
}

public sealed class PendingDownload
{
    public string AssetId { get; set; } = "";
    public string FirmletName { get; set; } = "";
    public string? FirmletVersion { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
