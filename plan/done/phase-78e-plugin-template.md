# Phase 78E -- Plugin Template and Documentation

> **Status**: Complete
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

> **Important limitation:** `RasterizerBackend` is an enum in the core `KernSmith` assembly. Third-party backends cannot add new named values. The recommended approach is to cast an integer value to the enum: `(RasterizerBackend)100`. The template should document this and suggest picking a high value to avoid collisions with future built-in backends.

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

Implementation notes:
- The startup code pattern uses `RuntimeHelpers.RunModuleConstructor()` (not bare `typeof()`)
- The ModuleInitializer class is `internal static` with `#pragma warning disable CA2255`
- The registration lambda is `() => new YourRasterizer()` -- a new instance per `Create()` call

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

Actual interface members to document:

**IRasterizer required members:** `Capabilities` property, `LoadFont()`, `RasterizeGlyph()`, `RasterizeAll()`, plus `Dispose()`

**IRasterizer optional members (default interface implementations):** `LoadSystemFont()`, `GetGlyphMetrics()`, `GetFontMetrics()`, `GetKerningPairs()`, `SetVariationAxes()`, `SelectColorPalette()`

**IRasterizerCapabilities required members:** `SupportsColorFonts`, `SupportsVariableFonts`, `SupportsSdf`, `SupportsOutlineStroke`, `SupportedAntiAliasModes`

**IRasterizerCapabilities optional members (defaults to false):** `HandlesOwnSizing`, `SupportsSystemFonts`

**RasterizedGlyph** has 8 required properties: `Codepoint`, `GlyphIndex`, `BitmapData`, `Width`, `Height`, `Pitch`, `Metrics`, `Format`

**RasterOptions** is a sealed record; only `Size` is required

### 3. Example: Minimal Skeleton Backend

A complete, minimal backend that demonstrates:
- Implementing `IRasterizer` with all required methods and stubs for optional methods (`GetFontMetrics`, `GetKerningPairs`, `LoadSystemFont`)
- Implementing `IRasterizerCapabilities` including `HandlesOwnSizing` and `SupportsSystemFonts`
- Registering with `RasterizerFactory`
- Proper `IDisposable` implementation
- Basic glyph rasterization (even if it just returns placeholder bitmaps)

### 4. Template README

A detailed README.md in the template directory explaining:
- What the template contains and how to use it
- How to copy the template directory and customize it for a new rasterizer backend
- Step-by-step walkthrough of what to change (names, namespace, backend ID, capabilities, rasterization logic)
- How to wire it into a consuming application

This replaces the original plan to publish as a NuGet template package -- the template lives in the repo and users copy it directly.

## Files Created

| File | Change |
|------|--------|
| `templates/KernSmith.Rasterizer.Example/` | New -- `dotnet new` template content |
| `templates/KernSmith.Rasterizer.Example/.template.config/template.json` | New -- template configuration |
| `templates/KernSmith.Rasterizer.Example/MyRasterizer.cs` | New -- skeleton IRasterizer |
| `templates/KernSmith.Rasterizer.Example/MyRasterizerCapabilities.cs` | New -- skeleton IRasterizerCapabilities |
| `templates/KernSmith.Rasterizer.Example/MyRasterizerRegistration.cs` | New -- ModuleInitializer registration |
| `templates/KernSmith.Rasterizer.Example/KernSmith.Rasterizers.MyRasterizer.csproj` | New -- project file |
| `templates/KernSmith.Rasterizer.Example/README.md` | New -- step-by-step guide for copying and customizing the template |
| `docs/rasterizers/custom-backend.md` | New -- "Writing a Custom Rasterizer Backend" guide |
| `docs/rasterizers/toc.yml` | Updated -- add custom backend entry |
| `docs/toc.yml` | Updated -- add custom backend under Rasterizers |

## Testing

- [x] Verify `dotnet new kernsmith-rasterizer` generates a compilable project
- [x] Verify the generated project builds and the skeleton rasterizer can be instantiated
- [x] Verify factory registration works from the generated template
