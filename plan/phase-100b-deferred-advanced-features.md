# Phase 100b — Deferred Advanced Effect Features

> **Status**: P2/P3 core items (SdfScale render path + AdvanceAdjustY metadata) landed 2026-06-07 and are verified (regression + unit tests). P4 (outline wobble/zigzag) and P5 (native render mode) are intentionally DEFERRED as low-priority decorative/low-impact items — not abandoned.
> **Created**: 2026-06-05
> **Depends on**: Phase 100
> **Goal**: Land the advanced-effect items that were deferred when Phase 100 shipped its core.

---

## Background

Phase 100 (Hiero Advanced Feature Support) shipped its core set of new effect properties on 2026-06-05 — fill color, two-parameter shadow blur, extended gradient, gamma correction, SDF spread, and per-glyph X advance adjustment, all with full `.hiero` round-trip support. A handful of items were either incompletely plumbed (present in `RasterOptions` but not applied), blocked by the BMFont output format, or scoped out as low-priority decorative work. This phase carries those deferred items forward so Phase 100 can be closed out as complete. See `phase-100-hiero-advanced-features.md` for the original scope and history.

---

## Scope

### 1. `SdfScale` — render-at-larger-ppem SDF path (P3) — ✅ DONE (2026-06-07)

`SdfScale` was plumbed through `RasterOptions` but not applied — a no-op at its default value of `1`. Now implemented at the BmFont pipeline level (`BmFont.cs`, `RasterizeFont`), mirroring the existing SuperSample path: when `Sdf` and `SdfScale > 1`, glyphs are rasterized at `Size × SdfScale` and downscaled via `SuperSampleDownscale`. For a single-channel SDF, box-averaging the 8-bit distance values is the correct, SDF-aware downscale — the field is locally linear and the `128 = edge` zero-crossing is preserved, so this is the standard "render SDF at high res, then downsample" technique. The non-SDF supersample path is untouched, and the `SuperSampleLevel + SDF` guard remains in force (`SdfScale` is the separate SDF-specific knob).

**Result**: Default (`SdfScale = 1`) is byte-identical — confirmed by the bmfont-compare regression (150/150 FNT + 24/24 images identical, including the SDF configs) and a byte-identical unit test. New tests cover non-throw, downscale-back-to-target dimensions, and valid-SDF (inside > 128, outside < 128).

### 2. `AdvanceAdjustY` — vertical advance adjustment (P2) — ✅ DONE (2026-06-07)

BMFont `char` entries have no `yadvance` field, so a vertical advance adjustment cannot be applied to the per-glyph advance and standard consumers cannot honor it. **Decision (user, 2026-06-07)**: surface it as an **optional** `ExtendedMetadata` field (we already pack extra data into the `.fnt`/`.hiero`; the eventual native format will carry all options). It must never be required — a standard BMFont `.fnt` with no such field reads back as `null`, and the default value (`0`) emits nothing, keeping default output byte-identical. Implemented as `ExtendedMetadata.AdvanceAdjustY` (populated only when non-zero), serialized/round-tripped through the text/xml/binary formatters and `BmFontReader`, plus the bmfc reader/writer (symmetric with `AdvanceAdjustX`). Hiero already round-trips `pad.advance.y`.

**Note**: Still not applied to the `char` `xadvance` (no yadvance field exists); it is preserved as metadata only, for round-trip fidelity and future use.

### 3. Outline wobble / zigzag effects (P4) — DEFERRED (low-priority, decorative)

New `IGlyphEffect` implementations for pixel-level outline distortion, mapping Hiero's `OutlineWobbleEffect` (Detail, Amplitude) and `OutlineZigzagEffect` (Wavelength, Amplitude). Decorative only.

**Notes**: Highest-effort item in this phase; requires pixel-level outline path manipulation. Low impact — purely decorative.

### 4. Native rendering mode (P5) — DEFERRED (low-impact, very high effort)

Support OS-native font rendering as an alternative to FreeType, mapping Hiero's `glyph.native.rendering`. Lowest priority — FreeType covers the vast majority of use cases.

**Notes**: Would require an alternative rasterizer implementation. Very high effort, very low impact; track but do not prioritize.

### 5. Channel-packing fill guard (P3, conditional)

Fill-tint (`FillColorR/G/B/A`) is not factored into `HasAnyEffects`, which feeds the channel-packing decision. This is only relevant if channel packing should react to RGB-only fill changes. Today a fill change alters RGB but not the alpha shape, so it is intentionally excluded from the guard.

**Notes**: Revisit only if a concrete need arises for channel packing to respond to RGB-only fill changes; otherwise leave excluded.

---

## Priority Summary

| Item | Impact | Effort | Priority | Status |
|------|--------|--------|----------|--------|
| `SdfScale` render path | Low | Medium | P3 | ✅ Done (2026-06-07) |
| `AdvanceAdjustY` | Medium | Medium | P2 | ✅ Done — optional extended metadata (2026-06-07) |
| Outline wobble/zigzag | Low | High | P4 | Deferred (decorative, not started) |
| Native rendering | Very Low | Very High | P5 | Deferred (not started) |
| Channel-packing fill guard | Low | Low | P3 | Intentionally excluded |

---

## Notes

- All items were carried over from Phase 100 — see `phase-100-hiero-advanced-features.md` for original scope and the items that shipped in the 2026-06-05 core.
- New properties must keep sensible defaults that preserve current byte-identical output.
- Round-trip tests should verify any newly supported properties survive export → import cycles.
