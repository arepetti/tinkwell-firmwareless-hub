namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A layout container (grid, hstack, vstack) with child elements.
/// </summary>
public sealed class UiLayout : UiElement
{
    public required UiLayoutType Type { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Layout-specific properties (e.g. columns, gap, align).
    /// </summary>
    public Dictionary<string, object?> Properties { get; init; } = [];

    /// <summary>
    /// Ordered children: controls and/or nested layouts.
    /// </summary>
    public List<UiElement> Children { get; init; } = [];
}
