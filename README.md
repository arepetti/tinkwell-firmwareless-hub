# Tinkwell Firmwareless Hub

The hub runs WASM firmlets that manage physical devices on the local network.
Devices communicate via binary CoAP/protobuf; firmlets are AoT-compiled WebAssembly modules downloaded from the firmlet registry.

## Architecture

The system is split into two layers:

- **Inside Docker** -- a Supervisor spawns and monitors Router and per-asset WasmHost processes, fully isolated from the network
- **Outside Docker** -- four Tinkwell runlets that integrate with the standard Tinkwell runner

See [docs/architecture.md](docs/architecture.md) for the full architecture, including component responsibilities, the responsibility matrix, data flow diagrams, and the service registration design.

See [docs/evaluation-guide.md](docs/evaluation-guide.md) for a step-by-step walkthrough of building, running, and evaluating the hub locally.

## Projects

### Inside Docker

| Project | Description |
|---------|-------------|
| `Tinkwell.Firmwareless.Hosting.Supervisor` | Docker entrypoint; spawns/monitors Router + WasmHost processes |
| `Tinkwell.Firmwareless.Hosting.Router` | Named-pipe hub, service registry, TCP bridge, message routing |
| `Tinkwell.Firmwareless.Hosting.WasmHost` | WAMR host for a single firmlet |

### Outside Docker (Tinkwell Runlets)

| Project | Description |
|---------|-------------|
| `Tinkwell.Runlet.Firmwareless.Proxy` | gRPC proxy runlet (Docker boundary) |
| `Tinkwell.Runlet.Firmwareless.AssetRegistry` | SQLite asset database runlet |
| `Tinkwell.Runlet.Firmwareless.CoAP` | CoAP protocol runlet |
| `Tinkwell.Runlet.Firmwareless.Provisioning` | Asset provisioning runlet |

## Inter-Firmlet Services

Firmlets expose services to each other inside Docker via a fully declarative model.
Services are declared in `content/config/services.tw` within the firmlet package.
Handler functions are resolved from named WASM exports -- firmlets write zero registration code.

```tw
service "mesh.v1.MeshService" {
    family="MeshRouting";
    module="mesh-bridge";
    handler="mesh_{method:lowercase}";

    method Route;
    method GetTopology handler="mesh_get_topo";
}

service "analytics.v1.AnalyticsService" {
    family="Analytics";
    module="analytics";
    load="on-demand";
    handler="analytics_{method:lowercase}";

    method Aggregate;
}
```

The `handler` template expands `{method:lowercase}` for each method (e.g. `mesh_route`).
Individual methods can override with an explicit `handler` property (e.g. `mesh_get_topo`).
All payloads are protobuf-serialized; `.proto` files ship in the package for callers.

See [docs/architecture.md](docs/architecture.md#service-registration) for the full design: handler resolution, service states, dispatch flow, timeouts, and error handling.

## Host Function API

Functions imported by WASM firmlets from the host:

| Function | Description |
|----------|-------------|
| `tw_log(level, message, message_len)` | Log a message |
| `tw_send_command(cmd_type, cmd_type_len, payload, payload_len)` | Send a device command |
| `tw_call_service(name, name_len, method, method_len, req, req_len, resp, resp_cap)` | Call another firmlet's service |
| `tw_write_measure_float(name, name_len, value)` | Write a float measure |
| `tw_write_measure_quantity(name, name_len, quantity_str, quantity_len)` | Write a quantity string measure |
| `tw_read_sensor(name, name_len, value_out, ts_out)` | Read a sensor value |
| `tw_get_time_ms()` | Get current time in ms |

**Lifecycle (export from firmlet):** `tw_on_lifecycle(event, reason)` -- called by the host at lifecycle transitions.

## Firmlet Package Layout

```
firmlet-package/
  content/
    mesh-bridge.aot                  # AoT-compiled WASM module(s)
    analytics.aot                    # additional module (optional)
    config/
      services.tw                    # Service declarations + handler bindings
      measures.tw                    # Declares measures the firmlet may write
      permissions.tw                 # Declares permissions the firmlet requires
    proto/
      *.proto                        # Protobuf contracts for callers
```

## Running

Build the Docker image, then start the Tinkwell runner with the firmwareless runlets.
The Proxy runlet automatically creates and starts the Docker container on boot (using Docker.DotNet), and stops it on shutdown.
No manual `docker run` or `docker-compose` needed.

```bash
cd extras/firmwareless/hub
docker build -t tinkwell-hub .
```

Container resource limits, image name, and volume mounts are configured via the Proxy runlet's `Proxy:Docker*` settings.
See [docs/evaluation-guide.md](docs/evaluation-guide.md) for the full config reference.

> **Note:** `TW_FIRMLET_HUB_API_KEY` and `TW_FIRMLET_REGISTRY_URL` are used only by the CLI commands and Provisioning runlet (outside Docker) to authenticate with the firmlet registry.
> Nothing inside the Docker container accesses the registry -- firmlets are loaded from the shared volume.

## Building

```bash
cd extras/firmwareless/hub
dotnet build Tinkwell.Firmwareless.Hosting.slnx
dotnet test Tinkwell.Firmwareless.Hosting.slnx
```
