using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Tinkwell.Runlet.Firmwareless.Proxy.Configuration;

namespace Tinkwell.Runlet.Firmwareless.Proxy;

/// <summary>
/// Manages the lifecycle of the firmwareless hub Docker container. Creates,
/// starts, and stops the container programmatically so operators don't need
/// to run docker commands manually.
/// </summary>
public sealed class DockerContainerManager : IAsyncDisposable
{
    private readonly ProxyOptions _options;
    private readonly ILogger<DockerContainerManager> _logger;
    private readonly DockerClient _docker;
    private string? _containerId;

    public DockerContainerManager(ProxyOptions options, ILogger<DockerContainerManager> logger)
    {
        _options = options;
        _logger = logger;
        _docker = new DockerClientConfiguration().CreateClient();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var existing = await FindContainerAsync(ct);

        if (existing is not null)
        {
            _containerId = existing.ID;

            if (existing.State == "running")
            {
                _logger.LogInformation("Hub container {Name} already running (id: {Id})",
                    _options.DockerContainerName, ShortId(existing.ID));
                return;
            }

            _logger.LogInformation("Starting existing hub container {Id}", ShortId(existing.ID));
            await _docker.Containers.StartContainerAsync(existing.ID, new ContainerStartParameters(), ct);
            return;
        }

        _logger.LogInformation("Creating hub container from image {Image}", _options.DockerImage);

        var response = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = _options.DockerImage,
            Name = _options.DockerContainerName,
            Env =
            [
                "TW_HUB_DATA_DIR=/var/lib/tinkwell/hub",
                $"Hub__TcpPort={_options.ProcessMonitorPort}",
            ],
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["5684/udp"] = default,
            },
            HostConfig = new HostConfig
            {
                Memory = _options.DockerMemoryLimitBytes,
                NanoCPUs = _options.DockerCpuNanos,
                ReadonlyRootfs = true,
                CapDrop = ["ALL"],
                Tmpfs = new Dictionary<string, string>
                {
                    ["/tmp"] = "rw,noexec,nosuid,size=64m",
                },
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["5684/udp"] = [new PortBinding { HostPort = "5684" }],
                },
                Binds =
                [
                    $"{_options.DockerFirmletVolume}:/var/lib/tinkwell/hub/firmlets",
                ],
                NetworkMode = "none",
            },
        }, ct);

        _containerId = response.ID;
        _logger.LogInformation("Created hub container {Id}, starting", ShortId(response.ID));

        await _docker.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);
        _logger.LogInformation("Hub container started");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_containerId is null)
            return;

        _logger.LogInformation("Stopping hub container {Id}", ShortId(_containerId));
        try
        {
            await _docker.Containers.StopContainerAsync(_containerId, new ContainerStopParameters
            {
                WaitBeforeKillSeconds = (uint)_options.DockerStopTimeoutSeconds,
            }, ct);
            _logger.LogInformation("Hub container stopped");
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to stop hub container {Id}", ShortId(_containerId));
        }
    }

    private async Task<ContainerListResponse?> FindContainerAsync(CancellationToken ct)
    {
        var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool>
                {
                    [$"^/{_options.DockerContainerName}$"] = true,
                },
            },
        }, ct);

        return containers.FirstOrDefault();
    }

    private static string ShortId(string id) => id.Length > 12 ? id[..12] : id;

    public async ValueTask DisposeAsync()
    {
        _docker.Dispose();
    }
}