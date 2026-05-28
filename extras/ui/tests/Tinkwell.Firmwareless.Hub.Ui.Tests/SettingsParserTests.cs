using FluentAssertions;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;

namespace Tinkwell.Firmwareless.Hub.Ui.Tests;

public class SettingsParserTests
{
    [Fact]
    public async Task ParsesNumberSetting()
    {
        const string tw = """
            setting "setpoint" {
                type = "number"
                default = 21
                min = 16
                max = 30
                step = 0.5
                unit = "°C"
                description = "Target temperature"
            }
            """;

        var settings = await ParseAsync(tw);

        settings.Should().ContainKey("setpoint");
        var def = settings["setpoint"];
        def.Type.Should().Be(SettingType.Number);
        def.Default.Should().Be(21L);
        def.Min.Should().Be(16);
        def.Max.Should().Be(30);
        def.Step.Should().Be(0.5);
        def.Unit.Should().Be("°C");
        def.Description.Should().Be("Target temperature");
    }

    [Fact]
    public async Task ParsesBoolSetting()
    {
        const string tw = """
            setting "schedule-enabled" {
                type = "bool"
                default = false
                description = "Enable scheduling"
            }
            """;

        var settings = await ParseAsync(tw);

        var def = settings["schedule-enabled"];
        def.Type.Should().Be(SettingType.Bool);
        def.Default.Should().Be(false);
    }

    [Fact]
    public async Task ParsesStringSetting()
    {
        const string tw = """
            setting "zone-name" {
                type = "string"
                default = "Living Room"
                max-length = 64
                description = "Zone display name"
            }
            """;

        var settings = await ParseAsync(tw);

        var def = settings["zone-name"];
        def.Type.Should().Be(SettingType.String);
        def.Default.Should().Be("Living Room");
        def.MaxLength.Should().Be(64);
    }

    [Fact]
    public async Task ParsesMultipleSettings()
    {
        const string tw = """
            setting "temp" {
                type = "number"
                min = 0
                max = 50
            }

            setting "mode" {
                type = "string"
            }

            setting "enabled" {
                type = "bool"
                default = true
            }
            """;

        var settings = await ParseAsync(tw);
        settings.Should().HaveCount(3);
    }

    [Fact]
    public async Task ParsesObjectSetting()
    {
        const string tw = """
            setting "weekly-schedule" {
                type = "object"
                schema = "weekly-schedule"
                description = "Heating schedule"
            }
            """;

        var settings = await ParseAsync(tw);

        var def = settings["weekly-schedule"];
        def.Type.Should().Be(SettingType.Object);
        def.Schema.Should().Be("weekly-schedule");
    }

    private static async Task<IReadOnlyDictionary<string, SettingDefinition>> ParseAsync(string tw)
    {
        var files = new Dictionary<string, string> { ["settings.tw"] = tw };
        var fileProvider = new InMemoryFileProvider(files);
        var parser = new SettingsParser();
        return await parser.LoadAsync(fileProvider, "settings.tw");
    }
}
