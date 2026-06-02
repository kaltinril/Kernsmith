# Phase 100 -- FreeType Vertical Alignment (Issue #67)

> **Status**: Planning
> **Size**: Small–Medium
> **Created**: 2026-05-25
> **Origin**: GitHub issue #67 — "Vertical letter alignment" (BrainwormSupreme, 2026-05-20)
> **Goal**: Fix the systematic 1px baseline difference between FreeType and GDI/BMFont that causes glyphs to sit 1px lower than expected in Gum's TextRuntime. Also document a separate GDI em-height sizing bug found during investigation.

---

## Two Bugs Found

Investigation uncovered two separate issues, both visible in `tests/bmfont-compare/output/`:

| Mode | FreeType `base` | GDI `base` | Delta | Cause |
|------|-----------------|------------|-------|-------|
| Cell-height (`fontSize=56`, plain.bmfc) | 46 | 45 | **1px** | Math.Ceiling vs Math.Round in Tier 2 |
| Em-height (`fontSize=-36`, Gum path) | 34 | 29 | **5px** | GDI always uses positive lfHeight (cell-height), ignores MatchCharHeight |

Bug 1 (1px) affects all non-GDI backends (FreeType, DirectWrite, StbTrueType) and is the root cause of issue #67.

Bug 2 (5px) affects GDI specifically when used in em-height mode (MatchCharHeight=true). Since GumFontGenerator always sets `MatchCharHeight=true`, any Gum consumer using the GDI backend would see an entirely wrong font size.

---

## Bug 1: 1px Baseline Shift (FreeType, DirectWrite, StbTrueType)

### Evidence from Comparison Files

Run `tests/bmfont-compare/GenerateAll` against `gum-bmfont/plain.bmfc` (Georgia 56pt) and `gum-bmfont/gum-em-height.bmfc` (Georgia 36pt em-height) to reproduce.

**Observed `common` block:**

| Config | Backend | `lineHeight` | `base` |
|--------|---------|-------------|--------|
| plain (56pt cell-height) | FreeType | 56 | **46** |
| plain (56pt cell-height) | GDI | 56 | **45** |
| plain (56pt cell-height) | DirectWrite | 56 | **46** |
| plain (56pt cell-height) | StbTrueType | 56 | **46** |
| gum-em-height (36pt em-height) | FreeType | 41 | **34** |
| gum-em-height (36pt em-height) | GDI | 36 | **29** |
| gum-em-height (36pt em-height) | DirectWrite | 41 | **34** |
| gum-em-height (36pt em-height) | StbTrueType | 41 | **34** |

BMFont (reference) would produce `base=45` for plain (matching GDI) and `base=33` for em-height 36pt. FreeType, DirectWrite, and StbTrueType are all 1px higher in both modes.

**Observed yoffsets for Georgia 56pt (plain) — FreeType vs GDI:**

- **49 of 95 characters agree** — full-ascender glyphs (H, b, f, k, l, A–Z). FreeType's bitmap_top is also 1px larger for these (hhea vs OS/2 clipping), so `yoffset = base - bitmap_top` cancels out.
- **46 of 95 characters differ by +1** in FreeType — x-height glyphs (a, c, e, n, o…), digits, and punctuation. FreeType's bitmap_top agrees with GDI, but base is 1px higher.
- **Zero negative yoffsets** in any backend.

This is a uniform 1px shift of the entire baseline coordinate system, not internal misalignment. Glyphs within a FreeType font align correctly with each other at `cursor_y + 46`.

### Root Cause

`baseLine` in `BmFontModelBuilder` is computed in Tier 2 (the path taken for FreeType, DirectWrite, and StbTrueType, which do not implement `GetFontMetrics()`):

```csharp
baseLine = (int)Math.Ceiling((double)os2.WinAscent * metricSize / fontInfo.UnitsPerEm);
```

Windows GDI computes `tmAscent` using **rounding** (per REF-09 §3):

```
tmAscent = round(usWinAscent * ppem / unitsPerEm)
```

BMFont writes `base = ceil(tmAscent / aa)` — ceiling of an already-rounded value.

For Georgia 56pt in cell-height mode:

| Step | Value |
|------|-------|
| effectivePpem (cell-height corrected) | ≈ 47.6 |
| `WinAscent * effectivePpem / UnitsPerEm` | ≈ 45.5 |
| GDI `tmAscent` = `round(45.5)` | **45** |
| KernSmith Tier 2 `baseLine` = `ceil(45.5)` | **46** |

The same 1px discrepancy occurs in em-height mode (Georgia 36pt):

| Step | Value |
|------|-------|
| ppem (em-height, no correction) | 36 |
| `WinAscent * 36 / UnitsPerEm` (Georgia) | ≈ 33.05 |
| BMFont `base` = `ceil(round(33.05))` | **33** |
| KernSmith Tier 2 `baseLine` = `ceil(33.05)` | **34** |

### Why the Character Groups Differ

Full-ascender glyphs (H, b, f…) have `bitmap_top` values that are also 1px larger in FreeType than in GDI, because FreeType follows hhea table metrics and does not clip at OS/2 `usWinAscent`. So:

```
FT:  yoffset = baseLine(46) − bitmap_top(41) = 5   ← same as GDI
GDI: yoffset = baseLine(45) − bitmap_top(40) = 5
```

For x-height glyphs (a, e, o…) FreeType and GDI produce the same `bitmap_top`. Since only `baseLine` differs:

```
FT:  yoffset = baseLine(46) − bitmap_top(24) = 22  ← 1 higher
GDI: yoffset = baseLine(45) − bitmap_top(24) = 21
```

### Cross-element Misalignment in Gum

The problem appears in Gum's TextRuntime when:

1. **Mixing KernSmith and BMFont fonts** — `base=46` (KernSmith FreeType) vs `base=45` (BMFont reference). Elements positioned relative to baseline are 1px off.
2. **UI elements sized to BMFont metrics** — buttons, labels, or containers calculated expecting `base=45` are 1px misaligned with KernSmith text.
3. **Multi-size text runs** — if different font sizes produce different rounding outcomes, mixed-size text lines have inconsistent baseline alignment.

---

## Bug 2: GDI Em-Height Sizing (5px offset, GDI-only)

### Root Cause

`GdiRasterizer.CreateHFont` always constructs a LOGFONT with positive `lfHeight`:

```csharp
LfHeight = (int)Math.Round((double)size * options.Dpi / 72),
```

In Windows LOGFONT, **positive lfHeight = cell-height mode**. BMFont uses **negative lfHeight** for em-height mode (`fontSize=-36` → `lfHeight=-36`). Our GDI rasterizer has no way to do em-height mode.

When `MatchCharHeight=true` (the Gum integration path — always set by `GumFontGenerator`):
- **Intent**: em-height mode — use size as ppem (36 → ppem=36)
- **What GDI does**: cell-height mode — cell height = 36 pixels → ppem ≈ 32.2

This produces a visually smaller font (ppem≈32 vs 36) and entirely wrong metrics.

### Impact

`RasterOptions` has no `MatchCharHeight` flag, so GDI's `CreateHFont` cannot know whether em-height mode is requested. The fix requires:

1. Add `MatchCharHeight` to `RasterOptions`
2. In `GdiRasterizer.CreateHFont`, use negative `LfHeight` when `MatchCharHeight=true`

This is a separate bug from Bug 1 and should be tracked separately.

**Practical scope**: GumFontGenerator uses FreeType by default (`RasterizerBackend.FreeType`). GDI is Windows-only and opt-in. Most Gum users are unaffected by Bug 2.

---

## How to Validate

1. Run `GenerateAll` against `gum-bmfont/plain.bmfc` and `gum-bmfont/gum-em-height.bmfc`.
2. **Check `base=` in the `common` line** of the generated `.fnt` files.
3. Expected after Bug 1 fix: `plain-freetype.fnt` shows `base=45` (matching GDI and BMFont).
4. Expected after Bug 1 fix: `gum-em-height-freetype.fnt` shows `base=33` (matching BMFont with `fontSize=-36`).
5. Run `python diff_fnt.py plain-freetype.fnt plain-gdi.fnt` to see the per-character breakdown.

---

## Fix for Bug 1

### Option A — Change Tier 2 rounding to match GDI (recommended)

In `BmFontModelBuilder.Build()` ([BmFontModelBuilder.cs:66-67](../src/KernSmith/Output/BmFontModelBuilder.cs#L66)), change the Tier 2 `lineHeight` and `baseLine` computation from `Math.Ceiling` to `Math.Round`:

```csharp
// Before (gives base=46 for Georgia 56pt cell-height, base=34 for Georgia 36pt em-height):
lineHeight = (int)Math.Ceiling((double)(os2.WinAscent + os2.WinDescent) * metricSize / fontInfo.UnitsPerEm);
baseLine = (int)Math.Ceiling((double)os2.WinAscent * metricSize / fontInfo.UnitsPerEm);

// After (gives base=45 for Georgia 56pt cell-height, base=33 for Georgia 36pt em-height):
lineHeight = (int)Math.Round((double)(os2.WinAscent + os2.WinDescent) * metricSize / fontInfo.UnitsPerEm,
                             MidpointRounding.AwayFromZero);
baseLine = (int)Math.Round((double)os2.WinAscent * metricSize / fontInfo.UnitsPerEm,
                           MidpointRounding.AwayFromZero);
```

This matches how Windows GDI computes `tmAscent` and `tmHeight`, which is what BMFont uses as its baseline.

**Trade-off**: StbTrueType and DirectWrite also use Tier 2 (both return `null` from `GetFontMetrics()`). They also benefit from this fix.

### Option B — Implement `GetFontMetrics()` in FreeTypeRasterizer

Have FreeType report its own hhea-based metrics via `GetFontMetrics()`, which `BmFontModelBuilder` would use via Tier 1:

```csharp
public unsafe RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
{
    var m = _face->size->metrics;
    return new RasterizerFontMetrics
    {
        Ascent     = (int)((m.ascender  + 32) >> 6),
        Descent    = (int)((-m.descender + 32) >> 6),
        LineHeight = (int)((m.height    + 32) >> 6),
    };
}
```

This ensures the `baseLine` is consistent with actual FreeType glyph positions. However, it likely produces `base=47` or `base=48` (hhea is larger than OS/2 WinAscent for most fonts) — further from BMFont, not closer. Not recommended for Gum parity.

### Option C — Accept the 1px difference

Per REF-09 §9 (Secondary Differences), a ±1px rounding discrepancy is acknowledged as an inherent difference between rasterizers. If Gum's consumer code is robust to 1px baseline variance, this may be acceptable.

### Recommendation

**Option A** is the right fix. It directly corrects the rounding mismatch that causes KernSmith to diverge from GDI/BMFont's `base` value. The change is minimal (two rounding function calls in one method) and brings KernSmith into alignment with the BMFont reference.

---

## Additional Finding: DirectWrite Space yoffset

From the comparison data:

| Backend | space (id=32) yoffset |
|---------|----------------------|
| FreeType | 46 |
| GDI | 45 |
| DirectWrite | **1** (incorrect) |
| StbTrueType | 46 |

DirectWrite reports `yoffset=1` for the space character, which is wildly incorrect (should equal `base` since space has no visible glyph). This is a separate DirectWrite bug worth investigating — likely `bitmap_top=base-1=45` being returned for space while `baseLine=46`, or some off-by-one in the space glyph path. Since space has `width=0, height=0`, this doesn't affect rendering but would affect any code that uses yoffset for space positioning.

---

## Files to Change

### Bug 1 fix

| File | Change |
|------|--------|
| `src/KernSmith/Output/BmFontModelBuilder.cs:66-67` | Change `Math.Ceiling` to `Math.Round(…, AwayFromZero)` for Tier 2 `lineHeight` and `baseLine` |
| `tests/KernSmith.Tests/` | Run regression baseline after fix to capture expected values |

### Bug 2 fix (separate, GDI em-height)

| File | Change |
|------|--------|
| `src/KernSmith/Rasterizer/RasterOptions.cs` | Add `MatchCharHeight` property |
| `src/KernSmith/Rasterizer/RasterOptions.cs` (`FromGeneratorOptions`) | Map `FontGeneratorOptions.MatchCharHeight` to the new field |
| `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs` (`CreateHFont`) | Use negative `LfHeight` when `options.MatchCharHeight` is true |

---

## Tests

1. Run `RegressionBaseline.GenerateAllBaselines()` before the fix to capture current values.
2. Apply the fix.
3. Run `RegressionBaseline.CompareAgainstBaselines()` — expect `base` to change for some fonts.
4. Run `GenerateAll` and verify:
   - `plain-freetype.fnt` shows `base=45`, matching `plain-gdi.fnt`
   - `gum-em-height-freetype.fnt` shows `base=33` (BMFont `fontSize=-36` would give `base=33`)
5. Add explicit assertion: for Roboto ASCII 32pt, `result.Model.Common.Base` should equal GDI backend output.

---

## References

- [tests/bmfont-compare/README.md](../tests/bmfont-compare/README.md) — How to run GenerateAll and diff scripts
- [tests/bmfont-compare/gum-bmfont/gum-em-height.bmfc](../tests/bmfont-compare/gum-bmfont/gum-em-height.bmfc) — Em-height test config (fontSize=-36, tests Gum integration path)
- [REF-09 §3](../reference/REF-09-font-metrics-and-sizing.md#3-windows-gdi-font-sizing-how-bmfont-works) — GDI tmAscent uses rounding
- [REF-08 §4](../reference/REF-08-bmfont-internals.md#4-font-metrics--lineheight-base) — BMFont base = ceil(tmAscent/aa)
- [REF-09 §9](../reference/REF-09-font-metrics-and-sizing.md#9-secondary-differences) — 1px rounding differences are expected
- [BmFontModelBuilder.cs:53-73](../src/KernSmith/Output/BmFontModelBuilder.cs#L53) — Three-tier baseLine selection
- [GdiRasterizer.cs:331-365](../src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs#L331) — CreateHFont with always-positive lfHeight
