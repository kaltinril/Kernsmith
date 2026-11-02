# Phase 188 research

The research behind Native's small-text hinting work (shipped in 0.26.0). The plan is
[../../phase-188-native-hint-levels.md](../../phase-188-native-hint-levels.md); the decisions
these reports led to are recorded in `project_decisions.md`, which wins wherever a report and
the code disagree. The reports are snapshots from 2026-11-01. They describe the code as it
was then, and several of their proposals have since shipped, changed or been rejected.

| Report | What it answers |
|---|---|
| [01-open-source-survey.md](01-open-source-survey.md) | How FreeType, skrifa, ttfautohint, DirectWrite, Core Text, stb, SharpFont, Typography and others hint small text; which engine FreeType picks per font; licence constraints. Background only: Native is written from specs and measurements, and no third-party code was ported. |
| [02-native-improvement-ideas.md](02-native-improvement-ideas.md) | An idea catalogue grounded in Native's code: x-phase alignment, stem classes, zones, x fitting, DP placement, tuning against FreeType, darkening and gamma, plus the evaluation metrics. Also contains the measurement that showed unhinted advances are rounded correctly and that the super-sample path truncated them. |
| [03-exploratory-report.md](03-exploratory-report.md) | "The font already knows": a survey of `hdmx`/`gasp`/`VDMX`/`cvt` across 473 system fonts; the integer-ppem and nearest-x-height results (shipped); `hdmx` advances (later rejected for advances, see `project_decisions.md`); aperture and "what to drop" policy; designer-hinted fonts as ground truth. Negative results: DP placement, CFF private-dict hints and multi-phase atlases. |
| [04-ocr-legibility-experiment.md](04-ocr-legibility-experiment.md) | Tesseract as an objective legibility judge. Usable as a tripwire for bilevel text at 10-16 px; blind in grayscale; not usable as a tuning objective. Includes per-renderer confusion pairs. |
| [05-native-determinism-audit.md](05-native-determinism-audit.md) | Every source of host-dependent output in Native (libm transcendentals, SIMD, FMA contraction, `(int)` casts, interpreter float semantics) and how each was closed, with sources. |

## Data and scripts

- `data/`: raw measurement tables from the exploratory work (`poc_measure.txt`, `profile.txt`, `xheight_rule.txt`, …).
- `scripts/explore/`: the exploratory measurement scripts (table survey, `gasp` decoder, `hdmx` comparison, designer-font comparison, crops). Run them with `python -I` from the repo root. `KERNSMITH_ROOT` sets the repo root (default `.`) and `KERNSMITH_RESEARCH_OUT` sets the output folder (default `output/research`, which is gitignored). The `gen_*.sh` scripts drive the CLI and expect a built `tools/KernSmith.Cli`. A few (`gen_poc.sh`, `metrics_dump.sh`, `poc_measure.py`) exercised environment-gated PoC switches (`KERNSMITH_POC_*`) that were never merged; they are kept as a record of how those numbers were produced.
- `scripts/ocr/`: the OCR benchmark (`ocr_bench.py`, `perline.py`, the noise probes). Needs a local Tesseract.
- The pairwise human-judgment tooling, and the owner's two rounds of results with their fitted weights, live with the parity tool in [`tests/hint-parity/calibration/`](../../../tests/hint-parity/calibration/).
