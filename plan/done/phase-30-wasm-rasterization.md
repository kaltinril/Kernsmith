# Phase 30 — Extract FreeType Rasterizer to Plugin

> **Status**: Complete
> **Created**: 2026-03-20
> **Updated**: 2026-03-30
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39), Phase 32 (StbTrueType plugin), Phase 33 (WASM validation)

## Problem

FreeTypeRasterizer and FreeTypeSharp are embedded in the core `KernSmith` library (`src/KernSmith/`). This means:

- **Every consumer takes a native dependency** — even platforms that can't load native libraries (Blazor WASM, iOS AOT, serverless)
- **The core NuGet package ships FreeType native binaries** (~12 MB) whether the user needs them or not
- **WASM is blocked** — `FreeTypeSharp.FT.ImportResolver` throws `PlatformNotSupportedException` in the browser sandbox

GDI and DirectWrite rasterizers are already proper plugins (separate assemblies with `[ModuleInitializer]` registration). FreeType should follow the same pattern.

## Current State

| Component | Location | Status |
|-----------|----------|--------|
| `IRasterizer` interface | `src/KernSmith/Rasterizer/IRasterizer.cs` | Core (correct) |
| `IRasterizerCapabilities` | `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs` | Core (correct) |
| `RasterizerFactory` | `src/KernSmith/Rasterizer/RasterizerFactory.cs` | Core (correct) |
| `RasterizedGlyph`, `RasterOptions` | `src/KernSmith/Rasterizer/` | Core (correct) |
| **`FreeTypeRasterizer`** | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` | **Core — must move** |
| **`FreeTypeNative`** | `src/KernSmith/Rasterizer/FreeTypeNative.cs` | **Core — must move** |
| **FreeTypeSharp NuGet ref** | `src/KernSmith/KernSmith.csproj` | **Core — must move** |
| `GdiRasterizer` | `src/KernSmith.Rasterizers.Gdi/` | Plugin (model to follow) |
| `DirectWriteRasterizer` | `src/KernSmith.Rasterizers.DirectWrite.TerraFX/` | Plugin (model to follow) |

### Key Coupling Points

1. **`RasterizerFactory` static constructor** pre-registers `FreeTypeRasterizer` — must change to `[ModuleInitializer]` pattern
2. **`BmFont.cs` line ~1210** — `CompositeWithFtStroker()` references FreeType-specific outline stroking
3. **Tests** — `ColorFontTests.cs` and `VariableFontTests.cs` directly instantiate `FreeTypeRasterizer`
4. **`InternalsVisibleTo`** — Tests and CLI have `InternalsVisibleTo` access to internal types
5. **`AllowUnsafeBlocks`** — Currently enabled on core project for FreeType interop only

## Implementation Plan

### Step 1: Create `KernSmith.Rasterizers.FreeType` project

Create a new project following the GDI/DirectWrite plugin pattern:

```
src/KernSmith.Rasterizers.FreeType/
├── KernSmith.Rasterizers.FreeType.csproj
├── FreeTypeRasterizer.cs          (moved from core)
├── FreeTypeNative.cs              (moved from core)
├── FreeTypeCapabilities.cs        (extracted from FreeTypeRasterizer)
└── FreeTypeRegistration.cs        (new — [ModuleInitializer])
```

**Project file requirements:**
- Target: `net8.0;net10.0` (match core)
- Dependencies: `KernSmith` (core) + `FreeTypeSharp` 3.1.0
- `AllowUnsafeBlocks`: true
- NuGet package: `KernSmith.Rasterizers.FreeType`
- Namespace: `KernSmith.Rasterizers.FreeType`

### Step 2: Move FreeType files out of core

1. Move `FreeTypeRasterizer.cs` and `FreeTypeNative.cs` to the new project
2. Move or extract the `FreeTypeCapabilities` inner class (currently a `private sealed class` nested inside `FreeTypeRasterizer`) into its own file
3. Check `FreeTypeException` — it is only used inside `FreeTypeRasterizer.cs`, so move it to the new plugin project
4. Change `FreeTypeRasterizer` from `internal sealed` to `public sealed`
5. Update namespace from `KernSmith.Rasterizer` to `KernSmith.Rasterizers.FreeType`
6. Add `using KernSmith.Rasterizer;` for core interfaces (`IRasterizer`, `IRasterizerCapabilities`, etc.)
7. Remove `FreeTypeSharp` dependency from `KernSmith.csproj`
8. Remove `AllowUnsafeBlocks` from `KernSmith.csproj` (if no other unsafe code remains)
9. Add `[ModuleInitializer]` registration in `FreeTypeRegistration.cs`
10. Add `[assembly: InternalsVisibleTo("KernSmith.Tests")]` on the new project so tests can access internals

### Step 3: Update `RasterizerFactory`

- Remove FreeType pre-registration from static constructor
- **Fix `ResetForTesting()`** — lines 58-62 currently hardcode FreeType re-registration (`Backends[RasterizerBackend.FreeType] = () => new FreeTypeRasterizer()`). After extraction, the factory must NOT reference `FreeTypeRasterizer`. Change `ResetForTesting()` to simply clear all backends without re-registering any
- After extraction, the factory starts **EMPTY**. Backends only register via `[ModuleInitializer]` when their assembly loads
- Add auto-detection: if no backends are registered, throw a clear error message explaining which plugin packages to install
- Default `RasterizerBackend.FreeType` enum value remains for backward compatibility, but the factory will not have it pre-registered

### Step 4: Remove `CompositeWithFtStroker` dead code

Verify `CompositeWithFtStroker` is dead code (never called anywhere in the codebase — it has been disabled since Phase 12 with `useFtStroker = false`), then delete it from `BmFont.cs`. This eliminates the FreeType coupling entirely rather than trying to abstract it.

### Step 4A: Update solution file

Add the new `KernSmith.Rasterizers.FreeType` project to `KernSmith.sln`.

### Step 5: Update consumers

For consumers to trigger the `[ModuleInitializer]`, they need a `<ProjectReference>` to the plugin project. The project reference causes the assembly to load at startup, which fires the module initializer automatically. This is the same pattern already working for GDI and DirectWrite plugins.

- **CLI**: Add project reference to `KernSmith.Rasterizers.FreeType`
- **Tests**: Add project reference, update direct `FreeTypeRasterizer` instantiations, update `using` directives
- **Samples**: Add project reference
- **UI**: Add project reference
- **Benchmarks**: Add project reference

In `FreeTypeRegistration.cs`, add `#pragma warning disable CA2255` / `#pragma warning restore CA2255` around the `[ModuleInitializer]` attribute (matching the GDI and DirectWrite registration pattern).

### Step 6: Update NuGet packaging

- Core `KernSmith` package becomes dependency-light (StbImageSharp/StbImageWriteSharp only)
- `KernSmith.Rasterizers.FreeType` is a separate NuGet package
- Consider a `KernSmith.Bundle` meta-package that pulls in core + FreeType for easy migration

## Breaking Change Mitigation

This is a **breaking change** for existing consumers:

| Before | After |
|--------|-------|
| `Install-Package KernSmith` | `Install-Package KernSmith` + `Install-Package KernSmith.Rasterizers.FreeType` |
| Just works | Must reference a rasterizer plugin |

**Mitigation options:**
1. **Meta-package** — `KernSmith.Bundle` that depends on core + FreeType (recommended)
2. **Clear error message** — If no rasterizer is registered, throw: "No rasterizer backend registered. Install KernSmith.Rasterizers.FreeType or another rasterizer plugin."
3. **Migration guide** — Document in CHANGELOG and README

## Testing Strategy

- **Factory empty state**: `RasterizerFactory.Create(RasterizerBackend.FreeType)` should throw `InvalidOperationException` when the FreeType plugin assembly is NOT loaded/referenced
- **Factory with plugin**: `RasterizerFactory.Create(RasterizerBackend.FreeType)` should succeed when FreeType plugin assembly is referenced (project reference triggers `[ModuleInitializer]`)
- **Regression test matrix**: Run the full existing test suite — `ColorFontTests`, `VariableFontTests`, `RasterizerFactoryTests`, and all other tests that exercise rasterization
- **NuGet packaging test**: Build the `KernSmith.Rasterizers.FreeType` package, verify it contains the correct managed assemblies AND native FreeType binaries
- **WASM smoke test**: Verify the core `KernSmith` library loads without `PlatformNotSupportedException` when no FreeType plugin is referenced (validates the extraction worked)

## Success Criteria

- [x] `KernSmith.csproj` has zero native dependencies
- [x] `FreeTypeRasterizer` lives in `KernSmith.Rasterizers.FreeType` project
- [x] GDI, DirectWrite, and FreeType plugins all follow identical registration pattern
- [x] All existing tests pass with the new project structure
- [x] CLI, UI, samples, benchmarks all work unchanged
- [x] Core library can be referenced on WASM without `PlatformNotSupportedException`

## Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| Breaking change for existing NuGet consumers | Medium | Meta-package + clear error messages |
| `CompositeWithFtStroker` coupling | Low | Delete dead code (disabled since Phase 12) |
| Test refactoring scope | Low | Straightforward reference additions |
| InternalsVisibleTo changes | Low | FreeType types become public in plugin |
| Module initializer ordering | Low | If multiple plugins load, order is undefined; last to register for a given enum value wins. Document this behavior |
| Assembly loading in WASM | Medium | `[ModuleInitializer]` pattern should work in WASM but needs validation in Phase 33 |
| FreeTypeSharp native binary packaging | Medium | After extraction, native binaries must ship with the **plugin** NuGet package, not core. Verify with NuGet packaging test |
| Backward compatibility — namespace change | Medium | Code using `using KernSmith.Rasterizer; new FreeTypeRasterizer()` will break due to namespace change to `KernSmith.Rasterizers.FreeType`. Document in migration guide |

## Downstream Corrections (for Phase 32 — StbTrueType Plugin)

- StbTrueTypeSharp **REQUIRES** `AllowUnsafeBlocks` — it is not pure safe managed code
- StbTrueTypeSharp **DOES** support SDF via `stbtt_GetGlyphSDF()` / `stbtt_GetCodepointSDF()`

## References

- [FreeTypeSharp NuGet](https://www.nuget.org/packages/FreeTypeSharp) — version 3.1.0
- [.NET Module Initializer docs](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-9.0/module-initializers)
- [FontStashSharp plugin architecture](https://github.com/FontStashSharp/FontStashSharp) — reference for plugin pattern

## Sources

- [Phase 78 — Pluggable Rasterizer Backends](done/phase-78-pluggable-rasterizers.md) — architecture established here
- [Phase 78A — Rasterizer Foundation](done/phase-78a-rasterizer-foundation.md) — IRasterizer interface design
- [Phase 78E — Plugin Template](done/phase-78e-plugin-template.md) — third-party plugin pattern
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39) — WASM support request
