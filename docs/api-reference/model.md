# BmFontModel -- Descriptor Structure

Namespace: `KernSmith.Output.Model`

`BmFontModel` is the in-memory representation of a BMFont `.fnt` descriptor. You get one from
`BmFontResult.Model`, `BmFont.LoadModel(...)`, or by parsing a `.fnt` file. Its layout mirrors
the blocks of the BMFont format.

## BmFontModel

| Property | Type | Description |
|----------|------|-------------|
| `Info` | `InfoBlock` | Font metadata and generation settings. |
| `Common` | `CommonBlock` | Metrics shared across all glyphs. |
| `Pages` | `IReadOnlyList<PageEntry>` | Atlas page filenames. |
| `Characters` | `IReadOnlyList<CharEntry>` | Per-glyph atlas placement and metrics. |
| `KerningPairs` | `IReadOnlyList<KerningEntry>` | Kerning adjustments (empty when kerning is disabled). |
| `Extended` | `ExtendedMetadata?` | KernSmith-specific metadata, or `null` when not present. |

## InfoBlock

A record. The BMFont `info` line.

| Member | Type | Description |
|--------|------|-------------|
| `Face` | `string` | Font face name. |
| `Size` | `float` | Font size (rounded to integer in on-disk formats). |
| `Bold` | `bool` | Bold flag. |
| `Italic` | `bool` | Italic flag. |
| `Unicode` | `bool` | Unicode charset flag. |
| `Smooth` | `bool` | Smoothing (anti-aliasing) flag. |
| `FixedHeight` | `bool` | Fixed-height flag. |
| `StretchH` | `int` | Horizontal stretch percentage (defaults to `100`). |
| `Charset` | `string` | Character set string. |
| `Aa` | `int` | Anti-alias level (defaults to `1`). |
| `Padding` | `Padding` | Glyph padding. |
| `Spacing` | `Spacing` | Glyph spacing. |
| `Outline` | `int` | Outline thickness in pixels (`0` = none). |

## CommonBlock

A record. The BMFont `common` line.

| Member | Type | Description |
|--------|------|-------------|
| `LineHeight` | `int` | Distance between lines of text in pixels. |
| `Base` | `int` | Distance from the top of a line to the baseline. |
| `ScaleW` | `int` | Atlas page width. |
| `ScaleH` | `int` | Atlas page height. |
| `Pages` | `int` | Number of atlas pages. |
| `Packed` | `bool` | Whether glyphs are channel-packed (default `false`). |
| `AlphaChnl` / `RedChnl` / `GreenChnl` / `BlueChnl` | `int` | Per-channel content codes (default `0`). |

## PageEntry

A record. Maps a page id to its atlas image filename.

| Member | Type | Description |
|--------|------|-------------|
| `Id` | `int` | Page id. |
| `File` | `string` | Atlas image filename. |

## CharEntry

A record. One glyph's atlas position and rendering metrics.

| Member | Type | Description |
|--------|------|-------------|
| `Id` | `int` | Unicode codepoint. |
| `X` / `Y` | `int` | Top-left position of the glyph in the atlas page. |
| `Width` / `Height` | `int` | Glyph size in pixels. |
| `XOffset` / `YOffset` | `int` | Offset to apply when drawing relative to the cursor. |
| `XAdvance` | `int` | How far to advance the cursor after drawing. |
| `Page` | `int` | Which atlas page holds this glyph. |
| `Channel` | `int` | Which channel(s) hold the glyph (default `15` = all). |

## KerningEntry

A record. A kerning adjustment between two glyphs.

| Member | Type | Description |
|--------|------|-------------|
| `First` | `int` | First codepoint. |
| `Second` | `int` | Second codepoint. |
| `Amount` | `int` | Pixels to adjust the advance when these glyphs are adjacent. |

## ExtendedMetadata

KernSmith-specific data that is not part of the standard BMFont format. Present only when
non-default features were used.

| Member | Type | Description |
|--------|------|-------------|
| `GeneratorVersion` | `string` | KernSmith version that produced the font (required). |
| `SdfSpread` | `int?` | SDF spread, if SDF mode was used. |
| `OutlineThickness` | `float?` | Outline thickness, if an outline was applied. |
| `GradientTopColor` / `GradientBottomColor` | `string?` | Gradient colors as hex RGB (e.g. `"FFD700"`). |
| `ShadowOffsetX` / `ShadowOffsetY` | `int?` | Shadow offset, if a shadow was applied. |
| `ShadowColor` | `string?` | Shadow color as hex RGB. |
| `SuperSampleLevel` | `int?` | Super-sampling level, if used. |
| `VariationAxes` | `IReadOnlyDictionary<string, float>?` | Variable-font axis values used during generation. |
| `ColorFont` | `bool?` | Whether color font rendering was enabled. |
| `FallbackCharacter` | `int?` | Fallback codepoint for missing glyphs, if configured. |
| `AdvanceAdjustY` | `float?` | Global vertical advance adjustment (Hiero `pad.advance.y`), if non-zero. BMFont has no per-glyph yadvance field, so this is surfaced here to round-trip. |
