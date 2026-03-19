# TrueType Font (TTF) Comprehensive Reference

> **Purpose**: This document is a research reference for understanding the TrueType font file format, its internal structures, glyph rendering pipeline, and how to work with TTF files programmatically in C#/.NET. It is intended to support BMFont atlas generation tooling.

---

## Table of Contents

1. [Overview and History](#1-overview-and-history)
2. [TTF File Format Structure](#2-ttf-file-format-structure)
   - [Table Directory](#table-directory)
   - [Data Types](#data-types)
   - [Checksum Calculation](#checksum-calculation)
   - [Font Collections (.TTC)](#font-collections-ttc)
3. [Required Tables in Detail](#3-required-tables-in-detail)
   - [head (Font Header)](#head-font-header)
   - [hhea (Horizontal Header)](#hhea-horizontal-header)
   - [hmtx (Horizontal Metrics)](#hmtx-horizontal-metrics)
   - [maxp (Maximum Profile)](#maxp-maximum-profile)
   - [loca (Index to Location)](#loca-index-to-location)
   - [name (Naming Table)](#name-naming-table)
   - [OS/2 Table](#os2-table)
   - [post (PostScript)](#post-postscript)
   - [Optional Tables](#optional-tables)
4. [Font Metrics](#4-font-metrics)
   - [Em Square and FUnits](#em-square-and-funits)
   - [Converting FUnits to Pixels](#converting-funits-to-pixels)
   - [Vertical Metrics](#vertical-metrics)
   - [Horizontal Glyph Metrics](#horizontal-glyph-metrics)
5. [Character Encoding and the cmap Table](#5-character-encoding-and-the-cmap-table)
   - [Structure](#structure)
   - [Platform/Encoding IDs](#platformencoding-ids)
   - [Subtable Formats](#subtable-formats)
   - [Recommended Lookup Priority](#recommended-lookup-priority)
6. [Glyph Data and the glyf Table](#6-glyph-data-and-the-glyf-table)
   - [Glyph Header](#glyph-header)
   - [Simple Glyphs](#simple-glyphs)
   - [Composite Glyphs](#composite-glyphs)
7. [Glyph Rendering and Rasterization](#7-glyph-rendering-and-rasterization)
   - [Rendering Pipeline](#rendering-pipeline)
   - [Hinting](#hinting)
   - [Scan Converter](#scan-converter)
   - [Anti-Aliasing](#anti-aliasing)
   - [ppem Calculation](#ppem-calculation)
8. [Kerning and Advanced Typography](#8-kerning-and-advanced-typography)
   - [Legacy kern Table](#legacy-kern-table)
   - [GPOS Table](#gpos-table)
   - [GSUB Table](#gsub-table)
   - [Common OpenType Features](#common-opentype-features)
9. [TTF vs OTF](#9-ttf-vs-otf)
10. [System Font Access in C#/.NET](#10-system-font-access-in-cnet)
    - [System.Drawing (Windows-only)](#systemdrawing-windows-only-as-of-net-6)
    - [System Font Directories](#system-font-directories)
    - [SkiaSharp (Cross-Platform)](#skiasharp-cross-platform)
    - [Direct File Access](#direct-file-access)
11. [C# Libraries for TTF Parsing](#11-c-libraries-for-ttf-parsing)
    - [SixLabors.Fonts](#sixlaborsfonts)
    - [SharpFont (FreeType wrapper)](#sharpfont-freetype-wrapper)
    - [Other Libraries](#other-libraries)
    - [Comparison for BMFont Generation](#comparison-table-for-bmfont-generation-suitability)
12. [Font Rendering to Bitmap](#12-font-rendering-to-bitmap)
    - [Atlas Generation Steps](#atlas-generation-steps)
13. [Key Metric Mappings (TTF to BMFont)](#13-key-metric-mappings-ttf--bmfont)
14. [Relevant Standards and References](#14-relevant-standards-and-references)

---

## 1. Overview and History

TrueType is a scalable font technology developed jointly by **Apple Computer** and **Microsoft** in the late 1980s. Apple began the project internally under the codename "Bass" (later "Royal") as an alternative to Adobe's Type 1 PostScript fonts, which dominated the desktop publishing market and carried expensive licensing fees. Microsoft joined the effort to secure a royalty-free scalable font technology for Windows.

**Key milestones:**

- **1989** -- Apple announces TrueType at the Apple Worldwide Developers Conference.
- **1990** -- TrueType ships with Mac System 7.
- **1991** -- Microsoft ships TrueType with Windows 3.1, bundling core fonts (Arial, Times New Roman, Courier New).
- **1994** -- Microsoft and Adobe begin collaborating on what would become OpenType.
- **1996** -- OpenType 1.0 specification published, extending the TrueType `sfnt` container to also support CFF (Compact Font Format) outlines.
- **2005** -- OpenType adopted as ISO standard (ISO/IEC 14496-22).

### Core Characteristics

| Characteristic | Description |
|---|---|
| **Outline representation** | Quadratic Bezier splines (second-degree curves with on-curve and off-curve control points) |
| **Container format** | `sfnt` -- a table-based binary container where each table stores a specific category of font data |
| **Byte order** | Big-endian (network byte order) throughout the entire file |
| **Coordinate system** | Integer coordinates in "font units" (FUnits), defined relative to an **em square** whose size is set by `unitsPerEm` in the `head` table |
| **Hinting** | Stack-based bytecode instruction set executed by a virtual machine; provides precise control over grid-fitting at small sizes |
| **File extension** | `.ttf` (single font), `.ttc` (font collection) |

### Relationship to OpenType

**OpenType** is the direct successor to TrueType, developed jointly by Microsoft and Adobe. OpenType retains the `sfnt` container and all TrueType tables, but adds:

- Support for **CFF/CFF2 outlines** (cubic Bezier splines) as an alternative to TrueType `glyf` outlines.
- **Advanced typographic layout** tables (`GPOS`, `GSUB`, `GDEF`, `BASE`, `JSTF`, `MATH`) enabling complex script shaping, ligatures, stylistic alternates, and more.
- **Variable font** support (OpenType 1.8+) via the `fvar`, `gvar`, `STAT`, and related tables.
- **Color font** support (COLR/CPAL, SVG, CBDT/CBLC, sbix).

A `.ttf` file with only TrueType outlines is technically a valid OpenType font. The distinction matters primarily when CFF outlines or advanced layout tables are present. Files with CFF outlines typically use the `.otf` extension.

---

## 2. TTF File Format Structure

A TrueType font file is organized as a flat collection of binary tables, preceded by a directory that provides the offset and length of each table. There is no nested or hierarchical structure -- every table is accessed via a direct offset from the beginning of the file.

### Table Directory

The file begins with an **Offset Subtable** (also called the Table Directory Header), followed by an array of **Table Records**.

#### Offset Subtable (12 bytes)

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `uint32` | `sfntVersion` | `0x00010000` for TrueType outlines; `0x4F54544F` (`OTTO`) for CFF outlines |
| 4 | `uint16` | `numTables` | Number of tables in the font |
| 6 | `uint16` | `searchRange` | `(maximum power of 2 <= numTables) * 16` |
| 8 | `uint16` | `entrySelector` | `log2(maximum power of 2 <= numTables)` |
| 10 | `uint16` | `rangeShift` | `numTables * 16 - searchRange` |

The `searchRange`, `entrySelector`, and `rangeShift` fields are optimization hints for binary search over the table records. They must be set correctly for the font to pass validation, but modern parsers typically ignore them and iterate or binary-search the table records directly.

#### Table Record (16 bytes each)

Immediately following the offset subtable are `numTables` table records, sorted alphabetically by tag:

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `Tag` (4 bytes) | `tableTag` | Four-character ASCII identifier (e.g., `head`, `glyf`, `cmap`) |
| 4 | `uint32` | `checksum` | Checksum of the table data |
| 8 | `uint32` | `offset` | Byte offset from the beginning of the file to the table data |
| 12 | `uint32` | `length` | Length of the table data in bytes |

**Example**: A font with 17 tables would have:
- 12 bytes for the offset subtable
- 17 x 16 = 272 bytes for the table records
- Total directory size: 284 bytes
- Table data begins at or after byte offset 284 (aligned to 4-byte boundaries)

Tables are padded to 4-byte boundaries with zero bytes, though the `length` field records the actual unpadded length.

### Data Types

All multi-byte values in TrueType are stored in **big-endian** byte order.

| Type | Size (bytes) | Description | Range / Notes |
|------|-------------|-------------|---------------|
| `uint8` | 1 | Unsigned 8-bit integer | 0 to 255 |
| `int8` | 1 | Signed 8-bit integer | -128 to 127 |
| `uint16` | 2 | Unsigned 16-bit integer | 0 to 65535 |
| `int16` | 2 | Signed 16-bit integer | -32768 to 32767 |
| `uint24` | 3 | Unsigned 24-bit integer | 0 to 16777215 (used in some coverage tables) |
| `uint32` | 4 | Unsigned 32-bit integer | 0 to 4294967295 |
| `int32` | 4 | Signed 32-bit integer | -2147483648 to 2147483647 |
| `Fixed` | 4 | 16.16 fixed-point number | High 16 bits = integer part, low 16 bits = fractional part. E.g., `0x00020000` = 2.0 |
| `FWORD` | 2 | Signed 16-bit integer in FUnits | Same as `int16`, but semantically represents a font design unit |
| `UFWORD` | 2 | Unsigned 16-bit integer in FUnits | Same as `uint16`, semantically a font design unit |
| `F2DOT14` | 2 | 2.14 fixed-point number | High 2 bits = signed integer, low 14 bits = fraction. Range: -2.0 to +1.99994 |
| `LONGDATETIME` | 8 | Signed 64-bit integer | Seconds since 12:00 midnight, January 1, 1904 (Mac epoch) |
| `Tag` | 4 | Four-byte ASCII identifier | Each byte is in the range 0x20-0x7E (printable ASCII) |
| `Offset16` | 2 | Unsigned 16-bit offset | Relative to some base within the table |
| `Offset32` | 4 | Unsigned 32-bit offset | Relative to some base within the table |

### Checksum Calculation

Every table has a checksum stored in its table record. The checksum is calculated by treating the table data as an array of `uint32` values and summing them (with overflow wrapping):

```
uint32 CalcTableChecksum(byte[] table, uint length)
{
    uint sum = 0;
    uint nLongs = (length + 3) / 4;  // round up to include partial last uint32
    for (uint i = 0; i < nLongs; i++)
        sum += ReadUInt32BigEndian(table, i * 4);  // pad trailing bytes with 0
    return sum;
}
```

**Special case -- the `head` table**: The `head` table contains a `checksumAdjustment` field at byte offset 8. To calculate the table's checksum, this field is temporarily set to 0. After all individual table checksums are computed, the entire file checksum is calculated the same way, and then:

```
head.checksumAdjustment = 0xB1B0AFBA - entireFileChecksum
```

This allows validators to check the whole-file integrity: summing all `uint32` values of the entire file should yield `0xB1B0AFBA`.

### Font Collections (.TTC)

A **TrueType Collection** (`.ttc`) bundles multiple fonts into a single file, allowing them to share common tables (e.g., `glyf`, `loca`, `cmap`) to reduce disk usage. This is common for font families or CJK fonts with large shared glyph sets.

**TTC Header:**

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `Tag` | `ttcTag` | `ttcf` |
| 4 | `uint16` | `majorVersion` | 1 or 2 |
| 6 | `uint16` | `minorVersion` | 0 |
| 8 | `uint32` | `numFonts` | Number of fonts in the collection |
| 12 | `uint32[numFonts]` | `tableDirectoryOffsets` | Byte offsets to each font's offset subtable |

Each offset in `tableDirectoryOffsets` points to a standard offset subtable + table records structure. Different fonts in the collection may reference the same table data at the same offset, achieving sharing.

Version 2 TTC headers add an optional DSIG (Digital Signature) reference after the offsets array.

---

## 3. Required Tables in Detail

The OpenType specification defines nine tables as **required** for a valid TrueType-outline font:

| Table | Name | Purpose |
|-------|------|---------|
| `cmap` | Character to Glyph Mapping | Maps character codes to glyph indices |
| `glyf` | Glyph Data | Contains glyph outlines |
| `head` | Font Header | Global font metrics and flags |
| `hhea` | Horizontal Header | Horizontal layout metrics |
| `hmtx` | Horizontal Metrics | Per-glyph horizontal metrics |
| `loca` | Index to Location | Maps glyph indices to `glyf` table offsets |
| `maxp` | Maximum Profile | Memory allocation hints and glyph count |
| `name` | Naming | Human-readable font names and metadata |
| `post` | PostScript | PostScript compatibility data |

The `OS/2` table is technically optional in the spec but is **required in practice** for Windows compatibility and is present in virtually all modern fonts.

### head (Font Header)

The `head` table contains global information about the font. It is 54 bytes long.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/head](https://learn.microsoft.com/en-us/typography/opentype/spec/head)

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `uint16` | `majorVersion` | 1 |
| 2 | `uint16` | `minorVersion` | 0 |
| 4 | `Fixed` | `fontRevision` | Set by font designer/manufacturer (e.g., 1.5 = `0x00018000`) |
| 8 | `uint32` | `checksumAdjustment` | See [Checksum Calculation](#checksum-calculation) |
| 12 | `uint32` | `magicNumber` | Must be `0x5F0F3CF5` |
| 16 | `uint16` | `flags` | Bit field (see below) |
| 18 | `uint16` | `unitsPerEm` | Font design units per em. Valid range: 16--16384. Common values: **2048** (TrueType), **1000** (CFF) |
| 20 | `LONGDATETIME` | `created` | Font creation timestamp |
| 28 | `LONGDATETIME` | `modified` | Font modification timestamp |
| 36 | `int16` | `xMin` | Minimum x for all glyph bounding boxes |
| 38 | `int16` | `yMin` | Minimum y for all glyph bounding boxes |
| 40 | `int16` | `xMax` | Maximum x for all glyph bounding boxes |
| 42 | `int16` | `yMax` | Maximum y for all glyph bounding boxes |
| 44 | `uint16` | `macStyle` | Bit 0: Bold, Bit 1: Italic, Bit 2: Underline, Bit 3: Outline, Bit 4: Shadow, Bit 5: Condensed, Bit 6: Extended |
| 46 | `uint16` | `lowestRecPPEM` | Smallest readable size in pixels per em |
| 48 | `int16` | `fontDirectionHint` | Deprecated. Set to 2. |
| 50 | `int16` | `indexToLocFormat` | `0` = short offsets (`Offset16` in `loca`), `1` = long offsets (`Offset32` in `loca`) |
| 52 | `int16` | `glyphDataFormat` | 0 (current format) |

**Key flags (bit field at offset 16):**

| Bit | Meaning |
|-----|---------|
| 0 | Baseline at y=0 |
| 1 | Left sidebearing point at x=0 |
| 2 | Instructions may depend on point size |
| 3 | Force ppem to integer values (no fractional scaling) |
| 4 | Instructions may alter advance width |
| 11 | Font data is "lossless" (compressed with Agfa MicroType Express) |
| 13 | Font optimized for ClearType |

### hhea (Horizontal Header)

The `hhea` table provides metrics used for horizontal text layout. It is 36 bytes long.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/hhea](https://learn.microsoft.com/en-us/typography/opentype/spec/hhea)

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `uint16` | `majorVersion` | 1 |
| 2 | `uint16` | `minorVersion` | 0 |
| 4 | `FWORD` | `ascender` | Typographic ascent (Apple platforms use this for line spacing) |
| 6 | `FWORD` | `descender` | Typographic descent (typically negative) |
| 8 | `FWORD` | `lineGap` | Typographic line gap |
| 10 | `UFWORD` | `advanceWidthMax` | Maximum advance width in the `hmtx` table |
| 12 | `FWORD` | `minLeftSideBearing` | Minimum left sidebearing in `hmtx` |
| 14 | `FWORD` | `minRightSideBearing` | Minimum right sidebearing: `min(aw - lsb - (xMax - xMin))` |
| 16 | `FWORD` | `xMaxExtent` | `max(lsb + (xMax - xMin))` |
| 18 | `int16` | `caretSlopeRise` | Caret slope numerator (1 for vertical caret, 0 for horizontal) |
| 20 | `int16` | `caretSlopeRun` | Caret slope denominator (0 for vertical caret) |
| 22 | `int16` | `caretOffset` | Caret offset for slanted fonts (0 for non-slanted) |
| 24 | `int16` | (reserved) | Set to 0 |
| 26 | `int16` | (reserved) | Set to 0 |
| 28 | `int16` | (reserved) | Set to 0 |
| 30 | `int16` | (reserved) | Set to 0 |
| 32 | `int16` | `metricDataFormat` | 0 (current format) |
| 34 | `uint16` | `numberOfHMetrics` | Number of `LongHorMetric` records in `hmtx` |

For an upright (non-italic) font, `caretSlopeRise` = 1 and `caretSlopeRun` = 0 gives a vertical caret. For an italic font, these values define the slope of the caret (e.g., rise=1, run=1 gives a 45-degree caret).

### hmtx (Horizontal Metrics)

The `hmtx` table contains the horizontal metrics (advance width and left side bearing) for every glyph. Its structure is variable-length.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/hmtx](https://learn.microsoft.com/en-us/typography/opentype/spec/hmtx)

The table has two parts:

1. **LongHorMetric records** -- the first `numberOfHMetrics` glyphs (from `hhea`):

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `advanceWidth` | Advance width in FUnits |
| `int16` | `lsb` | Left side bearing in FUnits |

2. **Left side bearing array** -- the remaining `(numGlyphs - numberOfHMetrics)` glyphs:

| Type | Field | Description |
|------|-------|-------------|
| `int16` | `leftSideBearing` | Left side bearing in FUnits |

Glyphs beyond `numberOfHMetrics` share the **last** `advanceWidth` value from the LongHorMetric records. This optimization is commonly used for monospaced fonts (where all glyphs have the same advance width), setting `numberOfHMetrics` to 1.

**Calculating Right Side Bearing (RSB):**

```
RSB = advanceWidth - (lsb + xMax - xMin)
```

Where `xMin` and `xMax` come from the glyph's bounding box in the `glyf` table.

**Example**: Glyph with `advanceWidth` = 600, `lsb` = 50, `xMin` = 50, `xMax` = 550:
```
glyphWidth = xMax - xMin = 500
RSB = 600 - (50 + 500) = 50
```

### maxp (Maximum Profile)

The `maxp` table specifies memory requirements for the font, used historically by rasterizers for pre-allocation. It also contains the all-important `numGlyphs` field.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/maxp](https://learn.microsoft.com/en-us/typography/opentype/spec/maxp)

For TrueType outlines (version 1.0), the table is 32 bytes:

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `Fixed` | `version` | `0x00010000` (1.0) for TrueType; `0x00005000` (0.5) for CFF |
| 4 | `uint16` | `numGlyphs` | Total number of glyphs in the font |
| 6 | `uint16` | `maxPoints` | Max points in a non-composite glyph |
| 8 | `uint16` | `maxContours` | Max contours in a non-composite glyph |
| 10 | `uint16` | `maxCompositePoints` | Max points in a composite glyph |
| 12 | `uint16` | `maxCompositeContours` | Max contours in a composite glyph |
| 14 | `uint16` | `maxZones` | 1 (no twilight zone) or 2 (twilight zone used) |
| 16 | `uint16` | `maxTwilightPoints` | Max points in twilight zone (Zone 0) |
| 18 | `uint16` | `maxStorage` | Max storage area locations |
| 20 | `uint16` | `maxFunctionDefs` | Max function definitions (FDEF) |
| 22 | `uint16` | `maxInstructionDefs` | Max instruction definitions (IDEF) |
| 24 | `uint16` | `maxStackElements` | Max stack depth |
| 26 | `uint16` | `maxSizeOfInstructions` | Max byte count for glyph instructions |
| 28 | `uint16` | `maxComponentElements` | Max top-level components in composite glyphs |
| 30 | `uint16` | `maxComponentDepth` | Max nesting depth of composite glyphs |

For CFF-based fonts (version 0.5), only `version` and `numGlyphs` are present (6 bytes total).

### loca (Index to Location)

The `loca` table maps glyph indices to byte offsets within the `glyf` table. The format is determined by `head.indexToLocFormat`.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/loca](https://learn.microsoft.com/en-us/typography/opentype/spec/loca)

The table contains **`numGlyphs + 1`** entries. The extra entry allows calculating the length of the last glyph's data.

#### Short Format (`indexToLocFormat` = 0)

Each entry is an `Offset16` (2 bytes). The **actual byte offset** is the stored value multiplied by 2:

```
actual_offset = stored_value * 2
```

This means glyph data must be aligned to 2-byte boundaries. Maximum addressable offset: 2 x 65535 = 131070 bytes.

#### Long Format (`indexToLocFormat` = 1)

Each entry is an `Offset32` (4 bytes). The stored value is the actual byte offset directly.

**Detecting empty glyphs**: If two consecutive entries are equal (`loca[n] == loca[n+1]`), glyph `n` has no outline data. This is normal for the space character (glyph index typically mapped from U+0020) and the `.notdef` glyph (index 0) in some fonts.

**Example** (short format):
```
Glyph 0: loca[0]=0, loca[1]=0     -> offset 0, length 0 (empty .notdef)
Glyph 1: loca[1]=0, loca[2]=50    -> offset 0, length 100 bytes (50*2=100 - 0*2=0)
Glyph 2: loca[2]=50, loca[3]=50   -> offset 100, length 0 (empty, e.g. space)
Glyph 3: loca[3]=50, loca[4]=120  -> offset 100, length 140 bytes
```

### name (Naming Table)

The `name` table contains human-readable strings for the font: family name, style, copyright, license, description, and more. Strings can be stored in multiple languages and encodings.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/name](https://learn.microsoft.com/en-us/typography/opentype/spec/name)

#### Key Name IDs

| Name ID | Description | Example |
|---------|-------------|---------|
| 0 | Copyright notice | "Copyright 2024 Google LLC" |
| 1 | Font Family name | "Roboto" |
| 2 | Font Subfamily name | "Bold Italic" |
| 3 | Unique font identifier | "1.001;GOOG;Roboto-BoldItalic" |
| 4 | Full font name | "Roboto Bold Italic" |
| 5 | Version string | "Version 1.001" |
| 6 | PostScript name | "Roboto-BoldItalic" (no spaces, max 63 chars) |
| 7 | Trademark | (trademark notice) |
| 8 | Manufacturer name | "Google" |
| 9 | Designer | "Christian Robertson" |
| 10 | Description | (free-form description) |
| 11 | Vendor URL | "http://www.google.com" |
| 12 | Designer URL | (designer's website) |
| 13 | License description | (license text) |
| 14 | License info URL | (license URL) |
| 16 | Typographic Family name | Used when name ID 1 has been altered for legacy 4-style grouping |
| 17 | Typographic Subfamily name | Used alongside name ID 16 |

**Important distinction -- Name IDs 1/2 vs 16/17**: Traditional systems (especially older Windows) only support four styles per family: Regular, Bold, Italic, Bold Italic. For a font like "Roboto Thin", name ID 1 might be "Roboto Thin" and name ID 2 "Regular", while name ID 16 is "Roboto" and name ID 17 is "Thin". Modern software uses IDs 16/17 when present, falling back to 1/2.

### OS/2 Table

The `OS/2` table contains metrics and classification data critical for Windows text layout. Despite being technically optional, it is present in virtually all modern fonts.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/os2](https://learn.microsoft.com/en-us/typography/opentype/spec/os2)

#### Key Fields

| Field | Type | Description |
|-------|------|-------------|
| `version` | `uint16` | Table version (0--5). Version 4+ is most common in modern fonts. |
| `xAvgCharWidth` | `int16` | Weighted average width of lowercase Latin glyphs |
| `usWeightClass` | `uint16` | Visual weight: 100=Thin, 200=ExtraLight, 300=Light, **400=Regular**, 500=Medium, 600=SemiBold, **700=Bold**, 800=ExtraBold, 900=Black |
| `usWidthClass` | `uint16` | Visual width: 1=UltraCondensed, 2=ExtraCondensed, 3=Condensed, 4=SemiCondensed, **5=Medium (Normal)**, 6=SemiExpanded, 7=Expanded, 8=ExtraExpanded, 9=UltraExpanded |
| `fsType` | `uint16` | Embedding permissions. Bit 1: Restricted, Bit 2: Preview & Print, Bit 3: Editable, Bit 8: No subsetting, Bit 9: Bitmap embedding only |
| `ySubscriptXSize` | `int16` | Recommended subscript horizontal size |
| `ySuperscriptXSize` | `int16` | Recommended superscript horizontal size |
| `yStrikeoutSize` | `int16` | Strikeout stroke thickness |
| `yStrikeoutPosition` | `int16` | Strikeout stroke position |
| `sFamilyClass` | `int16` | IBM font classification |
| `panose` | `byte[10]` | PANOSE classification (10 digits describing the typeface: family kind, serif style, weight, proportion, contrast, stroke variation, arm style, letterform, midline, x-height) |
| `ulUnicodeRange1-4` | `uint32` x4 | 128-bit field indicating supported Unicode ranges |
| `achVendID` | `Tag` | 4-character vendor identifier (registered with Microsoft) |
| `fsSelection` | `uint16` | Bit 0: Italic, Bit 5: Bold, Bit 6: Regular, Bit 7: USE_TYPO_METRICS, Bit 8: WWS, Bit 9: Oblique |
| `usFirstCharIndex` | `uint16` | Minimum Unicode code point (BMP only) |
| `usLastCharIndex` | `uint16` | Maximum Unicode code point (BMP only) |
| `sTypoAscender` | `int16` | Typographic ascender (see metrics discussion below) |
| `sTypoDescender` | `int16` | Typographic descender (negative) |
| `sTypoLineGap` | `int16` | Typographic line gap |
| `usWinAscent` | `uint16` | Windows clipping ascent (positive, from baseline) |
| `usWinDescent` | `uint16` | Windows clipping descent (positive, distance below baseline) |
| `ulCodePageRange1-2` | `uint32` x2 | 64-bit field indicating supported Windows code pages |
| `sxHeight` | `int16` | x-height (version 2+) |
| `sCapHeight` | `int16` | Cap height (version 2+) |
| `usDefaultChar` | `uint16` | Default character for missing glyphs (version 2+) |
| `usBreakChar` | `uint16` | Break character, usually U+0020 space (version 2+) |
| `usMaxContext` | `uint16` | Maximum context length for GSUB/GPOS lookups (version 2+) |

#### The Three Sets of Vertical Metrics

This is one of the most confusing aspects of font metrics. There are **three** sets of ascender/descender values, and different platforms use them differently:

**1. `OS/2.sTypoAscender` / `sTypoDescender` / `sTypoLineGap` (Modern layout)**

These represent the font designer's intended typographic metrics. They define the "ideal" line spacing. When `OS/2.fsSelection` bit 7 (`USE_TYPO_METRICS`) is set, Windows DirectWrite and modern applications use these values for line spacing:

```
lineHeight = sTypoAscender - sTypoDescender + sTypoLineGap
```

**2. `OS/2.usWinAscent` / `usWinDescent` (Windows GDI clipping)**

These define the **clipping region** for Windows GDI. Any glyph ink that extends above `usWinAscent` or below `usWinDescent` (measured from the baseline) will be clipped. These values are typically set to the font's bounding box extremes (`head.yMax` and `abs(head.yMin)`) to prevent clipping. In older Windows applications (GDI), these also determine line spacing:

```
lineHeight = usWinAscent + usWinDescent   (note: no line gap)
```

**Note**: `usWinDescent` is a **positive** value representing distance below the baseline, unlike `sTypoDescender` which is negative.

**3. `hhea.ascender` / `descender` / `lineGap` (Apple platforms)**

macOS and iOS use the `hhea` table values for line spacing in CoreText and most native text rendering:

```
lineHeight = ascender - descender + lineGap
```

**Recommendation for cross-platform consistency**: Set `OS/2.fsSelection` bit 7 (`USE_TYPO_METRICS`), and ensure all three metric sets produce the same intended line height. Many modern fonts set `hhea` values equal to the Typo values.

### post (PostScript)

The `post` table contains information needed for PostScript printing and naming of glyphs.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/post](https://learn.microsoft.com/en-us/typography/opentype/spec/post)

#### Header Fields (All Versions)

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `Fixed` | `version` | 1.0, 2.0, 2.5 (deprecated), or 3.0 |
| 4 | `Fixed` | `italicAngle` | Italic angle in degrees counter-clockwise from vertical. 0 for upright, negative for right-leaning italic (e.g., -12.0) |
| 8 | `FWORD` | `underlinePosition` | Top of underline stroke, relative to baseline (typically negative, e.g., -100) |
| 10 | `FWORD` | `underlineThickness` | Underline stroke thickness |
| 12 | `uint32` | `isFixedPitch` | Non-zero if the font is monospaced |
| 16 | `uint32` | `minMemType42` | Minimum memory for downloading as a Type 42 font (0 if unknown) |
| 20 | `uint32` | `maxMemType42` | Maximum memory for downloading as a Type 42 font (0 if unknown) |
| 24 | `uint32` | `minMemType1` | Minimum memory for downloading as a Type 1 font (0 if unknown) |
| 28 | `uint32` | `maxMemType1` | Maximum memory for downloading as a Type 1 font (0 if unknown) |

#### Version Differences

| Version | Description |
|---------|-------------|
| **1.0** | All glyph names come from the standard Macintosh ordering (258 glyphs). No additional data after the header. |
| **2.0** | Most common. After the header: `uint16 numberOfGlyphs`, then `uint16[numberOfGlyphs]` glyph name indices. Index 0--257 maps to standard Mac names; 258+ indexes into an appended string table of Pascal-style strings (length byte + ASCII characters). |
| **3.0** | No PostScript glyph names are provided. Used when glyph names are not needed (e.g., OpenType CFF fonts with their own naming, or fonts that do not need to support PostScript printers). No additional data after the header. |

### Optional Tables

| Table | Name | Description |
|-------|------|-------------|
| `cvt ` | Control Value Table | Array of `FWORD` values referenced by hinting instructions. Contains font-wide metrics like stem widths, overshoot values, and key distances that should be consistent across glyphs. |
| `fpgm` | Font Program | TrueType bytecode instructions executed once when the font is loaded. Typically defines subroutines (functions) used by glyph-level instructions. |
| `prep` | Control Value Program | TrueType bytecode executed each time the point size or transformation changes. Typically adjusts `cvt` values based on the current ppem. |
| `gasp` | Grid-fitting and Scan-conversion Procedure | Specifies ppem ranges and their preferred rendering behavior: gridfit only, grayscale only, both, or symmetric smoothing. Controls when hinting and anti-aliasing are applied. |
| `kern` | Kerning | Legacy kerning table with pair-based adjustments. Being superseded by `GPOS` but still widely supported as a fallback. See [Section 8](#8-kerning-and-advanced-typography). |
| `GPOS` | Glyph Positioning | OpenType advanced glyph positioning: kerning, mark attachment, cursive connection, and more. See [Section 8](#8-kerning-and-advanced-typography). |
| `GSUB` | Glyph Substitution | OpenType glyph substitution: ligatures, alternates, contextual forms, and more. See [Section 8](#8-kerning-and-advanced-typography). |
| `GDEF` | Glyph Definition | Classifies glyphs (base, ligature, mark, component), provides attachment point data, and defines ligature caret positions. Required support table for `GPOS` and `GSUB`. |

---

## 4. Font Metrics

### Em Square and FUnits

The **em square** is the abstract design grid on which all glyph outlines are drawn. Its size is defined by `head.unitsPerEm`:

- **TrueType fonts**: Commonly `2048` FUnits per em. This is a power of 2, which was historically preferred because TrueType uses integer coordinates and power-of-2 divisions are faster.
- **CFF/CFF2 fonts**: Commonly `1000` FUnits per em (inherited from the PostScript Type 1 convention).
- **Valid range**: 16 to 16384.

All coordinates in the font are expressed as integers in **font design units (FUnits)**. The valid coordinate range is -16384 to +16383 (signed 16-bit for most tables).

The em square does not directly correspond to any visible part of a glyph. A capital letter typically occupies about 70% of the em height. Ascenders may extend above the em square, and descenders below it. The em square defines the unit of measurement, not a bounding box.

### Converting FUnits to Pixels

To render text at a specific point size and resolution, FUnit values must be converted to pixel values:

```
ppem = pointSize * dpi / 72

pixel_value = font_unit_value * ppem / unitsPerEm
```

Or equivalently:

```
pixel_value = font_unit_value * pointSize * dpi / (72 * unitsPerEm)
```

Where:
- `pointSize` is the requested size in typographic points (1 point = 1/72 inch)
- `dpi` is the output device resolution in dots per inch (common values: 72, 96, 144)
- `unitsPerEm` is from the `head` table
- `ppem` is pixels per em -- the number of pixels that correspond to one em at the given size and resolution

**Worked example**: Convert an ascender value of 1900 FUnits to pixels at 12pt, 96 dpi, with `unitsPerEm` = 2048:

```
ppem = 12 * 96 / 72 = 16
pixel_value = 1900 * 16 / 2048 = 14.84 pixels
```

**Another example**: 550 FUnits at 18pt, 72 dpi, 2048 upem:

```
ppem = 18 * 72 / 72 = 18
pixel_value = 550 * 18 / 2048 = 4.83 pixels
```

### Vertical Metrics

The following diagram (read top to bottom) illustrates the key vertical metric positions relative to the baseline:

```
    +----------------------------------+  <- yMax (head) / usWinAscent (OS/2)
    |                                  |
    |   +--+         Ascender line     |  <- sTypoAscender / hhea.ascender
    |   |  |  +-+                      |
    |   |  |  | |    Cap Height        |  <- sCapHeight (OS/2)
    |   |  |  | |                      |
    |   +--+  | |    x-Height          |  <- sxHeight (OS/2)
    |         +-+                      |
====|==================================|==== Baseline (y = 0)
    |         +-+                      |
    |         | |    Descender line    |  <- sTypoDescender / hhea.descender
    |         +-+                      |     (negative value)
    |                                  |
    +----------------------------------+  <- yMin (head) / -usWinDescent (OS/2)
```

**Key vertical metric definitions:**

| Metric | Source | Description |
|--------|--------|-------------|
| Ascender | `OS/2.sTypoAscender` or `hhea.ascender` | Distance from baseline to top of tallest lowercase ascender (e.g., "h", "d") |
| Descender | `OS/2.sTypoDescender` or `hhea.descender` | Distance from baseline to bottom of lowest descender (e.g., "g", "p") -- negative |
| Line Gap | `OS/2.sTypoLineGap` or `hhea.lineGap` | Additional spacing between descent of one line and ascent of the next |
| Cap Height | `OS/2.sCapHeight` | Height of flat capital letters (e.g., "H", "I") |
| x-Height | `OS/2.sxHeight` | Height of flat lowercase letters (e.g., "x", "z") |

**Line spacing calculation:**

```
lineHeight = ascender - descender + lineGap
```

For a font with `sTypoAscender` = 1854, `sTypoDescender` = -434, `sTypoLineGap` = 0:
```
lineHeight = 1854 - (-434) + 0 = 2288 FUnits
```

At 16 ppem with 2048 upem: `2288 * 16 / 2048 = 17.875 pixels` (round to 18).

### Horizontal Glyph Metrics

Each glyph has a set of horizontal metrics that control its placement:

```
|<----- advanceWidth ----->|
|          |                |
|   lsb    |  glyph width  | RSB
|<-------->|<------------>|<->|
|          |    +---------+   |
|          |    |         |   |
|          |    |  glyph  |   |
|          |    |  outline |   |
|          |    |         |   |
|          |    +---------+   |
|          |                  |
|       xMin              xMax|
origin                     next origin
```

| Metric | Source | Description |
|--------|--------|-------------|
| **advanceWidth** | `hmtx` | Total horizontal distance from this glyph's origin to the next glyph's origin |
| **lsb** (left side bearing) | `hmtx` | Distance from the glyph origin to the left edge of the glyph's bounding box (`xMin`) |
| **RSB** (right side bearing) | Calculated | Distance from the right edge of the bounding box to the next glyph's origin |
| **glyph width** | `glyf` bounding box | `xMax - xMin` |

**Relationship:**

```
advanceWidth = lsb + (xMax - xMin) + RSB
RSB = advanceWidth - lsb - (xMax - xMin)
```

---

## 5. Character Encoding and the cmap Table

The `cmap` table maps character codes (Unicode code points or legacy encodings) to glyph indices. It is the primary interface between character input and glyph rendering.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/cmap](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap)

### Structure

The table starts with a header, followed by an array of encoding records, each pointing to a subtable:

**Header:**

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `version` | 0 |
| `uint16` | `numTables` | Number of encoding records |

**Encoding Record (8 bytes each):**

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `platformID` | Platform identifier |
| `uint16` | `encodingID` | Platform-specific encoding identifier |
| `Offset32` | `subtableOffset` | Byte offset from the start of the `cmap` table to the subtable |

Multiple encoding records may point to the same subtable.

### Platform/Encoding IDs

#### Platform 0 -- Unicode

| Encoding ID | Description |
|-------------|-------------|
| 0 | Unicode 1.0 (deprecated) |
| 1 | Unicode 1.1 (deprecated) |
| 2 | ISO/IEC 10646 (deprecated) |
| 3 | Unicode 2.0+ BMP only (subtable formats 0, 4, 6) |
| 4 | Unicode 2.0+ full repertoire (subtable formats 0, 4, 6, 10, 12, 13) |
| 5 | Unicode Variation Sequences (subtable format 14) |
| 6 | Unicode full repertoire (subtable format 13 for last resort fonts) |

#### Platform 3 -- Windows

| Encoding ID | Description |
|-------------|-------------|
| 0 | Symbol (custom encoding) |
| 1 | Unicode BMP only (subtable format 4) |
| 2 | ShiftJIS (deprecated) |
| 3 | PRC / GB2312 (deprecated) |
| 4 | Big5 (deprecated) |
| 5 | Wansung / KSC5601 (deprecated) |
| 6 | Johab (deprecated) |
| 10 | Unicode full repertoire (subtable format 12) |

### Subtable Formats

#### Format 0 -- Byte Encoding Table (Legacy)

A simple 256-byte array mapping character codes 0--255 to glyph indices 0--255. Only useful for single-byte legacy encodings (e.g., Mac Roman). Rarely used in modern fonts.

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `format` | 0 |
| `uint16` | `length` | Table length in bytes (262) |
| `uint16` | `language` | Mac-specific language code (0 for non-Mac) |
| `uint8[256]` | `glyphIdArray` | Glyph index for each character code |

#### Format 4 -- Segment Mapping to Delta Values (BMP)

The most widely used format. Maps the entire Basic Multilingual Plane (U+0000 -- U+FFFF) using a compact segment-based structure.

**Header:**

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `format` | 4 |
| `uint16` | `length` | Total subtable length |
| `uint16` | `language` | Language code |
| `uint16` | `segCountX2` | 2 x number of segments |
| `uint16` | `searchRange` | Binary search hint |
| `uint16` | `entrySelector` | Binary search hint |
| `uint16` | `rangeShift` | Binary search hint |

**Segment Arrays** (each has `segCount` entries):

| Array | Type | Description |
|-------|------|-------------|
| `endCode` | `uint16[]` | End character code for each segment. Last segment must end with 0xFFFF. |
| (padding) | `uint16` | Reserved, set to 0 |
| `startCode` | `uint16[]` | Start character code for each segment |
| `idDelta` | `int16[]` | Delta to add to character code to get glyph index |
| `idRangeOffset` | `uint16[]` | Offset into `glyphIdArray`, or 0 for delta-only mapping |

**Lookup algorithm for a character code `c`:**

1. Find the segment `i` where `startCode[i] <= c <= endCode[i]`.
2. If `idRangeOffset[i] == 0`:
   ```
   glyphIndex = (c + idDelta[i]) mod 65536
   ```
3. If `idRangeOffset[i] != 0`:
   ```
   glyphIndex = glyphIdArray[idRangeOffset[i]/2 + (c - startCode[i]) + (i - segCount)]
   // More precisely, the address is:
   // &idRangeOffset[i] + idRangeOffset[i] + 2*(c - startCode[i])
   ```
   If the resulting glyph index is 0, the character is not mapped. Otherwise, add `idDelta[i]` (mod 65536).

The `idRangeOffset` trick uses the offset relative to the current position in the `idRangeOffset` array itself, which is why the formula references `&idRangeOffset[i]`.

#### Format 6 -- Trimmed Table Mapping

A simple dense array for a contiguous range of character codes.

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `format` | 6 |
| `uint16` | `length` | Total subtable length |
| `uint16` | `language` | Language code |
| `uint16` | `firstCode` | First character code in range |
| `uint16` | `entryCount` | Number of entries |
| `uint16[entryCount]` | `glyphIdArray` | Glyph indices |

Lookup: `glyphIndex = glyphIdArray[c - firstCode]` (if `firstCode <= c < firstCode + entryCount`).

#### Format 12 -- Segmented Coverage (Full Unicode)

Extends Format 4 to the full Unicode range (U+0000 -- U+10FFFF) using 32-bit character codes.

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `format` | 12 |
| `uint16` | `reserved` | 0 |
| `uint32` | `length` | Total subtable length |
| `uint32` | `language` | Language code |
| `uint32` | `numGroups` | Number of groupings |

**SequentialMapGroup records** (`numGroups` entries, each 12 bytes):

| Type | Field | Description |
|------|-------|-------------|
| `uint32` | `startCharCode` | First character code in group |
| `uint32` | `endCharCode` | Last character code in group |
| `uint32` | `startGlyphID` | Glyph index for `startCharCode`; subsequent chars get sequential indices |

Lookup: Find group where `startCharCode <= c <= endCharCode`, then:
```
glyphIndex = startGlyphID + (c - startCharCode)
```

### Recommended Lookup Priority

When parsing a font, check for `cmap` subtables in this priority order to get the best Unicode coverage:

| Priority | Platform | Encoding | Format | Coverage |
|----------|----------|----------|--------|----------|
| 1 | 3 (Windows) | 10 (Unicode full) | 12 | Full Unicode (U+0000--U+10FFFF) |
| 2 | 3 (Windows) | 1 (Unicode BMP) | 4 | BMP only (U+0000--U+FFFF) |
| 3 | 0 (Unicode) | 4 (Full repertoire) | 12 | Full Unicode |
| 4 | 0 (Unicode) | 3 (BMP) | 4 | BMP only |

Most modern fonts include at least a Platform 3 / Encoding 1 / Format 4 subtable for Windows BMP compatibility, and many also include a Format 12 subtable for supplementary plane coverage.

---

## 6. Glyph Data and the glyf Table

The `glyf` table contains the actual outline data for every glyph. Each glyph is located via the `loca` table.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/glyf](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf)

### Glyph Header

Every non-empty glyph begins with a 10-byte header:

| Offset | Type | Field | Description |
|--------|------|-------|-------------|
| 0 | `int16` | `numberOfContours` | >= 0 for simple glyphs; -1 for composite glyphs |
| 2 | `int16` | `xMin` | Minimum x coordinate |
| 4 | `int16` | `yMin` | Minimum y coordinate |
| 6 | `int16` | `xMax` | Maximum x coordinate |
| 8 | `int16` | `yMax` | Maximum y coordinate |

### Simple Glyphs

A simple glyph (`numberOfContours >= 0`) contains one or more closed contours, each made of a sequence of points (on-curve and off-curve).

**Data after header:**

| Type | Field | Description |
|------|-------|-------------|
| `uint16[numberOfContours]` | `endPtsOfContours` | Index of last point in each contour (0-based). The total number of points is `endPtsOfContours[last] + 1`. |
| `uint16` | `instructionLength` | Size of instruction data in bytes |
| `uint8[instructionLength]` | `instructions` | TrueType hinting instructions for this glyph |
| `uint8[]` | `flags` | One flag byte per point (with run-length encoding via REPEAT_FLAG) |
| `varies` | `xCoordinates` | X coordinates (delta-encoded, format depends on flags) |
| `varies` | `yCoordinates` | Y coordinates (delta-encoded, format depends on flags) |

#### Flag Bits

| Bit | Name | Description |
|-----|------|-------------|
| 0 | `ON_CURVE_POINT` | If set, this is an on-curve point; otherwise, off-curve (quadratic control point) |
| 1 | `X_SHORT_VECTOR` | If set, x-coordinate is 1 byte (uint8); otherwise, 2 bytes (int16) or same as previous |
| 2 | `Y_SHORT_VECTOR` | If set, y-coordinate is 1 byte (uint8); otherwise, 2 bytes (int16) or same as previous |
| 3 | `REPEAT_FLAG` | If set, the next byte specifies how many additional times this flag byte is repeated |
| 4 | `X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR` | If `X_SHORT_VECTOR` set: 0=negative, 1=positive. If `X_SHORT_VECTOR` clear: 1=x is same as previous (delta=0), 0=x is a signed int16 delta. |
| 5 | `Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR` | Same logic as bit 4 but for y-coordinate |
| 6 | `OVERLAP_SIMPLE` | Contour may overlap (hint for rasterizer) |
| 7 | Reserved | Set to 0 |

#### Coordinate Encoding

Coordinates are stored as **deltas** relative to the previous point (or relative to 0,0 for the first point).

The encoding of each delta depends on two flag bits:

**X-coordinate encoding (bits 1 and 4):**

| Bit 1 (X_SHORT) | Bit 4 (X_SAME/POS) | Format | Meaning |
|-----------------|--------------------|---------| --------|
| 0 | 0 | `int16` | Signed 16-bit delta |
| 0 | 1 | (no data) | Delta is 0 (same x as previous point) |
| 1 | 0 | `uint8` | Unsigned byte, negated (delta = -value) |
| 1 | 1 | `uint8` | Unsigned byte, positive (delta = +value) |

The Y-coordinate encoding follows the same pattern with bits 2 and 5.

This encoding is quite compact: zero deltas use no bytes, small deltas use 1 byte, and only large deltas need 2 bytes.

#### Curve Construction Rules

TrueType outlines use **quadratic Bezier curves** with these rules:

1. **On-curve point followed by on-curve point**: Straight line segment between the two points.
2. **On-curve, off-curve, on-curve**: A quadratic Bezier curve. The off-curve point is the control point; the on-curve points are the start and end.
3. **Two consecutive off-curve points**: An implicit on-curve point is inserted at the midpoint between them. This creates a smooth curve through the implied midpoint. This is a key space-saving optimization in TrueType -- it allows smooth curves with fewer stored points.
4. **All contours are closed**: The last point implicitly connects back to the first point of the contour.

**Example**: Points `A(on), B(off), C(off), D(on)` produce two quadratic Bezier segments:
- Curve 1: from A, control B, to midpoint(B,C)
- Curve 2: from midpoint(B,C), control C, to D

This is distinct from CFF/CFF2 outlines, which use **cubic** Bezier curves (two control points per curve segment).

### Composite Glyphs

A composite glyph (`numberOfContours == -1`) is assembled from references to other glyphs. This is commonly used for accented characters (e.g., "e" + combining acute accent = "e-acute").

After the glyph header, the data consists of one or more **component records**, each starting with a flags word:

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `flags` | Component flags (see below) |
| `uint16` | `glyphIndex` | Glyph index of the component |
| varies | `argument1` | X offset or point number (size depends on flags) |
| varies | `argument2` | Y offset or point number (size depends on flags) |
| varies | `transform` | Optional scale/rotation matrix (depends on flags) |

#### Component Flags

| Bit | Name | Description |
|-----|------|-------------|
| 0 | `ARG_1_AND_2_ARE_WORDS` | If set, arguments are `int16`; otherwise `int8` |
| 1 | `ARGS_ARE_XY_VALUES` | If set, arguments are signed xy offsets; otherwise, they are point numbers for point-matching |
| 2 | `ROUND_XY_TO_GRID` | Round xy offsets to nearest grid line after scaling |
| 3 | `WE_HAVE_A_SCALE` | A single `F2DOT14` scale value follows arguments (applied to both x and y) |
| 5 | `MORE_COMPONENTS` | Another component record follows this one |
| 6 | `WE_HAVE_AN_X_AND_Y_SCALE` | Two `F2DOT14` values follow: x-scale and y-scale |
| 7 | `WE_HAVE_A_TWO_BY_TWO` | Four `F2DOT14` values follow: a full 2x2 transformation matrix (xscale, scale01, scale10, yscale) |
| 8 | `WE_HAVE_INSTRUCTIONS` | Hinting instructions follow all components |
| 9 | `USE_MY_METRICS` | Use this component's advance width and lsb for the composite glyph (typically set on the base glyph) |
| 10 | `OVERLAP_COMPOUND` | Components may overlap |

The `MORE_COMPONENTS` flag (bit 5) is set on all component records except the last one. After the last component (where bit 5 is clear), optional hinting instructions may follow if `WE_HAVE_INSTRUCTIONS` was set on any component.

When `ARGS_ARE_XY_VALUES` is set (the common case), `argument1` and `argument2` are x and y translation offsets for positioning the component. When clear, they are point indices used to align specific points of the parent and child glyphs.

---

## 7. Glyph Rendering and Rasterization

### Rendering Pipeline

Converting a glyph from font data to visible pixels involves three main stages:

```
1. SCALE          2. HINT             3. SCAN CONVERT
FUnits -> pixels  Grid-fit outlines   Outline -> bitmap
                  (optional)
```

**Stage 1 -- Scale**: The master outline coordinates (in FUnits) are scaled to the target pixel size using the formula from [Section 4](#converting-funits-to-pixels). This produces a floating-point outline in pixel coordinates.

**Stage 2 -- Hint (Grid-Fit)**: TrueType hinting instructions modify the scaled outline to align important features (stems, serifs, alignment zones) with the pixel grid. This improves readability at small sizes by ensuring consistent stem widths and avoiding pixel dropout. This stage is optional -- many modern renderers skip or reduce hinting at larger sizes.

**Stage 3 -- Scan Convert**: The (optionally hinted) outline is rasterized into a bitmap. The scan converter determines which pixels are "inside" the outline.

### Hinting

TrueType hinting uses a **stack-based bytecode virtual machine** that manipulates glyph points directly. It is far more powerful (and complex) than PostScript/CFF hints, which are limited to stem declarations and alignment zones.

**Three levels of hinting programs:**

| Level | Table | When Executed | Purpose |
|-------|-------|---------------|---------|
| Font Program | `fpgm` | Once, when font is loaded | Define subroutines (functions) reused across all glyphs |
| Control Value Program | `prep` (pre-program) | Each time size/transform changes | Adjust CVT values for the current ppem; set up the graphics state |
| Glyph Instructions | Embedded in each glyph in `glyf` | Each time a glyph is rasterized | Grid-fit this specific glyph's points |

**Control Value Table (`cvt`)**: An array of `FWORD` values representing key font-wide distances in FUnits (e.g., uppercase stem width, lowercase x-height overshoot, serif height). The `prep` program scales these to the current ppem and adjusts them for grid-fitting (e.g., rounding a stem width to exactly 1 or 2 pixels at small sizes).

**Graphics State**: The hinting VM maintains a set of state variables that control how instructions operate:

- `freedom_vector` and `projection_vector` -- directions for moving/measuring points
- `round_state` -- rounding mode (to grid, to half grid, off, etc.)
- `minimum_distance` -- smallest allowed distance between points
- `control_value_cut_in` -- threshold for using CVT values vs. outline distances
- `auto_flip` -- whether to automatically adjust direction for MIRP/MIAP
- And many more

### Scan Converter

The scan converter determines which pixels to turn on based on the outline:

**Rule 1**: If a pixel's center point falls **inside** the outline contour, the pixel is turned on (filled).

**Rule 2**: If a contour passes **exactly through** a pixel center, that pixel is turned on.

**Interior determination** uses the **non-zero winding number rule**: Cast a ray from the test point to infinity. For each contour crossing, add +1 if the contour crosses left-to-right, -1 if right-to-left. If the final sum is non-zero, the point is inside.

**Dropout control** (Rules 3 and 4): At very small sizes, thin features (like the crossbar of a lowercase "e") might fall between pixel centers and disappear entirely. Dropout control rules detect these cases:

- **Rule 3**: Turn on the closer of two adjacent pixels when a scan line intersects a contour that would otherwise produce a dropout.
- **Rule 4**: Same as Rule 3, but only when the contour is nearly horizontal/vertical. Stubs at acute angles are allowed to drop out.

The `gasp` table and glyph-level SCANCTRL/SCANTYPE instructions control which dropout rules are active.

### Anti-Aliasing

Binary rasterization (pixels are fully on or off) produces jagged edges. Anti-aliasing smooths these edges:

**Grayscale anti-aliasing**: Instead of a binary on/off decision, each pixel gets a **coverage value** (0--255) representing how much of the pixel is covered by the outline. This is typically computed by supersampling -- rasterizing at a higher resolution (e.g., 8x8 subpixels per pixel) and counting how many subpixels are "on".

**Subpixel rendering (ClearType)**: On LCD screens, each pixel is composed of three colored sub-elements (typically red, green, blue) arranged horizontally. By controlling each sub-element independently, the effective horizontal resolution is tripled. This technique, Microsoft's **ClearType**, provides significantly sharper text rendering:

- The outline is rasterized at 3x horizontal resolution.
- Each color channel (R, G, B) gets its own coverage value.
- Filtering is applied to reduce color fringing at the edges.

The `gasp` table specifies ppem thresholds for rendering behavior:

| Bit | Name | Meaning |
|-----|------|---------|
| 0 | `GASP_GRIDFIT` | Use gridfitting (hinting) at this size |
| 1 | `GASP_DOGRAY` | Use grayscale anti-aliasing |
| 2 | `GASP_SYMMETRIC_GRIDFIT` | Use gridfitting with ClearType symmetric smoothing |
| 3 | `GASP_SYMMETRIC_SMOOTHING` | Use smoothing with ClearType |

A common configuration: gridfit only below 9 ppem, gridfit + grayscale from 9--20 ppem, ClearType smoothing above 20 ppem.

### ppem Calculation

The core formula for pixels per em:

```
ppem = pointSize * dpi / 72
```

| Point Size | DPI | ppem |
|-----------|-----|------|
| 8pt | 72 | 8 |
| 8pt | 96 | 10.67 |
| 10pt | 96 | 13.33 |
| 12pt | 72 | 12 |
| 12pt | 96 | **16** |
| 12pt | 144 | 24 |
| 16pt | 96 | 21.33 |
| 24pt | 96 | 32 |
| 72pt | 96 | 96 |

At standard Windows resolution (96 dpi), **12pt text = 16 ppem**, which is a common baseline for readability testing.

---

## 8. Kerning and Advanced Typography

### Legacy kern Table

The `kern` table provides simple pair-based kerning adjustments. Although superseded by `GPOS`, it remains widely supported as a fallback.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/kern](https://learn.microsoft.com/en-us/typography/opentype/spec/kern)

**Format 0 subtable** (the most common format):

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `nPairs` | Number of kerning pairs |
| `uint16` | `searchRange` | Binary search hint |
| `uint16` | `entrySelector` | Binary search hint |
| `uint16` | `rangeShift` | Binary search hint |

Each pair record (6 bytes):

| Type | Field | Description |
|------|-------|-------------|
| `uint16` | `left` | Glyph index for left glyph |
| `uint16` | `right` | Glyph index for right glyph |
| `FWORD` | `value` | Kerning value in FUnits (negative = tighten, positive = loosen) |

Pairs are sorted by the combined key `(left << 16) | right` for binary search.

**Example pairs** (conceptual):

| Left | Right | Value | Effect |
|------|-------|-------|--------|
| A | V | -80 | Tighten -- the diagonal strokes nest together |
| T | o | -60 | Tighten -- "o" tucks under the crossbar of "T" |
| V | A | -80 | Tighten -- mirror of A-V |
| L | T | -40 | Tighten -- "T" can slide left over "L" |

### GPOS Table

The **Glyph Positioning** table is the OpenType replacement for `kern`, providing far more powerful positioning capabilities.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/gpos](https://learn.microsoft.com/en-us/typography/opentype/spec/gpos)

#### GPOS Lookup Types

| Type | Name | Description |
|------|------|-------------|
| 1 | Single Adjustment | Adjust position of a single glyph (e.g., superscript shift) |
| 2 | Pair Adjustment | Adjust positions of a pair of glyphs (kerning) |
| 3 | Cursive Attachment | Connect glyphs along a cursive baseline (Arabic) |
| 4 | MarkToBase Attachment | Position a combining mark relative to a base glyph |
| 5 | MarkToLigature Attachment | Position a combining mark relative to a ligature component |
| 6 | MarkToMark Attachment | Position a mark relative to another mark (stacking diacritics) |
| 7 | Context Positioning | Adjust positions based on glyph sequence context |
| 8 | Chained Context Positioning | Adjust positions based on extended context (backtrack + input + lookahead) |
| 9 | Extension Positioning | Allows subtables to exceed 64KB offset limit |

#### Pair Adjustment (Type 2) -- Kerning

**Format 1 -- Individual pair sets**: A list of specific glyph pairs with their adjustments. Conceptually equivalent to the `kern` table but with two ValueRecords per pair (one for each glyph), allowing adjustments in both X and Y on both glyphs.

**Format 2 -- Class-based pairs**: Glyphs are grouped into classes (e.g., "all round letters" or "all diagonal caps"), and kerning values are specified per class pair. This is far more efficient for large character sets:

- ClassDef1: groups left-side glyphs into classes
- ClassDef2: groups right-side glyphs into classes
- A 2D array indexed by [class1, class2] holds ValueRecords

**Advantages of GPOS over kern:**

| Feature | kern | GPOS |
|---------|------|------|
| Adjustments per pair | One value (horizontal only) | Both glyphs, X and Y positions and advances |
| Class-based kerning | No | Yes (Format 2) |
| Contextual positioning | No | Yes (Types 7, 8) |
| Mark positioning | No | Yes (Types 4, 5, 6) |
| Device-dependent values | No | Yes (device tables for pixel-level control) |
| Script/language awareness | No | Yes (via ScriptList / FeatureList) |

### GSUB Table

The **Glyph Substitution** table replaces glyph sequences during text shaping.

**Specification**: [https://learn.microsoft.com/en-us/typography/opentype/spec/gsub](https://learn.microsoft.com/en-us/typography/opentype/spec/gsub)

| Type | Name | Description | Example |
|------|------|-------------|---------|
| 1 | Single | Replace one glyph with another | Small caps: a -> a.smcp |
| 2 | Multiple | Replace one glyph with a sequence | Decomposition: fi -> f + i |
| 3 | Alternate | Replace one glyph with one of several alternatives | Stylistic alternates |
| 4 | Ligature | Replace a sequence with one glyph | f + i -> fi ligature |
| 5 | Context | Substitute based on glyph sequence context | |
| 6 | Chaining Context | Substitute based on extended context | |
| 7 | Extension | Allows subtables beyond 64KB offset | |
| 8 | Reverse Chaining Context | Right-to-left contextual substitution | Nastaliq Arabic |

### Common OpenType Features

| Tag | Name | Description |
|-----|------|-------------|
| `kern` | Kerning | Pair positioning adjustments (via GPOS Type 2) |
| `liga` | Standard Ligatures | Common ligatures: fi, fl, ff, ffi, ffl |
| `clig` | Contextual Ligatures | Context-dependent ligatures |
| `dlig` | Discretionary Ligatures | Decorative ligatures (ct, st, etc.) |
| `smcp` | Small Capitals | Replace lowercase with small capital forms |
| `onum` | Oldstyle Figures | Varying-height numerals (text figures) |
| `lnum` | Lining Figures | Uniform-height numerals (tabular or proportional) |
| `tnum` | Tabular Figures | Fixed-width numerals for column alignment |
| `pnum` | Proportional Figures | Variable-width numerals |

---

## 9. TTF vs OTF

The terms "TTF" and "OTF" are often used loosely. In precise terms, both are OpenType fonts stored in the `sfnt` container. The key difference is the outline format:

| Aspect | TrueType Outlines (`.ttf`) | CFF/CFF2 Outlines (`.otf`) |
|--------|---------------------------|----------------------------|
| **Curve type** | Quadratic Bezier (1 control point per curve) | Cubic Bezier (2 control points per curve) |
| **Outline storage** | `glyf` table (with `loca` index) | `CFF ` or `CFF2` table (self-contained) |
| **sfntVersion** | `0x00010000` | `0x4F54544F` (`OTTO`) |
| **Hinting** | Stack-based bytecode VM (very powerful, very complex) | Stem hints + alignment zones (simpler, less control) |
| **File size** | Often larger due to quadratic approximation of curves | Often smaller due to subroutinization and cubic efficiency |
| **Rendering at small sizes** | Potentially better with expert hinting (e.g., core Windows fonts) | Relies more on rasterizer quality; CFF2 adds variation support |
| **maxp version** | 1.0 (32 bytes, full profile) | 0.5 (6 bytes, only numGlyphs) |
| **Design tooling origin** | Apple / Microsoft ecosystem | Adobe ecosystem (Type 1 heritage) |

**Tables shared by both formats**: `head`, `hhea`, `hmtx`, `maxp`, `name`, `OS/2`, `post`, `cmap`, `GPOS`, `GSUB`, `GDEF`, `BASE`, `JSTF`, `gasp`, `kern`, and all other non-outline tables.

**OpenType extensions beyond classic TrueType:**

- Advanced layout (`GPOS`, `GSUB`, `GDEF`, `BASE`, `JSTF`, `MATH`)
- Font variations / variable fonts (`fvar`, `gvar`, `avar`, `STAT`, `HVAR`, `VVAR`, `MVAR`, `cvar`, `CFF2`)
- Color fonts (`COLR`/`CPAL`, `SVG `, `CBDT`/`CBLC`, `sbix`)
- `DSIG` (digital signature)
- `LTSH` (linear threshold), `VDMX` (vertical device metrics), `hdmx` (horizontal device metrics)

---

## 10. System Font Access in C#/.NET

### System.Drawing (Windows-only as of .NET 6+)

`System.Drawing.Common` was cross-platform in .NET Core 3.1 and .NET 5, but starting with **.NET 6**, it is **Windows-only** (throws `PlatformNotSupportedException` on other platforms). It wraps Windows GDI+.

**Enumerating installed fonts:**

```csharp
using System.Drawing;
using System.Drawing.Text;

// All installed font families
foreach (FontFamily family in FontFamily.Families)
    Console.WriteLine(family.Name);

// Or via InstalledFontCollection for more control
using var installed = new InstalledFontCollection();
foreach (FontFamily family in installed.Families)
    Console.WriteLine(family.Name);
```

**Getting font metrics:**

```csharp
using System.Drawing;

var family = new FontFamily("Arial");

// Metrics are in font design units
int ascent = family.GetCellAscent(FontStyle.Regular);    // e.g., 1854
int descent = family.GetCellDescent(FontStyle.Regular);   // e.g., 434
int emHeight = family.GetEmHeight(FontStyle.Regular);     // e.g., 2048
int lineSpacing = family.GetLineSpacing(FontStyle.Regular); // e.g., 2355

// Convert to pixels at 12pt, 96dpi
float ascentPx = ascent * 12f * 96f / (72f * emHeight);

// Check style availability
bool hasBold = family.IsStyleAvailable(FontStyle.Bold);
```

**Loading a private font (from file):**

```csharp
using System.Drawing.Text;

var privateFonts = new PrivateFontCollection();
privateFonts.AddFontFile(@"C:\MyFonts\CustomFont.ttf");

FontFamily family = privateFonts.Families[0];
using var font = new Font(family, 16f, FontStyle.Regular, GraphicsUnit.Pixel);
```

### System Font Directories

| Platform | Primary Directory | Additional Directories |
|----------|-------------------|----------------------|
| **Windows** | `C:\Windows\Fonts\` | `%LOCALAPPDATA%\Microsoft\Windows\Fonts\` (per-user fonts, Windows 10+) |
| **macOS** | `/System/Library/Fonts/` | `/Library/Fonts/` (system-wide), `~/Library/Fonts/` (per-user) |
| **Linux** | `/usr/share/fonts/` | `/usr/local/share/fonts/`, `~/.fonts/`, `~/.local/share/fonts/` |

On Windows, the font directory can be retrieved programmatically:

```csharp
string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
// Returns: C:\WINDOWS\Fonts  (typically)
```

### SkiaSharp (Cross-Platform)

**SkiaSharp** wraps Google's Skia graphics library and provides cross-platform font access and rendering.

- NuGet: `SkiaSharp` (plus platform-specific native packages)
- GitHub: [https://github.com/mono/SkiaSharp](https://github.com/mono/SkiaSharp)

**Enumerating system fonts:**

```csharp
using SkiaSharp;

var fontManager = SKFontManager.Default;

// List all font family names
foreach (var name in fontManager.FontFamilies)
    Console.WriteLine(name);

// Get a specific typeface
SKTypeface typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal);

// Or from a file
SKTypeface fileTypeface = SKTypeface.FromFile(@"C:\MyFonts\CustomFont.ttf");

// Get font metrics
using var font = new SKFont(typeface, 16f);
SKFontMetrics metrics = font.Metrics;
Console.WriteLine($"Ascent: {metrics.Ascent}");     // negative (above baseline)
Console.WriteLine($"Descent: {metrics.Descent}");   // positive (below baseline)
Console.WriteLine($"Leading: {metrics.Leading}");
Console.WriteLine($"AvgCharWidth: {metrics.AverageCharacterWidth}");

// Measure text
float width = font.MeasureText("Hello");
```

### Direct File Access

For direct binary parsing of font files:

```csharp
using System;
using System.IO;

// System fonts directory
string fontsPath = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

// Read a font file as raw bytes
byte[] fontData = File.ReadAllBytes(Path.Combine(fontsPath, "arial.ttf"));

// Or enumerate all TTF files
foreach (string file in Directory.GetFiles(fontsPath, "*.ttf"))
{
    Console.WriteLine($"{Path.GetFileName(file)}: {new FileInfo(file).Length} bytes");
}

// Reading the sfnt version to check the font type
uint sfntVersion = (uint)(fontData[0] << 24 | fontData[1] << 16
                        | fontData[2] << 8 | fontData[3]);
if (sfntVersion == 0x00010000)
    Console.WriteLine("TrueType outlines");
else if (sfntVersion == 0x4F54544F)
    Console.WriteLine("CFF outlines (OpenType)");
```

---

## 11. C# Libraries for TTF Parsing

### SixLabors.Fonts

The most actively maintained pure-managed .NET library for font parsing and text measurement.

- **NuGet**: `SixLabors.Fonts` (current stable: v2.1.x)
- **License**: Six Labors Split License (free for open source and small-revenue commercial use; paid license for larger commercial use)
- **GitHub**: [https://github.com/SixLabors/Fonts](https://github.com/SixLabors/Fonts)
- **Target**: .NET 6+

**Capabilities:**
- Reads TrueType (`.ttf`), OpenType (`.otf`), TrueType Collections (`.ttc`), WOFF, and WOFF2
- CFF and CFF2 outline support
- Variable font support (fvar, gvar)
- Color font support (COLR/CPAL, sbix)
- TrueType hinting
- GSUB/GPOS processing with complex script shaping (HarfBuzz integration via `SixLabors.Fonts.HarfBuzz`)
- Text measurement and layout
- Glyph outline access (points, contours, bounding boxes)

**Usage example:**

```csharp
using SixLabors.Fonts;

// Load from system fonts
FontCollection collection = new FontCollection();
FontFamily family = SystemFonts.Get("Arial");

// Or load from file
collection.Add("path/to/myfont.ttf");
FontFamily customFamily = collection.Get("MyFont");

// Create a font at a specific size
Font font = family.CreateFont(16, FontStyle.Regular);

// Access metrics
FontMetrics metrics = font.FontMetrics;
Console.WriteLine($"UnitsPerEm: {metrics.UnitsPerEm}");
Console.WriteLine($"Ascender: {metrics.Ascender}");
Console.WriteLine($"Descender: {metrics.Descender}");
Console.WriteLine($"LineGap: {metrics.LineGap}");

// Measure text
FontRectangle bounds = TextMeasurer.MeasureBounds("Hello, World!", new TextOptions(font));
Console.WriteLine($"Width: {bounds.Width}, Height: {bounds.Height}");

// Access glyph outlines
// (via the GlyphRenderer or IColorGlyphRenderer interfaces)
```

**Pros:**
- Pure managed code -- no native dependencies
- Actively maintained with modern .NET support
- Comprehensive format support
- Good API for text measurement

**Cons:**
- Six Labors Split License may require a paid license for some commercial use
- Rendering quality depends on its own rasterizer (not FreeType)
- API surface is focused on text layout rather than raw table access

### SharpFont (FreeType wrapper)

A managed wrapper around the native FreeType library.

- **NuGet**: `SharpFont` (v4.0.1)
- **License**: Proprietary (see LICENSE) (wrapper), FreeType License or GPLv2 (native FreeType library)
- **GitHub**: [https://github.com/Robmaister/SharpFont](https://github.com/Robmaister/SharpFont)

**Key characteristics:**
- Wraps FreeType 2.x via P/Invoke
- Requires shipping native FreeType binaries for each target platform
- Targets .NET Framework; may work on .NET Core/.NET 5+ with effort
- **Not actively maintained** -- last release in 2018

**Usage example:**

```csharp
using SharpFont;

using var library = new Library();
using var face = new Face(library, "arial.ttf");

face.SetCharSize(0, 16 * 64, 96, 96);  // 16pt at 96dpi (values in 26.6 fixed point)

// Load a glyph
uint glyphIndex = face.GetCharIndex('A');
face.LoadGlyph(glyphIndex, LoadFlags.Default, LoadTarget.Normal);

// Access metrics
Console.WriteLine($"Advance: {face.Glyph.Advance.X}");
Console.WriteLine($"Ascender: {face.Size.Metrics.Ascender}");

// Render to bitmap
face.Glyph.RenderGlyph(RenderMode.Normal);
FTBitmap bitmap = face.Glyph.Bitmap;
```

**Pros:**
- FreeType is the gold standard for font rendering quality
- Access to all FreeType features (hinting, anti-aliasing, outline access)

**Cons:**
- Requires native binaries (complicates deployment)
- Not actively maintained
- .NET Framework-focused
- Cross-platform deployment requires managing native libraries per OS/architecture

### Other Libraries

**Typography.OpenFont**
- Pure managed C# OpenType parser
- GitHub: [https://github.com/AmbientOS/Typography](https://github.com/AmbientOS/Typography)
- Reads TrueType and CFF outlines, provides access to most OpenType tables
- Less actively maintained; API can be rough

**FreeTypeSharp**
- Another FreeType wrapper, newer than SharpFont
- NuGet: `FreeTypeSharp`
- Better .NET 6+ support than SharpFont
- Still requires native FreeType binaries

### Comparison Table for BMFont Generation Suitability

| Feature | SixLabors.Fonts | SharpFont | SkiaSharp | Typography.OpenFont |
|---------|----------------|-----------|-----------|-------------------|
| **Pure managed** | Yes | No (native FreeType) | No (native Skia) | Yes |
| **.NET 6+ support** | Yes | Limited | Yes | Partial |
| **Cross-platform** | Yes | With native binaries | Yes | Yes |
| **Glyph outline access** | Yes | Yes | Yes | Yes |
| **Font metrics access** | Yes | Yes (via FreeType) | Yes | Yes |
| **Kerning data** | Yes (kern + GPOS) | Yes | Yes | Yes |
| **Rasterization** | Yes (built-in) | Yes (FreeType) | Yes (Skia) | No |
| **Hinting support** | Yes | Yes (FreeType) | Yes (Skia) | No |
| **Render quality** | Good | Excellent | Excellent | N/A |
| **Active maintenance** | Yes | No | Yes | Limited |
| **License** | Six Labors Split | MIT + FreeType | MIT | MIT/Apache |
| **BMFont suitability** | High | Medium (maintenance risk) | High | Low (no rasterizer) |

**Recommendation for BMFont generation**: **SixLabors.Fonts** is the best choice for a pure-managed solution with active maintenance and good .NET 6+ support. **SkiaSharp** is the best choice if you need production-quality rendering and are comfortable with native dependencies.

---

## 12. Font Rendering to Bitmap

The process of converting font data to bitmap glyph images for a texture atlas:

**Overall pipeline:**

```
1. Parse font file (read tables)
2. For each character in the target character set:
   a. Look up glyph index via cmap
   b. Read glyph outline from glyf (via loca)
   c. Read glyph metrics from hmtx
   d. Scale outline to target pixel size
   e. Apply hinting (optional, via fpgm/prep/glyph instructions + cvt)
   f. Rasterize outline to grayscale bitmap (scan convert + anti-alias)
3. Pack all glyph bitmaps into atlas pages
4. Record positions, metrics, and kerning
5. Write BMFont descriptor file
```

**Quality considerations:**

| Factor | Guidance |
|--------|----------|
| **Size threshold** | Below approximately 20 ppem, hinting makes a significant difference in legibility. Above 20 ppem, unhinted outlines are generally acceptable. |
| **Anti-aliasing** | Always use grayscale (8-bit) anti-aliasing for texture atlas glyphs. Binary rasterization produces unacceptable jaggies at typical game font sizes. |
| **Supersampling** | Rasterizing at 2x or 4x the target size and downsampling can improve quality, especially for small sizes without hinting. |
| **Padding** | Add at least 1-2 pixels of padding around each glyph in the atlas to prevent texture filtering bleed between adjacent glyphs. |
| **SDF (Signed Distance Fields)** | For fonts that need to scale smoothly at runtime, generate SDF atlases. Each pixel stores the distance to the nearest edge of the glyph outline, enabling resolution-independent rendering via a shader. Typical SDF spread: 4-8 pixels. |

### Atlas Generation Steps

**Step 1 -- Determine character set**: Define which Unicode code points to include. Common sets:

- ASCII printable (U+0020--U+007E) -- 95 characters
- Latin-1 Supplement (U+00A0--U+00FF) -- 96 more characters
- Full Latin Extended for European languages
- Custom character list from game/application text analysis

**Step 2 -- Rasterize each glyph**: For each code point:

- Map to glyph index via `cmap`
- Load outline, scale to target ppem, hint, rasterize
- Store the resulting bitmap and record metrics (width, height, xoffset, yoffset, xadvance)

**Step 3 -- Pack into atlas pages (bin packing)**: Arrange all glyph bitmaps into one or more texture atlas pages of a fixed size (e.g., 512x512, 1024x1024, 2048x2048). Common algorithms:

- **Shelf packing** (simple, fast, moderate efficiency)
- **Guillotine packing** (recursive subdivision)
- **MaxRects** (best results for glyph atlas packing -- tries to minimize wasted space)
- **Skyline** (good balance of speed and efficiency)

**Step 4 -- Record positions and metrics**: For each glyph, store:

- Atlas page index
- Position (x, y) and size (width, height) within the atlas
- Rendering offsets (xoffset, yoffset) for correct placement relative to the cursor
- Advance width (xadvance)

**Step 5 -- Export kerning pairs**: Extract kerning data from `kern` table (Format 0 pairs) and/or `GPOS` table (Type 2 pair adjustments). Convert from FUnits to pixels at the target size.

**Step 6 -- Write BMFont descriptor**: Output in BMFont text, XML, or binary format with all character metrics, kerning pairs, page references, and font-level information.

---

## 13. Key Metric Mappings (TTF to BMFont)

When generating a BMFont from a TTF, font-level and glyph-level metrics must be mapped:

### Font-Level Metrics

| BMFont Field | TTF Source | Formula / Notes |
|-------------|-----------|-----------------|
| `lineHeight` | `OS/2.sTypoAscender`, `sTypoDescender`, `sTypoLineGap` (or `hhea` equivalents) | `(sTypoAscender - sTypoDescender + sTypoLineGap) * ppem / unitsPerEm`, rounded to integer pixels |
| `base` | `OS/2.sTypoAscender` (or `hhea.ascender`) | `sTypoAscender * ppem / unitsPerEm`, rounded. This is the distance from the top of the line to the baseline. |
| `scaleW`, `scaleH` | N/A | Atlas texture dimensions (set by packer) |

### Glyph-Level Metrics

| BMFont Field | TTF Source | Formula / Notes |
|-------------|-----------|-----------------|
| `x`, `y` | N/A | Position of glyph bitmap in atlas (set by packer) |
| `width` | Rasterized bitmap | Pixel width of the rasterized glyph image |
| `height` | Rasterized bitmap | Pixel height of the rasterized glyph image |
| `xoffset` | `hmtx.lsb`, glyph bounding box | Horizontal offset from cursor position to left edge of glyph image: `lsb * ppem / unitsPerEm` (may need adjustment for padding) |
| `yoffset` | Glyph bounding box `yMax` | Vertical offset from top of line to top edge of glyph image: `base - (yMax * ppem / unitsPerEm)` |
| `xadvance` | `hmtx.advanceWidth` | `advanceWidth * ppem / unitsPerEm`, rounded to integer pixels |

### Kerning

| BMFont Field | TTF Source | Formula |
|-------------|-----------|---------|
| `first` | Kerning pair left glyph | The **character code** (not glyph index) of the first character |
| `second` | Kerning pair right glyph | The **character code** of the second character |
| `amount` | `kern` table value or `GPOS` Type 2 XAdvance | `kern_value * ppem / unitsPerEm`, rounded to integer pixels |

**Note**: BMFont kerning uses character IDs (Unicode code points), while TTF kern/GPOS tables use glyph indices. The mapping must go through the `cmap` table in reverse (glyph index to character code) or be tracked during the initial character-to-glyph mapping phase.

---

## 14. Relevant Standards and References

### Specifications

| Resource | URL |
|----------|-----|
| **OpenType Specification (1.9.1)** -- Primary reference for all tables | [https://learn.microsoft.com/en-us/typography/opentype/spec/](https://learn.microsoft.com/en-us/typography/opentype/spec/) |
| **Apple TrueType Reference Manual** -- Apple's original TrueType documentation | [https://developer.apple.com/fonts/TrueType-Reference-Manual/](https://developer.apple.com/fonts/TrueType-Reference-Manual/) |
| **ISO/IEC 14496-22** -- International standard for Open Font Format | [https://standards.iso.org/](https://standards.iso.org/) |
| **TrueType Fundamentals** -- Overview of TrueType digitization and rendering | [https://learn.microsoft.com/en-us/typography/opentype/spec/ttch01](https://learn.microsoft.com/en-us/typography/opentype/spec/ttch01) |

### Individual Table Specifications

| Table | Specification URL |
|-------|-------------------|
| `cmap` | [https://learn.microsoft.com/en-us/typography/opentype/spec/cmap](https://learn.microsoft.com/en-us/typography/opentype/spec/cmap) |
| `glyf` | [https://learn.microsoft.com/en-us/typography/opentype/spec/glyf](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf) |
| `head` | [https://learn.microsoft.com/en-us/typography/opentype/spec/head](https://learn.microsoft.com/en-us/typography/opentype/spec/head) |
| `hhea` | [https://learn.microsoft.com/en-us/typography/opentype/spec/hhea](https://learn.microsoft.com/en-us/typography/opentype/spec/hhea) |
| `hmtx` | [https://learn.microsoft.com/en-us/typography/opentype/spec/hmtx](https://learn.microsoft.com/en-us/typography/opentype/spec/hmtx) |
| `loca` | [https://learn.microsoft.com/en-us/typography/opentype/spec/loca](https://learn.microsoft.com/en-us/typography/opentype/spec/loca) |
| `maxp` | [https://learn.microsoft.com/en-us/typography/opentype/spec/maxp](https://learn.microsoft.com/en-us/typography/opentype/spec/maxp) |
| `name` | [https://learn.microsoft.com/en-us/typography/opentype/spec/name](https://learn.microsoft.com/en-us/typography/opentype/spec/name) |
| `OS/2` | [https://learn.microsoft.com/en-us/typography/opentype/spec/os2](https://learn.microsoft.com/en-us/typography/opentype/spec/os2) |
| `post` | [https://learn.microsoft.com/en-us/typography/opentype/spec/post](https://learn.microsoft.com/en-us/typography/opentype/spec/post) |
| `kern` | [https://learn.microsoft.com/en-us/typography/opentype/spec/kern](https://learn.microsoft.com/en-us/typography/opentype/spec/kern) |
| `GPOS` | [https://learn.microsoft.com/en-us/typography/opentype/spec/gpos](https://learn.microsoft.com/en-us/typography/opentype/spec/gpos) |
| `GSUB` | [https://learn.microsoft.com/en-us/typography/opentype/spec/gsub](https://learn.microsoft.com/en-us/typography/opentype/spec/gsub) |
| `GDEF` | [https://learn.microsoft.com/en-us/typography/opentype/spec/gdef](https://learn.microsoft.com/en-us/typography/opentype/spec/gdef) |

### Libraries and Tools

| Resource | URL |
|----------|-----|
| **FreeType** -- Industry-standard font rendering library | [https://freetype.org/](https://freetype.org/) |
| **FreeType Glyph Metrics** -- Explanation of glyph metrics concepts | [https://freetype.org/freetype2/docs/glyphs/glyphs-3.html](https://freetype.org/freetype2/docs/glyphs/glyphs-3.html) |
| **SixLabors.Fonts** -- Managed .NET font library | [https://github.com/SixLabors/Fonts](https://github.com/SixLabors/Fonts) |
| **SharpFont** -- FreeType wrapper for .NET | [https://github.com/Robmaister/SharpFont](https://github.com/Robmaister/SharpFont) |
| **SkiaSharp** -- Skia graphics library for .NET | [https://github.com/mono/SkiaSharp](https://github.com/mono/SkiaSharp) |
| **BMFont** -- AngelCode BMFont tool and format specification | [https://www.angelcode.com/products/bmfont/](https://www.angelcode.com/products/bmfont/) |
