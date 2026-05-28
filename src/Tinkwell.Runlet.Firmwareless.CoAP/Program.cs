using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tinkwell.Runlet.Firmwareless.CoAP;
using Tinkwell.Runlet.Firmwareless.CoAP.Configuration;
using Tinkwell.Telemetry;

// Allow gRPC over plaintext HTTP/2 to local proxy / asset registry (e.g. http://localhost:5000).
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = Host.CreateApplicationBuilder(args);

var options = new CoapRunletOptions
{
    Port = int.TryParse(builder.Configuration["CoAP:Port"], out var p) ? p : 5684,
    ProxyAddress = builder.Configuration["CoAP:ProxyAddress"] ?? "http://localhost:5000",
    AssetRegistryAddress = builder.Configuration["CoAP:AssetRegistryAddress"] ?? "http://localhost:5100",
    MeasuresAddress = builder.Configuration["CoAP:MeasuresAddress"] ?? "",
};

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<SensorCache>();
builder.Services.AddSingleton<MeasureBridge>();

builder.Services.AddHostedService<CoapRunletService>();
builder.Services.AddTinkwellTelemetry(
    builder.Configuration,
    sourceNames: ["Tinkwell.Firmwareless.CoAP"],
    meterNames: ["Tinkwell.Firmwareless.CoAP"]);

var host = builder.Build();
await host.RunAsync();
