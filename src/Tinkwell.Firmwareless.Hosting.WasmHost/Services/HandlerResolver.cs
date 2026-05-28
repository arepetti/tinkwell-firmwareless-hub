using System.Text;
using System.Text.RegularExpressions;

namespace Tinkwell.Firmwareless.Hosting.WasmHost.Services;

public static class HandlerResolver
{
    private static readonly Regex s_placeholder = new(
        @"\{(\w+)(?::(\w+))?\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns the default export name when no handler template is specified:
    /// <c>{ShortServiceName}_{MethodName}</c>, where the short name is the
    /// last dot-segment of the fully-qualified service name.
    /// </summary>
    public static string DefaultExportName(string fullServiceName, string methodName)
    {
        var lastDot = fullServiceName.LastIndexOf('.');
        var shortName = lastDot >= 0 ? fullServiceName[(lastDot + 1)..] : fullServiceName;
        return $"{shortName}_{methodName}";
    }

    /// <summary>
    /// Expand a handler template like "mesh_{name:lowercase}" for a given method.
    /// </summary>
    public static string ExpandTemplate(string template, string methodName, string serviceName,
        string? familyName, string? moduleName = null)
    {
        var result = template;
        result = ExpandVariable(result, "name", methodName);
        result = ExpandVariable(result, "service", serviceName);
        result = ExpandVariable(result, "family", familyName ?? "");
        result = ExpandVariable(result, "module", moduleName ?? "");
        return result;
    }

    private static string ExpandVariable(string template, string varName, string value)
    {
        return s_placeholder.Replace(template, m =>
        {
            if (!string.Equals(m.Groups[1].Value, varName, StringComparison.Ordinal))
                return m.Value;

            var modifier = m.Groups[2].Success ? m.Groups[2].Value : "";
            return ApplyModifier(value, modifier);
        });
    }

    private static string ApplyModifier(string value, string modifier)
    {
        return modifier.ToLowerInvariant() switch
        {
            "" => value,
            "lowercase" => value.ToLowerInvariant(),
            "uppercase" => value.ToUpperInvariant(),
            "pascalcase" => ToPascalCase(value),
            _ => value,
        };
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var parts = value.Split(['_', ' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return value;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0)
                continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                sb.Append(part.Substring(1).ToLowerInvariant());
        }

        return sb.Length > 0 ? sb.ToString() : value;
    }

    /// <summary>
    /// Resolve all explicitly declared methods in a service definition.
    /// </summary>
    public static void ResolveHandlers(ServiceDefinition service)
    {
        foreach (var method in service.Methods)
        {
            if (method.HandlerOverride is not null)
            {
                method.ResolvedExport = method.HandlerOverride;
            }
            else if (service.HandlerTemplate is not null)
            {
                method.ResolvedExport = ExpandTemplate(
                    service.HandlerTemplate, method.Name,
                    service.FullName, service.Family, service.Module);
            }
            else
            {
                method.ResolvedExport = DefaultExportName(service.FullName, method.Name);
            }

            // IsBound is set later when we verify the WASM exports exist
        }
    }

    /// <summary>
    /// Derives the WASM export name for a method that was not explicitly
    /// declared in <c>services.tw</c>. Uses the handler template when
    /// available, otherwise falls back to <see cref="DefaultExportName"/>.
    /// </summary>
    public static string ResolveAtCallTime(ServiceDefinition service, string methodName)
    {
        if (service.HandlerTemplate is not null)
        {
            return ExpandTemplate(
                service.HandlerTemplate, methodName,
                service.FullName, service.Family, service.Module);
        }

        return DefaultExportName(service.FullName, methodName);
    }
}
