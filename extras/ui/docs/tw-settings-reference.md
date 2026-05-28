# `settings.tw` reference

`SettingsParser` reads a `settings.tw` file into a dictionary of `SettingDefinition` entries.
Keys are merged into `UiDocument.Settings` and used to:

- Validate and interpret values read from StateStore
- Provide defaults when a key is missing in the store
- Supply metadata for interactive controls (`slider`, `toggle`, `button` with `setting`)

## Block shape

Each setting is a top-level block:

```tw
setting "setpoint" {
    type = "number"
    default = 21
    min = 16
    max = 30
    step = 0.5
    unit = "°C"
    description = "Target temperature"
}
```

The block **name** (quoted) is the setting key used in expressions and WebSocket `set` messages.

## Supported types

| `type` | `SettingType` | Stored in StateStore |
|--------|-----------------|----------------------|
| `number` | Number | Stringified with invariant culture |
| `bool` | Bool | `"true"` / `"false"` |
| `string` | String | As-is |
| `date` | Date | ISO date string (consumer-defined) |
| `time` | Time | Time string (consumer-defined) |
| `timespan` | Timespan | Duration string (consumer-defined) |
| `object` | Object | JSON or opaque string; `schema` names the shape |

Unknown `type` values fall back to **string**.

## Constraints by type

### `number`

| Property | Description |
|----------|-------------|
| `default` | Initial value when not in store |
| `min` | Minimum (inclusive) |
| `max` | Maximum (inclusive) |
| `step` | Suggested step for sliders |
| `unit` | Display unit |
| `description` | Help text |

### `bool`

| Property | Description |
|----------|-------------|
| `default` | `true` or `false` |
| `description` | Help text |

### `string`

| Property | Description |
|----------|-------------|
| `default` | Default string |
| `min-length` | Minimum length |
| `max-length` | Maximum length |
| `pattern` | Regex pattern hint |
| `description` | Help text |

### `date`, `time`, `timespan`

| Property | Description |
|----------|-------------|
| `default` | Optional default |
| `description` | Help text |

Use these types when values are not plain numbers or booleans but should still be addressable as single keys in the store.
Parsing to CLR types beyond string is left to future firmlet-specific tooling; the Hub UI passes strings through for non-number/bool types when loading context.

### `object`

| Property | Description |
|----------|-------------|
| `schema` | Logical schema name (e.g. `weekly-schedule`) for documentation and future validation |
| `description` | Help text |

## Fallback behavior for `slider` and `button`

| Control | When `setting` is missing |
|---------|---------------------------|
| `slider` | Renders label only; no writes |
| `toggle` | Same |
| `button` | If `command` is set, only `action` is sent; if neither `command` nor `setting`, click does nothing |

When `setting` is present but the key is absent in StateStore, `StoreBridge.LoadSettingsAsync` applies `default` from the definition into the expression context (when provided).

## StateStore mapping

All reads and writes use:

| Field | Value |
|-------|--------|
| `BucketId` | Asset id (`assetId` from discovery / control id prefix) |
| `KeyNamespace` | `"settings"` |
| `Key` | Setting name from the `setting "name"` block |

This matches `StoreBridge` in the Hub UI host.

## Example file

```tw
setting "setpoint" {
    type = "number"
    default = 21
    min = 16
    max = 30
    step = 0.5
    unit = "°C"
}

setting "eco-mode" {
    type = "bool"
    default = false
}

setting "zone-name" {
    type = "string"
    default = "Living Room"
    max-length = 64
}

setting "weekly-schedule" {
    type = "object"
    schema = "weekly-schedule"
}
```
