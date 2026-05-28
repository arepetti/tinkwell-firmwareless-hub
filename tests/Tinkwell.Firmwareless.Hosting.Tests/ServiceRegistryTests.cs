using Tinkwell.Firmwareless.Hosting.Router.Ipc;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class ServiceRegistryTests
{
    [Fact]
    public void Register_and_find_by_full_name()
    {
        var registry = new ServiceRegistry();
        var msg = new RegisterService
        {
            FullName = "mesh.v1.MeshService",
            FamilyName = "MeshRouting",
            State = ServiceState.Ready,
        };
        msg.Methods.Add(new MethodInfo { Name = "Route", HandlerExport = "mesh_route", Bound = true });

        registry.Register("host-1", msg);

        var found = registry.FindByFullName("mesh.v1.MeshService");
        Assert.NotNull(found);
        Assert.Equal("host-1", found.HostId);
        Assert.Equal("mesh.v1.MeshService", found.FullName);
        Assert.Equal("MeshRouting", found.FamilyName);
        Assert.Equal(ServiceState.Ready, found.State);
        Assert.Single(found.Methods);
        Assert.Equal("Route", found.Methods[0].Name);
    }

    [Fact]
    public void Register_and_find_by_family()
    {
        var registry = new ServiceRegistry();
        var msg = new RegisterService
        {
            FullName = "mesh.v1.MeshService",
            FamilyName = "MeshRouting",
            State = ServiceState.Ready,
        };
        msg.Methods.Add(new MethodInfo { Name = "Route", HandlerExport = "mesh_route", Bound = true });

        registry.Register("host-1", msg);

        var found = registry.FindByFamily("MeshRouting");
        Assert.NotNull(found);
        Assert.Equal("mesh.v1.MeshService", found.FullName);
    }

    [Fact]
    public void Find_returns_null_for_unknown_service()
    {
        var registry = new ServiceRegistry();
        Assert.Null(registry.FindByFullName("unknown.Service"));
        Assert.Null(registry.FindByFamily("Unknown"));
    }

    [Fact]
    public void MarkUnavailable_updates_state()
    {
        var registry = new ServiceRegistry();
        var msg = new RegisterService
        {
            FullName = "svc.A",
            FamilyName = "FamilyA",
            State = ServiceState.Ready,
        };
        registry.Register("host-1", msg);

        registry.MarkUnavailable("host-1");

        var found = registry.FindByFullName("svc.A");
        Assert.NotNull(found);
        Assert.Equal(ServiceState.Unavailable, found.State);
    }

    [Fact]
    public void MarkUnavailable_does_not_affect_other_hosts()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "svc.A",
            FamilyName = "FamilyA",
            State = ServiceState.Ready,
        });
        registry.Register("host-2", new RegisterService
        {
            FullName = "svc.B",
            FamilyName = "FamilyB",
            State = ServiceState.Ready,
        });

        registry.MarkUnavailable("host-1");

        Assert.Equal(ServiceState.Unavailable, registry.FindByFullName("svc.A")!.State);
        Assert.Equal(ServiceState.Ready, registry.FindByFullName("svc.B")!.State);
    }

    [Fact]
    public void UnregisterAll_removes_services()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "svc.A",
            FamilyName = "FamilyA",
            State = ServiceState.Ready,
        });

        registry.UnregisterAll("host-1");

        Assert.Null(registry.FindByFullName("svc.A"));
        Assert.Null(registry.FindByFamily("FamilyA"));
    }

    [Fact]
    public void Exists_returns_true_for_registered_service()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "svc.A",
            State = ServiceState.Defined,
        });

        Assert.True(registry.Exists("svc.A"));
        Assert.False(registry.Exists("svc.B"));
    }

    [Fact]
    public void ExistsWithState_returns_state()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "svc.A",
            State = ServiceState.Ready,
        });

        var (exists, state) = registry.ExistsWithState("svc.A");
        Assert.True(exists);
        Assert.Equal(ServiceState.Ready, state);

        var (exists2, _) = registry.ExistsWithState("svc.B");
        Assert.False(exists2);
    }

    [Fact]
    public void ListAll_returns_all_services()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "svc.A",
            FamilyName = "FamilyA",
            State = ServiceState.Ready,
        });
        registry.Register("host-2", new RegisterService
        {
            FullName = "svc.B",
            FamilyName = "FamilyB",
            State = ServiceState.Defined,
        });

        var all = registry.ListAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.FullName == "svc.A");
        Assert.Contains(all, s => s.FullName == "svc.B");
    }

    [Fact]
    public void Find_by_family_or_full_name()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "mesh.v1.MeshService",
            FamilyName = "MeshRouting",
            State = ServiceState.Ready,
        });

        Assert.NotNull(registry.Find("mesh.v1.MeshService", byFamily: false));
        Assert.NotNull(registry.Find("MeshRouting", byFamily: true));
        Assert.Null(registry.Find("MeshRouting", byFamily: false));
    }

    [Fact]
    public void Latest_registration_wins()
    {
        var registry = new ServiceRegistry();
        registry.Register("host-1", new RegisterService
        {
            FullName = "svc.A",
            FamilyName = "Fam",
            State = ServiceState.Defined,
        });
        registry.Register("host-2", new RegisterService
        {
            FullName = "svc.A",
            FamilyName = "Fam",
            State = ServiceState.Ready,
        });

        var found = registry.FindByFullName("svc.A");
        Assert.NotNull(found);
        Assert.Equal("host-2", found.HostId);
        Assert.Equal(ServiceState.Ready, found.State);
    }
}
