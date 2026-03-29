# Phase 78D -- CLI and UI Integration

> **Status**: Complete
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

## Design Decision: Backend Assembly Loading via Compile-Time `#if`

During implementation we identified that adding project references to the backend packages is not sufficient to make them register -- the CLR lazy-loads assemblies, so `[ModuleInitializer]` in the backend assemblies won't fire unless something forces the assembly to load.

**Options considered:**
1. ~~Runtime `Assembly.Load()` with try/catch~~ -- fragile, reflection-based
2. ~~Directory scanning for `KernSmith.Rasterizers.*.dll`~~ -- overly generic, unnecessary plugin architecture
3. ~~`OperatingSystem.IsWindows()` guard + `Assembly.Load()`~~ -- redundant since the build is already platform-specific
4. **Compile-time `#if WINDOWS` with direct type reference** -- chosen approach

**Rationale:** The UI and CLI are purpose-built per platform (`dotnet publish -r win-x64`, `linux-x64`, etc.). Since the build output is already platform-specific, compile-time `#if` is the simplest and safest approach. No reflection, no try/catch, no runtime overhead. The Phase 78 design decision "TFMs over `#if`" (decision #10) applies to the backend packages' own code, not to how consuming apps wire up references.

**Implementation pattern for UI and CLI `.csproj` files:**
```xml
<!-- Cross-platform by default, Windows TFM on Windows (enables #if WINDOWS) -->
<TargetFramework Condition="'$(OS)' != 'Windows_NT'">net10.0</TargetFramework>
<TargetFramework Condition="'$(OS)' == 'Windows_NT'">net10.0-windows</TargetFramework>

<!-- Backend references only included in Windows builds -->
<ItemGroup Condition="'$(TargetPlatformIdentifier)' == 'windows'">
  <ProjectReference Include="..\..\src\KernSmith.Rasterizers.Gdi\KernSmith.Rasterizers.Gdi.csproj" />
  <ProjectReference Include="..\..\src\KernSmith.Rasterizers.DirectWrite.TerraFX\KernSmith.Rasterizers.DirectWrite.TerraFX.csproj" />
</ItemGroup>
```

**Startup code (Program.cs or equivalent):**
```csharp
#if WINDOWS
// Force assembly load so [ModuleInitializer] registers the backends
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
    typeof(KernSmith.Rasterizers.Gdi.GdiRasterizer).Module.ModuleHandle);
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
    typeof(KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer).Module.ModuleHandle);
#endif
```

**Result:**
- Windows: all 3 backends available in dropdown / `--list-rasterizers`
- Linux/Mac/mobile/web: FreeType only, zero Windows-specific code compiled

## Tasks

### 1. CLI: `--rasterizer` Flag ✅

File: `tools/KernSmith.Cli/Commands/GenerateCommand.cs`

- Added `--rasterizer freetype|gdi|directwrite` flag in `ParseArgs()`
- Maps to `CliOptions.Backend` → `FontGeneratorOptions.Backend` in `BuildGenOptions()`
- Handled in `MergeConfigIntoOptions()` for .bmfc config overlay
- Early validation in `Execute()`: checks `RasterizerFactory.GetAvailableBackends()` and errors with available list if backend unavailable
- Help text updated

### 2. CLI: `--list-rasterizers` ✅

- New `ListRasterizersCommand.cs` prints available backends with platform and capability info
- Wired into `Program.cs` command dispatch as `list-rasterizers`
- Added to top-level help text

### 3. UI: Rasterizer Dropdown ✅

- ComboBox added to `FontConfigPanel.cs` in the font configuration section
- Populated from `FontConfigViewModel.AvailableBackends` (via `RasterizerFactory.GetAvailableBackends()`)
- Bound to `FontConfigViewModel.SelectedBackend`
- Uses named method `OnRasterizerComboSelectionChanged()` (not anonymous lambda)

### 4. Add `IsRegistered(RasterizerBackend)` to `RasterizerFactory` ✅

- Added `public static bool IsRegistered(RasterizerBackend backend)` method

### 5. UI: Capability-Aware Options ✅

- `FontConfigViewModel` exposes `BackendSupportsColorFonts`, `BackendSupportsVariableFonts`, `BackendSupportsSdf`, `BackendSupportsSystemFonts` properties
- Capabilities refresh when `SelectedBackend` changes (creates temporary rasterizer, reads capabilities, disposes)
- `MainViewModel` propagates capability changes from `FontConfigViewModel` → `EffectsViewModel`
- `EffectsPanel`: SDF checkbox disabled when `!BackendSupportsSdf`, color font checkbox gated on `BackendSupportsColorFonts`, variable font axis sliders disabled when `!BackendSupportsVariableFonts` with warning label
- System font section visibility in `FontConfigPanel` tied to `BackendSupportsSystemFonts`

### 6. UI: Linux Native Info Message

Not needed -- there is no `Auto` or `Native` backend value. The dropdown only shows actually-available backends (FreeType only on Linux). No misleading options to explain.

### 7. CLI: `--supersample` Flag ✅ (pre-existing)

Already implemented as `--super-sample <n>` in a prior phase. Maps to `CliOptions.SuperSampleLevel` → `FontGeneratorOptions.SuperSampleLevel`.

### 8. CLI: `--system-font` Flag ✅ (pre-existing)

Already implemented as `--system-font <name>` in a prior phase. Maps to `CliOptions.SystemFontName`, handled in `Execute()` via `BmFont.GenerateFromSystem()`.

### 9. UI: System Font Picker ✅ (pre-existing, now capability-gated)

System font ComboBox already existed in `FontConfigPanel`. Now its visibility is tied to `BackendSupportsSystemFonts` -- hidden when the selected backend doesn't support system fonts.

### 10. Validation ✅

- CLI: early validation in `Execute()` checks requested backend against `GetAvailableBackends()`, errors with list of alternatives
- UI: dropdown only shows available backends (invalid selection impossible)

### 11. Backend Assembly Wiring ✅

Both UI and CLI `.csproj` files need:
- [x] Conditional TFM: `net10.0-windows` on Windows, `net10.0` elsewhere
- [x] Windows-only project references to GDI and DirectWrite backend packages
- [x] `#if WINDOWS` type reference in startup to force `[ModuleInitializer]` execution

See "Design Decision: Backend Assembly Loading" section above for implementation pattern.

### 12. BmFontBuilder.WithBackend() ✅

- Added `WithBackend(RasterizerBackend)` fluent method to `BmFontBuilder`
- `GenerationService` calls `.WithBackend(request.Backend)` in the builder chain
- `GenerationRequest` includes `Backend` property, populated from `FontConfigViewModel.SelectedBackend`

## Files Changed

| File | Change | Status |
|------|--------|--------|
| `src/KernSmith/Rasterizer/RasterizerFactory.cs` | Added `IsRegistered(RasterizerBackend)` method | ✅ |
| `src/KernSmith/BmFontBuilder.cs` | Added `WithBackend(RasterizerBackend)` fluent method | ✅ |
| `tools/KernSmith.Cli/Commands/GenerateCommand.cs` | `--rasterizer` flag, Backend in BuildGenOptions/MergeConfigIntoOptions, validation, help text | ✅ |
| `tools/KernSmith.Cli/Commands/ListRasterizersCommand.cs` | New command: list available backends with capabilities | ✅ |
| `tools/KernSmith.Cli/Program.cs` | Wired `list-rasterizers` command, updated help | ✅ |
| `apps/KernSmith.Ui/ViewModels/FontConfigViewModel.cs` | `SelectedBackend`, `AvailableBackends`, `BackendSupports*` capability properties | ✅ |
| `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` | Rasterizer dropdown, system font visibility tied to capability | ✅ |
| `apps/KernSmith.Ui/ViewModels/EffectsViewModel.cs` | `BackendSupports*` bindable properties | ✅ |
| `apps/KernSmith.Ui/Layout/EffectsPanel.cs` | Capability-aware SDF/ColorFont/VariableFont disabling | ✅ |
| `apps/KernSmith.Ui/ViewModels/MainViewModel.cs` | Capability propagation FontConfigVM→EffectsVM, Backend in GenerationRequest | ✅ |
| `apps/KernSmith.Ui/Models/GenerationRequest.cs` | Added `Backend` property | ✅ |
| `apps/KernSmith.Ui/Services/GenerationService.cs` | `.WithBackend(request.Backend)` in builder chain | ✅ |
| `apps/KernSmith.Ui/KernSmith.Ui.csproj` | Conditional TFM + Windows backend references | ✅ |
| `tools/KernSmith.Cli/KernSmith.Cli.csproj` | Conditional TFM + Windows backend references | ✅ |
| UI/CLI startup code | `#if WINDOWS` type references to force backend loading | ✅ |

## Review Findings (2026-03-28)

**All issues below were resolved in implementation (verified 2026-03-28).**

Issues discovered during QA review of implemented code:

### HIGH — Must fix before merge

1. **`CopyOptions()` missing `Backend` property** — `BmFontBuilder.CopyOptions()` copies every `FontGeneratorOptions` property except `Backend`. Means `BmFont.Builder().FromConfig()` silently drops the backend setting. Fix: add `_options.Backend = source.Backend;` near the `Rasterizer` copy line.

2. **`WINDOWS` preprocessor symbol not auto-defined** — `net10.0-windows` TFM does NOT auto-define `WINDOWS`. Need explicit `<DefineConstants>$(DefineConstants);WINDOWS</DefineConstants>` block conditioned on `$(TargetFramework.Contains('-windows'))`. Pattern already exists in `tests/KernSmith.Tests/KernSmith.Tests.csproj`.

3. **`TargetFramework` (singular) vs `TargetFrameworks` (plural) conflict** — CLI inherits `<TargetFrameworks>` (plural) from `Directory.Build.props`. Using singular `<TargetFramework>` causes NETSDK1005. Must override the plural form. Pattern already exists in `tests/KernSmith.Tests/KernSmith.Tests.csproj`.

### MEDIUM — Should fix

4. **Use `RuntimeHelpers.RunModuleConstructor()` instead of `typeof()`** — `typeof()` may not reliably trigger `[ModuleInitializer]` in all scenarios (NativeAOT, trimming). `RuntimeHelpers.RunModuleConstructor(typeof(T).Module.ModuleHandle)` is the guaranteed API. The test code already uses `RuntimeHelpers.RunClassConstructor()` with a defensive fallback showing the team hit this before.

5. **Hardcoded capabilities in `ListRasterizersCommand`** — Claims DirectWrite supports "Color, Variable" but actual capabilities are `SupportsColorFonts => false`, `SupportsVariableFonts => false`. Should query actual `IRasterizerCapabilities` at runtime instead of hardcoding.

6. **Bare `catch` in `RefreshBackendCapabilities()`** — Silently sets everything to false on failure. Should at minimum log the exception via `Debug.WriteLine()`.

7. **CI/publish workflow TFM** — `.github/workflows/publish.yml` hardcodes `-f net10.0` for CLI/UI publish. Windows builds need `-f net10.0-windows` to include backends.

### LOW — Known limitations

8. **`MergeConfigIntoOptions` cannot override `Backend` back to FreeType** — `--rasterizer freetype` on CLI won't override a non-FreeType config because the value equals the default. Consistent with existing merge pattern for other flags.

9. **`AvailableBackends` is a snapshot** — Populated once at `FontConfigViewModel` construction. If backends register later, the dropdown won't update. Not an issue in practice since registration happens at startup.

## Testing

- [x] CLI: verify `--rasterizer freetype` produces output (same as default)
- [x] CLI: verify `--rasterizer gdi` works on Windows (errors on non-Windows)
- [x] CLI: verify `--rasterizer directwrite` works on Windows (errors on non-Windows)
- [x] CLI: verify `--rasterizer invalid` errors with helpful message listing available backends
- [x] CLI: verify `--list-rasterizers` output is correct on Windows (shows all 3) and Linux (shows FreeType only)
- [x] UI: verify dropdown populates with available backends per platform
- [x] UI: verify capability-dependent options disable/enable correctly when switching backends
- [x] UI: verify system font picker visibility toggles with backend SupportsSystemFonts
- [x] Build: verify `dotnet build` succeeds on Windows (all backends compiled)
- [x] Build: verify `dotnet build` succeeds on Linux (backends excluded, no errors)
