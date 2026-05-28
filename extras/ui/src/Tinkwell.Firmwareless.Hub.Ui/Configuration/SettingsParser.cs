using Microsoft.Extensions.Logging;
using Tinkwell.Configuration.Parser;

namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// Parses <c>settings.tw</c> files into a dictionary of <see cref="SettingDefinition"/>.
/// </summary>
public sealed class SettingsParser : ConfigurationParser<IReadOnlyDictionary<string, SettingDefinition>>
{
    public SettingsParser(ILogger? logger = null)
        : base(logger, new ParserOptions { Lax = true })
    {
    }

    protected override ValueTask<IReadOnlyDictionary<string, SettingDefinition>> TransformAsync(
        ConfigDocument document, CancellationToken cancellationToken)
    {
        var settings = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);

        foreach (var block in document.Blocks)
        {
            if (block.Type != "setting")
                continue;

            var def = ParseSetting(block);
            settings[def.Name] = def;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, SettingDefinition>>(settings);
    }

    private static SettingDefinition ParseSetting(ConfigBlock block)
    {
        var typeStr = GetString(block, "type") ?? "string";
        var settingType = ParseSettingType(typeStr);

        return new SettingDefinition
        {
            Name = block.Name,
            Type = settingType,
            Description = GetString(block, "description"),
            Default = GetValue(block, "default"),
            Min = GetDouble(block, "min"),
            Max = GetDouble(block, "max"),
            Step = GetDouble(block, "step"),
            Unit = GetString(block, "unit"),
            MinLength = GetInt(block, "min-length"),
            MaxLength = GetInt(block, "max-length"),
            Pattern = GetString(block, "pattern"),
            Schema = GetString(block, "schema"),
        };
    }

    private static SettingType ParseSettingType(string type) => type.ToLowerInvariant() switch
    {
        "number" => SettingType.Number,
        "bool" => SettingType.Bool,
        "string" => SettingType.String,
        "date" => SettingType.Date,
        "time" => SettingType.Time,
        "timespan" => SettingType.Timespan,
        "object" => SettingType.Object,
        _ => SettingType.String
    };

    private static string? GetString(ConfigBlock block, string key)
    {
        var prop = block.Properties.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (prop is null)
            return null;
        return prop.Value switch
        {
            StringValue s => s.Value,
            _ => prop.Value.ToString()
        };
    }

    private static double? GetDouble(ConfigBlock block, string key)
    {
        var prop = block.Properties.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (prop is null)
            return null;
        return prop.Value switch
        {
            LongValue l => l.Value,
            DoubleValue d => d.Value,
            StringValue s when double.TryParse(s.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v) => v,
            _ => null
        };
    }

    private static int? GetInt(ConfigBlock block, string key)
    {
        var d = GetDouble(block, key);
        return d.HasValue ? (int)d.Value : null;
    }

    private static object? GetValue(ConfigBlock block, string key)
    {
        var prop = block.Properties.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (prop is null)
            return null;
        return prop.Value switch
        {
            StringValue s => s.Value,
            LongValue l => l.Value,
            DoubleValue d => d.Value,
            BoolValue b => b.Value,
            _ => prop.Value.ToString()
        };
    }
}
