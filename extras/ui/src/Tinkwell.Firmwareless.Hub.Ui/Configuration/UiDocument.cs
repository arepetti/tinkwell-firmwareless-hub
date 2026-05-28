namespace Tinkwell.Firmwareless.Hub.Ui.Configuration;

/// <summary>
/// The complete parsed UI configuration, merging hub-level config
/// and all asset-contributed groups, pages, and widgets.
/// </summary>
public sealed class UiDocument
{
    public UiTheme Theme { get; init; } = new();
    public List<UiGroup> Groups { get; init; } = [];
    public List<UiWidget> Widgets { get; init; } = [];
    public Dictionary<string, SettingDefinition> Settings { get; init; } = [];

    /// <summary>
    /// Merges another document (typically from a single asset) into this one.
    /// Groups with the same name are merged (pages combined); otherwise the
    /// group is added. Widgets and settings are always appended/merged by key.
    /// </summary>
    public void Merge(UiDocument other)
    {
        foreach (var group in other.Groups)
        {
            var existing = Groups.Find(g =>
                string.Equals(g.Name, group.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Pages.AddRange(group.Pages);
            }
            else
            {
                Groups.Add(group);
            }
        }

        Widgets.AddRange(other.Widgets);

        foreach (var (key, definition) in other.Settings)
            Settings.TryAdd(key, definition);
    }
}
