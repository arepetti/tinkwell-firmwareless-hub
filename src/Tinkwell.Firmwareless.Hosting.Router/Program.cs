using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.Router;
using Tinkwell.Firmwareless.Hosting.Router.Configuration;
using Tinkwell.Firmwareless.Hosting.Router.Ipc;
using Tinkwell.Firmwareless.Hosting.Router.Tcp;

var builder = Host.CreateApplicationBuilder(args);

var options = new RouterOptions();
builder.Configuration.GetSection("Router").Bind(options);

for (var i=0; i < args.Length; ++i)
{
    switch (args[i])
    {
        case "--pipe" when i + 1 < args.Length:
            options.SupervisorPipeName = args[++i];
            break;
        case "--tcp-port" or "-p" when i + 1 < args.Length && int.TryParse(args[i + 1], out var tcpPort):
            options.TcpPort = tcpPort;
            i++;
            break;
    }
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IpcServer>();
builder.Services.AddSingleton<ServiceRegistry>();
builder.Services.AddSingleton<TcpBridge>(sp =>
    new TcpBridge(options.TcpPort, sp.GetRequiredService<ILogger<TcpBridge>>()));

builder.Services.AddHostedService<RouterService>();

var host = builder.Build();
await host.RunAsync();
