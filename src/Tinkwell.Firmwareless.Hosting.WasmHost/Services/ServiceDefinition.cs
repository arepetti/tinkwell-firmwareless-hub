namespace Tinkwell.Firmwareless.Hosting.WasmHost.Services;

public enum ModuleLoadPolicy
{
    Startup,
    OnDemand,
}

public sealed class ServiceDefinition
{
    public required string FullName { get; init; }
    public string? Family { get; init; }
    public string? Description { get; init; }
    public string? Module { get; init; }
    public ModuleLoadPolicy LoadPolicy { get; init; } = ModuleLoadPolicy.Startup;
    public string? HandlerTemplate { get; init; }
    public List<string> DependsOn { get; init; } = [];
    public required List<MethodDefinition> Methods { get; init; }
}

public sealed class MethodDefinition
{
    public required string Name { get; init; }
    public string? HandlerOverride { get; init; }

    public string? ResolvedExport { get; set; }
    public bool IsBound { get; set; }
}
