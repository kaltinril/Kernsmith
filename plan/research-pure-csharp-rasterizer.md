# Research: Building a Pure C# TTF/OTF Font Rasterizer

> Comprehensive research for KernSmith's pure C# font rasterizer with zero external dependencies.
> This document informs ~20 phases of implementation work.

---

## 1. TTF/OTF File Structure -- All Tables

An OpenType font file is a collection of tables, each identified by a 4-byte tag. The file begins with a Table Directory containing the offset, length, and checksum of every table. All data is big-endian.

### 1.1 Required Tables (ALL OpenType fonts)

| Tag | Name | Purpose |
|------|------|---------|
| `cmap` | Character to Glyph Mapping | Maps Unicode code points to glyph indices. Multiple subtable formats (0, 2, 4, 6, 8, 10, 12, 13, 14). Format 4 (BMP) and Format 12 (full Unicode) are most common. |
| `head` | Font Header | Global font metrics: units per em, created/modified dates, bounding box, index-to-loc format, macStyle flags. |
| `hhea` | Horizontal Header | Ascender, descender, line gap, advance width max, number of hMetrics. |
| `hmtx` | Horizontal Metrics | Per-glyph advance widths and left side bearings. |
| `maxp` | Maximum Profile | Number of glyphs, max points/contours/zones/storage/stack depth (TrueType version). |
| `name` | Naming Table | Font family name, style, copyright, version, designer, license strings (platform/encoding/language encoded). |
| `OS/2` | OS/2 and Windows Metrics | Weight class, width class, embedding flags, Panose, Unicode/codepage ranges, metrics (typo ascender/descender, win ascent/descent, x-height, cap height), subscript/superscript positions. |
| `post` | PostScript | Glyph names mapping to PostScript names, italic angle, underline position/thickness, isFixedPitch. |

### 1.2 TrueType Outline Tables

| Tag | Purpose |
|------|---------|
| `glyf` | Glyph data -- simple glyphs (quadratic Bezier contours) and composite/compound glyphs (references to other glyphs with transforms). |
| `loca` | Index to location -- maps glyph ID to byte offset in `glyf`. Two formats: short (offset/2 as uint16) and long (offset as uint32), selected by `head.indexToLocFormat`. |
| `cvt ` | Control Value Table -- array of FWord values used by TrueType hinting instructions. |
| `fpgm` | Font Program -- TrueType bytecode executed once when font is loaded. Defines functions used by glyph programs. |
| `prep` | Control Value Program -- TrueType bytecode executed when point size or transform changes. Sets up CVT and state. |
| `gasp` | Grid-fitting and Scan-conversion Procedure -- per-ppem range flags for grid-fitting and anti-aliasing behavior. |

### 1.3 CFF/CFF2 Tables (PostScript Outlines)

| Tag | Purpose |
|------|---------|
| `CFF ` | Compact Font Format 1.0 -- complete standalone font program with cubic Bezier outlines. Contains Header, Name INDEX, Top DICT, String INDEX, Global Subr INDEX, CharStrings INDEX, Private DICT, Local Subr INDEX. Uses a stack-based charstring interpreter (Type 2). |
| `CFF2` | Compact Font Format 2.0 -- designed for variable fonts. Removes data duplicated in other tables. Header, Top DICT, Global Subr INDEX, CharStrings, Font DICT, Private DICT, Local Subr INDEX. Adds `blend` operator for variation support. |
| `VORG` | Vertical Origin -- default vertical origin Y coordinates for CFF/CFF2 glyphs (optional, for vertical writing). |

**CFF CharString Interpreter**: A stack machine where numbers are pushed onto an operand stack and operators consume arguments from the bottom. Key operators: `rmoveto`, `rlineto`, `rrcurveto`, `callsubr`, `callgsubr`, `return`, `endchar`, `hstem`, `vstem`, `hintmask`, `cntrmask`. CFF2 adds `blend` and `vsindex`.

### 1.4 Advanced Typographic Tables

| Tag | Purpose |
|------|---------|
| `GDEF` | Glyph Definition -- classifies glyphs (base, ligature, mark, component), attachment points, ligature caret positions, mark glyph sets. |
| `GPOS` | Glyph Positioning -- pair positioning (kerning), mark-to-base, mark-to-ligature, mark-to-mark, cursive attachment, contextual positioning. Uses Coverage tables and ValueRecords. |
| `GSUB` | Glyph Substitution -- single, multiple, alternate, ligature, contextual, chaining contextual, reverse chaining substitutions. Implements features like `liga`, `ccmp`, `calt`, `smcp`, `frac`, etc. |
| `kern` | Legacy Kerning -- simpler than GPOS, format 0 (ordered pairs) most common. Being replaced by GPOS but still in many fonts. |
| `BASE` | Baseline data -- baseline positions for different scripts for cross-script alignment. |
| `JSTF` | Justification -- rules for text justification adjustments. |
| `MATH` | Math layout -- data for mathematical typesetting (superscript/subscript, fraction, radical metrics). |

### 1.5 Color Font Tables

| Tag | Purpose |
|------|---------|
| `COLR` | Color Table -- **v0**: glyphs as layered colored shapes (glyph ID + palette entry per layer, alpha blend only). **v1**: full paint graph with PaintSolid, PaintLinearGradient, PaintRadialGradient, PaintSweepGradient, PaintGlyph, PaintTransform, PaintComposite, PaintColrGlyph. Supports affine transforms and Porter-Duff blending modes. |
| `CPAL` | Color Palette -- defines one or more color palettes (RGBA entries) referenced by COLR. Supports palette labels and entry labels. |
| `CBDT` | Color Bitmap Data -- PNG or raw bitmap glyph images for color emoji. |
| `CBLC` | Color Bitmap Location -- index/location data for CBDT bitmaps, organized by strike (size). |
| `sbix` | Standard Bitmap Graphics -- Apple's bitmap glyph format (PNG, JPEG, TIFF, etc.) keyed by ppem. |
| `SVG ` | SVG Table -- glyph descriptions as SVG documents. Each glyph/range maps to an SVG document. |

### 1.6 Variable Font Tables

| Tag | Purpose |
|------|---------|
| `fvar` | Font Variations -- defines axes of variation (tag, min, default, max, name), and named instances. Standard axes: wght, wdth, ital, slnt, opsz. |
| `gvar` | Glyph Variations -- per-glyph delta sets for TrueType outlines. Each glyph has tuple variation data specifying how control points shift across axes. Uses IUP (Interpolate Untouched Points) optimization. |
| `avar` | Axis Variations -- remaps user-space axis coordinates to normalized coordinates via piecewise linear segments. Allows non-linear axis behavior. avar v2 adds axis-to-axis mapping. |
| `cvar` | CVT Variations -- variation deltas for the `cvt` table values (TrueType hinting only). |
| `HVAR` | Horizontal Metrics Variations -- deltas for advance widths and LSBs. |
| `MVAR` | Metrics Variations -- deltas for global metrics (ascender, descender, line gap, cap height, etc.). |
| `VVAR` | Vertical Metrics Variations -- deltas for vertical metrics. |
| `STAT` | Style Attributes -- axis value tables for UI presentation, required for variable fonts. |

**Interpolation Algorithm**: User coordinates are normalized via `fvar` min/default/max to [-1, 0, 1], then optionally remapped through `avar` segments. Delta values are scaled by axis scalars and summed across tuple variations. For TrueType outlines, `gvar` deltas are applied to control points; for CFF2, `blend` operators in charstrings perform interpolation inline.

### 1.7 Bitmap Tables

| Tag | Purpose |
|------|---------|
| `EBDT` | Embedded Bitmap Data -- monochrome or grayscale bitmaps. Multiple formats (1-9) for different compression/alignment. |
| `EBLC` | Embedded Bitmap Location -- index tables organized by strike (size), maps glyph IDs to EBDT offsets. |
| `EBSC` | Embedded Bitmap Scaling -- substitution rules to scale bitmaps from one size for use at another. |

### 1.8 Other Tables

| Tag | Purpose |
|------|---------|
| `DSIG` | Digital Signature -- font integrity verification. |
| `hdmx` | Horizontal Device Metrics -- precomputed widths at specific ppem sizes (not used in variable fonts). |
| `LTSH` | Linear Threshold -- ppem values above which glyphs are linearly scaled. |
| `MERG` | Merge -- glyph merging rules. |
| `meta` | Metadata -- key-value metadata (e.g., design languages). |
| `PCLT` | PCL 5 Data -- HP printer compatibility data. |
| `VDMX` | Vertical Device Metrics -- precomputed vertical metrics at specific sizes. |
| `vhea` | Vertical Metrics Header -- vertical layout header (ascent, descent, etc.). |
| `vmtx` | Vertical Metrics -- per-glyph vertical advance and top side bearing. |

### 1.9 Tables We Must Parse (Priority Order)

**Phase 1 (MVP rasterizer)**: `head`, `maxp`, `cmap`, `hhea`, `hmtx`, `loca`, `glyf`, `OS/2`, `name`, `post`
**Phase 2 (CFF support)**: `CFF `, `CFF2`
**Phase 3 (Kerning/Layout)**: `kern`, `GPOS`, `GSUB`, `GDEF`
**Phase 4 (Variable fonts)**: `fvar`, `gvar`, `avar`, `HVAR`, `MVAR`, `STAT`
**Phase 5 (Color fonts)**: `COLR`, `CPAL`, `CBDT`, `CBLC`, `sbix`, `SVG `
**Phase 6 (Hinting)**: `cvt `, `fpgm`, `prep`, `gasp`
**Phase 7 (Bitmap)**: `EBDT`, `EBLC`, `EBSC`

---

## 2. Features We Need to Support

### 2.1 TrueType Quadratic Bezier Outlines

TrueType glyphs use quadratic (2nd-order) Bezier curves. A contour is a sequence of on-curve and off-curve points:
- **On-curve to on-curve**: straight line
- **On-curve, off-curve, on-curve**: quadratic Bezier
- **Consecutive off-curve points**: implicit on-curve point at midpoint between them (TrueType shorthand)

Simple glyphs have a number of contours, each with endpoint indices and a set of (x, y, flag) points. Flags indicate on/off curve and whether coordinates are 1-byte deltas or 2-byte values.

### 2.2 CFF/CFF2 Cubic Bezier Outlines

PostScript-style outlines use cubic (3rd-order) Bezier curves, which can represent more complex curves with fewer control points. CFF charstrings use a Type 2 stack-based interpreter:
- Movement: `rmoveto`, `hmoveto`, `vmoveto`
- Lines: `rlineto`, `hlineto`, `vlineto`
- Curves: `rrcurveto`, `hhcurveto`, `vvcurveto`, `rcurveline`, `rlinecurve`, `hvcurveto`, `vhcurveto`
- Hints: `hstem`, `vstem`, `hintmask`, `cntrmask`
- Subroutines: `callsubr`, `callgsubr`, `return`
- End: `endchar`

For a rasterizer, quadratic Beziers can be trivially elevated to cubic (add control points at 1/3 and 2/3 of the way). The rasterizer itself can work on cubic curves universally, with quadratic as a special case.

### 2.3 Hinting

**TrueType Bytecode Interpreter**: A stack-based virtual machine with ~200 instructions operating on glyph control points. Extremely complex to implement fully -- FreeType's implementation is thousands of lines and tries to match Windows' behavior. Instructions manipulate the grid-fitting of control points (moving them to pixel boundaries).

**CFF Hints**: Stem hints (`hstem`, `vstem`) and hint masks declare zones where vertical/horizontal stems exist. Simpler than TrueType bytecode -- the rasterizer uses these zones to make stem widths consistent rather than executing arbitrary programs.

**Auto-Hinting**: FreeType's auto-hinter analyzes outlines and applies hinting without bytecode. It detects stems, blue zones (baseline, x-height, cap height), and aligns them to the pixel grid. This is the most practical approach for a new rasterizer.

**Recommendation**: Start without hinting (like stb_truetype). Add auto-hinting later as an enhancement. Full TrueType bytecode interpretation is a massive undertaking (estimate: 5,000-10,000 lines of C#) and should be deferred or optional.

### 2.4 Anti-Aliasing Approaches

**Grayscale AA**: Standard approach -- compute fractional pixel coverage (0-255). This is what the rasterizer produces natively.

**LCD Subpixel Rendering**: Exploits RGB subpixel layout to triple horizontal resolution.
- **ClearType-style**: Render at 3x horizontal resolution, then apply FIR filter to reduce color fringing. Patented until recently.
- **Harmony method** (FreeType): Shift glyph outline by -1/3, 0, +1/3 pixel for R, G, B channels respectively. Immune to color fringes. Simpler to implement.

**Supersampling**: Render at Nx resolution and downsample. Simple but slow. Useful as a quality reference.

### 2.5 SDF Generation

**Single-Channel SDF**: For each texel, compute the signed distance to the nearest edge. Positive outside, negative inside. Encode as 0-255 with 128 = edge. At render time, a threshold gives crisp edges at any scale.

**Multi-Channel SDF (MSDF)**: Uses RGB channels to encode distances to different edge segments, preserving sharp corners that single-channel SDF rounds off. The median of RGB channels reconstructs the distance. Algorithm:
1. Decompose outline into edge segments
2. Color edges using heuristic (edgeColoringSimple)
3. For each texel, compute distance to nearest edge of each color
4. Store per-channel distances in RGB

**Key reference**: Chlumsky/msdfgen -- the definitive MSDF generator. Algorithm is well-documented and can be ported to C#.

### 2.6 Synthetic Bold (Outline Expansion / Emboldening)

Two approaches:
1. **Outline-level**: Offset each contour outward by a distance `d`. For straight segments, shift perpendicular. For curves, compute offset curves (approximate, since exact offset of a Bezier is not a Bezier). Inner contours (counter-clockwise) shrink inward. This preserves outline quality and works before rasterization.
2. **Bitmap-level**: Dilate the rasterized bitmap. Simpler but lower quality -- loses curve precision and can fill in counters at small sizes.

**Recommended approach**: Outline-level offset using per-point normal displacement, with subdivision of curves that have high curvature. Fall back to bitmap dilation for embedded bitmaps.

### 2.7 Synthetic Italic (Shear Transform)

Apply an affine shear transform to outline points before rasterization:
```
x' = x + y * tan(angle)
y' = y
```
Typical italic angle: 12-14 degrees (tan ~= 0.21-0.25). This is a simple 2D affine transform applied to every control point. No curve quality loss since the transform is linear.

**Compensation**: Pure shear distorts stroke weight -- verticals appear thinner. Advanced implementations (like Glyphs' "Cursivy") adjust curve handles to maintain visual weight, but this is a refinement.

### 2.8 Variable Font Axis Interpolation

Processing pipeline:
1. Read `fvar` to get axis definitions (tag, min, default, max)
2. Normalize user coordinates: map [min, default, max] to [-1, 0, 1]
3. Apply `avar` remapping (piecewise linear segments) if present
4. For TrueType: read `gvar` tuple variation data, scale deltas by axis scalars, apply to control points. Apply IUP for untouched points.
5. For CFF2: `blend` operators in charstrings interpolate inline during interpretation
6. Apply `HVAR`/`MVAR`/`VVAR` deltas to metrics

### 2.9 Color Fonts

**COLR v0**: Simple layer stack. Each color glyph = ordered list of (glyph ID, palette index). Render each layer bottom-to-top with alpha blending.

**COLR v1**: Full paint graph with recursive paint tables. Must implement a paint tree walker:
- `PaintGlyph`: clip to glyph outline
- `PaintSolid` / `PaintLinearGradient` / `PaintRadialGradient` / `PaintSweepGradient`: fill operations
- `PaintTransform`: affine transform
- `PaintComposite`: Porter-Duff compositing
- `PaintColrGlyph`: reuse another color glyph definition

**SVG**: Parse SVG documents per glyph. Significant complexity -- may require an SVG renderer or delegate to external library.

**Bitmap emoji (CBDT/CBLC, sbix)**: Extract pre-rendered PNG/JPEG images at the closest available size and scale.

### 2.10 Kerning

**Legacy `kern` table**: Format 0 is most common -- ordered list of (left glyph, right glyph, value) pairs. Binary search by packed pair key.

**GPOS pair positioning**: More powerful -- supports class-based kerning (group glyphs into classes, define kerning between classes), device-table adjustments, and contextual kerning. Format 1 (glyph pairs) and Format 2 (class pairs).

### 2.11 OpenType Layout Features

The full GSUB/GPOS engine is essentially a text shaper. Feature processing order matters and varies by script. Common features:
- `ccmp` (glyph composition/decomposition) -- processed first
- `liga` (standard ligatures: fi, fl, etc.)
- `calt` (contextual alternates)
- `kern` (kerning via GPOS)
- `mark`, `mkmk` (mark positioning)

For complex scripts (Arabic, Devanagari, etc.), a full shaper (like HarfBuzz) is needed. For Latin/CJK basic support, implementing `liga`, `kern`, `ccmp`, and `calt` covers most needs.

### 2.12 Font Subsetting

Remove unused glyphs to reduce file size. Steps:
1. Determine required glyph set (from character set + closure over GSUB substitutions)
2. Rewrite `glyf`/`loca` or `CFF`/`CFF2` with only needed glyphs
3. Rebuild `cmap` with new glyph indices
4. Update `hmtx`, `GPOS`, `GSUB`, `GDEF` to match new indices
5. Recompute `maxp`, `head`, checksums
6. Drop unnecessary tables

### 2.13 Composite / Compound Glyphs

Composite glyphs reference other glyphs with transforms. Each component has:
- **Flags**: `ARG_1_AND_2_ARE_WORDS`, `ARGS_ARE_XY_VALUES`, `WE_HAVE_A_SCALE`, `WE_HAVE_AN_X_AND_Y_SCALE`, `WE_HAVE_A_TWO_BY_TWO`, `MORE_COMPONENTS`, `USE_MY_METRICS`, `SCALED_COMPONENT_OFFSET`, `UNSCALED_COMPONENT_OFFSET`
- **Glyph ID**: the component glyph to include
- **Arguments**: either point indices for point-matching or x/y offsets
- **Transform**: optional scale, xy-scale, or full 2x2 matrix

Must recursively resolve components (components can reference other composites). `USE_MY_METRICS` forces the composite to use the component's metrics.

### 2.14 Right-to-Left and Vertical Layout

**RTL**: Requires the layout engine to reverse glyph order for RTL runs and apply mirroring (GSUB `rtlm` feature). The rasterizer itself doesn't care about direction -- it renders individual glyphs.

**Vertical**: Use `vhea`/`vmtx` for vertical metrics. `VORG` provides vertical origins for CFF. Some GSUB features (`vert`, `vrt2`) substitute vertical glyph forms.

---

## 3. Pipeline Architecture

### 3.1 How stb_truetype Structures Its Pipeline

stb_truetype is a single ~5000-line C header file. Its architecture:

```
Font Loading (stbtt_InitFont)
  -> Parse offset table, locate tables by tag
  -> Cache table pointers (glyf, cmap, hhea, hmtx, kern, GPOS)

Glyph Lookup (stbtt_FindGlyphIndex)
  -> cmap format 4/12/13 lookup

Metrics (stbtt_GetGlyphHMetrics, stbtt_GetFontVMetrics)
  -> Read hmtx, hhea, OS/2

Outline Extraction (stbtt_GetGlyphShape)
  -> Parse glyf: decode simple glyph points/flags/contours
  -> Handle composite glyphs recursively
  -> Parse CFF charstrings via stack interpreter
  -> Returns array of move/line/curve/close commands

Rasterization (stbtt__rasterize)
  -> Flatten curves to line segments (adaptive subdivision)
  -> Sort edges by top Y
  -> Scanline processing with signed-area trapezoid coverage
  -> Accumulation buffer + cumulative sum
  -> Output 8-bit alpha bitmap

SDF Generation (stbtt_GetGlyphSDF)
  -> For each texel, compute distance to all edges
  -> Brute force approach (quadratic complexity)
```

**Strengths**: Simple, self-contained, correct output, handles both TTF and OTF.
**Weaknesses**: No hinting, no GSUB/GPOS layout, no variable fonts, no color fonts, brute-force SDF.

### 3.2 How FreeType Structures Its Pipeline

FreeType uses a modular, object-oriented architecture:

```
Library Init (FT_Init_FreeType)
  -> Load modules: drivers, renderers, auto-hinter

Face Loading (FT_New_Face)
  -> Detect format (sfntVersion)
  -> Activate driver: TrueType, CFF, Type1, BDF, PCF, WOFF2
  -> Parse face-level data (name, metrics, cmap)

Glyph Loading (FT_Load_Glyph)
  -> Driver extracts outline/bitmap
  -> Apply hinting (driver-specific or auto-hinter)
  -> Store in FT_GlyphSlot as FT_Outline or FT_Bitmap

Rendering (FT_Render_Glyph)
  -> Select renderer by mode (normal, LCD, mono, SDF)
  -> Smooth renderer: scanline rasterization with 256 gray levels
  -> LCD renderer: 3x horizontal resolution + FIR filter
  -> SDF renderer: signed distance field generation
  -> Output to FT_Bitmap
```

**Module system**: Each font format is a "driver" module. Renderers are separate modules. The auto-hinter is a module. New formats/renderers can be added without changing core code.

**Strengths**: Comprehensive format support, excellent hinting, battle-tested, LCD rendering, SDF support.
**Weaknesses**: C code, complex build, large codebase (~200K lines), native dependency.

### 3.3 How SixLabors.Fonts Works (C#)

Pure managed C#, Apache 2.0 license:

```
FontCollection (load fonts)
  -> Parse sfnt tables
  -> Support TTF, OTF, WOFF, WOFF2, variable fonts, color fonts

TextLayout (measure/lay out text)
  -> Unicode processing (bidi, line breaking)
  -> GSUB substitutions
  -> GPOS positioning
  -> IGlyphRenderer callbacks with outline data

Rendering (via IGlyphRenderer interface)
  -> BeginText -> BeginGlyph -> MoveTo/LineTo/CubicTo -> EndGlyph -> EndText
  -> Consumer implements actual rasterization (e.g., SixLabors.ImageSharp)
```

**Key insight**: SixLabors.Fonts does NOT rasterize. It provides parsed outlines and layout data, then calls an `IGlyphRenderer` interface that the consumer implements. The actual rasterization happens in ImageSharp or other consumers.

**Strengths**: Full managed C#, comprehensive OpenType support, GSUB/GPOS, variable fonts, COLR v0/v1, TrueType hinting, bidi, great architecture.
**Weaknesses**: No built-in rasterizer (by design), Apache 2.0 license (copyleft-compatible but not MIT).

### 3.4 How Typography/PixelFarm Works (C#)

```
Typography.OpenFont (font reading)
  -> Parse TTF/OTF/TTC/OTC/WOFF/WOFF2
  -> Access all font tables

Typography.GlyphLayout (text layout)
  -> OpenType layout engine (GSUB/GPOS)
  -> Convert strings to positioned glyph runs

PixelFarm.Typography (rendering bridge)
  -> Connect to PixelFarm 2D rendering
  -> MiniAgg software renderer (Anti-Grain Geometry quality)
  -> LCD subpixel rendering via custom scanline rasterizer
  -> GPU path: tessellate to GlyphRun mesh -> GLES2 shader
```

**Strengths**: Full managed C#, comprehensive font reading, includes actual rasterizer (MiniAgg), LCD subpixel support, GPU rendering path.
**Weaknesses**: Large/sprawling codebase, limited documentation, unclear maintenance status.

### 3.5 Recommended Pipeline for KernSmith

```
                        +--> IRasterizer interface
                        |     (FreeType, StbTrueType, or KernSmith native)
                        |
Font File               |
  |                     |
  v                     v
FontReader ---------> OutlineData ---------> Rasterizer ---------> GlyphBitmap
  |                     |                     |                     |
  | Parse tables        | Bezier contours     | Scanline fill       | 8-bit alpha
  | Extract metrics     | Move/Line/Curve     | Coverage compute    | or RGBA
  | Resolve composites  | commands            | Anti-aliasing       |
  |                     |                     |                     |
  v                     v                     v                     v
FontInfo              Transforms            PostProcessors        AtlasPacker
  |                   (bold, italic,        (SDF, effects,        (pack into
  | Kerning pairs     outline stroke,       channel packing)      texture atlas)
  | Metrics           variable interp)                            |
  | Name info                                                     v
  | GSUB/GPOS                                                   BmFontResult
```

**Key architectural principle**: Each stage should be an interface/abstraction that users can replace or extend. Users should be able to:
- Provide their own font reader (pre-parsed outlines)
- Provide pre-rasterized bitmaps (skip rasterization)
- Plug in custom post-processors
- Use KernSmith's rasterizer standalone (without atlas/BMFont output)

---

## 4. Missing Features in Existing Implementations

### 4.1 What stb_truetype Lacks (vs FreeType)

| Feature | stb_truetype | FreeType |
|---------|-------------|----------|
| TrueType hinting | None | Full bytecode interpreter |
| Auto-hinting | None | Comprehensive auto-hinter |
| CFF/CFF2 support | Basic (contributor-added, some gaps) | Full |
| GPOS pair positioning | Limited/basic | Full |
| GSUB substitutions | None | Via HarfBuzz integration |
| Variable fonts | None | Full fvar/gvar/CFF2 blend |
| Color fonts (COLR) | None | COLR v0/v1 |
| Color bitmaps (CBDT/sbix) | None | Supported |
| SVG glyphs | None | None (delegates to app) |
| LCD subpixel rendering | None (can be done externally) | ClearType + Harmony |
| SDF generation | Basic brute force | Dedicated SDF renderer |
| WOFF/WOFF2 decompression | None | Supported |
| Font collections (TTC/OTC) | Basic | Full |
| Vertical layout | Limited | Full |

### 4.2 What DirectWrite/CoreText Have That FreeType Doesn't

| Feature | DirectWrite/CoreText | FreeType |
|---------|---------------------|----------|
| System font enumeration | Native OS integration | None (application-level) |
| Font fallback | Automatic per-character | None |
| Text shaping | Built-in (USP10/CoreText) | Requires HarfBuzz |
| Color emoji rendering | Native compositor | COLR v0/v1, no SVG |
| Bidirectional text | Built-in (Uniscribe/CoreText) | Requires FriBidi |
| Complex script shaping | Built-in per-script shapers | Requires HarfBuzz |
| GPU-accelerated rendering | DirectWrite: Direct2D/Direct3D | CPU only |
| Font smoothing OS integration | ClearType/CoreText built-in | Standalone |

**Key takeaway**: DirectWrite/CoreText are full text rendering stacks (shaping + layout + rendering), while FreeType is just the glyph-level rasterizer. The gap is filled by HarfBuzz (shaping) + FriBidi (bidi) + Pango/ICU (layout).

### 4.3 Gaps in Pure C# Font Libraries

| Gap | Status in C# Ecosystem |
|-----|----------------------|
| Full TrueType bytecode hinting | Only SixLabors.Fonts has this |
| CFF2 variable font support | SixLabors.Fonts has it; Typography has basic support |
| High-performance rasterizer | No pure C# equivalent to font-rs / fontdue speed |
| MSDF generation | No pure C# implementation |
| LCD subpixel rendering | Typography/PixelFarm has it; nothing else |
| Full GSUB/GPOS shaping | SixLabors.Fonts; no standalone pure C# shaper |
| COLR v1 paint graph | SixLabors.Fonts |
| WOFF2 decompression | SixLabors.Fonts via Brotli |

---

## 5. Rasterization Algorithms

### 5.1 Scanline Fill with Active Edge Tables

The classic approach:
1. Convert all curves to line segments (flatten Beziers)
2. Build an Edge Table (ET) sorted by top Y coordinate
3. For each scanline Y:
   a. Move edges from ET to Active Edge Table (AET) whose top Y <= scanline
   b. Remove edges from AET whose bottom Y < scanline
   c. Sort AET by X intersection
   d. Fill between pairs of X intersections (even-odd or non-zero winding rule)
   e. Update X intersections by adding 1/slope

**For anti-aliasing**: Process N sub-scanlines per pixel row (supersampling), or compute exact edge-pixel intersection areas.

**Complexity**: O(n * h) where n = number of edges, h = height in pixels. The AET is typically small, so sorting is fast.

### 5.2 Signed-Area Trapezoid Method (stb_truetype v2 / font-rs)

The method used by stb_truetype and font-rs (derived from libart/AGG):

1. For each directed edge crossing a scanline band (1 pixel tall):
   - Compute the signed trapezoid area extending from the edge rightward to infinity
   - This contributes to pixel coverage for the column containing the edge
   - For edges spanning multiple pixels horizontally, compute per-pixel contributions

2. Use two accumulators per scanline:
   - **A[x]**: direct area contribution at pixel x
   - **X[x]**: height contribution (for cumulative sum)

3. Final pass (cumulative sum):
   ```
   s = 0
   for each pixel x:
       s += X[x]
       coverage = |A[x] + s|   // absolute value for non-zero winding
       output[x] = clamp(coverage * 255, 0, 255)
   ```

**Advantages**: No curve flattening needed (can work directly with Bezier edges, though most implementations flatten anyway), exact coverage computation, no supersampling needed, fast with simple inner loop.

**Disadvantage**: Slightly incorrect at overlapping shape boundaries (measures area, not coverage). Imperceptible in practice.

### 5.3 Exact Coverage Computation

Compute the exact geometric intersection of each edge with each pixel square. For a line segment crossing a pixel, the covered area is a trapezoid (or triangle at endpoints). Sum contributions from all edges for each pixel.

This is what stb_truetype v2 does in practice. The math for a line segment from (x0,y0) to (x1,y1) crossing pixel column `px`:
- Clip edge to pixel boundaries [px, px+1] horizontally and [py, py+1] vertically
- Compute trapezoid area = (clipped_height) * (average_x - px)
- Accumulate in coverage buffer

### 5.4 SDF Generation Algorithms

**Brute Force**: For each output texel, test distance to every edge segment. O(texels * edges). Simple but slow for complex glyphs.

**Euclidean Distance Transform (EDT)**: 
1. Rasterize glyph to binary bitmap
2. Apply EDT (e.g., Felzenszwalb-Huttenlocher algorithm, O(n) per row/column)
3. Take square root of distance^2 values
4. Negate for inside pixels, positive for outside
Very fast but loses precision at edges.

**Direct Distance Computation (what msdfgen uses)**:
1. For each texel, find the closest edge segment
2. Compute distance to that segment analytically (point-to-line, point-to-quadratic-bezier, point-to-cubic-bezier)
3. Sign is determined by winding number / cross product

**MSDF Algorithm** (Chlumsky):
1. Assign each edge segment a color (R, G, or B) using a heuristic that ensures adjacent segments at corners get different colors
2. For each texel, compute signed distance to nearest edge of each color independently
3. Store three distances in RGB channels
4. At render time: `distance = median(r, g, b)` reconstructs the SDF with sharp corners
5. Threshold/smoothstep on distance gives the final alpha

**MTSDF**: Adds a true SDF in the alpha channel alongside the MSDF in RGB for maximum flexibility.

### 5.5 Bezier Curve Flattening (Adaptive Subdivision)

Convert Bezier curves to line segments for rasterization:

**De Casteljau Subdivision**:
1. Evaluate flatness: measure max deviation of control points from the chord (line between endpoints)
2. If deviation < tolerance (e.g., 0.25 pixels), output the chord as a line segment
3. Otherwise, split at t=0.5 using De Casteljau's algorithm and recurse on both halves

**For quadratic Bezier** (P0, P1, P2):
- Flatness = distance from P1 to midpoint of P0-P2 line
- Split produces: (P0, (P0+P1)/2, (P0+2*P1+P2)/4) and ((P0+2*P1+P2)/4, (P1+P2)/2, P2)

**For cubic Bezier** (P0, P1, P2, P3):
- Flatness = max(distance(P1, P0-P3 line), distance(P2, P0-P3 line))
- Split at t=0.5 via De Casteljau

**Analytic Flattening** (Raph Levien): Compute the number of subdivisions needed analytically rather than recursively. Much faster, avoids stack depth issues, and is parallelizable. Uses the formula: `n = sqrt(3/4 * max_curvature * tolerance)` to determine the number of line segments.

### 5.6 Supersampling

Render at N*M resolution and downsample:
- **Uniform**: Render at 2x2, 4x4, or 8x8. Simple box filter downsample.
- **Rotated grid**: Sample at rotated positions (reduces axis-aligned artifacts). Common pattern: 2x2 RGSS (Rotated Grid Super Sampling).
- **Jittered**: Random sample positions, average results.

Supersampling is 4-64x slower than exact coverage methods but trivial to implement and produces reference-quality output.

### 5.7 LCD Subpixel Rendering

**ClearType approach**:
1. Render glyph at 3x horizontal resolution (treat each RGB subpixel as independent)
2. Apply FIR low-pass filter to reduce color fringing: typical 5-tap filter [1/9, 2/9, 3/9, 2/9, 1/9]
3. Result: 3 coverage values per pixel (R, G, B channels)

**Harmony approach** (simpler, patent-free):
1. Render glyph 3 times, each time with outline shifted by [-1/3, 0, +1/3] pixel horizontally
2. Use each render as R, G, B channel respectively
3. No filtering needed -- immune to color fringes by construction

**Gamma correction**: Coverage values from rasterizer are in linear space but displays are gamma-encoded. Must apply gamma correction: `output = pow(coverage, 1/gamma)` before quantizing.

---

## 6. Synthetic Transforms at the Outline Level

All transforms below operate on the outline (list of contour points and curve commands) BEFORE rasterization, ensuring maximum quality.

### 6.1 Synthetic Bold (Outline Expansion)

**Approach: Perpendicular normal offset**

For each contour point:
1. Compute the outward-facing normal at the point (perpendicular to the tangent direction)
2. Offset the point along the normal by distance `d` (positive = outward for outer contours, inward for inner contours/counters)
3. For control points on curves, offset proportionally

**Computing normals**:
- For line segments: normal is perpendicular to segment direction, rotated 90 degrees
- At corners (where two segments meet): average the normals of both segments, scale by `1/cos(half_angle)` (miter) or cap at a max
- For curve control points: interpolate normals at parameter boundaries

**Handling winding direction**: Outer contours (clockwise in TrueType) expand outward; inner contours (counter-clockwise) shrink. Determine winding by signed area of the contour.

**Approximation quality**: Offset curves of Beziers are not exact Beziers. For small offsets (1-3 units), the approximation is good. For larger offsets, subdivide curves first to improve accuracy.

**Simpler alternative**: Just offset all on-curve points along the average normal. This works surprisingly well for typical boldening amounts (0.5-2.0 pixels).

### 6.2 Synthetic Italic (Shear Transform)

Apply an affine shear to every point:
```csharp
float shearFactor = MathF.Tan(angle * MathF.PI / 180f); // ~12-14 degrees
foreach (var point in contour)
{
    point.X += point.Y * shearFactor;
    // Y unchanged
}
```

This is exact -- no approximation needed. The shear is a linear transform, so Bezier curves remain valid Beziers after transformation.

**Refinement**: To maintain vertical stem width, can apply a slight horizontal scale compensation. For 12-degree shear, verticals appear ~2% thinner.

### 6.3 Synthetic Outline/Stroke

Generate an outlined/stroked version of glyphs:

**Method 1: Two offset curves**
1. Offset the original contour outward by `width/2`
2. Offset inward by `width/2`
3. Reverse the inner offset contour winding
4. Combine as a single filled region

**Method 2: Minkowski sum with circle**
Theoretically exact but computationally expensive. The Minkowski sum of a polygon with a circle of radius r gives the exact dilation. For Bezier curves, this requires computing the offset curve (which is higher-degree).

**Practical approach**:
1. Flatten curves to polyline
2. Offset each segment perpendicular by stroke width
3. Join segments with miter/round/bevel joins
4. Add round/square/butt caps at endpoints (for open paths)
5. Optionally re-fit Bezier curves to the result

### 6.4 Small Caps (Scaling)

1. Determine the font's x-height and cap-height (from `OS/2` table)
2. Scale factor = x-height / cap-height (typically ~0.7)
3. Scale the uppercase glyph outlines uniformly by this factor
4. Optionally compensate stroke weight (scale + slight boldening to maintain stroke weight)

### 6.5 Condensed / Extended (Horizontal Scaling)

Scale only the X coordinates of all contour points:
```csharp
foreach (var point in contour)
{
    point.X *= scaleFactor; // < 1.0 for condensed, > 1.0 for extended
}
// Also scale advance width
advanceWidth *= scaleFactor;
```

Bezier control points scale the same way, maintaining curve validity.

---

## 7. Open Source Reference Implementations

### 7.1 stb_truetype.h (C, Public Domain)

**Repository**: https://github.com/nothings/stb
**License**: Public domain / MIT
**Size**: ~5,000 lines (including extensive comments)

**What it covers**: TrueType + basic CFF parsing, cmap lookup, glyph outline extraction, scanline rasterization with signed-area coverage, SDF generation, font-level kerning.

**Strengths**:
- Incredibly simple and self-contained -- single header file
- Clean, readable code with excellent comments
- Correct anti-aliased output using exact coverage
- Good reference for the core rasterization algorithm
- SDF support built in
- Handles composite glyphs correctly

**Weaknesses**:
- No hinting at all (glyphs look blurry at small sizes)
- CFF support is incomplete/contributor-added
- No GPOS/GSUB layout engine
- No variable font support
- No color font support
- No LCD subpixel rendering
- Brute-force SDF (O(n*m) where n=texels, m=edges)
- No WOFF/WOFF2

**What to learn**: The rasterization algorithm (signed-area trapezoid method), clean API design, how to parse TrueType glyph data efficiently, composite glyph handling.

### 7.2 FreeType (C, FreeType License / GPLv2)

**Repository**: https://github.com/freetype/freetype
**License**: FreeType License (BSD-like) or GPLv2
**Size**: ~200,000 lines

**What it covers**: Everything -- every font format, hinting, auto-hinting, LCD rendering, SDF, variable fonts, color fonts.

**Strengths**:
- The gold standard for font rasterization
- Modular architecture that cleanly separates concerns
- Full TrueType bytecode interpreter
- Excellent auto-hinter
- ClearType and Harmony LCD rendering
- SDF renderer
- Comprehensive format support
- Battle-tested across billions of devices

**Weaknesses**:
- Massive codebase, hard to understand fully
- C code with lots of macros and conventions
- No text shaping (needs HarfBuzz)
- Complex build system

**What to learn**: Module architecture pattern (drivers, renderers, auto-hinter as separate modules), how auto-hinting works (blue zones, stem detection), LCD rendering approaches, how to handle the full complexity of font formats.

### 7.3 Typography / PixelFarm (C#, MIT)

**Repository**: https://github.com/LayoutFarm/Typography
**License**: MIT
**Size**: Large (many sub-projects)

**What it covers**: Font reading (all formats), OpenType layout (GSUB/GPOS), software rasterizer (MiniAgg-based), LCD subpixel rendering, GPU rendering path.

**Strengths**:
- Pure managed C#, MIT license -- closest to what we want
- Has an actual rasterizer (not just layout like SixLabors)
- MiniAgg-based scanline renderer produces good quality output
- LCD subpixel rendering support
- Full OpenType layout engine
- WOFF/WOFF2 support

**Weaknesses**:
- Code quality is inconsistent, hard to navigate
- Large, sprawling codebase with many dependencies between sub-projects
- Limited documentation
- Not actively maintained (last significant activity is sporadic)
- Performance is unclear

**What to learn**: How to structure a C# font engine end-to-end, MiniAgg rasterization approach (Anti-Grain Geometry concepts in C#), how to implement GSUB/GPOS in C#.

### 7.4 SixLabors.Fonts (C#, Apache 2.0)

**Repository**: https://github.com/SixLabors/Fonts
**License**: Apache 2.0
**Size**: Moderate

**What it covers**: Font parsing (TTF, OTF, WOFF, WOFF2, variable, color), full OpenType layout, TrueType hinting, text measurement and layout, IGlyphRenderer interface.

**Strengths**:
- Excellent code quality, well-maintained
- Pure managed C#, no native dependencies
- Comprehensive OpenType support (GSUB/GPOS, COLR v0/v1, variable fonts)
- TrueType hinting implementation
- Clean IGlyphRenderer abstraction
- CFF1 and CFF2 parsing
- Active development

**Weaknesses**:
- No built-in rasterizer (delegates to consumers via IGlyphRenderer)
- Apache 2.0 license (not MIT, though compatible)
- Performance optimized for layout, not necessarily rasterization

**What to learn**: How to parse every OpenType table in C#, IGlyphRenderer interface pattern, TrueType hinting in C#, CFF charstring interpreter implementation, variable font interpolation, COLR v1 paint tree processing.

### 7.5 fontdue (Rust, MIT)

**Repository**: https://github.com/mooman219/fontdue
**License**: MIT
**Size**: Small/moderate

**What it covers**: TrueType/OpenType parsing, rasterization, basic layout.

**Strengths**:
- Claims to be the fastest font renderer in the world
- Pure Rust, no dependencies, no_std compatible
- Fully parses fonts on creation for fast per-glyph operations
- Subpixel anti-aliasing
- No lifetime dependencies -- owns all data
- SIMD optimizations

**Weaknesses**:
- No hinting
- No complex shaping
- No variable fonts
- No color fonts
- Limited layout capabilities

**What to learn**: Performance optimization techniques, how to pre-parse fonts for fast access, SIMD acceleration of rasterization, memory layout for cache efficiency.

### 7.6 font-rs (Rust, Apache 2.0)

**Repository**: https://github.com/raphlinus/font-rs
**License**: Apache 2.0
**Size**: Small (~1000 lines)

**What it covers**: TrueType parsing, high-performance rasterization.

**Strengths**:
- 7.6x faster than FreeType at 42ppem
- Elegant accumulation buffer technique
- Zero-allocation parsing using Rust iterators
- SIMD-accelerated cumulative sum
- Clean, minimal code

**Weaknesses**:
- TrueType only (no CFF)
- No hinting
- Research/demo project, not production-ready
- No layout, no metrics beyond basic

**What to learn**: The accumulation buffer dense-array approach, how to avoid allocations during parsing, SIMD for the integration pass, why dense arrays beat sparse representations.

### 7.7 ab-glyph (Rust, Apache 2.0)

**Repository**: https://github.com/alexheretic/ab-glyph
**License**: Apache 2.0

**What it covers**: Font loading, scaling, positioning, rasterization.

**Strengths**:
- Zero-dependency rasterizer (ab_glyph_rasterizer crate)
- Supports both TTF and OTF
- Clean separation between parsing and rasterizing
- Coverage rasterization for lines, quadratic, and cubic Beziers

**Weaknesses**:
- No hinting
- No shaping
- No variable/color fonts

**What to learn**: Clean separation of rasterizer from parser, zero-dependency rasterizer design that handles both quadratic and cubic curves.

### 7.8 Go Standard Library (Go, BSD)

**Repository**: golang.org/x/image/font (sfnt, opentype packages)
**License**: BSD

**What it covers**: Font parsing (TTF/OTF), vector rasterization, basic text rendering.

**Strengths**:
- Pure Go, no native dependencies
- Clean, idiomatic code
- Separate packages for parsing (sfnt), layout (opentype), and rasterization (vector)
- Good reference for how to structure a managed-language font stack

**Weaknesses**:
- Limited OpenType feature support
- No complex shaping
- Performance is adequate but not optimized

**What to learn**: Package separation pattern (sfnt for parsing, vector for rasterization, opentype for high-level API), clean managed-language font API design.

### 7.9 Slug Library (C/C++, MIT -- recently public domain)

**Repository**: https://github.com/EricLengyel/Slug (reference shaders)
**License**: MIT (algorithm patent disclaimed March 2026)

**What it covers**: GPU-based vector text rendering directly from Bezier curves without texture atlases.

**Strengths**:
- Renders directly from curve data on GPU -- resolution independent
- No atlas needed, no pre-rasterization
- Handles all font sizes and scales perfectly
- Used in AAA games (Activision, Blizzard, id Software, Ubisoft, etc.)
- Dynamic dilation for improved rendering
- Patent recently disclaimed -- algorithm is now public domain

**Weaknesses**:
- GPU-only (requires pixel shader)
- Complex shader math
- Not directly applicable to CPU bitmap generation

**What to learn**: The concept of rendering from curves directly (could inform a GPU rendering mode), the dynamic dilation technique, and the mathematical foundations of curve-based rendering.

### 7.10 FontStashSharp (C#, MIT)

**Repository**: https://github.com/FontStashSharp/FontStashSharp
**License**: MIT

**What it covers**: Runtime font rendering with on-demand atlas packing.

**Strengths**:
- Pure C# wrapper
- On-demand glyph rasterization and atlas management
- Supports MonoGame, FNA, Stride, etc.
- Text effects (blur, stroke, underline)
- Configurable resolution scaling

**Weaknesses**:
- Uses stb_truetype internally for actual rasterization
- Not a standalone rasterizer

**What to learn**: Atlas management patterns (on-demand packing, atlas overflow handling), text effect implementation, multi-framework integration.

---

## Summary: Key Decisions for KernSmith's Pure C# Rasterizer

### Algorithm Choice
Use the **signed-area trapezoid coverage** method (stb_truetype v2 / font-rs style) as the primary rasterizer. It provides exact coverage without supersampling, is fast, and is well-understood.

### Architecture
Follow a **modular pipeline** inspired by FreeType's module system but with C# interfaces:
- `IFontReader` -- parse font bytes into structured data
- `IOutlineDecoder` -- extract glyph outlines (TrueType, CFF, CFF2)
- `IOutlineTransformer` -- apply synthetic transforms (bold, italic, stroke, variable interpolation)
- `IRasterizer` -- convert outlines to bitmaps (scanline, SDF, LCD)
- `IPostProcessor` -- apply post-processing (effects, channel packing)

### Implementation Phases (suggested order)
1. Binary reader + table directory parser
2. Core tables: head, maxp, cmap, hhea, hmtx
3. TrueType outline decoder (glyf/loca, simple + composite glyphs)
4. Bezier flattening (adaptive De Casteljau subdivision)
5. Scanline rasterizer (signed-area trapezoid coverage)
6. Accumulation buffer + final bitmap generation
7. Metrics tables: OS/2, name, post
8. CFF charstring interpreter (Type 2 stack machine)
9. CFF2 extension (blend operator, variable support)
10. Kern table parser
11. GPOS pair positioning (Format 1 and Format 2)
12. Synthetic bold (outline expansion)
13. Synthetic italic (shear transform)
14. SDF generation (direct distance computation)
15. MSDF generation (edge coloring + per-channel distance)
16. Variable font support (fvar, gvar, avar interpolation)
17. COLR v0 color glyphs
18. COLR v1 paint graph
19. LCD subpixel rendering (Harmony method)
20. Auto-hinting (blue zone detection, stem alignment)
21. GSUB/GPOS layout engine (basic features: liga, kern, ccmp, calt)
22. WOFF/WOFF2 decompression
23. Font subsetting

### What NOT to Build (Defer or Skip)
- Full TrueType bytecode interpreter (enormous effort, diminishing returns)
- Full text shaper for complex scripts (use HarfBuzz via interop for Arabic/Devanagari)
- SVG glyph rendering (requires SVG parser/renderer -- separate concern)
- Full bidirectional text algorithm (separate concern from rasterizer)

---

## Sources

### Official Specifications
- [OpenType Specification (Microsoft)](https://learn.microsoft.com/en-us/typography/opentype/spec/otff)
- [Apple TrueType Reference Manual](https://developer.apple.com/fonts/TrueType-Reference-Manual/RM06/Chap6.html)
- [CFF2 Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/cff2)
- [COLR Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/colr)
- [CPAL Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal)
- [fvar Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/fvar)
- [avar Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/avar)
- [GPOS Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/gpos)
- [glyf Table Specification](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf)
- [OpenType Font Variations Overview](https://learn.microsoft.com/en-us/typography/opentype/spec/otvaroverview)
- [Type 2 Charstring Format (Adobe Tech Note #5177)](https://adobe-type-tools.github.io/font-tech-notes/pdfs/5177.Type2.pdf)

### Algorithm References
- [stb_truetype Rasterizer v2 Explanation (nothings.org)](https://nothings.org/gamedev/rasterize/)
- [Inside the Fastest Font Renderer in the World (font-rs, Raph Levien)](https://medium.com/@raphlinus/inside-the-fastest-font-renderer-in-the-world-75ae5270c445)
- [Chlumsky/msdfgen -- MSDF Generator](https://github.com/Chlumsky/msdfgen)
- [Subpixel Rendering (Wikipedia)](https://en.wikipedia.org/wiki/Subpixel_rendering)
- [FreeType Subpixel Rendering](http://freetype.org/freetype2/docs/reference/ft2-lcd_rendering.html)
- [A Primer on Bezier Curves (Pomax)](https://pomax.github.io/bezierinfo/)
- [Fast Cubic Bezier Curve Offsetting (Gasiulis)](https://gasiulis.name/cubic-curve-offsetting/)
- [Precise Offsetting of Quadratic Bezier Curves (Yzerman/Blend2D)](https://blend2d.com/research/precise_offset_curves.pdf)
- [Sub-Pixel Gamma Correct Font Rendering (PureDev)](https://www.puredevsoftware.com/blog/2019/01/22/sub-pixel-gamma-correct-font-rendering/)
- [Slug Library (Terathon)](https://sluglibrary.com/)
- [GPU Font Rendering State of the Art (Lengyel)](https://www.terathon.com/font_rendering_sota_lengyel.pdf)

### Reference Implementations
- [stb_truetype.h (nothings/stb)](https://github.com/nothings/stb/blob/master/stb_truetype.h)
- [FreeType](https://github.com/freetype/freetype) -- [Architecture Overview (DeepWiki)](https://deepwiki.com/freetype/freetype)
- [SixLabors/Fonts](https://github.com/SixLabors/Fonts)
- [LayoutFarm/Typography](https://github.com/LayoutFarm/Typography)
- [fontdue (Rust)](https://github.com/mooman219/fontdue)
- [font-rs (Rust)](https://github.com/raphlinus/font-rs)
- [ab-glyph (Rust)](https://github.com/alexheretic/ab-glyph)
- [golang.org/x/image/font](https://pkg.go.dev/golang.org/x/image/font)
- [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp)
- [robotools/compositor (Python GSUB/GPOS)](https://github.com/robotools/compositor)
- [n8willis/opentype-shaping-documents](https://github.com/n8willis/opentype-shaping-documents)
- [How OpenType Works (Simon Cozens)](https://simoncozens.github.io/fonts-and-layout/opentype.html)
- [FreeType Hinting and Auto-hinting (DeepWiki)](https://deepwiki.com/freetype/freetype/4.3-hinting-and-auto-hinting)
- [Handmade TTF Rasterization Tutorial](https://handmade.network/forums/wip/t/7610-reading_ttf_files_and_rasterizing_them_using_a_handmade_approach,_part_2__rasterization)
- [State of Text Rendering 2024 (Behdad Esfahbod)](https://behdad.org/text2024/)
