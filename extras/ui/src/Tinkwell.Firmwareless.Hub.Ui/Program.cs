using System.Net.WebSockets;
using Tinkwell.Expressions;
using Tinkwell.Firmwareless.Hub.Ui;
using Tinkwell.Firmwareless.Hub.Ui.Api;
using Tinkwell.Firmwareless.Hub.Ui.Bridge;
using Tinkwell.Firmwareless.Hub.Ui.Configuration;
using Tinkwell.Firmwareless.Hub.Ui.Expressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection(BridgeOptions.SectionName));

builder.Services.AddSingleton<UiDocument>();
builder.Services.AddSingleton<IExpressionEvaluator>(new ExpressionEvaluator());
builder.Services.AddSingleton<UiExpressionEngine>();
builder.Services.AddSingleton<UiConfigDiscovery>();
builder.Services.AddSingleton<MeasureBridge>();
builder.Services.AddSingleton<StoreBridge>();
builder.Services.AddSingleton<CommandBridge>();
builder.Services.AddSingleton<UiWebSocketHandler>();

builder.Services.AddHostedService<UiRunletService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.MapGet("/api/ui/tree", (UiDocument doc) =>
    Results.Json(UiTreeSerializer.BuildTree(doc)));

app.MapGet("/api/ui/theme", (UiDocument doc) =>
    Results.Json(UiTreeSerializer.BuildThemeObject(doc)));

app.MapGet("/api/ui/widgets", (UiDocument doc) =>
    Results.Json(doc.Widgets
        .OrderBy(w => w.Order)
        .Select(w => new
        {
            name = w.Name,
            label = w.Label?.Value ?? w.Name,
            icon = w.Icon?.Value,
        })));

app.Map("/ws", async (HttpContext context, UiWebSocketHandler handler) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await handler.HandleAsync(socket, context.RequestAborted);
});

app.MapFallbackToFile("index.html");

app.Run();
