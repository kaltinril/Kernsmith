# HintParity — Native vs FreeType output parity

Measures how close the Native backend's output is to the FreeType backend's default (hinted) output, the look [phase 188](../../plan/phase-188-native-hint-levels.md) targets. Both backends generate through the public `BmFont.Generate` path (cell-height sizing, anti-alias negotiation, no kerning), so the comparison is what a user gets, and every glyph is cut out of the atlas by its `.fnt` rectangle.

## Run

```
dotnet run -c Release --project tests/hint-parity/HintParity -- [options]
```

| Option | Meaning | Default |
|---|---|---|
| `--fonts a,b` | Fixture names or stems (`Roboto-Regular`), system font names (`arial`), or paths | Roboto-Regular, NotoSans-Regular-Latin, SourceSerif4-Regular, DancingScript-Variable, RobotoFlex-Latin, plus `arial` and `georgia` when the OS font folder has them |
| `--sizes 8,12` | Pixel sizes (BMFont cell height) | 8,10,12,14,16,24 |
| `--aa grayscale,none` | Anti-alias modes | both |
| `--charset ascii\|latin` | Printable ASCII, or ASCII + Latin-1 | ascii |
| `--native spec` | A Native configuration, repeatable; replaces the defaults. `label:aa=cell\|grayscale\|light\|none,hinting=on\|off,hintlevel=default\|none\|light\|normal\|full` | `default:aa=cell,hinting=on` (Native's default: `Normal` with its size taper) and `light:aa=light,hinting=on` (also `Normal` now that the anti-alias mode no longer picks a hinter; use `hintlevel=light` for the y-only fitter; skipped for `none` cells) |
| `--out dir` | Output folder | `tests/hint-parity/output` (gitignored) |
| `--png [Font:Size,...]` | x4 strips for every cell, or only the listed font:size pairs | none |
| `--text-png` | With `--png`: also `text-<cell>.png`, the sample lines set with each side's advances (row 1 FreeType, row 2 Native) | off |
| `--bar` / `--no-bar` | Enforce the parity bar in the exit code | enforce |
| `--legibility` | Score both sides on the legibility metric below (adds `legib FT/Nat` and its nine term columns; detail and the pairwise-page features in `parity.json`) | off |
| `--weights <file>` | Term weights for the score (implies `--legibility`): a JSON object of term name to weight (`{"topology": 0.2, ...}`, unlisted terms weigh nothing), or the pairwise page's `fit_bt.py` `weights.json`, whose per-feature weights are mapped onto the terms by sign (see below). Normalised to sum to 1; unknown names, negative weights and an all-zero result are errors (exit 2) | the table below |
| `--tuning k=v,...` | Override the Native `Normal` hinter's constants (`HintTuning`) for the run, e.g. `MaxShift=0.5,XHeightRoundUp=0.85` | shipped values |

Exit codes: `0` every cell passes the bar (or `--no-bar`), `1` a cell fails it, `2` an error (bad option, a font that failed to generate).

FreeType is always its default: the cell's anti-alias mode with hinting on, which is `FT_LOAD_DEFAULT` — the font's own engine when it has one. The tool reports which engine that is per font from the table directory (`cff`: CFF/CFF2, Adobe engine; `bytecode`: `fpgm` or glyph instructions, TrueType interpreter; `bytecode-prep-only`: `prep` but nothing to run, plus whether `head.flags` bit 3 asks for integer ppem; `autohint`: a `glyf` font with none of those).

## Output

- **stdout / `summary.txt`** — one row per (font, size, aa, Native configuration).
- **`parity.json`** — the same per cell plus per-glyph detail (advances, boxes, SSIM, max/mean difference, XOR count) and the run options.
- **`strip-<cell>.png`** (with `--png`) — three rows, x4 nearest-neighbour: FreeType, Native, diff (gray where equal, magenta where different, brighter for a larger difference). Glyphs are in codepoint order, each on its own bearing-aligned canvas.

## What is measured

Per glyph present on both sides (glyph boxes are compared baseline-relative — `yoffset - base` — so a `base` disagreement shows up once, in the line-metrics column, not in every yoffset):

| Column | Meaning |
|---|---|
| `line=` | `lineHeight` and `base` equal |
| `adv%` | glyphs whose `xadvance` matches |
| `box=%`, `box+-1%` | glyphs whose width/height/xoffset/yoffset all match exactly, or all within ±1 px |
| `SSIM`, `worst` | grayscale: mean and worst per-glyph SSIM (8x8 windows) over the two glyphs placed on a common canvas with 2 px of padding |
| `maxD`, `meanD` | largest and mean absolute pixel difference on that canvas |
| `mono=%` | mono: glyphs bit-identical (same box, no XOR pixels) |

Two statistics need no FreeType at all and are reported for both backends (`FT/Nat`), to tune Native's look directly:

- `sharp` — mean gradient magnitude (central differences, 0..255 units) over lit pixels; higher means harder edges. `solid` beside it is the share of lit pixels that are fully opaque.
- `stemVar` — stem-width consistency: for `i l I | H n m`, the leftmost run at 75% of the glyph's height (below H's crossbar and n/m's arches) gives a coverage-summed width (fractional pixels) and a threshold width (pixels ≥ 128); the table shows the population variance of the coverage width across those glyphs, and the JSON has every width (`stemGlyphs`) plus the threshold variance. A hinter that lands every stem in the same number of columns scores 0.

## The bar

Per cell, from the phase 188 plan: advances 100% identical; ≥ 98% of boxes within ±1 px; grayscale mean SSIM ≥ 0.98 and worst glyph ≥ 0.90; mono ≥ 95% of glyphs bit-identical. Byte identity is not the bar (Native's float pipeline will not reproduce FreeType's 26.6 rounding). The other columns are reported for diagnosis and do not gate.

## The legibility score (`--legibility`)

Parity asks "how close to FreeType"; the legibility score asks "how readable", with no FreeType in the definition. The reference is the font's own **ideal**: the same font rendered unhinted by Native at 8× the size (`Legibility/IdealFont.cs`), which gives each glyph's true topology, the box-filtered 1× image a perfect unhinted renderer would produce, and fractional advances and side bearings. Nine terms, each 0..1, weighted into one score (`Legibility/LegibilityScore.cs`; the pure per-term functions are in `LegibilityMetrics.cs` and unit-tested on synthetic bitmaps in `HintParity.Tests`):

| Term | Weight | What it measures | Why |
|---|---|---|---|
| topology | 0.10 | Mean share of the contrast band (cuts at 80, 128, 176) at which a glyph keeps the ideal's component and hole count | A closed 'e', a filled 'a', a broken 'B', a vanished dot change which letter is seen; the band gives full credit only with contrast to spare. Binary per glyph and cut |
| topologyError | 0.05 | Mean of 1/(1+e), e = the glyph's structural error at the read cut (128): components merged, vanished or shattered, counters gone at every cut or appearing where the ideal has none | Graded where the band term is binary: a 'B' with both counters lost scores below one with one. Its lever is shape |
| brokenRings | 0.05 | 1 − the share of the font's true counters that are whole at the faint cut (64) but open at the read cut | A ring with one weak pixel is how an anti-aliased counter is lost — every grayscale counter Native loses is one. Its lever is ink weight (stem placement, darkening), not shape |
| fidelity | 0.20 | Mean SSIM to the ideal 1× image, best over ±1 px shifts | Hinting earns the other terms by moving ink; this charges it for distorting the shape, not for translating it |
| concentration | 0.15 | Σv² / (255·Σv) over all pixels | Share of ink in solid pixels — the contrast a blur spends |
| separability | 0.15 | Over confusable pairs (c/e/o, a/o, l/I/1, i/l, 8/B, 6/b, O/0, 5/S, 2/Z, h/b, n/h, u/v, rn/m, cl/d, vv/w): min(1, rendered separation / ideal separation), separation = shift-tolerant Σ\|a−b\|/(Σa+Σb) | Direct confusability, normalised so the font's own design limits do not count against the renderer (and so a hinter that snaps 'l' and 'I' to the same height is charged for it) |
| alignment | 0.10 | Half: crispness of flat tops/bottoms at x-height, cap height and baseline (outer row's coverage over the stroke under it). Half: share of letters sharing the modal first/last read row along each line | Even reference lines give word shapes and let the eye track the line |
| stems | 0.10 | 1/(1+4·variance) of stem width in read pixels (≥128) across `i l I \| H n m h r L E F T` | Evenness of typographic colour |
| spacing | 0.10 | Mean of: 1/(1+3·excess stem-pitch spread in "minimum" over the ideal's); share of adjacent pairs in a pangram that do not touch where the ideal keeps ≥ 0.5 px; 1/(1+gap error vs the ideal beyond half a pixel). Gaps are between read columns, not bitmap boxes | Rhythm and collisions |

The three topology terms are one family, weighted together at the 0.20 the band term carried alone: at the read cut a lost feature is charged once between topologyError and brokenRings (by its cause) and again by the band term, which also sees the two outer cuts. A mono rendering has no broken rings by construction (its ink is 0 or 255), so its brokenRings term is always 1.

Scores compare renderings of the same font, size and anti-alias mode; across modes the terms mean different things (mono ink is always concentrated). Validation on Roboto: at 12 px grayscale FreeType 0.846 > Native `Normal` 0.837 > Native unhinted 0.721 (10 px: 0.787, 0.829, 0.737 — Normal ahead of FreeType there); bilevel 12 px 0.896 / 0.878 / 0.892; unhinted Native at 24 px scores 0.895 grayscale / 0.921 mono against 0.737 / 0.830 at 10 px. Native `Normal`'s broken-ring term (0.86 at 10 px, 0.79 at 12) is on a par with FreeType's (0.82 / 0.82); unhinted Native's is 0.32 / 0.14 — nearly every counter it loses is a ring with a weak pixel. `LegibilityRankingTests` pins the robust parts of that (Normal over unhinted at 10 and 12 px grayscale; 24 px over 10 px); the FreeType-versus-Normal order is a few hundredths either way and not asserted.

### The pairwise-page features

`parity.json` also carries, per side, the ten strip-level features the pairwise judgement page's `fit_bt.py` regresses on (`Legibility/LegibilityFeatures.cs`), computed from the glyph set exactly as the script computes them from its strip PNG — the same two sample lines set with the glyphs' own advances on a 3 px margin, one `lineHeight`-tall band per line: `ink_concentration` (lit pixels ≥ 32 that are ≥ 192), `mean_ink`, `sharpness` (mean gradient magnitude over lit pixels), `broken_rings` (per line, holes at 64 minus holes at 128), `holes_128`, `topology_error` (|components at 128 − the text's expected count|), `stem_width_var` (run widths on each line's busiest row), `gap_var`, `gap_mean` and `touching_pairs` (ink gaps between successive glyphs, word spaces excluded). They are strip-level proxies, not the per-glyph terms: a ring touching a solid neighbour keeps its hole at 128 on the strip, for instance. A `weights.json` from the script applied with `--weights` maps each feature onto the term it stands in for — `ink_concentration` → concentration, `mean_ink` → fidelity, `sharpness` → alignment, `holes_128` → topology, `broken_rings` → brokenRings, `topology_error` → topologyError, `stem_width_var` → stems, `gap_var` and `touching_pairs` → spacing — adding its magnitude when the fitted sign agrees with the direction the term rewards and nothing (printed as a note) when it contradicts it; `gap_mean` has no term and separability has no proxy, so a fit-form file leaves separability at 0.

## The constant sweep (`--sweep`)

`--sweep` tunes the Native `Normal` hinter's constants against the legibility score instead of running the matrix. The constants are the `HintTuning` record in `KernSmith.Rasterizers.Native` (`MinEdgeLength`, `MinLoneEdgeLength`, `MergeDistance`, `StandardStemTolerance`, `PhaseShiftPenalty`, `MaxShift`, `MinCounter`, `XHeightRoundUp`, `OriginNudge`, `SlopeToleranceX`, `SlopeToleranceY`, `LeftBearingSlack`, `TaperStartPpem`, `TaperEndPpem`; defaults are the shipped values, and `HintTuning.Current` is what the pipeline reads — Native exposes its internals to this tool for that alone). Coordinate descent: each constant is tried on a fixed grid (`Sweep.Parameters`), a change is kept when the mean score over tuning fonts × sizes × anti-alias modes improves, for `--sweep-rounds` passes (deterministic, so a run is reproducible). Defaults: tuning fonts Roboto-Regular, NotoSans-Regular-Latin, SourceSerif4-Regular, DancingScript-Variable; sizes 8,10,12,14,16; both modes; hold-out `--holdout` SourceCodeVF-Upright and RobotoFlex-Latin, scored at the start and best tuning but never tuned on. `--sweep-params a,b` restricts the constants (the taper window has no effect at or under 16 px and is skipped by default); `--tuning` sets the starting point. Output: `sweep.json` (every evaluation with per-cell terms), `sweep.txt` (term deltas and per-cell scores, tuning set and hold-out), and `text-<Font>-<size>-<aa>-before-after.png` strips for Roboto 10 and 12 (rows: FreeType, Native Normal at the start tuning, Native Normal at the best).

A gain that does not show on the hold-out is overfitting to the tuning fonts; apply a constant only when it improves both and the Native test suite stays green.
