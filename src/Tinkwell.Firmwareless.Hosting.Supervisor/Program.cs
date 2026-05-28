using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tinkwell.Firmwareless.Hosting.Supervisor;
using Tinkwell.Firmwareless.Hosting.Supervisor.Configuration;
using Tinkwell.Firmwareless.Hosting.Supervisor.Monitoring;

var builder = Host.CreateApplicationBuilder(args);

var options = new SupervisorOptions();
builder.Configuration.GetSection("Supervisor").Bind(options);

for (var i=0; i < args.Length; ++i)
{
    if ((args[i] == "--tcp-port" || args[i] == "-p") && i + 1 < args.Length &&
        int.TryParse(args[i + 1], out var tcpPort))
    {
        options.TcpPort = tcpPort;
        i++;
    }
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ProcessSupervisor>();
builder.Services.AddSingleton<HostHealthCollector>();
builder.Services.AddHostedService<SupervisorService>();

var host = builder.Build();
await host.RunAsync();
