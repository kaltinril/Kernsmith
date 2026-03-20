# FreeTypeSharp Deep Evaluation for BMFontier

**Date:** 2026-03-18
**Package:** [FreeTypeSharp](https://github.com/ryancheung/FreeTypeSharp) by ryancheung
**NuGet:** `FreeTypeSharp` v3.1.0 (Feb 6, 2026)
**License:** MIT
**FreeType version bundled:** 2.13.2

---

## 1. Project Overview

FreeTypeSharp is a **thin, auto-generated P/Invoke wrapper** around the native FreeType2 C library. The README states: "There's no magic (abstraction) based on the original C freetype API. All managed API are almost identical with the original freetype C API."

The bindings are source-generated from the original C headers using a custom generator tool (`FreeTypeSharp.Generator`). There is also a small convenience layer (`FreeTypeFaceFacade`, `FreeTypeLibrary`) on top, but the primary API surface is raw unsafe pointers.

### Code Example (from README)
```csharp
using static FreeTypeSharp.FT;

FT_LibraryRec_* lib;
FT_FaceRec_* face;
var error = FT_Init_FreeType(&lib);
error = FT_New_Face(lib, (byte*)Marshal.StringToHGlobalAnsi("font.ttf"), 0, &face);
error = FT_Set_Char_Size(face, 0, 16 * 64, 300, 300);
```

This is **unsafe C# code** that mirrors the C API almost verbatim.

---

## 2. API Surface Analysis

### What It CAN Do

| Capability | API | Notes |
|---|---|---|
| **Load font from file** | `FT_New_Face` | Yes |
| **Load font from memory** | `FT_New_Memory_Face` | Yes -- accepts IntPtr + length. Critical for in-memory loading. |
| **Load font from stream** | `FT_Open_Face` | Yes -- custom stream support via `FT_Open_Args` / `FT_StreamRec` |
| **Get font metadata** | `FT_FaceRec_.family_name`, `style_name` | Raw byte pointers; need manual marshaling. Facade has `MarshalFamilyName()` / `MarshalStyleName()`. |
| **Font-level metrics** | `FT_FaceRec_` fields | `ascender`, `descender`, `height` (line spacing), `units_per_EM`, `max_advance_width`, `max_advance_height`, `underline_position`, `underline_thickness` -- all available. |
| **Scaled metrics** | `FT_Size_Metrics_` | `x_ppem`, `y_ppem`, `x_scale`, `y_scale`, `ascender`, `descender`, `height`, `max_advance` -- all in 26.6 fixed point. |
| **Per-glyph metrics** | `FT_Glyph_Metrics_` | `width`, `height`, `horiBearingX`, `horiBearingY`, `horiAdvance`, `vertBearingX`, `vertBearingY`, `vertAdvance` -- all available. |
| **Unicode to glyph index** | `FT_Get_Char_Index` | Yes |
| **Enumerate all chars** | `FT_Get_First_Char` / `FT_Get_Next_Char` | Yes |
| **Kerning pairs** | `FT_Get_Kerning` | Yes -- supports `FT_KERNING_DEFAULT`, `FT_KERNING_UNFITTED`, `FT_KERNING_UNSCALED` modes. Returns x,y vector. |
| **Track kerning** | `FT_Get_Track_Kerning` | Yes |
| **Rasterize glyph to bitmap** | `FT_Load_Glyph` + `FT_Render_Glyph` or `FT_LOAD_RENDER` flag | Yes |
| **Anti-aliasing modes** | `FT_Render_Mode_` enum | `NORMAL` (8-bit AA), `LIGHT`, `MONO` (1-bit), `LCD`, `LCD_V`, **`SDF`** |
| **SDF bitmap generation** | `FT_RENDER_MODE_SDF` | Yes -- FreeType 2.13+ supports SDF natively |
| **Control size/DPI** | `FT_Set_Char_Size`, `FT_Set_Pixel_Sizes` | Full control over char size and DPI |
| **Glyph bitmap access** | `FT_Bitmap_` struct | `buffer` (raw pixels), `rows`, `width`, `pitch`, `pixel_mode`, `num_grays` |
| **Outline access** | `FT_Outline_` + decompose/transform ops | Full outline API for vector glyph data |
| **Stroking** | `FT_Stroker_*` family | Full stroker API for outline effects |
| **Emboldening** | `FT_GlyphSlot_Embolden`, `FT_Outline_Embolden` | Synthetic bold |
| **Oblique/Slant** | `FT_GlyphSlot_Oblique`, `FT_GlyphSlot_Slant` | Synthetic italic |
| **Transform** | `FT_Set_Transform`, `FT_Get_Transform` | Arbitrary 2x2 matrix + delta |
| **Face flags** | `FT_FACE_FLAG` enum | Detect: scalable, fixed sizes, kerning, color, variation, SFNT, glyph names, etc. (19 flags) |
| **Glyph names** | `FT_Get_Glyph_Name` | Yes |
| **PostScript name** | `FT_Get_Postscript_Name` | Yes |
| **Color/COLR support** | `FT_Palette_*`, `FT_Get_Color_Glyph_*` | Full COLRv0/v1 API |
| **Embedding flags** | `FT_Get_FSType_Flags` | Yes |
| **Advances (fast)** | `FT_Get_Advance`, `FT_Get_Advances` | Bulk advance retrieval without full glyph load |
| **Font collections (.ttc)** | `FT_New_Face` with face_index | Yes -- `num_faces` field tells how many faces |
| **Load flags** | `FT_LOAD` enum | 22 flags: NO_SCALE, NO_HINTING, RENDER, NO_BITMAP, VERTICAL_LAYOUT, FORCE_AUTOHINT, COLOR, etc. |
| **Caching** | `FTC_*` family | Full glyph/image/CMap cache subsystem |

### What It CANNOT Do (Missing from bindings)

| Capability | Missing API | Impact |
|---|---|---|
| **Access SFNT tables directly** | `FT_Get_Sfnt_Table` NOT bound | **HIGH** -- Cannot read OS/2, hhea, post tables directly. Cannot get xHeight, capHeight, sTypoAscender, panose, weight class, width class, etc. |
| **Get SFNT name strings** | `FT_Get_Sfnt_Name`, `FT_Get_Sfnt_Name_Count` NOT bound | **MEDIUM** -- Cannot read name table entries (copyright, description, license, etc.) |
| **Get font format** | `FT_Get_Font_Format` NOT bound | **LOW** -- Cannot programmatically detect TrueType vs CFF vs Type1 |
| **Variable font axis data** | `FT_Get_MM_Var`, `FT_Set_Var_Design_Coordinates`, etc. NOT bound | **MEDIUM** -- Cannot enumerate or set variation axes, despite `FT_FACE_FLAG_VARIATION` being detectable |
| **OpenType table validation** | `FT_OpenType_Validate` NOT bound | **LOW** -- Validation constants are defined but functions aren't |
| **GPOS table access** | No direct GPOS API | **HIGH** -- FreeType only exposes kern table kerning via `FT_Get_Kerning`, NOT OpenType GPOS kerning. This is a fundamental limitation for modern fonts. |
| **Enumerate system fonts** | Not a FreeType feature | **N/A** -- FreeType does not do font discovery; need fontconfig or platform APIs |

### The GPOS Kerning Problem (Critical)

`FT_Get_Kerning` only reads the legacy `kern` table. **Most modern fonts store kerning in the GPOS (Glyph Positioning) table**, which FreeType handles internally during layout but does NOT expose as extractable pair data.

To get GPOS kerning pairs with FreeType, you must:
1. Use `FT_HAS_KERNING` to check (but this only checks `kern` table)
2. For GPOS kerning, load each glyph with hinting and compare positioned advances -- but this doesn't give you the raw kerning pair table

**This means:** If a font has kerning only in GPOS (common for modern fonts), `FT_Get_Kerning` returns zero. To extract kerning pairs for BMFont output, you would need to either:
- Parse the GPOS table yourself (own parser needed)
- Use HarfBuzz (separate native dependency) for shaping-based kerning extraction
- Brute-force: for every glyph pair, load both glyphs and compute effective kerning by comparing advances (extremely slow for large character sets)

---

## 3. Native Dependencies

### Bundled Platforms

| Platform | Architecture | Binary |
|---|---|---|
| Windows | x86, x64, arm64 | `freetype.dll` |
| Linux | x64 | `libfreetype.so` |
| macOS | universal? | `libfreetype.dylib` |
| Android | arm64-v8a, armeabi-v7a, x86, x86_64 | `libfreetype.so` |
| iOS | xcframework | static lib |
| tvOS | xcframework | static lib |

### Package Size
- **NuGet package: 11.71 MB** (all native binaries included)
- No separate runtime packages -- everything in one package

### Resolution Strategy
- Searches NuGet runtime dirs, architecture-specific folders, app directory, then system paths
- Android/iOS handled specially
- Falls back to system FreeType if bundled not found

### Notable Gaps
- **No Linux ARM** (arm64) -- only x64
- **No WASM/Blazor** support
- **No Windows ARM32**

---

## 4. NuGet Package Details

| Property | Value |
|---|---|
| Package name | `FreeTypeSharp` |
| Current version | 3.1.0 |
| Published | February 6, 2026 |
| Total downloads | ~84,200 |
| Daily average | ~39 |
| Target frameworks | .NET Standard 2.0, .NET Core 3.1, .NET 9.0, net9.0-android35.0, net9.0-ios18.0, net9.0-tvos18.0 |
| Dependencies | **None** |
| License | MIT |
| Package size | 11.71 MB |

Also available: `FreeTypeSharp.UWP` for UWP targets.

---

## 5. Maintenance Status

| Metric | Value |
|---|---|
| Maintainer | ryancheung (sole maintainer) |
| Total commits | 149 |
| Stars | 83 |
| Forks | 12 |
| Open issues | 7 |
| Last release | v3.1.0 (Feb 6, 2026) -- Apple Color Emoji support |
| Previous release | v3.0.1 (Jul 23, 2025) |
| Release cadence | ~2-3 releases per year |

### Open Issues Summary
- Android 16KB page alignment (#33)
- sbix support gap (#32)
- Segfault with custom FT_StreamRec (#31)
- DLL search path issues (#30)
- Missing rasterization methods (#24, reopened)

**Assessment:** Moderately maintained. One-person project. Regular releases but small community. Issues stay open for extended periods.

---

## 6. Code Quality Assessment

### Architecture
- **Thin P/Invoke wrapper** -- auto-generated from C headers
- Small convenience layer (`FreeTypeFaceFacade`, `FreeTypeLibrary`) provides some managed ergonomics
- 69 generated struct/enum/function files mapping directly to C types

### Error Handling
- `FreeTypeException` maps ~100+ FreeType error codes to human-readable messages
- Convenience layer throws exceptions; raw API returns error codes
- Standard .NET exception pattern

### Memory Management
- `FreeTypeLibrary` implements `IDisposable` correctly (calls `FT_Done_FreeType`)
- `FreeTypeFaceFacade` does NOT appear to implement IDisposable -- **potential leak of FT_Face**
- Raw API users must manually call `FT_Done_Face`, `FT_Done_FreeType`, etc.
- All pointers are raw unsafe pointers -- no SafeHandle usage

### Thread Safety
- No thread safety mechanisms in the wrapper
- FreeType itself is thread-safe for independent faces with independent libraries
- Shared library instances are NOT thread-safe (FreeType limitation)

### Ergonomics
- Raw API requires `unsafe` context everywhere
- Must manually marshal strings (byte pointers)
- Must manually convert 26.6 fixed-point values
- Facade helps for common operations but covers limited surface

---

## 7. Alternatives Comparison

### SpaceWizards.SharpFont
- **Downloads:** 3.4M total (much larger user base)
- **API style:** Higher-level managed wrapper (methods on objects, exceptions, no unsafe required)
- **Native binaries:** NOT bundled -- must provide your own
- **FreeType version:** Unclear, older
- **Maintenance:** Fork of abandoned Robmaister/SharpFont. Updated but not actively developed
- **Verdict:** More ergonomic but less actively maintained and doesn't bundle natives

### Hexa.NET.FreeType
- **Downloads:** ~1,900
- **Targets:** .NET 6-9, .NET Standard 2.0/2.1
- **Package size:** 26.56 MB
- **Depends on:** HexaGen.Runtime
- **FreeType version:** 2.13.2
- **Verdict:** Similar approach (auto-generated), larger package, tiny community. Potentially exposes more APIs (needs investigation).

### GirCore.FreeType2-2.0
- **Downloads:** ~217,000
- **Targets:** .NET 8.0+
- **Verdict:** Part of the GirCore GObject binding ecosystem. Not standalone-friendly.

### Roll-your-own P/Invoke
- Always possible to add individual `[DllImport]` declarations for missing functions
- FreeTypeSharp's bundled native binaries are just standard FreeType -- all functions exist in the .dll/.so, just not all are bound in C#

---

## 8. Key Question: Own Parser vs. FreeTypeSharp for Everything

### What FreeTypeSharp Gives Us for BMFont Generation

For each character in the BMFont output, we need:

| BMFont Field | FreeTypeSharp Source | Available? |
|---|---|---|
| `id` (char code) | Input character | N/A |
| `x`, `y` (texture position) | Our packing algorithm | N/A |
| `width`, `height` (glyph bitmap size) | `FT_Bitmap_.width`, `FT_Bitmap_.rows` | YES |
| `xoffset` (bitmap left bearing) | `FT_GlyphSlotRec_.bitmap_left` | YES |
| `yoffset` (bitmap top bearing) | `FT_GlyphSlotRec_.bitmap_top` | YES |
| `xadvance` (cursor advance) | `FT_Glyph_Metrics_.horiAdvance` | YES |
| `page` (texture page) | Our packing algorithm | N/A |
| `chnl` (channel) | Our choice | N/A |

For font-level BMFont fields:

| BMFont Field | FreeTypeSharp Source | Available? |
|---|---|---|
| `face` (family name) | `FT_FaceRec_.family_name` | YES |
| `size` | Input parameter | N/A |
| `bold`, `italic` | `FT_STYLE_FLAG` | YES |
| `lineHeight` | `FT_Size_Metrics_.height` | YES |
| `base` (baseline) | `FT_Size_Metrics_.ascender` | YES |
| `scaleW`, `scaleH` | Our texture size | N/A |
| `padding`, `spacing` | Our parameters | N/A |

For kerning pairs:

| BMFont Field | FreeTypeSharp Source | Available? |
|---|---|---|
| `first`, `second` (char pair) | Our enumeration | N/A |
| `amount` (kerning offset) | `FT_Get_Kerning` | **PARTIAL** -- kern table only, NOT GPOS |

### What We LOSE Using FreeTypeSharp Alone

1. **GPOS kerning** -- The single biggest gap. Modern fonts (especially those from Google Fonts, Adobe, etc.) store kerning exclusively in GPOS. `FT_Get_Kerning` will return 0 for these fonts. **This is a deal-breaker for quality BMFont output.**

2. **OS/2 table data** -- `FT_Get_Sfnt_Table` is not bound. We cannot access:
   - `sTypoAscender` / `sTypoDescender` (preferred metrics)
   - `sxHeight`, `sCapHeight`
   - `usWeightClass`, `usWidthClass`
   - PANOSE classification
   - Unicode coverage ranges
   - `fsSelection` flags

3. **Name table strings** -- Cannot read copyright, designer, description, license info

4. **Variable font control** -- Cannot enumerate or set axes for variable fonts

5. **Native dependency requirement** -- 11.71 MB of native binaries across platforms. Cannot run in pure managed/.NET environments. No WASM support.

### What We GAIN Using FreeTypeSharp

1. **Battle-tested rasterization** -- FreeType is the industry standard rasterizer. Hinting, anti-aliasing, LCD rendering, SDF generation all work correctly.

2. **No need to implement glyph scaling** -- FreeType handles all the math of converting font units to pixels.

3. **No need to parse glyf/CFF tables** -- Complex glyph outline parsing (composite glyphs, hinting instructions, CFF charstrings) is handled.

4. **Outline access** -- Full vector outline API if we ever need it.

5. **Correct metrics** -- Properly computed metrics accounting for hinting and grid-fitting.

6. **SDF support** -- Native SDF rendering without third-party algorithms.

---

## 9. Hybrid Architecture Analysis

### Option A: FreeTypeSharp for Everything
- **Pro:** Single dependency, simpler code
- **Con:** Missing GPOS kerning (critical), missing SFNT table access, native dependency required
- **Verdict:** NOT SUFFICIENT for quality BMFont output

### Option B: Own TTF Parser for Everything
- **Pro:** No native dependencies, full control, GPOS access, cross-platform
- **Con:** Must implement rasterization (extremely complex), must handle hinting, CFF, composites
- **Verdict:** Rasterization is a multi-year effort to match FreeType quality

### Option C: Own Parser for Tables/Metrics + FreeTypeSharp for Rasterization (RECOMMENDED)
- **Pro:** Best of both worlds
  - Own parser reads kern + GPOS tables for complete kerning coverage
  - Own parser reads OS/2, name, head, hhea tables for full metadata
  - Own parser can work with variable fonts at table level
  - FreeTypeSharp handles the hard part: rasterization, hinting, SDF
  - Can fall back to pure managed mode (no raster) if FreeTypeSharp unavailable
- **Con:** Two codepaths for some data (e.g., metrics from both parser and FreeType)
- **Mitigation:** Use own parser as source of truth for metrics/kerning, FreeTypeSharp only for bitmap generation

### Option D: FreeTypeSharp + Custom P/Invoke for Missing Functions
- **Pro:** Reuses FreeTypeSharp's native binaries, just add missing bindings
- **Con:** Still requires native dependency, still can't extract GPOS kerning pairs from FreeType (it's not a binding gap -- FreeType literally doesn't expose GPOS pairs as a queryable table)
- **Verdict:** Fixes the SFNT table gap but NOT the GPOS kerning gap

---

## 10. Recommendation

**Option C: Hybrid approach -- own TTF parser + FreeTypeSharp for rasterization.**

### Rationale

1. **GPOS kerning is non-negotiable.** Any modern BMFont generator that can't extract GPOS kerning will produce inferior output for the majority of professional fonts. FreeType fundamentally cannot provide this -- it's not a binding limitation, it's an architectural one. FreeType applies GPOS during shaping but doesn't expose it as extractable data.

2. **Rasterization is FreeType's strength.** Writing a quality rasterizer with proper hinting is enormously complex. FreeType has 25+ years of development. We should use it.

3. **SDF support tips the scale.** FreeType 2.13+ has native SDF rendering. Implementing our own SDF algorithm would add significant complexity.

4. **The parser we need is tractable.** For BMFont generation, we need to parse:
   - `head` -- units per em, bounding box
   - `hhea` / `vhea` -- ascender, descender, line gap
   - `hmtx` / `vmtx` -- advance widths (optional, FreeType gives us these too)
   - `OS/2` -- weight class, width class, typo metrics, x-height, cap height, panose
   - `name` -- font name strings
   - `cmap` -- character to glyph mapping (optional, FreeType does this)
   - `kern` -- legacy kerning table
   - **`GPOS`** -- OpenType kerning and positioning (the critical one)
   - `maxp` -- number of glyphs

   This is a well-defined scope. The GPOS parser is the most complex part but is well-documented in the OpenType spec.

5. **Graceful degradation.** If FreeTypeSharp is unavailable (WASM, restricted environment), the parser still provides all metadata and kerning -- only rasterization is lost. A fallback managed rasterizer could be added later.

### Architecture Sketch

```
BMFontier Pipeline:

  [TTF File / byte[]]
       |
       +---> [Own Parser] ---> Font metadata, metrics, kerning pairs (kern + GPOS)
       |
       +---> [FreeTypeSharp] ---> Glyph bitmaps (rasterized at target size)
       |
       +---> [Packer] ---> Texture atlas layout
       |
       +---> [BMFont Writer] ---> .fnt + .png output
```

### Risk Mitigation for FreeTypeSharp Dependency

- **Single-maintainer risk:** FreeTypeSharp is maintained by one person. If abandoned:
  - We can add missing P/Invoke bindings ourselves (it's just DllImport)
  - We can fork -- it's MIT licensed
  - The native FreeType binary is the real dependency; the C# wrapper is thin

- **Missing bindings we might want to add:**
  - `FT_Get_Sfnt_Table` -- access OS/2 table as a cross-check
  - `FT_Get_Font_Format` -- detect font type
  - These are trivial single-function P/Invoke additions

- **Package size:** 11.71 MB is significant but acceptable for a font tool. Can be trimmed by including only target platform natives.

---

## Appendix: Complete API Function List

The 200+ P/Invoke functions exposed by FreeTypeSharp v3.1.0 are organized into these categories:

- **Library management:** Init, Done, Version, Reference, Module management
- **Face management:** New_Face, New_Memory_Face, Open_Face, Done_Face, Attach, Properties
- **Sizing:** Set_Char_Size, Set_Pixel_Sizes, Select_Size, Request_Size
- **Glyph loading:** Load_Glyph, Load_Char (with 22 load flags)
- **Rendering:** Render_Glyph (6 render modes including SDF)
- **Character mapping:** Get_Char_Index, First/Next_Char, Select_Charmap
- **Kerning:** Get_Kerning (3 modes), Get_Track_Kerning
- **Advances:** Get_Advance, Get_Advances
- **Outlines:** Full decompose/transform/embolden/render pipeline
- **Bitmaps:** Init, Copy, Convert, Embolden, Blend, Done
- **Stroking:** Full stroker API
- **Transforms:** Set/Get_Transform, Matrix/Vector math
- **Glyph effects:** Embolden, AdjustWeight, Oblique, Slant
- **Color/COLR:** Palette, Color glyph layers, Paint API
- **Caching:** Manager, CMap cache, Image cache, SBit cache
- **Unicode variations:** Variant selectors, variant indices
- **Info:** Glyph names, PostScript name, FSType flags
