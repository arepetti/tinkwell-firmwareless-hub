using System.Text.Json;
using System.Text.Json.Serialization;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;

namespace Tinkwell.Firmwareless.Hub.Ui.Api;

/// <summary>
/// Serializes the <see cref="UiDocument"/> into the JSON tree format
/// consumed by the frontend. Expressions are resolved to their current
/// values; the frontend never sees expression text.
/// </summary>
public static class UiTreeSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static object BuildTree(UiDocument document)
    {
        return new
        {
            theme = BuildTheme(document.Theme),
            groups = document.Groups
                .OrderBy(g => g.Order)
                .Select(BuildGroup)
                .ToArray(),
            widgets = document.Widgets
                .OrderBy(w => w.Order)
                .Select(BuildWidget)
                .ToArray(),
        };
    }

    public static string SerializeTree(UiDocument document) =>
        JsonSerializer.Serialize(BuildTree(document), JsonOptions);

    public static object BuildThemeObject(UiDocument document) =>
        BuildTheme(document.Theme);

    public static string SerializeTheme(UiDocument document) =>
        JsonSerializer.Serialize(BuildTheme(document.Theme), JsonOptions);

    private static object BuildTheme(UiTheme theme) => new
    {
        title = theme.Title,
        theme = theme.Theme,
        accent = theme.Accent,
        fontSize = theme.FontSize,
        locale = theme.Locale,
    };

    private static object BuildGroup(UiGroup group) => new
    {
        name = group.Name,
        label = ResolveValue(group.Label, group.Name),
        icon = ResolveValue(group.Icon),
        visible = ResolveValue(group.Visible, true),
        order = group.Order,
        pages = group.Pages
            .OrderBy(p => p.Order)
            .Select(BuildPage)
            .ToArray(),
    };

    private static object BuildPage(UiPage page) => new
    {
        name = page.Name,
        label = ResolveValue(page.Label, page.Name),
        icon = ResolveValue(page.Icon),
        visible = ResolveValue(page.Visible, true),
        order = page.Order,
        children = page.Children.Select(BuildElement).ToArray(),
    };

    private static object BuildWidget(UiWidget widget) => new
    {
        name = widget.Name,
        label = ResolveValue(widget.Label, widget.Name),
        icon = ResolveValue(widget.Icon),
        order = widget.Order,
        children = widget.Children.Select(BuildElement).ToArray(),
    };

    private static object BuildElement(UiElement element) => element switch
    {
        UiControl c => BuildControl(c),
        UiLayout l => BuildLayout(l),
        _ => new { type = "unknown" }
    };

    private static object BuildControl(UiControl control)
    {
        var props = new Dictionary<string, object?>();
        foreach (var (key, pv) in control.Properties)
            props[key] = pv.Value;

        return new
        {
            kind = "control",
            type = control.Type.ToString().ToLowerInvariant(),
            id = control.Id,
            name = control.Name,
            properties = props,
            map = control.Map,
        };
    }

    private static object BuildLayout(UiLayout layout) => new
    {
        kind = "layout",
        type = layout.Type.ToString().ToLowerInvariant(),
        id = layout.Id,
        name = layout.Name,
        properties = layout.Properties,
        children = layout.Children.Select(BuildElement).ToArray(),
    };

    private static object? ResolveValue(UiPropertyValue? pv, object? fallback = null) =>
        pv?.Value ?? fallback;
}
