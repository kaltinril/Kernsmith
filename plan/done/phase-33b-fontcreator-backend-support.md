# Phase 33b — KernSmithFontCreator Backend Selection

> **Status**: Complete
> **Created**: 2026-04-01
> **Depends on**: Phase 32 (StbTrueType rasterizer), Phase 33 (Blazor WASM validation)

## Goal

Allow consumers of `KernSmithFontCreator` and `GumFontGenerator` to select which rasterizer backend to use, eliminating the need to reimplement `KernSmithFontCreator` just to change the backend.

## Background

`KernSmithFontCreator.TryCreateFont` calls `GumFontGenerator.Generate`, which builds `FontGeneratorOptions` without setting `Backend` — defaulting to FreeType. On Blazor WASM, FreeType is unavailable (no native binary), and the only working backend is StbTrueType. Prior to this change, the workaround was to reimplement `KernSmithFontCreator` to call `BuildOptions()` + set `Backend` manually before generating.

## Changes

### GumFontGenerator (KernSmith.GumCommon)

- `Generate(BmfcSave, RasterizerBackend?)` — added optional `backend` parameter. When non-null, overrides the `Backend` property on the built options before generating.

### KernSmithFontCreator (KernSmith.MonoGameGum)

- Constructor now accepts optional `RasterizerBackend? backend = null` parameter, stored as a field.
- `TryCreateFont` passes the stored backend through to `GumFontGenerator.Generate()`.

### Tests

- `BuildOptions_DoesNotSetBackend` — verifies `BuildOptions()` leaves backend at default.
- `Generate_WithBackendOverride_UsesSpecifiedBackend` — end-to-end test using registered font + StbTrueType backend.

### Docs

- Updated `docs/integrations/gumcommon.md` with backend override examples.
- Updated `docs/integrations/monogamegum.md` with Blazor WASM setup example.
- CHANGELOG updated under `[Unreleased]`.

## Usage

```csharp
// MonoGame + Gum: specify StbTrueType at construction time
CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmithFontCreator(GraphicsDevice, RasterizerBackend.StbTrueType);

// GumCommon: pass backend directly to Generate
BmFontResult result = GumFontGenerator.Generate(bmfcSave, RasterizerBackend.StbTrueType);

// Or customize further via BuildOptions
var options = GumFontGenerator.BuildOptions(bmfcSave);
options.Backend = RasterizerBackend.StbTrueType;
var result = BmFont.GenerateFromSystem(bmfcSave.FontName, options);
```
