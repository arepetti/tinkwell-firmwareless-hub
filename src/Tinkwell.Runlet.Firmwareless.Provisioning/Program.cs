using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tinkwell.Firmlets.Registry.Client;
using Tinkwell.Runlet.Firmwareless.AssetRegistry.Proto;
using Tinkwell.Runlet.Firmwareless.Provisioning;
using Tinkwell.Runlet.Firmwareless.Provisioning.Configuration;
using Tinkwell.Runlet.Firmwareless.Provisioning.Firmlets;
using Tinkwell.Runlet.Firmwareless.Proxy.Proto;

var builder = Host.CreateApplicationBuilder(args);

var provSection = builder.Configuration.GetSection("Provisioning");
builder.Services.Configure<ProvisioningOptions>(provSection);

var provOpts = provSection.Get<ProvisioningOptions>() ?? new ProvisioningOptions();

if (!string.IsNullOrWhiteSpace(provOpts.RegistryUrl))
{
    builder.Services.AddFirmletRegistryClient(opts =>
    {
        opts.BaseUrl = provOpts.RegistryUrl;
        opts.ApiKey = provOpts.RegistryApiKey;
    });
}
else
{
    builder.Services.AddSingleton(_ => new FirmletRegistryApiClient(new HttpClient()));
}

Directory.CreateDirectory(provOpts.FirmletBaseDir);

builder.Services.AddSingleton<FirmletResolver>(sp =>
{
    var registry = sp.GetRequiredService<FirmletRegistryApiClient>();
    var o = sp.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<FirmletResolver>>();
    return new FirmletResolver(registry, o.Architecture, logger);
});

builder.Services.AddSingleton<FirmletInstaller>(sp =>
{
    var registry = sp.GetRequiredService<FirmletRegistryApiClient>();
    var o = sp.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<FirmletInstaller>>();
    return new FirmletInstaller(registry, o.FirmletBaseDir, o.Architecture, registryPublicKey: null, logger);
});

builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
    var channel = GrpcChannel.ForAddress(o.ProxyAddress);
    return new FirmwarelessProxy.FirmwarelessProxyClient(channel);
});

builder.Services.AddSingleton(sp =>
{
    var o = sp.GetRequiredService<IOptions<ProvisioningOptions>>().Value;
    var channel = GrpcChannel.ForAddress(o.AssetRegistryAddress);
    return new AssetRegistryService.AssetRegistryServiceClient(channel);
});

builder.Services.AddSingleton(new PendingDownloadStore(provOpts.FirmletBaseDir));
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddSingleton<ProvisioningService>();
builder.Services.AddHostedService<PendingDownloadWorker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");

_ = Task.Run(async () =>
{
    try
    {
        var checker = host.Services.GetRequiredService<UpdateChecker>();
        var updates = await checker.CheckAllAsync();
        if (updates.Count > 0)
        {
            logger.LogInformation("{Count} firmlet update(s) available:", updates.Count);
            foreach (var u in updates)
                logger.LogInformation("  {AssetId}: {Name} {Current} -> {Latest}",
                    u.AssetId, u.FirmletName, u.CurrentVersion, u.LatestVersion);
        }
        else
        {
            logger.LogInformation("All firmlets are up to date");
        }
    }
    catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
    catch (Exception ex)
    {
        logger.LogDebug(ex, "Startup update check failed (will retry on next cycle)");
    }
});

await host.RunAsync();