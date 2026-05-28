namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// A control property that carries both a resolved value and an optional
/// expression for runtime re-evaluation. When <see cref="Expression"/> is
/// <see langword="null"/> the value is static; otherwise the expression
/// engine re-evaluates it whenever a referenced variable changes.
/// </summary>
public sealed class UiPropertyValue
{
    public UiPropertyValue(object? value, string? expression = null)
    {
        Value = value;
        Expression = expression;
    }

    /// <summary>
    /// The current resolved value (number, string, bool).
    /// Updated by the expression engine when <see cref="Expression"/> is set.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// The NCalc expression text, or <see langword="null"/> for static values.
    /// </summary>
    public string? Expression { get; }

    public bool IsExpression => Expression is not null;

    public override string ToString() =>
        Expression is not null ? $"({Expression}) = {Value}" : $"{Value}";
}
