using Tinkwell.Firmwareless.Hosting.WasmHost.Services;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class ServicesTwParserTests
{
    [Fact]
    public void Parse_basic_service_with_methods()
    {
        var content = """
            service "mesh.v1.MeshService" {
                family="MeshRouting"
                description="Routes mesh network messages between devices"
                handler="mesh_{name:lowercase}"

                depends_on "device.v1.DeviceService"

                method Route;
                method Discover;
                method LegacyPing handler="do_ping";
            }
            """;

        var services = ServicesTwParser.Parse(content);

        Assert.Single(services);
        var svc = services[0];
        Assert.Equal("mesh.v1.MeshService", svc.FullName);
        Assert.Equal("MeshRouting", svc.Family);
        Assert.Equal("Routes mesh network messages between devices", svc.Description);
        Assert.Equal("mesh_{name:lowercase}", svc.HandlerTemplate);
        Assert.Single(svc.DependsOn);
        Assert.Equal("device.v1.DeviceService", svc.DependsOn[0]);
        Assert.Equal(3, svc.Methods.Count);
        Assert.Equal("Route", svc.Methods[0].Name);
        Assert.Null(svc.Methods[0].HandlerOverride);
        Assert.Equal("Discover", svc.Methods[1].Name);
        Assert.Null(svc.Methods[1].HandlerOverride);
        Assert.Equal("LegacyPing", svc.Methods[2].Name);
        Assert.Equal("do_ping", svc.Methods[2].HandlerOverride);
    }

    [Fact]
    public void Parse_multiple_services()
    {
        var content = """
            service "svc.A" {
                family="FamilyA"
                method Foo;
            }
            service "svc.B" {
                family="FamilyB"
                method Bar;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Equal(2, services.Count);
        Assert.Equal("svc.A", services[0].FullName);
        Assert.Equal("svc.B", services[1].FullName);
    }

    [Fact]
    public void Parse_strips_line_comments()
    {
        var content = """
            // A service
            service "my.Service" {
                family="Test" // inline comment
                method Foo; // method comment
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        Assert.Equal("Test", services[0].Family);
        Assert.Single(services[0].Methods);
    }

    [Fact]
    public void Parse_strips_block_comments()
    {
        var content = """
            /* header comment */
            service "my.Service" {
                family="Test"
                /* method Bar; -- removed */
                method Foo;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        Assert.Single(services[0].Methods);
        Assert.Equal("Foo", services[0].Methods[0].Name);
    }

    [Fact]
    public void Parse_method_with_unquoted_name()
    {
        var content = """
            service "svc" {
                method Route;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Equal("Route", services[0].Methods[0].Name);
    }

    [Fact]
    public void Parse_method_with_quoted_name()
    {
        var content = """
            service "svc" {
                method "Route";
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Equal("Route", services[0].Methods[0].Name);
    }

    [Fact]
    public void Parse_empty_service_body()
    {
        var content = """
            service "empty.Service" {
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        Assert.Empty(services[0].Methods);
        Assert.Null(services[0].Family);
    }

    [Fact]
    public void Parse_multiple_depends_on()
    {
        var content = """
            service "svc" {
                depends_on "a.Service";
                depends_on "b.Service";
                method Foo;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Equal(2, services[0].DependsOn.Count);
        Assert.Contains("a.Service", services[0].DependsOn);
        Assert.Contains("b.Service", services[0].DependsOn);
    }

    [Fact]
    public void Parse_module_and_load_startup()
    {
        var content = """
            service "svc" {
                module="my-module"
                load="startup"
                method Foo;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        Assert.Equal("my-module", services[0].Module);
        Assert.Equal(ModuleLoadPolicy.Startup, services[0].LoadPolicy);
    }

    [Fact]
    public void Parse_module_and_load_on_demand()
    {
        var content = """
            service "svc" {
                module="analytics"
                load="on-demand"
                method Aggregate;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        Assert.Equal("analytics", services[0].Module);
        Assert.Equal(ModuleLoadPolicy.OnDemand, services[0].LoadPolicy);
    }

    [Fact]
    public void Parse_default_load_policy_is_startup()
    {
        var content = """
            service "svc" {
                module="core"
                method Foo;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Equal(ModuleLoadPolicy.Startup, services[0].LoadPolicy);
    }

    [Fact]
    public void Parse_no_module_returns_null()
    {
        var content = """
            service "svc" {
                method Foo;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Null(services[0].Module);
    }

    [Fact]
    public void Parse_multi_module_firmlet()
    {
        var content = """
            service "mesh.v1.MeshService" {
                module="mesh-bridge"
                family="MeshRouting"
                handler="mesh_{name:lowercase}"
                method Route;
                method Discover;
            }
            service "analytics.v1.AnalyticsService" {
                module="analytics"
                load="on-demand"
                family="Analytics"
                handler="analytics_{name:lowercase}"
                method Aggregate;
                method Summarize;
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Equal(2, services.Count);

        Assert.Equal("mesh-bridge", services[0].Module);
        Assert.Equal(ModuleLoadPolicy.Startup, services[0].LoadPolicy);
        Assert.Equal(2, services[0].Methods.Count);

        Assert.Equal("analytics", services[1].Module);
        Assert.Equal(ModuleLoadPolicy.OnDemand, services[1].LoadPolicy);
        Assert.Equal(2, services[1].Methods.Count);
    }

    [Fact]
    public void Parse_service_with_handler_but_no_methods()
    {
        var content = """
            service "mesh.v1.MeshService" {
                family="MeshRouting"
                handler="mesh_{name:lowercase}"
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        var svc = services[0];
        Assert.Equal("mesh.v1.MeshService", svc.FullName);
        Assert.Equal("MeshRouting", svc.Family);
        Assert.Equal("mesh_{name:lowercase}", svc.HandlerTemplate);
        Assert.Empty(svc.Methods);
    }

    [Fact]
    public void Parse_service_with_partial_methods()
    {
        var content = """
            service "mesh.v1.MeshService" {
                handler="mesh_{name:lowercase}"
                method LegacyPing handler="do_ping";
            }
            """;

        var services = ServicesTwParser.Parse(content);
        Assert.Single(services);
        Assert.Single(services[0].Methods);
        Assert.Equal("LegacyPing", services[0].Methods[0].Name);
        Assert.Equal("do_ping", services[0].Methods[0].HandlerOverride);
    }

    [Fact]
    public void Parse_throws_on_unclosed_brace()
    {
        var content = """
            service "svc" {
                method Foo;
            """;

        Assert.Throws<FormatException>(() => ServicesTwParser.Parse(content));
    }
}
