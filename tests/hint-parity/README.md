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
| `--native spec` | A Native configuration, repeatable; replaces the defaults. `label:aa=cell\|grayscale\|light\|none,hinting=on\|off,hintlevel=default\|none\|light\|normal\|full` | `default:aa=cell,hinting=on` (today's unhinted Grayscale/None) and `light:aa=light,hinting=on` (Native's y-only auto-hinter; skipped for `none` cells) |
| `--out dir` | Output folder | `tests/hint-parity/output` (gitignored) |
| `--png [Font:Size,...]` | x4 strips for every cell, or only the listed font:size pairs | none |
| `--bar` / `--no-bar` | Enforce the parity bar in the exit code | enforce |

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
