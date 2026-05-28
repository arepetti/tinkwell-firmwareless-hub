# Hub UI frontend

React + Vite + TypeScript SPA for the Tinkwell Firmwareless Hub.
It renders the JSON UI tree from the ASP.NET host and connects over WebSocket for live property updates.

## Prerequisites

- **Node.js 20+** (LTS recommended)
- npm (bundled with Node)

## Setup

```bash
npm install
```

## Development

Run the backend from `../src/Tinkwell.Firmwareless.Hub.Ui` (`dotnet run`) so Kestrel listens on the URL expected by the Vite proxy (default `http://localhost:5000`).

```bash
npm run dev
```

Vite proxies `/api` and `/ws` to that host (see `vite.config.ts`).

## Production build

```bash
npm run build
```

Runs `tsc --noEmit` then `vite build`.
Output is written to **`../src/Tinkwell.Firmwareless.Hub.Ui/wwwroot`**, which the ASP.NET app serves as static files.

```bash
npm run preview
```

Serves the built assets locally (useful for smoke-testing the bundle; API calls still need the backend).

## Project structure

| Path | Purpose |
|------|---------|
| `src/App.tsx` | Root layout, theme application, WebSocket actions |
| `src/components/Shell.tsx` | Responsive navigation and page body |
| `src/components/ControlRenderer.tsx` | Control type dispatch |
| `src/components/controls/` | Gauge, indicator, text, value, button, toggle, slider |
| `src/components/layout/` | Grid, stack layouts |
| `src/hooks/useUiTree.ts` | Initial tree fetch + WebSocket state |
| `src/protocol.ts` | TypeScript types for tree and WS messages |
| `src/theme.ts` | Maps API theme to CSS variables |
| `src/index.css` | Global styles and design tokens |

## TypeScript

`tsconfig.json` enables **strict** mode, unused symbol checks, and switch exhaustiveness helpers.
Keep new components aligned with existing patterns in `components/`.
