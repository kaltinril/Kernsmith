# Phase 179 — Native Rasterizer: Validation, Golden Masters & Release

> **Status**: Future — Wave 3
> **Created**: 2026-04-01
> **Depends on**: Phases 166–174, 177 and 178 (175 and 176 only if they are kept)

## Goal

Comprehensive validation of the Native rasterizer against FreeType and StbTrueType baselines, establishment of golden master regression tests, and the release decision (publishing to NuGet is Jeremy's call — the project is `IsPackable=false` today).

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
| RobotoFlex-Variable (committed) | 32 | wght: 100, 400, 700, 900 |
| COLR v0 / v1 fixtures (added by Phase 172) | 32 | Color rendering, palette switch |
| `.otf` CFF fixture (added by Phase 166) | 16, 32 | Normal, Bold, Italic |
| WOFF / WOFF2 of a committed font (Phase 178 fixture) | 32 | Identical output to the raw sfnt |

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

### Unsupported Feature Behavior

Whatever is still unsupported at release must behave deliberately, and the docs must say which cases are silent fallbacks and which throw. Today's behavior, to be re-verified at release:
- `AntiAliasMode.Light` / `Lcd` → rendered as grayscale with no signal, because core does not negotiate AA modes; Phase 173 adds negotiation so the degradation happens in core and the `.fnt` metadata matches
- Variable font axes without Phase 171 → ignored by core's capability gate (default instance rendered)
- `ColorFont` without Phase 172 → **throws** `NotSupportedException` from the rasterizer (no monochrome fallback)
- `Sdf` without Phase 169 → core throws `NotSupportedException` before rasterizing
- CFF/OTF without Phase 166 → `RasterizationException` at `LoadFont`
- GSUB → no substitution (Phase 175 needs a decision)

### Performance Validation

- Benchmark against targets from Phase 177
- Memory usage profiling (peak, average, allocation count)
- WASM performance test (if applicable)

### Documentation

- Update the README backend and platform tables, `COMPARISON.md`, `docs/rasterizers/native.md`, the Native package README, and `reference/REF-12-rasterizer-backends.md`
- Verify Blazor WASM (the README platform table still shows ⏳ for Native) and record the result
- Add to `samples/` with usage examples
- Document known differences vs FreeType (auto-hinting vs bytecode, algorithm differences)
- Update `plan/done/plan-data-types.md` with new types

### Release Preparation

- `NativeCapabilities` accurately reflects every supported feature and AA mode
- Decide whether the Native csproj keeps `AllowUnsafeBlocks` (it contains no `unsafe` code)
- Ensure all analyzer warnings resolved (trimming, AOT, nullable)
- Code review pass for security (no buffer overflows, bounds checking)
- License headers on all new files (MIT)
- Final manual visual inspection of output at multiple sizes

### Regression coverage

- Golden masters live in the Native test project; any change > 1 byte/pixel fails
- `regression_check.py` runs with every config the feature phases added (`otf-plain`, `msdf`, `variable-wght700`, `color-colr`, `lcd`, hinting pairs, `woff2-plain`); each must show Native output present, not just "no diff"

## Success Criteria

- [ ] SSIM > 0.95 for standard rendering across all reference fonts/sizes
- [ ] All glyph metrics within ±1 pixel of FreeType
- [ ] Golden master tests established and passing
- [ ] All edge cases handled without crashes
- [ ] Performance meets Phase 177 targets
- [ ] No trimming/AOT/nullable warnings
- [ ] Documentation complete
- [ ] Ready for production use as `RasterizerBackend.Native`
