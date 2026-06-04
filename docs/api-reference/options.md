# FontGeneratorOptions

Namespace: `KernSmith`

The settings object for the generation pipeline. Every property has a sensible default, so
you only set what you need. The fluent [builder](builder.md) sets the same properties.

## Size, characters, and faces

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Size` | `float` | `32f` | Font size in pixels. Fractional values honored by FreeType/StbTrueType/DirectWrite; GDI rounds. |
| `Characters` | `CharacterSet` | `CharacterSet.Ascii` | Which characters to include. |
| `FallbackCharacter` | `char?` | `null` | Character shown for missing glyphs (BMP only). |
| `FallbackCodepoint` | `int?` | `null` | Fallback codepoint; supports supplementary plane. Takes precedence over `FallbackCharacter`. |
| `FaceIndex` | `int` | `0` | Face index for `.ttc`/`.otc` collections. |
| `Dpi` | `int` | `72` | Rendering DPI. |

## Style

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Bold` | `bool` | `false` | Request bold (native when available, else synthetic). |
| `Italic` | `bool` | `false` | Request italic (native when available, else synthetic). |
| `ForceSyntheticBold` | `bool` | `false` | Force synthetic bold even when a native bold face exists. |
| `ForceSyntheticItalic` | `bool` | `false` | Force synthetic italic even when a native italic face exists. |
| `HeightPercent` | `int` | `100` | Vertical height scaling. `150` = 50% taller. |
| `MatchCharHeight` | `bool` | `false` | Scale so the tallest character matches the requested pixel size. |

## Rendering quality

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AntiAlias` | `AntiAliasMode` | `Grayscale` | Anti-aliasing mode. |
| `EnableHinting` | `bool` | `true` | Enable hinting for crisp small text. |
| `SuperSampleLevel` | `int` | `1` | `1`-`4`; renders at Nx and downscales. Cannot combine with `Sdf`. |
| `Sdf` | `bool` | `false` | Generate a signed distance field font. |

## Outline

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Outline` | `int` | `0` | Outline thickness in pixels. `0` = none. |
| `OutlineR` / `OutlineG` / `OutlineB` | `byte` | `0` | Outline color channels. |

## Shadow

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ShadowOffsetX` / `ShadowOffsetY` | `int` | `0` | Shadow offset in pixels. Any non-zero offset (or blur) enables the shadow. |
| `ShadowR` / `ShadowG` / `ShadowB` | `byte` | `0` | Shadow color channels. |
| `ShadowOpacity` | `float` | `1.0f` | Shadow opacity, `0.0`-`1.0`. |
| `ShadowBlur` | `int` | `0` | Blur radius. `0` = hard edge. |
| `HardShadow` | `bool` | `false` | Use a binarized silhouette instead of antialiased glyph alpha. |

## Gradient

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `GradientStartR` / `GradientStartG` / `GradientStartB` | `byte?` | `null` | Start (top) color. Set both start and end red to enable the gradient. |
| `GradientEndR` / `GradientEndG` / `GradientEndB` | `byte?` | `null` | End (bottom) color. |
| `GradientAngle` | `float` | `90f` | Angle in degrees (`90` = top-to-bottom). |
| `GradientMidpoint` | `float` | `0.5f` | Midpoint bias, `0.0`-`1.0`. |

## Atlas and texture

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxTextureSize` | `int` | `1024` | Shortcut to set both width and height. Reads back as `MaxTextureWidth`. |
| `MaxTextureWidth` | `int` | `1024` | Max atlas width in pixels. |
| `MaxTextureHeight` | `int` | `1024` | Max atlas height in pixels. |
| `Padding` | `Padding` | `(0,0,0,0)` | Padding around each glyph. |
| `Spacing` | `Spacing` | `(1,1)` | Spacing between glyphs. |
| `PackingAlgorithm` | `PackingAlgorithm` | `MaxRects` | MaxRects or Skyline. |
| `PowerOfTwo` | `bool` | `true` | Round dimensions up to powers of two. |
| `AutofitTexture` | `bool` | `false` | Pick the smallest power-of-two texture that fits; overrides max width/height. |
| `EqualizeCellHeights` | `bool` | `false` | Pad all glyph cells to a common height and baseline. |
| `ForceOffsetsToZero` | `bool` | `false` | Zero out all xoffset/yoffset values. |
| `TextureFormat` | `TextureFormat` | `Png` | PNG, TGA, or DDS. |
| `SizeConstraints` | `AtlasSizeConstraints?` | `null` | Force square, power-of-two, or fixed width. |
| `TargetRegion` | `AtlasTargetRegion?` | `null` | Render glyphs into a region of an existing PNG. |

## Channels and packing

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ChannelPacking` | `bool` | `false` | Pack multiple glyphs into separate RGBA channels. Incompatible with effects and color fonts. |
| `Channels` | `ChannelConfig?` | `null` | Per-channel control of glyph/outline content and inversion. |

## Color and variable fonts

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ColorFont` | `bool` | `false` | Render color font glyphs (COLR/CPAL). |
| `ColorPaletteIndex` | `int` | `0` | CPAL palette to use. |
| `VariationAxes` | `Dictionary<string, float>?` | `null` | Variable-font axis values, e.g. `{ ["wght"] = 700 }`. |

## Custom glyphs

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CustomGlyphs` | `Dictionary<int, CustomGlyph>?` | `null` | Replace or add glyphs by codepoint, using raw pixel data. |

## Kerning and metrics

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Kerning` | `bool` | `true` | Include kerning pairs in the output. |
| `CollectMetrics` | `bool` | `false` | Record per-stage pipeline timing on the result. |

## Pluggable components

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Backend` | `RasterizerBackend` | `FreeType` | Backend used when `Rasterizer` is `null`. |
| `Rasterizer` | `IRasterizer?` | `null` | Custom rasterizer instance. |
| `FontReader` | `IFontReader?` | `null` | Custom font reader (defaults to the built-in TTF parser). |
| `Packer` | `IAtlasPacker?` | `null` | Custom atlas packer (defaults to `PackingAlgorithm`). |
| `AtlasEncoder` | `IAtlasEncoder?` | `null` | Custom atlas encoder (defaults to `TextureFormat`). |
| `PostProcessors` | `IReadOnlyList<IGlyphPostProcessor>?` | `null` | Extra per-glyph post-processors. |
