# WebSocket protocol

Clients connect to **`/ws`**.
Messages are UTF-8 JSON with a required `type` discriminator.
Property names use **camelCase** in transit (`JsonNamingPolicy.CamelCase` on the server).

## Server to client

### `update`

Sent when an expression-backed property changes after a measure or setting update.
One message per changed property.

**JSON shape**

| Field | Type | Description |
|-------|------|-------------|
| `type` | `"update"` | Discriminator |
| `controlId` | string | Full structural id (includes asset prefix) |
| `property` | string | Property name on the control (e.g. `value`, `label`) |
| `value` | any | Resolved value after evaluation |

**Example**

```json
{
  "type": "update",
  "controlId": "hvac-1/climate/living-room/main/temp",
  "property": "value",
  "value": 22.5
}
```

### `tree`

Full UI tree replacement (same shape as `GET /api/ui/tree`).

**JSON shape**

| Field | Type | Description |
|-------|------|-------------|
| `type` | `"tree"` | Discriminator |
| `tree` | object | `{ theme, groups, widgets }` |

**Example (abbreviated)**

```json
{
  "type": "tree",
  "tree": {
    "theme": {
      "title": "Home",
      "theme": "dark",
      "accent": "#4CAF50",
      "fontSize": "medium",
      "locale": "en-US"
    },
    "groups": [],
    "widgets": []
  }
}
```

The React client applies `update` by deep-patching the matching control’s `properties[property]`; `tree` replaces the entire in-memory tree.

## Client to server

### `set`

Writes a setting through `StoreBridge` for the asset inferred from `controlId` (text before the first `/`).

**JSON shape**

| Field | Type | Description |
|-------|------|-------------|
| `type` | `"set"` | Discriminator |
| `controlId` | string | Must include `{assetId}/...` prefix |
| `setting` | string | Setting key (matches `settings.tw` name) |
| `value` | string, number, or boolean | Serialized to string for StateStore |

**Examples**

```json
{
  "type": "set",
  "controlId": "hvac-1/climate/living-room/dimmer",
  "setting": "brightness",
  "value": 72
}
```

```json
{
  "type": "set",
  "controlId": "hvac-1/climate/living-room/lights",
  "setting": "lights-on",
  "value": true
}
```

### `action`

Enqueues a firmlet command via `CommandBridge` / AssetRegistry.

**JSON shape**

| Field | Type | Description |
|-------|------|-------------|
| `type` | `"action"` | Discriminator |
| `controlId` | string | Asset id extracted from prefix |
| `command` | string | Command type registered with the asset |

**Example**

```json
{
  "type": "action",
  "controlId": "hvac-1/climate/living-room/reboot",
  "command": "system.reboot"
}
```

If `command` is omitted, the server does not enqueue a command.

## JSON Schema summaries

**Inbound (`update` / `tree`)**

```json
{
  "oneOf": [
    {
      "type": "object",
      "required": ["type", "controlId", "property"],
      "properties": {
        "type": { "const": "update" },
        "controlId": { "type": "string" },
        "property": { "type": "string" },
        "value": {}
      }
    },
    {
      "type": "object",
      "required": ["type", "tree"],
      "properties": {
        "type": { "const": "tree" },
        "tree": { "type": "object" }
      }
    }
  ]
}
```

**Outbound (`set` / `action`)**

```json
{
  "oneOf": [
    {
      "type": "object",
      "required": ["type", "controlId", "setting", "value"],
      "properties": {
        "type": { "const": "set" },
        "controlId": { "type": "string" },
        "setting": { "type": "string" },
        "value": { "type": ["string", "number", "boolean", "null"] }
      }
    },
    {
      "type": "object",
      "required": ["type", "controlId", "command"],
      "properties": {
        "type": { "const": "action" },
        "controlId": { "type": "string" },
        "command": { "type": "string" }
      }
    }
  ]
}
```
