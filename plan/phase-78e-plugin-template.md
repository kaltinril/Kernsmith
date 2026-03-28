# Phase 78E -- Plugin Template and Documentation

> **Status**: Planning (deferred)
> **Size**: Small
> **Created**: 2026-03-25
> **Dependencies**: Phase 78B (GDI) and Phase 78C (DirectWrite) -- only proceed after 2+ backends exist and the abstraction is proven
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Provide a `dotnet new` project template and documentation for writing custom rasterizer backends.

---

## Rationale

This phase is intentionally deferred until after at least two real backends (GDI and DirectWrite) have been built and have proven the abstraction works. Building backends first validates that `IRasterizer` + `IRasterizerCapabilities` are sufficient before documenting them as a stable plugin API. The graveyard of OSS projects is full of beautiful plugin architectures with exactly one plugin -- we build the backends first, generalize second.

## Plugin Contract

The plugin contract is intentionally minimal: just two interfaces, `IRasterizer` and `IRasterizerCapabilities`. These are the same interfaces that first-party backends (GDI, DirectWrite) implement -- there are no privileged internal APIs.

Third parties publish their own NuGet packages (e.g. `MyCompany.MyFancyRasterizer`) that depend on `KernSmith` and register with `RasterizerFactory`. No special framework, metapackage, or gating is needed -- any package that implements these two interfaces and calls `RasterizerFactory.Register()` works as a KernSmith backend. Third-party extensibility is possible today (after 78A ships); this phase just makes it easier with a template and documentation.

## Lessons from 78B/78BB

- **Plugin contract is larger than originally planned**: 78BB added several optional methods to IRasterizer: `GetFontMetrics(RasterOptions)`, `GetKerningPairs(RasterOptions)`, `LoadSystemFont(string)`, plus capabilities `HandlesOwnSizing` and `SupportsSystemFonts`. The template and docs must cover all of these.
- **Default interface methods make the contract safe to extend**: New capabilities use defaults (return null/false) so minimal plugins only need to implement the core rasterization methods. Document which methods are required vs optional.
- **New record types in the contract**: `RasterizerFontMetrics` and `ScaledKerningPair` are part of the return types for optional methods. Template should show how to construct these.

## Tasks

### 1. `dotnet new` Project Template

Create a project template that scaffolds a custom rasterizer backend:
- Template name: `kernsmith-rasterizer` (e.g., `dotnet new kernsmith-rasterizer -n MyRasterizer`)
- Generates a `.csproj` with correct `KernSmith` reference
- Generates a skeleton `IRasterizer` implementation with all methods stubbed, including optional methods from 78BB (`GetFontMetrics`, `GetKerningPairs`, `LoadSystemFont`) with TODO comments
- Generates a skeleton `IRasterizerCapabilities` implementation including `HandlesOwnSizing` and `SupportsSystemFonts`
- Generates a module initializer for `RasterizerFactory.Register()`
- Includes comments explaining each method's contract and expectations

### 2. Documentation: "How to Write a KernSmith Rasterizer Backend"

Written guide covering:
- `IRasterizer` contract -- what each method must do, input/output expectations. Clearly distinguish required methods (core rasterization) from optional methods with defaults (`GetFontMetrics`, `GetKerningPairs`, `LoadSystemFont`, `SetVariationAxes`, `SelectColorPalette`)
- `IRasterizerCapabilities` -- what to report and why, including 78BB additions (`HandlesOwnSizing`, `SupportsSystemFonts`)
- Return types for optional methods -- `RasterizerFontMetrics` and `ScaledKerningPair` record construction
- `RasterizerFactory.Register()` -- how to register your backend
- `RasterizedGlyph` output format -- bitmap data layout, metrics fields, pixel formats
- Font loading -- `LoadFont(ReadOnlyMemory<byte>)` contract, lifecycle expectations
- Effects compatibility -- how existing post-processors work with custom backends
- Testing guidance -- how to validate your backend against FreeType reference output
- Packaging -- NuGet package structure, TFM selection, dependency on `KernSmith` core

### 3. Example: Minimal Skeleton Backend

A complete, minimal backend that demonstrates:
- Implementing `IRasterizer` with all required methods and stubs for optional methods (`GetFontMetrics`, `GetKerningPairs`, `LoadSystemFont`)
- Implementing `IRasterizerCapabilities` including `HandlesOwnSizing` and `SupportsSystemFonts`
- Registering with `RasterizerFactory`
- Proper `IDisposable` implementation
- Basic glyph rasterization (even if it just returns placeholder bitmaps)

### 4. Publish Template to NuGet

- Package the `dotnet new` template as a NuGet package
- Template package name: `KernSmith.Templates` (or similar)
- Include in CI/CD pipeline

## Files Created

| File | Change |
|------|--------|
| `templates/KernSmith.Rasterizer/` | New -- `dotnet new` template content |
| `templates/KernSmith.Rasterizer/.template.config/template.json` | New -- template configuration |
| `docs/` or docfx site | New guide: "Writing a Custom Rasterizer Backend" |

## Testing

- Verify `dotnet new kernsmith-rasterizer` generates a compilable project
- Verify the generated project builds and the skeleton rasterizer can be instantiated
- Verify factory registration works from the generated template
