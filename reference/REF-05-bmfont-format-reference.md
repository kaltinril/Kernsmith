# BMFont Format & Implementations Reference

> **Purpose**: Comprehensive reference document for the BMFont bitmap font format, its file structures, rendering algorithms, and notable implementations across game engines and frameworks.

---

## Table of Contents

1. [Overview and History](#1-overview-and-history)
2. [The .FNT File Format](#2-the-fnt-file-format)
   - [Text Format](#text-format)
   - [XML Format](#xml-format)
   - [Binary Format](#binary-format)
3. [Texture Atlas](#3-texture-atlas)
4. [Text Rendering Algorithm](#4-text-rendering-algorithm)
5. [MonoGame.Extended Implementation](#5-monogameextended-implementation)
6. [Gum UI Implementation](#6-gum-ui-implementation)
7. [libGDX Implementation](#7-libgdx-implementation)
8. [Other Implementations and Tools](#8-other-implementations-and-tools)
9. [Tutorials & Community Resources](#9-tutorials--community-resources)
10. [Limitations and Pain Points](#10-limitations-and-pain-points)
11. [Design Considerations for a New C# Library](#11-design-considerations-for-a-new-c-library)

---

## 1. Overview and History

**BMFont** is a freeware/open-source bitmap font generator created by **Andreas Jonsson** (AngelCode.com). It converts TrueType fonts into pre-rendered bitmap texture atlases paired with descriptor files, enabling efficient text rendering in games and real-time applications.

| Item | Detail |
|------|--------|
| **Author** | Andreas Jonsson |
| **License** | zlib license |
| **Main page** | https://www.angelcode.com/products/bmfont/ |
| **Documentation** | https://www.angelcode.com/products/bmfont/documentation.html |
| **Source code** | SVN at https://svn.code.sf.net/p/bmfont/code/trunk/ |
| **Platform** | Windows native (can run on Linux via [PlayOnLinux](https://www.playonlinux.com/)) |
| **Current version** | 1.14b beta (2025/08/16) |
| **First release** | 1.0 (2004/02/14) |
| **Pixel shader ref** | https://www.angelcode.com/products/bmfont/doc/pixel_shader.html |
| **Known issues** | https://www.angelcode.com/products/bmfont/doc/known_issues.html |
| **Command line ref** | https://www.angelcode.com/products/bmfont/doc/command_line.html |

### Key Features

- **Unicode 13.0 support** -- full Unicode range including supplementary planes (codepoints above U+FFFF)
- **Anti-aliasing** -- smooth glyph edges via Windows font rendering
- **ClearType rendering** -- sub-pixel RGB anti-aliasing for LCD displays
- **Outlines** -- configurable glyph outlines rendered into the texture
- **Kerning** -- automatic extraction of kerning pairs from TrueType font data
- **Channel packing** -- each RGBA channel can hold separate glyph data, allowing up to 4 glyphs per texel
- **Multiple output formats** -- text, XML, and binary descriptor formats
- **Command-line generation** -- batch processing via `bmfont.exe -c config.bmfc -o output.fnt`
- **Texture pages** -- automatic multi-page atlas generation when glyphs exceed a single texture
- **Icon image import** -- colored images (BMP, TGA, PNG, JPG, DDS) can be imported and assigned to character slots, enabling mixed text and icon rendering (v1.9+)
- **Autofit** -- automatically calculates the maximum font size that fits within a defined texture size (v1.14+)

### Version History (Key Releases)

| Version | Date | Notable Changes |
|---------|------|-----------------|
| **1.0** | 2004/02/14 | First public version. Basic TrueType-to-bitmap conversion with text format output. |
| **1.1** | 2005/03/05 | 32-bit and 8-bit TGA save options. Manual character spacing option. |
| **1.2** | 2005/03/09 | Font smoothing options (smoothing without ClearType). |
| **1.3** | 2005/05/08 | Italic font support. |
| **1.4** | 2005/07/17 | Kerning pairs now saved. Chooseable charset support (Arabic, Hebrew). |
| **1.5** | 2005/10/01 | Non-uniform font scaling (Win2K or later). |
| **1.6** | 2006/02/18 | XML and text format options for font descriptor. |
| **1.7** | 2006/09/08 | Unicode charset support. 4-channel 32-bit texture monochrome packing. Texture file names tag in font descriptor. |
| **1.8** | 2006/11/11 | Binary font descriptor format. PNG texture support. Command-line font generation. DDS texture support. Font configuration save/reload. |
| **1.9** | 2007/08/19 | Colored icon import. Black outline support. Outline encoding in 1 channel. Binary file version incremented. |
| **1.10** | 2008/05/11 | Unicode characters above U+FFFF. DXT1/3/5 compression. Customizable texture channel content. File format updated. |
| **1.11** | 2008/10/11 | "Output invalid char glyph" option. Character height vs. line height selection. |
| **1.12** | 2009/08/02 | Export option presets. Individual texture channel inversion. Command-line tool with completion wait. |
| **1.13** | 2012/08/12 | Fixed cell height export option. Improved kerning via GPOS table. Alternative glyph rasterization from TrueType outline. Re-added ClearType rendering. Run-length encoding for TGA. Option to force offsets to zero. |
| **1.14** | 2021/01/05 | Unicode 13.0 support. 64-bit build. Source code under zlib license. Unicode Windows compilation. Autofit feature. Support for Unicode character file paths. |
| **1.14a** | 2021/04/08 | Fixed crash with auto-fitting. Included both 64-bit and 32-bit builds. |
| **1.14b** | 2025/08/16 | Fixed UI performance in ANSI mode. Fixed horizontal clipping and xadvance in ANSI mode. Fixed auto-fitter adaptive padding bug. Fixed Unicode command-line string reading. |

> **Note**: Only key releases are shown. See the [official changelog](https://www.angelcode.com/products/bmfont/) for the full list including patch releases (1.0a, 1.4a, 1.8a-c, 1.9a-c, 1.10a-b, 1.11a-b).

### How It Works

1. The user selects a TrueType (or OpenType) font and configures size, style, and character set.
2. BMFont rasterizes each selected glyph into a bitmap.
3. Glyphs are packed into one or more texture atlas images (typically PNG).
4. A descriptor file (`.fnt`) is written describing each glyph's position, size, offsets, and kerning data.
5. At runtime, game/application code parses the `.fnt` file and uses the texture atlas to render text by drawing textured quads.

---

## 2. The .FNT File Format

**Official specification**: https://www.angelcode.com/products/bmfont/doc/file_format.html

BMFont produces descriptor files in three formats:

| Format | Extension | Detection Method |
|--------|-----------|-----------------|
| **Text** | `.fnt` | First line starts with `info ` |
| **XML** | `.fnt` or `.xml` | First character is `<` (XML declaration or root element) |
| **Binary** | `.fnt` | First 3 bytes are `BMF` (0x42 0x4D 0x46) followed by version byte |

All three formats encode the same logical data: font metadata (info), common rendering parameters, texture page filenames, character glyph descriptors, and kerning pairs.

---

### Text Format

The text format uses a simple tag-value scheme. Each line starts with a tag name followed by space-separated `key=value` pairs. String values are enclosed in double quotes. Fields on a line may appear in any order. Parsers should match by key name, not position.

> **Parsing note**: The `chars count=N` and `kernings count=N` lines are informational and may be absent in files generated by third-party tools. A robust parser should not require them; instead, parse `char` and `kerning` lines until the end of file or a different tag is encountered.

#### Complete Example

```
info face="Arial" size=32 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=1,1 outline=0
common lineHeight=32 base=26 scaleW=256 scaleH=256 pages=1 packed=0 alphaChnl=0 redChnl=4 greenChnl=4 blueChnl=4
page id=0 file="arial_0.png"
chars count=3
char id=65 x=10 y=20 width=18 height=22 xoffset=1 yoffset=4 xadvance=20 page=0 chnl=15
char id=66 x=30 y=20 width=16 height=22 xoffset=2 yoffset=4 xadvance=19 page=0 chnl=15
char id=32 x=0 y=0 width=0 height=0 xoffset=0 yoffset=0 xadvance=8 page=0 chnl=15
kernings count=1
kerning first=65 second=86 amount=-2
```

> This example uses the most common configuration: glyph data in the alpha channel (`alphaChnl=0`), RGB channels set to white/one (`redChnl=4, greenChnl=4, blueChnl=4`). The renderer multiplies by text color to produce colored text.

#### Info Tag Fields

The `info` tag describes how the font was generated. This is metadata; most fields are not needed at runtime.

| Field | Type | Description |
|-------|------|-------------|
| `face` | string | The name of the TrueType font face (e.g., `"Arial"`). |
| `size` | int | The size of the TrueType font. A positive value is the point size used during generation. A negative value indicates the size was specified as an absolute pixel height (the absolute value is the height in pixels). |
| `bold` | int (0/1) | Whether the font is bold. |
| `italic` | int (0/1) | Whether the font is italic. |
| `charset` | string | The name of the OEM charset used (empty string for Unicode). |
| `unicode` | int (0/1) | Set to 1 if it is a Unicode charset. |
| `stretchH` | int | The font height stretch percentage. 100 means no stretch. |
| `smooth` | int (0/1) | Set to 1 if smoothing (anti-aliasing) was turned on. |
| `aa` | int | The supersampling level used. 1 means no supersampling. |
| `padding` | int,int,int,int | Padding added to each character (up, right, down, left) in pixels. Used for post-processing effects like outlines or drop shadows. |
| `spacing` | int,int | Spacing added between characters (horizontal, vertical) in pixels. Used to avoid texture bleeding with bilinear filtering. |
| `outline` | int | The outline thickness in pixels. |

#### Common Tag Fields

The `common` tag holds rendering parameters shared across all glyphs.

| Field | Type | Description |
|-------|------|-------------|
| `lineHeight` | int | Distance in pixels between each line of text (typically the font height). |
| `base` | int | Number of pixels from the absolute top of the line to the base of the characters. Used for baseline alignment. |
| `scaleW` | int | Width of the texture atlas in pixels. Also used to compute normalized UV coordinates: `u = x / scaleW`. |
| `scaleH` | int | Height of the texture atlas in pixels. Also used to compute normalized UV coordinates: `v = y / scaleH`. |
| `pages` | int | Number of texture pages. |
| `packed` | int (0/1) | Set to 1 if the monochrome characters have been packed into each of the texture channels. See channel packing in [Section 3](#3-texture-atlas). |
| `alphaChnl` | int | How the alpha channel is encoded. See channel values below. |
| `redChnl` | int | How the red channel is encoded. |
| `greenChnl` | int | How the green channel is encoded. |
| `blueChnl` | int | How the blue channel is encoded. |

**Channel encoding values:**

| Value | Meaning |
|-------|---------|
| 0 | Glyph data (binary: 1 inside glyph, 0 outside) |
| 1 | Outline data (binary: 1 in outline or glyph area, 0 outside) |
| 2 | Glyph + outline combined (encoded: 0 = outside, ~0.5 = outline only, 1 = glyph; threshold at 0.5 separates outline from glyph) |
| 3 | Zero (channel is always 0) |
| 4 | One (channel is always 255) |

> **Shader decoding for value 2 (glyph+outline):** When a channel uses the combined encoding, the pixel shader should use `val > 0.5` to test for glyph presence and `2*val - 1` for glyph alpha; for the outline region, use `2*val` for outline alpha. See the [official pixel shader example](https://www.angelcode.com/products/bmfont/doc/pixel_shader.html) for the reference HLSL implementation.

#### Page Tag Fields

One `page` tag per texture page.

| Field | Type | Description |
|-------|------|-------------|
| `id` | int | The page index (0-based). |
| `file` | string | The filename of the texture file for this page (e.g., `"arial_0.png"`). Path is relative to the `.fnt` file. |

#### Char Tag Fields

One `char` tag per glyph. Preceded by a `chars count=N` line (though this count line may be absent in files from third-party generators).

| Field | Type | Description |
|-------|------|-------------|
| `id` | int | The Unicode codepoint (e.g., 65 = 'A', 32 = space). For supplementary plane characters this value exceeds 65535. A value of -1 (or 0xFFFFFFFF in binary format) represents the **invalid/fallback character glyph** -- used when a requested character is not found in the font (enabled by the "output invalid char glyph" option in BMFont v1.11+). |
| `x` | int | The left position of the glyph in the texture page, in pixels. |
| `y` | int | The top position of the glyph in the texture page, in pixels. |
| `width` | int | The width of the glyph in the texture page, in pixels. |
| `height` | int | The height of the glyph in the texture page, in pixels. |
| `xoffset` | int | How much the current cursor position should be offset when copying the glyph image from the texture to the screen (horizontal). |
| `yoffset` | int | How much the current cursor position should be offset when copying the glyph image from the texture to the screen (vertical, measured from the top of `lineHeight`). |
| `xadvance` | int | How much the current cursor position should be advanced after drawing the character. |
| `page` | int | The texture page index where the glyph is found. |
| `chnl` | int | The texture channel where the glyph is found. Bitfield: 1=blue, 2=green, 4=red, 8=alpha. Value 15 means all channels (typical when `packed=0`). When channel packing is enabled (`packed=1`), each glyph uses a single channel (1, 2, 4, or 8). |

> **Whitespace characters**: Characters like space (id=32) and tab typically have `width=0` and `height=0` -- there is no texture region to draw. The renderer should skip the draw call but still advance the cursor by `xadvance`. Implementations should guard against zero-width/zero-height draw calls.

#### Kerning Tag Fields

One `kerning` tag per kerning pair. Preceded by a `kernings count=N` line.

| Field | Type | Description |
|-------|------|-------------|
| `first` | int | The Unicode codepoint of the first character. |
| `second` | int | The Unicode codepoint of the second character. |
| `amount` | int | The amount to adjust the cursor position when the second character follows the first (typically negative to tighten spacing). |

---

### XML Format

The XML format encodes identical data as the text format but in a structured XML document. All values are stored as XML attributes.

#### Equivalent XML Example

```xml
<?xml version="1.0"?>
<font>
  <info face="Arial" size="32" bold="0" italic="0" charset="" unicode="1"
        stretchH="100" smooth="1" aa="1" padding="0,0,0,0" spacing="1,1"
        outline="0"/>
  <common lineHeight="32" base="26" scaleW="256" scaleH="256" pages="1"
          packed="0" alphaChnl="0" redChnl="4" greenChnl="4" blueChnl="4"/>
  <pages>
    <page id="0" file="arial_0.png"/>
  </pages>
  <chars count="3">
    <char id="65" x="10" y="20" width="18" height="22"
          xoffset="1" yoffset="4" xadvance="20" page="0" chnl="15"/>
    <char id="66" x="30" y="20" width="16" height="22"
          xoffset="2" yoffset="4" xadvance="19" page="0" chnl="15"/>
    <char id="32" x="0" y="0" width="0" height="0"
          xoffset="0" yoffset="0" xadvance="8" page="0" chnl="15"/>
  </chars>
  <kernings count="1">
    <kerning first="65" second="86" amount="-2"/>
  </kernings>
</font>
```

Field names and semantics are identical to the text format. The root element is `<font>`. Pages are wrapped in `<pages>`, chars in `<chars>`, and kernings in `<kernings>`.

---

### Binary Format

The binary format is the most compact and fastest to parse. It begins with a 4-byte header, followed by tagged blocks. All multi-byte integer values are **little-endian** (unlike OpenType/TrueType fonts which are big-endian). In C#, `BinaryReader` on a `MemoryStream` reads little-endian by default, making the binary format straightforward to parse without byte-swapping.

#### Header

| Offset | Size | Description |
|--------|------|-------------|
| 0 | 3 bytes | Magic bytes: `BMF` (0x42 0x4D 0x46) |
| 3 | 1 byte | Version: `3` (current format version) |

**Binary format version history:**

| Version | App Version | Changes |
|---------|-------------|---------|
| 1 | 1.8 | Initial binary format. |
| 2 | 1.9 | Added `outline` field to info block. Added `encoded` field. |
| 3 | 1.10 | Removed `encoded` field. Added `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` to common block. Block size field now reports the size of the block data only (excluding the 1-byte type and 4-byte size fields themselves). Increased character ID from 2 bytes to 4 bytes (supporting codepoints above U+FFFF). Kerning pair first/second also increased from 2 bytes to 4 bytes. |

#### Block Structure

Each block starts with:

| Offset | Size | Description |
|--------|------|-------------|
| 0 | 1 byte | Block type ID (1-5) |
| 1 | 4 bytes | Block size in bytes (uint32, little-endian), excluding the type and size fields |
| 5 | N bytes | Block data |

Blocks appear in order (1, 2, 3, 4, 5), but a robust parser should handle them by type ID rather than assuming order. Block 5 (kerning) is optional. A parser should read blocks in a loop until EOF, dispatching on the type ID.

#### Block 1: Info (type = 1)

| Offset | Size | Type | Field |
|--------|------|------|-------|
| 0 | 2 | int16 | fontSize (negative value = absolute pixel height, positive = point size) |
| 2 | 1 | uint8 | bitField (bit 0: smooth, bit 1: unicode, bit 2: italic, bit 3: bold, bit 4: fixedHeight -- set when cell height equalization is enabled, bits 5-7: reserved). Extract as: `smooth = bits & 1`, `unicode = (bits >> 1) & 1`, `italic = (bits >> 2) & 1`, `bold = (bits >> 3) & 1`, `fixedHeight = (bits >> 4) & 1` |
| 3 | 1 | uint8 | charSet |
| 4 | 2 | uint16 | stretchH |
| 6 | 1 | uint8 | aa |
| 7 | 1 | uint8 | paddingUp |
| 8 | 1 | uint8 | paddingRight |
| 9 | 1 | uint8 | paddingDown |
| 10 | 1 | uint8 | paddingLeft |
| 11 | 1 | uint8 | spacingHoriz |
| 12 | 1 | uint8 | spacingVert |
| 13 | 1 | uint8 | outline |
| 14 | N | string | fontName (null-terminated) |

#### Block 2: Common (type = 2, 15 bytes)

| Offset | Size | Type | Field |
|--------|------|------|-------|
| 0 | 2 | uint16 | lineHeight |
| 2 | 2 | uint16 | base |
| 4 | 2 | uint16 | scaleW |
| 6 | 2 | uint16 | scaleH |
| 8 | 2 | uint16 | pages |
| 10 | 1 | uint8 | bitField (bits 0-6: reserved, bit 7: packed). To extract: `packed = (bitField >> 7) & 1` |
| 11 | 1 | uint8 | alphaChnl |
| 12 | 1 | uint8 | redChnl |
| 13 | 1 | uint8 | greenChnl |
| 14 | 1 | uint8 | blueChnl |

#### Block 3: Pages (type = 3)

Contains `pages` number of null-terminated strings, packed consecutively. Each string is the filename of a texture page.

The length of each string (including the null terminator) is: `blockSize / pages`.

Example for 2 pages: `"font_0.png\0font_1.png\0"`

> **C# parsing tip**: To extract page names, read the entire block as a byte array, then split on null bytes (0x00). In C#: `Encoding.UTF8.GetString(blockData).Split('\0', StringSplitOptions.RemoveEmptyEntries)`. Alternatively, compute the stride as `blockSize / pages` and read each fixed-length slot.

#### Block 4: Chars (type = 4, 20 bytes per char)

The number of characters is `blockSize / 20`.

| Offset | Size | Type | Field |
|--------|------|------|-------|
| 0 | 4 | uint32 | id (Unicode codepoint) |
| 4 | 2 | uint16 | x |
| 6 | 2 | uint16 | y |
| 8 | 2 | uint16 | width |
| 10 | 2 | uint16 | height |
| 12 | 2 | int16 | xoffset |
| 14 | 2 | int16 | yoffset |
| 16 | 2 | int16 | xadvance |
| 18 | 1 | uint8 | page |
| 19 | 1 | uint8 | chnl |

#### Block 5: Kerning Pairs (type = 5, 10 bytes per pair)

The number of kerning pairs is `blockSize / 10`. This block is optional and may be absent if there are no kerning pairs.

| Offset | Size | Type | Field |
|--------|------|------|-------|
| 0 | 4 | uint32 | first (Unicode codepoint) |
| 4 | 4 | uint32 | second (Unicode codepoint) |
| 8 | 2 | int16 | amount |

---

## 3. Texture Atlas

**Reference**: https://www.angelcode.com/products/bmfont/doc/export_options.html

The texture atlas is one or more images (typically PNG, but also TGA or DDS) containing all rasterized glyphs arranged in a packed layout.

### Glyph Packing

BMFont uses a bin-packing algorithm to arrange glyphs efficiently into rectangular texture pages. Glyphs are sorted by height (tallest first) and placed left-to-right, top-to-bottom. When a glyph does not fit on the current row, a new row is started. When the page is full, a new texture page is created.

### Texture Dimensions

- **Power of 2 recommended**: Textures with power-of-2 dimensions (64, 128, 256, 512, 1024, 2048, 4096) are preferred for compatibility with older GPUs and to avoid NPOT texture penalties.
- `scaleW` and `scaleH` in the `.fnt` file specify the texture dimensions.
- BMFont allows non-power-of-2 dimensions, but some renderers may not support them efficiently.

### Supported Texture File Formats

| Format | Notes |
|--------|-------|
| **PNG** | Most common. Lossless compression. Supports 8-bit and 32-bit. |
| **TGA** | Targa format. Supports 8-bit and 32-bit. Run-length encoding available (v1.13+). |
| **DDS** | DirectDraw Surface. Supports uncompressed and DXT1/DXT3/DXT5 compression (v1.10+). |

### Bit Depth: 8-bit vs 32-bit Textures

| Bit Depth | Format | Use Case |
|-----------|--------|----------|
| **8-bit** | Single-channel alpha map | Smallest file size. Glyph data stored as grayscale intensity. Requires the renderer to apply color at draw time. |
| **32-bit** | RGBA | Supports colored outlines, pre-colored glyphs, channel packing, and colored icon images. Larger file size. |

### Channel Packing

When `packed=1` in the `common` tag, BMFont uses each RGBA channel to store separate glyph data. This allows packing up to 4 monochrome glyphs per texel, reducing texture memory by 75%.

Each character's `chnl` field indicates which channel(s) hold its data:

| `chnl` value | Channel(s) | Bit Interpretation |
|--------------|------------|-------------------|
| 1 | Blue only | Bit 0 set |
| 2 | Green only | Bit 1 set |
| 4 | Red only | Bit 2 set |
| 8 | Alpha only | Bit 3 set |
| 15 | All channels | All bits set (typical for non-packed textures) |

The `alphaChnl`, `redChnl`, `greenChnl`, and `blueChnl` fields in the `common` tag describe what type of data is stored in each channel:

### Common Channel Configurations (Export Presets)

These correspond to the preset configurations available in BMFont's export options dialog:

| Configuration | Bit Depth | alphaChnl | redChnl | greenChnl | blueChnl | Description |
|---------------|-----------|-----------|---------|-----------|----------|-------------|
| **White glyphs, no outline** | 32-bit | 0 | 4 | 4 | 4 | Glyph in alpha, RGB = white (255). Most common setup. Renderer multiplies by text color. |
| **White glyphs with outline** | 32-bit | 1 | 0 | 0 | 0 | Outline in alpha, glyph in RGB. Two-pass render: first draw outline using alpha for transparency, then draw glyph using RGB for transparency. |
| **Packed 8-bit, no outline** | 8-bit | 0 | 3 | 3 | 3 | Glyph data in alpha only. RGB channels are zero (unused). |
| **Packed 8-bit with outline** | 8-bit | 2 | 3 | 3 | 3 | Glyph+outline combined in alpha (using 0.5 threshold encoding). RGB channels are zero. |
| **4-char packed** | 32-bit | 0 | 0 | 0 | 0 | `packed=1`. Each RGBA channel holds a different glyph. Each `char` entry has a `chnl` value of 1, 2, 4, or 8. Requires custom pixel shader. Reduces texture memory by 75%. |

> **Note on "force offsets to zero" export option**: BMFont has an option to force `xoffset` and `yoffset` to 0 and `xadvance` to equal `width`, for renderers that lack offset support. This also enables cell height equalization. May alter spacing if the font uses negative offsets.

### Multi-Page Support

When the glyph set is too large for a single texture, BMFont generates multiple texture pages. Each page is a separate PNG file (e.g., `font_0.png`, `font_1.png`). The `page` field on each `char` entry indicates which texture page contains the glyph.

### Padding and Spacing

These are separate concepts that are commonly confused:

| Concept | Configured In | Purpose | Affects Layout |
|---------|---------------|---------|----------------|
| **Padding** | `info` tag: `padding=up,right,down,left` | Adds empty space *around each glyph in the texture*. This extra space provides room for post-processing effects like outlines, drop shadows, or glow that extend beyond the glyph's natural bounds. | Increases the `width` and `height` of each glyph region in the texture. The `xoffset` and `yoffset` values compensate so rendering position stays correct. |
| **Spacing** | `info` tag: `spacing=horiz,vert` | Adds empty space *between glyphs in the texture atlas*. Prevents texture bleeding artifacts when bilinear/trilinear filtering samples neighboring glyph pixels. | Does not affect glyph metrics or rendering -- only the atlas packing layout. |

**Rule of thumb**: Use padding >= outline thickness. Use spacing >= 1 when bilinear filtering is enabled.

### Font Size Sign Convention (.bmfc `fontSize`)

The `fontSize` field in `.bmfc` config files and the `fontSize` field in the `.fnt` info block use a sign convention:

| Value | Meaning | Example |
|-------|---------|---------|
| **Positive** (e.g., `fontSize=56`) | Size in **points**. Actual pixel size depends on DPI: `pixels = points * DPI / 72`. | `fontSize=56` at 96 DPI = 74.67px |
| **Negative** (e.g., `fontSize=-56`) | Size in **pixels** (absolute pixel height). Maps to BMFont's "Match char height" checkbox. | `fontSize=-56` = exactly 56px regardless of DPI |

In BMFont's UI, the "Match char height" option corresponds to negative `fontSize`. When enabled, the font is sized so that the character height matches the specified pixel value exactly.

> **KernSmith mapping**: `FontGeneratorOptions.MatchCharHeight = true` corresponds to negative `fontSize` in `.bmfc` files. `BmfcConfigReader` already parses this sign convention.

### Autofit and Adaptive Padding

BMFont (v1.14+) supports an **autofit** feature that calculates the maximum font size that fits within a defined texture size. When autofit is active, **adaptive padding** scales padding proportionally to font size, calculated as: `average character width * padding factor + standard padding`. This ensures padding remains appropriate as font size changes.

#### autoFitNumPages

The `autoFitNumPages` setting in `.bmfc` config files controls the autofit behavior:

| Value | Behavior |
|-------|----------|
| `autoFitNumPages=0` | **No auto-fitting.** The `fontSize` value is used as-is. This is the default. |
| `autoFitNumPages=1` | **Auto-fit to 1 page.** BMFont searches for the **maximum** font size that fits all selected characters into a single texture page. |
| `autoFitNumPages=N` | **Auto-fit to N pages.** Same as above but allows up to N texture pages. |

When autofit is active, BMFont **scales the font size UP** (not down) -- it finds the largest size that fits within the texture constraints. The `fontSize` value in the config serves as a starting point or minimum, not the final rendered size.

Related `.bmfc` settings that constrain the autofit search:

| Setting | Purpose |
|---------|---------|
| `autoFitFontSizeMin` | Minimum font size the auto-fitter will consider |
| `autoFitFontSizeMax` | Maximum font size the auto-fitter will consider |
| `autoFitMarginBottom` | Bottom margin reserved during auto-fit calculations |
| `autoFitMarginTop` | Top margin reserved during auto-fit calculations |
| `autoFitMarginLeft` | Left margin reserved during auto-fit calculations |
| `autoFitMarginRight` | Right margin reserved during auto-fit calculations |

> **Warning -- KernSmith vs BMFont autofit**: KernSmith's `AutofitTexture` option is NOT equivalent to BMFont's `autoFitNumPages`. They are essentially opposite operations:
> - **BMFont `autoFitNumPages`**: Grows the **font size** to fill a fixed-size texture.
> - **KernSmith `AutofitTexture`**: Shrinks the **texture** to fit glyphs at a fixed font size.
>
> A `.bmfc` config with `autoFitNumPages=1` and `fontSize=56` will NOT produce a size-56 font -- BMFont will search for the largest size that fits, potentially much larger than 56. To get an exact size-56 font from BMFont, set `autoFitNumPages=0`.

### Cell Height Equalization

When the "equalize cell heights" option is enabled, all characters are drawn in uniform-height cells. This simplifies gradient or height-based post-processing but reduces the number of characters that fit per texture and increases the size of drawn rectangles at runtime.

---

## 4. Text Rendering Algorithm

**Reference**: https://www.angelcode.com/products/bmfont/doc/render_text.html

### Core Rendering Loop

The fundamental algorithm for rendering bitmap font text:

```
cursor_x = start_x
cursor_y = start_y

for each character in text:
    if character is newline:
        cursor_y += lineHeight
        cursor_x = start_x
        continue

    glyph = lookup(character)
    if glyph is not found:
        continue  // or substitute a fallback glyph

    // Draw textured quad from atlas (skip for whitespace/zero-size glyphs)
    if glyph.width > 0 and glyph.height > 0:
        draw_x = cursor_x + glyph.xoffset
        draw_y = cursor_y + glyph.yoffset
        draw_texture_region(
            texture = pages[glyph.page],
            source  = rectangle(glyph.x, glyph.y, glyph.width, glyph.height),
            dest    = point(draw_x, draw_y)
        )

    // Apply kerning with the NEXT character (if any)
    if next_character exists:
        kerning = lookup_kerning(character, next_character)
        cursor_x += kerning

    // Advance cursor
    cursor_x += glyph.xadvance
```

### Positioning Concepts

Understanding these fields is essential for correct rendering:

```
    cursor_x
    |
    |   xoffset
    |   |<->|
    |       +--------+  ---                              ---
    |       | glyph  |   |                                |
    |       | image  |   | height                         |
    |       |        |   |                                |
    |       +--------+  ---                               | lineHeight
    |       |<------>|                                    | (vertical distance
    |         width                                      |  between lines)
    |                                                     |
    |   base  ----------------------------------------    |
    |                                                     |
    +----------------------------------------------------+---
    |<--xadvance--->|
                    ^ next character starts here (cursor_x += xadvance + kerning)

    yoffset = distance from cursor_y (top of line) to top of glyph image
    base    = distance from cursor_y (top of line) to baseline
```

- **`lineHeight`**: The vertical distance from one line's top to the next line's top. This is the total line advance, not just the glyph height.
- **`base`**: The distance from the top of `lineHeight` to the font baseline. Used for aligning fonts of different sizes or mixing bitmap fonts with vector fonts.
- **`yoffset`**: The vertical distance from the top of `lineHeight` to the top of this glyph's image. Tall characters like `|` have small yoffset; short characters like `.` have large yoffset. Can be negative if the glyph extends above the line (e.g., some accented characters or characters with outline/padding).
- **`xoffset`**: The horizontal distance from the cursor to where this glyph's image should be drawn. Can be negative (e.g., italic characters that lean left of the cursor).
- **`xadvance`**: How much to advance the cursor after rendering this character. This is the "logical width" of the character and does not necessarily equal `xoffset + width`.

### Kerning

Kerning adjusts the spacing between specific character pairs for better visual appearance. For example, the pair "AV" typically has a negative kerning value (e.g., -2), pulling the "V" closer to the "A".

**How kerning interacts with xadvance**: After drawing a character, the cursor advances by `xadvance + kerning_amount`. The kerning amount is looked up using the *current* character as `first` and the *next* character as `second`. Both values are combined into a single cursor advance:

```
// Effective cursor advance for character at position i:
advance = glyph[i].xadvance + kerning(glyph[i].id, glyph[i+1].id)
cursor_x += advance
```

The kerning `amount` is typically negative (tightening the space between characters) but can be positive (loosening). It modifies the `xadvance` of the first character in the pair -- it does **not** affect the `xoffset` of the second character.

**Example**: Given "AV" where A has `xadvance=20` and kerning(A,V)=-2, the cursor advances by `20 + (-2) = 18` pixels after drawing A, placing V 2 pixels closer than it would be without kerning.

Typical implementation stores kerning as `Dictionary<(int first, int second), int>` or a nested dictionary `Dictionary<int, Dictionary<int, int>>`. The nested form is faster for the common access pattern: given a character, look up all kerning adjustments for possible next characters.

### Outline Rendering (Two-Pass Approach)

When a font includes outlines (stored in separate channels), text is rendered in two passes:

```
// Pass 1: Draw outlines (border)
for each character in text:
    // Use the alpha channel as transparency, apply outline color
    draw glyph using the alpha channel data with outline color

// Pass 2: Draw glyphs (interior) over outlines
for each character in text:
    // Use the RGB color channels as transparency, apply text color
    draw glyph using the RGB channel data with text color
```

Per the official documentation: the **first pass** uses "only the alpha channel of the font texture as the transparency value when rendering the border." The **second pass** uses "only the color channels of the font texture as the transparency value to render the characters over the border." This allows "different colors to the border and the internal characters dynamically" by multiplying the transparency value with the desired colors before blending.

The two-pass approach ensures outlines appear behind glyphs even where adjacent characters overlap.

> **Important**: The `xoffset` and `yoffset` values should NOT be used to move the cursor position -- they only offset where the glyph image is drawn relative to the cursor. The cursor advances solely by `xadvance` (plus any kerning adjustment).

**Alternative: Single-Pass Pixel Shader Approach**

When outlines are encoded in a single channel using the combined glyph+outline encoding (channel value 2), a pixel shader can decode both in one pass:

```hlsl
// From the official BMFont pixel shader reference
float4 PixScene(float4 color : COLOR0, int4 chnl : TEXCOORD1, float2 tex0 : TEXCOORD0) : COLOR0
{
    float4 pixel = tex2D(g_samScene, tex0);
    if (dot(vector(1,1,1,1), chnl))
    {
        float val = dot(pixel, chnl);
        pixel.rgb = val > 0.5 ? 2*val-1 : 0;     // glyph alpha
        pixel.a   = val > 0.5 ? 1 : 2*val;        // outline alpha
    }
    return pixel * color;
}
```

The `chnl` vector selects which texture channel to read (e.g., `(0,0,0,1)` for alpha). When `chnl` is `(0,0,0,0)`, the character is a full 32-bit colored image (e.g., an imported icon) and is drawn as-is.

### Text Measurement

To measure the dimensions of a text string without rendering:

```
width calculation:
    line_width = 0
    max_width = 0
    for each character in text:
        if newline:
            max_width = max(max_width, line_width)
            line_width = 0
            continue
        glyph = lookup(character)
        line_width += glyph.xadvance
        if next_character exists:
            line_width += lookup_kerning(character, next_character)
    max_width = max(max_width, line_width)
    return max_width

height calculation:
    lines = count_newlines(text) + 1
    return lineHeight * lines
    // Note: last line might use a tighter metric:
    // (lines - 1) * lineHeight + max(yoffset + height) for chars on last line
```

**Notes on measurement accuracy**:
- **Width, last character**: For the last character on a line, the visual extent may differ from the advance. The actual pixel width is `max(xadvance, xoffset + width)`. Advance-based measurement (shown above) is simpler and standard; tight bounding box measurement requires special handling of the last character: `line_width - last_glyph.xadvance + max(last_glyph.xadvance, last_glyph.xoffset + last_glyph.width)`.
- **Height, last line**: The actual rendered height extends from `yoffset` to `yoffset + height` of the tallest glyph, which may be less than `lineHeight`. Some implementations use the full `lineHeight` for simplicity; others calculate a tighter bounding box.

---

## 5. MonoGame.Extended Implementation

- **Repository**: https://github.com/craftworkgames/MonoGame.Extended
- **Source path**: https://github.com/MonoGame-Extended/Monogame-Extended/tree/develop/source/MonoGame.Extended/BitmapFonts

### Data Model

```
BitmapFont
  +-- Name: string (Face)
  +-- Size: int
  +-- LineHeight: int
  +-- LetterSpacing: int          (additional spacing added to xadvance)
  +-- UseKernings: bool           (default: true; set to false to disable kerning)
  +-- Characters: Dictionary<int, BitmapFontCharacter>
  +-- Textures: Texture2D[]
  +-- GetCharacter(int id): BitmapFontCharacter
  +-- TryGetCharacter(int id, out BitmapFontCharacter): bool
  +-- MeasureString(string text): Size2
  +-- MeasureString(StringBuilder text): Size2
  +-- GetGlyphs(string text, ...): IEnumerable<BitmapFontGlyph>

BitmapFontCharacter
  +-- Character: int (Unicode codepoint)
  +-- TextureRegion: Texture2DRegion (reference to atlas sub-rectangle)
  +-- XOffset: int
  +-- YOffset: int
  +-- XAdvance: int
  +-- Kernings: Dictionary<int, int>  (keyed by the FOLLOWING character id)
```

### Key Design Decisions

| Aspect | Approach |
|--------|----------|
| **Character lookup** | `Dictionary<int, BitmapFontCharacter>` -- supports full Unicode range including supplementary plane characters |
| **Kerning lookup** | Stored per-character: each `BitmapFontCharacter` has its own `Dictionary<int, int>` mapping the *next* character ID to the kerning amount |
| **Texture regions** | Uses `Texture2DRegion` abstraction (shared with sprite atlas system) |
| **Rendering** | Extension methods on `SpriteBatch`: `spriteBatch.DrawString(font, text, position, color)` |
| **GC pressure** | Struct-based glyph enumerator avoids heap allocations during text iteration |
| **Surrogate pairs** | Full support -- the enumerator correctly handles UTF-16 surrogate pairs, yielding a single codepoint (int) for supplementary plane characters |
| **API surface** | Dual `string` / `StringBuilder` overloads for all measurement and rendering methods, so callers building dynamic text can avoid string allocations |

### Rendering Flow

1. `DrawString` is called with font, text, position, color.
2. A struct enumerator iterates the string, decoding surrogate pairs to int codepoints.
3. For each codepoint, the `BitmapFontCharacter` is looked up in the dictionary.
4. Kerning is checked against the next codepoint.
5. `SpriteBatch.Draw` is called with the character's `TextureRegion`, calculated position (cursor + offset), and color.
6. The cursor advances by `xadvance + kerning`.

---

## 6. Gum UI Implementation

- **Repository**: https://github.com/vchelaru/Gum
- **Key source files**:
  - `RenderingLibrary/Graphics/Fonts/BitmapFont.cs` -- main font class and rendering
  - `RenderingLibrary/Graphics/Fonts/ParsedFontFile.cs` -- .fnt file parser
  - `RenderingLibrary/Graphics/Fonts/BmfcSave.cs` -- .bmfc configuration file generator

### Distinctive Approach: BMFont Executable Wrapper

Gum does not just *consume* BMFont files -- it wraps the BMFont executable itself for font generation as part of its tooling pipeline:

1. **Scan project**: Identifies all font usages (face, size, outline, etc.) across the Gum UI project.
2. **Estimate texture size**: Calculates required texture dimensions based on character set and font size.
3. **Generate .bmfc config**: Writes a BMFont configuration file (`BmfcSave.cs`) with the appropriate settings.
4. **Invoke bmfont.exe**: Runs `bmfont.exe -c config.bmfc -o output.fnt` to generate the atlas and descriptor.

### Parser Details

- `ParsedFontFile` supports **text**, **XML**, and detects **binary** format.
- **Binary format is detected but unsupported** -- the parser recognizes the `BMF` magic bytes but throws an exception: `"Unable to load Binary Font files, please convert to XML or TEXT."`
- Format detection: `<` = XML, `B` (0x42) = binary, `i` = text (starts with `info`).
- XML parsing uses .NET `XmlSerializer` for deserialization. Text parsing uses line-by-line tokenization via `ParsedFontLine.Parse()`.
- Parsing extracts data into flat arrays/dictionaries; does not directly mirror the tag structure.

### Data Model

```
BitmapFont
  +-- BitmapCharacterInfo[] (indexed by character id)
  +-- LineHeightInPixels: int
  +-- Textures: Texture2D[]
  +-- RenderAtPosition(string text, float x, float y, ...)

BitmapCharacterInfo
  +-- TULeft, TVTop: float     (normalized UV, 0.0-1.0, for texture sampling)
  +-- TURight, TVBottom: float
  +-- ScaleWidth, ScaleHeight: float
  +-- Spacing: float           (xadvance)
  +-- XOffset, YOffset: float
  +-- PageNumber: int
  +-- SecondLetterKearning: Dictionary<int, int>  (kerning pairs for this character)
```

### Notable Features

| Feature | Details |
|---------|---------|
| **Normalized UVs** | `BitmapCharacterInfo` pre-computes normalized texture coordinates (0.0-1.0) rather than storing pixel coordinates, eliminating per-frame division by texture size. |
| **Dynamic space generation** | If the space character is missing from the font data, Gum synthesizes one with a width derived from other characters. |
| **Tab handling** | Tab characters are rendered as multiple spaces (configurable tab width). |
| **Monospace digits** | Option to render all digits (0-9) with the same advance width, regardless of the font's native glyph widths. |
| **Static buffer reuse** | Uses static `StringBuilder` and array buffers for text measurement and rendering to minimize GC allocations. |
| **Measurement styles** | `HorizontalMeasurementStyle` controls whether final character width uses actual texture bounds or advance value, enabling both tight bounding box and advance-based measurement. |
| **Outline compensation** | Rendering adjusts glyph positioning by `mOutlineThickness` to compensate for outline padding in the texture atlas. |

---

## 7. libGDX Implementation

- **Reference**: https://libgdx.com/wiki/graphics/2d/fonts/bitmap-fonts
- **Repository**: https://github.com/libgdx/libgdx
- **Key classes**: `BitmapFont`, `BitmapFontData`, `BitmapFontCache`, `Glyph`

libGDX is the most widely used BMFont implementation in the Java/Android ecosystem.

### Parser

`BitmapFontData.load()` reads the **text format** only. It does line-by-line parsing using simple string splitting (not regex). It handles:
- Standard `info`, `common`, `page`, `char`, `kerning` tags
- Flipped vs. unflipped Y-axis coordinate systems
- Missing glyphs with fallback behavior

### Glyph Lookup: Paged Array

libGDX uses a **paged array** for O(1) glyph lookup instead of a hash map:

```java
Glyph[][] glyphs = new Glyph[256][];  // 256 "pages"
// Each page holds up to 512 entries

// Lookup:
Glyph getGlyph(int codepoint) {
    int pageIndex = codepoint / 512;       // which page
    int glyphIndex = codepoint % 512;      // index within page
    if (pageIndex >= glyphs.length) return null;
    Glyph[] page = glyphs[pageIndex];
    if (page == null) return null;
    return page[glyphIndex];
}
```

This design supports codepoints up to 256 * 512 = 131,072, covering the entire Basic Multilingual Plane and part of the supplementary planes. Pages are allocated lazily (only when a glyph exists in that range), so memory is proportional to the actual character set, not the theoretical maximum.

### Glyph Class

```java
class Glyph {
    int id;
    int srcX, srcY, width, height;   // texture atlas source rectangle
    float u, v, u2, v2;              // pre-computed normalized UVs
    int xoffset, yoffset, xadvance;
    int page;                         // texture page
    byte[][] kerning;                 // paged kerning array (same structure)
    boolean fixedWidth;
}
```

### BitmapFontCache

For **static text** (text that does not change every frame), `BitmapFontCache` pre-computes all glyph positions and vertex data. This avoids re-iterating the string and re-calculating layout every frame.

```java
BitmapFontCache cache = new BitmapFontCache(font);
cache.setText("Score: 1000", x, y);
// Later, in render loop:
cache.draw(batch);  // draws pre-computed vertices, no layout calculation
```

When the text changes, `cache.setText()` is called again. This pattern is ideal for UI elements like score displays, labels, and dialog text.

### Integer Positioning

libGDX offers an `integer` flag that snaps glyph positions to integer pixel coordinates. This prevents sub-pixel positioning which can cause blurry text due to bilinear texture filtering. Enabled by default:

```java
font.setUseIntegerPositions(true);  // default
```

### Fixed-Width Glyphs

libGDX provides a `setFixedWidthGlyphs()` method that forces specified characters to use the same advance width. This is useful for rendering monospaced digits in score displays or tables where alignment matters. All glyphs in the specified string are set to the width of the widest glyph.

### Runtime Font Generation (FreeTypeFontGenerator)

In addition to loading pre-generated BMFont files, libGDX provides `FreeTypeFontGenerator` which generates bitmap fonts from TrueType files at runtime. This is particularly useful for:

- Supporting international character sets (e.g., Korean, Cyrillic) where pre-generating all glyphs would create enormous textures
- Dynamically adjusting font size to screen resolution
- User-selectable fonts

The generated fonts use the same `BitmapFont` class and rendering path as pre-generated BMFont files.

### Distance Field Fonts

libGDX recommends distance field fonts for "scaling/rotating fonts without ugly artifacts." This requires a different shader but provides resolution-independent rendering, making the same font atlas usable at multiple sizes without regeneration.

### Y-Axis Flipping

A notable detail: libGDX supports both flipped and unflipped Y-axis coordinate systems. The `BitmapFontData.load()` method handles this during parsing, adjusting `yoffset` values accordingly. This is relevant because libGDX's default coordinate system has Y pointing up (OpenGL convention), while BMFont generates data with Y pointing down (screen convention).

---

## 8. Other Implementations and Tools

### Engine/Framework Implementations

| Engine/Framework | Language | Notes |
|-----------------|----------|-------|
| **Cocos2d-x** | C++ | Built-in `Label` class with BMFont support. Uses text format parser. Widely used in mobile games. |
| **LOVE** | Lua | `love.graphics.newFont()` accepts BMFont files natively. Simple API: `love.graphics.print(text, font, x, y)`. |
| **Phaser** | JavaScript | `Phaser.GameObjects.BitmapText` with web-optimized rendering. Supports retro-style and smooth bitmap fonts. |
| **Unity** | C# | No built-in support; third-party assets like **TextMesh Pro** (now integrated) use a similar but incompatible SDF-based format. BMFont import is available via community packages. |
| **Godot** | GDScript/C++ | Built-in `BitmapFont` resource type. Import pipeline converts `.fnt` files to Godot's native resource format. Supports text and XML formats. |
| **Raylib** | C | `LoadBMFont()` function. Minimalist implementation focused on the text format. Part of the rtext module. |
| **pygame** | Python | Community library `pygame-bmfont` provides BMFont loading. Not part of core pygame. |

### npm: load-bmfont

- **Package**: https://www.npmjs.com/package/load-bmfont
- Supports all 4 descriptor formats: **text**, **XML**, **binary**, and **JSON** (a non-standard extension)
- Node.js-based, commonly used with three.js and other WebGL frameworks
- Handles file loading and parsing into a normalized JavaScript object

### Tools That Generate BMFont-Compatible Files

| Tool | Platform | License | Description |
|------|----------|---------|-------------|
| **[Littera](http://kvazars.com/littera/)** | Web (browser) | Free | Web-based bitmap font generator. Drag-and-drop TTF upload, visual character selection, export to BMFont text format + PNG. |
| **[Glyph Designer](https://www.71squared.com/glyphdesigner)** | macOS | Paid | Professional bitmap font tool for Mac. Rich effects (gradients, shadows, outlines). Exports BMFont format among others. |
| **[ShoeBox](https://renderhjs.net/shoebox/)** | Cross-platform | Free | Adobe AIR-based tool with bitmap font generation as one of many sprite/atlas features. |
| **[Hiero](https://github.com/libgdx/libgdx/wiki/Hiero)** | Cross-platform (Java) | Open source | Part of the libGDX toolset. Java-based GUI for generating bitmap fonts. Exports BMFont text format. Supports effects like shadows, outlines, and distance field generation. |
| **[GlyphCombiner](http://www.binaryblobs.com/)** | macOS | Free | Mac OS X tool by Binary Blobs for combining and editing bitmap font atlases. |

---

## 9. Tutorials & Community Resources

### Official Documentation Pages

| Resource | Description |
|----------|-------------|
| [BMFont Homepage](https://www.angelcode.com/products/bmfont/) | Official homepage with download, documentation, and changelog. |
| [Font Settings](https://www.angelcode.com/products/bmfont/doc/font_settings.html) | Configuring font parameters. |
| [Export Options](https://www.angelcode.com/products/bmfont/doc/export_options.html) | Texture format, channel encoding, and export settings. |
| [File Format](https://www.angelcode.com/products/bmfont/doc/file_format.html) | Complete .fnt file format specification (text, XML, binary). |
| [Render Text](https://www.angelcode.com/products/bmfont/doc/render_text.html) | Official rendering algorithm description. |
| [Pixel Shader](https://www.angelcode.com/products/bmfont/doc/pixel_shader.html) | HLSL pixel shader for channel-packed and outline fonts. |
| [Command Line](https://www.angelcode.com/products/bmfont/doc/command_line.html) | Command-line usage reference. |
| [Known Issues](https://www.angelcode.com/products/bmfont/doc/known_issues.html) | Recognized limitations and bugs. |

### Tutorials & Community Resources

The following resources are listed on or referenced from the official BMFont page:

| Resource | Author | Description |
|----------|--------|-------------|
| [Bitmap Fonts](https://www.angelcode.com/dev/bmfonts) | Andreas Jonsson | The author's article on bitmap font concepts. |
| [Bitmap Fonts tutorial](http://www.chadvernon.com/blog/tutorials/managed-directx-2/bitmap-fonts/) | Chad Vernon | Tutorial on loading and rendering BMFont files in Managed DirectX. |
| [Quick tutorial: Variable width bitmap fonts](http://www.gamedev.net/community/forums/topic.asp?topic_id=330742) | Promit | GameDev.net forum post explaining variable-width bitmap fonts and basic renderer implementation. |
| [BMFont OpenGL Implementation](http://sourceforge.net/projects/oglbmfont) | legolas558 | SourceForge project implementing a BMFont renderer in OpenGL. |
| [bmfont BlitzMax module](http://www.wieringsoftware.nl/blitz/) | Mike Wiering | BlitzMax language module for loading and rendering BMFont files. |
| [C# XML serializer for font loading](http://pastebin.com/x3Z2mDC6) | DeadlyDan | Pastebin showing C# XML serialization attributes to deserialize BMFont XML format directly into C# objects. |
| [C# XML BMFont reader](https://github.com/ironpowertga/BMFont-Reader-Csharp) | Antoine Guilbaud | GitHub repo implementing a C# BMFont XML reader with rendering support. |
| [BMFont to C source code converter](http://larsee.dk/converting-fonts-to-c-source-using-bmfont2c/) | Lars Ole Pontoppidan | Tool that converts BMFont output into C source code for embedded systems. |
| [Discussion on legality of bitmap fonts](http://gamedev.stackexchange.com/questions/35455/legality-of-angelcodes-bmfont) | (community) | GameDev StackExchange discussion on legal aspects of distributing bitmap fonts from commercial TrueType fonts. |
| [Converting bitmap fonts into distance fields](http://bitsquid.blogspot.com/2010/04/distance-field-based-rendering-of.html) | Bitsquid | Blog post on distance-field-based rendering of vector textures/fonts. |
| [Another tool for converting bitmap fonts into distance fields](https://www.gamedev.net/topic/491938-signed-distance-bitmap-font-tool/) | (community) | GameDev.net thread about a signed distance bitmap font tool. |
| [PlayOnLinux](https://www.playonlinux.com/en/) | (community) | Wine-based tool for running the Windows-only BMFont application on Linux. |
| [GlyphCombiner](http://www.binaryblobs.com/GlyphCombiner/) | Binary Blobs | Mac OS X tool for combining, editing, and managing bitmap font atlases. |
| [Rust bmfont descriptor parser](https://github.com/shampoofactory/bmfont_rs) | shampoofactory | Rust library for parsing BMFont descriptor files. Supports text, XML, and binary formats. |
| [Adding colored outline and drop shadow in GIMP](https://www.gamedev.net/forums/topic/714335-heres-how-to-create-a-25px-outline-drop-shadow-moved-x25-y25/) | rukh | Tutorial on post-processing BMFont texture atlases in GIMP to add outlines and drop shadows. |

---

## 10. Limitations and Pain Points

### Format Limitations

| Limitation | Detail |
|------------|--------|
| **Loosely specified text format** | The text format has no formal grammar. Different parsers handle edge cases differently (e.g., extra whitespace, missing fields, field ordering). This leads to subtle incompatibilities between implementations. |
| **No SDF support** | The format has no provision for signed distance field data. SDF was planned but never added. SDF fonts require separate tooling (e.g., msdf-atlas-gen) and a different rendering shader. |
| **Windows-only generation tool** | The official BMFont application is Windows-native. Linux/macOS users must use alternatives (Hiero, Littera, ShoeBox) or run BMFont via Wine/PlayOnLinux, which can be unreliable. |
| **No runtime generation** | BMFont is an offline tool only. There is no library or API for generating bitmap fonts at runtime (e.g., for user-entered text in arbitrary languages). |
| **No color emoji / multi-color glyphs** | Each glyph is monochrome (single-channel alpha or grayscale). Multi-color glyphs, color emoji, and layered OpenType features are not supported. |
| **Large Unicode ranges produce huge textures** | Including CJK characters or other large Unicode blocks can produce textures exceeding 4096x4096, which some GPUs cannot handle. |
| **Pairs-only kerning** | Only pair-based kerning is supported (first + second character). Modern OpenType GPOS-style contextual kerning, mark positioning, and other advanced features are not available. |

### Common Implementation Struggles

| Problem | Explanation |
|---------|-------------|
| **Texture bleeding** | When bilinear filtering is on, adjacent glyphs in the atlas can "bleed" into each other, causing visual artifacts at glyph edges. Fix: increase `spacing` in BMFont export settings to at least 1-2 pixels. |
| **Padding confusion** | Padding and spacing are frequently conflated. Padding adds space *inside* each glyph's region (for effects); spacing adds gaps *between* glyph regions (for filtering). Using the wrong one causes either wasted texture space or rendering artifacts. |
| **Negative offsets** | `xoffset` and `yoffset` can be negative (e.g., italic fonts, characters with outlines). Renderers that assume non-negative offsets will clip these characters. |
| **Baseline alignment** | When mixing fonts of different sizes or families, the `base` value must be used to align baselines. Many implementations ignore `base` and align to the top of `lineHeight`, causing misaligned text. |
| **Channel packing complexity** | Channel-packed fonts require the renderer to sample specific channels and apply the correct shader logic. Simple sprite-based renderers that draw RGBA as-is will show garbled text. |
| **Missing character handling** | The format does not mandate a fallback glyph. BMFont has an "output invalid char glyph" option (v1.11+) that includes a glyph for character ID `-1` (0xFFFFFFFF in binary) as the fallback. Implementations vary: some skip missing characters, some substitute a box or question mark, some crash. A robust implementation should check for the `-1` glyph first, then fall back to a configurable substitute. |
| **Cross-platform path separators** | The `file` field in `page` tags uses the path separator of the generating platform (typically backslash on Windows). Parsers on Linux/macOS must normalize to forward slashes. |
| **Surrogate pair handling** | The format uses integer codepoints, but many implementations use `char`-based iteration which breaks for codepoints above U+FFFF (supplementary plane characters encoded as UTF-16 surrogate pairs). |

---

## 11. Design Considerations for a New C# Library

### Parsing

A new library must support all three formats and auto-detect the format:

```
byte[] data = File.ReadAllBytes(path);

if (data[0] == 'B' && data[1] == 'M' && data[2] == 'F')
    return ParseBinary(data);
else if (data[0] == '<' || (data[0] == 0xEF && data[1] == 0xBB))  // '<' or UTF-8 BOM
    return ParseXml(data);
else
    return ParseText(data);
```

Key parsing considerations:

| Consideration | Recommendation |
|---------------|----------------|
| **Format detection** | Check magic bytes first (`BMF` for binary), then first non-BOM character (`<` for XML, otherwise text). |
| **Character ID type** | Use `int`, not `char`. C# `char` is 16-bit and cannot represent supplementary plane codepoints (U+10000 and above). |
| **Kerning storage** | Dictionary keyed by `(int first, int second)` tuple, or nested `Dictionary<int, Dictionary<int, int>>`. The nested form is faster for the common access pattern (lookup all kerning pairs for a given first character). |
| **Multi-page textures** | Store as `Texture[]` or `TextureRegion[]` indexed by page ID. |
| **Error tolerance** | Handle missing fields, extra fields, and non-standard formatting gracefully. Real-world `.fnt` files from different tools vary in formatting. |
| **Stream support** | Accept both file paths and `Stream` objects for loading, enabling loading from archives, memory, or network sources. |

### Data Model

```
BitmapFontData
  +-- Info: FontInfo             // face, size, bold, italic, padding, spacing, outline
  +-- Common: CommonInfo         // lineHeight, base, scaleW, scaleH, packed, channels
  +-- Pages: string[]            // texture filenames indexed by page id
  +-- Characters: Dictionary<int, CharacterInfo>
  +-- Kernings: Dictionary<int, Dictionary<int, int>>
       // first char -> (second char -> amount)

CharacterInfo
  +-- Id: int                    // Unicode codepoint
  +-- X, Y, Width, Height: int  // source rectangle in texture atlas
  +-- XOffset, YOffset: int     // rendering offsets
  +-- XAdvance: int              // cursor advance
  +-- Page: int                  // texture page index
  +-- Channel: int               // channel bitfield
```

### Rendering: Struct-Based Iteration for Zero GC

Following MonoGame.Extended's pattern, use a `ref struct` enumerator to iterate text without heap allocations:

```csharp
public ref struct GlyphEnumerator
{
    private ReadOnlySpan<char> _text;
    private int _index;
    private BitmapFontData _font;

    public GlyphLayout Current { get; private set; }

    public bool MoveNext()
    {
        if (_index >= _text.Length) return false;

        // Decode codepoint (handling surrogate pairs)
        int codepoint;
        if (char.IsHighSurrogate(_text[_index]) && _index + 1 < _text.Length)
        {
            codepoint = char.ConvertToUtf32(_text[_index], _text[_index + 1]);
            _index += 2;
        }
        else
        {
            codepoint = _text[_index];
            _index += 1;
        }

        // Look up glyph and compute position...
        Current = new GlyphLayout(codepoint, ...);
        return true;
    }
}
```

### Architecture Comparison

| Aspect | MonoGame.Extended | libGDX | Gum |
|--------|-------------------|--------|-----|
| **Language** | C# | Java | C# |
| **Glyph lookup** | `Dictionary<int, BitmapFontCharacter>` | Paged array (`Glyph[256][512]`) | Array indexed by char ID |
| **Kerning storage** | Per-character `Dictionary<int, int>` | Per-glyph paged byte array | Per-character `Dictionary<int, int>` (`SecondLetterKearning`) |
| **Texture coords** | Integer pixel coordinates + `Texture2DRegion` | Pre-computed normalized UVs on `Glyph` | Pre-computed normalized UVs on `BitmapCharacterInfo` |
| **Caching** | No built-in cache (relies on SpriteBatch batching) | `BitmapFontCache` for static text vertex buffers | Static buffer reuse for measurement/rendering |
| **GC mitigation** | Struct enumerator, StringBuilder overloads | Primitive arrays, GlyphRun reuse | Static buffers, array pooling |
| **Format support** | Text (primary), XML | Text only | Text and XML (binary detected but throws exception) |
| **Surrogate pairs** | Full support | Full support (Java `char` + `Character.toCodePoint`) | Limited (depends on usage) |
| **Generation** | External (user runs BMFont) | External (Hiero tool) | Integrated (wraps bmfont.exe) |
| **Text measurement** | `MeasureString()` returns `Size2` | `GlyphLayout` computes full layout with wrapping | `MeasureString()` returns width/height |

### Recommendations for a New Library

1. **Support all 3 formats** with auto-detection. Do not require the caller to specify the format.
2. **Use `int` for character IDs** throughout. Never use `char` as the primary type.
3. **Nested dictionary for kerning**: `Dictionary<int, Dictionary<int, int>>` matches the access pattern (given a character, find kerning for the next character).
4. **Separate data model from rendering**: Parse into a renderer-agnostic `BitmapFontData` object. Rendering integration is a separate layer.
5. **Struct-based enumerators**: Use `ref struct` and `ReadOnlySpan<char>` for zero-allocation text iteration.
6. **Pre-computed UVs**: Optionally compute normalized texture coordinates at load time to avoid per-frame division.
7. **Fallback glyph**: Support a configurable fallback character (typically `?` or a box) for missing codepoints.
8. **Thread safety**: The data model should be immutable after loading. Rendering state (cursor position, glyph layout) should be per-call, not shared.
