# Phase 179 — Native Rasterizer: Validation, Golden Masters & Release

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: All previous phases (161–178)

## Goal

Comprehensive validation of the Native rasterizer against FreeType and StbTrueType baselines, establishment of golden master regression tests, and preparation for production use.

## Scope

### Bitmap Comparison Testing

For each reference font × size × feature combination:
1. Render with Native rasterizer
2. Render with FreeType and StbTrueType
3. Compute SSIM (Structural Similarity Index):
   - SSIM > 0.95 for standard rendering
   - SSIM > 0.90 for SDF (algorithm differences expected)
   - SSIM > 0.85 for LCD (different algorithm = different output)
4. Compute PSNR as secondary metric
5. Flag any glyph with SSIM < threshold for manual review

### Reference Matrix

| Font | Sizes | Features |
|------|-------|----------|
| Roboto Regular | 12, 16, 24, 32, 48, 96 | Normal, Bold, Italic, Bold+Italic |
| Roboto Regular | 32 | SDF, Outline(2px), Shadow(2,2), Gradient |
| Noto Sans | 16, 32 | Normal, CJK subset (if TTF available) |
| DejaVu Sans | 16, 32 | Normal, Full Latin Extended |
| Variable font (TBD) | 32 | wght: 100, 400, 700, 900 |
| Color font (TBD) | 32 | COLR v0 color rendering |
| WOFF font (TBD) | 32 | Normal (verify identical output to raw sfnt) |
| WOFF2 font (TBD) | 32 | Normal (verify identical output to raw sfnt, including glyf transform) |

### Golden Master Tests

1. Render fixed set of glyphs ('A'-'Z', 'a'-'z', '0'-'9', common punctuation)
2. At fixed sizes (16, 32 px) with Roboto Regular
3. Save as reference bitmaps (checked into test fixtures)
4. Future test runs compare against golden masters
5. Any change > 1 byte per pixel flags as regression

### Metrics Validation

For every glyph in ASCII set:
- Advance width: within ±1 pixel of FreeType
- BearingX: within ±1 pixel of FreeType
- BearingY: within ±1 pixel of FreeType
- Width/Height: within ±1 pixel of FreeType
- Font-level: ascent, descent, lineHeight within ±1 pixel

### Edge Case Testing

- Empty glyphs (space, zero-width)
- Composite glyphs with deep nesting (accent stacking)
- Overlapping contours
- Very small sizes (8px) — verify no crashes
- Very large sizes (200px) — verify no overflow
- Degenerate curves (zero-length, duplicate points)
- Fonts with unusual table ordering
- CFF fonts (if Phase 166 complete)
- Variable fonts at axis extremes (if Phase 171 complete)
- Corrupt/truncated font data — verify graceful errors, not crashes
- `AntiAliasMode.Light` requested: verify graceful fallback to Grayscale (until Phase 174 adds auto-hinting)

### Unsupported Feature Behavior

Verify graceful behavior when users request features the Native rasterizer doesn't support:
- `AntiAliasMode.Light` without Phase 174 → falls back to Grayscale
- `AntiAliasMode.Lcd` without Phase 173 → falls back to Grayscale (or throws if strict)
- Variable font axes without Phase 171 → ignored (renders at default instance)
- Color font without Phase 172 → renders monochrome outline
- GSUB features without Phase 175 → no substitution applied
- Document which behaviors are silent fallbacks vs exceptions

### Performance Validation

- Benchmark against targets from Phase 177
- Memory usage profiling (peak, average, allocation count)
- WASM performance test (if applicable)

### Documentation

- Update README with Native rasterizer documentation
- Add to `samples/` with usage examples
- Document known differences vs FreeType (unhinted, algorithm differences)
- Update `plan/done/plan-data-types.md` with new types

### Release Preparation

- Update `IRasterizerCapabilities` to accurately reflect all supported features
- Ensure all analyzer warnings resolved (trimming, AOT, nullable)
- Code review pass for security (no buffer overflows, bounds checking)
- License headers on all new files (MIT)
- Final manual visual inspection of output at multiple sizes

## Success Criteria

- [ ] SSIM > 0.95 for standard rendering across all reference fonts/sizes
- [ ] All glyph metrics within ±1 pixel of FreeType
- [ ] Golden master tests established and passing
- [ ] All edge cases handled without crashes
- [ ] Performance meets Phase 177 targets
- [ ] No trimming/AOT/nullable warnings
- [ ] Documentation complete
- [ ] Ready for production use as `RasterizerBackend.Native`
