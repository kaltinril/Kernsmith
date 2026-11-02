# Small-size glyph quality for KernSmith Native: survey of open-source approaches

*Research date: 2026-11-01. Sources were read from upstream repositories (shallow clones of FreeType master 2.15.0-dev, googlefonts/fontations at HEAD of 2026-11-01, SixLabors/Fonts, LayoutFarm/Typography, MikePopoloski/SharpFont, dfrg/swash), plus project documentation and mailing-list posts. Line counts are `wc -l` on those clones. Anything I could not confirm is marked **[unverified]**.*

---

## 1. Executive summary

**The main finding.** For the fonts KernSmith actually tests (Arial and Georgia in `tests/bmfont-compare/gum-bmfont/`, plus most shipped Windows/Google TTFs), FreeType's crispness at 8–16 px **does not come from its auto-hinter or its rasterizer**. It comes from **executing the font's own TrueType bytecode**.

- KernSmith's FreeType backend (`src/KernSmith.Rasterizers.FreeType/FreeTypeRasterizer.cs:182-194`) loads with `FT_LOAD_DEFAULT`, adding `FT_LOAD_TARGET_MONO` for `AntiAlias = None`.
- In `FT_Load_Glyph` (`src/base/ftobjs.c`, around lines 960–1020), FreeType auto-hints only in these cases:
  - `FT_LOAD_FORCE_AUTOHINT` is set;
  - the driver has no hinter;
  - the target is LIGHT and the driver can't hint lightly;
  - the font has no `fpgm` and a `prep` of 7 bytes or less.
- Otherwise a hinted TTF goes through **`ttinterp.c`**. Its rasterizer, `ftgrays`, computes exact-area coverage, which Native already does correctly.
- **Grayscale (target NORMAL):** the v40 interpreter runs every **y** instruction and ignores **x** moves ("backward compatibility"), unless the font's `prep` sets INSTCTRL selector 3.
- **Mono:** `tt_loader_init` (`ttgload.c` ~2300) **turns backward compatibility off**, so the font's full x+y bi-level hinting runs. That is the Arial/Georgia "pixel-perfect" mono look.

Native today has no interpreter, a y-only heuristic fitter, and hinting only in `Light` mode. That is three of the gaps. Recorded decisions block two of them (no bytecode, no x fitting: `project_decisions.md` line 47 and `project_facts.md` line 54), and those decisions would need to be revisited.

There is a second structural point. BMFont atlases hold **one bitmap per glyph at an integer origin**. So the "y-only hinting + runtime subpixel positioning" approach used by Chrome, macOS and DirectWrite-natural only half-applies here. KernSmith gets no runtime subpixel placement to hide x blur, which is why x fitting (NORMAL/MONO targets) matters more for KernSmith than for a browser.

### Ranked recommendation

1. **Port a TrueType bytecode interpreter with FreeType v40 semantics (grayscale) and v35-style full semantics (mono). Port it from Google's skrifa (`skrifa/src/outline/glyf/hint`, Rust, MIT OR Apache-2.0).**
   - This is the single biggest win, and it is the only route to *matching* FreeType on hinted TTFs. It is literally FreeType's algorithm.
   - skrifa's interpreter is a close, modern port of `ttinterp.c`: 152 source comments link to specific FreeType lines. It is validated against FreeType by Google's `fauntlet` tool and ships in Chrome.
   - Size: about 6.4k lines of production Rust (9,963 with tests), probably 6–8k lines of C#.
   - It is pure integer 26.6/16.16 math, so it suits WASM well.
   - Fallback skeleton: SharpFont's MIT C# `Interpreter.cs` (1,614 lines, 2015, v35-era, incomplete).
2. **Replace Native's `GridFitter` with a port of the FreeType auto-hinter, Latin script first, ported from skrifa's `autohint` (MIT OR Apache-2.0).**
   - This covers unhinted TTFs and the `Light` target. It is also what FreeType does for unhinted fonts, so it is needed for parity.
   - The pieces that matter:
     - x-height **scale** adjustment, with a round-up preference;
     - blue zones with overshoot suppression;
     - segment → edge → stem/serif linking in **both** axes;
     - `af_latin_compute_stem_width` quantisation;
     - strong/weak point interpolation;
     - `lsb_delta`/`rsb_delta`.
   - Size: about 7.7k lines of production Rust (10,803 with tests), and less for Latin only.
3. **Port the CFF/Type 2 hint-map hinter (the Adobe engine as ported in skrifa `cff/hint.rs`, 1,505 lines). Then add optional perceptual post-processing as a separate feature: gamma/contrast remapping of coverage and stem darkening.**
   - The CFF hinter is what FreeType uses for `.otf`/CFF by default. It is y-only and cheap.
   - The perceptual tricks **cannot** close the FreeType gap, because FreeType's default output has no gamma and no darkening. They are worth offering only as opt-in "atlas looks better in a non-linear blender" options. They must stay off by default, or the FreeType parity target moves.

**Licence bottom line.**
- FreeType is dual **FTL** or GPLv2. **Choose FTL.** FTL is BSD-style and MIT-compatible; it asks for a credit line in documentation and a "changes indicated" note for modified sources.
  - That means even a direct C-to-C# port of FreeType *code* is legally possible as an FTL-licensed component inside MIT KernSmith. It is not "learn only".
  - The GPLv2 option must not be chosen.
- skrifa is **MIT OR Apache-2.0**, but it is a line-by-line derivative of FreeType. Its repository ships no FTL notice, and I could not find the legal basis for the relicensing **[unverified]**.
  - The cheap, belt-and-braces path is to port from skrifa under MIT, *and* add the FTL credit line ("Portions of this software are copyright © 2026 The FreeType Project (https://freetype.org). All rights reserved.").
- **SixLabors.Fonts** (Six Labors Split License: Apache-2.0 only for some consumers, otherwise commercial) is **learn-only**. The same applies to anything GPL.

---

## 2. Comparison table

| Approach / project | What it does for small sizes | Quality at 8–16 px (vs FreeType default) | Licence | Port into MIT KernSmith? | Size | Pure-C# / WASM portability |
|---|---|---|---|---|---|---|
| **FreeType TrueType interpreter v40** (`src/truetype/ttinterp.c`) | Runs the font's own hints. Grayscale: y only, x moves ignored. Mono: full x+y | *Is* the reference for hinted TTFs | FTL / GPLv2 | Yes, under FTL (credit line + mark changes) | 7,631 (interp) + 2,751 (gload) C | Excellent: integer math, no platform calls |
| **skrifa glyf/hint** (fontations, Rust) | Port of the v40 interpreter, with `Target::Mono`, `Smooth{Normal,Light,Lcd,VerticalLcd}`, a `gasp`-driven ClearType policy, and `preserve_linear_metrics` | ≈ FreeType (validated with `fauntlet`; ships in Chrome) | MIT OR Apache-2.0 (FreeType-derived) | Yes. Best source | ~6.4k prod + 3.6k tests | Excellent |
| **FreeType auto-hinter** (`src/autofit/`, mainly `aflatin.c`) | Blue zones, x-height scale snap, stems in both axes (NORMAL/MONO), y-only (LIGHT), deltas | Very crisp for unhinted fonts; what FreeType itself uses for them | FTL / GPLv2 | Yes, under FTL | 16.7k C total; aflatin 5.1k + afhints 1.8k | Excellent (HarfBuzz is used only for script coverage; optional) |
| **skrifa autohint** | Port of aflatin, afcjk and afindic style handling | ≈ FreeType autohinter | MIT OR Apache-2.0 | Yes. Best source | ~7.7k prod + 3.1k tests | Excellent |
| **FreeType CFF hinter** (Adobe "cf2", `src/psaux/ps*.c`) | Hint map from charstring stems + blue zones, y only; darkening hook | Reference for CFF OTFs | FTL (Adobe code under FTL + patent grant) | Yes, under FTL | psaux+pshinter 15.3k (includes Type 1 parsing) | Excellent |
| **skrifa cff/hint.rs** | Port of the Adobe hint map | ≈ FreeType CFF | MIT OR Apache-2.0 | Yes | 1,505 | Excellent |
| **ttfautohint** | Compiles the FreeType auto-hinter's analysis into TrueType bytecode, offline | Matches FT light autohint on any TT renderer | FTL / GPLv2 | Algorithms only (it is a font tool, not a renderer) | Large C + HarfBuzz | N/A at runtime; ideas only |
| **Native today** (`Internal/Hinting/GridFitter.cs`) | y-only heuristic: zones, stroke rounding, monotone y map, ≤1 px moves, `Light` only | Softer than FT; no x; no bytecode | MIT | N/A | ~1.06k C# | — |
| **SharpFont** (Popoloski, C#) | Pure-C# TT interpreter (v35), parser, rasterizer | v35-era; partial | MIT | Yes (but old/incomplete) | Interpreter 1,614 lines | Already C# |
| **Typography / LayoutFarm** (C#) | Ships SharpFont's interpreter (`//MIT, 2015, Michael Popoloski's SharpFont`); experimental "CustomAutoFit" (PixelFarm `GlyphOutlineAnalyzer`, via triangulation) | Interpreter as SharpFont; auto-fit experimental | MIT (files may carry their own headers) | Yes; low value over SharpFont | Interpreter 2,148; contours 990 | Already C# |
| **SixLabors.Fonts** (C#) | TT interpreter adapted from SharpFont and upgraded to v40 semantics (`HintingMode.Standard`) and full mode (`HintingMode.Full`); CFF `HintMap`; compatibility lists | Probably close to FT v40 **[unverified visually]** | Six Labors Split License | **Learn only** | Interpreter 5.0k; CFF hint 3.0k | Already C# |
| **swash** (Rust) | "Asymmetric vertical hinting (TrueType and Postscript)", now delegating to skrifa (`skrifa >=0.31.1,<=0.44` dependency) | = skrifa light | MIT OR Apache-2.0 | Use skrifa directly | — | — |
| **stb_truetype** | No hinting; oversampling + box prefilter for subpixel positioning | Blurry at 8–12 px | Public domain / MIT | Yes | single header | Trivial |
| **font-rs / fontdue / ab_glyph** (Rust) | Fast exact-area accumulation rasterizers, no hinting | Same as Native unhinted | font-rs Apache-2.0; fontdue MIT/Apache/Zlib; ab_glyph Apache-2.0 | Yes, but nothing to gain | small | Trivial |
| **opentype.js `hintingtt.js`** | TT interpreter in float math; some instructions missing; off by default | Partial | MIT | Yes, but weaker than skrifa | ~3k JS **[size unverified]** | Fine |
| **Typr.js** | Parser/renderer; I found no hinting | Unhinted | MIT | — | — | — |
| **Skia / Chrome** | FreeType or Fontations with slight hinting, subpixel positioning, `SkMaskGamma` luminance-dependent gamma + contrast LUTs | Smooth; relies on runtime subpixel placement | BSD-3 (Skia) | Ideas + LUT approach | — | LUT is trivial |
| **DirectWrite** | NATURAL (x-AA only, y grid-fit) vs NATURAL_SYMMETRIC (AA both axes, larger sizes); GDI_CLASSIC; `gasp`-driven | Crisp horizontals, natural x | Proprietary | Learn only | — | — |
| **GDI** | Full TrueType hinting (v35-like, x+y), ClearType compatibility "dirty" mode, integer advances | Crispest/most distorted; the font's design target | Proprietary | Learn only | — | — |
| **Apple Core Text** | No hinting; fractional advances; "dilation" (stem darkening) on low-DPI | Soft at 8–12 px on 1× screens | Proprietary | Learn only | — | — |
| **Pathfinder** (Rust GPU) | Exact coverage on GPU; macOS-style stem darkening/dilation | Unhinted | MIT/Apache | Ideas only | — | GPU-centric |
| **Slug** (Lengyel) | GPU direct curve rendering; no hinting | Unhinted, resolution-independent | Patent dedicated to public domain 2026-03-17; reference shaders MIT **[unverified]**; library commercial | Ideas only | — | GPU |
| **msdfgen** | Distance fields; no hinting | Poor below ~16 px without hinting | MIT | Already have MSDF | — | — |

---

## 3. Per-approach details

### 3.1 FreeType TrueType bytecode interpreter: v35, v38, v40

**Source:** `src/truetype/ttinterp.c`, `ttgload.c`, `ttobjs.c`; docs <https://freetype.org/freetype2/docs/hinting/subpixel-hinting.html>.

- **v35** follows the 1990s TrueType spec literally: every instruction moves points in x and y, the `hdmx` advances are honoured, and integer advances result. Fonts like Arial, Times and Georgia carry heavy x+y hinting aimed at GDI bi-level and early-ClearType output.
- **v38 ("Infinality")** layered ClearType-compatibility hacks plus per-font/per-glyph configuration on top of v35. It was "painfully slow" and is not compiled by default.
- **v40 ("minimal subpixel", default since FreeType 2.7)** is v38 stripped down. The docs say: "The code simply ignores *all* horizontal hinting instructions."
  - Why: snapping on one axis instead of two "greatly reduces the complexity and cost of hinting". Advances are no longer changed by hints. Stem darkening (which fights x hinting) becomes possible. The output resembles DirectWrite/ClearType.
  - Modern ClearType-era fonts (Calibri, Cambria, Consolas) render well. Arial/Times/Georgia/Verdana render "mostly fine with smaller details off".
- **Concrete v40 behaviour**, verified in FreeType master and corroborated by SixLabors' implementation notes:
  - Backward-compatibility mode is on unless the `prep` executes INSTCTRL selector 3 (the "native ClearType" waiver): `exec->backward_compatibility = (GS.instruct_control & 4) ^ 4`.
  - In backward-compatibility mode, x moves are dropped. No points move after both IUP[x] and IUP[y] have run ("post-IUP"). SHPIX and DELTAP run only in their gated forms.
  - **Mono target:** backward compatibility is forced **off**, so the font's full programming runs (v35-equivalent output). This is what KernSmith's FreeType backend uses for `AntiAlias = None`.
  - Component x offsets are not rounded in v40 non-mono mode (`ttgload.c` ~1171). `pp3`/`pp4` are set to advance/2 for non-mono, non-LCD targets (~1377).
  - `GETINFO` reports the ClearType/subpixel capability bits, so fonts take their ClearType code paths.
- **What produces crispness:** the font's own CVT-driven stem widths (`MIRP` with minimum distance and the CVT cut-in) make every vertical stem in `l`, `i`, `t` the same whole-pixel count. `MDAP`/`MIAP` round heights to x-height and cap-height CVT entries. Deltas fix individual ppems. Those deltas are hand-tuned for exactly 8–16 px, which no heuristic reproduces.
- **Also required for parity:**
  - the `prep` program re-runs per size and render mode;
  - `cvar` deltas for variable fonts;
  - the twilight zone;
  - composite `ROUND_XY_TO_GRID`;
  - phantom points (Native already uses pp1 for placement);
  - the instruction-control rules (INSTCTRL bit 1 disables hinting);
  - FreeType's "tricky font" list, which matters only for CJK fonts like DFKai-SB.
- **Licence:** FTL or GPLv2. FTL (`docs/FTL.TXT`) "specifically permit[s] and encourage[s] the inclusion of this software, with or without modifications, in commercial products". It requires a documentation credit ("Portions of this software are copyright © <year> The FreeType Project…"), and modified sources must indicate their changes. An FTL-licensed C# port inside an MIT package is fine; many MIT projects ship FTL components. The relevant TrueType hinting patents (Apple) expired around 2010 **[from memory; unverified here]**.
- **Size:** `ttinterp.c` 7,631 lines, `ttgload.c` 2,751, `ttobjs.c` 1,442 (C).

### 3.2 skrifa / fontations (Google, Rust): the recommended port source

**Repository:** <https://github.com/googlefonts/fontations> (workspace `license = "MIT OR Apache-2.0"`, skrifa 0.49.0 on 2026-11-01). Chrome blog post: <https://developer.chrome.com/blog/memory-safety-fonts> (2025-03-19).

- **Purpose** (from the README): "replace FreeType in Google applications". Chrome enabled Fontations in Chrome 128 for some formats. In Chrome 133 it covers all web fonts on Linux, Android and ChromeOS, and is the fallback on Windows and Mac. Pixel comparisons against FreeType covered "different antialiasing and hinting modes".
- **`outline/glyf/hint/`: TrueType interpreter.**
  - Size: about 6.4k production lines, 9,963 including tests. 152 comments link to `ttinterp.c` lines, and `cleartype.rs` implements the backward-compatibility rules.
  - `hint.rs` API: `Engine::{Interpreter, Auto(styles), AutoFallback}` ("Selects the engine based on the same rules that FreeType uses…"), `Target::Mono` ("Corresponds to `FT_LOAD_TARGET_MONO`"), and `Target::Smooth{ mode: Normal|Light|Lcd|VerticalLcd, symmetric_rendering, preserve_linear_metrics }`.
  - Its docs say `FT_LOAD_TARGET_LIGHT` "is equivalent to `FT_LOAD_TARGET_NORMAL` since FreeType 2.7" for the TrueType interpreter.
  - `cleartype.rs` also implements a **`gasp`-driven policy** ("Windows reads the same two properties from the same source, so honoring them makes text match Windows"). That is a useful extra for a "match DirectWrite" mode.
- **`outline/autohint/`: auto-hinter port.**
  - Size: about 7.7k production lines, 10,803 including tests. Constants and functions cite `aflatin.c` at FreeType commit `57617782…`.
  - Modules: `topo/segments.rs` (1,102), `topo/edges.rs` (875), `metrics/blues.rs` (1,013), `metrics/widths.rs`, `metrics/scale.rs`, `hint/edges.rs` (1,242), `hint/outline.rs` (735), `shape.rs` (1,351: HarfBuzz-free script/style coverage), and `style.rs` (Latin, CJK and Indic groups).
- **`outline/cff/hint.rs`: Adobe hint-map port**, 1,505 lines, citing `psaux/psblues.h` and `pshints.h`.
- **Provenance caveat.** These files are clearly derived from FreeType's FTL code, yet the repository has only `LICENSE-MIT` and `LICENSE-APACHE`, and no file I grepped carries FTL text. Google's arrangement with the FreeType copyright holders is not documented in what I could see **[unverified]**. Google employs or funds FreeType contributors, and the risk is low in practice. Even so, add the FTL credit line when porting.
- **Why it is the best source:**
  - clean, modern and tested (`fauntlet` compares it with FreeType glyph by glyph);
  - in production at Chrome scale;
  - written as plain integer code in a structured style that maps naturally to C# (`ref struct`/`Span<T>`, no GC churn).
  - Rust-to-C# is far easier than C macro soup.

### 3.3 FreeType auto-hinter (`src/autofit`): what each piece contributes

**Docs:** <https://freetype.org/autohinting/hinter.html> (original design, patent-free "from day 1"); source `aflatin.c` (5,102 lines) and `afhints.c` (1,811).

The pipeline per glyph (`af_latin_hints_apply`):

1. **Per-font metrics** (`af_latin_metrics_init_widths` and `init_blues`): standard widths are measured from reference glyphs, both horizontal and vertical. Blue zones (reference and overshoot pairs) come from per-script blue strings (`afblue.dat`).
2. **x-height scale snapping** (`af_latin_metrics_scale_dim`, lines 1178–1300). **This is a big part of FreeType's crispness at 8–16 px, and Native lacks it.** The *vertical scale itself* is changed so the x-height overshoot lands on a pixel boundary. FreeType rounds with threshold 40/64, which biases rounding *up*; with the `increase-x-height` property it uses 52/64 for small ppem. The change is capped so that no blue moves more than 2 px. Every lowercase letter then has a whole-pixel x-height with a crisp top edge.
3. **Segments** (`compute_segments`): runs of points aligned with an axis, in both dimensions.
4. **Linking** (`link_segments`): opposite-direction segment pairs are scored on distance and overlap to form **stems**. Leftover segments link as **serifs**.
5. **Edges** (`compute_edges`): segments at the same position merge into edges, which carry ROUND and SERIF flags.
6. **Blue edges** (`compute_blue_edges`): edges near a blue zone attach to it.
7. **Edge hinting** (`af_latin_hint_edges`, lines 4220+), per axis:
   - blue-zone edges snap first, and their linked stem edges follow via `align_linked_edge`;
   - the first remaining stem becomes the **anchor**, with "voodoo" centre rounding for stems under 1.5 px (`u_off`/`d_off` = 32/32 or 38/26, so a 1 px stem lands on a whole pixel row instead of straddling two);
   - subsequent stems keep their position relative to the anchor;
   - serifs align to their base;
   - lone edges interpolate between neighbours.
8. **Stem width** (`af_latin_compute_stem_width`, lines 3967–4160), by mode (flags set in `af_latin_hints_init`):
   - **LIGHT:** `STEM_ADJUST` is off, so the width is returned unchanged. Only edge positions move, and only in y (`AF_SCALER_FLAG_NO_HORIZONTAL`). Italic faces also skip x.
   - **NORMAL (grayscale):** both axes are hinted with **gentle quantisation**:
     - widths under 56/64 px are raised to 56;
     - widths near the standard width (within 40/64) snap to it, with a 48/64 minimum;
     - widths under 3 px get their fractional part pushed toward 0, 10/64 or 54/64 (i.e. "almost whole");
     - larger widths are rounded, with a ppem-dependent correction for double rounding.
   - **MONO:** `HORZ_SNAP` and `VERT_SNAP` are on: snap to a standard width, then round to whole pixels with a 1 px minimum (y uses `+16` bias rounding).
   - **LCD:** x snapping only, no stem adjust.
9. **Point alignment:** `align_edge_points`, then `align_strong_points`, then `align_weak_points` (an IUP-like interpolation between touched points).
10. **Metrics:** in NORMAL/MONO, the hinted advance is rounded, and `lsb_delta`/`rsb_delta` report how far the hinted left and right edges moved. Clients use these to fix spacing between pairs: FreeType's `ftview` and text layout add ±1 px when `rsb_delta(prev) - lsb_delta(cur)` exceeds ±32/64. **KernSmith could bake this into BMFont kerning pairs**, which addresses "cramped".
11. **Newer:** `afadjust.c` (2023+, by Craig White) adds an adjustment database for diacritics. It keeps accents from colliding with base glyphs at small sizes, with tilde stretching (`af_latin_stretch_top_tilde` and related functions).

**Which pieces produce crispness, ranked for Latin at 8–16 px:**
- (a) the x-height and cap blue snap with scale adjustment;
- (b) whole-pixel or near-whole stem widths that agree across glyphs (standard-width snap);
- (c) in NORMAL/MONO, x-axis stem snapping, so vertical stems are 1–2 full pixel columns instead of 2–3 grey columns. This is the biggest visible difference from Native's `Light`;
- (d) anchor centring of thin stems;
- (e) serif linking (keeps serifs attached and the same size).

Native's `GridFitter` approximates (a) partly (zone snap without the scale change), (b) only in y, and none of (c), (d) or (e).

- **Licence:** FTL/GPLv2. The design was meant to be "absolutely patent-free from day 1" (hinter.html).
- **Size:** about 9k C lines for Latin and core (aflatin, afhints, afglobal, afloader, afblue, afmodule), and 16.7k for the whole module.

### 3.4 FreeType CFF hinter (Adobe engine)

- Adobe contributed this engine in 2013. It is the default `hinting-engine` for CFF/CFF2/Type 1. Files such as `psaux/psblues.c` say they are distributed "under the FreeType Project License" and add a contributor **patent grant**.
- It builds a monotone **hint map** from the charstring's `hstem` hints plus Private DICT `BlueValues`/`BlueScale`/`BlueShift`/`StdHW`, and fits **y only**. It snaps blues when below `BlueScale`, rounds stems to whole pixels, and handles `hintmask` switching and counter control.
- This is essentially what Native's monotone-y map already resembles, but driven by the font's declared hints.
- **Stem darkening** lives in this engine: darkening parameters x1..y4 default to (500,400),(1000,275),(1667,275),(2333,0). `no-stem-darkening` defaults to TRUE (`cffobjs.c:1147`). It has been off by default since 2.6.2, because without linear blending it makes text too dark.
- **Size:** skrifa `cff/hint.rs` is 1,505 lines. SixLabors' clean-room C# `HintMap` is a further learn-only reference; it documents FreeType's exact `UseStdWidth` threshold (0x5700/2^16 ≈ 0.34 px).

### 3.5 Stem darkening, gamma and FreeType's "light vs normal" rationale

**Doc:** <https://freetype.org/freetype2/docs/hinting/text-rendering-general.html> (written for 2.6.2, 2015; updated 2018).

- FreeType says: "Text must be rendered with linear alpha blending and gamma correction". Linear blending with gamma 1.8 "is much better, but text thins out". Hence stem darkening, which "emboldens glyphs at smaller sizes to make them more readable on common low-DPI screens" and "should only be enabled when doing gamma correction".
- macOS and Adobe's engine use stem darkening. Stem darkening is off by default for both CFF and the auto-hinter (since 2.6.2).
- The case for light hinting: it "snaps glyphs to the pixel grid only vertically… a compromise between design fidelity and sharpness that preserves inter-glyph spacing". It suits subpixel positioning, an idea first pushed by Maxim Shemanarev's "Texts Rasterization Exposures" (AGG, 2007) and endorsed by David Turner on the freetype list (<https://lists.gnu.org/archive/html/freetype/2007-07/msg00007.html>).
- **For KernSmith:**
  - FreeType's bitmap is **linear coverage with no gamma**. To match FreeType, Native must not add gamma.
  - The coverage in a BMFont atlas, though, is usually blended by a game engine in sRGB space. An *optional*, documented coverage LUT can make text look heavier, in the spirit of Skia's `SkMaskGamma` (contrast plus device gamma, per luminance; <https://skia.googlesource.com/skia/+/main/src/core/SkMaskGamma.h>). So can a FreeType-style darkening curve.
  - Offer it as an option, off by default.

### 3.6 ttfautohint

**Docs:** <https://freetype.org/ttfautohint/doc/ttfautohint.html>.

- It "converts the glyph analysis done by FreeType's auto-hinting module to TrueType bytecode", y only, using only the "latin" and "dummy" writing systems, with HarfBuzz for feature coverage.
- Useful ideas for KernSmith:
  - a **hinting range** (default 8–50 ppem) and a hinting limit (200);
  - **x-height increase** (`-x`, default 14 ppem: round up much more often below that size);
  - three stem-width algorithms, 'natural', 'quantized' and 'strong', chosen per target. The default `qsq` means quantized for grayscale and DirectWrite, strong for GDI ClearType;
  - a fallback stem width of 50 units at 2048 UPEM for fonts with no reference glyphs;
  - a control-instructions file for per-glyph overrides.
- **Licence:** FTL/GPLv2 (Debian copyright file). Whether output fonts are unencumbered is not stated in the pages I read **[unverified]**, and it doesn't matter for porting.
- **Relevance:** it proves the FreeType auto-hinter's analysis can be expressed as y-only TT instructions. If KernSmith gets an interpreter (rank 1), ttfautohinted fonts (common on Google Fonts) render exactly as intended.

### 3.7 Skia / Chrome

- **Linux/ChromeOS:** FreeType or Fontations with slight (light) hinting by default, subpixel positioning, and gamma/contrast LUTs (`SkMaskGamma`: "creates and maintains tables which convert linear alpha values to gamma correcting alpha values", with "contrast… the amount of artificial contrast to add").
- **Windows:** DirectWrite. **Mac:** Core Text.
- Chrome's Fontations backend in Skia uses skrifa's hinting. I could not confirm whether the skrifa *auto*-hinter is used by default for unhinted web fonts **[unverified]**.
- **Lesson:** Chrome's look depends on runtime subpixel positioning plus gamma/contrast. A BMFont atlas gets neither for free.

### 3.8 DirectWrite / ClearType, and GDI

**Docs:** <https://learn.microsoft.com/en-us/windows/win32/api/dwrite/ne-dwrite-dwrite_rendering_mode>.

- **NATURAL:** antialiasing in the horizontal direction only, with y grid-fitted by the font's hints run in ClearType backward-compatibility mode. That is effectively what FreeType v40 copies.
- **NATURAL_SYMMETRIC:** antialiasing in both directions, typically for larger sizes.
- **GDI_CLASSIC/GDI_NATURAL:** GDI-compatible metrics, with integer or whole-pixel advances.
- The `gasp` table selects grid-fit and symmetric smoothing per ppem.
- **GDI grayscale/mono:** full v35-style x+y hinting with `hdmx` advances. That is the target Arial/Georgia/Verdana were hand-hinted for.
- Beat Stamm's *The Raster Tragedy at Low-Resolution Revisited* (<http://rastertragedy.com>) is the primary treatise on why these choices exist: stems, "dirty" compatibility hacks, and why x hinting matters for bi-level output.
- **Licence:** proprietary; learn only.

### 3.9 Apple Core Text

- No hinting: outlines are scaled with fractional advances.
- On 1× displays, "font smoothing" dilates (stem-darkens) glyphs by an amount that depends on luminance. Subpixel (LCD) AA was removed in macOS 10.14 Mojave, while the dilation stayed (secondary sources: HowToGeek, MacSales).
- Pathfinder's author reportedly reverse-engineered the dilation formula **[unverified, secondhand]**.
- At 8–12 px on 1× screens the result is soft but even. It is not a FreeType-crispness target.

### 3.10 Managed / other-language rasterizers

- **SharpFont (Michael Popoloski, C#, MIT, 2015)** <https://github.com/MikePopoloski/SharpFont>
  - Pure C# parser, rasterizer and TT **interpreter** (`Internal/Interpreter.cs`, 1,614 lines). v35-style; some instructions are stubbed; unmaintained since 2015.
  - Legally the easiest C# starting point. Technically it is behind skrifa: no v40 rules, no `cvar`, fewer edge cases.
  - Not the FreeType binding of the same name.
- **Typography / LayoutFarm (C#, MIT)** <https://github.com/LayoutFarm/Typography>
  - `Typography.OpenFont/TrueTypeInterperter/TrueTypeInterpreter.cs` (2,148 lines) is SharpFont's interpreter (header `//MIT, 2015, Michael Popoloski's SharpFont`).
  - Its "CustomAutoFit" auto-hinter is PixelFarm's experimental `GlyphOutlineAnalyzer`/`DynamicOutline`, which uses triangulation (poly2tri) to find stems. It is not FreeType-grade.
  - Last commit 2023-09; the README says layout "is not stable".
  - **Low value** beyond SharpFont.
- **SixLabors.Fonts (C#)** <https://github.com/SixLabors/Fonts>
  - The TT interpreter is "adapted from SharpFont" and modernised. Its doc comment says `HintingMode.Standard` "matches the behavior of FreeType's v40 subpixel hinting interpreter… X-axis moves are ignored, no point moves after both IUP calls, and SHPIX/DELTAP execute only in their gated forms", and that `HintingMode.Full` "match[es] FreeType's behavior when subpixel hinting is disabled for a mono render target".
  - It also has a CFF `HintMap`, compatibility override lists, and recent activity (2026-09).
  - **Licence:** the Six Labors Split License. It is Apache-2.0 only for transitive consumers, companies under $1M revenue, or non-profits, and commercial otherwise. KernSmith cannot vendor it as MIT: **learn only**. It is a valuable *behavioural* cross-check, since it is a C# design that already solved v40 semantics in managed code.
- **swash (Rust, MIT/Apache)** <https://github.com/dfrg/swash>: README "Asymmetric vertical hinting (TrueType and Postscript)". It now depends on skrifa for outlines and hinting, so use skrifa.
- **ab_glyph, fontdue, font-rs (Rust):** none hint.
  - font-rs is Raph Levien's accumulation-buffer exact-area design (blog "Inside the fastest font renderer in the world", 2016). Native's coverage is already equivalent.
  - fontdue: MIT/Apache/Zlib. ab_glyph: Apache-2.0. font-rs: Apache-2.0.
- **stb_truetype (MIT/public domain):** no hinting. It offers `stbtt_PackSetOversampling` plus a box prefilter so that bilinear sampling gives subpixel positioning (Sean Barrett's `tests/oversample` notes). Not a crispness technique.
- **opentype.js `src/hintingtt.js` (MIT, Axel Kittenberger, 2017):** a TT interpreter written against Apple's reference manual. It uses "full floating point precision" instead of 26.6, ignores xScale/yScale/rotation, misses some instructions, and is off by default (`hinting: false`). Weaker than skrifa.
- **Typr.js (Photopea, MIT):** I found no hinting. Photopea moved to HarfBuzz in 2020.
- **Pathfinder (MIT/Apache), Slug, msdfgen (MIT):** GPU or distance-field renderers without hinting.
  - Pathfinder offers macOS-style stem darkening.
  - The Slug patent (US 10,373,352) was dedicated to the public domain on 2026-03-17 ("A Decade of Slug"; Hackaday 2026-03-20). The reference shaders are reportedly MIT and the Slug Library is still commercial **[unverified]**.
  - Distance fields without hinting are poor at 8–16 px. KernSmith already has SDF/MSDF.

### 3.11 Perceptual tricks independent of hinting

- **Exact-area (box) coverage** is the right filter for small text. Native already has it, and so does FreeType's `ftgrays`. Tent or Lanczos supersampling either blurs (tent) or rings with haloes (Lanczos) at 8–16 px. Super-sampling plus box downsampling only approximates exact area. **The filter is not the problem; geometry is.**
- **Gamma-correct coverage:** what matters is how the *consumer* blends. FreeType returns linear coverage. An atlas consumed by sRGB-space blending looks thin, so an optional coverage LUT helps. Skia's luminance-dependent contrast LUT is the best-engineered open (BSD-3) example.
- **Stem darkening:** the FreeType CFF curve (above), Adobe and macOS dilation. It thickens stems at small ppem in proportion to size. It only looks right with linear blending.
- **Subpixel positioning:** the main reason Chrome, macOS and DirectWrite can afford y-only hinting. In a BMFont atlas it would need multiple x-phase variants per glyph (e.g. 3–4 phases), which BMFont's format doesn't express. That is another reason to prefer x fitting (NORMAL/MONO) for atlases.
- **LCD filters:** FreeType's 5-tap FIR (`FT_LCD_FILTER_DEFAULT` = [0x08,0x4D,0x56,0x4D,0x08]) or ClearType-style. They are only relevant to Native's `Lcd` mode, and LCD atlases are rare in games.
- **Writings:**
  - Beat Stamm, *Raster Tragedy*;
  - FreeType's text-rendering-general article;
  - Shemanarev, "Texts Rasterization Exposures" (<https://agg.sourceforge.net/antigrain.com/research/font_rasterization/>);
  - Aria Beingessner (Gankra), "Text Rendering Hates You" (2019);
  - Behdad Esfahbod's text-rendering notes and GLyphy (<https://behdad.org/text/>);
  - Raph Levien's font-rs post.
  - I did not locate a specific Raph Levien or Behdad paper on small-size hinting quality **[unverified]**.

---

## 4. Implications and suggested plan for Native

1. **Revisit the recorded decisions.** Two recorded decisions are now blockers: "no TrueType bytecode" (`project_facts.md` line 54) and "Light-only, no x fitting" (`project_decisions.md` line 47).
   - FreeType parity on KernSmith's harness fonts needs both an interpreter and x fitting for the NORMAL/MONO targets.
   - Keep the stability guarantee by gating: for example, a new `HintingMode`/`HintTarget` option, or apply hinting to `Grayscale`/`None` only when explicitly opted in. That leaves default output hashes unchanged until a deliberate switch.
2. **Phase A: TT interpreter** (port skrifa `glyf/hint`; FreeType `ttinterp.c` as the oracle).
   - Targets: v40 for grayscale/light (backward-compatibility rules, INSTCTRL), full mode for mono.
   - Run `prep` per size and per mode change, with `cvar` support.
   - Round the advance as FreeType does.
   - Validate pixel-for-pixel against KernSmith's own FreeType backend in `tests/bmfont-compare`. An `fauntlet`-style per-glyph outline diff is the ideal unit-test oracle.
   - Estimate: 6–8k C#, the largest single piece.
3. **Phase B: auto-hinter port** (skrifa `autohint`, Latin style first; CJK/Indic later).
   - Engine selection should follow FreeType's `FT_Load_Glyph` rules quoted in §1.
   - Replace `GridFitter`. Keep its useful safety invariants only where FreeType has equivalents; FreeType does not cap moves at 1 px.
   - Emit `lsb_delta`/`rsb_delta` and optionally fold them into kerning pairs.
4. **Phase C: CFF hint map** (skrifa `cff/hint.rs`), then optional, off-by-default gamma/darkening LUTs.
5. **Licensing hygiene:** credit FTL in `THIRD-PARTY-NOTICES` and the Native README, plus the MIT/Apache notice for fontations. Mark ported files ("Ported from fontations skrifa …, derived from FreeType …").

---

## 5. Unverified or open items

- The legal basis for fontations relicensing FreeType-derived code as MIT/Apache.
- Whether Chrome uses the skrifa auto-hinter by default.
- The Slug reference-shader licence.
- The `hintingtt.js` line count.
- Whether ttfautohint output fonts are licence-free.
- Apple dilation details.
- Expiry dates of the TrueType and ClearType patents. Apple's TrueType hinting patents are widely reported to have expired in 2010; Microsoft's ClearType patents expired later in the 2010s.
- SixLabors' visual parity with FreeType.

Everything stated about FreeType behaviour, skrifa structure and sizes, and SixLabors/SharpFont/Typography headers and sizes was checked directly in source. The clones are under `<session scratch>/research/src/`.
