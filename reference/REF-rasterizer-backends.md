# Rasterizer Backends Reference

> **Purpose**: Documents the three rasterizer backends supported by KernSmith, their capabilities, trade-offs, and when to use each one.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Comparison Table](#2-comparison-table)
3. [FreeType](#3-freetype)
4. [GDI](#4-gdi)
5. [DirectWrite](#5-directwrite)
6. [Output Differences](#6-output-differences)
7. [Adding Custom Backends](#7-adding-custom-backends)

---

## 1. Overview

KernSmith uses a pluggable rasterizer architecture. All backends implement the `IRasterizer` interface and expose their feature set through `IRasterizerCapabilities`. The core library selects a backend at runtime via `RasterizerFactory`, which maintains a thread-safe registry of `RasterizerBackend` -> factory function mappings.

FreeType is the default backend, pre-registered in `RasterizerFactory`. The GDI and DirectWrite backends ship as separate NuGet packages (`KernSmith.Rasterizers.Gdi`, `KernSmith.Rasterizers.DirectWrite.TerraFX`) and register themselves when their assembly is loaded.

```
RasterizerBackend enum:
  FreeType      — Cross-platform, full-featured (default)
  Gdi           — Windows-only
  DirectWrite   — Windows-only, high quality
```

---

## 2. Comparison Table

| Capability | FreeType | GDI | DirectWrite |
|---|---|---|---|
| **Platform** | Cross-platform (Windows, Linux, macOS) | Windows only | Windows only |
| **NuGet package** | `KernSmith` (built-in) | `KernSmith.Rasterizers.Gdi` | `KernSmith.Rasterizers.DirectWrite.TerraFX` |
| **Color fonts** (COLR/CPAL, CBDT/CBLC, sbix) | Yes | No | No (stubbed, no impl yet) |
| **Variable fonts** (fvar axes) | Yes | No | No (stubbed, no impl yet) |
| **SDF rendering** | Yes | No | No |
| **Outline stroke** | Yes | No | No |
| **System font loading** | No | Yes | Yes |
| **Handles own sizing** | No (core converts cell height to ppem) | Yes (GDI sizes via LOGFONT) | No (core converts) |
| **Anti-alias: None** | Yes | Yes | Yes |
| **Anti-alias: Grayscale** | Yes | Yes | Yes |
| **Anti-alias: Light** | Yes | No | No |
| **Anti-alias: LCD** | Yes | No | No |
| **Hinting** | FreeType auto-hinter + font bytecode | Windows GDI hinter | DirectWrite natural/symmetric hinting |
| **Bold/italic simulation** | FreeType emboldening + oblique shear | GDI font mapper + MAT2 shear | DWRITE_FONT_SIMULATIONS flags |
| **Font collection (TTC) support** | Yes (faceIndex parameter) | No (faceIndex must be 0) | Yes (faceIndex parameter) |
| **Kerning source** | Falls back to shared GPOS/kern parser | Falls back to shared GPOS/kern parser | Falls back to shared GPOS/kern parser |
| **Font metrics source** | Falls back to shared OS/2 table parser | GDI TEXTMETRIC (own impl) | Falls back to shared OS/2 table parser |

---

## 3. FreeType

**Package**: Built into `KernSmith` (via FreeTypeSharp 3.1.0).

**When to use**: Default choice for all platforms. Required when you need color fonts, variable fonts, SDF output, or outline stroke effects.

**Strengths**:

- Cross-platform: works on Windows, Linux, and macOS.
- Full feature coverage: color fonts, variable fonts (fvar axis coordinates), SDF, outline stroke.
- All four anti-alias modes: None, Grayscale, Light, LCD.
- TTC font collection support via `faceIndex`.
- Mature, widely-used rasterizer with extensive font format coverage.

**Limitations**:

- Cannot load system-installed fonts by family name. Requires raw font file bytes via `LoadFont`.
- Unsafe code required for interop (`FreeTypeRasterizer.cs`, `FreeTypeNative.cs`).
- Hinting behavior differs from Windows GDI, which means output will not match BMFont pixel-for-pixel.

---

## 4. GDI

**Package**: `KernSmith.Rasterizers.Gdi` (separate NuGet, Windows-only TFMs: `net8.0-windows`, `net10.0-windows`).

**When to use**: When you need pixel-accurate BMFont parity. BMFont itself uses GDI's `GetGlyphOutlineW` with `GGO_GRAY8_BITMAP`, so this backend reproduces the same rendering pipeline.

**Strengths**:

- Closest match to BMFont's own output because it uses the same Windows GDI rendering path.
- System font loading by family name (`LoadSystemFont`), matching BMFont's font picker workflow.
- Handles its own font sizing via `LOGFONT.lfHeight`, so point sizes map identically to BMFont.
- GDI font mapper resolves bold/italic from intrinsic font weight (OS/2 `usWeightClass` + `fsSelection`).

**Limitations**:

- Windows only. Will not work on Linux or macOS.
- No color fonts, variable fonts, SDF, or outline stroke support.
- Anti-alias limited to None and Grayscale (via `GGO_GRAY8_BITMAP` 0-64 value range remapped to 0-255).
- No TTC font collection support (faceIndex must be 0).
- Grayscale values come from GDI's 65-level quantization (0-64), remapped to 0-255.

---

## 5. DirectWrite

**Package**: `KernSmith.Rasterizers.DirectWrite.TerraFX` (separate NuGet, Windows-only TFM: `net10.0-windows`).

**When to use**: When you want modern Windows text rendering with DirectWrite's natural/symmetric hinting. Useful for applications targeting Windows where GDI output quality is not sufficient and FreeType's hinting is undesirable.

**Strengths**:

- Modern rendering pipeline with `IDWriteGlyphRunAnalysis`.
- Natural symmetric hinting mode produces smoother curves than GDI at small sizes.
- System font loading by family name via `IDWriteFontCollection`.
- Bold/italic simulation via `DWRITE_FONT_SIMULATIONS` flags with cached font face variants.
- TTC font collection support via faceIndex.
- ClearType 3x1 subpixel rendering internally, converted to grayscale for atlas output.

**Limitations**:

- Windows only.
- Color fonts and variable fonts are stubbed but not yet implemented (capabilities report `false`).
- No SDF or outline stroke support.
- Anti-alias limited to None and Grayscale.
- Returns `null` for `GetFontMetrics` (delegates to shared OS/2 parser) because DirectWrite's `DWRITE_FONT_METRICS` uses hhea typographic values that produce incorrect `lineHeight` for many fonts.
- Uses TerraFX.Interop.Windows for COM interop, which requires unsafe code throughout.

---

## 6. Output Differences

The three backends will produce visually different output for the same font, size, and codepoints. This is expected and unavoidable because each uses a different rendering pipeline:

- **Hinting**: FreeType uses its auto-hinter or the font's bytecode interpreter. GDI uses the Windows hinting engine. DirectWrite uses natural or symmetric hinting. These produce different pixel grid alignment, especially at small sizes.
- **Gamma and blending**: GDI's `GGO_GRAY8_BITMAP` outputs 65 quantization levels (0-64). FreeType outputs 256 levels. DirectWrite outputs ClearType RGB triples averaged to grayscale. The alpha ramps differ.
- **Metrics rounding**: GDI handles sizing internally via `LOGFONT.lfHeight` (with DPI conversion), while FreeType and DirectWrite receive ppem values from the core. Rounding differences of 1 pixel in bearingX, bearingY, or advance are common.
- **Bold/italic synthesis**: Each backend applies synthetic bold and italic differently. GDI uses font mapper weight + MAT2 shear. FreeType uses `FT_Outline_Embolden` + `FT_GlyphSlot_Oblique`. DirectWrite uses `DWRITE_FONT_SIMULATIONS` flags.
- **Kerning**: All three backends return `null` from `GetKerningPairs` and delegate to the shared GPOS/kern table parser, so kerning values are consistent across backends.

For BMFont parity testing, use the GDI backend. For cross-platform builds, use FreeType.

---

## 7. Adding Custom Backends

To add a custom rasterizer backend:

1. Implement `IRasterizer` (in `KernSmith.Rasterizer` namespace). This requires `LoadFont`, `RasterizeGlyph`, `RasterizeAll`, and `Dispose`. Optional methods (`GetGlyphMetrics`, `GetFontMetrics`, `GetKerningPairs`, `SetVariationAxes`, `SelectColorPalette`, `LoadSystemFont`) have default implementations that return null or throw.

2. Implement `IRasterizerCapabilities` to declare what your backend supports. The core pipeline checks these flags before calling optional methods.

3. Register with the factory:
   ```csharp
   RasterizerFactory.Register(RasterizerBackend.MyBackend, () => new MyRasterizer());
   ```

4. If shipping as a separate NuGet package, provide a static registration method (see `GdiRegistration.cs` or `DirectWriteRegistration.cs` for the pattern).

The `IRasterizerCapabilities` interface:

| Property | Type | Purpose |
|---|---|---|
| `SupportsColorFonts` | `bool` | COLR/CPAL/sbix/CBDT color glyph rendering |
| `SupportsVariableFonts` | `bool` | fvar axis coordinate support |
| `SupportsSdf` | `bool` | Signed distance field output |
| `SupportsOutlineStroke` | `bool` | Glyph outline stroking |
| `SupportedAntiAliasModes` | `IReadOnlyList<AntiAliasMode>` | Which AA modes are available |
| `HandlesOwnSizing` | `bool` | If true, core skips ppem conversion |
| `SupportsSystemFonts` | `bool` | If true, `LoadSystemFont` is available |
