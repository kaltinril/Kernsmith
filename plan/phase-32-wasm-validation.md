# Phase 32 — WASM Integration & Validation

> **Status**: Future
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction), Phase 31 (StbTrueType plugin)
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)

## Goal

Validate that the core KernSmith library + StbTrueType plugin works in a Blazor WASM environment, and document the integration path for web consumers.

## Scope

### In Scope

1. **Blazor WASM test project** — minimal Blazor WASM app that references `KernSmith` + `KernSmith.Rasterizers.StbTrueType` and generates a bitmap font client-side
2. **Verify no native dependencies leak** — confirm the core library loads without `PlatformNotSupportedException`
3. **Integration test** — upload TTF, generate BMFont, download result (all in-browser)
4. **Documentation** — usage guide for WASM consumers
5. **Trimming/AOT compatibility** — verify the StbTrueType path works with Blazor WASM AOT compilation

### Out of Scope

- Full web UI (KNI/Blazor WebGL UI is a separate, much larger effort)
- Server-side rasterization API (viable but separate from this validation)
- SkiaSharp WASM rasterizer (evaluated in Phase 30 research; not needed if StbTrueType works)
- FreeType via Emscripten (rejected — maintenance burden too high)

## Implementation Plan

### Step 1: Create validation project

```
samples/KernSmith.Samples.BlazorWasm/
├── KernSmith.Samples.BlazorWasm.csproj   (Blazor WASM, net8.0 or net10.0)
├── Pages/
│   └── Index.razor                        (font upload + generate + preview)
├── Program.cs
└── wwwroot/
    └── index.html
```

### Step 2: Verify core library loads

- Reference only `KernSmith` + `KernSmith.Rasterizers.StbTrueType` (no FreeType plugin)
- Confirm no `PlatformNotSupportedException` on startup
- Confirm `RasterizerFactory.IsRegistered(RasterizerBackend.StbTrueType)` returns true

### Step 3: End-to-end font generation

- User uploads a TTF file via `<InputFile>`
- Call `BmFont.Generate()` with `Backend = RasterizerBackend.StbTrueType`
- Display the generated atlas PNG in the browser
- Offer `.fnt` + `.png` download

### Step 4: Performance baseline

- Measure generation time for Roboto-Regular, 32px, ASCII charset in WASM
- Compare with native (.NET console app) performance
- Document expected WASM overhead

### Step 5: AOT compilation test

- Build with `<RunAOTCompilation>true</RunAOTCompilation>`
- Verify no trimming warnings from KernSmith or StbTrueTypeSharp
- Confirm generation still works

## KNI Web UI Notes

For a full web UI (not just validation), the path is:
1. Swap MonoGame → KNI (`nkast.Xna.Framework.Blazor`)
2. Swap `Gum.MonoGame` → `Gum.KNI`
3. Swap `MonoGame.Extended` → `KNI.Extended`
4. Use StbTrueType backend for rasterization

This is a much larger effort and is NOT part of Phase 32. Tracked separately as a future phase.

## Server-Side Alternative

If WASM performance or quality is insufficient, a server-side API is the fallback:
- ASP.NET Core API that accepts font bytes + options, runs FreeType backend, returns BMFont result
- Zero library changes needed
- Full feature parity (SDF, effects, color fonts, variable fonts)
- Tracked separately if needed

## Success Criteria

- [ ] Blazor WASM sample project builds and runs
- [ ] Font generation works entirely client-side (no server)
- [ ] No native dependency errors
- [ ] Generated output matches expected BMFont format
- [ ] AOT compilation works without trimming warnings
- [ ] Performance documented and acceptable

## Sources

- [Blazor WASM documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot)
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)
- [KNI Blazor support](https://github.com/kniEngine/kni)
