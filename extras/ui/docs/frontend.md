# Frontend

The UI is a React 19 + Vite 6 + TypeScript SPA under `frontend/`.
Production builds emit static assets to `src/Tinkwell.Firmwareless.Hub.Ui/wwwroot`, which Kestrel serves alongside the API.

## Component tree

| Area | Responsibility |
|------|------------------|
| `App.tsx` | Loads theme, wires `useUiTree`, provides `UiActionsContext` |
| `Shell.tsx` | Responsive chrome: sidebar or bottom nav, page tabs, Overview vs detail |
| `OverviewPage.tsx` | Renders `widgets` when the active page is named `overview` |
| `ControlRenderer.tsx` | Dispatches control `type` to concrete control components |
| `components/layout/Grid.tsx`, `Stack.tsx` | Layout containers from JSON |
| `components/controls/*` | One file per control type (`Gauge`, `Indicator`, …) |
| `hooks/useUiTree.ts` | `fetch("/api/ui/tree")`, WebSocket to `/ws`, patch-on-`update` |

Protocol types live in `src/protocol.ts` and mirror the JSON shape from `UiTreeSerializer`.

## Vite and dev server

`vite.config.ts` builds to `../src/Tinkwell.Firmwareless.Hub.Ui/wwwroot` with `emptyOutDir: true`.

Development proxy:

| Path | Target |
|------|--------|
| `/api` | `http://localhost:5000` |
| `/ws` | `ws://localhost:5000` |

Run the ASP.NET host first (`dotnet run`), then `npm run dev` in `frontend` so API and WebSocket calls resolve through the proxy.

## Theming (CSS custom properties)

`theme.ts` maps `UiTheme` from the API to document variables:

| Variable | Role |
|----------|------|
| `--tw-accent` | Accent from `ui.accent` |
| `--tw-bg` | Page background (from light/dark palette) |
| `--tw-surface` | Cards and elevated surfaces |
| `--tw-text` | Primary text |
| `--tw-font-size` | From `font-size` (`small` / `medium` / `large`) |
| `--tw-spacing` | Default padding scale (currently `0.75rem`) |

Light vs dark is derived from `theme.theme`; `color-scheme` is set on `:root` for native controls.

## Touch-first targets

Global styles enforce comfortable hit areas:

- Buttons: `min-height` / `min-width` **48px**
- Range inputs: **48px** min height, `accent-color: var(--tw-accent)`
- `.tw-control-wrap`: `min-height: 48px` baseline for controls

## Responsive layout

`Shell` uses `matchMedia("(min-width: 768px)")`:

| Viewport | Navigation |
|----------|------------|
| **≥ 768px** | **Sidebar** for groups (`aside.tw-sidebar`) |
| **< 768px** | **Bottom tab bar** (`nav.tw-bottom-nav`) |

Page tabs remain horizontal under the header.
Main content adds bottom padding on narrow viewports so content clears the fixed bottom nav.

## TypeScript

`tsconfig.json` enables **strict** mode plus `noUnusedLocals`, `noUnusedParameters`, and `noFallthroughCasesInSwitch`.
