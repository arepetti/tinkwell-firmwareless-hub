using System.Collections.Concurrent;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

namespace Tinkwell.Firmwareless.Hosting.Router.Ipc;

public sealed class ServiceRegistry
{
    private readonly ConcurrentDictionary<string, ServiceRegistration> _byFullName = new();
    private readonly ConcurrentDictionary<string, ServiceRegistration> _byFamily = new();

    public void Register(string hostId, RegisterService msg)
    {
        var reg = new ServiceRegistration(
            msg.FullName, msg.FamilyName, hostId,
            msg.Methods.ToList().AsReadOnly(), msg.State);
        _byFullName[msg.FullName] = reg;
        if (!string.IsNullOrEmpty(msg.FamilyName))
            _byFamily[msg.FamilyName] = reg;
    }

    public ServiceRegistration? FindByFullName(string fullName) =>
        _byFullName.GetValueOrDefault(fullName);

    public ServiceRegistration? FindByFamily(string familyName) =>
        _byFamily.GetValueOrDefault(familyName);

    public ServiceRegistration? Find(string name, bool byFamily) =>
        byFamily ? FindByFamily(name) : FindByFullName(name);

    public void MarkUnavailable(string hostId)
    {
        foreach (var reg in _byFullName.Values.Where(r => r.HostId == hostId))
        {
            var updated = reg with { State = ServiceState.Unavailable };
            _byFullName[reg.FullName] = updated;
            if (!string.IsNullOrEmpty(reg.FamilyName))
                _byFamily[reg.FamilyName] = updated;
        }
    }

    public void UnregisterAll(string hostId)
    {
        foreach (var reg in _byFullName.Values.Where(r => r.HostId == hostId).ToList())
        {
            _byFullName.TryRemove(reg.FullName, out _);
            if (!string.IsNullOrEmpty(reg.FamilyName))
                _byFamily.TryRemove(reg.FamilyName, out _);
        }
    }

    public (bool Exists, ServiceState State) ExistsWithState(string name)
    {
        if (_byFullName.TryGetValue(name, out var reg))
            return (true, reg.State);
        if (_byFamily.TryGetValue(name, out reg))
            return (true, reg.State);
        return (false, ServiceState.Defined);
    }

    public bool Exists(string name) => ExistsWithState(name).Exists;

    public IReadOnlyList<ServiceInfo> ListAll() =>
        _byFullName.Values.Select(r => new ServiceInfo
        {
            FullName = r.FullName,
            FamilyName = r.FamilyName,
            HostId = r.HostId,
            State = r.State,
        }).ToList();

    public record ServiceRegistration(
        string FullName,
        string FamilyName,
        string HostId,
        IReadOnlyList<MethodInfo> Methods,
        ServiceState State);
}
