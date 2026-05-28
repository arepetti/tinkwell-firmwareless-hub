using FluentAssertions;
using Tinkwell.Expressions;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;
using Tinkwell.Firmwareless.Hub.Ui.Expressions;

namespace Tinkwell.Firmwareless.Hub.Ui.Tests;

public class UiExpressionEngineTests
{
    [Fact]
    public async Task EvaluatesExpressionOnValueChange()
    {
        var engine = CreateEngine();

        var control = new UiControl
        {
            Id = "asset1/gauge/temp",
            AssetId = "asset1",
            Type = UiControlType.Gauge,
            Name = "temp",
            Properties = new()
            {
                ["value"] = new UiPropertyValue(null, "temperature"),
                ["min"] = new UiPropertyValue(0),
            }
        };

        var doc = new UiDocument();
        doc.Groups.Add(new UiGroup
        {
            Name = "test",
            Pages = [new UiPage
            {
                Name = "page",
                Children = [control]
            }]
        });

        engine.RegisterDocument(doc);

        var updates = await engine.OnValueChangedAsync("asset1", "temperature", 23.5);

        updates.Should().HaveCount(1);
        updates[0].ControlId.Should().Be("asset1/gauge/temp");
        updates[0].Property.Should().Be("value");
        updates[0].Value.Should().Be(23.5);
        control.Properties["value"].Value.Should().Be(23.5);
    }

    [Fact]
    public async Task DoesNotUpdateWhenValueUnchanged()
    {
        var engine = CreateEngine();

        var control = new UiControl
        {
            Id = "a1/gauge/temp",
            AssetId = "a1",
            Type = UiControlType.Gauge,
            Name = "temp",
            Properties = new()
            {
                ["value"] = new UiPropertyValue(null, "temperature"),
            }
        };

        var doc = new UiDocument();
        doc.Groups.Add(new UiGroup
        {
            Name = "test",
            Pages = [new UiPage { Name = "p", Children = [control] }]
        });

        engine.RegisterDocument(doc);

        await engine.OnValueChangedAsync("a1", "temperature", 20.0);
        var updates = await engine.OnValueChangedAsync("a1", "temperature", 20.0);

        updates.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAllPopulatesInitialValues()
    {
        var engine = CreateEngine();

        var control = new UiControl
        {
            Id = "a1/gauge/temp",
            AssetId = "a1",
            Type = UiControlType.Gauge,
            Name = "temp",
            Properties = new()
            {
                ["value"] = new UiPropertyValue(null, "temperature"),
            }
        };

        var doc = new UiDocument();
        doc.Groups.Add(new UiGroup
        {
            Name = "test",
            Pages = [new UiPage { Name = "p", Children = [control] }]
        });

        engine.RegisterDocument(doc);
        engine.GetOrCreateContext("a1").SetValue("temperature", 25.0);

        await engine.EvaluateAllAsync();

        control.Properties["value"].Value.Should().Be(25.0);
    }

    [Fact]
    public void ExtractsDependenciesFromExpression()
    {
        var deps = UiExpressionEngine.ExtractDependencies("temperature + setpoint * 2");
        deps.Should().Contain("temperature");
        deps.Should().Contain("setpoint");
        deps.Should().NotContain("2");
    }

    [Fact]
    public void ExcludesKeywordsFromDependencies()
    {
        var deps = UiExpressionEngine.ExtractDependencies("if(temperature > 0, true, false)");
        deps.Should().Contain("temperature");
        deps.Should().NotContain("if");
        deps.Should().NotContain("true");
        deps.Should().NotContain("false");
    }

    [Fact]
    public async Task MultipleExpressionsShareDependency()
    {
        var engine = CreateEngine();

        var gauge = new UiControl
        {
            Id = "a1/gauge/temp",
            AssetId = "a1",
            Type = UiControlType.Gauge,
            Name = "temp",
            Properties = new()
            {
                ["value"] = new UiPropertyValue(null, "temperature"),
                ["min"] = new UiPropertyValue(null, "temperature - 5"),
            }
        };

        var doc = new UiDocument();
        doc.Groups.Add(new UiGroup
        {
            Name = "test",
            Pages = [new UiPage { Name = "p", Children = [gauge] }]
        });

        engine.RegisterDocument(doc);

        var updates = await engine.OnValueChangedAsync("a1", "temperature", 20.0);

        updates.Should().HaveCount(2);
    }

    [Fact]
    public async Task IgnoresUpdatesForUnregisteredAssets()
    {
        var engine = CreateEngine();
        engine.RegisterDocument(new UiDocument());

        var updates = await engine.OnValueChangedAsync("unknown-asset", "temp", 10);

        updates.Should().BeEmpty();
    }

    private static UiExpressionEngine CreateEngine()
    {
        var evaluator = new ExpressionEvaluator();
        return new UiExpressionEngine(evaluator);
    }
}
