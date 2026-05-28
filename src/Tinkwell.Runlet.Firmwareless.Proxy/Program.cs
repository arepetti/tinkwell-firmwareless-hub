using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Tinkwell.Runlet.Firmwareless.Proxy;
using Tinkwell.Runlet.Firmwareless.Proxy.Configuration;
using Tinkwell.Runlet.Store.Grpc;
using Tinkwell.Telemetry;

var builder = WebApplication.CreateBuilder(args);

var options = new ProxyOptions
{
    ProcessMonitorHost = builder.Configuration["Proxy:ProcessMonitorHost"] ?? "localhost",
    ProcessMonitorPort = int.TryParse(builder.Configuration["Proxy:ProcessMonitorPort"], out var p) ? p : 9500,
    AssetRegistryAddress = builder.Configuration["Proxy:AssetRegistryAddress"] ?? "http://localhost:5100",
    StateStoreAddress = builder.Configuration["Proxy:StateStoreAddress"] ?? "",
    HealthTtlSeconds = int.TryParse(builder.Configuration["Proxy:HealthTtlSeconds"], out var ttl) ? ttl : 120,
    DockerManageContainer = !string.Equals(builder.Configuration["Proxy:DockerManageContainer"], "false", StringComparison.OrdinalIgnoreCase),
    DockerImage = builder.Configuration["Proxy:DockerImage"] ?? "tinkwell-hub:latest",
    DockerContainerName = builder.Configuration["Proxy:DockerContainerName"] ?? "tinkwell-hub",
    DockerMemoryLimitBytes = long.TryParse(builder.Configuration["Proxy:DockerMemoryLimitBytes"], out var mem) ? mem : 512 * 1024 * 1024,
    DockerCpuNanos = long.TryParse(builder.Configuration["Proxy:DockerCpuNanos"], out var cpu) ? cpu : 1_000_000_000,
    DockerFirmletVolume = builder.Configuration["Proxy:DockerFirmletVolume"] ?? "tinkwell-firmlets",
    DockerStopTimeoutSeconds = int.TryParse(builder.Configuration["Proxy:DockerStopTimeoutSeconds"], out var dst) ? dst : 15,
};

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TcpTunnel>();
builder.Services.AddGrpc();
builder.Services.AddGrpcClient<Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto.AssetRegistryService.AssetRegistryServiceClient>(o =>
{
    o.Address = new Uri(options.AssetRegistryAddress);
});

if (!string.IsNullOrEmpty(options.StateStoreAddress))
{
    builder.Services.AddGrpcClient<StateStore.StateStoreClient>(o =>
    {
        o.Address = new Uri(options.StateStoreAddress);
    });
}

builder.Services.AddSingleton<HealthStoreWriter>();
builder.Services.AddSingleton<DockerContainerManager>();
builder.Services.AddHostedService<TunnelService>();
builder.Services.AddTinkwellTelemetry(
    builder.Configuration,
    sourceNames: ["Tinkwell.Firmwareless.Proxy"],
    meterNames: ["Tinkwell.Firmwareless.Proxy"]);

var app = builder.Build();

var tunnel = app.Services.GetRequiredService<TcpTunnel>();
var healthWriter = app.Services.GetRequiredService<HealthStoreWriter>();
tunnel.MessageReceived += async envelope =>
{
    if (envelope.PayloadCase == IpcEnvelope.PayloadOneofCase.HealthSnapshot)
        await healthWriter.WriteAsync(envelope.HealthSnapshot);
};

app.MapGrpcService<ProxyGrpcService>();

var dockerManager = app.Services.GetRequiredService<DockerContainerManager>();
if (options.DockerManageContainer)
{
    await dockerManager.StartAsync(CancellationToken.None);
}

app.Lifetime.ApplicationStopping.Register(() =>
{
    if (options.DockerManageContainer)
        dockerManager.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
});

await app.RunAsync();
