namespace Tinkwell.Runlet.Firmwareless.AssetRegistry.Data;

/// <summary>
/// A permission row: <see cref="PermissionType"/> distinguishes measure vs service vs other;
/// <see cref="Target"/> is the allowed id or name (empty string when not applicable).
/// </summary>
public readonly record struct AssetPermission(string PermissionType, string Target);
