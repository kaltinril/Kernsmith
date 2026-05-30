# Phase 105: Atlas Pixel Format Helpers

**Status:** Complete — shipped in commit `f5a781d`
**Date:** 2026-04-10
**Branch:** `feature/atlas-pixel-format-helpers`

> Originally proposed as "Text Layout Engine (Core) + Framework Rendering Examples". After review, the layout engine scope was **cancelled** in favor of the pre-cursor format helpers alone. The cancellation rationale is preserved below in "Why this scope (and not a layout engine)" so future agents understand the boundary.

## Purpose

Reduce friction for downstream consumers uploading KernSmith atlas pages to GPU textures, regardless of the page's native pixel format (`Grayscale8` or `Rgba32`). Provides on-demand format conversion via two methods on `AtlasPage`.

## Why this scope (and not a layout engine)

KernSmith's mission is **font generation**, not rendering. KernSmith was created at Vic Chelaru's request specifically to enable Gum to generate bitmap fonts on-the-fly. **Gum is the primary consumer**, and Gum already provides full text layout and rendering via `BitmapFont`.

This phase originally proposed adding a text layout engine (`TextRenderer`, `GlyphLayout`, word wrap, measurement, ~250 lines + 27 tests) to KernSmith core. After review, that scope was cancelled. **The layout engine should not be resurrected without re-engaging with this rationale:**

1. **Gum wouldn't use it.** Gum's `BitmapFont` does layout/measurement/word wrap and is the canonical path for Gum users. Vic isn't going to add a KernSmith dependency to Gum to delegate layout. So `TextRenderer` would only serve non-Gum consumers.

2. **Non-Gum consumers aren't underserved.** MonoGame.Extended supports BMFont rendering. FontStashSharp supports BMFont. Raylib has built-in font rendering. Silk.NET integrations exist. The "30-50 lines of boilerplate" framing assumed users would write layout from scratch — in practice they pick an existing renderer.

3. **Layout is a deep rabbit hole.** Word wrap rules (CJK breaks, hyphenation, soft-hyphens), alignment, RTL, ligatures, color emoji — doing it well requires owning the consumer UX, not just the data. That's Gum's lane and the lane of dedicated text rendering libraries.

4. **Strategic focus.** KernSmith was created to be the upstream piece Gum doesn't have (font generation). Adding downstream pieces dilutes that mission and creates duplication in the Gum ecosystem.

What survived the cancellation: the **pixel format helpers** that were originally drafted as a pre-cursor to make the layout engine's sample code clean. These solve real friction for any downstream consumer (Gum or otherwise) and don't enter layout territory — they're squarely in the generation/output layer.

### Options considered and rejected (kept here so future agents don't undo this)

| Option | Why rejected |
|--------|-------------|
| **Full text layout engine** (`TextRenderer` + word wrap + measurement) | Duplicates Gum's `BitmapFont`. Would not be used by Gum (the primary customer). Non-Gum users have established alternatives. |
| **Flip atlas default to RGBA** | 4× memory waste for the common plain-text case. Inverts the natural rasterizer output (FreeType, stb_truetype, GDI, DirectWrite all default to grayscale). Forces everyone to pay the RGBA cost. |
| **Force grayscale default with RGBA helper only** | Doesn't address effects/color-font cases that genuinely need RGBA. Same code complexity as the chosen approach. |
| **Semantic `PixelFormat` enum** (`Alpha8`/`Luminance8`/`Rgba32`) | Public API breaking change. Forces every consumer that reads `PixelFormat` to update. Helpers achieve the same disambiguation without breakage. |

The chosen approach keeps the raw-format auto-detection (current pipeline behavior — atlases stay grayscale until effects need RGBA) and adds intent-declaring accessors that resolve format ambiguity at the consumer boundary, not deep in the pipeline.

## What shipped

### API additions on `AtlasPage`

```csharp
public byte[] GetRgbaPixelData();
public byte[] GetAlpha8PixelData();
```

Both return fresh caller-owned buffers regardless of native format:

| Source format | `GetRgbaPixelData()` | `GetAlpha8PixelData()` |
|---|---|---|
| `Grayscale8` | Expands `[v]` → `[255, 255, 255, v]` (Angelcode canonical alpha-coverage layout, see `reference/REF-05-bmfont-format-reference.md`) | Returns clone of `PixelData` |
| `Rgba32` | Returns clone of `PixelData` | Extracts alpha channel only (RGB discarded) |

Always returns a fresh array. Mutating the result never affects the source page's `PixelData`.

### Files added/modified

| File | Change |
|------|--------|
| `src/KernSmith/Atlas/AtlasPage.cs` | Added `GetRgbaPixelData()` and `GetAlpha8PixelData()` (lines 54-99) |
| `tests/KernSmith.Tests/Atlas/AtlasPageTests.cs` | New file — 8 unit tests covering both methods, both source formats, and array-independence guarantees |
| `samples/KernSmith.Samples.Minimal/Game1.cs` | New minimal MonoGame sample. Demonstrates helper usage by uploading the atlas as a Texture2D. Atlas-display only — text rendering deliberately not included (use Gum, MonoGame.Extended, FontStashSharp, etc. for that). |
| `samples/KernSmith.Samples.Minimal/Program.cs` | New — minimal sample entry point |
| `samples/KernSmith.Samples.Minimal/KernSmith.Samples.Minimal.csproj` | New — references core + StbTrueType rasterizer |

### Verification

- `dotnet build src/KernSmith/KernSmith.csproj` — 0 warnings, 0 errors on `net8.0` and `net10.0`
- `dotnet test tests/KernSmith.Tests/KernSmith.Tests.csproj --filter "FullyQualifiedName~AtlasPageTests"` — 8/8 passing across `net8.0`, `net8.0-windows`, `net10.0`, `net10.0-windows`
- `dotnet build samples/KernSmith.Samples.Minimal/KernSmith.Samples.Minimal.csproj` — 0 warnings, 0 errors

### Edge cases (verified by tests)

| Case | Behavior |
|---|---|
| Source format matches requested format | Returns a clone — not the same array reference as `PixelData` |
| `GetRgbaPixelData()` on `Grayscale8` | Expands to `(255, 255, 255, v)` per Angelcode BMFont canonical layout |
| `GetAlpha8PixelData()` on `Rgba32` | Extracts alpha channel only; RGB values discarded |
| Caller mutates returned array | `PixelData` unaffected (always fresh buffer) |
| Result array length | `Width * Height * 4` for RGBA, `Width * Height` for Alpha8, regardless of source format |

## How consumers use it

```csharp
// Single page
var page = result.Pages[0];
var tex = new Texture2D(GraphicsDevice, page.Width, page.Height, false, SurfaceFormat.Color);
tex.SetData(page.GetRgbaPixelData());

// Multiple pages
var textures = result.Pages.Select(p =>
{
    var t = new Texture2D(GraphicsDevice, p.Width, p.Height);
    t.SetData(p.GetRgbaPixelData());
    return t;
}).ToArray();
```

**Before this phase:** ~20 lines of `if (page.Format == PixelFormat.Grayscale8) { ... }` conversion code, scattered across every sample and integration.

**After this phase:** 1 line. Same shape works for both formats. Users who genuinely need raw access (SDF workflows, channel-packed atlases, memory-constrained scenarios) keep using `PixelData` + `Format` directly — no breaking change, no waste.

## Followup work (potential Phase 106 candidates)

These are aligned with KernSmith's actual mission (be a great font generator for Gum and any standards-compliant consumer) and would not duplicate Gum's territory:

- **Fix the atlas channel config bug.** Project memory `project_atlas_channel_bug.md` notes: encoder ignores `.bmfc` channel config, produces white-on-black instead of white-on-alpha. Affects Gum (reads `alphaChnl`/`redChnl` metadata) and any other BMFont-compliant consumer. Pure correctness fix in the output layer.

- **Lookup helpers on `BmFontResult`.** `GetCharLookup()` returning `IReadOnlyDictionary<int, CharEntry>`, similar for kerning pairs. Saves 1-2 lines of `.ToDictionary(c => c.Id)` boilerplate in every integration — including the `KernSmith.MonoGameGum` integration that's the primary consumer.

- **Integration guide README.** Point users at the right downstream libraries: Gum (canonical for Gum users), MonoGame.Extended, FontStashSharp, Raylib. One minimal example per. Replaces the "boilerplate everywhere" pain better than a layout engine would.

- **Ask Vic what's painful in `KernSmith.MonoGameGum` integration today.** Gum is the primary consumer KernSmith was built for; Vic's friction list is where the highest-value next work likely lives.

- **The KernSmith UI's hand-rolled text layout** (`apps/KernSmith.Ui/Layout/PreviewPanel.cs:677-805`) is the only KernSmith code that actually duplicates Gum's territory. Since the UI is already a Gum app, that block could be replaced with a Gum `BitmapFont` rendering call. Internal cleanup, no new dependencies.
