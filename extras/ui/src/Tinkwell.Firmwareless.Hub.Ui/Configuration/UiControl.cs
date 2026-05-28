namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A UI control (gauge, indicator, text, value, button, toggle, slider).
/// All properties are stored in a generic dictionary so the parser doesn't
/// need per-control-type model classes; the frontend interprets properties
/// based on <see cref="Type"/>.
/// </summary>
public sealed class UiControl : UiElement
{
    public required UiControlType Type { get; init; }

    /// <summary>
    /// The block name from the .tw file (the user-chosen ID).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// All properties for this control. Keys are property names (e.g. "value",
    /// "min", "label"), values carry both the resolved value and the optional
    /// expression text.
    /// </summary>
    public Dictionary<string, UiPropertyValue> Properties { get; init; } = [];

    /// <summary>
    /// For indicator controls: the map of discrete values to display entries.
    /// Key is the entry name (e.g. "off", "heat"), value is the entry properties.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? Map { get; init; }
}
