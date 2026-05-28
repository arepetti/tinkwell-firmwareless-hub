using FluentAssertions;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;

namespace Tinkwell.Firmwareless.Hub.Ui.Tests;

public class UiConfigDiscoveryTests
{
    [Fact]
    public async Task LoadsUiAndSettingsFromInMemoryFiles()
    {
        var files = new Dictionary<string, string>
        {
            ["ui.tw"] = """
                widget "summary" {
                    label = "Summary"
                    gauge "temp" {
                        value = (temperature)
                    }
                }

                group "climate" {
                    page "room" {
                        slider "dimmer" {
                            setting = "brightness"
                        }
                    }
                }
                """,
            ["settings.tw"] = """
                setting "brightness" {
                    type = "number"
                    default = 50
                    min = 0
                    max = 100
                }
                """
        };

        var doc = await UiConfigDiscovery.LoadFromFilesAsync("hvac-1", files);

        doc.Widgets.Should().HaveCount(1);
        doc.Widgets[0].Name.Should().Be("summary");

        doc.Groups.Should().HaveCount(1);
        doc.Groups[0].Pages[0].Children.Should().HaveCount(1);

        doc.Settings.Should().ContainKey("brightness");
        doc.Settings["brightness"].Type.Should().Be(SettingType.Number);
        doc.Settings["brightness"].Min.Should().Be(0);
        doc.Settings["brightness"].Max.Should().Be(100);
    }

    [Fact]
    public async Task HandlesUiOnlyPackage()
    {
        var files = new Dictionary<string, string>
        {
            ["ui.tw"] = """
                widget "status" {
                    label = "Status"
                }
                """
        };

        var doc = await UiConfigDiscovery.LoadFromFilesAsync("sensor-1", files);

        doc.Widgets.Should().HaveCount(1);
        doc.Settings.Should().BeEmpty();
    }

    [Fact]
    public async Task HandlesSettingsOnlyPackage()
    {
        var files = new Dictionary<string, string>
        {
            ["settings.tw"] = """
                setting "interval" {
                    type = "number"
                    default = 30
                }
                """
        };

        var doc = await UiConfigDiscovery.LoadFromFilesAsync("poller-1", files);

        doc.Widgets.Should().BeEmpty();
        doc.Groups.Should().BeEmpty();
        doc.Settings.Should().ContainKey("interval");
    }
}
