# Localization

The Hub UI separates **structure** (defined in `ui.tw`) from **translated strings**.
The recommended pattern is a **structural overlay**: locale-specific `.tw` files that repeat the same block hierarchy as the default config but replace only human-readable properties.

## Locale files

Place optional overlays next to firmlet content:

```
content/config/locales/en-US.tw
content/config/locales/it-IT.tw
```

Each file uses the same block types (`group`, `page`, `widget`, controls) and the same names as the base `ui.tw`.
Only properties that carry user-visible text need to be present in the overlay; the merge strategy is **by structural path**:

1. Parse the default `ui.tw` for the asset.
2. If `ui.theme.locale` (or a hub-level setting) selects a locale, parse the matching `locales/{locale}.tw`.
3. For each block at path `group "climate" / page "living-room" / text "title"`, overlay properties from the locale file when the path and block type match.

Unspecified properties keep their default-language values.

## Matching rules

| Key | Rule |
|-----|------|
| Group / page / widget / control **name** | Must match exactly (same quoted identifier) |
| Nesting | Parent chain must match (group → page → layout → control) |
| Asset scope | Overlays apply per asset id when configs are merged in discovery |

Expressions can remain in the base file; overlays typically replace **static** `label`, `text`, and `description` fields only.

## Fallback

1. Requested locale file missing: use base `ui.tw` strings.
2. Block missing in overlay: inherit all properties from base.
3. Property missing in overlay: inherit that property from base.

## Number and date formatting

Numeric and date **display** should stay server-side where possible:

- The `ui` block’s `locale` drives `UiTheme.Locale` and is serialized to `/api/ui/theme`.
- Resolved string properties in `/api/ui/tree` should use that locale for formatting when expressions return `DateTime` or numeric values that are shown as text (future tightening of `UiTreeSerializer`).

The React client treats most resolved values as opaque strings or numbers; locale-aware formatting belongs in the expression layer or serializer so all clients stay consistent.

## Example overlay

**Base `ui.tw` (excerpt):**

```tw
group "climate" {
    label = "Climate"
    page "living" {
        label = "Living Room"
        text "welcome" {
            text = "Welcome home"
            variant = "heading"
        }
    }
}
```

**`locales/it-IT.tw` (excerpt):**

```tw
group "climate" {
    label = "Clima"
    page "living" {
        label = "Soggiorno"
        text "welcome" {
            text = "Benvenuto a casa"
            variant = "heading"
        }
    }
}
```

The structural paths align; only literals change.
