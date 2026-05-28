using Microsoft.Extensions.Logging;
using Tinkwell.Configuration.Parser;

namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// Parses <c>ui.tw</c> files into a <see cref="UiDocument"/> containing
/// groups, pages, widgets, controls, and layouts.
/// </summary>
public sealed class UiConfigParser : ConfigurationParser<UiDocument>
{
    private readonly string? _assetId;

    public UiConfigParser(string? assetId = null, ILogger? logger = null)
        : base(logger, new ParserOptions { Lax = true })
    {
        _assetId = assetId;
    }

    protected override ValueTask<UiDocument> TransformAsync(
        ConfigDocument document, CancellationToken cancellationToken)
    {
        var doc = new UiDocument();

        foreach (var block in document.Blocks)
        {
            switch (block.Type)
            {
                case "ui":
                    doc.Theme.Apply(block);
                    break;
                case "group":
                    doc.Groups.Add(ParseGroup(block));
                    break;
                case "widget":
                    doc.Widgets.Add(ParseWidget(block));
                    break;
            }
        }

        return ValueTask.FromResult(doc);
    }

    private UiGroup ParseGroup(ConfigBlock block)
    {
        var groupPath = BuildRootPath(block.Name);
        var group = new UiGroup
        {
            Name = block.Name,
            AssetId = _assetId,
            Label = GetPropertyValue(block, "label"),
            Icon = GetPropertyValue(block, "icon"),
            Visible = GetPropertyValue(block, "visible"),
            Order = GetIntProperty(block, "order", 0),
        };

        foreach (var child in block.Children)
        {
            if (child.Type == "page")
                group.Pages.Add(ParsePage(child, groupPath));
        }

        return group;
    }

    private UiPage ParsePage(ConfigBlock block, string parentPath)
    {
        var pagePath = BuildId(parentPath, block.Name);
        var page = new UiPage
        {
            Name = block.Name,
            AssetId = _assetId,
            Label = GetPropertyValue(block, "label"),
            Icon = GetPropertyValue(block, "icon"),
            Visible = GetPropertyValue(block, "visible"),
            Order = GetIntProperty(block, "order", 0),
        };

        ParseChildren(block, page.Children, pagePath);
        return page;
    }

    private UiWidget ParseWidget(ConfigBlock block)
    {
        var widgetPath = BuildRootPath(block.Name);
        var widget = new UiWidget
        {
            Name = block.Name,
            AssetId = _assetId,
            Label = GetPropertyValue(block, "label"),
            Icon = GetPropertyValue(block, "icon"),
            Order = GetIntProperty(block, "order", 0),
        };

        ParseChildren(block, widget.Children, widgetPath);
        return widget;
    }

    private void ParseChildren(ConfigBlock block, List<UiElement> target, string parentPath)
    {
        foreach (var child in block.Children)
        {
            var element = ParseElement(child, parentPath);
            if (element is not null)
                target.Add(element);
        }
    }

    private UiElement? ParseElement(ConfigBlock block, string parentPath)
    {
        if (TryParseLayoutType(block.Type, out var layoutType))
            return ParseLayout(block, layoutType, parentPath);

        if (TryParseControlType(block.Type, out var controlType))
            return ParseControl(block, controlType, parentPath);

        return null;
    }

    private UiLayout ParseLayout(ConfigBlock block, UiLayoutType layoutType, string parentPath)
    {
        var id = BuildId(parentPath, block.Name);
        var layout = new UiLayout
        {
            Id = id,
            AssetId = _assetId,
            Type = layoutType,
            Name = block.Name,
        };

        foreach (var prop in block.Properties)
            layout.Properties[prop.Key] = ConfigValueToObject(prop.Value);

        ParseChildren(block, layout.Children, id);
        return layout;
    }

    private UiControl ParseControl(ConfigBlock block, UiControlType controlType, string parentPath)
    {
        var properties = new Dictionary<string, UiPropertyValue>();
        foreach (var prop in block.Properties)
            properties[prop.Key] = ToPropertyValue(prop.Value);

        return new UiControl
        {
            Id = BuildId(parentPath, block.Name),
            AssetId = _assetId,
            Type = controlType,
            Name = block.Name,
            Properties = properties,
            Map = controlType == UiControlType.Indicator ? ParseMap(block) : null,
        };
    }

    private static Dictionary<string, Dictionary<string, string>>? ParseMap(ConfigBlock block)
    {
        var mapBlock = block.Children.FirstOrDefault(c => c.Type == "map");
        if (mapBlock is null)
            return null;

        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var entry in mapBlock.Children)
        {
            if (entry.Type != "entry")
                continue;

            var props = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in entry.Properties)
            {
                var val = ConfigValueToObject(prop.Value);
                if (val is not null)
                    props[prop.Key] = val.ToString()!;
            }

            map[entry.Name] = props;
        }

        return map.Count > 0 ? map : null;
    }

    private static UiPropertyValue ToPropertyValue(ConfigValue value)
    {
        return value switch
        {
            ExpressionValue ev => new UiPropertyValue(null, ev.Expression),
            _ => new UiPropertyValue(ConfigValueToObject(value))
        };
    }

    private static object? ConfigValueToObject(ConfigValue value) => value switch
    {
        StringValue s => s.Value,
        LongValue l => l.Value,
        DoubleValue d => d.Value,
        BoolValue b => b.Value,
        ExpressionValue e => e.Expression,
        _ => value.ToString()
    };

    private static UiPropertyValue? GetPropertyValue(ConfigBlock block, string key)
    {
        var prop = block.Properties.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        return prop is null ? null : ToPropertyValue(prop.Value);
    }

    private static int GetIntProperty(ConfigBlock block, string key, int defaultValue)
    {
        var prop = block.Properties.FirstOrDefault(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        if (prop is null)
            return defaultValue;

        return prop.Value switch
        {
            LongValue lv => (int)lv.Value,
            DoubleValue dv => (int)dv.Value,
            _ => defaultValue
        };
    }

    private static string BuildId(string parentPath, string name)
    {
        return string.IsNullOrEmpty(parentPath)
            ? name
            : $"{parentPath}/{name}";
    }

    private string BuildRootPath(string name)
    {
        return _assetId is not null ? $"{_assetId}/{name}" : name;
    }

    private static bool TryParseControlType(string type, out UiControlType result)
    {
        result = type switch
        {
            "gauge" => UiControlType.Gauge,
            "indicator" => UiControlType.Indicator,
            "text" => UiControlType.Text,
            "value" => UiControlType.Value,
            "button" => UiControlType.Button,
            "toggle" => UiControlType.Toggle,
            "slider" => UiControlType.Slider,
            _ => (UiControlType)(-1)
        };
        return (int)result >= 0;
    }

    private static bool TryParseLayoutType(string type, out UiLayoutType result)
    {
        result = type switch
        {
            "grid" => UiLayoutType.Grid,
            "hstack" => UiLayoutType.HStack,
            "vstack" => UiLayoutType.VStack,
            _ => (UiLayoutType)(-1)
        };
        return (int)result >= 0;
    }
}
