namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A top-level navigation group (sidebar/bottom tab item). Contains pages.
/// </summary>
public sealed class UiGroup
{
    public required string Name { get; init; }
    public string? AssetId { get; init; }
    public UiPropertyValue? Label { get; init; }
    public UiPropertyValue? Icon { get; init; }
    public UiPropertyValue? Visible { get; init; }
    public int Order { get; init; }
    public List<UiPage> Pages { get; init; } = [];
}
