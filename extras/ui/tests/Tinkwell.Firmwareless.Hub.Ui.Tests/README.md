# Tinkwell.Firmwareless.Hub.Ui.Tests

xUnit tests for the Hub UI parsing and expression layers.

## What is covered

| Area | Tests |
|------|--------|
| `UiConfigParser` | Groups, pages, widgets, layouts, controls, `ui` theme, `map "entries"` / `entry`, expression properties, `Merge`, id paths with `assetId` |
| `SettingsParser` | All primary setting types (`number`, `bool`, `string`, `object`) and constraint fields |
| `UiExpressionEngine` | Dependency extraction, re-evaluation on value change, `EvaluateAllAsync` |
| `UiConfigDiscovery` | `LoadFromFilesAsync` merging `ui.tw` and `settings.tw` |

## Run

From the Hub UI solution root (`extras/firmwareless/hub/extras/ui`):

```bash
dotnet test Tinkwell.Firmwareless.Hub.Ui.slnx
```

Or filter to this project:

```bash
dotnet test tests/Tinkwell.Firmwareless.Hub.Ui.Tests/Tinkwell.Firmwareless.Hub.Ui.Tests.csproj
```

## Framework

- **xUnit** for test discovery and execution
- **FluentAssertions** for readable assertions (where referenced in the project)
