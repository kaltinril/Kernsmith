# BmFontBuilder -- Fluent Builder

Namespace: `KernSmith`

Created via `BmFont.Builder()`. Every method returns the same builder for chaining, except
`Build()`, which runs the pipeline and returns a [`BmFontResult`](result.md).

```csharp
var result = BmFont.Builder()
    .WithFont("font.ttf")
    .WithSize(48)
    .WithCharacters(CharacterSet.Latin)
    .WithOutline(2, r: 0, g: 0, b: 0)
    .WithKerning()
    .Build();
```

## Choosing the font source

Call exactly one of these before `Build()`.

| Method | Description |
|--------|-------------|
| `WithFont(byte[] fontData)` | Load a font from raw bytes. |
| `WithFont(string fontPath)` | Load a font from a `.ttf`, `.otf`, or `.woff` file. |
| `WithSystemFont(string familyName)` | Use a system-installed font by family name. |
| `FromConfig(string bmfcPath)` | Seed the builder from a `.bmfc` or `.hiero` config file. |
| `FromConfig(BmfcConfig config)` | Seed the builder from an already-parsed config. |

## Size and characters

| Method | Description |
|--------|-------------|
| `WithSize(float size)` | Font size. Fractional values supported on FreeType, StbTrueType, DirectWrite; GDI rounds to the nearest integer. |
| `WithCharacters(CharacterSet characters)` | Which characters to include. |
| `WithFallbackCharacter(char fallbackChar)` | Character to substitute for missing glyphs (BMP only). |
| `WithFallbackCodepoint(int codepoint)` | Fallback for missing glyphs; supports supplementary-plane codepoints. Takes precedence over `WithFallbackCharacter`. |
| `WithFaceIndex(int faceIndex)` | Select a face in a `.ttc`/`.otc` collection. |

## Style

| Method | Description |
|--------|-------------|
| `WithBold(bool bold = true)` | Request bold (native face when available, otherwise synthetic). |
| `WithItalic(bool italic = true)` | Request italic (native face when available, otherwise synthetic). |
| `WithForceSyntheticBold(bool force = true)` | Force synthetic bold even when a native bold face exists. |
| `WithForceSyntheticItalic(bool force = true)` | Force synthetic italic even when a native italic face exists. |
| `WithHeightPercent(int percent)` | Vertical height scale. `100` = normal, `200` = double height. |

## Rendering quality

| Method | Description |
|--------|-------------|
| `WithAntiAlias(AntiAliasMode mode)` | Anti-aliasing mode. |
| `WithHinting(bool enable = true)` | Enable font hinting for sharper small text. |
| `WithSuperSampling(int level)` | `1` = off, `2` or `4` for smoother edges. |
| `WithSdf(bool sdf = true)` | Emit a signed distance field instead of bitmaps. |

## Effects

| Method | Description |
|--------|-------------|
| `WithOutline(int outline)` | Add a black outline of the given pixel width. |
| `WithOutline(int width, byte r, byte g = 0, byte b = 0)` | Add a colored outline. |
| `WithShadow(int offsetX = 2, int offsetY = 2, int blur = 0, (byte R, byte G, byte B)? color = null, float opacity = 1.0f)` | Add a drop shadow. |
| `WithHardShadow(bool enabled = true)` | Use a hard (binarized) shadow silhouette. |
| `WithGradient((byte R, byte G, byte B) startColor, (byte R, byte G, byte B) endColor, float angleDegrees = 90f, float midpoint = 0.5f)` | Apply a color gradient across glyphs. |
| `WithPostProcessor(IGlyphPostProcessor processor)` | Add a custom per-glyph post-processing step. |

## Atlas and texture

| Method | Description |
|--------|-------------|
| `WithMaxTextureSize(int size)` | Set max width and height together. |
| `WithMaxTextureSize(int width, int height)` | Set max width and height separately. |
| `WithPadding(int up, int right, int down, int left)` | Per-side glyph padding. |
| `WithPadding(int all)` | Uniform glyph padding. |
| `WithSpacing(int horizontal, int vertical)` | Gap between glyphs in the atlas. |
| `WithSpacing(int both)` | Uniform glyph spacing. |
| `WithPackingAlgorithm(PackingAlgorithm algorithm)` | MaxRects or Skyline. |
| `WithPowerOfTwo(bool powerOfTwo = true)` | Round texture dimensions up to powers of two. |
| `WithAutofitTexture(bool autofit = true)` | Shrink the texture to fit glyphs tightly. |
| `WithEqualizeCellHeights(bool equalize = true)` | Pad all glyph cells to the same height. |
| `WithForceOffsetsToZero(bool force = true)` | Zero out all glyph x/y offsets. |
| `WithTextureFormat(TextureFormat format)` | PNG, TGA, or DDS. |
| `WithPackingEfficiency(float efficiency)` | Hint for atlas size estimation (`0.0`-`1.0`). |

## Channels and packing

| Method | Description |
|--------|-------------|
| `WithChannelPacking(bool channelPacking = true)` | Pack multiple glyphs into separate RGBA channels. |
| `WithChannels(ChannelConfig config)` | Set per-channel content from a config object. |
| `WithChannels(ChannelContent alpha = ..., ChannelContent red = ..., ChannelContent green = ..., ChannelContent blue = ..., bool invertAlpha = false, ...)` | Set per-channel content and inversion inline. |

## Color and variable fonts

| Method | Description |
|--------|-------------|
| `WithColorFont(bool colorFont = true)` | Render color font layers (COLR/CPAL emoji, etc.). |
| `WithColorPaletteIndex(int index)` | Select a CPAL palette. |
| `WithVariationAxis(string tag, float value)` | Set a variable-font axis, e.g. `("wght", 700)`. Call multiple times for multiple axes. |

## Custom glyphs

| Method | Description |
|--------|-------------|
| `WithCustomGlyph(int codepoint, CustomGlyph glyph)` | Replace or add a glyph with a prepared `CustomGlyph`. |
| `WithCustomGlyph(int codepoint, int width, int height, byte[] pixelData, PixelFormat format = PixelFormat.Rgba32, int? xAdvance = null)` | Replace or add a glyph from raw pixels. |
| `WithMatchCharHeight(bool match = true)` | Scale custom-glyph advances to the font's character height. |

## Kerning, DPI, and metrics

| Method | Description |
|--------|-------------|
| `WithKerning(bool kerning = true)` | Include kerning pairs in the output. |
| `WithDpi(int dpi)` | DPI used for size calculation. |
| `WithCollectMetrics(bool collect = true)` | Record per-stage pipeline timing on the result. |

## Pluggable components

| Method | Description |
|--------|-------------|
| `WithBackend(RasterizerBackend backend)` | Choose the rasterizer backend (FreeType, Gdi, DirectWrite, StbTrueType). |
| `WithRasterizer(IRasterizer rasterizer)` | Supply a custom rasterizer instance. |
| `WithPacker(IAtlasPacker packer)` | Supply a custom atlas packer. |
| `WithEncoder(IAtlasEncoder encoder)` | Supply a custom atlas encoder. |
| `WithFontReader(IFontReader reader)` | Supply a custom font reader. |

## Building

| Method | Description |
|--------|-------------|
| `Build()` | Run the pipeline and return a [`BmFontResult`](result.md). Throws `InvalidOperationException` if no font source was set. |
