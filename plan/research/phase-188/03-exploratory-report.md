# Exploratory report — small-text quality (2026-11-01)

Worktree base 1148282. All C# changes are env-gated `// PoC` blocks; Default output untouched.
Every table/image cited is under this folder (`scratchpad/explore/`).

## Ranking — (measured gain × confidence) / effort

| # | Idea | Gain measured | Confidence | Effort | Verdict |
|---|---|---|---|---|---|
| 1 | Integer ppem under Normal for bytecode/bit-3 fonts + `hdmx` advances + linear@int fallback | hdmx = FT hinted mono 94/94 wherever present; Verdana ident 106→181/940, xh+cap 10/10; Tahoma 91→132; gray advances +30–66/940 on all four MS fonts | high | small | do first |
| 2 | Mono aperture policy (e a s c) + `s` spine as bar ≤ 5 x-height rows; minimum y-stroke weight for hairlines in gray (Georgia class) | FT autohinter mono shapes vs ours (crops); Georgia gray counters FT 23–31 vs ours 1–15 at 10–16 px | high on defect, medium on rule | medium | do second |
| 3 | `gasp`-driven taper floor | 79 % of gasp fonts: no grid-fit ≤ 8 ppem (252 at 8, 10 at 7, 17 at 9, 15 at 10); bilevel band 9–16/17 | medium | tiny | confirm with pairwise page |
| 4 | x-height = nearest at integer ppem (not floor(x+2/3) at fractional) | designer agreement 32/40 vs 28/40; Verdana/Tahoma exact 10/10; Arial/Georgia designers push up per-ppem | medium | tiny | A/B on the page |
| 5 | Broken-ring metric (hole at 64, gone at 128) + topology in legibility score | every gray "closed counter" in our output is a broken ring | high | tiny | add to hint-parity |
| 6 | Unrounded advances in extended metadata | fixes −18.5…+11 px line drift per 89 chars; 2-phase atlas only −30 % RMS gap error | high | tiny | metadata yes, phases no |
| 7 | CFF private dict / `cvt` as hint source | measured 86/46u vs StdVW 85/StdHW 46 (SourceSerif); zones = BlueValues | high (negative) | — | drop |
| 8 | DP placement (`KERNSMITH_POC_DP=1`) | lost counters 105→112, extra holes 181→164, ΔSSIM −0.0035, +5–15 µs/axis | high (negative) | — | drop; keep `KERNSMITH_POC_DUMP` |
| 9 | Pairwise page + Bradley–Terry fit | built, self-tested | tool | done | owner runs it |

## 1. Tables ("the font already knows")
Survey `survey_tables.py` → `survey_tables.txt/json` (9 fixtures, 473 system fonts): hdmx 0 % / 42 %, VDMX 0 / 46 %, LTSH 0 / 41 %, gasp 44 / 77 %, cvt+fpgm+prep 11 / 90–93 %, CFF 22 / 4 %. hdmx present in Arial/Georgia/Tahoma/Times/SegoeUI/Trebuchet; absent in Verdana (this machine), Roboto, all fixtures.

1a. Advances (`hdmx_compare.txt`), ASCII glyphs of 94 whose xadvance equals FreeType hinted mono: Arial 12/13/14: Native-normal mono 50/60/68, gray 41/60/66, Native default 68/49/55, linear@frac 68/49/55, linear@int 69/82/84, **hdmx 94/94/94**. Georgia 10–20 and Tahoma 11–20: hdmx 94 at every size; ours 29–65. Tahoma 9/10 and Verdana 8/9/10 (no hdmx entry): linear@int **94/94**, ours 24–47. Roboto 12 (autohinted by FreeType): ours 65 vs linear 41–44 — the rebuild is right for autohint fonts. FreeType's cap-height rows track cap@int-ppem (Arial `-s 13`: 8.59→9 rows, not 8.33→8).

1b. PoC (`poc_measure.txt`; `GlyphHinter.ApplyNormal` rounds scale×upem under `KERNSMITH_POC_INTPPEM=1`; `NormalFontMetrics.Scale` uses 0.5 instead of 2/3 under `KERNSMITH_POC_XHNEAREST=1`). Totals over 10 sizes × 94 glyphs, variants nn (current) / ni (int ppem) / nx (int ppem + nearest xh):
- Verdana: mono adv 522/571/571; gray adv 592/658/658; xh rows equal FT 7/8/10 of 10; cap 6/8/10; mono bit-identical 106/141/181.
- Tahoma: 481/466/470; 632/687/691; xh 7/8/10; cap 6/8/10; ident 91/110/132.
- Arial: 487/482/483; 648/682/682; xh 7/7/6; cap 7/8/9; ident 118/135/139.
- Georgia: 480/465/464; 643/682/682; xh 6/5/6; cap 7/9/10; ident 65/64/66.
- Roboto (no bytecode; FreeType autohints at fractional ppem): 550/538/538; 718/671/671; xh 10/9/8; cap 10/8/8; ident 233/193/172 → gate the rule on bytecode presence.
Mono advances barely move because the PoC did not change the advance rule — hdmx/linear@int is the remaining step. Productising: read `head.flags` bit 3 + bytecode presence; apply under Normal only; `hdmx` reader (per-ppem records, byte per glyph) for unstyled glyphs at the default instance; else round(linear at int ppem) for bytecode fonts, rebuild for autohint fonts. Risks: none for determinism/BMFont; glyphs up to ±4 % off the fractional size inside the same cell (FreeType's already are).

1c. gasp (`gasp_decode.py`): most common tables `(8:2,16:1,∞:3)`, `(8:2,∞:3)`, `(8:10,19:7,∞:15)`; Georgia/Arial/Tahoma/Verdana: ≤8 DOGRAY|SYMSMOOTH (no grid-fit), 9–16/17 GRIDFIT|SYMGRID (bilevel), then all. Roboto has no gasp.

1d. CFF/cvt negative (`metrics_dump.sh`, `KERNSMITH_POC_METRICS=1`): SourceSerif measured stdV 86u/stdH 46u vs StdVW 85 (StemSnapV 85,95)/StdHW 46 (StemSnapH 36,46); zones B0 T475 T670 T740 B−240 vs BlueValues (we miss only the 527/549 pair). Noto CFF 89/74 vs 90/79, zones identical. Verdana/Tahoma cvt contains xh 1117 and cap 1489 but is opaque without prep.

## 2. Counters and rings
Fork B (`dp/README.md`, `holes.txt`, `diff.txt`, crops `dp/renders/crop_*`): search trades defects; lost-counter glyphs identical under greedy and DP. By-product: every grayscale lost counter exists at threshold 64 and vanishes at 128 (broken ring). `profile.txt`: Roboto `o` weakest-row coverage FT 188/187 (10 px), 178/177 (12 px) vs ours 165/165, 150/152. Verdana gray: FreeType default (v40, y-only) splits `o` sides over two columns, ours concentrates them — we beat FreeType's default there. Georgia gray `e` at 11 px: FT bar solid 255 (1 hole at 128); ours bar 111, 0 holes at 128. Georgia gray holes over ASCII FT 23/31/29/29/23 at `-s 10–14` vs ours 2/1/3/13/15.

## 3. Shape policy
Crops `crops/focus_roboto_mono_9-12.png`, `focus_verdana_mono_9-11.png`, `focus_roboto_gray_8-10.png`, `focus_georgia_mono_10-12.png`, full sheets `crops_<font>_<aa>.png`. FreeType autohinter Roboto mono: `s` = 3 bars + 2 one-pixel verticals, `e` = c+bar with open aperture, `a` with bowl; ours `s` ring/`8`, `e` `o`-with-nub, `a` blob. Verdana designer at 8 ppem: x-height 4, `e` eye dropped, clean `s`; ours 5 rows with an eye but broken `s`. Gray Roboto 8–10: ours ≈ FreeType. Policy: aperture > eye > bar > terminal; round pair on the open side must not widen into the aperture; `s` spine as a snapped bar ≤ 5 rows; decide by topology proxy (components, holes vs expected).

## 4. Designer fonts — `designer.txt`, `xheight_rule.txt`
Stems 1 px for everyone to ~16 ppem (agree). x-height/cap: ours +1 row at Verdana/Tahoma `-s 10`, `-s 12`, −1 at `-s 14` (fractional ppem + floor(x+2/3)); nearest@int matches designer 32/40. `o` 1 px wider than designer at Verdana 11/16, Georgia 11–14, Arial 11–20 (bowl pair widens outward). Universal: integer ppem for bytecode fonts, 1-px stems, nearest xh default. Font-specific: xh push (xh/upem 0.48–0.52 fonts push up; 0.545 Verdana/Tahoma do not), hdmx, gasp. Signature = {bytecode present, gasp first range, hdmx present, xh/upem}.

## 5. Pairwise page — `pairwise/{pairwise.html, make_pairs.py, fit_bt.py, synth_results.py, README.md, pairs.json, strips/}`
36 pairs, ~1–2 min, file:// only. `python -I pairwise/fit_bt.py results.json` → Bradley–Terry ranking + regression → `weights.json`; synthetic self-test recovers a planted order exactly. Features regressed: ink_concentration, mean_ink, sharpness, broken_rings, holes_128, topology_error, stem_width_var, gap_var, gap_mean, touching_pairs. To test ideas 3/4, regenerate with the `ni`/`nx` renders in `renders/`.

## 6. Multi-phase — `multiphase.txt`
1-phase RMS 0.18–0.38 px, max ≤ 0.5, drift −18.5…+11.3 px/89 chars; diffused pen: no drift but RMS 0.3–0.47; 2 phases RMS 0.10–0.24; 4 phases ≈ 0.10/0.23. Encoding possible but no stock renderer uses it → ship unrounded advances/bearings in extended metadata instead.

## 7. Other
Stem darkening curve would over-darken straight stems; target round-edge concentration and hairline minimum weight instead. Diagonal regularity: `s` spine-as-bar. AA-but-snapped / vertical supersample: subsumed by ideas 1 + 4.

## Next phase
1. Integer ppem + hdmx + linear@int for bytecode fonts (accept: advances vs FT mono ≥ 90/94 on the four MS fonts 8–16 px; Verdana/Tahoma xh rows = FT every size).
2. Mono aperture/`s` policy + gray hairline minimum weight (accept: topology = FT autohinter on Roboto mono 9–12 px for e a s c 8 B; Georgia gray holes ≥ 20 at 10–14 px).
3. Owner runs pairwise.html ×2 → weights; decides xh rule and ≤ 8 ppem floor.
4. Broken-ring + topology metrics in hint-parity; fractional advances in metadata.
5. Drop DP, CFF hints, multi-phase.

## PoC files (env-gated, `// PoC`; prototype worktree since deleted — integer ppem and nearest x-height shipped in 0.26.0, DP placement rejected)
`StemFitter.cs` (SearchPlace; KERNSMITH_POC_DP/TIME/DUMP), `NormalHinter.cs` (tags), `NormalFontMetrics.cs` (KERNSMITH_POC_XHNEAREST), `GlyphHinter.cs` (KERNSMITH_POC_INTPPEM, KERNSMITH_POC_METRICS). Scripts: `gen_matrix.sh`, `gen_poc.sh`, `fntlib.py`, `survey_tables.py`, `hdmx_compare.py`, `designer.py`, `xheight_rule.py`, `poc_measure.py`, `profile.py`, `multiphase.py`, `crops.py`, `focus.py`, `gasp_decode.py`, `metrics_dump.sh`.
