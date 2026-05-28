using System.Collections.Concurrent;

namespace Tinkwell.Firmwareless.Hosting.WasmHost.Measures;

public sealed class MeasureCache
{
    private readonly ConcurrentDictionary<string, CachedMeasure> _values = new(StringComparer.Ordinal);

    public void Set(string name, double value, string unit, ulong timestampMs)
    {
        _values[name] = new CachedMeasure(name, value, unit, timestampMs);
    }

    public CachedMeasure? Get(string name) =>
        _values.GetValueOrDefault(name);

    public IReadOnlyList<CachedMeasure> GetAll() =>
        _values.Values.ToList();

    public void Clear() => _values.Clear();

    public sealed record CachedMeasure(string Name, double Value, string Unit, ulong TimestampMs);
}
