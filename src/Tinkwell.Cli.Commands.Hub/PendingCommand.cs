using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class PendingSettings : TwSettings
{
    [Description("Base directory where firmlet directories are stored")]
    [CommandOption("--firmlet-base-dir")]
    [DefaultValue(null)]
    public string? FirmletBaseDir { get; set; }

    public string ResolveFirmletBaseDir()
    {
        if (!string.IsNullOrWhiteSpace(FirmletBaseDir))
            return FirmletBaseDir;
        var env = Environment.GetEnvironmentVariable("TW_FIRMLET_BASE_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return env;
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tinkwell", "Firmlets")
            : "/var/lib/tinkwell/firmlets";
    }
}

/// <summary>
/// Lists firmlet downloads that are pending retry.
/// </summary>
[CliCommand("hub", "pending", Description = "List pending firmlet downloads awaiting retry")]
public sealed class PendingCommand : AsyncCommand<PendingSettings>
{
    private static readonly ColumnDef<PendingEntry>[] Columns =
    [
        new("Asset ID", e => e.AssetId),
        new("Firmlet", e => e.FirmletName),
        new("Version", e => e.FirmletVersion ?? "latest"),
        new("Attempts", e => e.AttemptCount.ToString()),
        new("Last Attempt", e => e.LastAttemptUtc?.ToString("u") ?? "-"),
        new("Error", e => Truncate(e.LastError ?? "-", 50)),
        new("Queued", e => e.CreatedUtc.ToString("u"), VerboseOnly: true),
    ];

    public override async Task<int> ExecuteAsync(
        CommandContext context, PendingSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);
        var filePath = Path.Combine(settings.ResolveFirmletBaseDir(), "pending-downloads.json");

        if (!File.Exists(filePath))
        {
            output.WriteMarkup("[dim]No pending downloads[/]");
            return 0;
        }

        var json = await File.ReadAllTextAsync(filePath, ct);
        var entries = JsonSerializer.Deserialize<List<PendingEntry>>(json, JsonOpts) ?? [];

        if (entries.Count == 0)
        {
            output.WriteMarkup("[dim]No pending downloads[/]");
            return 0;
        }

        output.WriteTable("Pending Downloads", Columns, entries);
        return 0;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class PendingEntry
    {
        public string AssetId { get; set; } = "";
        public string FirmletName { get; set; } = "";
        public string? FirmletVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
        public int AttemptCount { get; set; }
        public string? LastError { get; set; }
    }
}
