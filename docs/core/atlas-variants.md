# Atlas Variants (Dropshadow)

An **atlas variant** is an additional character-set rendering -- packed into the *same* shared
atlas PNG as the primary font -- that gets its own complete `.fnt`/`BmFontModel`. The first (and
currently only) variant kind is a dropshadow **shadow silhouette**: a flat coverage mask of each
glyph with no offset or color baked in, so a renderer can translate and tint it however it wants
at draw time.

## Why a shared atlas

Drawing primary glyph + shadow glyph from two separate textures means switching texture (and
often render state) per draw call. Packing both into one atlas means a renderer can draw an
entire shadowed string with a single texture bound.

## Configuring a variant

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii,
    Variants = new[]
    {
        new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette, BlurRadius: 2, HardShadow: false)
    }
};

var result = BmFont.Generate(fontData, options);
```

- `Name` -- identifies the variant; used as the dictionary key on `BmFontResult.VariantModels`/
  `VariantPages` and as the `.fnt` filename suffix (e.g. `myfont-shadow.fnt`).
- `Kind` -- `AtlasVariantKind.ShadowSilhouette` is the only kind in v1.
- `BlurRadius` -- blur applied to the silhouette's edge, in pixels.
- `HardShadow` -- when `true`, produces a crisp (binarized) silhouette instead of a soft edge.

Not supported together with `Variants`: `TargetRegion` (rendering into an existing PNG),
`ChannelPacking`, or a custom `Channels` configuration. `BmFont.Generate` throws
`NotSupportedException` if you combine them.

## What you get

`BmFontResult` exposes the primary model as usual (`.Model`, `.Pages`), plus:

| Member | Description |
|--------|-------------|
| `VariantModels` | `IReadOnlyDictionary<string, BmFontModel>`, keyed by variant `Name`. |
| `VariantPages` | `IReadOnlyDictionary<string, IReadOnlyList<AtlasPage>>`, keyed the same way. For a shared-atlas variant this is the *same* page list as `.Pages` -- one physical PNG. |

`result.Model.Extended.Variants` lists the variant names generated alongside the primary;
`result.VariantModels["shadow"].Extended.VariantOf` points back at the primary's font family
name. Neither field exists in stock BMFont -- see [`ExtendedMetadata`](../api-reference/model.md)
for KernSmith's extended-metadata fields and their text/XML/binary round-trip.

### Writing to disk

`BmFontResult.ToFile(outputPath)` writes the primary as `<outputPath>.fnt` + `<outputPath>_0.png`
(etc. per page) as usual, then writes each variant as `<outputPath>-<variantName>.fnt`. Because
the variant shares the primary's atlas pages, **only one PNG is written** -- the variant's `.fnt`
references the exact same PNG filename(s) as the primary's `.fnt`. No duplicate image is produced.

```
myfont.fnt          <- primary, references myfont_0.png
myfont-shadow.fnt    <- shadow variant, references the SAME myfont_0.png
myfont_0.png         <- one shared PNG containing both primary and shadow glyphs
```

## Drawing the shadow

The shadow silhouette has no baked offset or color -- draw it using its *own* `CharEntry` data
from `myfont-shadow.fnt`, translated and tinted however you like, then draw the primary glyph on
top from `myfont.fnt`:

1. Look up the shadow glyph's rect/UV from `myfont-shadow.fnt` (same character id as the primary).
2. Draw it at the primary glyph's screen position **plus your own shadow offset** (e.g. `(2, 2)`
   pixels), tinted to your shadow color/opacity.
3. Draw the primary glyph on top, untranslated, at full color.

Because both `.fnt` files' pages reference the same PNG, this is a single texture bind for both
draw calls.

## Design note: this is a general shared-atlas mechanism

Dropshadow is the only thing wired up today, but it's built on a general-purpose packer,
`AtlasGroupBuilder` (`KernSmith.Atlas`), that packs rects from any number of independently-tagged
glyph sources into one shared atlas pass, then splits the placements back out per source. The
dropshadow variant is just two sources (primary glyphs + shadow silhouette glyphs, same character
set) submitted to it.

The same mechanism is intended to eventually support **multi-font packing** -- mixing glyphs from
two entirely different fonts into one shared atlas, so drawing mixed-font text never switches
texture either. That is **not implemented** -- there's no public API today for submitting a second
font's glyphs as an atlas-group source, and `FontGeneratorOptions.Variants` only expresses
same-font variants of one character set. If you're building something that wants multi-font
packing, treat this as the extension point to design against, not a ready-made feature: expect to
add a new config surface (something that isn't `AtlasVariant`, since that assumes one font) and
wire it through `AtlasGroupBuilder` the same way the dropshadow variant does in `BmFont.cs`.

## CLI / `.bmfc`

```bash
kernsmith generate -f MyFont.ttf -s 32 -o myfont --shadow-variant --shadow-blur 2 --hard-shadow
```

`--shadow-variant` requests the `AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette, ...)`
variant; `--shadow-blur` and `--hard-shadow` map to `BlurRadius`/`HardShadow`. In a `.bmfc` file,
the equivalent extension line is `variantShadow=1` (alongside the existing `shadowBlur=`/
`hardShadow=` keys).
