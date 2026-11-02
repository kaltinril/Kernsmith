# Phase 188 — Native Rasterizer: Hint Levels and FreeType-Quality Hinting

> **Status**: In progress — step 1 (the `HintLevel` API) is in; step 2 (Native `Normal`: both-axis auto-hinter within the font's advances, bilevel fit-then-threshold, size taper) is in and advertised; step 4's default switch is done: `Default` resolves to `Normal` on Native with the taper (full to 16 ppem, x off by 24, y off by 40) and the kerning bake on; steps 3 and the font-native engines not started; the parity bar is not met (see the harness summary)
> **Created**: 2026-11-01
> **Depends on**: [Phase 174](done/phase-174-auto-hinting.md) (y-only fitter, blue zones, mono dropout), [Phase 177](phase-177-performance-optimization.md) (benchmark harness), [Phase 179](phase-179-validation-golden-masters.md) (`NativeSimilarityMatrixTests`, `ImageSimilarity`)
> **Supersedes**: the Phase 174 decisions "hinting stays `Light`-only" and "no x fitting" — see [Decision changes](#decision-changes)

## Goal

Native output at small sizes that **matches the FreeType backend's default output** — the one backend the project owner judges acceptable at 12px — for grayscale and mono, measured by an objective parity harness, without slowing generation past the budget below. Lower levels (unhinted, y-only) stay available for callers who want them, but are not polished: the work goes straight at parity.

## Evidence

Roboto-Regular 12px (BMFont cell-height sizing, so ~9.1 ppem), printable ASCII. Comparison images: `compare_x4.png`, `mono_x4.png`, `best/BEST_x4.png` in the session scratchpad. FreeType is crisp and wider; GDI, DirectWrite, StbTrueType and Native are blurry and cramped; FreeType mono is far cleaner than any other mono.

### What the FreeType backend actually does by default

- **Size**: `FT_Set_Char_Size` with the fractional ppem in 26.6 (`FreeTypeRasterizer.cs:565-566`).
- **Load flags**: `FT_LOAD_DEFAULT` (= `FT_LOAD_TARGET_NORMAL`), plus `FT_LOAD_TARGET_MONO` for `AntiAliasMode.None`, plus `FT_LOAD_NO_HINTING` only when `EnableHinting` is false (`FreeTypeRasterizer.cs:183-195`). It never passes `FT_LOAD_TARGET_LIGHT` or `FT_LOAD_FORCE_AUTOHINT`.
- **Render mode**: `Light` → `FT_RENDER_MODE_LIGHT`, otherwise `NORMAL`/`MONO`/`LCD` (`FreeTypeRasterizer.cs:226-235`) — chosen *after* the glyph was already hinted at load time.
- **Advance**: `metrics.horiAdvance` rounded `(v + 32) >> 6` (`FreeTypeRasterizer.cs:260`, `:739-743`); bearings from `bitmap_left`/`bitmap_top` (`:255-259`). `lsb_delta`/`rsb_delta` are ignored (BMFont has nowhere to put them).
- **Which hinting engine runs** is FreeType's own per-font choice, not ours. With `FT_LOAD_DEFAULT` the font's native hinter is preferred; a TrueType font with no `fpgm`, no `prep` and `maxp.maxSizeOfInstructions == 0` has nothing to run, so FreeType falls back to its **auto-hinter**. Measured (FreeType hinted vs `--no-hinting`, 12px ASCII, 95 glyphs; the probe outputs are in the session scratchpad `ftprobe/`):

| Font | Tables | FreeType engine | Advances changed | x boxes changed | y boxes changed | Mono advances ≠ gray |
|---|---|---|---|---|---|---|
| Roboto (fixture) | no `fpgm`/`prep`, no instructions | **auto-hinter, normal target** | 30 (net **+14 px**) | 53 | 42 | 28 |
| NotoSans-Latin | `fpgm`+`prep`+`cvt` (ttfautohint) | TrueType bytecode, v40 | 0 | 0 | 74 | 0 |
| Arial | full bytecode | TrueType bytecode, v40 | 11 (+11) | 14 | 52 | 25 |
| Georgia | full bytecode | TrueType bytecode, v40 | 21 (+21) | 16 | 60 | 62 |
| SourceSerif4 (`.otf`) | CFF | CFF hinter (Adobe engine) | 0 | 0 | 46 | 0 |
| RobotoFlex | `prep` only, no glyph instructions, `head.flags` bit 3 | bytecode (nothing to run) | 0 | 3 | 6 (93 glyphs' pixels differ) | 0 |
| DancingScript | `prep` only, bit 3 clear | bytecode (nothing to run) | 0 | 0 | 0 (1 glyph differs) | 0 |

Readings:

1. **The user's case (Roboto) is the auto-hinter in normal mode**, which hints *both* axes: vertical stems snap to whole pixels (x boxes change) and the advance is rebuilt from the hinted edges, which at small sizes adds a pixel to about a third of the glyphs. That extra space is most of why FreeType reads "not cramped".
2. **Bytecode fonts are y-only in grayscale** (v40 ignores horizontal instructions — FreeType's subpixel-hinting docs). Arial/Georgia advances still grow, but **only through integer ppem**: Arial's 12px cell is 10.74 ppem, hinted at 11; every hinted Arial advance equals the linear advance at ppem 11, rounded (0 mismatches; Georgia 1 of 95). Mono runs the full (v35-style) interpreter, x included, so mono advances differ again.
   Roboto's growth is not a ppem effect: no single ppem reproduces its hinted advances (30 mismatches at 9.10, 33 at 9, 51 at 10). It is the auto-hinter's side-bearing rebuild, and the mono target adds far more (+52 px over ASCII vs +14 in grayscale).
3. **CFF is y-only** with unchanged advances.
4. **`prep`-only fonts change only through integer ppem**: hinted TrueType loading rounds the ppem to an integer when `head.flags` bit 3 is set (RobotoFlex: every glyph shifts slightly; DancingScript, bit clear: nothing changes). Unhinted loading keeps the fractional ppem.
5. GDI renders Roboto unhinted because the fixture has no bytecode and GDI has no auto-hinter; with Arial it would be crisp. DirectWrite uses `NATURAL_SYMMETRIC` with natural measuring (`DirectWriteRasterizer.cs:480-497`) — y-only smoothing, unrounded design advances.

So "FreeType quality" is three engines selected per font, plus integer-ppem scaling. Parity scope follows that split (see [Survey](#survey) and the step order).

### `--aa light` is byte-identical to grayscale on every backend but Native

- **FreeType: a bug.** `Light` only changes the *render* mode (`FreeTypeRasterizer.cs:233`); the hinting target is fixed at *load* time (`:183-195`), where `Light` still loads `FT_LOAD_TARGET_NORMAL`. Rendering an already-hinted outline in `LIGHT` vs `NORMAL` gives the same coverage, so `Light` is normal hinting. The fix is `FT_LOAD_TARGET_LIGHT` at load for `Light` (and for `HintLevel.Light`, below). It changes FreeType's `Light` output (the `hint-*.bmfc` harness configs on FreeType), so it is listed as a decision.
- **GDI, DirectWrite, StbTrueType: by design.** None advertises `Light` (`GdiRasterizer.cs:754-755`, `DirectWriteCapabilities.cs:21-24`, `StbTrueTypeCapabilities.cs:20-21`), so core's `AntiAliasNegotiation` hands them `Grayscale` (the recorded "unadvertised modes degrade silently" decision).

### Are unhinted advances truncated? No — but supersampled ones are

- Unhinted advances are **rounded** on every backend: Native `GlyphPipeline.PixelAdvance` rounds half away from zero (`GlyphPipeline.cs:125-126`); StbTrueType `Math.Round` (ties to even, `StbTrueTypeRasterizer.cs:121`); DirectWrite `Math.Round` (`DirectWriteRasterizer.cs:289`, `:459`); FreeType unhinted 26.6 then `(v+32)>>6` (`FreeTypeRasterizer.cs:739-743`, the known two-step rounding); GDI takes GDI's own `gmCellIncX` (`GdiRasterizer.cs:419`). Native and FreeType unhinted advances agree on all 95 Roboto glyphs at 12px.
- 'h' is 5 unhinted because its linear advance is 1128 × 9.099 / 2048 = **5.01 px**; FreeType's 6 is the auto-hinter's rebuilt advance, not a rounding fix. Native is not truncating.
- **Separate real bug**: with super sampling, core divides the hi-res advance with integer division, truncating — `Advance = m.Advance / level` (`PreparedRasterization.cs:910`, blank glyphs) and `Advance: metrics.Advance / level` (`:935`). Native `aa=4`: 'e' 19 px at 4× → 4 (4.75 should be 5); FreeType `aa=4`: 'h' 5 → 4, 'm' 8 → 7. Every backend's supersampled output is cramped by this. It may be deliberate BMFont parity (bearings are documented as truncating on purpose), so step 0 checks BMFont64 `aa=2/4` output before changing it.

> The research reports behind this plan (open-source survey, idea catalogue, exploratory measurements, OCR experiment, determinism audit) are in [research/phase-188/](research/phase-188/README.md).

## Survey

### FreeType's engine selection (`FT_LOAD_DEFAULT`)

| Font | Engine | x | y | Advance |
|---|---|---|---|---|
| `glyf`, no `fpgm`/`prep`/instructions | auto-hinter (target from load flags) | normal/mono: yes; light: no | yes | normal/mono: rebuilt from hinted edges; light: linear, rounded |
| `glyf` with bytecode | TrueType interpreter v40 (grayscale), v35-style (mono) | v40: ignored; mono: executed | executed | grayscale: linear advance at the integer ppem (`head.flags` bit 3), rounded; mono: hinted phantom points (possibly `hdmx`) |
| CFF / CFF2 | Adobe CFF engine (font's `hstem`, BlueValues, BlueScale/Shift/Fuzz, `hintmask`) | no | yes | unchanged (rounded linear) |

### Auto-hinter (FreeType "latin" writing system), per target

Global, once per face (cached) and per size:

- **Standard widths**: dominant horizontal and vertical stem widths, measured from a reference glyph ('o').
- **Blue zones** from reference strings: capital top/bottom, small (x-height) top/bottom, ascender, descender, each with flat and round (overshoot) heights.
- **x-height scale fitting**: the *whole vertical scale* is adjusted so the scaled x-height lands on a pixel, rounding up from a fraction well under one half — not just snapping edges. Native's `TopZoneRoundUp` (`FontHintMetrics.cs:38`) snaps zones similarly but leaves everything else unscaled and caps moves at 1 px (`GridFitter.cs:52`).
- **Overshoot suppression** under half a pixel; zones round independently.

Per glyph, per dimension: **segments** (runs along an axis) → **edges** (segments at one position) → **stems** (paired opposite edges) and **serifs**; edges snap to blue zones, then the anchor stem rounds, other stems get **width rules** (snap to the standard width when close, minimum 1 px, extra rules below ~1.5 px), then serifs, then **strong points** follow their edges and **weak points** interpolate (IUP-like).

- **Light**: vertical dimension only; advance = linear advance rounded. This is what Native Phase 174 approximates.
- **Normal**: adds the horizontal dimension (vertical stems snapped, "stem adjust" width rules), but not for fonts with the italic style flag; after hinting the left/right side bearings are rebuilt around the first/last hinted edge with a "prefer more space at tiny sizes" bias, so the advance can grow by a pixel; `lsb_delta`/`rsb_delta` report the change.
- **Mono**: normal plus strict integer snapping of stem widths in both axes, then mono rendering with dropout control.
- **LCD**: horizontal hinting off (light-like).

### TrueType bytecode

An interpreter for the font's `fpgm`/`prep`/glyph programs: ~200 opcodes, graphics state (vectors, rounding incl. `SROUND`/`S45ROUND`, loop, minimum distance, control-value cut-in), CVT (with `cvar` at variation instances), storage, function and instruction definitions, twilight zone, phantom points, composite instructions, `IUP`, `DELTAP`/`DELTAC`, `GETINFO`/`INSTCTRL`/`SCANCTRL`, plus execution limits for hostile fonts. FreeType's grayscale default (v40) runs it with horizontal moves ignored and post-`IUP[y]` hacks suppressed; mono runs everything. Honest size: **4-6k lines of C# plus a comparable test volume and fuzzing, 4-8 weeks of focused work**, written from the Apple/Microsoft specs (Native's code-originality decision forbids porting FreeType's `ttinterp.c`). It is the only route to byte parity on Arial/Georgia/NotoSans and other bytecode fonts (many Windows fonts; many WOFF2s from CSS CDNs — step 0 records how many Google/Bunny WOFF2s carry `fpgm`/`prep`).

### CFF hinter

Y-only fitting driven by the font's own hints: Private DICT BlueValues/OtherBlues/BlueScale/BlueShift/BlueFuzz/StdHW, charstring `hstem`/`hstemhm` and `hintmask` replacement. Native already parses charstrings (Phase 166) but discards hints; CFF2 would need the blended Private DICT values (currently left at defaults). Size: **~1.5-2.5k lines, 1-2 weeks**.

### GDI and DirectWrite (for context only)

GDI runs the font's bytecode in full (x and y; integer advances from `hdmx`/`gmCellIncX`) and has no auto-hinter. DirectWrite's natural/symmetric modes are y-only with design advances; `GDI_CLASSIC` runs bytecode like GDI. Neither is a parity target.

## Design: hint levels

New public enum `HintLevel` in the root namespace, applied to `Grayscale`, `None` (mono) and, later, `Lcd`; distance fields are never hinted.

| Level | What Native does | FreeType mapping | Default output change |
|---|---|---|---|
| `Default` | `Normal` with `EnableHinting` on, `None` with it off, in every anti-alias mode (the size taper leaves text above 40 ppem exactly unhinted) | today's load flags | small text is hinted; above 40 ppem none |
| `None` | unhinted (today's `Grayscale`/`None`) | `FT_LOAD_NO_HINTING` | — |
| `Light` | the Phase 174 y-only fitter, unchanged | `FT_LOAD_TARGET_LIGHT` | — |
| `Normal` | **auto-hinter, both axes**, the font's advances with the ink placed by side, touching pairs opened in the kerning; stroke renderer when `AntiAlias = None` | `FT_LOAD_FORCE_AUTOHINT` + normal/mono target | — |
| `Full` | **matches FreeType default output**: the font's own hinting where it has it (TrueType bytecode v40, mono v35-style; CFF hinter) and the `Normal` auto-hinter otherwise; integer ppem for hinted TrueType with `head.flags` bit 3 | `FT_LOAD_DEFAULT` (today's hinted output) | — |

Per level:

- **`Light`** — kept as is (Phase 174); not polished further. Applies to Grayscale and, newly, mono (the same fit, then the mono threshold). Risk: none beyond today's.
- **`Normal`** — the parity engine for unhinted TrueType, and the fallback for every font `Full` cannot yet serve.
  - Features: both-axis segments/edges/stems/serifs; standard widths in x and y; whole-axis x-height scale fit; width rules per target (gray vs mono strict); strong/weak point alignment; side-bearing and advance rebuild; italic-flagged fonts get no x hinting; mono target strict snapping. The Phase 174 one-pixel move cap does not apply here (FreeType does not have it; the whole-axis scale fit alone moves tall glyphs more than a pixel at 24px).
  - Gain: the evidence above — crisp vertical stems and FreeType's spacing on Roboto-class fonts (most of the user's complaint).
  - Risks: **metrics change** (advances grow, boxes move), so atlas packing, `QueryAtlasSize` and `.fnt` advances change (the metrics shortcut must be bypassed, as for `Light` today). **Kerning** stays the unhinted GPOS value scaled — the same as on the FreeType backend, so parity holds. FreeType's docs warn that strong auto-hinting without applying `lsb_delta`/`rsb_delta` can space text "far too large" at small sizes; the FreeType backend ignores them, so the parity target carries that looseness. Folding the deltas into kerning pairs is out of scope. **Synthetic bold/italic**: the FreeType backend hints first and emboldens/obliques after (`FreeTypeRasterizer.cs:204-213`); Native transforms before hinting (Phase 160 D14), so `Normal`/`Full` with synthetic styles hint the untransformed outline and then transform, matching FreeType's order. **Variable fonts**: the per-instance metrics cache already keys on the instance. **CFF**: the auto-hinter is outline-only and works on CFF (fallback until the CFF hinter lands). **Super sampling**: hinting runs at the supersampled ppem and is then box-filtered, which defeats it (true on FreeType too); documented, not changed. **Distortion**: an auto-hinter can collapse thin features; the parity bar catches divergence from FreeType, and FreeType's own output is the accepted look.
- **`Full`** — dispatch exactly as FreeType chooses (same table test), so a font goes to the same engine on both backends. Until the CFF hinter (step 5) and the interpreter (step 6) land, CFF and bytecode fonts fall back to `Normal` and the parity report labels them "fallback". Integer ppem for hinted TrueType (bit 3) lands with `Full` in step 3, so `prep`-only fonts reach parity without an interpreter.
- **`Lcd`** — later and optional: `Normal`/`Full` apply their y-only part (FreeType disables x hinting for LCD). Not in the parity bar: the FreeType backend's LCD buffer is the raw 3× layout.

## Public API

- `FontGeneratorOptions.HintLevel` and `RasterOptions.HintLevel` (`HintLevel.Default`), copied by `RasterOptions.FromGeneratorOptions`, `BmFontBuilder.FromConfig` (it drops unknown fields today — add it explicitly) and a new `BmFontBuilder.WithHintLevel`.
- **Resolution**: an explicit level wins; `Default` keeps today's per-backend mapping from `EnableHinting` + `AntiAlias`, so every existing caller and config renders byte-identically. `EnableHinting = false` with an explicit level other than `None` is an `ArgumentException` (CLI: a usage error), so contradictory requests fail loudly. With an explicit level, `AntiAlias = Light` renders as grayscale coverage (the level owns hinting).
- **Capability**: `IRasterizerCapabilities.SupportedHintLevels`, default interface member `[]` (meaning "only `Default`"). FreeType: `None, Light, Normal, Full`. DirectWrite: `None` (`NATURAL`), `Light` (`NATURAL_SYMMETRIC`), `Full` (`GDI_CLASSIC`). GDI: `Full`. StbTrueType: `None`. Native grows as steps land. Core negotiates like `AntiAliasNegotiation`: an unadvertised level steps down (`Full` → `Normal` → `Light` → `None`) to the first one the backend supports, else `Default`; silent, per the existing degrade-silently decision.
- **BMFC**: `hintLevel=default|none|light|normal|full` in the KernSmith extension block; `useHinting` keeps its meaning. **CLI**: `--hint-level <level>`; `--hinting`/`--no-hinting` unchanged. **UI**: a Hint level combo next to Anti-alias, disabled per capability like the other Native-only settings.
- **Default output**: this phase changes no default output. Recommendation for Jeremy (Q1): once `Full` meets the parity bar on auto-hint and CFF fonts, make Native's `Default` with `EnableHinting = true` resolve to `Full` for `Grayscale`/`None`. It is what `EnableHinting = true` promises and what the default backend (FreeType) does, and Native is unpublished (`IsPackable=false`), so no NuGet consumer's output moves. The cost is regenerating Native goldens, characterization hashes and Native's harness baselines in that one commit.

## Parity measure

A test-project tool plus a gate, both driving the **public** generation path (`BmFont.Generate`, cell-height sizing, AA negotiation), so the comparison is what users get:

- `tests/hint-parity/HintParity` (console, references core + FreeType + Native; Windows adds GDI/DirectWrite for context columns). For each font × size × AA × Native level it generates with FreeType (`Full`, i.e. today's default) and Native, crops each glyph by its `.fnt` rect, places both on a common canvas by `xoffset`/`yoffset` (`ImageSimilarity.Place`), and reports per cell and per glyph:
  - `lineHeight`/`base` equal; **advance mismatches** (count, list, signed sum); box mismatches (`xoffset`, `yoffset`, `width`, `height`, each and within ±1);
  - grayscale: pixel-identical glyphs, mean and max absolute difference, mean and worst SSIM (`ImageSimilarity.Ssim`); mono: bit-identical glyphs and XOR pixel count;
  - the engine FreeType used (auto-hint / bytecode / CFF / `prep`-only) and whether Native ran the same engine or a fallback.
  - Output: `parity-report.md` + `parity.csv` + side-by-side x4 sheets of the worst 20 glyphs, into a gitignored `output/`.
- Matrix: Roboto, NotoSans-Latin, SourceSerif4 (CFF), DancingScript, RobotoFlex (fixtures) and Arial, Georgia (system, Windows only) × 8/10/12/14/16/24 px × `Grayscale` and `None` × printable ASCII + Latin-1.
- **Gate**: `NativeFreeTypeParityTests` in `KernSmith.Rasterizers.Native.Tests` (it already references FreeType) runs the fixture-only matrix and asserts the bar per engine that Native claims to implement; the Phase 179 similarity matrix keeps its role for unhinted output.
- **Parity bar** (proposal, Q2), per (font, size, AA) cell whose engine Native implements:
  - advances: **100% identical**; line metrics identical;
  - boxes: ≥ 98% of glyphs identical, all within ±1 px;
  - grayscale: mean SSIM ≥ 0.98, worst glyph ≥ 0.90, ≥ 90% of glyphs with max pixel difference ≤ 16;
  - mono: ≥ 95% of glyphs bit-identical, every glyph within 3 differing pixels.
  Byte identity is not the bar: Native's float pipeline will not reproduce FreeType's 26.6 fixed-point rounding exactly, and the code-originality decision rules out porting it.
- Step 0 records today's numbers (Native `None` and `Light` vs FreeType) as the starting gap.

## Performance plan

Existing coverage: `NativeFeatureRasterizeBenchmarks`/`NativeFeatureMetricsBenchmarks` "Light hinted" (Roboto ASCII 32px; `NativeFeatureBenchmarks.cs:93-94`), `RasterizationBenchmarks` "32px Light AA" (`:81-86`), and `NativeComparison*` (Grayscale/None at 12/32/72, FreeType hinted alongside). Phase 177 reference numbers: Native Grayscale 32px 1.69 µs/glyph, Light hinted 4.24 µs (metrics 2.66 µs); `Generate` ASCII 32px Native 0.37 ms vs FreeType (hinted) 0.74 ms.

- **New `NativeHintingBenchmarks`** (step 0, written against today's API so it runs on `main`): params level × size {8, 12, 16, 32} × charset {ASCII, ASCII + Latin-1} × font {Roboto (auto-hint), NotoSans-Latin (bytecode), SourceSerif4 (CFF)} × AA {Grayscale, None}; per-glyph `RasterizeAll` and `GetGlyphMetrics` with `OperationsPerInvoke`; FreeType `Full` and `None` rows as the reference; a cold row (new rasterizer + first glyph) for per-face/per-size analysis; and `Generate` ASCII 12px and 32px per level. Levels not yet implemented are filtered out by capability, as the feature cases are.
- **Baseline before any change**: in a separate worktree on `main` (`git worktree add <dir> main`, never stash), on a quiet machine (no parallel agents, no builds), run `--filter *NativeHinting* *NativeFeature* *NativeComparison*` with the default job (not `short`, whose ~15% noise would hide the budgets); record in this doc.
- **Budget**, Release, per glyph unless noted:
  - `Default`/`None`/`Light`: within noise of the baseline (and byte-identical output).
  - `Normal`: rasterize ≤ 1.5× today's `Light` (≈ 6.4 µs at 32px); metrics ≤ 2× `Light` metrics; per-face analysis ≤ 1 ms once; `Generate` ASCII 32px no slower than FreeType hinted.
  - `Full` CFF hinter: ≤ `Normal`.
  - `Full` bytecode: ≤ 1.5× FreeType's own hinted/unhinted time ratio, measured on the same machine; per-face `fpgm`/`prep` ≤ 2 ms once per size.
  - Allocation: `NativeAllocationTests` extended per level; ≤ +2 KB per hinted glyph over `Light`.
  A step that misses its budget profiles (stage timing, `dotnet-trace`) before it lands, as Phase 177 did.

## Verification

- **TDD per step**: each component starts with a failing unit test on synthetic outlines (x edges/stems found; stem-width rules per target; x-height scale fit; side-bearing/advance rebuild; mono strict snapping; italic-flag x bypass; integer-ppem scale; CFF blue zones from Private DICT; one interpreter opcode group per test). The parity gate itself is the red test for `Normal`/`Full`: it is committed first, failing with today's gap, and goes green as the bar is met per engine.
- **Stability**: `Default`, `None` and `Light` stay byte-identical — goldens, `NativeOutputCharacterizationTests` and the `hint-*`/`nohint-*` harness configs unchanged — asserted before and after each step.
- **Hostile input**: `NativeFuzzTests` and `NativeHostileInputTests` run every level; the interpreter gets execution budgets and its own fuzz corpus.
- **Regression harness** (force-added `.bmfc`, fonts available everywhere): `hintlevel-normal-12.bmfc` and `hintlevel-full-12.bmfc` (Roboto fixture, `fontFile=../../KernSmith.Tests/Fixtures/Roboto-Regular.ttf`), `hintlevel-full-mono-12.bmfc` (`aa` none), `hintlevel-full-otf-12.bmfc` (SourceSerif4), `hintlevel-full-arial-12.bmfc` (bytecode), `hintlevel-none-12.bmfc`; plus `supersample-advance.bmfc` (`aa=4`) for the truncation fix. Each config's own test asserts Native produced output (the harness passes `NEW` configs unseen). `GenerateAll` reads `hintLevel` through `FromConfig`.
- **Visual check**: the parity tool's x4 sheets for each step, and the scratchpad-style side-by-side (FreeType vs Native per level) attached to the PR.

## Implementation order

Straight to parity on the user's case first; intermediate tiers only as far as parity needs them.

0. **Measure** (S): parity tool + gate (red), `NativeHintingBenchmarks`, baseline on `main`, record FreeType's library version (`FT_Library_Version`), the starting gap, and whether the auto-hinter uses integer ppem; check BMFont64 `aa=2/4` advances, then fix the supersample advance truncation if it is not BMFont parity (TDD, all backends; harness).
1. **API** (S): `HintLevel`, options, `SupportedHintLevels`, negotiation, BMFC/CLI/builder/`FromConfig`; FreeType maps all four levels (and, per Q3, `AntiAlias = Light` loads `FT_LOAD_TARGET_LIGHT`). No Native output change.
2. **Native `Normal`, grayscale + mono** (L, ~2-3k lines): the both-axis auto-hinter and advance rebuild; Native advertises `Normal`. Exit: parity bar on Roboto at every size, both AA modes.
3. **Native `Full` dispatch + integer ppem** (S): FreeType's engine-selection test; bit-3 integer ppem for hinted TrueType; fallback to `Normal` for CFF/bytecode. Exit: bar on Roboto, RobotoFlex, DancingScript; Native advertises `Full`. Integer ppem alone should also give grayscale *advance* parity on Arial/Georgia (shown above) before any interpreter exists; their pixels stay on the fallback.
4. **Default decision** (S, if Q1 = yes): Native `Default` → `Full` for Grayscale/None; regenerate goldens/characterization; CHANGELOG.
5. **CFF hinter** (M, ~1.5-2.5k lines): Private DICT zones + stem hints + `hintmask`; CFF2 blended Private values. Exit: bar on SourceSerif4 and `NotoSans-Regular-Latin-CFF`.
6. **TrueType interpreter** (XL, 4-8 weeks; gated on Q4): v40 grayscale, full mono, `cvar`, limits. Exit: bar on NotoSans-Latin, Arial, Georgia.
7. **Lcd** (S, optional): y-only part of `Normal`/`Full` for `Lcd`.
8. **Surfaces** (S): Blazor sample (`samples/KernSmith.Samples.BlazorWasm/Pages/Index.razor`) gains Anti-alias (`Grayscale`/`None`/`Light`/`Lcd`) and Hint level selects, plus `?aa=`/`?hint=` query parameters for the headless smoke run; desktop UI combo; Native README "Hinting" section, CLI help (`GenerateCommand.cs:1083` text), docs, CHANGELOG, `project_facts.md` entries (Native anti-aliasing, pipeline).

Each step: full `dotnet test`, `regression_check.py` with the configs above, benchmarks against the budget, phase table in `master-plan.md`.

## Decision changes

Rewritten in place in `project_decisions.md` when the step that changes them lands:

- **"Native auto-hints only `Light` requests…"** (Native Rasterizer section) → Native hints per `HintLevel`; `Default` resolves to `Normal` with hinting on (Jeremy's answer to step 4: size-scaled hinting unless told otherwise); `Light` stays y-only with the one-pixel cap; `Normal`/`Full` fit both axes within the font's advances (the FreeType-style advance rebuild was measured and rejected for drifting line length). Rejected alternatives recorded: widening `EnableHinting` alone (no way to pick a level), copying FreeType code (originality decision).
- **"No x fitting"** (same entry, and Phase 174's decision list) → x fitting exists at `Normal`/`Full`; `Light` remains y-only.
- **"Native rounds the advance once…"** → still true for `None`/`Light`; hinted levels take the auto-hinter/interpreter advance.
- **"Unadvertised anti-alias modes degrade silently"** → extended to hint levels (same reasoning).
- **Phase 160 Q2/D5 "no TrueType bytecode"** → superseded only if Q4 = yes; then a pure managed interpreter written from the specs, with execution limits.
- **New**: FreeType backend `Light` loads `FT_LOAD_TARGET_LIGHT` (if Q3 = yes); supersample advance rounding (step 0 outcome).
- `project_facts.md` "Native anti-aliasing" and "Only `Light` is ever hinted" lines are rewritten at the same time.

## Open questions (Jeremy)

1. **Default output**: once parity is met, should Native's `Default` (hinting on) become `Full` for Grayscale/None? Recommended yes — Native is unpublished, and it is what `EnableHinting = true` means everywhere else.
2. **Parity bar**: accept the bar above (identical advances; SSIM ≥ 0.98 gray; ≥ 95% bit-identical mono), or demand byte identity (which would require porting FreeType's fixed-point code — question 5)?
3. **FreeType `Light` bug**: fix it (`FT_LOAD_TARGET_LIGHT`), changing FreeType's `antiAlias=light` output, or leave `Light` = normal on FreeType?
4. **Bytecode interpreter**: commit to step 6 (4-8 weeks, needed for parity on Arial/Georgia/hinted web fonts), or stop at auto-hinter + CFF with bytecode fonts on the `Normal` fallback?
5. **Originality rule**: may contributors *read* FreeType's auto-hinter source to learn its constants (matching output, writing our own code), or strictly docs/papers + black-box tuning against the FreeType oracle? The latter is slower to reach the bar.

## Success Criteria

- [ ] Parity tool, gate and `NativeHintingBenchmarks` in place; baseline and starting gap recorded
- [ ] Supersample advance truncation resolved (fixed, or recorded as BMFont parity)
- [ ] `HintLevel` public API, capability, negotiation, BMFC/CLI/builder; default output unchanged
- [ ] `Normal` meets the bar on Roboto, grayscale and mono, within budget
- [ ] `Full` dispatch and integer ppem meet the bar on auto-hint and `prep`-only fonts
- [ ] CFF hinter meets the bar on the CFF fixtures
- [ ] Interpreter meets the bar on bytecode fonts (if Q4 = yes)
- [ ] Blazor sample and UI expose anti-alias and hint level; docs, CHANGELOG, decisions and facts updated
- [ ] All tests pass; `regression_check.py` run with the hint-level configs
