namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A typed setting definition parsed from <c>settings.tw</c>.
/// Provides metadata for UI validation and fallback values for
/// controls that bind to this setting.
/// </summary>
public sealed class SettingDefinition
{
    public required string Name { get; init; }
    public required SettingType Type { get; init; }
    public string? Description { get; init; }
    public object? Default { get; init; }

    // Number constraints
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Unit { get; init; }

    // String constraints
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }
    public string? Pattern { get; init; }

    // Object
    public string? Schema { get; init; }
}
