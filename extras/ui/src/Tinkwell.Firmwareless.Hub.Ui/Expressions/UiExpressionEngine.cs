using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tinkwell.Expressions;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;

namespace Tinkwell.Firmwareless.Hub.Ui.Expressions;

/// <summary>
/// Evaluates NCalc expressions for UI control properties and maintains a
/// dependency graph so that only affected expressions are re-evaluated when
/// a measure or setting value changes.
/// </summary>
public sealed partial class UiExpressionEngine
{
    private readonly IExpressionEvaluator _evaluator;
    private readonly ConcurrentDictionary<string, AssetContext> _contexts = new(StringComparer.Ordinal);
    private readonly List<ExpressionBinding> _bindings = [];
    private readonly Dictionary<string, List<ExpressionBinding>> _dependencyIndex = new(StringComparer.Ordinal);
    private readonly ILogger<UiExpressionEngine>? _logger;

    public UiExpressionEngine(IExpressionEvaluator evaluator, ILogger<UiExpressionEngine>? logger = null)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    public AssetContext GetOrCreateContext(string assetId)
    {
        return _contexts.GetOrAdd(assetId, id => new AssetContext(id));
    }

    /// <summary>
    /// Registers all expression-driven properties from the document.
    /// Call once after parsing and merging all UI configs.
    /// </summary>
    public void RegisterDocument(UiDocument document)
    {
        foreach (var group in document.Groups)
        {
            RegisterPropertyValueBindings(group.Name, group.AssetId, "label", group.Label);
            RegisterPropertyValueBindings(group.Name, group.AssetId, "icon", group.Icon);
            RegisterPropertyValueBindings(group.Name, group.AssetId, "visible", group.Visible);

            foreach (var page in group.Pages)
            {
                var pageId = $"{group.Name}/{page.Name}";
                RegisterPropertyValueBindings(pageId, page.AssetId, "label", page.Label);
                RegisterPropertyValueBindings(pageId, page.AssetId, "icon", page.Icon);
                RegisterPropertyValueBindings(pageId, page.AssetId, "visible", page.Visible);
                RegisterElements(page.Children);
            }
        }

        foreach (var widget in document.Widgets)
        {
            RegisterPropertyValueBindings(widget.Name, widget.AssetId, "label", widget.Label);
            RegisterPropertyValueBindings(widget.Name, widget.AssetId, "icon", widget.Icon);
            RegisterElements(widget.Children);
        }
    }

    private void RegisterElements(List<UiElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case UiControl control:
                    foreach (var (key, pv) in control.Properties)
                    {
                        if (pv.IsExpression)
                            RegisterBinding(control.Id, control.AssetId, key, pv);
                    }
                    break;

                case UiLayout layout:
                    RegisterElements(layout.Children);
                    break;
            }
        }
    }

    private void RegisterPropertyValueBindings(string id, string? assetId, string property, UiPropertyValue? pv)
    {
        if (pv?.IsExpression == true)
            RegisterBinding(id, assetId, property, pv);
    }

    private void RegisterBinding(string controlId, string? assetId, string property, UiPropertyValue pv)
    {
        var deps = ExtractDependencies(pv.Expression!);
        var binding = new ExpressionBinding
        {
            ControlId = controlId,
            PropertyName = property,
            Expression = pv.Expression!,
            AssetId = assetId,
            Target = pv,
            Dependencies = deps,
        };

        _bindings.Add(binding);

        foreach (var dep in deps)
        {
            var key = BuildDependencyKey(assetId, dep);
            if (!_dependencyIndex.TryGetValue(key, out var list))
            {
                list = [];
                _dependencyIndex[key] = list;
            }
            list.Add(binding);
        }
    }

    /// <summary>
    /// Called when a variable changes. Re-evaluates all expressions that depend
    /// on it and returns the list of property updates for the frontend.
    /// </summary>
    public async Task<List<PropertyUpdate>> OnValueChangedAsync(
        string assetId, string variableName, object? newValue,
        CancellationToken cancellationToken = default)
    {
        var context = GetOrCreateContext(assetId);
        if (!context.SetValue(variableName, newValue))
            return [];

        var key = BuildDependencyKey(assetId, variableName);
        if (!_dependencyIndex.TryGetValue(key, out var bindings))
            return [];

        var updates = new List<PropertyUpdate>();
        var parameters = context.GetAllValues();

        foreach (var binding in bindings)
        {
            try
            {
                var result = await _evaluator.EvaluateAsync(
                    binding.Expression, parameters, cancellationToken: cancellationToken);

                if (!Equals(binding.Target.Value, result))
                {
                    binding.Target.Value = result;
                    updates.Add(new PropertyUpdate(binding.ControlId, binding.PropertyName, result));
                }
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Failed to evaluate expression '{Expression}' for {ControlId}.{Property}",
                    binding.Expression, binding.ControlId, binding.PropertyName);
            }
        }

        return updates;
    }

    /// <summary>
    /// Evaluates all registered expressions with current context values.
    /// Called once at startup to populate initial resolved values.
    /// </summary>
    public async Task EvaluateAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var binding in _bindings)
        {
            try
            {
                var context = binding.AssetId is not null
                    ? GetOrCreateContext(binding.AssetId)
                    : null;

                var parameters = context?.GetAllValues()
                    ?? (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>();

                var result = await _evaluator.EvaluateAsync(
                    binding.Expression, parameters, cancellationToken: cancellationToken);

                binding.Target.Value = result;
            }
            catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Failed initial evaluation of '{Expression}' for {ControlId}.{Property}",
                    binding.Expression, binding.ControlId, binding.PropertyName);
            }
        }
    }

    private static string BuildDependencyKey(string? assetId, string variableName) =>
        assetId is not null ? $"{assetId}:{variableName}" : variableName;

    public static HashSet<string> ExtractDependencies(string expression)
    {
        var deps = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in IdentifierRegex().Matches(expression))
        {
            var name = match.Groups[1].Value;
            if (!IsKeyword(name))
                deps.Add(name);
        }
        return deps;
    }

    private static bool IsKeyword(string name) => name is
        "true" or "false" or "null" or "if" or "in" or "not" or "and" or "or";

    [GeneratedRegex(@"(?<!['""\w])([a-zA-Z_][\w-]*)(?!['""\w(])")]
    private static partial Regex IdentifierRegex();
}