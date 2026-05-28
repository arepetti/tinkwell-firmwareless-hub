using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;

namespace Tinkwell.Firmwareless.Hub.Ui.Tests;

public class UiConfigParserTests
{
    [Fact]
    public async Task ParsesGroupWithPagesAndControls()
    {
        const string tw = """
            group "climate" {
                label = "Climate"
                icon = "thermometer"
                order = 10

                page "living-room" {
                    label = "Living Room"
                    icon = "sofa"

                    gauge "temp" {
                        label = "Temperature"
                        value = (temperature)
                        unit = "°C"
                        min = 0
                        max = 50
                        precision = 1
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw);

        doc.Groups.Should().HaveCount(1);
        var group = doc.Groups[0];
        group.Name.Should().Be("climate");
        group.Label!.Value.Should().Be("Climate");
        group.Icon!.Value.Should().Be("thermometer");
        group.Order.Should().Be(10);

        group.Pages.Should().HaveCount(1);
        var page = group.Pages[0];
        page.Name.Should().Be("living-room");
        page.Label!.Value.Should().Be("Living Room");

        page.Children.Should().HaveCount(1);
        var gauge = page.Children[0].Should().BeOfType<UiControl>().Subject;
        gauge.Type.Should().Be(UiControlType.Gauge);
        gauge.Name.Should().Be("temp");
        gauge.Properties["label"].Value.Should().Be("Temperature");
        gauge.Properties["value"].Expression.Should().Be("temperature");
        gauge.Properties["unit"].Value.Should().Be("°C");
        gauge.Properties["min"].Value.Should().Be(0L);
        gauge.Properties["max"].Value.Should().Be(50L);
        gauge.Properties["precision"].Value.Should().Be(1L);
    }

    [Fact]
    public async Task ParsesWidgetWithLayout()
    {
        const string tw = """
            widget "climate-summary" {
                label = "Climate Summary"
                icon = "thermometer"

                hstack "row" {
                    gauge "temp" {
                        value = (temperature)
                    }

                    indicator "mode" {
                        value = (hvac-mode)
                        map "entries" {
                            entry "off"  { label = "Off"  color = "gray" }
                            entry "heat" { label = "Heat" color = "orange" }
                        }
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw);

        doc.Widgets.Should().HaveCount(1);
        var widget = doc.Widgets[0];
        widget.Name.Should().Be("climate-summary");
        widget.Label!.Value.Should().Be("Climate Summary");

        widget.Children.Should().HaveCount(1);
        var hstack = widget.Children[0].Should().BeOfType<UiLayout>().Subject;
        hstack.Type.Should().Be(UiLayoutType.HStack);
        hstack.Children.Should().HaveCount(2);

        var indicator = hstack.Children[1].Should().BeOfType<UiControl>().Subject;
        indicator.Type.Should().Be(UiControlType.Indicator);
        indicator.Map.Should().NotBeNull();
        indicator.Map!.Should().ContainKey("off");
        indicator.Map["off"]["label"].Should().Be("Off");
        indicator.Map["off"]["color"].Should().Be("gray");
    }

    [Fact]
    public async Task ParsesThemeBlock()
    {
        const string tw = """
            ui "home" {
                title = "My Home"
                theme = "light"
                accent = "#FF5722"
                locale = "it-IT"
            }
            """;

        var doc = await ParseAsync(tw);

        doc.Theme.Title.Should().Be("My Home");
        doc.Theme.Theme.Should().Be("light");
        doc.Theme.Accent.Should().Be("#FF5722");
        doc.Theme.Locale.Should().Be("it-IT");
    }

    [Fact]
    public async Task ParsesInteractiveControls()
    {
        const string tw = """
            group "test" {
                page "controls" {
                    button "set-temp" {
                        label = "Set Temperature"
                        setting = "setpoint"
                        input = "number"
                        min = 16
                        max = 30
                        step = 0.5
                    }

                    toggle "lights" {
                        setting = "lights-on"
                        label = "Living Room Lights"
                    }

                    slider "dimmer" {
                        setting = "brightness"
                        min = 0
                        max = 100
                        step = 1
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw);
        var page = doc.Groups[0].Pages[0];
        page.Children.Should().HaveCount(3);

        var button = page.Children[0].Should().BeOfType<UiControl>().Subject;
        button.Type.Should().Be(UiControlType.Button);
        button.Properties["setting"].Value.Should().Be("setpoint");
        button.Properties["input"].Value.Should().Be("number");
        button.Properties["step"].Value.Should().Be(0.5);

        var toggle = page.Children[1].Should().BeOfType<UiControl>().Subject;
        toggle.Type.Should().Be(UiControlType.Toggle);
        toggle.Properties["setting"].Value.Should().Be("lights-on");

        var slider = page.Children[2].Should().BeOfType<UiControl>().Subject;
        slider.Type.Should().Be(UiControlType.Slider);
        slider.Properties["setting"].Value.Should().Be("brightness");
    }

    [Fact]
    public async Task ParsesGridWithColumns()
    {
        const string tw = """
            group "test" {
                page "test-page" {
                    grid "main" {
                        columns = 2

                        text "header" {
                            value = "Welcome"
                            style = "heading"
                        }

                        value "reading" {
                            value = (temperature)
                            unit = "°C"
                            precision = 1
                        }
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw);
        var page = doc.Groups[0].Pages[0];
        var grid = page.Children[0].Should().BeOfType<UiLayout>().Subject;
        grid.Type.Should().Be(UiLayoutType.Grid);
        grid.Properties["columns"].Should().Be(2L);
        grid.Children.Should().HaveCount(2);

        var text = grid.Children[0].Should().BeOfType<UiControl>().Subject;
        text.Type.Should().Be(UiControlType.Text);
        text.Properties["value"].Value.Should().Be("Welcome");

        var val = grid.Children[1].Should().BeOfType<UiControl>().Subject;
        val.Type.Should().Be(UiControlType.Value);
        val.Properties["value"].Expression.Should().Be("temperature");
    }

    [Fact]
    public async Task ParsesExpressionProperties()
    {
        const string tw = """
            group "test" {
                page "test-page" {
                    gauge "temp" {
                        value = (temperature)
                        min = (setpoint - 5)
                        max = (setpoint + 5)
                        color = (if(temperature > 30, 'red', 'green'))
                        enabled = (hvac-mode != 'off')
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw);
        var gauge = doc.Groups[0].Pages[0].Children[0].Should().BeOfType<UiControl>().Subject;

        gauge.Properties["value"].IsExpression.Should().BeTrue();
        gauge.Properties["value"].Expression.Should().Be("temperature");
        gauge.Properties["min"].Expression.Should().Be("setpoint - 5");
        gauge.Properties["max"].Expression.Should().Be("setpoint + 5");
        gauge.Properties["color"].Expression.Should().Contain("if");
        gauge.Properties["enabled"].Expression.Should().Contain("hvac-mode");
    }

    [Fact]
    public async Task MergesDocuments()
    {
        const string tw1 = """
            group "climate" {
                page "room-a" { }
            }
            """;

        const string tw2 = """
            group "climate" {
                page "room-b" { }
            }
            """;

        var doc1 = await ParseAsync(tw1);
        var doc2 = await ParseAsync(tw2, "asset-2");
        doc1.Merge(doc2);

        doc1.Groups.Should().HaveCount(1);
        doc1.Groups[0].Pages.Should().HaveCount(2);
    }

    [Fact]
    public async Task BuildsStructuralPathIds()
    {
        const string tw = """
            group "climate" {
                page "living-room" {
                    grid "main" {
                        gauge "temp" {
                            value = (temperature)
                        }
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw, "hvac-1");
        var grid = doc.Groups[0].Pages[0].Children[0].Should().BeOfType<UiLayout>().Subject;
        grid.Id.Should().Be("hvac-1/climate/living-room/main");

        var gauge = grid.Children[0].Should().BeOfType<UiControl>().Subject;
        gauge.Id.Should().Be("hvac-1/climate/living-room/main/temp");
    }

    [Fact]
    public async Task AssignsAssetId()
    {
        const string tw = """
            group "climate" {
                page "room" {
                    gauge "temp" {
                        value = (temperature)
                    }
                }
            }
            """;

        var doc = await ParseAsync(tw, "hvac-1");
        doc.Groups[0].AssetId.Should().Be("hvac-1");
        doc.Groups[0].Pages[0].AssetId.Should().Be("hvac-1");

        var gauge = doc.Groups[0].Pages[0].Children[0].Should().BeOfType<UiControl>().Subject;
        gauge.AssetId.Should().Be("hvac-1");
    }

    private static async Task<UiDocument> ParseAsync(string tw, string? assetId = null)
    {
        var files = new Dictionary<string, string> { ["ui.tw"] = tw };
        var fileProvider = new InMemoryFileProvider(files);
        var parser = new UiConfigParser(assetId);
        return await parser.LoadAsync(fileProvider, "ui.tw");
    }
}
