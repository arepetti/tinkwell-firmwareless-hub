namespace Tinkwell.Runlet.Firmwareless.CoAP;

/// <summary>
/// Simple in-memory cache for last-known sensor values per asset.
/// </summary>
public sealed class SensorCache
{
    private readonly Dictionary<string, SensorReading> _readings = new();

    public void Update(Guid assetId, string sensorName, double value, ulong timestampMs)
    {
        var key = $"{assetId}:{sensorName}";
        _readings[key] = new SensorReading(sensorName, value, timestampMs);
    }

    public SensorReading? Get(Guid assetId, string sensorName)
    {
        var key = $"{assetId}:{sensorName}";
        return _readings.GetValueOrDefault(key);
    }

    public sealed record SensorReading(string Name, double Value, ulong TimestampMs);
}
