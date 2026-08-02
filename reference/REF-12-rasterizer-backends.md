# Rasterizer Backends Reference

> **Purpose**: Documents the five rasterizer backends supported by KernSmith, their capabilities, trade-offs, and when to use each one.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Comparison Table](#2-comparison-table)
3. [FreeType](#3-freetype)
4. [GDI](#4-gdi)
5. [DirectWrite](#5-directwrite)
6. [StbTrueType](#6-stbtruetype)
7. [Native](#7-native)
8. [Output Differences](#8-output-differences)
9. [Adding Custom Backends](#9-adding-custom-backends)

---

## 1. Overview

KernSmith uses a pluggable rasterizer architecture. All backends implement the `IRasterizer` interface and expose their feature set through `IRasterizerCapabilities`. The core library selects a backend at runtime via `RasterizerFactory`, which maintains a thread-safe registry of `RasterizerBackend` -> factory function mappings.

FreeType is the *default* backend in the sense that `FontGeneratorOptions.Backend` defaults to it, but nothing is pre-registered: `RasterizerFactory` reflection-discovers all backends identically on first use. Every backend ships as a separate NuGet package (FreeType included — it is not part of the core `KernSmith` package) and registers itself when its assembly is loaded. The Native backend is the exception: it is not packable and not published (see its status note below).

```
RasterizerBackend enum:
  FreeType      — Cross-platform, full-featured (default)
  Gdi           — Windows-only
  DirectWrite   — Windows-only, high quality
  StbTrueType   — Cross-platform, pure C#, no native dependencies
  Native        — Cross-platform, pure C#, no external dependencies (experimental, unpublished)
```

> **Native backend status**: The Native backend renders as of Phase 165 — SFNT/table parsing (161), `glyf`/`loca` parsing (162), outline extraction and flattening (163), and the signed-area scanline fill (164) have all landed, and it is registered via `[ModuleInitializer]` and wired into the pipeline (165). It renders **TrueType (`glyf`) outlines only**: a CFF/OTF face is rejected at `LoadFont` with a `RasterizationException` (CFF is Phase 166). Hinting, SDF, outline stroke, synthetic bold/italic, variable fonts and color fonts are all still unimplemented. The project remains `IsPackable=false` and unpublished, so `RasterizerBackend.Native` resolves only from a source build or the KernSmith CLI (which project-references it) — never from an app built on the released NuGet packages.

---

## 2. Comparison Table

| Capability | FreeType | GDI | DirectWrite | StbTrueType | Native |
|---|---|---|---|---|---|
| **Platform** | Cross-platform (Windows, Linux, macOS) | Windows only | Windows only | Cross-platform (Windows, Linux, macOS, WASM) | Cross-platform (Windows, Linux, macOS, WASM) |
| **NuGet package** | `KernSmith.Rasterizers.FreeType` | `KernSmith.Rasterizers.Gdi` | `KernSmith.Rasterizers.DirectWrite.TerraFX` | `KernSmith.Rasterizers.StbTrueType` | none (not published) |
| **Color fonts** (COLR/CPAL, CBDT/CBLC, sbix) | Yes | No | No (stubbed, no impl yet) | No | No |
| **Variable fonts** (fvar axes) | Yes | No | No (stubbed, no impl yet) | No | No |
| **SDF rendering** | Yes | No | No | Yes | No |
| **Outline stroke** | Yes | No | No | No | No |
| **System font loading** | No | Yes | Yes | No | No |
| **Handles own sizing** | No (core converts cell height to ppem) | Yes (GDI sizes via LOGFONT) | No (core converts) | No (core converts) | No (core converts) |
| **Anti-alias: None** | Yes | Yes | Yes | Yes | Yes |
| **Anti-alias: Grayscale** | Yes | Yes | Yes | Yes | Yes |
| **Anti-alias: Light** | Yes | No | No | No | No |
| **Anti-alias: LCD** | Yes | No | No | No | No |
| **Hinting** | FreeType auto-hinter + font bytecode | Windows GDI hinter | DirectWrite natural/symmetric hinting | stb_truetype hinting (limited) | None |
| **Bold/italic simulation** | FreeType emboldening + oblique shear | GDI font mapper + MAT2 shear | DWRITE_FONT_SIMULATIONS flags | Synthetic bold + oblique shear | No (Phase 167) |
| **Font collection (TTC) support** | Yes (faceIndex parameter) | No (faceIndex must be 0) | Yes (faceIndex parameter) | Yes (faceIndex parameter) | Yes (faceIndex parameter) |
| **Kerning source** | Falls back to shared GPOS/kern parser | Falls back to shared GPOS/kern parser | Falls back to shared GPOS/kern parser | Falls back to shared GPOS/kern parser | Falls back to shared GPOS/kern parser |
| **Font metrics source** | Falls back to shared OS/2 table parser | GDI TEXTMETRIC (own impl) | Falls back to shared OS/2 table parser | Falls back to shared OS/2 table parser | Parses own core tables (`head`/`hhea`/`hmtx`/`OS/2`/`cmap`) |

---

## 3. FreeType

**Package**: `KernSmith.Rasterizers.FreeType` (via FreeTypeSharp 3.1.0).

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
- Color fonts and variable fonts are stubbed but not yet implemented. `DirectWriteCapabilities.SupportsColorFonts` and `SupportsVariableFonts` both return `false` (see `DirectWriteCapabilities.cs`), so the core pipeline never invokes the color/variable code paths. The `SetVariationAxes` and `SelectColorPalette` methods exist and accept their arguments, but only store the values — they have no rendering effect yet (no `TranslateColorGlyphRun` or `IDWriteFontFace5` axis implementation). Verified against `DirectWriteRasterizer.cs` and `DirectWriteCapabilities.cs` as of KernSmith 0.14.0.
- No SDF or outline stroke support.
- Anti-alias limited to None and Grayscale.
- Returns `null` for `GetFontMetrics` (delegates to shared OS/2 parser) because DirectWrite's `DWRITE_FONT_METRICS` uses hhea typographic values that produce incorrect `lineHeight` for many fonts.
- Uses TerraFX.Interop.Windows for COM interop, which requires unsafe code throughout.

---

## 6. StbTrueType

**Package**: `KernSmith.Rasterizers.StbTrueType` (separate NuGet, cross-platform TFMs: `net8.0`, `net10.0`).

**When to use**: When you need a pure C# rasterizer with zero native dependencies. Required for Blazor WASM, iOS AOT, and serverless environments where native P/Invoke is unavailable. Also useful when you want to avoid shipping platform-specific native binaries.

**Strengths**:

- Pure managed C# -- no native dependencies, no `DllImport` or `LibraryImport`.
- Cross-platform: works on Windows, Linux, macOS, Blazor WASM, and any .NET AOT target.
- Marked `IsTrimmable` and `IsAotCompatible` for WASM and Native AOT scenarios.
- SDF rendering support via vendored stb_truetype SDF implementation.
- Synthetic bold and italic simulation.
- TTC font collection support via `faceIndex`.
- Auto-registers via `[ModuleInitializer]` when the assembly is loaded.

**Limitations**:

- No color font support (COLR/CPAL, CBDT/CBLC, sbix).
- No variable font support (fvar axes).
- No outline stroke support.
- Cannot load system-installed fonts by family name.
- Anti-alias limited to None and Grayscale.
- Hinting quality is more limited than FreeType or platform rasterizers.
- Requires `AllowUnsafeBlocks` due to StbTrueTypeSharp's pointer-based API.

---

## 7. Native

**Package**: none. The `KernSmith.Rasterizers.Native` project is `IsPackable=false` and is not published to NuGet; it builds in-repo only (cross-platform TFMs: `net8.0`, `net10.0`).

**Status**: Experimental, but functional as of Phase 165. Font loading, table parsing, outline extraction, adaptive curve flattening and a signed-area scanline fill all work, and font/glyph metrics track FreeType to within a pixel. It renders TrueType (`glyf`) outlines only — a CFF/OTF face is rejected at `LoadFont` with a `RasterizationException` (Phase 166). Reachable from a source build or the KernSmith CLI (`--rasterizer native`); not from the released NuGet packages.

**When to use**: A fully-owned, dependency-free fallback for the most constrained environments (Blazor WASM, Native AOT, trimmed/single-file apps) where even StbTrueTypeSharp's `AllowUnsafeBlocks` requirement is undesirable. For those scenarios today, use StbTrueType — it is published, and it adds SDF plus synthetic bold/italic.

**Strengths**:

- Pure managed C# owned entirely by KernSmith -- zero external dependencies (no FreeType, no stb, no platform API).
- Cross-platform and WASM/AOT-friendly by design.
- Parses the core font tables itself (`head`, `hhea`, `hmtx`, `OS/2`, `cmap`) rather than delegating sizing/metrics to a native library.
- TTC font collection support via `faceIndex`.

**Limitations**:

- TrueType (`glyf`) outlines only — CFF/CFF2 (`.otf`) faces are rejected at load (Phase 166). No WOFF/WOFF2.
- No hinting, so small sizes are softer than FreeType or GDI output.
- No color fonts, variable fonts, SDF, or outline stroke support.
- Cannot load system-installed fonts by family name (`LoadSystemFont` throws `NotSupportedException`).
- Anti-alias modes declared are None and Grayscale only.
- No synthetic bold or italic (`SupportsSyntheticBold` / `SupportsSyntheticItalic` are `false`).
- Not thread-safe: create one instance per thread for parallel rasterization.

---

## 8. Output Differences

The five rendering backends (FreeType, GDI, DirectWrite, StbTrueType, Native) will produce visually different output for the same font, size, and codepoints. This is expected and unavoidable because each uses a different rendering pipeline:

- **Hinting**: FreeType uses its auto-hinter or the font's bytecode interpreter. GDI uses the Windows hinting engine. DirectWrite uses natural or symmetric hinting. These produce different pixel grid alignment, especially at small sizes.
- **Gamma and blending**: GDI's `GGO_GRAY8_BITMAP` outputs 65 quantization levels (0-64). FreeType outputs 256 levels. DirectWrite outputs ClearType RGB triples averaged to grayscale. StbTrueType outputs 256 levels via stb_truetype coverage values. The alpha ramps differ.
- **Metrics rounding**: GDI handles sizing internally via `LOGFONT.lfHeight` (with DPI conversion), while FreeType and DirectWrite receive ppem values from the core. Rounding differences of 1 pixel in bearingX, bearingY, or advance are common.
- **Bold/italic synthesis**: Each backend applies synthetic bold and italic differently. GDI uses font mapper weight + MAT2 shear. FreeType uses `FT_Outline_Embolden` + `FT_GlyphSlot_Oblique`. DirectWrite uses `DWRITE_FONT_SIMULATIONS` flags. StbTrueType uses outline transforms for synthetic bold and oblique shear for italic.
- **Kerning**: All five backends return `null` from `GetKerningPairs` and delegate to the shared GPOS/kern table parser, so kerning values are consistent across backends.

For BMFont parity testing, use the GDI backend. For cross-platform builds, use FreeType. For WASM, AOT, or native-dependency-free deployments, use StbTrueType.

---

## 9. Adding Custom Backends

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
| `SupportsSyntheticBold` | `bool` | If true, can apply synthetic bold (emboldening) to outlines |
| `SupportsSyntheticItalic` | `bool` | If true, can apply synthetic italic (oblique/shear) to outlines |
