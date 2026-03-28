# BMFont Internals Reference

> **Research date**: 2026-03-26
> **Purpose**: Document the internal implementation details of AngelCode's BMFont tool — rasterization pipeline, metrics calculation, outline algorithm, channel encoding, atlas packing, and configuration — based on source code analysis. This serves as the authoritative reference for achieving parity in KernSmith's GDI and pluggable rasterizer backends.

---

## Table of Contents

1. [Rasterization Pipeline](#1-rasterization-pipeline)
2. [Glyph Borders/Outlines — The AddOutline Algorithm](#2-glyph-bordersoutlines--the-addoutline-algorithm)
3. [Layer System & Output Channel Encoding](#3-layer-system--output-channel-encoding)
4. [Font Metrics — lineHeight, base](#4-font-metrics--lineheight-base)
5. [Kerning](#5-kerning)
6. [Advance Width (xadvance)](#6-advance-width-xadvance)
7. [xoffset / yoffset Calculation](#7-xoffset--yoffset-calculation)
8. [Spacing vs Padding](#8-spacing-vs-padding)
9. [Texture Atlas Packing](#9-texture-atlas-packing)
10. [Channel Packing](#10-channel-packing)
11. [Output Formats](#11-output-formats)
12. [Font Effects](#12-font-effects)
13. [Complete .bmfc Configuration Keys](#13-complete-bmfc-configuration-keys)
14. [Rendering Pseudocode](#14-rendering-pseudocode)
15. [Sources](#15-sources)

---

## 1. Rasterization Pipeline

BMFont is a Windows-only C++ application using **exclusively Windows GDI** for rasterization. It does not use FreeType, DirectWrite, or any other text rendering library.

### Two Rendering Paths

BMFont implements two rendering paths. It tries Path A first; if that fails (returns -1), it falls back to Path B.

#### Path A: Outline Rendering (`DrawGlyphFromOutline`)

The higher-quality path, operating on raw TrueType contour data:

| Step | Detail |
|------|--------|
| **Contour extraction** | `GetGlyphOutlineW` / `GetGlyphOutlineA` with `GGO_NATIVE` flag |
| **Hinting control** | Optionally uses `GGO_UNHINTED` to disable hinting |
| **Data structures** | Parses `TTPOLYGONHEADER` and `TTPOLYCURVE` structures |
| **Curve interpolation** | For quadratic B-splines (`TT_PRIM_QSPLINE`), interpolates **100 points** per curve segment |
| **Internal supersampling** | Fixed **8x** internal supersampling — renders at 8x resolution then downscales via `DownscaleImage()` (averages 8x8 = 64 samples per output pixel) |
| **Non-smoothed threshold** | `c >= 150 ? 255 : 0` |
| **Polygon fill** | GDI `PolyPolygon()` |

#### Path B: Bitmap Rendering (`DrawGlyphFromBitmap`)

The fallback path for glyphs where outline data is unavailable:

| Step | Detail |
|------|--------|
| **Surface** | 32-bit DIB section via `CreateDIBSection` |
| **Rendering** | `TextOutW` / `TextOutA` (white on black, `TRANSPARENT` background mode) |
| **ClearType handling** | Averages R, G, B channels: `c = ((c&0xFF) + ((c>>8)&0xFF) + ((c>>16)&0xFF)) / 3` |

### Three Smoothing Modes

Smoothing is set during `CreateFont()` via the quality parameter:

| Mode | GDI Quality Flag | Result |
|------|-------------------|--------|
| No smoothing | `NONANTIALIASED_QUALITY` | Binary black/white |
| Smooth | `ANTIALIASED_QUALITY` | Standard grayscale antialiasing |
| ClearType | `CLEARTYPE_QUALITY` | Sub-pixel rendering, then averaged to grayscale |

### Supersampling (`aa` Factor, 1-4x)

| Detail | Value |
|--------|-------|
| **Font creation** | Font created at `fontSize * aa` |
| **Downscale filter** | Box filter — 2x2, 3x3, or 4x4 depending on `aa` value |
| **Stacking with outline path** | Outline path's fixed 8x + 4x AA = effectively **32x** supersampling |
| **Metric adjustment** | All metrics divided by `aa` after rendering |

---

## 2. Glyph Borders/Outlines — The AddOutline Algorithm

The outline is a **post-processing dilation pass**, not a separate rendering operation. Implemented in `CFontChar::AddOutline(int thickness)`.

### Step 1: Expand Image

Expand the glyph image by `thickness` pixels on all sides:

```cpp
m_width  += thickness * 2;
m_height += thickness * 2;
m_xoffset -= thickness;
m_yoffset -= thickness;
```

### Step 2: Build Circular Antialiased Kernel

Kernel size is `(2*thickness+1)^2`:

```cpp
for (int y = 0; y < kernelWidth; y++) {
    for (int x = 0; x < kernelWidth; x++) {
        float val;
        if (x == thickness || y == thickness)
            val = 1;  // axis-aligned pixels always full
        else {
            val = thickness + 1 - thickness * float(
                (x - thickness) * (x - thickness) +
                (y - thickness) * (y - thickness)
            ) / (thickness * thickness);
            if (val > 1) val = 1;
            else if (val < 0) val = 0;
        }
        kernel[y * kernelWidth + x] = val;
    }
}
```

> **Note:** This is not a true Euclidean distance field. It produces a roughly circular shape with quadratic distance falloff.

### Step 3: Morphological Dilation

Convolve using a **MAX** operation (not additive):

| Position | Channel | Content |
|----------|---------|---------|
| Center (glyph body) | RGB | Original glyph intensity |
| Non-center (outline region) | Alpha | Outline intensity |

After `AddOutline`, `m_colored = true` and pixels encode dual-channel data:
- **RGB** = original glyph intensity
- **Alpha** = outline/border intensity

---

## 3. Layer System & Output Channel Encoding

BMFont has no true configurable layer system. It uses an implicit 2-layer model: **glyph + outline**.

### `GetPixelValue` with `EChnlValues` Enum

| Value | Name | Returns |
|-------|------|---------|
| 0 | `e_glyph` | Blue channel (glyph body, 0 in outline region) |
| 1 | `e_outline` | 255 for glyph pixels, alpha for outline pixels |
| 2 | `e_glyph_outline` | `0x80 \| (blue >> 1)` for glyph (128-255), `alpha >> 1` for outline (0-127) |
| 3 | `e_zero` | 0 |
| 4 | `e_one` | 255 |

### Typical Outlined Font Setup

- **Alpha** = `e_outline`, **RGB** = `e_glyph`
- Two-pass rendering: first draw outline (alpha as opacity), then draw glyph (RGB as opacity)

### Per-Channel Inversion

Each channel can be independently inverted via `invA`, `invR`, `invG`, `invB` configuration flags.

---

## 4. Font Metrics — lineHeight, base

All metrics come from Windows GDI `TEXTMETRIC`, **not** direct TTF table parsing:

```cpp
TEXTMETRIC tm;
GetTextMetrics(dc, &tm);
height = (int)ceil(float(tm.tmHeight) / aa);  // tmHeight = tmAscent + tmDescent
base   = (int)ceil(float(tm.tmAscent) / aa);

// With scale height applied:
lineHeight = int(ceilf(height * float(scaleH) / 100.0f));
base       = int(ceilf(base   * float(scaleH) / 100.0f));
```

### Key Details

| Metric | Formula | Notes |
|--------|---------|-------|
| `lineHeight` | `ceil(tmHeight / aa) * scaleH / 100` | Does **NOT** include `tmExternalLeading` |
| `base` | `ceil(tmAscent / aa) * scaleH / 100` | Directly the font's ascender |
| `scaleW` / `scaleH` in common block | — | Texture atlas dimensions, not font metrics |
| Padding / spacing | — | Do **NOT** affect `lineHeight` or `base` |

### Font Size Handling

| Mode | Behavior |
|------|----------|
| Positive `fontSize` | Passed to `CreateFont` as cell height (includes internal leading) |
| Negative `fontSize` ("Match char height") | Em height mode (excludes internal leading) |

- Size is in **pixels**, not points
- `scaleH` percentage applies vertical scaling via GDI world transform

---

## 5. Kerning

Extracted from three sources, in order of priority:

| Priority | Source | Notes |
|----------|--------|-------|
| 1 | Windows API (`GetKerningPairsW` / `GetKerningPairsA`) | Tried first |
| 2 | GPOS table (`GetKerningPairsFromGPOS()`) | Only if Windows API returns zero pairs |
| 3 | kern table (`GetKerningPairsFromKERN()`) | Fallback |

### GPOS Parsing (`unicode.cpp`)

- Finds `DFLT` script, default language system
- Scans for `kern` feature tag
- Supports PairAdjustment **Format 1** (individual pairs) and **Format 2** (class-based)
- Extracts only **XAdvance** from ValueRecord — ignores XPlacement, YPlacement, YAdvance

### Design Unit to Pixel Scaling

```cpp
float factor = float(tm.otmrcFontBox.top - tm.otmrcFontBox.bottom) /
               float(yMax - yMin);  // yMax, yMin from head table
```

### Rounding

Round away from zero:
- Positive: `int(kern + 0.5f)`
- Negative: `int(kern - 0.5f)`

### Post-Processing

- Divided by `aa` when writing output
- Zero-amount pairs are discarded

---

## 6. Advance Width (xadvance)

Derived from Windows ABC character widths:

```cpp
ABC abc;
GetCharABCWidths(dc, ch, ch, &abc);
m_advance = abc.abcA + abc.abcB + abc.abcC;  // A + B + C
```

Where:

| Component | Meaning |
|-----------|---------|
| `abcA` | Left side bearing (A-space) |
| `abcB` | Black width (glyph body) |
| `abcC` | Right side bearing (C-space) |

### Critical Behavior

- **Padding does NOT modify xadvance**
- **Outline does NOT modify xadvance**
- The outline thickness from the info block is available for dynamic adjustment at render time

```
final_xadvance = (abcA + abcB + abcC) / aa
```

---

## 7. xoffset / yoffset Calculation

### Initial Values

```cpp
m_xoffset = abc.abcA;    // left side bearing (A-space)
m_yoffset = 0;           // set during rendering
```

### Modifications Applied in Sequence

| Order | Operation | Effect |
|-------|-----------|--------|
| 1 | **TrimLeftAndRight** | Removes empty columns: `m_xoffset += trimmedColumns` |
| 2 | **AA downscale** | `m_xoffset /= aa; m_yoffset /= aa` |
| 3 | **Outline** | `m_xoffset -= thickness; m_yoffset -= thickness` |
| 4 | **Padding** | `m_xoffset -= paddingLeft; m_yoffset -= paddingUp` |

### Final Formulas

```
final_xoffset  = (abcA + trimLeft) / aa - outlineThickness - paddingLeft
final_yoffset  = (initialY) / aa - outlineThickness - paddingUp
final_width    = (trimmedWidth / aa) + outlineThickness*2 + paddingLeft + paddingRight
final_height   = (trimmedHeight / aa) + outlineThickness*2 + paddingUp + paddingDown
final_xadvance = (abcA + abcB + abcC) / aa
```

---

## 8. Spacing vs Padding

| Property | Scope | Affects Metrics? | Purpose |
|----------|-------|:---:|---------|
| **Spacing** (`spacingHoriz`, `spacingVert`) | Atlas packing only | No | Gap between glyph rectangles on texture; prevents bleeding with mipmapping/bilinear filtering |
| **Padding** (`paddingUp/Down/Left/Right`) | Per-glyph | Yes (width, height, xoffset, yoffset) | Extra pixels around each glyph for post-processing effects (outline, shadow) |

### Spacing Details

- Gap between glyph rectangles on the texture atlas
- Does **NOT** affect text rendering or character metrics
- **NOT** written to per-character data in the .fnt file

### Padding Details

- Extra pixels included in the character's `width` and `height`
- **DOES** affect `xoffset` and `yoffset` (offsets shift to compensate)
- Net effect cancels out during rendering — the padding area is reserved for post-processing effects
- Should be >= outline thickness to avoid clipping

---

## 9. Texture Atlas Packing

### Algorithm: Skyline Packer with Hole-Filling

| Step | Detail |
|------|--------|
| **Data structure** | Height array per channel, tracking max Y at each X coordinate |
| **Sort order** | Glyphs sorted largest-to-smallest by height, then width |
| **Placement** | Scans heightmap at `currX` to find max Y, places glyph at `(currX, maxY)` |
| **Row wrapping** | Wraps to next row when horizontal space is exhausted |
| **Hole filling** | Records gaps between tall + short glyphs as holes; fills them with smaller glyphs |
| **Multiple pages** | When space is exhausted, allocates a new page with a fresh heightmap |

---

## 10. Channel Packing

When `fourChnlPacked=true` and `outBitDepth=32`:

- Up to **4 monochrome glyphs per texel** (one per channel)
- Each channel gets an **independent heightmap** — glyphs can overlap spatially in different channels
- `packed` bit in common block's `bitField` indicates channel packing is active

### Channel Assignment (per-char `chnl` field)

| Value | Channel |
|-------|---------|
| 1 | Blue |
| 2 | Green |
| 4 | Red |
| 8 | Alpha |
| 15 | All (colored/icon glyph) |

### Common Block Channel Descriptors (`alphaChnl`, `redChnl`, `greenChnl`, `blueChnl`)

| Value | Meaning |
|-------|---------|
| 0 | Glyph data |
| 1 | Outline data |
| 2 | Glyph + outline encoded together |
| 3 | Set to zero |
| 4 | Set to one |

> **Note (2026-03-27):** BMFont64 uses these channel settings to control outline rendering -- there is no explicit "outline color" setting. A typical outlined font uses `alphaChnl=1` (outline data in alpha), `redChnl=0, greenChnl=0, blueChnl=0` (glyph data in RGB). The pixel shader then renders in two passes: first the outline using the alpha channel with the outline color, then the glyph body using the RGB channels with the text color. KernSmith's `outlineColor` extension provides explicit color control instead of relying on channel encoding.

---

## 11. Output Formats

| Format | Bit Depths | Compression | Notes |
|--------|-----------|-------------|-------|
| PNG | 8, 24, 32 | Deflate (always) | BGR to RGB swap via `png_set_bgr` |
| TGA | 8, 24, 32 | None or RLE | Native BGR/BGRA order |
| DDS | 8, 24, 32 | None, DXT1, DXT3, DXT5 | Uses squish library; 4px alignment for block compression |

---

## 12. Font Effects

| Effect | Supported | Implementation |
|--------|:---------:|----------------|
| Outline/Border | Yes | Morphological dilation post-process (`AddOutline`) |
| Bold | Yes | Delegates to Windows `CreateFont(FW_BOLD)` |
| Italic | Yes | Delegates to Windows `CreateFont(italic=TRUE)` |
| Shadow | No | Users add padding + external post-process |
| Gradient | No | — |
| Glow | No | Outline + alpha can approximate |
| SDF | No | External tools (msdfgen, Hiero) generate BMFont-compatible output |

---

## 13. Complete .bmfc Configuration Keys

| Key | Type | Description |
|-----|------|-------------|
| `fileVersion` | int | Config format version |
| `fontName` | string | Font family name |
| `fontFile` | string | Path to .ttf/.otf (for non-installed fonts) |
| `charSet` | int | Windows character set ID |
| `fontSize` | int | Size in pixels; negative = match char height |
| `aa` | int | Supersampling level (1-4) |
| `scaleH` | int | Vertical scale percentage (default 100) |
| `useSmoothing` | bool | Enable font smoothing |
| `isBold` | bool | Render bold |
| `isItalic` | bool | Render italic |
| `useUnicode` | bool | Unicode encoding |
| `disableBoxChars` | bool | Skip missing glyphs |
| `outputInvalidCharGlyph` | bool | Include .notdef glyph |
| `dontIncludeKerningPairs` | bool | Omit kerning data |
| `useHinting` | bool | Apply TrueType hinting |
| `renderFromOutline` | bool | Use outline rendering path (Path A) |
| `useClearType` | bool | Enable ClearType smoothing |
| `paddingDown` | int | Bottom padding pixels |
| `paddingUp` | int | Top padding pixels |
| `paddingRight` | int | Right padding pixels |
| `paddingLeft` | int | Left padding pixels |
| `spacingHoriz` | int | Horizontal atlas spacing pixels |
| `spacingVert` | int | Vertical atlas spacing pixels |
| `useFixedHeight` | bool | Equalize cell heights across all glyphs |
| `forceZero` | bool | Force offsets to zero |
| `outWidth` | int | Texture page width |
| `outHeight` | int | Texture page height |
| `outBitDepth` | int | Output bit depth (8 or 32) |
| `fontDescFormat` | int | Descriptor format: 0=text, 1=XML, 2=binary |
| `fourChnlPacked` | bool | Enable 4-channel packing |
| `textureFormat` | string | Output format: `"tga"`, `"png"`, `"dds"` |
| `textureCompression` | int | Compression mode (format-dependent) |
| `alphaChnl` | int | Alpha channel content (0-4) |
| `redChnl` | int | Red channel content (0-4) |
| `greenChnl` | int | Green channel content (0-4) |
| `blueChnl` | int | Blue channel content (0-4) |
| `invA` | bool | Invert alpha channel |
| `invR` | bool | Invert red channel |
| `invG` | bool | Invert green channel |
| `invB` | bool | Invert blue channel |
| `outlineThickness` | int | Outline width in pixels |
| `chars` | string (multi) | Character range entries |
| `icon` | string (multi) | Icon image entries |

---

## 14. Rendering Pseudocode

The following pseudocode describes how a consumer should render text using BMFont output:

```
cursor = (startX, startY)
for each char:
    kerning = getKerning(prevChar, thisChar)
    cursor.x += kerning

    dst.left   = cursor.x + xoffset
    dst.top    = cursor.y + yoffset
    dst.right  = dst.left + width
    dst.bottom = dst.top + height

    drawQuad(dst, srcRect from atlas)
    cursor.x += xadvance

for next line:
    cursor.y += lineHeight
```

---

## 15. Rendering Pipeline Detail

Additional implementation details from the glyph rendering pipeline.

### Rendering Order

1. Create DC, select font
2. Get `TEXTMETRIC` **before** world transform (values are inconsistent after transform)
3. Compute scaled `fontHeight = ceil(tmHeight * scaleH / 100)` and `fontAscent = ceil(tmAscent * scaleH / 100)`
4. Apply `scaleH` world transform: `eM22 = scaleH / 100.0f`
5. Render: try `DrawGlyphFromOutline()`, fall back to `DrawGlyphFromBitmap()`
6. `TrimLeftAndRight()` — remove empty columns, adjust `m_xoffset`
7. Supersampling downscale (if `aa > 1`) — divide dimensions and metrics by `aa`
8. Empty scanline removal — trim top/bottom empty rows, adjust `m_yoffset`/`m_height`
9. `ForceZero` adjustment (if enabled) — force `xoffset = 0` by expanding bitmap

### Outline Path Metrics

From `DrawGlyphFromOutline()`:
- `m_advance = gm.gmCellIncX` (from `GLYPHMETRICS`)
- `m_xoffset = minX / scale` (from outline bounding box)
- `m_yoffset = fontAscent - maxY/65536` (baseline-relative, set after 8x downscale)
- `m_width` / `m_height` from outline bounding box extents, then `/= 8`

### Bitmap Path Metrics

From `DrawGlyphFromBitmap()`:
- `m_width = abc.abcB` (black box width)
- `m_xoffset = abc.abcA` (left side bearing, can be negative for italic)
- `m_advance = abc.abcA + m_width + abc.abcC`
- `m_yoffset = 0` (top of cell)
- `m_height = fontHeight` (full cell height)
- Extra width buffer added for italic overhang, then trimmed by `TrimLeftAndRight()`

### Supersampling Division Details

```cpp
m_width  = int(ceilf(float(m_width) / aa));   // ceiling
m_height = int(ceilf(float(m_height) / aa));   // ceiling
m_xoffset /= aa;    // integer division (truncation)
m_yoffset /= aa;    // integer division (truncation)
m_advance /= aa;    // integer division (truncation)
```

Note: `width`/`height` use **ceiling**, but `xoffset`/`yoffset`/`advance` use **integer division** (truncation). This is an important distinction for parity testing.

### Empty Scanline Removal

After supersampling downscale, BMFont removes empty scanlines:
- Top: increments `m_yoffset`, decrements `m_height` per empty row
- Bottom: only decrements `m_height`
- Skipped when `fixedHeight` or `forceZero` is true

### ForceZero Mode

When `forceZero` is enabled:
- `m_xoffset` forced to 0 by adding empty columns to left
- If `m_advance > m_width`, empty columns added to right to fill advance width
- `m_height` set to `fontHeight` (full cell), `m_yoffset` set to 0

### Font Size Sign Convention

The sign of `fontSize` passed to `CreateFont` is critical:

| `fontSize` sign | `lfHeight` to `CreateFont` | GDI interpretation | ppem |
|---|---|---|---|
| **Positive** (BMFont default) | `+fontSize * aa` | Cell height (includes internal leading) | `fontSize * unitsPerEm / (usWinAscent + usWinDescent)` |
| **Negative** ("Match char height") | `-fontSize * aa` | Em height (excludes internal leading) | `abs(fontSize)` |

This has a **major impact** on fonts with large internal leading (e.g., Batang, BatangChe, Bell MT) — positive vs negative lfHeight produces different glyph sizes and metrics.

---

## 16. Sources

### Official Documentation

- **BMFont Official Site**: https://www.angelcode.com/products/bmfont/
- **BMFont File Format**: https://www.angelcode.com/products/bmfont/doc/file_format.html
- **BMFont Font Settings**: https://www.angelcode.com/products/bmfont/doc/font_settings.html
- **BMFont Export Options**: https://www.angelcode.com/products/bmfont/doc/export_options.html
- **BMFont Render Text**: https://angelcode.com/products/bmfont/doc/render_text.html

### Source Code

- **GitHub mirror (OpenTechEngine)**: https://github.com/OpenTechEngine/bmfont
- **GitHub mirror (kylawl)**: https://github.com/kylawl/bmfont
- **SourceForge SVN**: https://sourceforge.net/p/bmfont/code/HEAD/tree/
- **Key source files**: `fontchar.cpp`, `fontgen.cpp`, `fontpage.cpp`, `unicode.cpp`, `choosefont.cpp`

### Related Projects

- **fontbm** (cross-platform reimplementation): https://github.com/vladimirgamalyan/fontbm
