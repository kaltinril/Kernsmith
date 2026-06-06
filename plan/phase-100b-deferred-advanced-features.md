# Phase 100b — Deferred Advanced Effect Features

> **Status**: Planning
> **Created**: 2026-06-05
> **Depends on**: Phase 100
> **Goal**: Land the advanced-effect items that were deferred when Phase 100 shipped its core.

---

## Background

Phase 100 (Hiero Advanced Feature Support) shipped its core set of new effect properties on 2026-06-05 — fill color, two-parameter shadow blur, extended gradient, gamma correction, SDF spread, and per-glyph X advance adjustment, all with full `.hiero` round-trip support. A handful of items were either incompletely plumbed (present in `RasterOptions` but not applied), blocked by the BMFont output format, or scoped out as low-priority decorative work. This phase carries those deferred items forward so Phase 100 can be closed out as complete. See `phase-100-hiero-advanced-features.md` for the original scope and history.

---

## Scope

### 1. `SdfScale` — render-at-larger-ppem SDF path (P3)

`SdfScale` is plumbed through `RasterOptions` but is not applied; it is a no-op at its default value of `1` today. Applying it requires rendering the SDF at a larger ppem and scaling down, which the existing supersample path cannot provide because that path currently blocks SDF. This needs a dedicated, SDF-aware scaling path in the BmFont pipeline rather than reusing supersampling.

**Notes**: Keep the default (1) byte-identical to current output. The work is isolated to the SDF render path and should not touch the non-SDF supersample path.

### 2. `AdvanceAdjustY` — vertical advance adjustment (P2, blocked)

BMFont `char` entries have no `yadvance` field, so there is currently nowhere to write a vertical advance adjustment. This is blocked on a format decision: emit it via extended metadata, or skip it permanently. The horizontal counterpart (`AdvanceAdjustX`) already shipped in Phase 100.

**Notes**: Requires a decision before implementation — extended metadata vs. permanent skip. If skipped, document the limitation and drop the property to avoid implying support.

### 3. Outline wobble / zigzag effects (P4)

New `IGlyphEffect` implementations for pixel-level outline distortion, mapping Hiero's `OutlineWobbleEffect` (Detail, Amplitude) and `OutlineZigzagEffect` (Wavelength, Amplitude). Decorative only.

**Notes**: Highest-effort item in this phase; requires pixel-level outline path manipulation. Low impact — purely decorative.

### 4. Native rendering mode (P5)

Support OS-native font rendering as an alternative to FreeType, mapping Hiero's `glyph.native.rendering`. Lowest priority — FreeType covers the vast majority of use cases.

**Notes**: Would require an alternative rasterizer implementation. Very high effort, very low impact; track but do not prioritize.

### 5. Channel-packing fill guard (P3, conditional)

Fill-tint (`FillColorR/G/B/A`) is not factored into `HasAnyEffects`, which feeds the channel-packing decision. This is only relevant if channel packing should react to RGB-only fill changes. Today a fill change alters RGB but not the alpha shape, so it is intentionally excluded from the guard.

**Notes**: Revisit only if a concrete need arises for channel packing to respond to RGB-only fill changes; otherwise leave excluded.

---

## Priority Summary

| Item | Impact | Effort | Priority | Status |
|------|--------|--------|----------|--------|
| `SdfScale` render path | Low | Medium | P3 | Plumbed, not applied |
| `AdvanceAdjustY` | Medium | Medium | P2 | Blocked on format decision |
| Outline wobble/zigzag | Low | High | P4 | Not started |
| Native rendering | Very Low | Very High | P5 | Not started |
| Channel-packing fill guard | Low | Low | P3 | Intentionally excluded |

---

## Notes

- All items were carried over from Phase 100 — see `phase-100-hiero-advanced-features.md` for original scope and the items that shipped in the 2026-06-05 core.
- New properties must keep sensible defaults that preserve current byte-identical output.
- Round-trip tests should verify any newly supported properties survive export → import cycles.
