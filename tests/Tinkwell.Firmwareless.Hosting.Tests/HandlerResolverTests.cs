using Tinkwell.Firmwareless.Hosting.WasmHost.Services;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class HandlerResolverTests
{
    [Fact]
    public void ExpandTemplate_lowercase()
    {
        var result = HandlerResolver.ExpandTemplate(
            "mesh_{name:lowercase}", "Route", "mesh.v1.MeshService", "MeshRouting");
        Assert.Equal("mesh_route", result);
    }

    [Fact]
    public void ExpandTemplate_uppercase()
    {
        var result = HandlerResolver.ExpandTemplate(
            "MESH_{name:uppercase}", "Route", "mesh.v1.MeshService", "MeshRouting");
        Assert.Equal("MESH_ROUTE", result);
    }

    [Fact]
    public void ExpandTemplate_pascalcase()
    {
        var result = HandlerResolver.ExpandTemplate(
            "handle_{name:pascalcase}", "get_status", "svc", "fam");
        Assert.Equal("handle_GetStatus", result);
    }

    [Fact]
    public void ExpandTemplate_no_modifier()
    {
        var result = HandlerResolver.ExpandTemplate(
            "handle_{name}", "DoStuff", "svc", "fam");
        Assert.Equal("handle_DoStuff", result);
    }

    [Fact]
    public void ExpandTemplate_multiple_variables()
    {
        var result = HandlerResolver.ExpandTemplate(
            "{family:lowercase}_{name:lowercase}", "Route", "mesh.v1.MeshService", "MeshRouting");
        Assert.Equal("meshrouting_route", result);
    }

    [Fact]
    public void ExpandTemplate_service_variable()
    {
        var result = HandlerResolver.ExpandTemplate(
            "{service:lowercase}_{name:lowercase}", "Route", "MeshService", null);
        Assert.Equal("meshservice_route", result);
    }

    [Fact]
    public void ResolveHandlers_uses_template_for_plain_methods()
    {
        var svc = new ServiceDefinition
        {
            FullName = "mesh.v1.MeshService",
            Family = "MeshRouting",
            HandlerTemplate = "mesh_{name:lowercase}",
            Methods =
            [
                new MethodDefinition { Name = "Route" },
                new MethodDefinition { Name = "Discover" },
            ],
        };

        HandlerResolver.ResolveHandlers(svc);

        Assert.Equal("mesh_route", svc.Methods[0].ResolvedExport);
        Assert.Equal("mesh_discover", svc.Methods[1].ResolvedExport);
    }

    [Fact]
    public void ResolveHandlers_uses_override_when_specified()
    {
        var svc = new ServiceDefinition
        {
            FullName = "mesh.v1.MeshService",
            Family = "MeshRouting",
            HandlerTemplate = "mesh_{name:lowercase}",
            Methods =
            [
                new MethodDefinition { Name = "Route" },
                new MethodDefinition { Name = "LegacyPing", HandlerOverride = "do_ping" },
            ],
        };

        HandlerResolver.ResolveHandlers(svc);

        Assert.Equal("mesh_route", svc.Methods[0].ResolvedExport);
        Assert.Equal("do_ping", svc.Methods[1].ResolvedExport);
    }

    [Fact]
    public void ExpandTemplate_module_variable()
    {
        var result = HandlerResolver.ExpandTemplate(
            "{module:lowercase}_{name:lowercase}", "Route", "mesh.v1.MeshService", "MeshRouting", "mesh-bridge");
        Assert.Equal("mesh-bridge_route", result);
    }

    [Fact]
    public void ResolveHandlers_includes_module_in_expansion()
    {
        var svc = new ServiceDefinition
        {
            FullName = "svc",
            Module = "core",
            HandlerTemplate = "{module}_{name:lowercase}",
            Methods = [new MethodDefinition { Name = "Foo" }],
        };

        HandlerResolver.ResolveHandlers(svc);
        Assert.Equal("core_foo", svc.Methods[0].ResolvedExport);
    }

    [Fact]
    public void ResolveHandlers_without_template_uses_default()
    {
        var svc = new ServiceDefinition
        {
            FullName = "mesh.v1.MeshService",
            Methods =
            [
                new MethodDefinition { Name = "Ping" },
            ],
        };

        HandlerResolver.ResolveHandlers(svc);

        Assert.Equal("MeshService_Ping", svc.Methods[0].ResolvedExport);
    }

    [Fact]
    public void DefaultExportName_uses_last_dot_segment()
    {
        Assert.Equal("MeshService_Ping", HandlerResolver.DefaultExportName("mesh.v1.MeshService", "Ping"));
    }

    [Fact]
    public void DefaultExportName_no_dots()
    {
        Assert.Equal("svc_Foo", HandlerResolver.DefaultExportName("svc", "Foo"));
    }

    [Fact]
    public void ResolveAtCallTime_expands_template()
    {
        var svc = new ServiceDefinition
        {
            FullName = "mesh.v1.MeshService",
            Family = "MeshRouting",
            HandlerTemplate = "mesh_{name:lowercase}",
            Methods = [],
        };

        var export = HandlerResolver.ResolveAtCallTime(svc, "Route");
        Assert.Equal("mesh_route", export);
    }

    [Fact]
    public void ResolveAtCallTime_uses_default_without_template()
    {
        var svc = new ServiceDefinition
        {
            FullName = "mesh.v1.MeshService",
            Methods = [],
        };

        var export = HandlerResolver.ResolveAtCallTime(svc, "Ping");
        Assert.Equal("MeshService_Ping", export);
    }

    [Fact]
    public void ResolveAtCallTime_includes_module()
    {
        var svc = new ServiceDefinition
        {
            FullName = "svc",
            Module = "core",
            HandlerTemplate = "{module}_{name:lowercase}",
            Methods = [],
        };

        var export = HandlerResolver.ResolveAtCallTime(svc, "Bar");
        Assert.Equal("core_bar", export);
    }
}
