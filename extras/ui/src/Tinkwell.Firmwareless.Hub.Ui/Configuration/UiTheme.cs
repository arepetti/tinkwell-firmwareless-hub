using Tinkwell.Configuration.Parser;

namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// Hub-level theme and UI settings parsed from the <c>ui "home" { ... }</c> block.
/// </summary>
public sealed class UiTheme
{
    public string Title { get; set; } = "Home";
    public string Theme { get; set; } = "dark";
    public string Accent { get; set; } = "#4CAF50";
    public string FontSize { get; set; } = "medium";
    public string Locale { get; set; } = "en-US";

    internal void Apply(ConfigBlock block)
    {
        foreach (var prop in block.Properties)
        {
            var value = prop.Value switch
            {
                StringValue s => s.Value,
                _ => prop.Value.ToString()
            };

            switch (prop.Key)
            {
                case "title": Title = value ?? Title; break;
                case "theme": Theme = value ?? Theme; break;
                case "accent": Accent = value ?? Accent; break;
                case "font-size": FontSize = value ?? FontSize; break;
                case "locale": Locale = value ?? Locale; break;
            }
        }
    }
}
