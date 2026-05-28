using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.WasmHost.Ipc;
using Tinkwell.Firmwareless.Hosting.WasmHost.Services;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;

using ProtoLoadPolicy = Tinkwell.Firmwareless.Hosting.Ipc.Proto.ModuleLoadPolicy;

namespace Tinkwell.Firmwareless.Hosting.WasmHost;

public sealed class HostService : BackgroundService
{
    private readonly string _pipeName;
    private readonly string _hostId;
    private readonly string _modulesPath;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<HostService> _logger;

    private IpcClient? _ipc;
    private HostFunctions? _hostFunctions;

    private readonly List<ServiceDefinition> _serviceDefinitions = [];
    private readonly Dictionary<string, Dictionary<string, MethodDefinition>> _handlers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedModules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _availableModules = new(StringComparer.Ordinal);

    private FirmletState _firmletState = FirmletState.Loading;
    private bool _firmletInitializedOnce;

    public HostService(string pipeName, string hostId, string modulesPath,
        IHostApplicationLifetime lifetime, ILogger<HostService> logger)
    {
        _pipeName = pipeName;
        _hostId = hostId;
        _modulesPath = modulesPath;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Host process {HostId} starting, pipe={Pipe}, modules={Path}",
            _hostId, _pipeName, _modulesPath);

        _ipc = new IpcClient(_pipeName);

        try
        {
            await _ipc.ConnectAsync(ct: stoppingToken);
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to connect to coordinator pipe");
            return;
        }

        _hostFunctions = new HostFunctions(_ipc, _logger);

        await _ipc.SendAsync(new IpcEnvelope
        {
            RegisterClient = new RegisterClient { HostId = _hostId }
        }, stoppingToken);

        DiscoverAvailableModules();

        var services = ReadServicesConfig();
        _serviceDefinitions.Clear();
        _serviceDefinitions.AddRange(services);
        RebuildHandlerMaps();

        LoadStartupModules();

        await RegisterServicesAsync(stoppingToken);

        await _ipc.SendAsync(new IpcEnvelope
        {
            Ready = new Ready { LoadedModules = { _loadedModules } }
        }, stoppingToken);

        _logger.LogInformation(
            "Host process {HostId} ready: {Loaded} modules loaded, {Available} available, {SvcCount} services",
            _hostId, _loadedModules.Count, _availableModules.Count, _serviceDefinitions.Count);

        // TODO: query Asset Registry for _firmletInitializedOnce flag via Proxy gRPC

        var initReason = _firmletInitializedOnce
            ? LifecycleReason.Start
            : LifecycleReason.FirstTimeSetup;

        await DispatchLifecycleAsync(LifecycleEvent.Initialize, initReason, stoppingToken);
        await DispatchLifecycleAsync(LifecycleEvent.Start, LifecycleReason.Start, stoppingToken);

        if (!_firmletInitializedOnce)
        {
            _firmletInitializedOnce = true;
            // TODO: notify Asset Registry to persist firmlet_initialized = true
        }

        _ipc.UnsolicitedMessage += async envelope =>
        {
            await HandleMessageAsync(envelope, stoppingToken);
        };

        try
        {
            await _ipc.RunReadLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message loop error in host {HostId}", _hostId);
        }

        _logger.LogInformation("Host process {HostId} shutting down", _hostId);
    }

    private List<ServiceDefinition> ReadServicesConfig()
    {
        var path = Path.Combine(_modulesPath, "services.tw");
        if (!File.Exists(path))
        {
            _logger.LogWarning("services.tw not found at {Path}", path);
            return [];
        }

        try
        {
            var text = File.ReadAllText(path);
            var services = ServicesTwParser.Parse(text);
            foreach (var s in services)
                HandlerResolver.ResolveHandlers(s);

            return services;
        }
        catch (OutOfMemoryException) { Environment.FailFast("Out of memory"); throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse services.tw");
            return [];
        }
    }

    private void RebuildHandlerMaps()
    {
        _handlers.Clear();
        foreach (var s in _serviceDefinitions)
        {
            _handlers[s.FullName] = s.Methods.ToDictionary(m => m.Name, StringComparer.Ordinal);
        }
    }

    private async Task RegisterServicesAsync(CancellationToken ct)
    {
        if (_ipc is null)
            return;

        foreach (var svc in _serviceDefinitions)
        {
            var state = svc.LoadPolicy == Services.ModuleLoadPolicy.OnDemand
                ? ServiceState.Defined
                : DetermineServiceState(svc);

            var reg = new RegisterService
            {
                FullName = svc.FullName,
                FamilyName = svc.Family ?? "",
                State = state,
                ModuleName = svc.Module ?? "",
                LoadPolicy = svc.LoadPolicy == Services.ModuleLoadPolicy.OnDemand
                    ? ProtoLoadPolicy.OnDemand
                    : ProtoLoadPolicy.Startup,
            };

            foreach (var m in svc.Methods)
            {
                reg.Methods.Add(new MethodInfo
                {
                    Name = m.Name,
                    HandlerExport = m.ResolvedExport ?? "",
                    Bound = m.IsBound,
                });
            }

            await _ipc.SendAsync(new IpcEnvelope { RegisterService = reg }, ct);
        }
    }

    private ServiceState DetermineServiceState(ServiceDefinition svc)
    {
        if (svc.Methods.Count == 0)
            return ServiceState.Ready;

        var boundCount = svc.Methods.Count(m => m.ResolvedExport is not null);
        if (boundCount == svc.Methods.Count)
            return ServiceState.Ready;
        if (boundCount > 0)
            return ServiceState.Degraded;
        return ServiceState.Defined;
    }

    private void DiscoverAvailableModules()
    {
        var contentDir = Path.Combine(_modulesPath, "content");
        if (!Directory.Exists(contentDir))
        {
            _logger.LogWarning("Content directory not found: {Path}", contentDir);
            return;
        }

        foreach (var aotFile in Directory.EnumerateFiles(contentDir, "*.aot"))
        {
            var name = Path.GetFileNameWithoutExtension(aotFile);
            _availableModules.Add(name);
            _logger.LogDebug("Discovered module: {Name}", name);
        }
    }

    private void LoadStartupModules()
    {
        var startupModules = _serviceDefinitions
            .Where(s => s.LoadPolicy == Services.ModuleLoadPolicy.Startup && s.Module is not null)
            .Select(s => s.Module!)
            .Distinct(StringComparer.Ordinal);

        foreach (var moduleName in startupModules)
            LoadModule(moduleName);

        // Also load any modules that have no service declaration (backward compat)
        foreach (var available in _availableModules)
        {
            if (!_loadedModules.Contains(available) &&
                !_serviceDefinitions.Any(s => string.Equals(s.Module, available, StringComparison.Ordinal)))
            {
                LoadModule(available);
            }
        }
    }

    private bool LoadModule(string moduleName)
    {
        if (_loadedModules.Contains(moduleName))
            return true;

        if (!_availableModules.Contains(moduleName))
        {
            _logger.LogWarning("Module {Module} not found in content directory", moduleName);
            return false;
        }

        var aotPath = Path.Combine(_modulesPath, "content", $"{moduleName}.aot");
        _logger.LogInformation("Loading module: {Name} from {Path}", moduleName, aotPath);

        // WAMR integration point: wasm_runtime_load() + wasm_runtime_instantiate()
        _loadedModules.Add(moduleName);
        return true;
    }

    private bool EnsureModuleLoaded(ServiceDefinition service)
    {
        if (service.Module is null)
            return true;

        if (_loadedModules.Contains(service.Module))
            return true;

        _logger.LogInformation("On-demand loading module {Module} for service {Service}",
            service.Module, service.FullName);
        return LoadModule(service.Module);
    }

    private async Task HandleMessageAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        switch (envelope.PayloadCase)
        {
            case IpcEnvelope.PayloadOneofCase.Shutdown:
                _logger.LogInformation("Shutdown requested: {Reason}", envelope.Shutdown.Reason);
                await DispatchLifecycleAsync(LifecycleEvent.Stop, LifecycleReason.Quit, ct);
                _lifetime.StopApplication();
                break;

            case IpcEnvelope.PayloadOneofCase.SuspendFirmlet:
                await HandleSuspendAsync(envelope.SuspendFirmlet.Reason, ct);
                break;

            case IpcEnvelope.PayloadOneofCase.ResumeFirmlet:
                await HandleResumeAsync(ct);
                break;

            case IpcEnvelope.PayloadOneofCase.DeviceHeartbeat:
                _logger.LogDebug("Device heartbeat received for host {HostId}", _hostId);
                break;

            case IpcEnvelope.PayloadOneofCase.DeviceTelemetry:
                _logger.LogDebug("Device telemetry received for host {HostId}", _hostId);
                break;

            case IpcEnvelope.PayloadOneofCase.ServiceCall:
                if (_firmletState == FirmletState.Suspended)
                {
                    await _ipc!.SendAsync(new IpcEnvelope
                    {
                        ServiceReply = new ServiceReply
                        {
                            CorrelationId = envelope.ServiceCall.CorrelationId,
                            Success = false,
                            Error = "Firmlet is suspended",
                        }
                    }, ct);
                    break;
                }
                await HandleServiceCallAsync(envelope.ServiceCall, ct);
                break;

            case IpcEnvelope.PayloadOneofCase.WriteMeasure:
                await HandleWriteMeasureAsync(envelope.WriteMeasure, ct);
                break;

            default:
                _logger.LogDebug("Unhandled message type: {Case}", envelope.PayloadCase);
                break;
        }
    }

    private async Task HandleSuspendAsync(string reason, CancellationToken ct)
    {
        if (_firmletState == FirmletState.Suspended)
        {
            _logger.LogWarning("Host {HostId}: already suspended", _hostId);
            return;
        }

        _logger.LogInformation("Host {HostId}: suspending ({Reason})", _hostId, reason);
        await DispatchLifecycleAsync(LifecycleEvent.Stop, LifecycleReason.Suspend, ct);
    }

    private async Task HandleResumeAsync(CancellationToken ct)
    {
        if (_firmletState != FirmletState.Suspended)
        {
            _logger.LogWarning("Host {HostId}: not suspended, ignoring resume", _hostId);
            return;
        }

        _logger.LogInformation("Host {HostId}: resuming", _hostId);
        await DispatchLifecycleAsync(LifecycleEvent.Initialize, LifecycleReason.Start, ct);
        await DispatchLifecycleAsync(LifecycleEvent.Start, LifecycleReason.Resume, ct);
    }

    private async Task DispatchLifecycleAsync(LifecycleEvent evt, LifecycleReason reason, CancellationToken ct)
    {
        var prevState = _firmletState;

        _firmletState = evt switch
        {
            LifecycleEvent.Initialize => FirmletState.Initialized,
            LifecycleEvent.Start => FirmletState.Running,
            LifecycleEvent.Stop when reason == LifecycleReason.Suspend => FirmletState.Suspended,
            LifecycleEvent.Stop => FirmletState.Stopped,
            _ => _firmletState,
        };

        _logger.LogInformation("Host {HostId}: lifecycle {Event}({Reason}), {PrevState} -> {NewState}",
            _hostId, evt, reason, prevState, _firmletState);

        // WAMR integration point: call tw_on_lifecycle(event, reason) export
        // wasm_runtime_call_exported("tw_on_lifecycle", (int)evt, (int)reason);

        if (_ipc is not null)
        {
            await _ipc.SendAsync(new IpcEnvelope
            {
                FirmletStateChanged = new FirmletStateChanged
                {
                    State = _firmletState,
                    LastEvent = evt,
                    LastReason = reason,
                }
            }, ct);
        }
    }

    private async Task HandleServiceCallAsync(ServiceCall call, CancellationToken ct)
    {
        var fullName = ResolveFullServiceName(call);
        string? resolvedExport = null;

        ServiceDefinition? svc = fullName is not null
            ? _serviceDefinitions.Find(s => string.Equals(s.FullName, fullName, StringComparison.Ordinal))
            : null;

        if (fullName is not null &&
            _handlers.TryGetValue(fullName, out var byMethod) &&
            byMethod.TryGetValue(call.MethodName, out var method))
        {
            resolvedExport = method.ResolvedExport;
        }
        else if (svc is not null)
        {
            resolvedExport = HandlerResolver.ResolveAtCallTime(svc, call.MethodName);
            _logger.LogDebug(
                "Service call: {Service}/{Method} resolved at call time -> {Export}",
                fullName, call.MethodName, resolvedExport);
        }

        if (svc is not null && resolvedExport is not null)
        {
            if (!EnsureModuleLoaded(svc))
            {
                await _ipc!.SendAsync(new IpcEnvelope
                {
                    ServiceReply = new ServiceReply
                    {
                        CorrelationId = call.CorrelationId,
                        Success = false,
                        Error = $"Module '{svc.Module}' failed to load",
                    }
                }, ct);
                return;
            }

            _logger.LogDebug(
                "Service call: {Service}/{Method} -> export {Export} (stub)",
                fullName, call.MethodName, resolvedExport);
        }
        else
        {
            _logger.LogDebug("Service call: unresolved {Service}/{Method} (ByFamily={ByFamily})",
                call.ServiceName, call.MethodName, call.ByFamily);
        }

        // WAMR integration point: call the resolved export in the loaded module
        await _ipc!.SendAsync(new IpcEnvelope
        {
            ServiceReply = new ServiceReply
            {
                CorrelationId = call.CorrelationId,
                Success = false,
                Error = "not implemented in prototype",
            }
        }, ct);
    }

    private string? ResolveFullServiceName(ServiceCall call)
    {
        if (!call.ByFamily)
            return call.ServiceName;

        var match = _serviceDefinitions.Find(s =>
            string.Equals(s.Family, call.ServiceName, StringComparison.Ordinal));

        return match?.FullName;
    }

    private async Task HandleWriteMeasureAsync(WriteMeasure msg, CancellationToken ct)
    {
        var displayValue = msg.MeasureValueCase == WriteMeasure.MeasureValueOneofCase.FloatValue
            ? msg.FloatValue.ToString("G")
            : msg.QuantityValue;
        _logger.LogDebug("WriteMeasure: {Name}={Value}", msg.Name, displayValue);

        await _ipc!.SendAsync(new IpcEnvelope
        {
            WriteMeasureReply = new WriteMeasureReply { Success = true }
        }, ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_firmletState is FirmletState.Running or FirmletState.Initialized or FirmletState.Suspended)
            await DispatchLifecycleAsync(LifecycleEvent.Stop, LifecycleReason.Quit, cancellationToken);

        if (_ipc is not null)
            await _ipc.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}