using System.Collections.Concurrent;

namespace Tinkwell.Firmwareless.Hub.Ui.Expressions;

/// <summary>
/// Holds the current measure values and setting values for a single asset.
/// Thread-safe: values are updated from gRPC streams and read by the
/// expression engine concurrently.
/// </summary>
public sealed class AssetContext
{
    public string AssetId { get; }

    private readonly ConcurrentDictionary<string, object?> _values = new(StringComparer.Ordinal);

    public AssetContext(string assetId)
    {
        AssetId = assetId;
    }

    public object? GetValue(string name) =>
        _values.TryGetValue(name, out var v) ? v : null;

    /// <returns>
    /// <see langword="true"/> if the value actually changed.
    /// </returns>
    public bool SetValue(string name, object? value)
    {
        var existing = _values.TryGetValue(name, out var old) ? old : null;
        _values[name] = value;
        return !Equals(existing, value);
    }

    public IReadOnlyDictionary<string, object?> GetAllValues() =>
        _values;
}
