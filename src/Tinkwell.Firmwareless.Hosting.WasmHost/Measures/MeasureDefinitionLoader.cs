using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Tinkwell.Firmwareless.Hosting.WasmHost.Measures;

public sealed class MeasureDefinitionLoader
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, MeasureDefinitionEntry> _definitions = new(StringComparer.Ordinal);

    public MeasureDefinitionLoader(ILogger logger)
    {
        _logger = logger;
    }

    public void LoadFromFile(string measuresPath)
    {
        _definitions.Clear();

        if (!File.Exists(measuresPath))
        {
            _logger.LogDebug("No measures.tw found at {Path}", measuresPath);
            return;
        }

        try
        {
            var text = File.ReadAllText(measuresPath);
            ParseMeasures(text);
            _logger.LogInformation("Loaded {Count} measure definitions from {Path}", _definitions.Count, measuresPath);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse measures.tw at {Path}", measuresPath);
        }
    }

    public MeasureDefinitionEntry? Find(string name) =>
        _definitions.GetValueOrDefault(name);

    public IReadOnlyList<MeasureDefinitionEntry> ListAll() =>
        _definitions.Values.ToList();

    private void ParseMeasures(string text)
    {
        var reader = new StringReader(text);
        string? line;
        MeasureDefinitionEntry? current = null;

        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("//") || trimmed.Length == 0)
                continue;

            if (trimmed.StartsWith("measure ") || trimmed.StartsWith("measure\t"))
            {
                if (current is not null)
                {
                    _logger.LogWarning("Unclosed measure block for '{Name}', saving partial", current.Name);
                    _definitions[current.Name] = current;
                }

                var name = ExtractBlockName(trimmed, "measure");
                current = new MeasureDefinitionEntry { Name = name };
                if (trimmed.EndsWith('{'))
                    continue;

                _definitions[name] = current;
                current = null;
                continue;
            }

            if (trimmed == "{" && current is not null)
                continue;

            if (trimmed == "}" && current is not null)
            {
                _definitions[current.Name] = current;
                current = null;
                continue;
            }

            if (current is not null && trimmed.Contains('='))
            {
                var eqIdx = trimmed.IndexOf('=');
                var key = trimmed[..eqIdx].Trim();
                var val = trimmed[(eqIdx + 1)..].TrimEnd(';').Trim().Trim('"');

                switch (key)
                {
                    case "quantity_type": current.QuantityType = val; break;
                    case "unit": current.Unit = val; break;
                    case "description": current.Description = val; break;
                    case "category": current.Category = val; break;
                    case "minimum":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var min))
                            current.Minimum = min;
                        break;
                    case "maximum":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                            current.Maximum = max;
                        break;
                    case "precision":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prec))
                            current.Precision = prec;
                        break;
                    case "ttl":
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttl))
                            current.TtlSeconds = ttl;
                        break;
                }
            }
        }

        if (current is not null)
        {
            _logger.LogWarning("Unclosed measure block at EOF for '{Name}', saving partial", current.Name);
            _definitions[current.Name] = current;
        }
    }

    private static string ExtractBlockName(string line, string keyword)
    {
        var rest = line[(keyword.Length + 1)..].Trim();
        if (rest.EndsWith('{'))
            rest = rest[..^1].Trim();
        return rest.Trim('"');
    }

    public sealed class MeasureDefinitionEntry
    {
        public required string Name { get; set; }
        public string? QuantityType { get; set; }
        public string? Unit { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public int? Precision { get; set; }
        public int? TtlSeconds { get; set; }
    }
}