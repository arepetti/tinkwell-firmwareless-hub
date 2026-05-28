# `ui.tw` reference

This document describes the Hub UI dialect parsed by `UiConfigParser`.
Files are standard Tinkwell `.tw` documents: named blocks, nested children, and `key = value` properties.
The parser runs with **lax** mode enabled.

## Grammar recap

- **Blocks** have a type and optional quoted name: `group "climate" { ... }`.
- **Properties** use `=`; string values are quoted; numbers and booleans are literal.
- **Expressions** are parenthesized NCalc snippets: `value = (temperature)` or `min = (setpoint - 5)`.
- **Identifiers** in expressions refer to measure names and setting keys in the same asset context.

Top-level block types recognized by the UI parser:

| Block | Purpose |
|-------|---------|
| `ui` | Hub theme and defaults (single logical document) |
| `group` | Navigation group containing `page` children |
| `widget` | Dashboard tile (shown on the Overview page) |

## Hub theme: `ui`

```tw
ui "home" {
    title = "My Home"
    theme = "dark"
    accent = "#4CAF50"
    font-size = "medium"
    locale = "en-US"
}
```

| Property | Description |
|----------|-------------|
| `title` | Shell title |
| `theme` | `dark` or `light` (used by the React theme) |
| `accent` | Accent color (CSS color string) |
| `font-size` | `small`, `medium`, or `large` |
| `locale` | BCP 47 locale; reserved for formatting and localization (see [localization.md](localization.md)) |

## Structural blocks

### `group`

A sidebar (wide) or bottom-nav (narrow) entry.
Children must be `page` blocks.

| Property | Type | Description |
|----------|------|-------------|
| `label` | string or expression | Display label |
| `icon` | string or expression | Short icon or glyph |
| `visible` | bool or expression | Default `true` |
| `order` | number | Sort order (lower first) |

### `page`

| Property | Type | Description |
|----------|------|-------------|
| `label` | string or expression | Tab label |
| `icon` | string or expression | Optional |
| `visible` | bool or expression | Default `true` |
| `order` | number | Tab order |

Children are **layouts** (`grid`, `hstack`, `vstack`) or **controls**.

### `widget`

Same navigation metadata as a group (`label`, `icon`, `order`) but no `page` children.
Widgets render on the **Overview** page (page name `overview`, case-insensitive).

## Layout blocks

| Block | Description |
|-------|-------------|
| `grid` | CSS grid; see properties below |
| `hstack` | Horizontal flex stack |
| `vstack` | Vertical flex stack |

### Layout properties

**`grid`**

| Property | Description |
|----------|-------------|
| `columns` | Number (repeat count) or CSS `grid-template-columns` string |
| `gap` | CSS gap (string or number) |

Default columns when omitted: `repeat(auto-fill, minmax(140px, 1fr))`.

**`hstack` / `vstack`**

| Property | Description |
|----------|-------------|
| `gap` | Flex gap |
| `align` | `align-items` (default `center` for hstack, `stretch` for vstack) |
| `justify` | `justify-content` |
| `wrap` | Boolean; when true, `flex-wrap: wrap` |

## Controls

Control blocks are named (`gauge "temp" { ... }`).
The parser records every property; the React layer reads only the names listed in each table.
Expression-backed properties update live when measures or settings change.

### `gauge`

Radial arc (default) or linear bar.

| Property | Description |
|----------|-------------|
| `label` | Title |
| `value` | numeric value or expression |
| `min` | range minimum |
| `max` | range maximum |
| `unit` | suffix (e.g. `°C`) |
| `style` | `radial` (default) or `linear` |

Additional keys (e.g. `color`, `enabled`) are stored and evaluated but are not used by the stock gauge renderer unless you extend the frontend.

### `indicator`

Maps a discrete `value` to label, color, and icon via a **`map`** block.

The map must be named `entries` per grammar rules.
Each row is an `entry` with a quoted key matching the runtime value (string form of booleans and numbers).

```tw
indicator "mode" {
    value = (hvac-mode)
    map "entries" {
        entry "off"  { label = "Off"  color = "gray" }
        entry "heat" { label = "Heat" color = "orange" }
        entry "cold" { label = "Cool" color = "#2196F3" }
    }
}
```

| Property | Description |
|----------|-------------|
| `value` | Current value (expression) |
| `map` | Named `map "entries"` with `entry` children |

**`entry` properties** (all optional; strings):

| Property | Description |
|----------|-------------|
| `label` | Text when this entry matches |
| `color` | Background tint for the pill |
| `icon` | Optional prefix character or emoji |

### `text`

| Property | Description |
|----------|-------------|
| `text` | Content |
| `variant` | `heading`, `body`, or `caption` (default `body`) |

### `value`

Large numeric readout.

| Property | Description |
|----------|-------------|
| `label` | Caption |
| `value` | numeric or expression |
| `unit` | Suffix |
| `decimals` | 0–8 fixed decimal places (optional) |

### `button`

| Property | Description |
|----------|-------------|
| `label` | Button text |
| `command` | If set, click sends a WebSocket `action` with this command type (no setting write) |
| `setting` | Setting key to write when not using `command` |
| `input` | With `setting`: `button` (default) or `number` |
| `min`, `max`, `step` | Reserved for `input = "number"` flows (modal entry) |

**Fallback:** If `command` is absent and `setting` is absent, the click does nothing useful.
If `setting` is set and `input` is `number`, the UI opens a modal to enter a number, then sends `set` on confirm.

### `toggle`

| Property | Description |
|----------|-------------|
| `label` | Row label |
| `setting` | Setting key (required for interactive toggle) |
| `value` | Current bool (expression or literal) |

**Fallback:** Without `setting`, the control shows a muted label and does not send updates.

### `slider`

| Property | Description |
|----------|-------------|
| `label` | Row label |
| `setting` | Setting key (required for interactive slider) |
| `value` | Current value |
| `min`, `max`, `step` | Range |
| `unit` | Display suffix |

**Fallback:** Without `setting`, the control shows a muted label only.

## Control IDs and paths

When parsing with an `assetId`, element ids are structural paths:

`{assetId}/{group}/{page}/.../{controlName}`

Widgets use `{assetId}/{widgetName}/...`.
These ids are used in WebSocket messages and for routing `set` / `action` to the correct asset.

## Full example

```tw
ui "home" {
    title = "Demo"
    theme = "dark"
    accent = "#7C4DFF"
}

group "climate" {
    label = "Climate"
    icon = "thermometer"
    order = 10

    page "living-room" {
        label = "Living Room"
        order = 0

        vstack "main" {
            gap = "0.75rem"

            gauge "temp" {
                label = "Temperature"
                value = (temperature)
                unit = "°C"
                min = 0
                max = 40
                style = "linear"
            }

            hstack "row" {
                gap = "0.5rem"
                align = "center"

                indicator "mode" {
                    value = (hvac-mode)
                    map "entries" {
                        entry "off" { label = "Off" color = "#525252" }
                        entry "heat" { label = "Heat" color = "#EA580C" }
                    }
                }

                toggle "eco" {
                    label = "Eco"
                    setting = "eco-mode"
                    value = (eco-mode)
                }
            }

            slider "setpoint" {
                label = "Setpoint"
                setting = "setpoint"
                value = (setpoint)
                min = 16
                max = 30
                step = 0.5
                unit = "°C"
            }
        }
    }

    page "overview" {
        label = "Overview"
        order = 1
    }
}

widget "summary" {
    label = "Summary"
    order = 0

    grid "grid" {
        columns = 2
        value "cpu" {
            label = "CPU"
            value = (cpu-percent)
            unit = "%"
            decimals = 0
        }
    }
}
```
