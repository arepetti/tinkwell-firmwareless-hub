using System.ComponentModel;
using Grpc.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Tinkwell.Cli;
using Tinkwell.Cli.Commands;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;

namespace Tinkwell.Cli.Commands.Hub;

public sealed class LogSettings : HubSettings
{
    [Description("Asset ID(s) to stream logs for (comma-separated), or '*' for all assets. Omit for system logs only.")]
    [CommandArgument(0, "[asset-filter]")]
    [DefaultValue(null)]
    public string? AssetFilter { get; set; }
}

/// <summary>
/// Streams live logs from the firmwareless hub.
/// <list type="bullet">
/// <item><c>tw hub log</c> -- system/internal logs only</item>
/// <item><c>tw hub log &lt;asset-id&gt;</c> -- logs from a specific asset</item>
/// <item><c>tw hub log *</c> -- logs from all assets</item>
/// </list>
/// </summary>
[CliCommand("hub", "log", Description = "Stream live logs from the firmwareless hub")]
public sealed class LogCommand : AsyncCommand<LogSettings>
{
    private static readonly string[] LogLevelNames = ["TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"];

    public override async Task<int> ExecuteAsync(
        CommandContext context, LogSettings settings, CancellationToken ct)
    {
        var output = new OutputContext(settings);
        using var grpc = GrpcFactory.FromSettings(settings);

        var request = new StreamLogsRequest();

        if (settings.AssetFilter == "*")
        {
            request.AllAssets = true;
            output.WriteMarkup("[dim]Streaming logs from all assets (Ctrl+C to stop)...[/]");
        }
        else if (!string.IsNullOrWhiteSpace(settings.AssetFilter))
        {
            foreach (var id in settings.AssetFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                request.AssetIds.Add(id);
            output.WriteMarkup($"[dim]Streaming logs for {string.Join(", ", request.AssetIds)} (Ctrl+C to stop)...[/]");
        }
        else
        {
            output.WriteMarkup("[dim]Streaming system logs (Ctrl+C to stop)...[/]");
        }

        try
        {
            using var call = grpc.Proxy.StreamLogs(request, cancellationToken: ct);
            while (await call.ResponseStream.MoveNext(ct))
            {
                var msg = call.ResponseStream.Current;
                var level = msg.Level >= 0 && msg.Level < LogLevelNames.Length
                    ? LogLevelNames[msg.Level]
                    : msg.Level.ToString();

                var timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)msg.TimestampMs)
                    .ToLocalTime().ToString("HH:mm:ss.fff");

                var levelColor = msg.Level switch
                {
                    >= 4 => "red",
                    3 => "yellow",
                    2 => "green",
                    _ => "dim",
                };

                var source = string.IsNullOrEmpty(msg.Module)
                    ? msg.AssetId
                    : $"{msg.AssetId}:{msg.Module}";

                AnsiConsole.MarkupLine(
                    $"[dim]{timestamp}[/] [{levelColor}]{level,-5}[/] [cyan]{Markup.Escape(source)}[/] {Markup.Escape(msg.Message)}");
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[dim]Log streaming stopped.[/]");
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            output.WriteError("Cannot connect to proxy. Is the hub running?");
            return 1;
        }

        return 0;
    }
}
