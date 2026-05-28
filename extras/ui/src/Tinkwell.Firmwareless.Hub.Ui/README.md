# Tinkwell.Firmwareless.Hub.Ui

ASP.NET Core host for the Firmwareless Hub UI: `.tw` parsing, NCalc evaluation, gRPC bridges (Proxy, AssetRegistry, StateStore), REST endpoints, and WebSocket push.

## Prerequisites

- **.NET 10 SDK** (see repository `global.json` in the main Tinkwell tree when applicable)

## Standalone mode

From this project directory:

```bash
dotnet run
```

Kestrel uses the default development URLs (typically **`http://localhost:5000`** and **`https://localhost:5001`** unless overridden by `ASPNETCORE_URLS` or environment-specific settings).
The Vite dev server proxies to port **5000** by default.

Static files are served from **`wwwroot`**.
Build the React app in `../../frontend` before publishing so `wwwroot` contains the SPA.

## Runlet mode

Hosting as a Tinkwell runlet is planned; bridge endpoints would be supplied by runner configuration instead of local `appsettings.json`.
The `BridgeOptions` shape remains the same.

## gRPC configuration

Configure addresses in **`appsettings.json`** under `Bridge`, or via environment variables:

| Key | Environment variable |
|-----|----------------------|
| `Bridge:ProxyAddress` | `Bridge__ProxyAddress` |
| `Bridge:AssetRegistryAddress` | `Bridge__AssetRegistryAddress` |
| `Bridge:StateStoreAddress` | `Bridge__StateStoreAddress` |

Defaults point at `localhost` ports **50051**, **50052**, and **50053** for Proxy, AssetRegistry, and StateStore respectively.

## HTTP surface

| Route | Description |
|-------|-------------|
| `/` | SPA (`index.html`) |
| `/api/ui/tree` | Full resolved UI tree |
| `/api/ui/theme` | Theme object |
| `/api/ui/widgets` | Widget summaries |
| `/ws` | WebSocket (see `docs/websocket-protocol.md` in the Hub UI repo) |

`UseStaticFiles` + `MapFallbackToFile("index.html")` serve the frontend after API routes are registered.
