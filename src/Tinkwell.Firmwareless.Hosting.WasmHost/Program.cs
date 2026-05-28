using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.WasmHost;

var pipeName = "";
var hostId = "";
var modulesPath = "";

for (int i=0; i < args.Length - 1; ++i)
{
    switch (args[i])
    {
        case "--pipe":
            pipeName = args[++i];
            break;
        case "--host-id":
            hostId = args[++i];
            break;
        case "--modules":
            modulesPath = args[++i];
            break;
    }
}

if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(modulesPath))
{
    Console.Error.WriteLine(
        "Usage: Tinkwell.Firmwareless.Hosting.WasmHost --pipe <name> --host-id <id> --modules <path>");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService(sp =>
{
    var lifetime = sp.GetRequiredService<IHostApplicationLifetime>();
    var logger = sp.GetRequiredService<ILogger<HostService>>();
    return new HostService(pipeName, hostId, modulesPath, lifetime, logger);
});

var host = builder.Build();
await host.RunAsync();
return 0;
