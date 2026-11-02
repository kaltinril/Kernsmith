# OCR as a legibility judge for small bitmap text - experiment report

Date: 2026-11-01. Read-only experiment; nothing in the repo was touched.

## Setup

- **Engine**: Tesseract 5.5.0 (UB-Mannheim build already installed at `C:\Program Files\Tesseract-OCR`), `eng.traineddata` (4.1 MB = the *fast* integer LSTM model, not `tessdata_best`). Driven via subprocess from `ocr_bench.py` (`python -I`), no pytesseract needed. Output: `txt` + `tsv` (word confidences).
- **Corpus** (11 lines, 356 non-space characters, 76 words): two pangrams, A-Z, a-z, digit groups, a confusables line (`rn m cl d Illegal 1Il O0 8B 6b 5S 2Z 7T vv w`), "Hamburgefonstiv WMgy illicit minimum", three lines of high-frequency English words, and a log-style line. Text laid out from the `.fnt` advances + kerning, **dark on white**, nearest-neighbour upscales x1, x2, x3, x4, plus a Lanczos x4 control.
- **Renderings** (86 total): everything in `scratchpad/mono/base/` (FreeType / Native-normal *after* / Native-normal *before* x Roboto, NotoSans, SourceSerif4 at 8/10/12 px x gray/mono, Arial 12, plus the 64-96 px `ideal-*` set) and everything in `scratchpad/final/` (FreeType / Native normal / Native default x 12/16/24 x gray/mono).
- **Metrics**: CER = Levenshtein distance over characters with all whitespace removed / 356 (spacing is a layout choice, not legibility); WER = sequence word error rate; WRec = bag-of-words recall of ground-truth tokens (robust to Tesseract merging/splitting words); Conf = mean Tesseract word confidence. Two OCR modes: **block** (`--psm 6`, whole page) and **per-line** (`--psm 7`, each corpus line rendered and OCR'd alone; immune to page-segmentation and line-merge artefacts, reported by line class: words / alpha / digits / confusables).
- **Files**: `ocr_bench.py`, `analyze.py` (block-mode tables -> `analysis_block_psm6.md`), `perline.py` (-> `perline.log`, `perline/perline.json`, `perline/img`, `perline/raw`), `noise.py` / `noise2.py` (noise probes, `noise/`, `noise2/`, `noise2.log`), `results.json`, `img/` (rendered corpus at every upscale), `raw/` (Tesseract txt/tsv), `headline_strip_x3.png` (side-by-side of the headline cells), `sweep.log`.

## Headline table - Roboto 10 & 12 (gray + mono), Arial 12 mono

Block OCR at the best nearest-neighbour upscale (by CER); per-line columns at x2 (per-line CER over all lines / over the natural-word lines only).

| Rendering | Size | AA | Best up | CER | WER | WRec | Conf | per-line CER all | per-line CER words |
|---|---|---|---|---|---|---|---|---|---|
| FreeType | 10 | gray | x3 | **19.1%** | 64.5% | 50.0% | 65.4 | **23.3%** | 19.8% |
| Native normal (after) | 10 | gray | x3 | 26.7% | 63.2% | 38.2% | 60.1 | 28.1% | 22.2% |
| Native normal (before) | 10 | gray | x3 | 24.4% | 61.8% | 40.8% | 64.0 | 23.9% | 18.1% |
| FreeType | 10 | mono | x3 | **17.4%** | 57.9% | 42.1% | 61.6 | **16.0%** | 7.0% |
| Native normal (after) | 10 | mono | x2 | 23.3% | 55.3% | 47.4% | 57.1 | 22.2% | 12.8% |
| Native normal (before) | 10 | mono | x2 | 26.4% | 68.4% | 42.1% | 48.6 | 21.9% | 13.2% |
| FreeType | 12 | gray | x2 | 6.7% | 34.2% | 67.1% | 79.7 | 6.7% | 0.4% |
| Native normal (after = before, identical bitmaps) | 12 | gray | x2 | **6.5%** | 30.3% | 72.4% | 74.2 | **6.2%** | 0.4% |
| Native default (unhinted) | 12 | gray | x2 | 8.4% | 26.3% | 75.0% | 79.3 | 7.3% | 0.8% |
| FreeType | 12 | mono | x2 | **7.0%** | 39.5% | 63.2% | 75.8 | **8.1%** | 2.1% |
| Native normal (after) | 12 | mono | x2 | 12.4% | 42.1% | 60.5% | 67.2 | 11.5% | 6.6% |
| Native normal (before) | 12 | mono | x2 | 11.5% | 43.4% | 57.9% | 72.4 | 11.8% | 6.6% |
| Native default (unhinted) | 12 | mono | x2 | 19.1% | 50.0% | 50.0% | 56.1 | 20.5% | 5.8% |
| FreeType | Arial 12 | mono | x2 | **7.6%** | 21.1% | 78.9% | 79.7 | **7.3%** | 2.5% |
| Native normal (after) | Arial 12 | mono | x3 | 7.6% | 25.0% | 77.6% | 74.1 | 9.0% | 3.7% |
| Native normal (before) | Arial 12 | mono | x1 | 8.4% | 31.6% | 68.4% | 76.4 | 9.8% | 4.5% |

Reference ceiling: the 64-96 px `ideal-*` renderings still score **CER 0.8-2.0 %, WER 5-10 %, WRec 90-95 %** at x1. That floor is almost entirely the confusables line and the bare alphabets (per-line "confus" CER is 23-32 % even at 24 px and 96 px), so absolute numbers below ~2 % CER / ~8 % WER are not reachable by any renderer with this corpus.

Full tables for every rendering (NotoSans, SourceSerif4, 8 px, 16/24 px, all upscales) are in `analysis_block_psm6.md`; per-line numbers are in `perline.log`.

## Does OCR reproduce the expected ranking FreeType > Native normal > Native default?

**Mono (bi-level) at 12 and 16 px: yes, cleanly.** FT < normal < default on CER at every upscale, in both block and per-line mode (12 px: 7.0 / 11.5 / 19.1; 16 px: 4.2 / 5.6 / 6.7). Confidence agrees (75.8 / 72.4 / 56.1). The gap between normal and default is large (7-8 pp), between FT and normal moderate (1.5-4.5 pp).

**Mono at 10 px: FT wins by a wide margin (17.4 vs 23-26 %).** Per-line "words" CER is the starkest: FT 7.0 % vs Native 12.8-13.2 %. Native normal mono at 10 px loses mostly on `s->e`, `o->e/a`, `m->n/i`, `c->e` - counters and two-storey letters close up - which matches what a human sees in `headline_strip_x3.png`.

**Grayscale: OCR does not separate the backends.** At 12 px gray the three renderers are within 2 pp (6.5-8.4 %) and the order flips with the upscale (normal < FT < default at x2; FT < normal < default at x3). At 16 px gray: FT 2.8, default 3.1, normal 3.4 - default beats normal. At 24 px everything is 1.4 %. Tesseract's LSTM was trained on anti-aliased text, so once there is grey it is very forgiving and the residual errors are corpus-floor errors (Q->O, Z->2, l->i, O->0), not renderer errors.

**Disagreements with the human / legibility-score ranking:**
- Gray 10 px: FT 19.1 but *before* (24.4) beats *after* (26.7); at 8 px gray and mono *before* beats both *after* and FT (Roboto 8 mono: before 40.7, after 48.0, FT 49.7). At 8 px Tesseract is at 40-60 % CER, i.e. guessing, and rankings there are noise.
- Roboto 24 mono: normal (2.0) beats FT (2.8). NotoSans 10/12 mono and SourceSerif4 mono at all sizes: Native normal beats FT by a lot (NotoSans 10 mono: after 17.4 vs FT 31.5; SourceSerif4 12 mono: after 9.8 vs FT 23.3). FreeType mono's top "dropped" characters are `t` x29, `l` x28, `i` x19, `I` x14 - thin 1-px stems that Tesseract's internal downscale erases - so part of this is engine bias against light bi-level stems rather than a human-visible FT defect. Worth a look by eye: `img/ft-NotoSans-Regular-Latin-none-10_x3.png` vs `img/after-NotoSans-Regular-Latin-none-10_x3.png`.

## Which letters trip each renderer (pooled, per-line mode, sizes 10-12)

- **FreeType**: `l->i`, `c->e`, `Z->2`, `Q->O`, `v->w`, `v->y`, `0->O`, `x->a`, `6->0`, `r->m`. Mono FT additionally *drops* thin stems (`t`, `l`, `i`, `I`) and reads `o->a`, `t->l`: light 1-px stems.
- **Native normal (after)**: `c->e` (x24), `0->O`, `s->e`, `Q->O`, `m->i`, `m->n`, `Z->2`, `r->m`, `i->l`, `f->t`, `5->S`. Mono: `e->a` (x29), `o->e`, `o->a`, `w->e`. Apertures/counters filling in (`c e s o` closing up) and arch letters merging (`m`).
- **Native normal (before)**: same family but more `s->e` (x19-24), `c->o`, `m->a`, `o->e` - more closed counters than *after*; the mono renderer change reduced `s` and `m` errors at 10 px.
- **Native default (unhinted)**: `5->S`, `l->i`, `0->O`, `x->o`, `d->c`, `J->U`, `6->E`, `g->p`, `8->B`, `1->T`; **digits are catastrophic in mono** (per-line digits CER 77-100 % at 12 px vs 3-17 % for FT/normal). Unhinted bi-level thresholding breaks the digit shapes outright - the clearest OCR win in the study.
- Common to all renderers *and* the 96 px ideal set: `Q->O`, `Z->2`, `O->0`, `l->1/i/I` - Tesseract's own priors, not renderer defects.

## Is OCR usable as a tuning objective, or only as a validator?

**Sensitivity to before vs after (the mono renderer change).** First, the change is not "a few letters": pixel-diffing the atlases shows 18/20/28 of 94 Roboto glyphs differ at 8/10/12 px mono and 59/94 for Arial 12 mono; Roboto grayscale 8/10 differ by small tonal deltas across nearly all glyphs; grayscale 12 is byte-identical (and OCR duly returned identical numbers). OCR's verdict on after vs before:

| Cell | after - before CER (pp), block best | per-line x2 | per-line x3 | verdict |
|---|---|---|---|---|
| Roboto 10 mono | -3.1 | +0.3 | -3.4 | after better at x3, tie at x2 |
| Roboto 12 mono | +0.8 | -0.3 | +0.6 | tie |
| Arial 12 mono | -0.8 | -0.8 | -1.4 | after slightly better |
| Roboto 8 mono | +7.3 | +7.3 | +5.1 | before better (but at ~45 % CER) |
| NotoSans 10 / 12 mono | -2.0 / +0.3 | - | - | tie / after |
| SourceSerif4 8 / 10 / 12 mono | -44 / -26 / -12 | - | - | after much better |

So OCR *does* detect the change where it is large (SourceSerif4, where *before* evidently broke the serif strokes) and gives a weak, upscale-dependent signal where it is subtle (Roboto). It cannot resolve +/-1 pp.

**Noise characteristics - the important caveat.** Tesseract turned out to be *fully deterministic* under every nuisance perturbation tried: five page layouts (margins 11-14 px, leading 1.6-2.2) and all sub-glyph-pixel translations of the upscaled image (4 shifts at x2, 9 at x3) gave bit-identical CER for all 14 headline renderings (`noise.py`, `noise2.py`, `noise2.log`). So there are no error bars to average down. Instead the noise is *systematic and chaotic*: the same font swings 3-6 pp between x2 and x3 (FT Roboto 12 mono: 7.0 -> 11.0 -> 8.4 at x2/x3/x4; Native-after Arial 12 mono: 9.6 -> 7.6 -> 8.1), and changing one glyph changes how neighbours are read (the LSTM is contextual: at Roboto 12 mono, `X`, `k`, `s`, `w`, `x` changing moved errors in `jumps`, `lazy dog`, `minimum`). A tuner that minimised CER directly would optimise those quirks - e.g. learn whatever makes `s` not look like `e` to Tesseract at x2, which is not the same as what a human prefers at x1.

**Upscale choice.** x1 is useless below 12 px (CER 80-100 %; Tesseract's internal normalisation wants >= ~20 px x-height). x2 nearest wins most often (48/76 renderings), x3 next (16/76). Lanczos x4 beats the best nearest upscale in 56/76 cases and *compresses* the gaps between renderers (Roboto 12 mono: FT 3.9 / after 9.6 / before 6.7 vs nearest 7.0 / 12.4 / 11.5) - smoothing repairs bi-level text toward the engine's training distribution and hides exactly the mono-rendering differences of interest. Use nearest-neighbour only, and report x2 *and* x3 (or their mean) rather than the best of several, because "best upscale" cherry-picks noise.

**Verdict.**
- *As a validator*: yes, with constraints. In **mono at 10-16 px** OCR reproduces the human ranking FT >= Native normal >> Native default, exposes unhinted mono's digit failure unambiguously, and its confusion lists point at real defects (closed counters in `c e s o`, merged `m`, dropped thin stems). Use per-line `--psm 7`, nearest x2 and x3, natural-word lines weighted separately from the confusables line, and treat differences under ~3 pp CER as a tie.
- *As a tuning objective*: no. It is blind in grayscale at >= 12 px (all renderers within noise), it is guessing at 8 px, it rewards smoothing/regularity over faithfulness, and its deterministic-but-chaotic response to irrelevant factors (upscale, neighbouring glyphs) means a hill-climber would chase engine artefacts. The existing legibility score plus eyes should stay primary; OCR fits as a cheap regression tripwire run afterwards (e.g. "Native normal mono 12 px per-line words CER must stay < FT + 5 pp; default mono digits must not get worse").

## Other caveats

- Training bias: the `eng` LSTM is trained on anti-aliased text; bi-level input is out-of-distribution, so mono penalties are partly engine bias (FT mono 10 px scored *better* than FT gray 10 px here, which no human would say - gray 10 px Roboto at x1 is clearly easier to read).
- This used `tessdata_fast`; `tessdata_best` would lower absolute CER but not change the structural problems above.
- WER in block mode is inflated by Tesseract fusing words set with the font's tight 2-3 px space advance at small sizes ("overthe", "lazydog.") and occasionally re-ordering lines; WRec or per-line CER are the numbers to quote.
- Corpus matters: with the confusables/alphabet lines removed, Roboto 12 gray/mono natural-word CER is 0.4-7 %, and the renderer gaps are proportionally larger there (FT 2.1 vs normal 6.6 vs default 5.8 in mono 12), i.e. real-word legibility is where OCR is most informative.
- One engine, one corpus, one layout, Windows Tesseract build; no human-rating ground truth was collected to calibrate against beyond the owner's stated ordering.
