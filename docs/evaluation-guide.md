# Firmwareless Hub -- Evaluation Guide (0.1)

End-to-end walkthrough for evaluating the firmwareless hub locally.
Covers building, running, installing a firmlet, monitoring, and updating.

> **Prerequisites**  - .NET 10 SDK - Docker Desktop (or compatible runtime) - A running Tinkwell runner with the firmwareless runlets registered - Access to a firmlet registry (or a local instance via   `extras/firmwareless/registry`)

## 1. Build the hub

Build the hosting solution (Supervisor, Router, WasmHost) and the runlets:

```bash
cd extras/firmwareless/hub
dotnet build Tinkwell.Firmwareless.Hosting.slnx
```

Build the Docker image for the in-Docker components:

```bash
docker build -t tinkwell-hub .
```

## 2. Start the Tinkwell runner with firmwareless runlets

The four outside-Docker runlets must be registered with the Tinkwell runner.
Use whatever mechanism your runner configuration supports (typically `runner.tw` or `tw runner install`):

| Runlet | Default port | Project |
|--------|-------------|---------|
| Proxy | 5000 | `Tinkwell.Runlet.Firmwareless.Proxy` |
| Asset Registry | 5100 | `Tinkwell.Runlet.Firmwareless.AssetRegistry` |
| CoAP | 5684/udp | `Tinkwell.Runlet.Firmwareless.CoAP` |
| Provisioning | (internal) | `Tinkwell.Runlet.Firmwareless.Provisioning` |

When the Proxy runlet starts, it automatically creates and starts the Docker container (using the `tinkwell-hub` image built in step 1).
The container is configured with resource limits, a read-only root filesystem, no internet access, and a shared volume for firmlet data.
On shutdown, the Proxy stops the container gracefully.

Set `Proxy:DockerManageContainer=false` to disable automatic container management (e.g. if running the container externally for debugging).

## 3. Install a firmlet

Download a compiled firmlet from the registry and install it on the hub:

```bash
tw hub install <firmlet-id> \
  --registry-url http://localhost:5200 \
  --api-key <your-api-key>
```

The command:

1. Requests the compiled artifact from the registry for the current architecture.
2. If the registry returns **202 Accepted** (compilation in progress), polls until the artifact is ready.
3. Downloads the `.zip` package, extracts it to the firmlet base directory.
4. Registers the asset with the Asset Registry.
5. Tells the Proxy to start a WasmHost for the new asset.

**Options:**

| Option | Description |
|--------|-------------|
| `--arch` | Target architecture (e.g. `arm64-linux`). Auto-detected if omitted. |
| `--asset-id` | Explicit asset GUID. Auto-generated if omitted. |
| `--firmlet-base-dir` | Override the firmlet install directory. |
| `-o, --output` | Save the compiled artifact to a file instead of installing. |

### Alternative: manual add

If you already have a firmlet directory (e.g. from `--output` above):

```bash
tw hub add /path/to/firmlet-dir --display-name "My Sensor"
```

## 4. Check hub health

```bash
tw runners health
```

Firmwareless assets appear under `firmwareless/<asset-id>` with CPU, memory, thread count, firmlet state, and uptime.

## 5. Stream logs

System logs (Supervisor, Router):

```bash
tw hub log
```

Logs from a specific asset:

```bash
tw hub log <asset-id>
```

Logs from all assets:

```bash
tw hub log *
```

Multiple specific assets:

```bash
tw hub log <id-1>,<id-2>
```

Output is color-coded by level (ERROR=red, WARN=yellow, INFO=green) with timestamps and source module.

## 6. Check for updates

Check whether newer firmlet versions are available:

```bash
tw hub update --all --check
```

Apply updates for a single asset:

```bash
tw hub update <asset-id> \
  --registry-url http://localhost:5200 \
  --api-key <your-api-key>
```

Apply all available updates:

```bash
tw hub update --all \
  --registry-url http://localhost:5200 \
  --api-key <your-api-key>
```

The Provisioning runlet also checks for updates at startup and logs any that are available.

## 7. Pending downloads

If a firmlet download fails (network error, compilation not ready), it is queued for automatic retry.
The background worker retries every 5 minutes (configurable via `Provisioning:PendingRetryMinutes`).

View the queue:

```bash
tw hub pending
```

Removing an asset cancels any pending download for it:

```bash
tw hub remove <asset-id>
```

## 8. Device provisioning (physical devices)

When a physical device is on the LAN:

```bash
tw hub provision --device-addr 192.168.4.1
```

This discovers the device's identity via CoAP, assigns a GUID, and prints the device info.
You can then install the appropriate firmlet with `tw hub install`.

## Configuration reference

### Environment variables

| Variable | Used by | Description |
|----------|---------|-------------|
| `TW_FIRMLET_REGISTRY_URL` | CLI, Provisioning | Firmlet registry base URL (outside Docker only) |
| `TW_FIRMLET_HUB_API_KEY` | CLI, Provisioning | Bearer token for registry auth (outside Docker only) |
| `TW_FIRMLET_BASE_DIR` | CLI | Firmlet install directory (default: platform-specific) |
| `TW_HUB_PROXY_URL` | CLI | Proxy runlet gRPC address (default: `http://localhost:5000`) |
| `TW_HUB_ASSET_REGISTRY_URL` | CLI | Asset registry gRPC address (default: `http://localhost:5100`) |
| `TW_HUB_DATA_DIR` | Docker (internal) | Hub data directory inside the container |

> **Note:** Nothing inside the Docker container accesses the firmlet registry.
> The API key and registry URL are only used by components running outside Docker (CLI and Provisioning runlet) to download compiled firmlets into the shared volume.

### Proxy runlet (`Proxy:*`)

| Key | Default | Description |
|-----|---------|-------------|
| `ProcessMonitorHost` | `localhost` | Supervisor TCP host |
| `ProcessMonitorPort` | `9500` | Supervisor TCP port |
| `AssetRegistryAddress` | `http://localhost:5100` | Asset registry gRPC |
| `StateStoreAddress` | *(empty)* | Tinkwell state store gRPC. If empty, health forwarding to `tw runners health` is disabled. |
| `HealthTtlSeconds` | `120` | TTL for health entries in the state store |
| `DockerManageContainer` | `true` | Automatically create/start the Docker container on boot |
| `DockerImage` | `tinkwell-hub:latest` | Docker image to use for the hub container |
| `DockerContainerName` | `tinkwell-hub` | Name for the managed container |
| `DockerMemoryLimitBytes` | `536870912` | Container memory limit (512 MiB) |
| `DockerCpuNanos` | `1000000000` | Container CPU limit in nanoseconds (1.0 CPU) |
| `DockerFirmletVolume` | `tinkwell-firmlets` | Docker volume name for the firmlet shared directory |
| `DockerStopTimeoutSeconds` | `15` | Grace period before force-killing the container on shutdown |

### CoAP runlet (`CoAP:*`)

| Key | Default | Description |
|-----|---------|-------------|
| `Port` | `5684` | UDP listen port |
| `ProxyAddress` | `http://localhost:5000` | Proxy runlet gRPC |
| `AssetRegistryAddress` | `http://localhost:5100` | Asset registry gRPC |
| `MeasuresAddress` | *(empty)* | Tinkwell measures gRPC. If empty, device telemetry is not bridged to the measures system. |

### Provisioning runlet (`Provisioning:*`)

| Key | Default | Description |
|-----|---------|-------------|
| `RegistryUrl` | *(empty)* | Firmlet registry URL |
| `RegistryApiKey` | *(empty)* | Registry API key |
| `Architecture` | `{cpu}-linux` | Target architecture for downloads (always Linux; CPU auto-detected) |
| `FirmletBaseDir` | `/var/lib/tinkwell/firmlets` | Firmlet install directory |
| `ProxyAddress` | `http://localhost:5000` | Proxy runlet gRPC |
| `AssetRegistryAddress` | `http://localhost:5100` | Asset registry gRPC |
| `PendingRetryMinutes` | `5` | Retry interval for failed downloads |
| `PendingTimeoutHours` | `6` | Overall timeout after which pending downloads are abandoned |

### Supervisor (`Hub:*`)

| Key | Default | Description |
|-----|---------|-------------|
| `HostExePath` | *(required)* | Path to the WasmHost executable |
| `RouterExePath` | *(required)* | Path to the Router executable |
| `FirmletBaseDir` | `/var/lib/tinkwell/firmlets` | Firmlet base directory |
| `TcpPort` | `9500` | TCP port for the Router bridge |
| `HealthIntervalSeconds` | `30` | Health collection interval |
| `ShutdownGraceSeconds` | `10` | Grace period for clean shutdown |
| `StartupTimeoutSeconds` | `30` | Timeout for Router pipe connection |
| `MaxRouterRestarts` | `3` | Router restart limit before exit |
| `MaxHostRestartsPerHour` | `10` | Per-asset restart limit |
| `CpuThresholdPercent` | `90` | CPU alert threshold |
| `MemoryThresholdBytes` | `536870912` | Memory alert threshold (512 MiB) |

### Telemetry (OTLP)

Telemetry export is configured via the standard `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable (or the `OpenTelemetry:Endpoint` config key used by `AddTinkwellTelemetry`).
If no endpoint is configured, telemetry is discarded (black-hole collector).
You can use an Aspire dashboard, Jaeger, or any OTLP-compatible collector.

## Notes

### Shutdown grace period

The Supervisor's `ShutdownGraceSeconds` (default: 10) controls how long child processes have to shut down cleanly before being killed.
If your firmlets perform significant cleanup (flushing buffers, writing state), you may need to increase this value.
In future versions, crash isolation will be improved so that a firmlet crash cannot corrupt global state -- at most it may require a restart.

### CLI command reference

| Command | Description |
|---------|-------------|
| `tw hub install <firmlet-id>` | Download and install a firmlet from the registry |
| `tw hub add <firmlet-dir>` | Register and start a locally available firmlet |
| `tw hub remove <asset-id>` | Stop, unregister, and optionally delete a firmlet |
| `tw hub provision` | Discover and provision a physical device via CoAP |
| `tw hub update [<asset-id>\|--all]` | Check for or apply firmlet updates |
| `tw hub pending` | List pending firmlet downloads awaiting retry |
| `tw hub log [<filter>]` | Stream live logs (system, specific asset, or all) |
