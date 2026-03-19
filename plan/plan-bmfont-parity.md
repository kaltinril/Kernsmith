# bmfontier -- BMFont Parity Features

> Features present in AngelCode's BMFont.exe that bmfontier does not yet support.
> These are practical, useful features worth implementing to achieve full parity and beyond.

---

## Priority: High

### 1. Separate Texture Width and Height

BMFont allows setting texture width and height independently (e.g., 512x256). We only have a single `MaxTextureSize` that produces square textures.

**Changes needed:**
- Add `MaxTextureWidth` and `MaxTextureHeight` to `FontGeneratorOptions` (default: 1024 each)
- Update `BmFont.Generate()` page size estimation to handle non-square
- Update packer calls to pass separate width/height
- Keep `MaxTextureSize` as a convenience that sets both
- CLI: `--texture-width <n> --texture-height <n>`

### 2. Per-Channel Configuration

BMFont's export dialog allows configuring each RGBA channel independently:
- Each channel (A, R, G, B) can be set to: `glyph`, `outline`, `zero`, `one`
- Each channel has an `Invert` checkbox
- This enables effects like: glyph in RGB, outline in Alpha (for shader-based outline rendering)

**Changes needed:**
- Create `ChannelConfig` type:
  ```csharp
  public enum ChannelContent { Glyph, Outline, Zero, One }
  public record ChannelConfig(
      ChannelContent Alpha = ChannelContent.Glyph,
      ChannelContent Red = ChannelContent.Glyph,
      ChannelContent Green = ChannelContent.Glyph,
      ChannelContent Blue = ChannelContent.Glyph,
      bool InvertAlpha = false,
      bool InvertRed = false,
      bool InvertGreen = false,
      bool InvertBlue = false);
  ```
- Add `ChannelConfig? Channels` to `FontGeneratorOptions`
- Create a `ChannelCompositor` that renders the atlas with per-channel content assignment
- This replaces the simpler `ChannelPackedAtlasBuilder` for advanced use cases
- CLI: `--chnl-a glyph --chnl-r outline --invert-a`

### 3. TGA Texture Output

BMFont supports TGA (Targa) as a texture format. Many game engines and asset pipelines expect TGA.

**Changes needed:**
- Create `TgaEncoder : IAtlasEncoder` — TGA is a simple uncompressed format (header + raw pixels), easy to implement without dependencies
- Add `TextureFormat` enum: `Png`, `Tga`, `Dds`
- Add `TextureFormat` to `FontGeneratorOptions` (default: Png)
- Update `FileWriter` to use the correct encoder and extension
- CLI: `--texture-format tga`

### 4. Super Sampling

BMFont renders glyphs at a higher resolution then downsamples for smoother results, especially at small sizes.

**Changes needed:**
- Add `int SuperSampling` to `FontGeneratorOptions` (default: 1, valid: 1-4)
- When > 1: render at `size * superSampling`, then downsample the bitmap by averaging NxN pixel blocks
- Create `SuperSamplingPostProcessor : IGlyphPostProcessor` that performs the downscale
- The pipeline automatically inserts this as the LAST post-processor when configured
- CLI: `--super-sampling <level>` (1-4)

### 5. Fallback/Invalid Char Glyph

BMFont has "Output invalid char glyph" which includes the .notdef glyph (usually a rectangle or question mark) so renderers can show something for missing characters.

**Changes needed:**
- Add `bool IncludeFallbackGlyph` to `FontGeneratorOptions` (default: false)
- When enabled, rasterize glyph index 0 (the .notdef glyph) and include it as character ID 0 or -1 in the output
- This is what BMFont readers use as a fallback when a requested character isn't in the font
- CLI: `--fallback-glyph`

---

## Priority: Medium

### 6. TrueType Hinting Toggle

BMFont allows enabling/disabling TrueType hinting. Hinting snaps glyph outlines to pixel boundaries for crisp rendering at small sizes, but can distort shapes.

**Changes needed:**
- Add `bool Hinting` to `FontGeneratorOptions` (default: true)
- In `FreeTypeRasterizer`, when hinting is disabled, use `FT_LOAD_NO_HINTING` flag with `FT_Load_Glyph`
- When enabled, use `FT_LOAD_DEFAULT` (current behavior)
- CLI: `--no-hinting`

### 7. Force Offsets to Zero

BMFont's "Force offsets to zero" sets all xoffset and yoffset values to 0. Useful for monospace/grid-based text rendering where all characters should be positioned identically within their cell.

**Changes needed:**
- Add `bool ForceOffsetsToZero` to `FontGeneratorOptions` (default: false)
- In `BmFontModelBuilder`, when enabled, set `XOffset = 0` and `YOffset = 0` on all `CharEntry` records
- CLI: `--force-offsets-zero`

### 8. Equalize Cell Heights

BMFont's "Equalize the cell heights" makes every character cell the same height in the atlas, regardless of actual glyph height. Simplifies rendering for some engines.

**Changes needed:**
- Add `bool EqualizeCellHeights` to `FontGeneratorOptions` (default: false)
- When enabled, after rasterization, pad all glyph bitmaps to the maximum glyph height (adding transparent rows at top/bottom)
- This means every `CharEntry` in the output has the same `Height` value
- CLI: `--equalize-heights`

### 9. Height Stretch (Height %)

BMFont allows scaling the vertical dimension of rendered glyphs as a percentage (e.g., 120% for taller, 80% for squished). Useful for stylistic effects.

**Changes needed:**
- Add `int HeightPercent` to `FontGeneratorOptions` (default: 100)
- Create `HeightStretchPostProcessor : IGlyphPostProcessor` that scales the bitmap vertically using nearest-neighbor or bilinear interpolation
- Adjust metrics accordingly (BearingY, Height)
- CLI: `--height-percent <n>`

### 10. Autofit Texture Size

BMFont can automatically find the smallest texture size that fits all glyphs within a page count constraint.

**Changes needed:**
- Add `int? MaxPages` to `FontGeneratorOptions` (default: null = unlimited)
- When set, the pipeline tries progressively larger texture sizes (starting from the estimated minimum) until the packer fits everything in ≤ MaxPages
- Also add `bool AutofitTextureSize` (default: false) — when true, try progressively SMALLER sizes until glyphs no longer fit, then use the last size that worked
- CLI: `--max-pages <n>`, `--autofit`

### 11. Custom Glyph Images (Image Manager)

BMFont's Image Manager allows replacing or adding glyphs with custom images. This is used for:
- Icon fonts (inject custom icon PNGs as glyphs)
- Emoji or symbol replacement
- Decorative glyphs that can't be rasterized from the font

**Changes needed:**
- Add `Dictionary<int, byte[]>? CustomGlyphs` to `FontGeneratorOptions` — maps codepoint to raw image bytes (PNG)
- In the pipeline, after rasterization, replace/add glyphs from the custom map
- Decode the PNG to get dimensions and pixel data
- CLI: `--custom-glyph <codepoint>=<path>` (repeatable)

### 12. Failed Character Reporting

BMFont tracks which characters failed to render and lets the user find/clear them.

**Changes needed:**
- Add `IReadOnlyList<int> FailedCodepoints` to `BmFontResult` — codepoints that were requested but couldn't be rasterized
- Already partially implemented (rasterizer returns null for missing glyphs), just need to collect and expose them
- CLI: print a summary at the end if any characters failed, with `--verbose` showing the full list

---

## Priority: Low

### 13. DDS Texture Output

DDS (DirectDraw Surface) is used by some game engines, especially older DirectX-based ones.

**Changes needed:**
- Create `DdsEncoder : IAtlasEncoder` — more complex format with headers for mipmaps, compression, etc.
- Start with uncompressed DDS (just a header + raw pixel data)
- Compressed DDS (DXT/BC formats) would require a block compression library
- CLI: `--texture-format dds`

### 14. Adaptive Padding Factor

BMFont's "Adaptive padding factor" automatically adjusts padding based on glyph size or font metrics.

**Changes needed:**
- Add `float AdaptivePaddingFactor` to `FontGeneratorOptions` (default: 0)
- When > 0, calculate padding per-glyph as `(int)(glyphHeight * factor)`
- This requires per-glyph padding support in the packer (currently padding is global)
- Lower priority — most users set explicit padding

### 15. Match Char Height

BMFont's "Match char height" scales the font so the actual rendered character height (tallest glyph) matches the specified size, rather than using the typographic em size.

**Changes needed:**
- Add `bool MatchCharHeight` to `FontGeneratorOptions` (default: false)
- When enabled: render a reference character set at the requested size, measure the max height, then scale: `adjustedSize = requestedSize * requestedSize / maxRenderedHeight`
- Re-render at the adjusted size
- CLI: `--match-char-height`

---

## Implementation Notes

- All features should be additive — they don't break existing behavior
- Each feature maps to a `FontGeneratorOptions` property, a CLI flag, and a `.bmfc` config key
- Features can be implemented independently in any order
- The per-channel configuration (item 2) is the most architecturally significant — it may require refactoring the atlas building pipeline to support multiple render passes (glyph pass + outline pass)
- Super sampling (item 4) and height stretch (item 9) are clean post-processor implementations
- TGA output (item 3) is trivial — TGA is just a 18-byte header + raw pixels

---

## Cross-References

- Library API: [plan-data-types.md](plan-data-types.md), [plan-api-design.md](plan-api-design.md)
- CLI flags: [plan-cli.md](plan-cli.md)
- Atlas pipeline: [plan-texture-packing.md](plan-texture-packing.md)
- Post-processors: [plan-rasterization.md](plan-rasterization.md)
- Output formats: [plan-output-formats.md](plan-output-formats.md)
