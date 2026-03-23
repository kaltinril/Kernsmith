# Font Metrics, Sizing, and BMFont Parity Reference

This document is the authoritative KernSmith project reference for understanding how font sizes translate into pixel metrics, why BMFont and FreeType produce different results at the same nominal size, and how to correct for the difference.

---

## 1. Font Size Terminology

### Em Square (unitsPerEm)

The em square is the abstract design grid in which a font's glyphs are drawn. It defines the coordinate space for all glyph outlines, metrics, and positioning values stored in the font file.

- TrueType fonts typically use **2048** units per em.
- CFF/PostScript-based fonts typically use **1000** units per em.

The em square is not a visible measurement. It is a dimensionless coordinate system. All font metrics (ascent, descent, advance widths, kerning values) are expressed in these design units and must be scaled to produce pixel values.

### Point Size

A typographic point is defined as exactly **1/72 of an inch**. When a user requests a 12pt font, they are asking for the em square to be scaled so that it occupies 12/72 = 1/6 inch on the output medium.

### ppem (Pixels Per Em)

The number of pixels that the em square occupies at a given point size and DPI:

```
ppem = pointSize * dpi / 72
```

At 72 DPI, 1 point equals 1 pixel, so ppem equals the point size numerically. At 96 DPI (Windows default), a 12pt font has ppem = 16.

### Cell Height

The full height of a font's character cell, including internal leading:

```
cellHeight = tmAscent + tmDescent
           = scaled(usWinAscent + usWinDescent)
           = round((usWinAscent + usWinDescent) * ppem / unitsPerEm)
```

The cell height is always greater than or equal to the em height, because fonts typically have ascenders and descenders that extend beyond the em square.

### Character Height (Em Height)

The height of the em square in pixels. Equal to ppem. This is the cell height minus internal leading:

```
characterHeight = ppem
                = cellHeight - internalLeading
```

### Internal Leading

The space within the cell height that falls outside the em square. It accounts for the fact that usWinAscent + usWinDescent typically exceeds unitsPerEm:

```
internalLeading = cellHeight - ppem
                = scaled(usWinAscent + usWinDescent - unitsPerEm)
```

In Windows GDI terms: `tmInternalLeading = tmHeight - ppem`.

---

## 2. OpenType Font Table Metrics

OpenType fonts contain three independent sets of vertical metrics, each intended for different platforms and contexts. Understanding which set is used in which context is critical to achieving cross-platform metric parity.

### OS/2 Table

The OS/2 table contains two pairs of vertical metrics:

**Win metrics (clipping rectangle):**

| Field | Type | Description |
|-------|------|-------------|
| `usWinAscent` | unsigned, positive | Distance from baseline to top of clipping rectangle, in design units |
| `usWinDescent` | unsigned, positive | Distance from baseline to bottom of clipping rectangle, in design units (note: positive value despite being below baseline) |

These define the clipping rectangle for rendered glyphs on Windows. Legacy Windows GDI also uses them for line spacing computation.

**Typo metrics (intended line spacing):**

| Field | Type | Description |
|-------|------|-------------|
| `sTypoAscender` | signed, positive | Typographic ascender, in design units |
| `sTypoDescender` | signed, negative | Typographic descender, in design units |
| `sTypoLineGap` | signed, non-negative | Additional line gap, in design units |

These represent the font designer's intended line spacing. Line height = sTypoAscender - sTypoDescender + sTypoLineGap.

**USE_TYPO_METRICS flag:**

Bit 7 of the `fsSelection` field in the OS/2 table. When set, applications should use sTypo* values for line spacing instead of usWin* values. This flag was introduced to resolve the ambiguity between the two metric sets.

### hhea Table

| Field | Type | Description |
|-------|------|-------------|
| `ascender` | signed, positive | Typographic ascender, in design units |
| `descender` | signed, negative | Typographic descender, in design units |
| `lineGap` | signed, non-negative | Additional line gap, in design units |

These are the primary vertical metrics on Apple platforms. Microsoft recommends setting hhea values equal to sTypo* values, but many fonts do not follow this recommendation.

### Platform Usage

| Context | Metrics Used |
|---------|-------------|
| Windows GDI (legacy) | usWinAscent / usWinDescent for both line spacing and clipping |
| Windows GDI with USE_TYPO_METRICS | sTypo* for line spacing; usWin* still used for clipping |
| macOS / iOS (CoreText) | hhea ascender / descender / lineGap |
| Web browsers on macOS | hhea values |
| Web browsers on Windows | Depends on USE_TYPO_METRICS flag |
| DirectWrite (modern Windows) | sTypo* when USE_TYPO_METRICS set; usWin* otherwise |

---

## 3. Windows GDI Font Sizing (How BMFont Works)

BMFont uses the Windows GDI `CreateFont()` API to create fonts for rasterization. The critical parameter is `lfHeight` in the `LOGFONT` structure.

### LOGFONT.lfHeight Semantics

The sign of `lfHeight` determines how the font mapper interprets the size:

| lfHeight Value | Maps To | Meaning |
|----------------|---------|---------|
| `> 0` | Cell height | Font is sized so that the full cell (tmAscent + tmDescent) equals the requested value |
| `< 0` | Character height (em height) | Font is sized so that the em square equals abs(lfHeight) pixels |
| `= 0` | Default size | System default |

### BMFont's Default Behavior

BMFont creates fonts with a **positive** lfHeight:

```cpp
HFONT font = CreateFont(fontSize * aa, ...);  // positive = cell height
```

Where `aa` is the supersampling factor (1, 2, 4, etc.). This means that when a user specifies fontSize=32, BMFont asks GDI for a font whose cell height is 32 pixels (before supersampling).

### BMFont's "Match Char Height" Option

When the user enables "Match char height" in the BMFont settings dialog, the fontSize is negated before being passed to CreateFont:

```cpp
HFONT font = CreateFont(-fontSize * aa, ...);  // negative = em height
```

This makes fontSize=32 mean "em square = 32 pixels." The .fnt output file records this as a **negative** `size` value in the info block, per BMFont convention:

```
info ... size=-32 ...
```

### TEXTMETRIC Computation from Font Tables

Windows GDI computes the TEXTMETRIC values from the OS/2 table as follows:

```
tmAscent  = round(usWinAscent  * ppem / unitsPerEm)
tmDescent = round(usWinDescent * ppem / unitsPerEm)
tmHeight  = tmAscent + tmDescent
tmInternalLeading = tmHeight - ppem
```

### BMFont Metric Formulas

The values written to the .fnt file are derived from GDI calls, then divided by the supersampling factor:

```
lineHeight = ceil(tmHeight / aa)
base       = ceil(tmAscent / aa)
```

Per-glyph metrics:

```
xAdvance = gmCellIncX / aa                  // outline mode: GetGlyphOutlineW
         = (abcA + abcB + abcC) / aa        // bitmap mode: GetCharABCWidths

xOffset  = abcA / aa                        // left side bearing

yOffset  = (fontAscent - glyphTop) / aa     // distance from line top to glyph top
```

Where `fontAscent` is tmAscent (scaled usWinAscent), and `glyphTop` is the top of the rendered glyph bitmap relative to the baseline.

### ppem from Positive lfHeight

When lfHeight is positive (cell height mode), the actual ppem used for scaling is:

```
ppem = lfHeight * unitsPerEm / (usWinAscent + usWinDescent)
```

This is the fundamental equation. A positive lfHeight of 32 does NOT produce ppem=32. The ppem is smaller, because the cell height exceeds the em square.

---

## 4. FreeType Sizing (How KernSmith Works)

### FT_Set_Char_Size

```c
FT_Error FT_Set_Char_Size(
    FT_Face face,
    FT_F26Dot6 char_width,   // 0 means same as height
    FT_F26Dot6 char_height,  // in 26.6 fractional points
    FT_UInt h_resolution,    // horizontal DPI
    FT_UInt v_resolution     // vertical DPI
);
```

The resulting pixel size is:

```
pixel_size = point_size * dpi / 72
```

With size=32 and dpi=72: ppem = 32. This sets the **em square** to 32 pixels. This is equivalent to a **negative** lfHeight in GDI terms --- it matches character height, not cell height.

### FT_Set_Pixel_Sizes

```c
FT_Error FT_Set_Pixel_Sizes(
    FT_Face face,
    FT_UInt pixel_width,   // 0 means same as height
    FT_UInt pixel_height   // em square height in pixels
);
```

Sets the em square to exactly `pixel_height` pixels. Equivalent to `FT_Set_Char_Size(face, 0, size*64, 72, 72)`.

### 26.6 Fixed-Point Format

FreeType uses 26.6 fixed-point format for sub-pixel precision. The lower 6 bits represent the fractional part (1/64 pixel increments), and the upper 26 bits represent the integer part.

Standard conversions:

```
Round to nearest pixel: (value + 32) >> 6
Floor (positive values): value >> 6
Ceiling:                 (value + 63) >> 6
```

One 26.6 unit = 1/64 pixel = 0.015625 pixels.

To convert a pixel value to 26.6: multiply by 64 (or left-shift by 6).

### FreeType Metric Sources

**Face-level scaled metrics** (available after setting size):

| Field | Source Table | Format | Description |
|-------|-------------|--------|-------------|
| `face->size->metrics.ascender` | hhea | 26.6 | Scaled ascender from hhea table |
| `face->size->metrics.descender` | hhea | 26.6 | Scaled descender from hhea table (negative) |
| `face->size->metrics.height` | hhea | 26.6 | Baseline-to-baseline distance |
| `face->size->metrics.x_ppem` | computed | integer | Horizontal ppem |
| `face->size->metrics.y_ppem` | computed | integer | Vertical ppem |

Important: FreeType's `ascender` and `descender` come from the **hhea** table, not the OS/2 table. This is a source of divergence from Windows GDI, which uses OS/2 usWinAscent/usWinDescent.

**Per-glyph metrics** (available after loading a glyph):

| Field | Format | Description |
|-------|--------|-------------|
| `slot->metrics.horiAdvance` | 26.6 | Horizontal advance width |
| `slot->metrics.horiBearingX` | 26.6 | Left side bearing |
| `slot->metrics.horiBearingY` | 26.6 | Top bearing (baseline to glyph top) |
| `slot->metrics.width` | 26.6 | Glyph bitmap width |
| `slot->metrics.height` | 26.6 | Glyph bitmap height |
| `slot->bitmap_left` | integer pixels | X bearing after hinting/rendering |
| `slot->bitmap_top` | integer pixels | Y bearing after hinting/rendering (positive = up) |
| `slot->advance.x` | 26.6 | Advance width after hinting |

### Hinting Modes and Their Effect on Advance Widths

| Mode | Hinting | Advance Width Behavior |
|------|---------|----------------------|
| `FT_LOAD_TARGET_NORMAL` | Full TrueType bytecode | Grid-fitted to integer pixels |
| `FT_LOAD_TARGET_LIGHT` | Auto-hinter, vertical only | NOT horizontally grid-fitted (fractional advances) |
| `FT_LOAD_TARGET_MONO` | Aggressive for 1-bit rendering | Snapped to integer pixels |
| `FT_LOAD_NO_HINTING` | None | Exact scaled design values (fractional) |

### TrueType Interpreter Versions

FreeType's TrueType bytecode interpreter has two major versions that affect hinting behavior:

| Version | Behavior | Similarity |
|---------|----------|-----------|
| v35 (classic) | Executes all TT hints including horizontal. Hints CAN modify advance widths. | GDI-like behavior |
| v40 (default since FreeType 2.7) | Ignores horizontal stem hints. Prevents advance width modification by hints. | DirectWrite / ClearType-like output |

The v40 interpreter is the default in modern FreeType builds. It produces results closer to DirectWrite than to GDI. This is a secondary source of metric differences compared to BMFont.

---

## 5. The Root Cause: BMFont vs FreeType Size Interpretation

The primary difference between BMFont and KernSmith metrics is that they interpret the fontSize value differently:

- **BMFont** (default): fontSize = **cell height** (includes internal leading)
- **FreeType**: fontSize = **em height** (em square only, excludes internal leading)

Since the cell height always exceeds the em height (for any font where usWinAscent + usWinDescent > unitsPerEm, which is nearly all fonts), FreeType renders glyphs **larger** than BMFont at the same nominal fontSize.

### Concrete Example: Arial at fontSize=32

**Arial font table values:**
- unitsPerEm = 2048
- OS/2 usWinAscent = 1854
- OS/2 usWinDescent = 434
- usWinAscent + usWinDescent = 2288

**BMFont (positive lfHeight = 32, cell height mode):**

```
ppem = 32 * 2048 / (1854 + 434)
     = 32 * 2048 / 2288
     = 65536 / 2288
     ~ 28.64

lineHeight = 32   (matches fontSize by design, since cell height = fontSize)

base = ceil(1854 * 28.64 / 2048)
     = ceil(25.94)
     = 26
```

**KernSmith / FreeType (em square = 32 pixels):**

```
ppem = 32

lineHeight = ceil((1854 + 434) * 32 / 2048)
           = ceil(2288 * 32 / 2048)
           = ceil(35.75)
           = 36

base = ceil(1854 * 32 / 2048)
     = ceil(28.97)
     = 29
```

**Result: KernSmith produces approximately 12% larger metrics at the same fontSize.**

The ratio is exactly `(usWinAscent + usWinDescent) / unitsPerEm = 2288 / 2048 = 1.117`.

### Measured Comparison Data

Full comparison of Arial at fontSize=32, no supersampling, with hinting:

| Metric | BMFont | KernSmith | Delta |
|--------|--------|-----------|-------|
| lineHeight | 32 | 36 | +4 |
| base | 26 | 29 | +3 |
| 'A' xAdvance | 18 | 21 | +3 |
| 'H' xAdvance | 19 | 23 | +4 |
| 'i' xAdvance | 6 | 7 | +1 |
| '@' xAdvance | 27 | 32 | +5 |
| Avg xAdvance delta | --- | --- | +2.66 |

The advance width differences are proportional to the ppem difference (32/28.64 = 1.117), confirming that the root cause is the ppem computation, not a hinting or rounding issue.

---

## 6. The Fix: Matching BMFont's Default Sizing

To match BMFont's default behavior, KernSmith must compute the effective ppem that produces the requested cell height, rather than using the fontSize directly as the ppem.

### Cell Height Mode (BMFont Default)

Compute the effective ppem from the desired cell height:

```
effectivePpem = fontSize * unitsPerEm / (usWinAscent + usWinDescent)
```

Then set FreeType's size using this adjusted ppem:

```c
FT_Set_Char_Size(face, effectivePpem * 64, effectivePpem * 64, 72, 72)
```

This makes the resulting cell height (scaled usWinAscent + scaled usWinDescent) equal to the requested fontSize, matching BMFont's default positive-lfHeight behavior.

The .fnt output records a positive `size` value in the info block.

### Em Height Mode (BMFont "Match Char Height")

When the user enables "Match char height" mode, use the fontSize directly as the ppem:

```c
FT_Set_Char_Size(face, fontSize * 64, fontSize * 64, 72, 72)
```

The .fnt output records a **negative** `size` value in the info block, matching BMFont's convention:

```
info ... size=-32 ...
```

### Deriving BMFont-Compatible Output Metrics

After setting the correct ppem, compute the .fnt output values using OS/2 table metrics (not hhea):

```
scaledAscent  = round(usWinAscent  * effectivePpem / unitsPerEm)
scaledDescent = round(usWinDescent * effectivePpem / unitsPerEm)
lineHeight    = scaledAscent + scaledDescent
base          = scaledAscent
```

Per-glyph values come from FreeType after rendering at the adjusted ppem, converted from 26.6 format.

---

## 7. Other Bitmap Font Generators --- How They Handle This

| Tool | Size Meaning | Matches BMFont Default? |
|------|-------------|------------------------|
| BMFont (default) | Cell height (positive lfHeight) | Reference implementation |
| BMFont (match char height) | Em height (negative lfHeight) | Different from default |
| fontbm | Em height (FT_Set_Pixel_Sizes) | No --- only matches BMFont "match char height" mode |
| Hiero (FreeType backend) | Em height (FT_Set_Pixel_Sizes) | No |
| gdx-freetype (libGDX) | Em height (FT_Set_Pixel_Sizes) | No |
| msdf-atlas-gen | px/em (resolution-independent SDF) | Different paradigm entirely |
| KernSmith (before fix) | Em height (FT_Set_Char_Size) | No --- this is the bug |
| KernSmith (after fix) | Cell height by default; em height optional | Yes |

fontbm's README explicitly states: "font-size matches to BMFont size when 'Match char height' option in Font Settings dialog is ticked." This confirms that FreeType-based tools universally default to em-height sizing and do not match BMFont's default cell-height sizing without correction.

No other FreeType-based bitmap font generator in common use performs the cell-height correction. KernSmith will be the first to match BMFont's default sizing out of the box.

---

## 8. Kerning Differences

Kerning values are stored in design units and scaled by ppem, so they are also affected by the ppem discrepancy.

### Comparison Results (Arial, fontSize=32, before ppem fix)

- **81 of 91** BMFont kerning pairs matched exactly.
- **10 pairs** differed by -1 (KernSmith produced slightly stronger negative kerning at the larger ppem).
- **4 additional pairs** were found by KernSmith but absent from BMFont output. These were semicolon combinations (e.g., `T;`, `V;`), likely extracted from the GPOS table. BMFont reads only the legacy `kern` table, which may not contain these pairs.

### Expected Behavior After ppem Fix

After correcting the ppem to match BMFont's cell-height interpretation:

- Kerning values will be scaled at the same ppem, eliminating the -1 differences caused by rounding at different scales.
- The 4 additional GPOS-sourced pairs will remain, as they represent correct data that BMFont simply does not extract.

---

## 9. Secondary Differences

Even after fixing the ppem computation, some minor differences between BMFont and KernSmith output will remain. These are expected and acceptable.

### Rounding Method

| Tool | Rounding | Example (value=97, aa=4) |
|------|----------|--------------------------|
| BMFont | Integer division (truncation) | 97 / 4 = 24 |
| KernSmith | Round to nearest (26.6 conversion) | (value + 32) >> 6 |

After fixing the ppem issue, remaining 1px differences in individual glyph metrics are most likely caused by different rounding behavior.

### Hinting Engine

| Tool | Hinting Engine | Behavior |
|------|---------------|----------|
| BMFont | Windows GDI native (TrueType bytecode, v35-like) | Horizontal and vertical grid-fitting |
| KernSmith | FreeType TrueType interpreter (v40 default) | Vertical grid-fitting only; horizontal hints suppressed |

Different hinting engines produce different grid-fitting decisions, resulting in different bitmap shapes and bearing values at the same ppem. This is inherent to the choice of rasterizer and cannot be eliminated without using the same hinting engine.

### Glyph Rendering Pipeline

| Tool | Rendering Method |
|------|-----------------|
| BMFont (outline mode) | GetGlyphOutlineW with GGO_GRAY8_BITMAP |
| BMFont (bitmap mode) | GetCharABCWidths for metrics, GDI rendering for bitmaps |
| KernSmith | FT_Load_Glyph + FT_Render_Glyph with FT_RENDER_MODE_NORMAL |

The rasterization pipeline differs at every stage: outline interpretation, scan conversion, and anti-aliasing. Pixel-exact matching between GDI and FreeType is not achievable.

---

## 10. Recommendations

1. **Fix the sizing first.** The ppem correction from Section 6 accounts for the majority of metric differences (~12% at typical font metrics). This is the single highest-impact change for BMFont parity.

2. **Accept minor rounding differences.** After the ppem fix, individual glyph metrics may differ by 1px due to different rounding methods (truncation vs round-to-nearest). This is within acceptable tolerance for bitmap font consumers.

3. **Accept hinting differences.** FreeType's v40 interpreter and GDI's native hinter produce different grid-fitting decisions. Exact pixel-identical output is not achievable without using GDI as the rasterizer. The differences are minor and do not affect text layout correctness.

4. **Document the behavior clearly.** Make clear in user-facing documentation that:
   - KernSmith matches BMFont's default sizing mode (cell height) by default.
   - An "em height" mode is available, equivalent to BMFont's "Match char height" option.
   - Minor per-glyph differences (1px) compared to BMFont are expected due to different rasterization engines.
   - KernSmith may produce additional kerning pairs from GPOS data that BMFont does not extract.

---

## 11. Synthetic Bold

### FreeType

- `FT_GlyphSlot_Embolden(slot)` thickens glyph strokes using strength `ppem/24` (in 26.6 units)
- At size 72 this is 3px — too aggressive, fills letter counters in heavy fonts like Bauhaus 93
- Applied after loading, before rendering
- Only applied when font lacks native bold variant (`style_flags & 0x01 == 0`)
- KernSmith tries loading bold font variant first; falls back to synthetic

### Windows GDI

- `CreateFont()` with `lfWeight = FW_BOLD (700)`
- GDI loads native bold face if available, otherwise synthesizes
- GDI's synthetic bold is lighter and scales differently than FreeType's
- Exact algorithm is undocumented but empirically closer to `ppem/36` strength

### KernSmith Approach

- Uses `FT_Outline_Embolden` with `ppem/36` strength as a GDI approximation (minimum 0.5px)
- Lighter than FreeType's default `ppem/24`, preserves letter counters in heavy fonts
- Metric adjustments after emboldening must match FreeType's internal behavior:
  - `width += 2 * strength`
  - `height += 2 * strength`
  - `horiBearingY += strength`
  - `horiAdvance += strength`
  - `vertAdvance += strength`
  - Do NOT adjust `horiBearingX`

---

## 12. Synthetic Italic

### FreeType

- `FT_GlyphSlot_Oblique(slot)` applies a fixed 12-degree shear matrix after glyph loading
- Shear factor: `tan(12 deg) = 0.2126`
- Transform matrix: `[1.0, 0.2126; 0.0, 1.0]` (in FreeType 16.16 fixed-point: `0x000036B8`)
- Only applied when font lacks native italic flag (`style_flags & 0x02 == 0`)
- Angle is NOT configurable in FreeType

### Windows GDI

- `CreateFont()` with `lfItalic = TRUE` applies 12-degree shear during rasterization
- GDI also uses approximately 12-degree shear for synthetic italic
- Visual differences arise from different hinting engines (GDI ClearType vs FreeType grayscale) and rasterization, not the angle
- Cannot be fixed while using FreeType — would require a pluggable rasterizer (Phase 78)

### Font's Own Italic Angle

- The `post` table contains `italicAngle` field (Fixed 16.16, degrees counter-clockwise)
- Neither FreeType's `FT_GlyphSlot_Oblique` nor GDI's synthetic italic reads this value
- Custom angle possible via `FT_Set_Transform` with a manual shear matrix

---

## 13. Outline Rendering Approaches

### EDT (Euclidean Distance Transform) --- Current KernSmith Default

- Computes distance from each pixel to nearest glyph edge
- Outline alpha = `clamp(outlineWidth - distance + 0.5, 0, 255)`
- Anti-aliased, smooth results
- EDT expands bidirectionally — both outward and into letter counters

### Outline Counter Protection

- BFS flood-fill from image edges through zero-alpha pixels identifies true exterior pixels
- Counter pixels (zero-alpha but unreachable from edges) are excluded from outline rendering
- Applied in OutlineEffect.cs between EDT computation and RGBA output generation
- Fixes the counter-fill problem at large sizes with thick outlines

### FT_Stroker (FreeType Native) --- Implemented but Disabled

- `FT_Stroker_New`, `FT_Stroker_Set` (radius in 26.6 fixed-point)
- `FT_Glyph_StrokeBorder(glyph, stroker, inside=false, destroy=true)` --- strokes OUTER contour only
- Produces geometrically precise outline that never fills counters
- Vector-based: follows actual glyph contour topology
- Currently disabled due to compositing integration issues (Phase 12 Track D)

### Windows GDI

- `GetGlyphOutline` with `GGO_BEZIER` returns vector outline paths
- Path can be widened with GDI+ `GraphicsPath.Widen(Pen)`
- Or use `ExtCreatePen` + `StrokePath` for outline rendering
- Native counter handling --- always correct topology

---

## 14. TTC (TrueType Collection) Handling

### Structure

- A `.ttc` file bundles multiple font faces (e.g., `batang.ttc` contains Batang, BatangChe, Gungsuh, GungsuhChe)
- Each face has a zero-based index (face 0, face 1, etc.)
- Windows registry entries list them as `"Batang & BatangChe & Gungsuh & GungsuhChe (TrueType)"` followed by `batang.ttc`

### FreeType

- `FT_New_Face(library, path, face_index, &face)` --- `face_index` selects the face
- `FT_Open_Face` with memory buffer also accepts face index
- `face->num_faces` reports total faces in the collection

### Windows GDI

- `CreateFont()` with the font name automatically selects the correct face
- No face index needed --- GDI resolves by name

### KernSmith

- `ISystemFontProvider.LoadFont()` returns `FontLoadResult(byte[] Data, int FaceIndex)`
- Registry parser computes face index from the segment position in "&"-separated TTC entries
- Filesystem scanner reads each face via `TtfParser(data, faceIndex)`

---

## 15. Windows Cloud Fonts

- Word and other Office apps can trigger on-demand font downloads
- Cloud fonts are stored in `%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts\{FontName}\`
- These are NOT in the standard Fonts directory or Windows registry
- GDI `CreateFont()` can access cloud fonts transparently
- FreeType-based tools (including KernSmith) cannot find them unless the path is specified manually
- Not worth scanning automatically --- users can point to the file directly
