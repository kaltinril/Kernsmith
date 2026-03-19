# Other Font Formats Reference

A comprehensive reference for font formats beyond TrueType (.ttf), covering format internals, detection strategies, conversion considerations, and implementation priorities for bmfontier.

---

## Table of Contents

1. [OpenType Font (.OTF)](#1-opentype-font-otf)
2. [Web Open Font Format (.WOFF / .WOFF2)](#2-web-open-font-format-woff--woff2)
3. [Embedded OpenType (.EOT)](#3-embedded-opentype-eot)
4. [PostScript Type 1 Fonts (.PFB/.PFM/.AFM)](#4-postscript-type-1-fonts-pfbpfmafm)
5. [Bitmap Font Formats](#5-bitmap-font-formats)
6. [Variable Fonts](#6-variable-fonts)
7. [Color Fonts](#7-color-fonts)
8. [Font Collections (.TTC/.OTC)](#8-font-collections-ttcotc)
9. [System Font Locations](#9-system-font-locations)
10. [Font Format Detection](#10-font-format-detection)
11. [Format Conversion Considerations](#11-format-conversion-considerations)
12. [Licensing Considerations](#12-licensing-considerations)
13. [Format Priority for bmfontier](#13-format-priority-for-bmfontier)
14. [References](#14-references)

---

## 1. OpenType Font (.OTF)

OpenType is a scalable font format jointly developed by **Microsoft and Adobe**, first announced in 1996. It is an extension of the TrueType format, adding support for PostScript (CFF) outlines and advanced typographic features via the GSUB and GPOS tables.

- **Standard**: [ISO/IEC 14496-22](https://www.iso.org/standard/74461.html) (also known as "Open Font Format")
- **Current version**: 1.9.1
- **File extensions**:
  - `.ttf` -- OpenType with TrueType outlines
  - `.otf` -- OpenType with CFF (PostScript) outlines
  - `.ttc` -- TrueType/OpenType Collection (TrueType outlines)
  - `.otc` -- OpenType Collection (CFF outlines)

OpenType is the dominant modern font format. Virtually all fonts shipped with Windows, macOS, and Linux are OpenType fonts.

**Reference**: <https://learn.microsoft.com/en-us/typography/opentype/spec/>

### CFF vs TrueType Outlines

OpenType fonts contain one of two outline flavors, distinguished by the `sfntVersion` field in the Offset Table header:

| Property | TrueType Outlines | CFF Outlines |
|---|---|---|
| **Curve type** | Quadratic Bezier (on-curve + off-curve control points) | Cubic Bezier (two control points per segment) |
| **Outline storage table** | `glyf` (+ `loca` for indexing) | `CFF ` or `CFF2` |
| **sfntVersion** | `0x00010000` (or `0x74727565` on older Mac fonts) | `OTTO` (`0x4F54544F`) |
| **Hinting approach** | TrueType instructions (bytecode VM, stack-based) | Type 1 hints (stem hints, alignment zones, declarative) |
| **Hinting control** | Very fine-grained, pixel-level control | Higher-level, relies more on rasterizer intelligence |
| **Typical file size** | Slightly larger for complex glyphs (more points needed for quadratics) | Slightly smaller (cubics need fewer points, also uses subroutinization) |
| **Design tool preference** | Historically preferred by Microsoft and screen-optimized fonts | Historically preferred by Adobe and print-oriented designers |

Both outline types share the same OpenType table infrastructure for metrics (`hmtx`, `hhea`), character mapping (`cmap`), naming (`name`), and layout features (`GSUB`, `GPOS`).

### GSUB and GPOS

The **GSUB** (Glyph Substitution) and **GPOS** (Glyph Positioning) tables power OpenType's advanced typographic features. They use a shared architecture of Scripts, Languages, Features, and Lookups.

#### GSUB Lookup Types

| Type | Name | Description |
|------|------|-------------|
| 1 | **Single Substitution** | Replace one glyph with another (e.g., smallcaps) |
| 2 | **Multiple Substitution** | Replace one glyph with a sequence (e.g., decomposition) |
| 3 | **Alternate Substitution** | Replace one glyph with one of several alternatives (user-selectable) |
| 4 | **Ligature Substitution** | Replace a sequence with one glyph (e.g., f+i -> fi) |
| 5 | **Context Substitution** | Apply substitutions based on surrounding glyph context |
| 6 | **Chaining Context Substitution** | Context substitution with backtrack and lookahead sequences |
| 7 | **Extension Substitution** | Wrapper for 32-bit offsets to other lookup types |
| 8 | **Reverse Chaining Context Single** | Single substitution applied in reverse order (for Nastaliq) |

#### GPOS Lookup Types

| Type | Name | Description |
|------|------|-------------|
| 1 | **Single Adjustment** | Adjust position of a single glyph (x/y placement and advance) |
| 2 | **Pair Adjustment** | Adjust positions of a pair of glyphs -- this is **kerning** |
| 3 | **Cursive Attachment** | Connect glyphs along a cursive baseline (Arabic) |
| 4 | **Mark-to-Base Attachment** | Position a combining mark relative to a base glyph |
| 5 | **Mark-to-Ligature Attachment** | Position marks on individual components of a ligature |
| 6 | **Mark-to-Mark Attachment** | Position a mark relative to another mark (stacked diacritics) |
| 7 | **Context Positioning** | Apply positioning based on glyph context |
| 8 | **Chaining Context Positioning** | Context positioning with backtrack and lookahead |
| 9 | **Extension Positioning** | Wrapper for 32-bit offsets to other lookup types |

> **Relevance to BMFont**: Kerning data may be stored in GPOS Lookup Type 2 (Pair Adjustment) rather than or in addition to the legacy `kern` table. bmfontier **must** extract kerning from GPOS when present, as many modern fonts store kerning exclusively there. The legacy `kern` table is increasingly omitted.

### Required Tables

Every valid OpenType font must include these tables:

| Table | Purpose |
|-------|---------|
| `cmap` | Character-to-glyph mapping. Maps Unicode code points to glyph IDs. |
| `head` | Font header. Contains global metrics: unitsPerEm, created/modified dates, flags, macStyle, indexToLocFormat. |
| `hhea` | Horizontal header. Contains ascender, descender, lineGap, numberOfHMetrics, caretSlope. |
| `hmtx` | Horizontal metrics. Per-glyph advance widths and left side bearings. |
| `maxp` | Maximum profile. Number of glyphs and (for TrueType) memory limits for the rasterizer. |
| `name` | Naming table. Font family, subfamily, copyright, license, designer, description, etc. in multiple languages. |
| `OS/2` | OS/2 and Windows metrics. Panose classification, Unicode/codepage ranges, fsType embedding flags, sTypoAscender/Descender/LineGap, usWinAscent/Descent, xAvgCharWidth. |
| `post` | PostScript name mapping. Maps glyph IDs to PostScript glyph names; also contains isFixedPitch and underline metrics. |

Additionally, TrueType-outline fonts require `glyf` and `loca`; CFF-outline fonts require `CFF ` (or `CFF2` for variable fonts).

### Other Notable Tables

| Table | Purpose |
|-------|---------|
| `BASE` | Baseline data for different scripts (e.g., Latin vs Tibetan vs CJK baselines) |
| `GDEF` | Glyph Definition -- classifies glyphs as base, ligature, mark, or component; provides mark attachment classes and ligature caret positions |
| `JSTF` | Justification data for Arabic/CJK text layout |
| `MATH` | Mathematical typesetting constants and glyph construction (used by TeX-like renderers) |
| `kern` | Legacy kerning table (pair-based, simpler than GPOS Type 2 but limited to simple pairs) |
| `DSIG` | Digital signature (deprecated since OpenType 1.8.3; font signing is no longer recommended) |
| `meta` | Font metadata (design languages, supported languages as BCP 47 tags) |

**Reference**: <https://learn.microsoft.com/en-us/typography/opentype/spec/>

---

## 2. Web Open Font Format (.WOFF / .WOFF2)

WOFF is a compressed wrapper around OpenType/TrueType fonts, designed for efficient web delivery. The underlying font data is semantically identical to the source sfnt font -- WOFF is purely a transport encoding.

### WOFF 1.0

- **W3C Recommendation**: December 2012
- **Magic number**: `0x774F4646` (ASCII `wOFF`)
- **Compression**: Each table individually compressed with **zlib** (DEFLATE)
- **Header size**: 44 bytes
- **Optional blocks**:
  - Extended metadata (XML, zlib-compressed)
  - Private data block (arbitrary vendor data)
- **Round-trip**: Lossless conversion to/from sfnt. Decompressing a WOFF file produces a byte-identical sfnt (with possible table reordering).
- **MIME type**: `font/woff`

**Detection example**:
```
First 4 bytes: 77 4F 46 46 -> WOFF 1.0
```

**WOFF 1.0 Header Structure**:
```
Offset  Size  Field
0       4     signature        (0x774F4646)
4       4     flavor           (sfntVersion of the input font)
8       4     length           (total WOFF file size)
12      2     numTables
14      2     reserved         (must be 0)
16      4     totalSfntSize    (uncompressed sfnt size)
20      2     majorVersion     (of the WOFF file)
22      2     minorVersion
24      4     metaOffset
28      4     metaLength       (compressed)
32      4     metaOrigLength   (uncompressed)
36      4     privOffset
40      4     privLength
```

**Reference**: <https://www.w3.org/TR/WOFF/>

### WOFF 2.0

- **W3C Recommendation**: March 2018
- **Magic number**: `0x774F4632` (ASCII `wOF2`)
- **Compression**: **Brotli** (achieves 5-10% better compression than WOFF 1.0 on average; up to 30% for larger fonts)
- **Content-aware preprocessing**: Before Brotli compression, specific tables undergo preprocessing transforms:
  - `glyf` and `loca`: Triplet encoding of point coordinates, split into separate streams
  - `hmtx`: Reconstructed from `glyf` data where possible, eliminating redundancy
  - Other tables may use null transform or custom transforms identified by transform flags
- **Header size**: 48 bytes
- **Supports font collections** (`flavor` = `ttcf`)
- **MIME type**: `font/woff2`

**Detection example**:
```
First 4 bytes: 77 4F 46 32 -> WOFF 2.0
```

**WOFF 2.0 Header Structure**:
```
Offset  Size  Field
0       4     signature        (0x774F4632)
4       4     flavor           (sfntVersion of the input font)
8       4     length           (total WOFF2 file size)
12      2     numTables
16      4     totalSfntSize    (uncompressed size, informational only)
20      4     totalCompressedSize
24      2     majorVersion
26      2     minorVersion
28      4     metaOffset
32      4     metaLength       (compressed)
36      4     metaOrigLength   (uncompressed)
40      4     privOffset
44      4     privLength
```

**Reference**: <https://www.w3.org/TR/WOFF2/>

### Extracting the Underlying Font

#### WOFF 1.0 Decompression

1. Read the WOFF header to obtain `flavor` (the original sfntVersion) and `numTables`.
2. Read each WOFF table directory entry (tag, offset, compLength, origLength, origChecksum).
3. For each table:
   - If `compLength < origLength`, decompress the table data with **zlib inflate**.
   - If `compLength == origLength`, the table is stored uncompressed.
4. Reconstruct the sfnt:
   - Write the Offset Table header with `flavor` as the sfntVersion.
   - Write the table record entries (sorted by tag).
   - Write each decompressed table, padded to 4-byte boundaries.
   - Recalculate the `head.checksumAdjustment` if desired (optional for reading).

#### WOFF 2.0 Decompression

1. Read the WOFF2 header and table directory.
2. Decompress the single compressed data block with **Brotli**.
3. Apply inverse transforms to preprocessed tables:
   - `glyf`/`loca`: Reconstruct from triplet-encoded streams, rebuild `loca` offsets.
   - `hmtx`: Reconstruct from stored deltas and glyf-derived LSBs.
4. Reconstruct the sfnt as with WOFF 1.0.

> **Note**: WOFF2 decompression is significantly more complex than WOFF1 due to the preprocessing transforms. Using a library (e.g., Google's `woff2` reference implementation) is strongly recommended over hand-rolling.

### Relevance to bmfontier

The handling strategy is:

1. **Detect** by reading the first 4 bytes (magic number).
2. **Decompress** to an in-memory sfnt byte stream.
3. **Re-identify** the decompressed data (check `flavor` to determine TrueType vs CFF outlines).
4. **Process** using the same TTF/OTF parsing pipeline.

This means WOFF/WOFF2 support requires only a decompression frontend -- the core font processing logic is shared with TTF/OTF.

---

## 3. Embedded OpenType (.EOT)

Embedded OpenType is **Microsoft's proprietary web font format**, introduced in Internet Explorer 4 (1997). It was never standardized as a W3C Recommendation (only a Member Submission).

- **Magic number detection**: The format does not have a clean magic number at offset 0. Instead, check for `0x504C` at bytes 34-35 (the MagicNumber field in the EOT header), though this is not guaranteed to be at a fixed offset in all versions.
- **Compression**: MicroType Express (MTX) -- a proprietary compression scheme that applies content-aware transforms similar in spirit to WOFF2's preprocessing, but undocumented and patent-encumbered.
- **URL binding**: EOT files can be restricted to specific domains. The font will not render if the referring URL does not match the allowed root strings embedded in the file.
- **Optional XOR encryption**: Some EOT files use XOR-based obfuscation (not real encryption) to discourage casual extraction.
- **Versions**: EOT header version 0x00020001 (most common) and 0x00020002 (adds root string entries).

**Status**: **OBSOLETE**. EOT was only ever supported by Internet Explorer. It is not supported by any modern browser (Chrome, Firefox, Safari, Edge). Internet Explorer itself reached end of life in June 2022.

**Priority for bmfontier**: **Low**. Implementing MTX decompression is non-trivial, and the format has no remaining user base. If EOT support is ever considered, the simplest approach would be to shell out to an existing conversion tool.

**Reference**: <https://www.w3.org/Submission/EOT/>

---

## 4. PostScript Type 1 Fonts (.PFB/.PFM/.AFM)

PostScript Type 1 is a legacy outline font format developed by **Adobe** in the mid-1980s as part of the PostScript page description language. It dominated professional typography through the 1990s before being superseded by OpenType.

### File Components

A complete Type 1 font installation typically consists of multiple files:

| Extension | Name | Contents |
|-----------|------|----------|
| `.pfb` | Printer Font Binary | Glyph outlines in binary-encoded PostScript (Windows) |
| `.pfa` | Printer Font ASCII | Glyph outlines in ASCII hex-encoded PostScript (Unix) |
| `.pfm` | Printer Font Metrics | Windows-specific metrics (binary, includes kerning pairs) |
| `.afm` | Adobe Font Metrics | Cross-platform metrics in human-readable ASCII (includes kerning, character widths, bounding boxes) |
| `.inf` | Font Information | Installation metadata (font name, style, encoding) |

### Key Characteristics

- **Curve type**: Cubic Bezier (same mathematical basis as CFF/OpenType-CFF)
- **Glyph limit**: **256 glyphs per font** (limited by the single-byte encoding model). Extended character sets required "expert" companion fonts.
- **Hinting**: Type 1 hints (stem hints, alignment zones) -- the precursor to CFF hinting.
- **Encryption**: Outlines are encrypted with a known key (eexec encryption, charstring encryption). This was DRM-by-obscurity; the keys have been public since the early 1990s.
- **Encoding**: Originally limited to custom 256-character encodings; no native Unicode support.

### Comparison with TrueType

| Property | PostScript Type 1 | TrueType |
|----------|-------------------|----------|
| **Curves** | Cubic Bezier | Quadratic Bezier |
| **Hinting** | Declarative (stem hints) | Imperative (bytecode VM) |
| **Glyph limit** | 256 per font | 65,535 |
| **Encoding** | Custom 8-bit encodings | Unicode via `cmap` table |
| **File structure** | Multiple files (.pfb + .pfm or .afm) | Single file |
| **Metric source** | .afm or .pfm file | Built into font file (hmtx, hhea) |

**Status**: **DEPRECATED**. Adobe officially ended Type 1 support in January 2023 across all its products. Major operating systems no longer include Type 1 rasterizers by default.

**Priority for bmfontier**: **Low**. Type 1 fonts are increasingly rare in active use. If support is added, it would involve parsing the .pfb/.pfa for outlines and the .afm for metrics. FreeType can load Type 1 fonts transparently, so if bmfontier uses FreeType or a similar library as its rasterization backend, Type 1 support may come "for free."

---

## 5. Bitmap Font Formats

These are existing bitmap font formats -- formats that store pre-rendered pixel data rather than scalable outlines. They are documented here for reference, but they are **NOT** input formats for bmfontier. bmfontier's purpose is to rasterize *outline* fonts into BMFont-format bitmap atlases.

### BDF (Bitmap Distribution Format)

- **Developer**: Adobe
- **Purpose**: Standard bitmap font format for the X Window System
- **Format**: Plain text, human-readable
- **Structure**: Header with global properties, followed by per-glyph bitmap data in hexadecimal

**Example** (glyph 'A' in a simple 8x16 font):
```
STARTCHAR A
ENCODING 65
SWIDTH 500 0
DWIDTH 8 0
BBX 8 16 0 -2
BITMAP
18
24
42
42
7E
42
42
00
ENDCHAR
```

Each hex line represents one row of pixels. `18` = `00011000` in binary, meaning pixels 4 and 5 are set. `7E` = `01111110`, drawing the crossbar of the 'A'.

BDF files begin with the line `STARTFONT 2.1` and are straightforward to parse.

### PCF (Portable Compiled Format)

- **Developer**: X Consortium
- **Purpose**: Compiled binary form of BDF for faster loading by X servers
- **Format**: Binary, table-based (tables for metrics, bitmaps, encodings, properties, accelerators)
- **Relationship to BDF**: Generated from BDF using `bdftopcf`. Contains the same data in a more efficient representation.
- **Usage**: Still found on Unix/Linux systems in `/usr/share/fonts/X11/misc/`, often gzip-compressed (`.pcf.gz`).

### FNT (Windows Bitmap Font)

- **Developer**: Microsoft
- **Versions**: 1.0, 2.0, 3.0
- **Purpose**: Microsoft's original bitmap font format for Windows (pre-TrueType era)
- **Format**: Binary, with a fixed-size header followed by a character table and bitmap data
- **Container**: Multiple FNT resources are bundled into `.fon` files (NE or PE executable format)
- **Status**: **Obsolete**. Replaced by TrueType in Windows 3.1 (1992). Still partially supported for legacy compatibility, but no new FNT fonts are being created.

### PSF (PC Screen Font)

- **Developer**: Linux kernel developers
- **Purpose**: Console font format for the Linux framebuffer console
- **Versions**:
  - **PSF1**: Header magic `0x3604`, supports 256 or 512 glyphs, fixed glyph dimensions
  - **PSF2**: Header magic `0x72B54A86`, supports unlimited glyphs, variable dimensions, includes Unicode mapping table
- **Format**: Binary, compact (header + bitmap data + optional Unicode table)
- **Status**: **Still actively used**. Every Linux distribution ships PSF fonts for the text console. They are typically found at `/usr/share/consolefonts/` or `/usr/lib/kbd/consolefonts/`.

### OTB (OpenType Bitmap)

- **Format**: An OpenType font file containing only bitmap data (no outline data)
- **Tables used**:
  - `EBDT`/`EBLC` (Embedded Bitmap Data/Location) -- monochrome or grayscale bitmaps
  - `CBDT`/`CBLC` (Color Bitmap Data/Location) -- color bitmaps (PNG)
- **Usage**: Some Linux distributions package bitmap fonts as OTB for compatibility with applications that only support OpenType. Pango and FreeType support OTB.
- **Identification**: Standard OpenType sfntVersion but with no `glyf`/`CFF` table and with `EBDT`/`EBLC` or `CBDT`/`CBLC` tables present.

---

## 6. Variable Fonts

Variable fonts were introduced in **OpenType 1.8** (September 2016) as a collaboration between Microsoft, Google, Apple, and Adobe. A single variable font file contains a continuous, parameterized design space, allowing smooth interpolation between design extremes along one or more axes.

For example, a single variable font file can contain the full range from Thin to Black weight, and from Condensed to Expanded width, with every intermediate combination accessible.

### Registered Axes

The OpenType specification defines five registered (standard) axes:

| Tag | Name | Description | Typical Range | CSS Property |
|-----|------|-------------|---------------|-------------|
| `wght` | Weight | Stroke thickness (Thin to Black) | 1--1000 (400 = Regular, 700 = Bold) | `font-weight` |
| `wdth` | Width | Overall character width (Condensed to Expanded) | 50--200 (100 = Normal) | `font-stretch` |
| `slnt` | Slant | Oblique angle in degrees | -90 to 90 (0 = upright, negative = clockwise) | `font-style: oblique Xdeg` |
| `ital` | Italic | Italic vs upright (binary toggle, not continuous) | 0 or 1 | `font-style: italic` |
| `opsz` | Optical Size | Adjusts design for different point sizes | Font-specific (e.g., 8--144) | `font-optical-sizing` |

**Custom axes** use tags that start with an uppercase letter (e.g., `GRAD` for Grade, `CASL` for Casual, `CRSV` for Cursive). Any 4-character tag with at least one uppercase letter is valid as a custom axis.

### Named Instances

Variable fonts may define **named instances** -- specific combinations of axis values that correspond to traditional named styles. For example:

- "Bold" = `wght=700`
- "Bold Condensed" = `wght=700, wdth=75`
- "Light Italic" = `wght=300, ital=1`

Named instances allow variable fonts to behave like traditional font families in applications that do not support axis sliders.

### Related Tables

| Table | Purpose |
|-------|---------|
| `fvar` | Font Variations -- defines axes (tag, min, default, max, nameID) and named instances |
| `gvar` | Glyph Variations -- per-glyph TrueType outline deltas for each axis |
| `CFF2` | CFF2 table -- variable CFF outlines (replaces `CFF ` for variable fonts with PostScript outlines) |
| `avar` | Axis Variations -- remaps axis coordinates (e.g., make perceptually linear weight scale) |
| `cvar` | CVT Variations -- variation deltas for the Control Value Table (TrueType hinting) |
| `STAT` | Style Attributes -- defines axis value labels for font chooser UI (e.g., "Bold" for wght=700) |
| `HVAR` | Horizontal Metrics Variations -- deltas for `hmtx` values across the design space |
| `VVAR` | Vertical Metrics Variations -- deltas for `vmtx` values (vertical text) |
| `MVAR` | Metrics Variations -- deltas for global metrics (ascender, descender, etc.) |

### Handling for BMFont Generation

BMFont is a bitmap format -- it represents glyphs at a fixed size and style. A variable font must therefore be **instantiated** at specific axis values before rasterization.

Steps for bmfontier:

1. **Read the `fvar` table** to discover available axes and named instances.
2. **Select an instance**: Either a named instance or arbitrary axis values provided by the user.
3. **Apply variation deltas**: For each glyph, interpolate the outline by applying deltas from `gvar` (TrueType) or `CFF2` (CFF) at the selected axis coordinates. Also apply `HVAR`/`MVAR` deltas to metrics.
4. **Rasterize** the instantiated outlines as normal.

**Axis selection interface**: Expose axis values as a dictionary, e.g.:

```
Dictionary<string, float> axisValues = new Dictionary<string, float>
{
    { "wght", 700f },
    { "wdth", 75f }
};
```

**Enumerate named instances** for user selection:

```
// From fvar table:
Instance 0: "Thin"             -> wght=100
Instance 1: "Regular"          -> wght=400
Instance 2: "Bold"             -> wght=700
Instance 3: "Bold Condensed"   -> wght=700, wdth=75
...
```

**Example of axis specification**:
```
Axes: wght=700, wdth=75  ->  Bold Condensed
```

If no axis values are specified, use the default values from the `fvar` table (the `defaultValue` field for each axis). This typically produces the "Regular" style.

---

## 7. Color Fonts

Color fonts extend OpenType with mechanisms for multi-color glyph rendering. There are four distinct approaches, each stored in different tables. A single font may contain multiple color representations for fallback purposes.

### COLR/CPAL Tables

#### CPAL (Color Palette)

The `CPAL` table stores one or more **color palettes**, each containing an array of color entries in **BGRA** format (Blue, Green, Red, Alpha -- each one byte, little-endian).

- Multiple palettes allow theme variants (e.g., light mode vs dark mode colors).
- Each palette has the same number of entries.
- **Special palette index `0xFFFF`** means "use the current foreground/text color." This allows color glyphs to adapt to the surrounding text color.

#### COLRv0 (Simple Layered Color)

The original COLR table (version 0) uses a simple layered composition model:

- Each color glyph is defined as a **stack of layers**.
- Each layer is a pair: **(glyph ID, palette color index)**.
- Layers are composited bottom-to-top using simple alpha blending.
- The glyph ID in each layer references a standard monochrome outline from the `glyf` or `CFF` table, filled with the specified solid color.

Example: A flag emoji might be composed of 5 layers -- a background rectangle, three colored stripes, and an outline.

#### COLRv1 (Advanced Color)

COLRv1 (introduced in OpenType 1.9) dramatically expands the color model:

- **Paint tables**: A directed acyclic graph (DAG) of Paint operations.
- **Gradients**: Linear, radial, and sweep gradients with multiple color stops.
- **Transforms**: 2D affine transforms (translate, rotate, scale, skew).
- **Compositing**: 30+ Porter-Duff blending/compositing modes.
- **Reusable sub-graphs**: Paint tables can be shared via PaintColrGlyph.
- **Variable**: All coordinates, colors, and transforms can be variable (respond to font variation axes).

COLRv1 is essentially a vector graphics format embedded in the font, comparable to SVG but more constrained and more efficient.

### SVG Table

The `SVG ` table stores **SVG 1.1 documents** for color glyph rendering.

- Each entry maps a glyph ID range to an SVG document.
- SVG documents **may be gzip-compressed** within the table.
- Documents must be valid **UTF-8**.
- Coordinate system is **y-axis down** (matching SVG convention, opposite to typical font coordinate systems).
- **Prohibited elements**: `script`, `text`, `font`, `foreignObject` -- no dynamic content or recursive font references.
- Each SVG document may define multiple glyphs using `<svg>` elements with `id="glyphXX"` attributes.
- CSS styling within the SVG is permitted.

SVG color fonts are used by Mozilla (Firefox emoji) and are well-supported across browsers. They offer the most design flexibility but are the largest and most complex to render.

### CBDT/CBLC (Color Bitmap Data/Location)

- **Approach**: Pre-rendered **PNG bitmaps** at specific pixel sizes.
- **CBLC** (Color Bitmap Location): Index table that maps glyph IDs to bitmap data locations, organized by **strike** (a set of bitmaps at a specific ppem size).
- **CBDT** (Color Bitmap Data): The actual PNG image data.
- **Usage**: Google's **Noto Color Emoji** uses this format. Android's emoji rendering is based on CBDT/CBLC.
- **Limitation**: Only looks correct at the specific sizes for which bitmaps were authored. Scaling produces blurry or pixelated results.

### sbix (Standard Bitmap Graphics)

- **Developer**: Apple
- **Usage**: **Apple Color Emoji** uses this format.
- **Approach**: Bitmap images (PNG, JPEG, or TIFF) organized by **strikes** (ppem size or device resolution).
- Each strike contains one image per glyph.
- The `sbix` table includes a header with strike count, followed by strike headers (ppem, ppi), followed by per-glyph data (graphicType tag + image data).
- Falls back to monochrome outlines at sizes where no strike is available.

### Challenges for BMFont Generation

Color fonts present several challenges for BMFont output:

- **Multi-channel output**: BMFont traditionally stores single-channel (grayscale alpha) or multi-channel (for SDF) atlas textures. Full-color glyphs require RGBA atlas pages.
- **Palette selection**: Which CPAL palette to use? Need user selection or default to palette 0.
- **Resolution dependence**: CBDT/sbix bitmaps are authored at specific sizes. Requesting a different size requires scaling.
- **SVG rendering**: Requires an SVG rasterizer (e.g., Skia, resvg, or a custom implementation).
- **COLRv1 complexity**: The Paint graph is non-trivial to render correctly, especially with gradients and compositing.
- **Foreground color substitution**: `0xFFFF` palette entries need a user-specified foreground color.
- **Fallback**: If color rendering fails or is not implemented, fall back to monochrome outline rendering.

### Recommended Implementation Phases

| Phase | Scope | Effort |
|-------|-------|--------|
| **Phase 1** | Ignore color tables entirely; render monochrome outlines for all glyphs | Minimal (default behavior) |
| **Phase 2** | COLRv0 + CPAL (layered composition) and sbix/CBDT (bitmap extraction) | Moderate |
| **Phase 3** | COLRv1 (full paint graph rendering) and SVG (requires SVG rasterizer) | Significant |

---

## 8. Font Collections (.TTC/.OTC)

A font collection bundles multiple fonts into a single file, allowing them to share common tables (saving disk space when fonts in a family share the same glyph outlines).

- **Tag**: `ttcf` (always `0x74746366`, regardless of whether the contained fonts use TrueType or CFF outlines)
- **File extensions**: `.ttc` (traditionally TrueType), `.otc` (traditionally CFF), though the internal tag is always `ttcf`

### Structure

```
TTC Header
  ttcTag:               'ttcf'
  majorVersion:         1 or 2
  minorVersion:         0
  numFonts:             N
  tableDirectoryOffsets: [offset_0, offset_1, ..., offset_N-1]
  (v2 only: dsigTag, dsigLength, dsigOffset)

Font 0 Table Directory (at offset_0)
  sfntVersion, numTables, ...
  Table Records: [tag, checksum, offset, length] ...

Font 1 Table Directory (at offset_1)
  ...
```

Multiple fonts may point to the same table data (shared tables).

### Typically Shared Tables

Tables that are often shared across fonts in a collection:

- `glyf` / `CFF ` -- glyph outlines (when fonts share the same glyph set)
- `loca` -- glyph location index
- `hmtx` -- horizontal metrics
- `cvt ` -- control value table (TrueType hinting)
- `fpgm` -- font program (TrueType hinting)
- `prep` -- CVT program (TrueType hinting)

### Typically Unique Tables

Tables that usually differ between fonts in a collection:

- `cmap` -- character mapping (may differ if fonts cover different character sets)
- `name` -- naming (different family/subfamily names)
- `OS/2` -- style-specific metrics (weight class, width class, fsType)
- `head` -- font-specific flags and dates
- `hhea` -- font-specific ascender/descender values

### Handling for bmfontier

1. **Detect**: Read first 4 bytes. If `ttcf`, it is a collection.
2. **Parse TTC header**: Read `numFonts` and `tableDirectoryOffsets`.
3. **Enumerate fonts**: For each font index, read its table directory to get the `name` table and extract the font family/subfamily names.
4. **User selection**: Present the list of contained fonts and let the user select which one(s) to process.
5. **Process**: Seek to the selected font's table directory offset and process it as a normal single-font OpenType file.

---

## 9. System Font Locations

### Windows

| Location | Type | Notes |
|----------|------|-------|
| `C:\Windows\Fonts` | System fonts | Shared by all users; requires admin to modify |
| `%LOCALAPPDATA%\Microsoft\Windows\Fonts` | Per-user fonts | Added via Settings > Fonts (Windows 10 1803+); no admin required |

**Registry keys** for font enumeration:

- **System fonts**: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts`
  - Values: "Font Name (TrueType)" = "filename.ttf" (relative to `C:\Windows\Fonts`) or full path
- **Per-user fonts**: `HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts`
  - Values: Same format, paths relative to the per-user font directory

Windows also supports fonts installed via the legacy `Fonts` control panel, drag-and-drop into `C:\Windows\Fonts`, and programmatic installation via `AddFontResource` / `AddFontResourceEx` APIs.

### macOS

| Location | Type | Notes |
|----------|------|-------|
| `/System/Library/Fonts` | Core system fonts | Protected by SIP; required for system operation |
| `/System/Library/Fonts/Supplemental` | Additional system fonts | Also SIP-protected; supplementary fonts shipped with macOS |
| `/Library/Fonts` | System-wide third-party | Available to all users; admin install |
| `~/Library/Fonts` | Per-user fonts | Current user only; no admin required |
| `/Network/Library/Fonts` | Network fonts | Shared via network home directories (rare) |

macOS uses **Core Text** for font management. The `CTFontManager` API can enumerate all available fonts. The `fc-list` command (if Homebrew fontconfig is installed) also works.

### Linux

| Location | Type | Notes |
|----------|------|-------|
| `/usr/share/fonts` | Distribution-provided fonts | Managed by the package manager |
| `/usr/local/share/fonts` | Locally installed system-wide fonts | Manually installed by admin |
| `~/.local/share/fonts` | Per-user fonts (XDG standard) | Recommended user font directory |
| `~/.fonts` | Per-user fonts (legacy) | **Deprecated** in favor of `~/.local/share/fonts`; still supported by fontconfig |

**Font management**: Linux uses **fontconfig** for font discovery, matching, and configuration.

- **Configuration files**: `/etc/fonts/fonts.conf`, `/etc/fonts/conf.d/*.conf`, `~/.config/fontconfig/fonts.conf`
- **Cache**: `fc-cache -fv` rebuilds the font cache after installing new fonts
- **Enumeration**: `fc-list` lists all available fonts; `fc-match` finds the best match for a pattern
- **Programmatic access**: `FcConfigGetFontDirs()`, `FcFontList()`, `FcFontMatch()`

**Reference**: <https://www.freedesktop.org/software/fontconfig/fontconfig-user.html>

---

## 10. Font Format Detection

### Magic Bytes Table

| Bytes (hex) | ASCII | Format | Notes |
|-------------|-------|--------|-------|
| `00 01 00 00` | (none) | TrueType / OpenType (TrueType outlines) | sfntVersion 1.0 |
| `4F 54 54 4F` | `OTTO` | OpenType (CFF outlines) | sfntVersion 'OTTO' |
| `74 74 63 66` | `ttcf` | Font Collection (TTC/OTC) | Contains multiple fonts |
| `77 4F 46 46` | `wOFF` | WOFF 1.0 | Compressed sfnt (zlib) |
| `77 4F 46 32` | `wOF2` | WOFF 2.0 | Compressed sfnt (Brotli) |
| `80 01` | (none) | PostScript Type 1 (PFB) | Binary PostScript; first segment header |
| `25 21` | `%!` | PostScript Type 1 (PFA) | ASCII PostScript (starts with `%!PS-AdobeFont`) |
| `53 54 41 52 54 46 4F 4E 54` | `STARTFONT` | BDF | Bitmap Distribution Format |

### Detection Pseudocode

```
function DetectFontFormat(byte[] data):
    if data.Length < 4:
        return Unknown

    // Read first 4 bytes as big-endian uint32
    uint32 magic = ReadUInt32BE(data, 0)

    switch magic:
        case 0x00010000:
            return TrueType_OpenType    // TrueType outlines
        case 0x4F54544F:               // 'OTTO'
            return OpenType_CFF         // CFF outlines
        case 0x74746366:               // 'ttcf'
            return FontCollection
        case 0x774F4646:               // 'wOFF'
            return WOFF1               // -> decompress -> re-identify
        case 0x774F4632:               // 'wOF2'
            return WOFF2               // -> decompress -> re-identify

    // Check 2-byte signatures
    if data[0] == 0x80 && data[1] == 0x01:
        return Type1_PFB

    if data[0] == 0x25 && data[1] == 0x21:    // '%!'
        return Type1_PFA

    // Check text-based formats
    if StartsWith(data, "STARTFONT"):
        return BDF

    return Unknown
```

For WOFF1 and WOFF2, after decompression, re-run detection on the decompressed data to determine the underlying font type (TrueType or CFF).

---

## 11. Format Conversion Considerations

### Outline-to-Bitmap Challenges

Converting scalable outlines to fixed-size bitmap glyphs involves several considerations:

- **Hinting and grid-fitting**: TrueType instructions and CFF hints align outlines to the pixel grid. This process is **size-dependent** -- the same glyph looks different at 12px vs 48px, not just scaled. Hinting dramatically improves legibility at small sizes but is irrelevant at large sizes.
- **Anti-aliasing modes**:
  - **Grayscale**: 256-level alpha channel. Each pixel gets an opacity value based on outline coverage. **This is the standard for BMFont output.**
  - **Subpixel (ClearType/LCD)**: Treats each RGB sub-pixel as an independent sample, tripling horizontal resolution. **Not suitable for BMFont** because it produces color-dependent fringes and is display-technology-specific.
  - **Monochrome (1-bit)**: Each pixel is fully on or fully off. Used for very small sizes or specialized applications.
- **Size-dependent rendering**: At small sizes (8-16px), hinting dramatically affects the output. A font that looks excellent at 48px may look poor at 12px if hinting is weak. Conversely, heavily hinted fonts (e.g., core Windows fonts) look extremely sharp at small sizes.
- **Unicode coverage**: Fonts vary enormously in coverage -- from a few hundred Latin glyphs to 50,000+ CJK characters. Large character sets require multiple atlas pages and careful bin packing.
- **Performance considerations**:
  - **Parallel rasterization**: Glyph rendering is embarrassingly parallel. Each glyph can be rasterized independently.
  - **Bin packing heuristics**: Arranging variable-sized glyph rectangles into fixed-size atlas pages is a 2D bin packing problem (NP-hard). Common heuristics:
    - **MaxRects**: Maintains a list of free rectangles; selects best fit. Good balance of speed and density.
    - **Shelf packing**: Arranges glyphs in horizontal rows (shelves). Simpler but wastes more space.
    - **Skyline**: Tracks the top edge of placed rectangles. Good for similar-height items.

### Metrics Mapping (OpenType to BMFont)

| OpenType Source | BMFont Field | Notes |
|----------------|--------------|-------|
| `hhea.ascender` or `OS/2.sTypoAscender` | `base` | Baseline position from top of line |
| `hmtx[glyphID].advanceWidth` | `xadvance` | Horizontal advance after rendering the glyph |
| Left side bearing (`hmtx[glyphID].lsb`) | `xoffset` | Horizontal offset from cursor to glyph image |
| Top side bearing (derived from ascender - glyph yMax) | `yoffset` | Vertical offset from top of line to glyph image |
| `hhea.ascender - hhea.descender + hhea.lineGap` or `OS/2.sTypoAscender - OS/2.sTypoDescender + OS/2.sTypoLineGap` | `lineHeight` | Total line height |
| `kern` table or GPOS Lookup Type 2 | `kerning` pairs | Horizontal adjustment between specific glyph pairs |

Note: All OpenType metrics are in **font design units** (typically 1000 or 2048 units per em). They must be scaled to pixel values: `pixelValue = designUnits * fontSize / unitsPerEm`.

### Kerning Extraction

Kerning data may exist in two places in an OpenType font:

1. **Legacy `kern` table**: Simple pair-based kerning. Format 0 subtables contain a flat list of (left glyph, right glyph, value) triples. Easy to parse but increasingly omitted from modern fonts.

2. **GPOS table, Lookup Type 2 (Pair Adjustment)**: The modern kerning mechanism.
   - **Format 1**: Individual pair records (similar to `kern` table format 0).
   - **Format 2**: Class-based kerning -- glyphs are grouped into classes, and kerning values are defined between classes. More compact for large fonts.

**Important rules**:

- **GPOS takes precedence** if both `kern` and GPOS kerning exist. Some fonts include both for backward compatibility, but GPOS is authoritative.
- Only extract **simple pair adjustments** (GPOS Lookup Type 2). Contextual kerning (Types 7/8) is too complex for BMFont's simple pair model.
- From GPOS Type 2, only the **XAdvance** component of the ValueRecord is relevant for BMFont kerning (horizontal advance adjustment). XPlacement, YPlacement, and YAdvance are typically zero for basic kerning.

---

## 12. Licensing Considerations

### OS/2 fsType Field

The `fsType` field in the `OS/2` table specifies font embedding and usage permissions. It is a bitfield, but **bits 0-3 are mutually exclusive** (only one should be set).

#### Embedding Permission Levels (bits 0-3)

| Value | Name | Description |
|-------|------|-------------|
| 0 | **Installable Embedding** | No restrictions. Font may be embedded, permanently installed, and used for editing. |
| 2 | **Restricted License Embedding** | Font must not be modified, embedded, or exchanged. Viewing of pre-embedded fonts is allowed (e.g., in a PDF). |
| 4 | **Preview & Print Embedding** | Font may be embedded in documents for viewing and printing only. No editing of embedded text. |
| 8 | **Editable Embedding** | Font may be embedded and temporarily installed for editing documents. Not for permanent installation. |

#### Additional Flags

| Bit | Mask | Name | Description |
|-----|------|------|-------------|
| 8 | `0x0100` | **No Subsetting** | Font must not be subsetted prior to embedding. The complete font must be embedded. |
| 9 | `0x0200` | **Bitmap Embedding Only** | Only bitmap data (not outlines) may be embedded. Outline embedding is prohibited. |

### Implications for bmfontier

bmfontier converts fonts to bitmap atlases, which involves rasterizing outlines -- an operation that may be restricted by the font's license.

- **Installable (0)**: No restrictions. Process normally.
- **Restricted License (2)**: **Refuse to process** or display a prominent warning. Converting to BMFont could violate the license.
- **Preview & Print (4)**: Processing may be acceptable for preview purposes but distribution of the resulting BMFont files is questionable. Warn the user.
- **Editable (8)**: Converting to BMFont is likely acceptable for application embedding. Inform the user.
- **No Subsetting (bit 8, `0x0100`)**: If the user is exporting only a subset of characters, display a warning that the font license prohibits subsetting.
- **Bitmap Embedding Only (bit 9, `0x0200`)**: Only extract embedded bitmaps (from `EBDT`/`EBLC` or `CBDT`/`CBLC` tables). Do not rasterize outlines.
- Provide a `--force` flag (or equivalent option) that bypasses fsType checks with a clear disclaimer that the user assumes responsibility for licensing compliance.

### Common Open Font Licenses

| License | Conversion to BMFont Allowed? | Notes |
|---------|-------------------------------|-------|
| **SIL Open Font License (OFL)** | Yes | Derivative works (including bitmap conversions) must use a different name for the font. Cannot sell the font standalone. |
| **Apache License 2.0** | Yes | Permissive; include license notice and attribution. |
| **Ubuntu Font License** | Yes, with conditions | Must include copyright notice. Modified versions must use a different name. |
| **Commercial / Proprietary** | Check the EULA | Many commercial licenses prohibit conversion or embedding. Some offer "app embedding" licenses for this purpose. |
| **GPL with Font Exception** | Yes | The Font Exception explicitly permits embedding and bundling without triggering GPL's copyleft for the embedding application. |

> **Note**: `fsType` is informational/honor-system -- it is not DRM. However, violating it may constitute a license breach. bmfontier should surface this information to help users make informed decisions.

---

## 13. Format Priority for bmfontier

Implementation priority, based on format prevalence and user impact:

| Priority | Format(s) | Rationale |
|----------|-----------|-----------|
| **P0** (Must have) | TTF, OTF, TTC/OTC | The most common font formats. Covers 95%+ of fonts users will encounter. |
| **P1** (Should have) | WOFF2, WOFF1, Variable Fonts | WOFF is ubiquitous on the web; variable fonts are the modern standard for font families. |
| **P2** (Nice to have) | COLRv0/CPAL, sbix/CBDT | Basic color font support for emoji and icon fonts. |
| **P3** (Low priority) | COLRv1, SVG table, Type 1 (.pfb/.pfa) | COLRv1 and SVG are complex to render; Type 1 is legacy. |
| **P4** (Minimal priority) | EOT | Obsolete format with no remaining user base. |
| **N/A** (Not applicable) | BDF, PCF, PSF, FNT | Bitmap font inputs are out of scope -- bmfontier converts *outline* fonts to bitmaps. |

---

## 14. References

- **OpenType Specification**: <https://learn.microsoft.com/en-us/typography/opentype/spec/>
- **WOFF 1.0 (W3C Recommendation)**: <https://www.w3.org/TR/WOFF/>
- **WOFF 2.0 (W3C Recommendation)**: <https://www.w3.org/TR/WOFF2/>
- **EOT (W3C Member Submission)**: <https://www.w3.org/Submission/EOT/>
- **FreeType Project**: <https://freetype.org/>
- **MDN @font-face reference**: <https://developer.mozilla.org/en-US/docs/Web/CSS/@font-face>
- **Fontconfig documentation**: <https://www.freedesktop.org/software/fontconfig/fontconfig-user.html>
- **Apple Developer -- Fonts**: <https://developer.apple.com/fonts/>
- **ISO/IEC 14496-22 (Open Font Format)**: <https://www.iso.org/standard/74461.html>
- **Google Fonts Variable Fonts Guide**: <https://fonts.google.com/knowledge/introducing_type/introducing_variable_fonts>
- **Microsoft Typography -- Color Fonts**: <https://learn.microsoft.com/en-us/typography/opentype/spec/colr>
