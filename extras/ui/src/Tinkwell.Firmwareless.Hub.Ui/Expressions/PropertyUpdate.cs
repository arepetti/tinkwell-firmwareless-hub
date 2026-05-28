namespace Tinkwell.Firmwareless.Hub.Ui.Expressions;

/// <summary>
/// Represents a single property change to push to the frontend via WebSocket.
/// </summary>
public sealed record PropertyUpdate(string ControlId, string Property, object? Value);
