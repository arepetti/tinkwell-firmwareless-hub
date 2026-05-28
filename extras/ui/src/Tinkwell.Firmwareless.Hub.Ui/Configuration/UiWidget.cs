namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A reusable widget card defined outside groups. Each widget is resolved
/// in its originating asset's context (measures + settings).
/// </summary>
public sealed class UiWidget
{
    public required string Name { get; init; }
    public string? AssetId { get; init; }
    public UiPropertyValue? Label { get; init; }
    public UiPropertyValue? Icon { get; init; }
    public int Order { get; init; }
    public List<UiElement> Children { get; init; } = [];
}
