# Phase 100 — Hiero Advanced Feature Support

> **Status**: Future
> **Created**: 2026-03-22
> **Depends on**: Phase 82
> **Goal**: Extend Hiero `.hiero` format support with advanced features that require new KernSmith properties or capabilities.

---

## Background

Phase 82 implements basic Hiero read/write support, mapping existing KernSmith `FontGeneratorOptions` properties to/from `.hiero` format. Several Hiero features have no KernSmith equivalent and were deferred to this phase. These features would require adding new properties to `FontGeneratorOptions` or new effect implementations.

## Deferred Features

### 1. Per-Glyph Advance Adjustment (`pad.advance.x/y`)

Hiero's `pad.advance.x` and `pad.advance.y` are per-glyph advance adjustments (added to xadvance/yadvance in the output). This is semantically different from KernSmith's `Spacing` property (inter-glyph atlas spacing).

**Requires**: New `FontGeneratorOptions.AdvanceAdjustX` / `AdvanceAdjustY` (float) properties, applied during glyph metrics calculation in the rasterizer.

### 2. Fill Color (ColorEffect)

Hiero's `ColorEffect` sets the base glyph fill color (RGBA). KernSmith currently renders glyphs in white and applies color via effects (gradient, outline). Phase 82 ignores non-white ColorEffect on import and always writes white on export.

**Requires**: New `FontGeneratorOptions.FillColorR/G/B/A` properties and rasterizer support for tinting the base glyph.

### 3. Two-Parameter Shadow Blur

Hiero separates shadow blur into `Blur kernel size` (discrete options: None, 3x3, 5x5, etc.) and `Blur passes` (int, number of blur iterations). Phase 82 collapses these as `ShadowBlur = kernelSize * passes` on import.

**Requires**: New `FontGeneratorOptions.ShadowBlurKernelSize` and `ShadowBlurPasses` properties, and updated shadow effect implementation to support multi-pass blur with configurable kernel.

### 4. Gradient Extended Properties

Hiero's `GradientEffect` has `Offset` (float), `Scale` (float), and `Cyclic` (bool) properties that control gradient positioning and repetition. KernSmith's gradient uses `GradientAngle` and `GradientMidpoint` instead. These are not directly mappable.

**Requires**: New `FontGeneratorOptions.GradientOffset`, `GradientScale`, `GradientCyclic` properties, or a rethink of gradient parameterization to support both models.

### 5. Distance Field Scale and Spread

Hiero's `DistanceFieldEffect` has `Scale` (int, default 1) and `Spread` (float, default 1.0) properties. Phase 82 maps only the boolean `Sdf = true` flag.

**Requires**: New `FontGeneratorOptions.SdfScale` and `FontGeneratorOptions.SdfSpread` properties if finer SDF control is desired.

### 6. Outline Wobble and Zigzag Effects

Hiero supports `OutlineWobbleEffect` (Detail, Amplitude) and `OutlineZigzagEffect` (Wavelength, Amplitude) for decorative outline distortion. These have no KernSmith equivalent.

**Requires**: New `IGlyphEffect` implementations (`OutlineWobbleEffect`, `OutlineZigzagEffect`) with pixel-level outline path manipulation.

### 7. FreeType Gamma Correction (`font.gamma`)

Hiero's `font.gamma` property controls FreeType gamma correction during rasterization. KernSmith does not expose this setting.

**Requires**: New `FontGeneratorOptions.Gamma` (float) property, passed through to FreeType rasterizer.

### 8. Native Rendering Mode (`glyph.native.rendering`)

Hiero supports OS-native font rendering (Java2D) as an alternative to FreeType. KernSmith always uses FreeType.

**Requires**: Alternative rasterizer implementation. Low priority — FreeType covers the vast majority of use cases.

## Priority Assessment

| Feature | Impact | Effort | Priority |
|---------|--------|--------|----------|
| Fill Color | Medium — affects visual appearance | Low | P2 |
| Advance Adjustment | Medium — affects glyph spacing | Medium | P2 |
| Shadow Blur (two-param) | Low — current collapse is adequate | Medium | P3 |
| Gradient Extended | Low — rarely used in practice | Medium | P3 |
| SDF Scale/Spread | Low — boolean SDF is sufficient for most | Low | P3 |
| Gamma Correction | Low — niche use case | Low | P3 |
| Outline Wobble/Zigzag | Low — decorative only | High | P4 |
| Native Rendering | Very Low — FreeType is standard | Very High | P5 |

## Implementation Notes

- Each feature should be implemented independently as a separate PR
- All new properties must have sensible defaults that preserve current behavior
- Round-trip tests should verify that newly supported properties survive export → import cycles
- Phase 82's warning log messages should be updated to remove warnings for newly supported features as they are implemented
