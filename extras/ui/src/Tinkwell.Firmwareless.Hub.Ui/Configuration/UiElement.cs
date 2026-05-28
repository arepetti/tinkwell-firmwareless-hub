namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// Base type for all UI tree elements (controls and layouts).
/// </summary>
public abstract class UiElement
{
    /// <summary>
    /// Globally unique identifier for this element, constructed by the parser
    /// from the structural path (e.g. "climate/living-room/main/temp").
    /// Used for targeted WebSocket updates.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The originating asset ID, or <see langword="null"/> for hub-level elements.
    /// Determines which measure/setting context is used for expression evaluation.
    /// </summary>
    public string? AssetId { get; init; }
}
