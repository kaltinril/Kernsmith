# Phase 21 — Atlas Output Modes: Code Review Findings

**Branch:** `MultipleFontMergeSupport` (PR #1)
**Reviewed:** 2026-03-21
**Build:** Clean — 0 errors, 0 warnings, 322/322 tests pass (net8.0 + net10.0)

---

## High Severity

### H1. Outline re-pack omits atlas spacing
**File:** `src/KernSmith/BmFont.cs` ~line 386
**Issue:** When outline glyphs trigger a re-pack, the new `outlineRects` include padding but omit `spacing.Horizontal` / `spacing.Vertical`. Compare to the original rect construction (~line 207) which includes both. This means outline glyph cells are packed with zero inter-cell gap in the atlas texture, which can cause texture filtering bleed between adjacent cells.
**Note:** This does NOT affect visual inter-character spacing at render time (that's `xadvance`). It only affects the atlas texture layout. If texture filtering (bilinear, etc.) is not used, this may be acceptable.

### H2. `CombinedMaxTextureWidth/Height` are dead code
**File:** `src/KernSmith/Config/BatchOptions.cs`, `src/KernSmith/BmFont.cs` ~line 1204
**Issue:** `BatchOptions` defines `CombinedMaxTextureWidth` and `CombinedMaxTextureHeight` properties, but `GenerateBatchCombined` hardcodes `MaxWidth = 4096, MaxHeight = 4096` instead of reading them. Users cannot control the combined atlas size.
**Fix:** Wire the properties through to the `AtlasSizingOptions` in `GenerateBatchCombined`.

### H3. `EncodeCombinedId` silently truncates high codepoints
**File:** `src/KernSmith/BmFont.cs` ~line 175
**Issue:** Encoding uses `(fontIndex << 20) | (codepoint & 0xFFFFF)` — only 20 bits for codepoints. Unicode goes to U+10FFFF (21 bits). Supplementary Plane 16 characters (U+100000–U+10FFFF) will silently collide with Plane 0 characters.
**Fix:** Use 21-bit codepoint space (`fontIndex << 21`), use `long` keys, or validate and throw for out-of-range values.

---

## Medium Severity

### M1. `CompositeOnto` does hard pixel copy, not alpha blending
**File:** `src/KernSmith/Atlas/AtlasBuilder.cs` ~line 130
**Issue:** Source pixels with partial alpha (anti-aliased glyph edges) overwrite the destination entirely instead of blending. The shadow compositor elsewhere in `BmFont.cs` (~line 1040) does proper alpha blending with `sA + dA * (1f - sA)`.
**Fix:** Implement proper alpha blending consistent with the existing shadow compositing approach.

### M2. `ApplyConstraints` — MaxWidth/MaxHeight clamp can break ForceSquare
**File:** `src/KernSmith/Atlas/AtlasSizeEstimator.cs` ~line 385
**Issue:** `ForceSquare` sets both dimensions to `Math.Max(width, height)`, then `MaxWidth`/`MaxHeight` clamp runs after. If `ForceSquare` produces 512x512 but `MaxWidth = 256`, the result is 256x512 — no longer square.
**Fix:** Re-apply the square constraint after clamping, or validate that max constraints are compatible with the square constraint.

### M3. Empty batch jobs list crashes
**File:** `src/KernSmith/BmFont.cs` ~line 1204
**Issue:** `GenerateBatchCombined` accesses `jobs[0]` unconditionally. An empty list throws `IndexOutOfRangeException`. The outer `GenerateBatch` guards against null but not empty.
**Fix:** Add early return for empty list: `if (jobs.Count == 0) return empty result`.

---

## Code Quality

### Q1. `QueryAtlasSizeCore` duplicates the font loading pipeline
**File:** `src/KernSmith/BmFont.cs` ~line 587
**Issue:** Re-implements ~80 lines from `RasterizeFont` (font parsing, WOFF decompression, charset resolution, rasterizer setup, variable axes). The copy is already incomplete — it doesn't account for `MatchCharHeight`, `SuperSampleLevel`, effects, `HeightPercent`, or post-processors. Size estimates will be inaccurate when those features are used. Any future bug fix in `RasterizeFont` must be manually mirrored here.
**Recommendation:** Extract the shared prefix into a common method.

### Q2. `GetGlyphMetrics` calls `FT_Set_Char_Size` on every invocation
**File:** `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` ~line 410
**Issue:** When called in a loop from `QueryAtlasSizeCore` (once per codepoint), the font size and DPI are redundantly re-set on every iteration.
**Recommendation:** Set size once before the loop, or cache the last-set size and skip when unchanged.

### Q3. `GenerateBatchCombined` skips autofit/bump logic
**File:** `src/KernSmith/BmFont.cs` ~line 1214
**Issue:** The combined batch path calls `Estimate` then `Pack` directly without the verification-and-bump logic that `GenerateCore` uses when `AutofitTexture` is enabled. If the estimate is slightly too small, you get unexpected multi-page output.
**Recommendation:** Add the same bump logic, or document as a known limitation.

---

## Low Severity / Minor

- **`IRasterizer.GetGlyphMetrics` default returns `null`** — Custom rasterizer implementations silently produce zero-glyph size queries with no error. Consider throwing `NotImplementedException` in the default.
- **`AtlasTargetRegion` doesn't validate negative X/Y at construction** — Fails late with a confusing error deep in `GenerateCore`.
- **Metrics tracking granularity lost** — The `RasterizeFont` refactor collapsed individual sub-timings (FontParsing, CharsetResolution, Rasterization, etc.) into a single "Rasterization" bucket.

---

## Test Coverage Gaps

- No test for `QueryAtlasSize` with features that `QueryAtlasSizeCore` doesn't account for (`MatchCharHeight`, `SuperSampleLevel`, effects)
- No test for `SourcePngPath` (file-based) target region — all tests use `SourcePngData` (in-memory)
- No test for empty batch jobs list
- No test for combined batch with 3+ fonts
- No test for combined batch with supplementary plane codepoints (U+100000+)
- No test for `FixedWidth` + `ForceSquare` constraint interaction
- No test for `TargetRegion` with negative coordinates (error path)
- No alpha blending correctness verification in render-to-existing tests
