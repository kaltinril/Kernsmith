# Phase 78B -- GDI Rasterizer Backend

> **Status**: Planning
> **Size**: Medium
> **Created**: 2026-03-25
> **Dependencies**: Phase 78A (foundation must be in place)
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Implement a GDI-based rasterizer backend for Windows that matches BMFont output. This is the highest-value backend.

---

## Overview

BMFont uses Windows GDI for rasterization. To achieve exact BMFont output parity, KernSmith needs a GDI backend. This is a separate NuGet package (`KernSmith.Rasterizers.Gdi`) that is Windows-only.

**This is the highest-priority backend and the primary motivation for the entire phase 78 effort.** BMFont uses GDI internally, so this backend is the path to BMFont-identical output. If scope needs to be cut anywhere in phase 78, DirectWrite (78C) goes before this phase.

### Key Design Context

- **`AddFontMemResourceEx` for font loading:** Registers raw font bytes as a private GDI font, keeping the `IRasterizer.LoadFont(ReadOnlyMemory<byte>)` interface unchanged. No temp files, no file paths, no name-based overloads needed.
- **ClearType explicitly excluded:** ClearType produces 3x-wide RGB subpixel bitmaps that don't fit the Grayscale8/Rgba32 pipeline. Game engines don't use subpixel rendering. This is a deliberate design decision, not a limitation to fix later.
- **Effects reuse existing pipeline:** GDI just produces compatible `RasterizedGlyph` output (grayscale bitmap + metrics). The existing post-processor pipeline handles outline, shadow, and gradient effects. No effect-specific code in the GDI backend.
- **No external dependencies beyond Windows P/Invoke.** This is pure Win32 interop -- no NuGet packages like Vortice needed.

## Tasks

### 1. New Project

- Path: `src/KernSmith.Rasterizers.Gdi/KernSmith.Rasterizers.Gdi.csproj`
- TFM: `net10.0-windows` (Windows-only)
- Namespace: `KernSmith.Rasterizers.Gdi`
- Separate NuGet package
- References `KernSmith` core library

### 2. Implement `GdiRasterizer : IRasterizer`

Core rasterizer class implementing the full `IRasterizer` interface.

### 3. Font Loading via `AddFontMemResourceEx`

Use `AddFontMemResourceEx` P/Invoke to register `ReadOnlyMemory<byte>` font data as a private font. This keeps the `IRasterizer.LoadFont(ReadOnlyMemory<byte>)` interface unchanged -- no temp files, no file path needed.

### 4. Win32 P/Invoke APIs

Required P/Invoke declarations:

| API | Purpose |
|-----|---------|
| `AddFontMemResourceEx` | Register font bytes as private font |
| `RemoveFontMemResourceEx` | Unregister font in `Dispose` |
| `CreateFont` / `CreateFontIndirectW` | Create HFONT with size, weight, italic flags |
| `CreateCompatibleDC` | Create device context for rasterization |
| `SelectObject` | Bind font to DC |
| `GetGlyphOutline` (GGO_GRAY8_BITMAP) | Rasterize individual glyphs as 65-level grayscale |
| `GetTextMetrics` | Font-wide metrics (tmAscent, tmDescent, tmHeight) |
| `GetCharABCWidths` | Per-character advance widths (A + B + C spacing) |
| `DeleteObject` | Release HFONT |
| `DeleteDC` | Release device context |

### 5. DC Lifecycle Management

- Create DC and select font in `LoadFont`
- Reuse DC across `RasterizeGlyph` / `RasterizeAll` / `GetGlyphMetrics` calls
- Release DC, font handle, and memory font resource in `Dispose`

### 6. Synthetic Bold and Italic

- Bold: set `lfWeight` in `LOGFONT` struct (e.g., `FW_BOLD = 700`)
- Italic: set `lfItalic` flag in `LOGFONT` struct

### 7. Anti-Alias Mode Mapping

Map KernSmith `AntiAliasMode` to GDI quality flags:

| AntiAliasMode | GDI Quality |
|---------------|-------------|
| `Normal` / `Grayscale` | `ANTIALIASED_QUALITY` |
| `None` | `NONANTIALIASED_QUALITY` |

**ClearType: explicitly NOT supported for atlas output.** Subpixel RGB data (3x-wide bitmaps with separate R/G/B channels) does not fit the current Grayscale8/Rgba32 pixel pipeline. Game engines do not use ClearType. Document this limitation clearly.

### 8. Effects Pipeline

GDI just needs to produce compatible `RasterizedGlyph` output with correct:
- `BitmapData` (grayscale pixel buffer)
- `Width`, `Height`
- `BearingX`, `BearingY`
- `Advance`

Existing post-processor pipeline handles outline, shadow, and gradient effects. No effect-specific code needed in the GDI backend.

### 9. Implement `IRasterizerCapabilities`

Report GDI capabilities:
- `SupportsColorFonts`: false
- `SupportsVariableFonts`: false
- `SupportsSdf`: false
- `SupportsOutlineStroke`: false (reuse post-processor pipeline instead)
- `SupportedAntiAliasModes`: `[None, Normal]`

### 10. Add `ResetForTesting()` to `RasterizerFactory`

Add an `internal` `ResetForTesting()` method to `RasterizerFactory` that clears all registered backends and restores factory-default state. Phase 78B is the first phase that registers a second backend, so tests need a way to isolate factory state between test runs (e.g., prevent a GDI registration from leaking into a FreeType-only test). Expose via `[InternalsVisibleTo]` to the test project. Deferred from Phase 78A since it's not needed until multiple backends exist.

### 11. Static Registration with Factory

Register with `RasterizerFactory` so the enum-based API works:

```csharp
RasterizerFactory.Register(RasterizerBackend.Gdi, () => new GdiRasterizer());
```

Use a `[ModuleInitializer]` attribute or an explicit registration call. Test both approaches -- module initializer may have ordering issues.

### 12. Disposal

In `Dispose`:
- `RemoveFontMemResourceEx` to unregister the private font
- `DeleteObject` to release HFONT
- `DeleteDC` to release device context
- Null out handles to prevent double-free

## Files Created/Changed

| File | Change |
|------|--------|
| `src/KernSmith.Rasterizers.Gdi/KernSmith.Rasterizers.Gdi.csproj` | New project file |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` | New -- main rasterizer implementation |
| `src/KernSmith.Rasterizers.Gdi/NativeMethods.cs` | New -- P/Invoke declarations and structs |
| `src/KernSmith.Rasterizers.Gdi/GdiRegistration.cs` | New -- module initializer / factory registration |

## Testing

- **Windows-only CI runner** required for GDI tests
- Platform skip attribute: `[Fact(Skip = "Windows only")]` or a custom `SkipOnPlatform` attribute for cross-platform CI
- **Metrics comparison**: generate the same font at the same size with FreeType and GDI, compare glyph metrics (expect small differences, document them)
- **BMFont reference output**: compare GDI backend output against actual BMFont-generated files for the test font
- **Glyph rendering**: visual comparison of rasterized glyphs (golden image tests where possible)
- **Lifecycle**: verify `Dispose` properly releases all handles (no handle leaks)
- **Edge cases**: empty glyphs (space, control chars), large sizes, synthetic bold+italic combination

**BMFont Parity Validation:**
- Use existing `tests/bmfont-compare/` Python scripts (`diff_fnt.py`, `diff_all_fonts.py`) from Phase 76 to compare GDI backend output against actual BMFont output
- These scripts compare .fnt metrics (lineHeight, base, per-character xadvance/xoffset/yoffset, kerning pairs) and produce tabular diffs
- Reference BMFont output already exists in `tests/bmfont-compare/gum-bmfont/` (12 font configs)
- Regression testing via `RegressionBaseline.cs` (SHA256 hash of PNG pixel data + glyph metrics) can be extended with GDI-specific baseline configurations
- Goal: metrics should match BMFont exactly; pixel output may differ slightly due to antialiasing implementation details but should be visually equivalent

## Technical Findings

Research-validated implementation details for the `AddFontMemResourceEx` approach:

1. **`AddFontMemResourceEx` works with `CreateFont` + `GetGlyphOutline`.** The workflow: call `AddFontMemResourceEx` to register bytes, call `CreateFont` with the font family name, `SelectObject` into HDC, then `GetGlyphOutline` for rasterization. Windows makes its own copy of the font data.

2. **Font family name must be parsed from TTF `name` table.** `AddFontMemResourceEx` does NOT return the font family name. You must parse nameID 1 from the TTF `name` table (prefer platformID 3/Windows, encodingID 1/Unicode BMP, languageID 0x0409/English US). KernSmith's existing `TtfFontReader` / `NameInfo` already parses this -- reuse it.

3. **`GetGlyphOutline` GGO_GRAY8_BITMAP returns values 0-64, not 0-255.** Must remap to 0-255 range for compatibility with the existing pipeline.

4. **`GetCharABCWidths` can fail on some fonts** (notably Calibri). Need a fallback strategy -- possibly pixel-scanning rendered bitmaps for metrics, or using `GetTextExtentPoint32` as alternative.

5. **Font name collisions risk.** If `RemoveFontMemResourceEx` is not called on dispose, a stale registration can cause Windows to silently use the wrong font. Disposal must be robust.

6. **Registered fonts are process-private and not enumerable.** Cannot discover the name via `EnumFontFamilies` -- another reason the TTF name table parse is required.

7. **Edge case: Windows Firewall service state** can affect whether `AddFontMemResourceEx` succeeds (Mozilla bug 1228799). Rare, but worth a diagnostic message if the call fails.

8. **No ClearType from `GetGlyphOutline`** -- it only supports grayscale up to 6-bit depth. This further validates the decision to exclude ClearType from atlas output.

## Reference

### GDI Rasterization from .NET

Key Win32 APIs via P/Invoke:
- `CreateFont()` / `CreateFontIndirectW()` -- create HFONT
- `CreateCompatibleDC()` -- device context
- `SelectObject()` -- bind font to DC
- `GetGlyphOutline()` with `GGO_GRAY8_BITMAP` -- 65-level grayscale glyph bitmaps
- `GetTextMetrics()` -- font-wide metrics (tmAscent, tmDescent, tmHeight)
- `GetCharABCWidths()` -- per-char advance widths (A + B + C spacing)
- `GLYPHMETRICS` struct -- per-glyph width, height, bearings

P/Invoke helpers: CsWin32 (Microsoft source generator) or manual declarations.
