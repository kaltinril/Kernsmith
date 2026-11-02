# Making Native's small-text rendering better: ideas from our own code

Scope: Native rasterizer (`src/KernSmith.Rasterizers.Native`) at 8-16 px. This is first-principles work: what our code does, what the pixels show, and what math/logic/statistics would close the gap with the FreeType backend. A separate agent covers the literature survey, so this report doesn't repeat it. Everything here is read-only analysis. The only code written is a throwaway simulation in `scratchpad/research/sim/phase.py`.

Binding constraints, from `project_decisions.md`:
- Native is our own pure-managed code. We may match FreeType's *output*, but we may not port its code.
- `Light` is y-only and moves no point more than 1 px.
- Hinting applies only to `Light`.
- Advances are rounded once.

Several ideas below cross the second and third constraints. Each one is tagged **[DECISION]**, and Jeremy would have to approve it first. I propose all of them as **opt-in modes**, so default output stays the same.

---

## 0. TL;DR: ranked shortlist

Impact means visible quality at 8-13 px. Effort means code plus risk. Ordered by value for effort.

| # | Idea | Impact | Effort | Decision needed? |
|---|------|--------|--------|------------------|
| 1 | **Fix the core super-sample advance truncation** (`Advance / level` is integer floor). This is a bug, not a Native issue (§2). | High for `aa>1` users. It's the "cramped" look in the ss4 rows | XS (2 lines + test) | Core behaviour change; needs Jeremy's OK to change ss output |
| 2 | **Per-glyph x phase alignment (X1)**: translate the whole outline by s ∈ [-½, ½) px so vertical stems land inside pixel columns. Advance unchanged, shape unchanged. | **Very high at ≤13 px.** Simulated ink concentration rises from 0.08 to 0.65 at 12 px (§1.3) | S (~200 lines; closed-form 1-D search) | [DECISION]: moves horizontal positions, which "Light = y-only" forbids |
| 3 | **Fit-then-threshold for mono (M1)**: run the y fitter and X1 before the 50% threshold, and require counters of at least 1 px | High for `None`. Fixes the collapsed 'h' counter and the broken 'm' | S | [DECISION]: hinting `None` |
| 4 | **Evaluation harness + metrics (E)**: ink concentration, Euler-number topology check, stem-phase variance, SSIM vs FreeType, distortion vs ideal, advance error | Enabler for everything else | S-M | No |
| 5 | **Vertical stem detection + standard vertical stem (X2)**: transpose the existing horizontal machinery | Needed for X1 robustness and for X3 | S (reuse `GridFitter` with an axis parameter) | No (analysis only) |
| 6 | **Robust blue zones by clustering extrema over many glyphs (Z1)** | Medium (robustness: fonts where 'x' or 'H' is odd, scripts, CFF) | S-M | No |
| 7 | **Full x grid fitting with stem widths, counters and advance re-derivation (X3)**, plus lsb/rsb delta baked into kerning (K1) | Highest ceiling (this is FreeType "normal") | L | [DECISION]: x fitting and advance changes |
| 8 | **Replace the greedy KeyMap with exact 1-D dynamic programming (O1)** | Medium (fewer refused fits, consistent results) | M | No |
| 9 | **Cross-glyph consistency (S1)**: per-font stem-width classes and per-size width tables | Medium | M | No |
| 10 | **Offline FreeType-oracle tuning loop (T)**: parameter sweep, distilled into constants | Medium, multiplies the effect of 2-9 | M (offline only) | No (dev-time tool) |
| 11 | **x-height rounding policy (Z2)**: size-aware threshold and joint x/cap rounding | Low-medium | XS-S | Changes `Light` output |
| 12 | **Stem darkening / contrast (P1), gamma (P2)** | Low for Roboto (we're not too light; §1.3); higher for ExtraLight/CFF | S | P2: Native currently ignores `Gamma` (fact) |

**Suggested order:**
1. **E** (harness + metrics; nothing ships) and **#1** (core advance bug, its own small fix).
2. **X2** analysis code, then **X1** behind an opt-in. Measure it.
3. **M1**, reusing X1 and the y fitter for `None` (opt-in).
4. **Z1** and **S1**, which make 2-3 consistent across glyphs and fonts.
5. **T**: tune the constants of 2-4 against FreeType and ideal renders on a train/test split.
6. **X3 + K1 + O1**: the full "normal" hinter, if Jeremy wants FreeType-like widened spacing.
7. **P1/P2**, only if the metrics show a darkness gap on light-weight fonts.

---

## 1. Diagnosis: what Native does today and where it loses

### 1.1 What the code does

- **Gate.** `GlyphHinter.Applies` (`Internal/Pipeline/GlyphHinter.cs:47`) returns true only for `Light` with `EnableHinting` and no SDF. Every other request is unhinted exact-area coverage (`ScanlineRasterizer`, `Level()` at `Raster/ScanlineRasterizer.cs:398`).
- **Per-font metrics.** `FontHintMetrics.Measure` (`Hinting/FontHintMetrics.cs:80`):
  - Blue zones come from the tight bounds of the *first* drawable candidate glyph per zone ('xz', 'o', 'HEI', 'O', 'dlhkb', 'pq'; lines 86-91), with OS/2 as a fallback.
  - The baseline comes from the YMin of 'H' or 'x' (line 100).
  - The standard horizontal stem is the median of stem widths found in 'H', 'e' and 'o' (lines 182-193).
  - There is **no vertical-stem statistic**.
- **Per-size scaling** (`FontHintMetrics.Scale`):
  - Top zones round with `floor(flat + 2/3)` (line 135), i.e. they round up from a fraction of ⅓.
  - Overshoots under 0.5 px are suppressed (line 140).
  - The zone tolerance is min(0.02 em, 0.5 px).
- **Fitter** (`Hinting/GridFitter.cs`):
  - It finds horizontal runs: slope ≤ 0.07 (line 43), length ≥ 0.2 px (line 46), merged within 0.25 px.
  - It pairs them into strokes by mutual nearest opposite edge with ≥ 30% horizontal overlap (lines 216-247).
  - It places edges greedily in three passes: zones, then stems (anchored first, then longest), then lone flat edges.
  - Every placement goes through `KeyMap.TryAdd` (lines 720-772). That check refuses any shift over 1 px (`MaxShift`, line 52) and any gap shrinking below ½·min(original gap, 1 px) (line 756). The result is a monotone piecewise-linear y map applied to every point.
  - That monotone-map invariant is excellent. It is the main asset to reuse in x.
- **X.** X is never touched. The advance is `round(adv·scale)` once (`Pipeline/GlyphPipeline.cs:125-126`).
- **Mono.** Mono is a 128 threshold of unhinted coverage plus TrueType-style smart dropout (`Raster/DropoutControl.cs`). There is no fitting before the threshold.

### 1.2 What the pixels show (Roboto, 12 px cell = 9.10 ppem)

Coverage (0-255) from the existing atlases in `scratchpad/best/`:

```
FreeType gray 'h' (xoff 1, adv 6)      Native light 'h' (xoff 0, adv 5)     Native mono 'h' (xoff 1)
 212   0   0   0                         95 114   0   0   0                    255   0   0
 218 163 213 115                         95 164 224 172   5                    255 255 255
 221  20   5 209                         95 174  20 160  67                    255   0 255
 212   0   0 208                         95 114   0 106 103                    255   0 255
```

Roboto's 'h' stems are 185 units wide, which is 0.82 px. Unhinted, the left stem spans x = 0.63-1.45, so it splits 37%/45% across two columns (95/114). FreeType puts *the same 0.82 px of ink* into one column (212 ≈ 0.83). It does not thicken the stem; it only **moves** it.

So the dominant gap is **horizontal phase, not width or darkness**. Native's y fitting is already doing its job: rows are crisp, and the 'o' has 5 rows on both backends.

In mono, the straddling stems make the threshold choose columns that collapse the 2-px counter into 1 px ('h' is 3 columns wide). The 'm' loses its top-row joins.

### 1.3 Baseline statistics (all 94 printable ASCII glyphs, 12 px cell; computed from the atlases)

| backend | ink mass | lit px (>6%) | **mass in px ≥ 0.75** | mass in px ≥ 0.5 | mean \|∂x\| | mean \|∂y\| |
|---|---|---|---|---|---|---|
| freetype-gray (autohinted) | 910.6 | 1540 | **0.612** | 0.840 | 0.373 | 0.238 |
| native-gray (unhinted) | 848.1 | 1926 | 0.155 | 0.583 | 0.293 | 0.205 |
| native-light (y-fitted) | 943.0 | 1903 | 0.322 | 0.690 | 0.343 | 0.224 |
| gdi-gray / stb-gray | ~850 | ~1925 | ~0.15 | ~0.57 | ~0.29 | ~0.205 |

How to read this:
- Native-light already matches FreeType's y sharpness (∂y 0.224 vs 0.238) and has *more* ink mass. **Darkness is not the problem.**
- The gap is the concentration of ink into full pixels: 0.32 vs 0.61. FreeType packs the same ink into 20% fewer pixels.

**Simulation of X1 (x phase only, y left unhinted).** I rendered at 32× supersampling with even-odd polygons in Pillow. For each glyph I searched the shift s ∈ [-½, ½) that maximizes "mass in px ≥ 0.75":

| cell size | mean concentration at s=0 | at best s | examples (s*) |
|---|---|---|---|
| 12 px (9.10 ppem) | 0.083 | **0.648** | h +0.44 → 0.83; l +0.31 → 0.87; H +0.25 → 0.78; m -0.50 → 0.79 |
| 16 px (12.13 ppem) | 0.612 | 0.669 | stems are ~1.1 px there, so the phase matters less |

The simulation excludes y fitting, so its absolute numbers are not comparable to the atlas table. The *relative* gain is the point. At 12 px, the shifted 'h' is FreeType's 'h' bitmap column-for-column (stem in col 1, 2-px counter, stem in col 4). The only difference is the advance: 5 instead of 6.

---

## 2. The xadvance question (verified)

**Claim to check:** at Roboto 12 px, unhinted backends give 'h' xadvance = 5 while hinted FreeType gives 6. Is our unhinted advance truncated?

**Answer: no.** 5 is the correctly rounded value. FreeType's 6 is an auto-hinter metric, not the "true" advance. There *is* a real truncation bug, but it is in core's super-sampling path.

1. **Size → scale.** BMFont `fontSize` is the cell height. Core converts it to a ppem (`src/KernSmith/PreparedRasterization.cs`, the `effectiveSize` block around lines 264-276):

   ppem = size · upem / (winAscent + winDescent) = 12 · 2048 / (2146 + 555) = **9.0989**

   Native then uses scale = ppem·dpi/72/upem (`GlyphPipeline.PixelScale`, `Pipeline/GlyphPipeline.cs:39`) = 0.0044428 px/unit.
2. **True advance.** In `hmtx`, 'h' = 1128 units. 1128 × 0.0044428 = **5.011 px**. Round to nearest gives 5. Native uses `Math.Round(..., AwayFromZero)` (`GlyphPipeline.cs:125-126`), which is rounding, not truncation.
   - Every one of the 94 printable glyphs in native-gray, native-light and native-mono equals round(true advance) exactly. So do StbTrueType and DirectWrite.
   - Line sum: 439 px against an unrounded ideal of 442.0.
3. **Why FreeType says 6.**
   - The Roboto fixture has no `fpgm`/`prep`/`cvt`, and `maxp.maxSizeOfInstructions = 0`. With hinting on, FreeType's loader sends such a TrueType font to its **auto-hinter**.
   - Our FreeType backend passes `FT_LOAD_DEFAULT` (`FreeTypeRasterizer.cs:185`). It adds `TARGET_MONO` only for `None` (line 191) and never `TARGET_LIGHT`. So this is the auto-hinter's *normal* (x + y) mode.
   - Normal mode moves stems in x and re-derives the advance from the hinted outer edges plus the original side bearings, then rounds.
   - Measured over ASCII:
     - **gray:** 22 glyphs +1, 8 glyphs −1, sum 453 (+2.5% wider than ideal);
     - **mono:** 50 glyphs +1, sum 491 (+11%).
   - The `freetype-light` atlas is identical to `freetype-gray` because the backend never maps `Light` to `TARGET_LIGHT`. That's a side observation about the FreeType backend.
4. **The real truncation: core super-sampling.** `PreparedRasterization.SuperSampleDownscale` does `Advance = m.Advance / level` (blank glyphs, line 910) and `Advance: metrics.Advance / level` (line 935). These are C# integer divisions, i.e. floor.
   - With `aa=4` at Roboto 12 px, **25 of the 94 Native advances are 1 px short** (sum 414 vs 439). Examples:
     - '#' 5.602 px → 22 at 4× → 22/4 = 5 (should be 6)
     - 'B' 5.669 → 23 → 5 (6)
     - ',' 1.790 → 7 → 1 (2)
   - The same happens on StbTrueType (25), DirectWrite (25), GDI (30) and FreeType (29). That's the cramped look in every `*-ss4` row of `matrix_x4.png` ("thelazy").
   - Fix sketch: `(int)Math.Round((double)adv / level, MidpointRounding.AwayFromZero)`. Better still, take the advance from the backend's final-size metric, which avoids double rounding (a 4×-rounded value rounded again).
   - The bearings also use `/ level`. That truncates toward zero, so a negative bearing rounds *up*. The comment at line 917 says bearing truncation is deliberate (BMFont parity). Re-check it for negatives.
   - This is a core change that alters `aa>1` output for every backend. It needs TDD (red: "expected 6 but was 5" for '#' at 12 px ss4), the regression harness, and Jeremy's go-ahead.

---

## 3. Idea catalogue

Format for each idea: **Mechanism / Why it helps at 8-16 px / Cost / Risks / How to measure / Where it fits.**

The pipeline stages are referenced as in the Native README:
1. decode
2. transforms
3. box
4. scale + `GlyphHinter`
5. render

### A. Geometry and logic

#### X2. Vertical stem detection and the font's standard vertical stem
- **Mechanism.**
  - `GridFitter.FindEdges`/`Pair` already detect horizontal edges with ink side from winding, merge runs and mutual-nearest-pair them into strokes. Run the same code on the **transposed outline**: swap (x, y), then negate the winding sense, or swap the `IsTop` meaning to "ink to the right".
  - That gives vertical edges, pairs and widths.
  - Standard vertical stem: the median over the stems of a reference set such as 'l i n h m u d b p q' plus 'H I T L'. Prefer **weighted by edge length**, with an outlier gate: drop widths outside [0.5, 1.5] × median, then take the median again.
  - Also keep a **second cluster** for capitals versus lowercase when the two medians differ by more than about 8%. Many fonts have heavier cap stems.
- **Why.** X1/X3 need to know which x positions are stems, and S1 needs the width classes. Detection is the risky part of any x hinter, and this reuses already-tested logic.
- **Cost.** Turn `GridFitter` into an axis-parameterized type, or add a transposing adapter: about 100 lines. Measurement runs once per face/size/instance and is already cached in `GlyphHinter.MetricsFor`.
- **Risks.**
  - Italic and synthetic-italic outlines have no vertical edges, since they're sheared by 12°. Detect in the *unsheared* frame (shear-aware detection: apply x' = x − y·tan θ before detecting), or skip x work for italics.
  - Diagonal-heavy glyphs ('v w x y z k') have no stems, which is correct: leave them alone.
- **Measure.** Unit tests on Roboto, Source Serif, Noto CFF and DancingScript: the stem count per glyph and the standard widths against hand-measured `glyf` values (e.g. Roboto 'l' = 186 units, 'h' stems 185/186).
- **Fits.** `Hinting/GridFitter.cs` (axis parameter) and `FontHintMetrics` (`StandardVerticalStem`, `VerticalStemClasses`).

#### X1. Per-glyph x phase alignment (pure translation)
- **Mechanism.** Choose one shift s ∈ [−½, ½) px per glyph and size. Translate the whole pixel-space outline by s before rasterizing.
  - The advance and pen origin are unchanged. BMFont `xoffset` comes from the shifted box. The shape is not distorted at all.
  - Objective, over the vertical stems i from X2, with left edge aᵢ, width wᵢ, length Lᵢ, and frac(·) = the fractional part:

    ```
    stem centre cᵢ = aᵢ + wᵢ/2
    slack σᵢ = max(0, 1 − wᵢ)/2          (a sub-pixel stem can sit anywhere inside its pixel)
    penalty pᵢ(s) = max(0, |frac(cᵢ + s) − ½| − σᵢ)        for wᵢ < 1
    pᵢ(s) = dist(aᵢ + s, ℤ) + dist(aᵢ + wᵢ + s, ℤ)          for wᵢ ≥ 1  (edges on grid lines)
    E(s) = Σᵢ Lᵢ·pᵢ(s) + λ·s² + μ·max(0, |s| − ¼)²
    ```

  - E is piecewise linear in s, so its minimum lies at a breakpoint. The candidates are s = −frac(aᵢ), −frac(aᵢ + wᵢ), and ½ − frac(cᵢ) ± σᵢ, wrapped into [−½, ½). That's O(n²) for n ≤ ~10 stems: microseconds.
  - With no stems (an 'o'), fall back to the **round extrema**: the vertical tangent points from the transposed `HorizontalTangent`, treated as stems of width = the local stroke thickness. Or just s = 0.
- **Why it works at 8-16 px.**
  - Stems at 0.6-1.1 px wide decide the look. The simulation lifts ink concentration from 0.08 to 0.65 at 12 px and reproduces FreeType's 'h' bitmap exactly.
  - Fidelity cost is close to nothing. In an integer-advance atlas, where a glyph's ink sits inside its cell (lsb mod 1) is already an artifact: every pen position is quantized to whole pixels, and pens drift up to ±0.5 px per glyph against fractional layout. So choosing the sub-pixel phase deliberately, within ±½, does not reduce positional accuracy by any meaningful amount.
- **Cost.** About 150-200 lines plus X2. CPU is negligible next to rasterization. Code size is tiny.
- **Risks.**
  - **Uneven spacing.** Two neighbours with shifts s₁ = −0.45 and s₂ = +0.45 gain 0.9 px between them. Mitigations: the λ and μ terms; a cap of |s| ≤ 0.4 tuned by T; and **side-bearing balance**: penalize the change in visual lsb−rsb asymmetry rather than raw |s|.
  - Stems at different phases inside one glyph (the 'm' at a non-integer stem pitch) can't all align by translation. The objective picks a weighted compromise; full alignment needs X3.
  - [DECISION] It breaks "horizontal positions stay exactly as unhinted". It must be an opt-in: a new option, or a new `Light`-like mode.
  - It changes `GetGlyphMetrics`. The metrics must run the same shift (bitmap and metrics agree is a hard invariant), and the header-box shortcut must be off for this mode, as it already is for hinted `Light` (`TryPrepareBox`).
- **Measure.**
  - Concentration C75 and C50, and mean |∂x|.
  - Stem-phase variance (§4).
  - Line-level SSIM vs FreeType on pangrams.
  - **Inter-glyph gap error**: the distribution over all bigrams of (actual ink gap − ideal fractional ink gap). This guards the spacing risk.
- **Fits.** Stage 4. In `GlyphHinter.Apply`, after `GridFitter.Fit`, apply `x += s` to every command; `GlyphBox.Compute` already recomputes the box. Mono gets it through M1.

#### X3. Full x grid fitting (FreeType-"normal"-like, our own algorithm)
- **Mechanism.** The y algorithm, mirrored onto x, with x-specific rules:
  1. **Stem widths**: width′ = max(1, round(w)), with snapping to the standard vertical stem when |w − std| ≤ min(0.1 + 0.15·std, 0.5). This is exactly `StemWidth` (`GridFitter.cs:178`).
  2. **Anchoring**: the leftmost stem edge goes to round(a + s*) using X1's global phase, and later stems are placed relative to it.
  3. **Counters**: each gap between consecutive stems becomes g′ = max(1, round(g)) when g ≥ ~0.7 px (keep a white column) and keeps its rank order. This is the "1-2-1" layout of the 'h'.
  4. **Every other point**, serifs, bowls and diagonals included, moves by the monotone piecewise-linear x map through the placed edges. That's `KeyMap`, unchanged, on the x axis. **The same invariant carries over**: a strictly increasing map of x alone cannot make the outline self-intersect, and because y is fitted independently by a monotone map of y, the composition (x′(x), y′(y)) is a homeomorphism of the plane. That's a strong argument for this architecture over per-contour IUP.
  5. **Advance re-derivation**: lsb′ and rsb′ are the original side bearings. adv′ = round(x′_right_edge + rsb) − round(x′_left_edge − lsb), with a small bias toward more space at tiny sizes (tunable). Record δ_l = x′_left − x_left and δ_r = x′_right − x_right.
- **Why.** It lets the bitmap use the 1-px-stem / 2-px-counter layout even when the ideal advance is too tight for it. That's where FreeType's extra crispness comes from at ≤10 ppem. X1 alone can't give an 'm' with three aligned stems if its stem pitch is 2.96 px.
- **Cost.** Large: about 500-800 lines with tests, mostly reused from y. The [DECISION] items are x fitting and advance changes, and the spacing quality needs K1.
- **Risks.**
  - Text gets wider: FreeType is +2.5% gray and +11% mono on Roboto 12.
  - Kerning tables were computed for ideal advances.
  - Variable widths across sizes break "same layout at every size".
  - Must be opt-in.
- **Measure.** Everything in §4, plus total line width against ideal (bound it, e.g. ≤ +3%).
- **Fits.** `GridFitter` (axis-generic) and `GlyphHinter`. `PreparedGlyph.Advance` becomes hint-dependent, so `GetGlyphMetrics` must hint too.

#### K1. Bake hinting side-bearing deltas into BMFont kerning
- **Mechanism.** Engines that use FreeType compensate per pair at layout time:
  - if (rsb_delta(prev) − lsb_delta(next) > +½ px), take 1 px off the pen;
  - if it is < −½ px, add 1 px.
  - BMFont has no such hook, but it *does* have integer kerning pairs. For every ordered pair (a, b) in the charset, add k = round(δ_r(a) − δ_l(b)) to the pair's kerning (merged with GPOS kerning, then rounded once).
- **Why.** It removes the cumulative spacing drift X1/X3 introduce, without changing per-glyph bitmaps.
- **Cost.** O(n²) pairs; only the non-zero ones are written. The ASCII estimate is low thousands of pairs (a few KB of `.fnt`). It needs core plumbing, because kerning is computed in core (`BmFontModelBuilder`), so the backend has to expose δ_l/δ_r. That could be an optional `GlyphMetrics` extension, or a Native-only side channel. Public API → [DECISION].
- **Risks.** File size, and engines that ignore kerning gain nothing. CJK-size charsets would explode the pair count, so gate it to small charsets or only emit pairs with |k| ≥ 1.
- **Measure.** The bigram gap-error distribution with and without K1.
- **Fits.** Core `BmFontModelBuilder` kerning merge, plus a backend metric extension.

#### Z1. Blue zones by clustering extrema
- **Mechanism.** Today each zone is the bound of one glyph ('x' YMax, 'H' YMax, …).
  - Instead, collect the y of every *flat* top/bottom edge and every round extremum from `FindEdges` over a reference set: lowercase a-z, capitals A-Z, digits.
  - Cluster them in 1-D: sort, split at gaps > 0.015 em (or use a kernel density with bandwidth 0.01 em and take the modes).
  - Label the clusters by membership: the cluster holding 'x z v w' tops is the x-height; 'H E T' is the cap; 'b d h k l' is the ascender; 'p q g y' is the descender; the baseline is the bottom cluster at ≈ 0.
  - Within a cluster, **flat** = the mode of the flat edges, and **overshoot** = the mode of the round edges in that cluster.
  - Robust estimates: medians, with outliers rejected by MAD.
- **Why.** One odd glyph (a decorated 'x', a CFF 'H' with a slanted cut, a font with no 'O') currently mis-sets a zone for every glyph. Clustering also finds zones the hard-coded list misses: digit height, small caps, figure height.
- **Cost.** About 150 lines, once per face/instance; cheap. It needs `FindEdges` exposed for analysis, which `HorizontalStemWidths` already sets a precedent for.
- **Risks.** A changed zone value moves `Light` output, so goldens update. Non-Latin fonts: keep the existing "unmeasurable → unhinted" rule by requiring the labelled clusters.
- **Measure.** Zone values against OS/2 `sxHeight`/`sCapHeight` across fixtures (a test already exists for Roboto). The "edges captured per zone" histogram.
- **Fits.** `FontHintMetrics.Measure`.

#### Z2. x-height rounding policy
- **Mechanism.** Today the top zones use `floor(flat + 2/3)`, i.e. round up when frac ≥ ⅓ at every size. Options:
  - **(a) Size-aware threshold**: t(ppem) = t₀ + t₁·clamp((ppem − 8)/8, 0, 1). That rounds up more eagerly at the smallest sizes, where the x-height is 4-5 px and one row is 20%.
  - **(b) Joint rounding**: choose (x′, cap′) ∈ {⌊x⌋, ⌈x⌉} × {⌊cap⌋, ⌈cap⌉} to minimize |x′/cap′ − x/cap| + α·(|x′ − x| + |cap′ − cap|), so the x/cap proportion survives.
  - **(c) Scale fitting**: the vertical scale itself is adjusted so that the x-height lands on the grid, then everything else scales with it. Our zone-snap plus monotone map is a local version of that, so (c) mainly matters between zones.
  - I believe FreeType's auto-hinter does round-up-biased x-height scaling of this kind: about 5/8 px of bias, more with its optional "increase x-height" property. That's from memory, so verify it with the black-box probe in T.
- **Why.** At 8-12 px the x-height row decides readability more than anything else.
- **Cost.** XS. Risk: changes `Light` output.
- **Measure.** x-height pixel count against FreeType across sizes 8-18. The x/cap ratio error.
- **Fits.** `FontHintMetrics.Scale` (line 135).

#### C1. Counter and minimum-gap preservation
- **Mechanism.** `KeyMap` allows a gap to shrink to ½·min(original, 1 px) (`GridFitter.cs:756`).
  - For gray that's fine. For mono, any white gap under 1 px fills in after the threshold.
  - Rule: **a gap that is white in the outline and ≥ g_min (≈ 0.6-0.7 px) must end ≥ 1 px in pixels**. If it can't, refuse the edge that would close it, and prefer moving the outer edge instead.
  - Apply the same rule in x under X3.
- **Why.** It fixes 'e' and 'a' eyes and the 'h'/'m'/'B' counters in mono, the most visible mono failures.
- **Cost.** XS-S (a mode-dependent minimum in `TryAdd`).
- **Risks.** At 8 px some glyphs can't satisfy it within MaxShift = 1 px. Then fall back to the current behaviour.
- **Measure.** The Euler-number topology check (§4).
- **Fits.** `GridFitter.KeyMap`.

#### L1. Serif and linked edges
- **Mechanism.** Classify short edges (length < ~1.5 × stem width) that are adjacent to a stem edge along the contour, with the same ink side, as **serifs** linked to that stem.
  - A linked serif takes its base edge's *shift* exactly; it is never rounded independently.
  - In y, the serif's top/bottom flats (the "feet") are already zone edges. The new part is the x linkage under X3, and the y linkage of a mid-height bracket.
- **Why.** Independent rounding of a serif and its stem produces 1-px "steps" and double-width feet at 10-14 px on serif faces (Source Serif, Georgia).
- **Cost.** S.
- **Risks.** Misclassifying a short real stem as a serif ('r' arm, 'f' crossbar). Restrict linking to edges whose far end lies within the stem's span.
- **Measure.** Stem-width consistency on a serif fixture. Visual check on Georgia `.bmfc`.
- **Fits.** `GridFitter` (a new pass between "stems" and "lone edges").

#### D1. Diagonals and round strokes
- **Mechanism.** Don't hint them directly. They move only through the monotone maps between anchored edges, which is the existing invariant.
  - One addition: **diagonal-thickness preservation**. After fitting, estimate the stroke thickness of each diagonal (distance between paired opposite-direction diagonal segments). If the anisotropic x/y maps thinned it by more than 15%, relax the nearest unanchored key.
  - Optionally, under P1, add a small darkening for strokes thinner than 1 px.
- **Why.** At small sizes, 'v w x y k' tend to look lighter than the fitted stems next to them. That's the "uneven colour" artifact.
- **Cost.** S-M.
- **Risks.** Low. It's mostly a measurement gate.
- **Measure.** Per-glyph darkness relative to its ideal: the 'v' darkness ratio vs the 'n' ratio.
- **Fits.** A post-check in `GridFitter.Apply`.

#### R1. Advance and side-bearing rounding with delta tracking
- **Mechanism.** This only matters with X1 or X3. Keep the advance as `round(adv·scale)` (current decision) for X1. Track the "virtual" ink edges:
  - lsb_eff = lsb + s;
  - rsb_eff = adv_round − (xMax + s).
  - Clamp s so neither drops below lsb_min = 0 when the original was ≥ 0.3 px: never let ink touch the cell edge if it didn't before.
  - Under X3, use the formula in X3 step 5 and export δ for K1.
- **Why.** It prevents "touching letters" at 8-10 px, the cramped look.
- **Cost.** XS.
- **Measure.** The minimum white gap over all bigrams. Count bigrams where ink touches.
- **Fits.** `GlyphHinter` / `GlyphPipeline.Prepare`.

### B. Optimization formulations

#### O1. Exact 1-D fitting by dynamic programming (replaces the greedy KeyMap order)
- **Setup.** Per axis, sort the detected edges e₁ < … < eₙ by original coordinate yᵢ. Each edge chooses an integer target tᵢ ∈ {⌊yᵢ⌋ − 1, …, ⌈yᵢ⌉ + 1}, a candidate set of at most 4. It may also be left *free* (interpolated), which we model as a "virtual" target at its interpolated position, with a free-cost term.
- **Energy:**

  ```
  E = Σᵢ wᵢ·(tᵢ − yᵢ)²                                        (fidelity, wᵢ ∝ edge length)
    + Σ_(i,j)∈stems  α·(tⱼ − tᵢ − W*(i,j))²                   (stem width = its target class width, W* from S1)
    + Σ_(i,j)∈stems  ∞·[tⱼ − tᵢ < 1]                           (min 1 px stem)
    + Σ_consecutive  ∞·[tᵢ₊₁ < tᵢ] + β·[gap white ∧ tᵢ₊₁ − tᵢ < 1]   (order; counters)
    + Σ_i∈zone  γ·(tᵢ − Z_snap)²                               (zones)
    + Σᵢ ∞·[|tᵢ − yᵢ| > MaxShift]
  ```

- **Why DP is exact here.** If the stem pairs are adjacent in sorted order, which is mostly true per axis for Latin glyphs, every coupling is between consecutive edges. The problem is then a chain, and Viterbi DP over the states (candidate tᵢ) is exact in O(n·k²) with k ≤ 4. A few dozen edges means microseconds.
  - Non-adjacent pairs, e.g. an 'e' bar between bowl edges, need either one extra state dimension (the open stem's bottom target) or a small branch-and-bound. Either stays tiny.
- **Why it beats greedy.** The current order (zones → anchored stems → longest stems → lone edges) means an early placement can make a later, more important one fail `TryAdd`. Such edges are then only interpolated. DP finds the globally best consistent assignment, so there are fewer "refused" edges, and the result doesn't depend on processing order.
- **Cost.** About 300 lines. CPU is negligible.
- **Risks.** Weight tuning (that's T's job). A changed outcome in edge cases means golden churn.
- **Measure.** The count of refused or unplaced edges per glyph (instrument `TryAdd`). The energy terms themselves are interpretable metrics.
- **Fits.** It replaces `Fit` steps 1-3 in `GridFitter`. `KeyMap.Eval` and `Apply` stay as they are.

#### O2. Constrained least squares for the free points (smooth alternative to piecewise-linear interpolation)
- **Mechanism.** Instead of a piecewise-linear map between keys, use a **monotone cubic (Fritsch-Carlson) map** through the keys. Or solve min Σ (Δy′ − Δy)² over the points between keys, subject to the keys' positions (a tridiagonal system, so linear time).
- **Why.** The piecewise-linear map has slope discontinuities at keys, which put visible kinks in bowls ('o', 'e') when neighbouring keys moved in opposite directions. A C¹ monotone map keeps curvature smoother.
- **Cost.** S. Risk: monotonicity must still be guaranteed. Fritsch-Carlson guarantees it, and the invariant comment must be updated.
- **Measure.** Shape distortion versus the ideal (§4, D_ideal) on round glyphs.
- **Fits.** `KeyMap.Eval`.

#### O3. A global energy across glyphs (consistency)
- **Mechanism.** Two-level optimization.
  - First solve, per font and size, the **class decisions**: the pixel widths W_h, W_v for each stem class, the zone pixel heights, and the phase rule.
  - The objective is the sum of per-glyph energies over the reference set: Σ_g E_g(W, Z).
  - Then fit each glyph with those fixed (O1).
  - Because the class space is tiny (W ∈ {1, 2, 3}, Z ∈ {⌊·⌋, ⌈·⌉}), just enumerate it.
- **Why.** Every 'n'-like glyph then gets the same stem width and every lowercase the same x-height. Consistency matters more to perceived quality than per-glyph optimality.
- **Cost.** S on top of O1 + S1. It runs once per size.
- **Fits.** `FontHintMetrics.Scale` → `ScaledHintMetrics`.

#### O4. Perceptual energy terms (optional)
- Add a contrast term: −κ·(fraction of ink in full pixels), computed analytically from the edge fractional positions, as in X1's penalty.
- Add a colour term: (darkness(g) − mean darkness)².
- This turns "what looks crisp" into the same objective as fidelity, and T tunes κ.

### C. Statistics and data-driven

#### S1. Per-font measurement pass: stem histograms and width classes
- **Mechanism.**
  - At face load (cached), run X2 and the horizontal stem detection over a reference set (a-z, A-Z, 0-9).
  - Build **length-weighted histograms** of stem widths per axis. Fit a 1-D Gaussian mixture, or simply take the k-means with k chosen by BIC, k ≤ 3.
  - The classes become, typically: lowercase vertical, capital vertical, horizontal (thin), and horizontal capital bars.
  - Per size, map each class to a pixel width once. A stem snaps to its class width when its width is within the class's 2σ, else it rounds alone.
- **Why.** Today the horizontal standard stem is the median of 'H', 'e', 'o' only, and vertical stems have no standard at all. With classes, every 'l i j n m h u' stem gets the identical width and phase rule, even when the outlines differ by a few units (Roboto 'h' left 185 vs right 186; 'n' 316/322/326).
- **Cost.** S. About 60 glyph decodes once per face/instance.
- **Risks.** Weird fonts produce degenerate mixtures. Fall back to the median.
- **Measure.** Stem-width variance across glyphs (§4).
- **Fits.** `FontHintMetrics`.

#### S2. Consistency constraints as post-checks
- After fitting, compute each stem class's pixel-width distribution across the charset.
- If more than 10% of a class deviates from the class width, log the deviation. In tests, fail the golden metric.
- This is a quality gate, not a runtime cost.

#### T. Learning from FreeType offline (tuning loop, plus optionally a distilled model)
- **Black-box probe.** Render with the FreeType backend: auto-hinter normal and light (the latter needs a test-only flag in the probe, not in the backend), plus mono. Fonts: Roboto, Noto Sans, Source Serif, Arial and Georgia (Windows), a CFF and a variable font. Sizes 8-20 ppem. Record per glyph:
  - stem x positions and widths in pixels, from column profiles;
  - zone rows;
  - advance;
  - lsb/rsb deltas.

  This turns FreeType's *behaviour* into tables we can fit. Examples:
  - "x-height px vs fractional x-height", which gives the Z2 threshold;
  - "stem px width vs scaled width", which gives the snapping thresholds;
  - "advance − round(ideal) vs (Σ stem shifts)", which gives the X3 step 5 bias.
- **Parameter sweep.** Our hinter's constants are about 15 scalars:
  - `SlopeTolerance`, `MinEdgeLength`, `MergeTolerance`, `MaxShift`, `MaxStandardStemDeviation`, the 0.1 + 0.15·std tolerance, `TopZoneRoundUp`, `ZoneToleranceEm`, `MaxZoneTolerancePixels`, the overshoot 0.5 px;
  - the pairing overlap 0.3;
  - the X1 λ, μ and cap;
  - the O1 α, β, γ;
  - the C1 g_min.

  Objective (§4):

  ```
  J = w₁·(1 − SSIM_FT) + w₂·D_ideal + w₃·Var_stem + w₄·AdvErr − w₅·C75 + w₆·TopoErr
  ```

  Optimize with coordinate descent or CMA-ES. Each evaluation renders about 6 fonts × 10 sizes × 94 glyphs ≈ 6k glyphs in well under a second in C#. **Split train/test by font family** to avoid overfitting Roboto. Report both.
- **Distilled model, optional.** For decisions FreeType makes non-linearly, such as "round this x-height up?" or "widen this stem to 2 px?", fit a logistic regression on features like (frac, ppem, w/std, x/cap) over the probe data. Ship only its 3-4 coefficients as constants, or a tiny threshold table. That keeps the runtime deterministic, pure C#, with nothing to attribute.
- **Licence.** FreeType is dual FTL/GPLv2. Running it as a measurement oracle during development, and choosing our own constants from the measurements, is "matching output", which `project_decisions.md` (Native Rasterizer, "Native's code is original") explicitly allows. Porting `aflatin.c` logic or copying its constants verbatim *as code* is not. Record the derived constants with comments that state the measurement, not the source.
- **Don't overfit to FreeType.** Its auto-hinter widens text by +2.5% and mono by +11%. Keep D_ideal and AdvErr in J so that "more like FreeType" never beats "more like the font".
- **Cost.** M, dev-time only: a console tool or test-project utility, plus a Python report.
- **Fits.** `tests/` (a new tool, or extend `tests/bmfont-compare/GenerateAll`). The output is constant changes in `Hinting/`.

### D. Perceptual and rasterization

#### P1. Stem darkening and a contrast curve at small sizes
- **Mechanism.** Embolden the outline by
  d(ppem) = clamp(k·(ppem₀ − ppem), 0, d_max) px, with ppem₀ ≈ 14-18 and d_max ≈ 0.25-0.4 px,
  using the existing `OutlineEmboldener`, *before* hinting, so the fitter sees the darkened stems. Equivalently, apply a coverage tone curve c′ = 1 − (1 − c)^γ, γ ≈ 1.2-1.5, on gray output (cheaper, but it doesn't help stem detection).
- **Why.** Thin strokes (≤ 0.6 px) fade at small sizes, especially on light weights and CFF ExtraLight.
- **Evidence it's low priority for Roboto.** Native-light's total ink mass is already 943 vs FreeType's 911 (§1.3). Prioritize it only if the metrics show a darkness ratio < 0.9 on light faces.
- **Cost.** XS (the emboldener exists).
- **Risks.** Changes glyph colour and spacing; bold-on-bold. Opt-in, or tie it to the hinting mode.
- **Measure.** The darkness ratio versus the ideal, per weight.
- **Fits.** Stage 2/4 (a transform before `GlyphHinter`), or a post-coverage LUT in `ScanlineRasterizer.Level`.

#### P2. Gamma-correct coverage and honouring `Gamma`
- **Mechanism.** Native currently ignores `RasterOptions.Gamma` (fact). Apply the LUT c′ = c^(1/γ) at `Level()`.
  - Separately, the box-filter downscale (core super-sampling) averages coverage linearly. That's correct for coverage, and gamma belongs to the consumer's blend.
- **Why.** Text rendered white-on-dark in game engines blends in sRGB. Linear coverage looks thinner on dark backgrounds. The BEST images are white-on-black, where gray text looks lighter than it is.
- **Cost.** XS.
- **Risks.** `BmFontBuilder.FromConfig` drops `Gamma` (known gotcha), so the harness won't see it.
- **Fits.** `ScanlineRasterizer.Level`.

#### P3. Coverage filter choice
- Exact area (box) coverage is the right default for an atlas.
- Alternatives such as a 1.2-px tent or Mitchell filter soften more, which is wrong for small text.
- Note for super-sampling: core's integer box sum truncates *each pixel's mean* (`PreparedRasterization.cs` around line 953). Rounding instead of truncating would remove a systematic −0.5-level bias. That's small but free.

#### M1. Mono: fit, then threshold (beats dropout-only)
- **Mechanism.** For `AntiAlias = None` with hinting (opt-in):
  1. y fitting (existing) with C1's mono gap rule;
  2. X1 phase, or X3;
  3. then threshold at 50%;
  4. keep `DropoutControl` as the safety net.

  With stems in whole columns, the threshold sees 0.83-coverage pixels instead of a 0.37/0.45 split. The stem is decided by geometry rather than by which side of 50% the split falls.
  - Additionally, use a **mono-specific stem rule**: a stem whose width is ≥ 0.5 px becomes exactly 1 px (or its rounded width) in the fitted outline, so the threshold can't drop or double it.
- **Why.**
  - Native-mono 'h' is 3 columns with a 1-px counter and 'm' loses joins. With the phase shift, the 12 px 'h' becomes FreeType-mono's 4-column, 2-px-counter shape (but within adv 5).
  - Dropout control only *adds* ink. It can't fix a counter that thresholding filled, or a stem that landed on the wrong column.
- **Cost.** S, given X1.
- **Risks.** [DECISION] hinting `None`. Today `useHinting=1` configs with `aa=none` would change, so gate it on a new option. Also the box/metrics must agree, which they do: `None` already measures by rendering.
- **Measure.** The topology check (§4), stem-width variance in pixels, and the bigram touching count.
- **Fits.** `GlyphHinter.Applies` (an extended gate) and `CoverageRenderer` (unchanged).

#### P4. Sub-pixel positioning trade-offs for atlases
- BMFont needs integer advances and one bitmap per glyph, so true sub-pixel positioning (several phase variants per glyph) is only possible as **atlas variants**. Core already has a `Variants` concept.
- Option: render each glyph at phases {0, ⅓, ⅔} and let a custom renderer pick by fractional pen position. That triples the atlas. It's out of scope for standard BMFont consumers, but a good "premium" path for Gum, whose text layout could track a fractional pen.
- X1 is the opposite trade: one phase, chosen for crispness. The two are compatible: X1 per variant phase.

---

## 4. Evaluation design

### 4.1 Metrics

Per glyph, per size, per mode. "Ideal" is the outline rendered at 16× exact coverage and box-downsampled, at the true fractional pen position and with no hinting.

| Metric | Definition | Drives |
|---|---|---|
| **C75 / C50** (ink concentration) | Σ coverage over pixels with c ≥ 0.75 (resp. 0.5) ÷ Σ coverage. Baseline: FreeType 0.61, Native-light 0.32 (§1.3) | X1, X3, M1 |
| **Mean gradient** | mean \|∂x\|, \|∂y\| (or Tenengrad) over the glyph cell | sharpness |
| **SSIM_FT** | `ImageSimilarity.Ssim` (exists in `tests/KernSmith.Rasterizers.Native.Tests/ImageSimilarity.cs`) after placing both bitmaps on a shared canvas by pen origin (`ImageSimilarity.Place`) | parity |
| **L1_FT** | mean \|a − b\| on the same canvas | parity |
| **D_ideal** (distortion) | L2 between the hinted bitmap, low-pass filtered (σ ≈ 0.7 px Gaussian), and the ideal, low-pass filtered. It measures shape change while ignoring the sharpening we *want* | fidelity guard |
| **Darkness ratio** | Σ coverage ÷ Σ ideal coverage, overall and per glyph; colour evenness = its stdev across glyphs | P1, D1 |
| **Stem-width variance** | Per stem class (S1), the pixel width (second moment of the column profile over stem rows) across glyphs; report its variance | S1, O3 |
| **Stem-phase variance** | Per class, the stem centroid's fractional position; circular variance | X1 consistency |
| **TopoErr** (mono, also gray at a 50% cut) | Euler number χ = components − holes of the 50% binary image (8-connected ink, 4-connected holes) vs the outline's χ ('o' = 0, 'B' = −1, 'i' = 2, 'h' = 1). Flags filled counters and broken strokes, e.g. Native-mono 'm' | M1, C1 |
| **AdvErr** | Σ \|adv − round(ideal)\|; plus signed line drift over pangrams | #1, X3 |
| **Bigram gap error** | For all ordered ASCII pairs with kerning applied: ink gap in pixels vs the ideal fractional gap; report the RMS, the touching count (gap ≤ 0 where the ideal > 0.3) and over-gap (> 1.5 px where the ideal < 0.8) | X1, R1, K1 |
| **Line SSIM** | Pangrams laid out from the `.fnt` (integer pens + kerning) vs FreeType's layout and vs the ideal layout | holistic |

### 4.2 Parity harness

- **Corpus.**
  - Fonts: Roboto, Noto Sans (TTF and CFF), Source Serif 4 (CFF serif), Source Code VF (mono, CFF2), Roboto Flex (variable), DancingScript (script; should mostly be left alone), and on Windows Arial and Georgia.
  - Sizes: cells 8-18 px plus a ppem sweep.
  - Modes: gray, light, mono, and the new opt-in modes.
- **Generation.** Reuse `tests/bmfont-compare/GenerateAll`, or add a dedicated C# console under `tests/`, to emit per-glyph raw coverage, the metrics JSON and the `.fnt`. The analysis in §1.3 was done in Python straight from `.fnt` + PNG and is easy to productionize (`metrics.py` next to `diff_comparisons.py`).
- **Output.** Per run, a CSV (font, size, mode, glyph, metrics) plus a summary table per (backend, mode), and the existing magenta diff images for eyeballing.
- **Gates.**
  - xUnit tests pin *thresholds* on a few key metrics. Examples: "Native light-x C75 at Roboto 12 ≥ 0.55"; "TopoErr = 0 for a-z at 10-16 px mono"; "AdvErr = 0 unhinted".
  - The goldens pin exact bytes, as today. `regression_check.py` remains the main-vs-branch gate (per CLAUDE.md) and needs `.bmfc` configs for every new opt-in mode, or it will report "identical" for untested paths.

### 4.3 Tuning loop

1. Freeze the eval corpus. Split it into train (Roboto, Noto, Arial) and test (Source Serif, Georgia, Source Code, Roboto Flex).
2. Define J (§3, T) with weights set once with Jeremy, e.g. "parity 0.3, fidelity 0.3, consistency 0.2, crispness 0.2".
3. Run a coarse grid, then CMA-ES or coordinate descent over the ~15 constants, evaluating in-process (C#, no PNG I/O). Expect seconds per evaluation.
4. Accept a candidate only if it improves J on **test** and doesn't regress any per-font metric by more than ε. Look at the magenta diffs by eye.
5. Commit the constants with invariant comments (what was measured, what breaks if changed), update goldens with `KERNSMITH_UPDATE_GOLDENS=1`, and run the regression harness.

---

## 5. Decision touch-points (for Jeremy)

Each conflicts with a current entry in `project_decisions.md` (Native Rasterizer). The safe framing for all of them is a **new opt-in mode, with existing modes byte-identical**.

1. **x phase alignment (X1).** It moves horizontal ink positions by up to ½ px. It conflicts with "Light is y-only… horizontal positions stay exactly as unhinted". Proposal: a new mode or option, e.g. `Light` + `HintX` or a `Normal` hint target, so `Light` keeps its meaning.
2. **Full x fitting with advance changes (X3) + kerning baking (K1).** Conflicts with "x grid fitting is not planned" and "advance rounded once". K1 also needs a backend → core metric channel (public API).
3. **Hinting `None` (M1).** Conflicts with "Native auto-hints only `Light`". It would need its own opt-in so `useHinting=1` mono configs don't change.
4. **Core super-sample advance rounding (#1).** It isn't a Native decision, but it changes `aa>1` output for every backend. It's a bug by any reading ('#' 5.6 px → 5), but it changes existing atlases.
5. **`Gamma` in Native (P2).** The fact currently says "ignored"; implementing it changes that fact.

---

## Appendix: reproducibility

- Atlas statistics: computed with Pillow from the existing atlases in `<session scratch>/best/*.fnt` and `*_0.png`. Coverage is in the R channel; alpha is constant.
- Phase simulation: `<session scratch>/research/sim/phase.py <font.ttf>`. It uses 32× supersampling, flattens curves into 6-8 segments, and fills even-odd, so it approximates exact area. It is good enough for relative comparisons, not for byte parity.
- Roboto facts (fontTools):
  - upem 2048; winAscent 2146; winDescent 555; OS/2 xHeight 1082, cap 1456;
  - head.flags bit 3 set; no `fpgm`/`prep`/`cvt`; `maxp.maxSizeOfInstructions` = 0;
  - 'h' advance 1128, lsb 141, stems 141-326 and 806-992 units.
