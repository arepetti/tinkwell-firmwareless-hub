namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A page (tab) within a group. Contains controls and layouts.
/// </summary>
public sealed class UiPage
{
    public required string Name { get; init; }
    public string? AssetId { get; init; }
    public UiPropertyValue? Label { get; init; }
    public UiPropertyValue? Icon { get; init; }
    public UiPropertyValue? Visible { get; init; }
    public int Order { get; init; }
    public List<UiElement> Children { get; init; } = [];
}
