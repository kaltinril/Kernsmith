# Phase 30 — Extract FreeType Rasterizer to Plugin

> **Status**: Planning
> **Created**: 2026-03-20
> **Updated**: 2026-03-30
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39), Phase 31 (StbTrueType plugin), Phase 32 (WASM validation)

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
2. Change `FreeTypeRasterizer` from `internal sealed` to `public sealed`
3. Remove `FreeTypeSharp` dependency from `KernSmith.csproj`
4. Remove `AllowUnsafeBlocks` from `KernSmith.csproj` (if no other unsafe code remains)
5. Add `[ModuleInitializer]` registration in `FreeTypeRegistration.cs`

### Step 3: Update `RasterizerFactory`

- Remove FreeType pre-registration from static constructor
- Add auto-detection: if no backends are registered, throw a clear error message explaining which plugin packages to install
- Default `RasterizerBackend` enum value should remain `FreeType` for backward compatibility

### Step 4: Handle `CompositeWithFtStroker`

The `BmFont.cs` outline stroking code calls FreeType-specific methods. Options:
- Move outline stroking to `IRasterizer` capability (preferred — `SupportsOutlineStroke` already exists in capabilities)
- Or move the compositing code to the FreeType plugin and expose via an interface

### Step 5: Update consumers

- **CLI**: Add project reference to `KernSmith.Rasterizers.FreeType` + module initializer trigger (same pattern as GDI/DirectWrite)
- **Tests**: Add project reference, update direct `FreeTypeRasterizer` instantiations
- **Samples**: Add project reference
- **UI**: Add project reference
- **Benchmarks**: Add project reference

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

## Success Criteria

- [ ] `KernSmith.csproj` has zero native dependencies
- [ ] `FreeTypeRasterizer` lives in `KernSmith.Rasterizers.FreeType` project
- [ ] GDI, DirectWrite, and FreeType plugins all follow identical registration pattern
- [ ] All existing tests pass with the new project structure
- [ ] CLI, UI, samples, benchmarks all work unchanged
- [ ] Core library can be referenced on WASM without `PlatformNotSupportedException`

## Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| Breaking change for existing NuGet consumers | Medium | Meta-package + clear error messages |
| `CompositeWithFtStroker` coupling | Low | Move to IRasterizer capability |
| Test refactoring scope | Low | Straightforward reference additions |
| InternalsVisibleTo changes | Low | FreeType types become public in plugin |

## Sources

- [Phase 78 — Pluggable Rasterizer Backends](done/phase-78-pluggable-rasterizers.md) — architecture established here
- [Phase 78A — Rasterizer Foundation](done/phase-78a-rasterizer-foundation.md) — IRasterizer interface design
- [Phase 78E — Plugin Template](done/phase-78e-plugin-template.md) — third-party plugin pattern
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39) — WASM support request
