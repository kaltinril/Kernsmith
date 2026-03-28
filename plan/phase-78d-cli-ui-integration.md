# Phase 78D -- CLI and UI Integration

> **Status**: Planning
> **Size**: Small
> **Created**: 2026-03-25
> **Dependencies**: Phase 78A (foundation), at least one backend (78B or 78C) for meaningful testing
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Expose rasterizer backend selection in the CLI tool and UI application, with capability-aware option presentation.

---

## Key Design Context

- **Only show backends that are actually available** -- based on installed packages AND current platform. Use `RasterizerFactory.GetAvailableBackends()` for runtime discovery.
- **Linux users see FreeType only.** There is no separate "Linux native" backend -- native on Linux IS FreeType. The UI/CLI should not present a misleading "Native" option that resolves to the same thing.

## Lessons from 78B/78BB

- **SuperSampleLevel needs CLI exposure**: 78BB added `SuperSampleLevel` to `FontGeneratorOptions` and the bmfc `aa` key now maps to it. The CLI needs a `--supersample` flag (integer, default 1).
- **System font loading path exists**: 78BB added `LoadSystemFont(string familyName)` for backends that support it. CLI may benefit from a `--system-font <name>` flag that uses the system font path instead of requiring a TTF file path. UI should offer a system font picker when the selected backend supports it (`SupportsSystemFonts` capability).

## Tasks

### 1. CLI: `--rasterizer` Flag

File: `tools/KernSmith.Cli/` (generate command)

Add `--rasterizer auto|freetype|gdi|directwrite` option:
- Maps to `FontGeneratorOptions.Backend`
- Only show available options based on platform and installed packages (use `RasterizerFactory.GetAvailableBackends()`)
- Default: `freetype` (matches current behavior)
- Validate early: if user requests an unavailable backend, error with a clear message listing available backends

### 2. CLI: `--list-rasterizers`

Add a `--list-rasterizers` flag (or subcommand) that prints:
- Available backends with their capabilities
- Which backend is the default
- Platform information

Example output:
```
Available rasterizer backends:
  freetype     (default)  All platforms    Color, Variable, SDF, Outline
  gdi                     Windows only     Grayscale only
  directwrite             Windows only     Color, Variable
```

### 3. UI: Rasterizer Dropdown

File: `apps/KernSmith.Ui/` (font config panel)

- Add a dropdown/combo box for rasterizer selection in the font configuration panel
- Populate from `RasterizerFactory.GetAvailableBackends()`
- Default selection: `FreeType`
- Persist selection in project file

### 4. Add `IsRegistered(RasterizerBackend)` to `RasterizerFactory`

Add an `IsRegistered(RasterizerBackend)` method to `RasterizerFactory` that returns `bool`. This enables the UI to check backend availability for capability-aware option graying without catching exceptions or iterating `GetAvailableBackends()`. Deferred from Phase 78A since it's only needed for UI integration.

### 5. UI: Capability-Aware Options

Use `IRasterizerCapabilities` to gray out or disable options not supported by the selected backend:
- If backend doesn't support color fonts, disable color font toggle
- If backend doesn't support variable fonts, disable variation axis controls
- If backend doesn't support SDF, disable SDF option
- Show tooltip explaining why the option is disabled (e.g., "SDF is not supported by the GDI backend")

### 6. UI: Linux Native Info Message

When `Native` or `Auto` resolves to FreeType on Linux, show an informational message:
- "Native rasterizer on Linux uses FreeType (same as the default)"
- Non-blocking info, not an error

### 7. CLI: `--supersample` Flag

File: `tools/KernSmith.Cli/Commands/GenerateCommand.cs`

Add `--supersample <level>` option:
- Integer value, default 1 (no supersampling)
- Maps to `FontGeneratorOptions.SuperSampleLevel`
- Validate: must be >= 1

### 8. CLI: `--system-font` Flag

File: `tools/KernSmith.Cli/Commands/GenerateCommand.cs`

Add `--system-font <name>` option as an alternative to the positional font file path:
- Mutually exclusive with the font file path argument
- Only valid when the selected backend reports `SupportsSystemFonts = true`
- Error with a clear message if the backend does not support system fonts

### 9. UI: System Font Picker

File: `apps/KernSmith.Ui/` (font config panel)

- When the selected backend reports `SupportsSystemFonts = true`, show a system font picker
- Allow the user to choose between loading from file or selecting a system font
- Disable system font picker when the backend does not support it

### 10. Validation

Both CLI and UI should validate early:
- If the requested backend is not available (package not installed or wrong platform), error immediately with a helpful message
- List available alternatives in the error message
- Use `RasterizerFactory.GetAvailableBackends()` for runtime availability checks

## Files Changed

| File | Change |
|------|--------|
| `tools/KernSmith.Cli/Commands/GenerateCommand.cs` | Add `--rasterizer` and `--list-rasterizers` options |
| `apps/KernSmith.Ui/` (font config panel) | Add rasterizer dropdown, capability-aware option disabling |
| `apps/KernSmith.Ui/` (project service) | Persist rasterizer selection |
| `tools/KernSmith.Cli/Commands/GenerateCommand.cs` | Add `--supersample` and `--system-font` options |
| `apps/KernSmith.Ui/` (font config panel) | Add system font picker when backend supports it |

## Testing

- CLI: verify `--rasterizer freetype` produces output (same as default)
- CLI: verify `--rasterizer gdi` errors gracefully on non-Windows or when package is not installed
- CLI: verify `--list-rasterizers` output is correct
- UI: verify dropdown populates with available backends
- UI: verify capability-dependent options are disabled/enabled correctly when switching backends
- CLI: verify `--supersample 4` sets SuperSampleLevel correctly
- CLI: verify `--system-font "Arial"` works with GDI backend and errors with FreeType backend
- UI: verify system font picker is shown/hidden based on backend SupportsSystemFonts capability
