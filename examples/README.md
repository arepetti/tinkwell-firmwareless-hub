# Firmlet Package Examples

Example firmlet packages illustrating the package layout and configuration files.
These are not buildable -- they show the structure, `.tw` config syntax, `.proto` contracts, and C handler implementation patterns.

## Package Layout

Every firmlet package has this structure when installed into the Docker volume:

```
firmlet-package/
  firmlet.aot                        # AoT-compiled WASM module (required)
  content/
    config/
      services.tw                    # Service declarations + handler bindings
      measures.tw                    # Measures the firmlet may write
      permissions.tw                 # Permissions the firmlet requires
    proto/
      *.proto                        # Protobuf contracts (for callers)
```

Only `firmlet.aot` is required.
All config files are optional.

## Examples

### mesh-bridge

A service-only firmlet (no physical device).
Exposes `mesh.v1.MeshService` with three methods: `Route`, `Discover`, `GetTopology`.

- `content/config/services.tw` -- service declaration with `handler="mesh_{name:lowercase}"`
- `content/proto/mesh.proto` -- protobuf contract
- `src/main.c` -- C implementation using nanopb (exports `mesh_route`, `mesh_discover`, `mesh_gettopology`)

### hvac-controller

A device-bound firmlet that manages a thermostat.
Exposes `hvac.v1.ClimateService` and declares measures and permissions.

- `content/config/services.tw` -- service declaration with `handler="hvac_{name:lowercase}"`
- `content/config/measures.tw` -- temperature, humidity, setpoint, hvac-mode
- `content/config/permissions.tw` -- device access + service call + measure write permissions
- `content/proto/hvac.proto` -- protobuf contract

## Handler Resolution

Given a service-level `handler="mesh_{name:lowercase}"`:

| Method | Template Variable | Resolved Export |
|--------|-------------------|-----------------|
| Route | `{name}` = "Route", `:lowercase` | `mesh_route` |
| Discover | `{name}` = "Discover", `:lowercase` | `mesh_discover` |
| GetTopology | `{name}` = "GetTopology", `:lowercase` | `mesh_gettopology` |

Individual methods can override with `method LegacyPing handler="do_ping";`.

## Handler Signature

All handlers use the same standard C signature:

```c
int32_t handler(const uint8_t* request, uint32_t request_len,
                uint8_t* response, uint32_t* response_capacity);
```

- Return `0` for success, negative for error codes.
- Write the serialized protobuf response into `response` and set `*response_capacity` to the actual byte count.
