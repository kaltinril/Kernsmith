# bmfontier -- Color Font Support Plan (Task 13B)

> FreeType handles COLRv0, sbix, and CBDT transparently with `FT_LOAD_COLOR`.
> No custom table parsing needed. Main work is RGBA atlas support and byte order fix.
>
> **Date**: 2026-03-19

---

## Current State

**Already exists:**
- `FreeTypeRasterizer` detects `FT_PIXEL_MODE_BGRA` and sets `Format = PixelFormat.Rgba32`
- `PixelFormat` enum has `Rgba32 = 1`
- `StbPngEncoder` handles both `Grayscale8` and `Rgba32`
- `ChannelPackedAtlasBuilder` produces RGBA pages
- FreeTypeSharp v3.1.0 bundles FreeType 2.13.2 with full color font support

**Missing:**
1. `FT_LOAD_COLOR` flag never passed — color glyphs render as monochrome outlines
2. BGRA-to-RGBA byte swap not performed — existing BGRA detection labels as RGBA but doesn't reorder bytes (**bug**)
3. `AtlasBuilder` is grayscale-only (1 bpp) — RGBA glyphs get corrupted
4. No color table detection (`FT_FACE_FLAG_COLOR`, COLR/sbix/CBDT tags)
5. No configuration option to enable/disable color rendering
6. Mixed color + grayscale glyphs on same page not handled
7. Channel packing incompatible with color glyphs (needs guard)

---

## How FreeType Handles Color Fonts

| Format | Tables | FreeType Behavior with `FT_LOAD_COLOR` |
|--------|--------|----------------------------------------|
| COLRv0/CPAL | COLR + CPAL | Composites color layers into single BGRA bitmap |
| sbix | sbix | Decodes embedded PNG/TIFF, returns BGRA |
| CBDT/CBLC | CBDT + CBLC | Decodes embedded bitmaps, returns BGRA |
| COLRv1 | COLR v1 + CPAL | Partial support in FreeType 2.13.2 (stretch goal) |

---

## Implementation Phases

### Phase A — Core Rendering (Small effort)

| # | Task | Description |
|---|------|-------------|
| A1 | BGRA-to-RGBA byte swap | Fix existing bug: swap B↔R after `Marshal.Copy` when `pixel_mode == FT_PIXEL_MODE_BGRA` |
| A2 | Add `FT_LOAD_COLOR` flag | Gate on `options.ColorFont`. Define constant (`1 << 5 = 32`) if not in FreeTypeSharp enum |
| A3 | Config options | Add `ColorFont` (bool) and `ColorPaletteIndex` (int) to `FontGeneratorOptions` and `RasterOptions` |

### Phase B — Atlas Support (Medium effort)

| # | Task | Description |
|---|------|-------------|
| B1 | RGBA-aware `AtlasBuilder` | Detect if any glyph is RGBA → allocate 4 bpp pages. Promote grayscale glyphs to (255,255,255,alpha) during blit |
| B2 | Route in `BmFont.Generate()` | Use RGBA atlas path when `ColorFont` is enabled |
| B3 | Color table detection | Check COLR/sbix/CBDT tag presence in `TtfParser` table directory. Add `HasColorGlyphs` to `FontInfo` |

### Phase C — Guards and Compatibility (Small-Medium effort)

| # | Task | Description |
|---|------|-------------|
| C1 | Palette selection | P/Invoke for `FT_Palette_Select`, call before rendering when `ColorPaletteIndex != 0` |
| C2 | Channel packing guard | Throw/warn if `ChannelPacking` and `ColorFont` are both enabled |
| C3 | Post-processor awareness | `GradientPostProcessor` skips RGBA glyphs. `OutlinePostProcessor` may need RGBA adaptation |

### Phase D — Polish and Testing (Medium effort)

| # | Task | Description |
|---|------|-------------|
| D1 | Extended metadata | Add `color_font=1` to bmfontier output line per `plan-extended-metadata.md` |
| D2 | Tests | Noto Color Emoji (CBDT), Twemoji (COLRv0). Both freely available |
| D3 | Auto-detect mode | Optional: auto-enable `ColorFont` when `FontInfo.HasColorGlyphs` is true |

---

## Files to Modify

| File | Changes |
|------|---------|
| `FreeTypeRasterizer.cs` | `FT_LOAD_COLOR`, BGRA swap, palette selection |
| `FreeTypeNative.cs` | `FT_Palette_Select` P/Invoke, `FT_LOAD_COLOR` constant |
| `FontGeneratorOptions.cs` | `ColorFont`, `ColorPaletteIndex` properties |
| `RasterOptions.cs` | `ColorFont`, `ColorPaletteIndex` properties |
| `AtlasBuilder.cs` | RGBA page support, mixed-format blitting |
| `TtfParser.cs` | Color table tag detection |
| `FontInfo.cs` | `HasColorGlyphs` property |
| `BmFont.cs` | Channel packing guard, pass color options |
| `GradientPostProcessor.cs` | Skip RGBA glyphs |

---

## Risks

| Risk | Mitigation |
|------|------------|
| `FT_LOAD_COLOR` not in FreeTypeSharp enum | Define constant ourselves (`1 << 5`) |
| sbix bitmap strike size mismatch | FreeType selects best strike. Document size limitations |
| Color emoji are large (~128x128 at standard sizes) | Document texture size implications |
| COLRv1 not fully supported | Defer to future. COLRv0 covers majority of deployed color fonts |
| Channel packing incompatibility | Clear error message |

---

## Estimated Effort

- **Total**: 3-5 days focused work
- **Risk**: Medium — RGBA atlas path is the most complex change
- **Needs**: Color font test fixtures (Noto Color Emoji, Twemoji — both OFL/MIT licensed)
