# Tinkwell Firmwareless Hub UI

Declarative, configuration-driven smart home UI for the Tinkwell Firmwareless Hub.
Firmlet packages contribute pages, widgets, and controls via `.tw` configuration files.
All control properties are NCalc expression-driven.
The backend (ASP.NET) produces a platform-neutral JSON UI tree; the frontend (React) is a thin renderer.

```
Firmlet Packages (ui.tw, settings.tw)
         |
    AssetRegistry (gRPC)
         |
   +-----+-----+
   |  UI Runlet |  (ASP.NET)
   | - Discovery|
   | - Parser   |
   | - Expr Eng |
   | - Bridges  |
   | - REST API |
   | - WebSocket|
   +-----+-----+
         |
   +-----+-----+
   |  Frontend  |  (React + Vite)
   | - Shell    |
   | - Controls |
   | - Layouts  |
   +-----------+
```

## Quick Start

```bash
# Backend
cd src/Tinkwell.Firmwareless.Hub.Ui
dotnet run

# Frontend (dev)
cd frontend
npm install
npm run dev

# Full build
cd frontend && npm run build
cd ../.. && dotnet build Tinkwell.Firmwareless.Hub.Ui.slnx
```

## Projects

| Project | Description |
|---------|-------------|
| `src/Tinkwell.Firmwareless.Hub.Ui` | ASP.NET backend: config parsing, expression engine, gRPC bridges, REST/WS API |
| `frontend` | React + Vite + TypeScript frontend: control renderers, layouts, navigation shell |
| `tests/Tinkwell.Firmwareless.Hub.Ui.Tests` | Unit tests for parsers, expression engine, discovery |

## Documentation

| Document | Description |
|----------|-------------|
| [docs/README.md](docs/README.md) | Documentation index |
| [docs/architecture.md](docs/architecture.md) | Backend design, bridges, expression engine, discovery |
| [docs/frontend.md](docs/frontend.md) | React shell, Vite, theming, responsive layout |
| [docs/tw-ui-reference.md](docs/tw-ui-reference.md) | `ui.tw` grammar: groups, pages, layouts, controls |
| [docs/tw-settings-reference.md](docs/tw-settings-reference.md) | `settings.tw` setting types and constraints |
| [docs/websocket-protocol.md](docs/websocket-protocol.md) | WebSocket message types and payloads |
| [docs/localization.md](docs/localization.md) | Locale overlays and server-side formatting |
| [frontend/README.md](frontend/README.md) | Frontend prerequisites and scripts |
| [src/Tinkwell.Firmwareless.Hub.Ui/README.md](src/Tinkwell.Firmwareless.Hub.Ui/README.md) | Backend run modes and configuration |
| [tests/Tinkwell.Firmwareless.Hub.Ui.Tests/README.md](tests/Tinkwell.Firmwareless.Hub.Ui.Tests/README.md) | Test coverage and how to run |

## Environment Variables

Configuration uses the `Bridge` section (`BridgeOptions`).
Environment variables follow ASP.NET Core conventions (double underscore for nesting).

| Variable | Default | Description |
|----------|---------|-------------|
| `Bridge__ProxyAddress` | `http://localhost:50051` | FirmwarelessProxy gRPC endpoint |
| `Bridge__AssetRegistryAddress` | `http://localhost:50052` | AssetRegistry gRPC endpoint |
| `Bridge__StateStoreAddress` | `http://localhost:50053` | StateStore gRPC endpoint |

## Related Repositories

- [tinkwell-firmwareless](https://github.com/arepetti/tinkwell-firmwareless) — Firmwareless platform umbrella
- [tinkwell-firmwareless-hub](https://github.com/arepetti/tinkwell-firmwareless-hub) — Edge hub (parent project)
- [Tinkwell](https://github.com/arepetti/tinkwell) — Core runtime and coordinator

## Version

![Version](https://img.shields.io/badge/version-v0.1.0-blue)
