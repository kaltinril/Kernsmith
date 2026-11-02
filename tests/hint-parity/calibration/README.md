# Legibility calibration

Pairwise human judgments and the weights fitted from them (Bradley-Terry +
logistic regression over the legibility features, `fit_bt.py` in the
exploratory pairwise page). Apply with `--weights <file>`:

    dotnet run -c Release --project tests/hint-parity/HintParity -- --weights tests/hint-parity/calibration/jeremy-2026-11-01-weights.json ...

| File | Who / when | Pairs | Renders judged |
|---|---|---|---|
| `jeremy-2026-11-01-*` | Jeremy, 2026-11-01 | 36 (1 tie) | Roboto, Georgia, Verdana at 10-12 px, grayscale + bilevel: FreeType, GDI, Native unhinted, Native Normal as of commit 1148282 (before the aperture/hairline/spacing work) |
| `jeremy-2026-11-01-round2-*` | Jeremy, 2026-11-01 (round 2) | 44 (4 ties) | Same fonts/sizes/modes; Native Normal as of afd4de0 (current default). **Use these weights**: fitted on current output. |

Round 2 (leave-one-pair-out accuracy 0.75): preferences in order — fewer broken rings,
fewer topology errors, lower stem-width variance, higher ink concentration, fewer
touching pairs, wider gaps. Head-to-head: grayscale Native Normal vs FreeType 3-4 with
2 ties (parity by eye), Native Normal vs unhinted 7-0; bilevel FreeType beat Native
Normal 7-2 (Georgia every time, Verdana 2-1, Roboto split), Native Normal beat unhinted
9-0 and GDI 2-1; GDI Georgia 12 bilevel beat everything (its bytecode). The bilevel gap
is serif rendering and spacing (narrow word gaps, touching pairs).

Round 1 fit summary (leave-one-pair-out accuracy 0.77): fewer topology errors and
higher ink concentration were the strongest preferences, then darker ink and
even stem widths. Variant ranking: FreeType > Native Normal > Native unhinted > GDI;
FreeType bilevel was preferred almost everywhere; Native Normal grayscale beat
unhinted and sat close to FreeType grayscale. A second round on current
renders is the next step; 36 judgments give wide error bars.
