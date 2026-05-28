using Tinkwell.Firmwareless.Hub.Ui.Configuration;

namespace Tinkwell.Firmwareless.Hub.Ui.Expressions;

/// <summary>
/// Tracks a single expression binding: which control property it belongs to,
/// the expression text, and which variable names it depends on.
/// </summary>
internal sealed class ExpressionBinding
{
    public required string ControlId { get; init; }
    public required string PropertyName { get; init; }
    public required string Expression { get; init; }
    public required string? AssetId { get; init; }
    public required UiPropertyValue Target { get; init; }

    /// <summary>
    /// Variable names referenced by this expression, extracted by scanning
    /// for identifiers. Used for the dependency graph.
    /// </summary>
    public HashSet<string> Dependencies { get; init; } = [];
}
