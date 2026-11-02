# Pairwise legibility judgments

Tooling for calibrating the hint-parity legibility score from human judgments: show two
renderings of the same text side by side, record which reads better, and fit the result.

1. **Render** the matrix with the CLI: `gen_matrix2.sh` for round 2, `gen_matrix_round1.sh` for
   round 1. Both use FreeType, GDI, Native unhinted (`nd`) and Native default (`nn`) for Roboto,
   Georgia and Verdana at 10-12 px, grayscale and bilevel. `KERNSMITH_ROOT` sets the repo root
   (default `.`) and `KERNSMITH_RESEARCH_OUT` sets the output folder (default `output/research`,
   which is gitignored).
2. **Build the page**: `python -I make_pairs.py ...` (round 2; `make_pairs_round1.py` is the
   first round's generator) writes a self-contained `pairwise.html`, `pairs.json` and the strip
   PNGs. Open the HTML from the file system and click the more readable side of each pair
   (keys 1/2/0, with undo). It downloads `results.json` at the end.
3. **Fit**: `python -I fit_bt.py results.json` prints a Bradley-Terry ranking by variant, by
   variant × anti-alias and by variant × font, plus a logistic regression of the choices on ten
   measured strip features. It writes `weights.json`, which the parity tool accepts as
   `--weights`. `synth_results.py` produces a synthetic `results.json` with a planted order, to
   check the fit end to end.

Each strip shows the same two lines of text: "The quick brown fox jumps over 13 lazy dogs." and
"Hamburgefonstiv 0123456789 %@&".

The owner's results and fitted weights are one folder up (`../jeremy-2026-11-01-*`); see
`../README.md`.
