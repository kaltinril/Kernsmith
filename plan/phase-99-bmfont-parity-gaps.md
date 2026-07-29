# Phase 99 -- BMFont Parity Remaining Gaps

> **Status**: Planning -- 2 of the 8 originally listed gaps are no longer gaps (see below)
> **Size**: Medium
> **Created**: 2026-03-27
> **Updated**: 2026-07-28
> **Origin**: Remaining gaps from Phase 78BB (GDI parity)
> **Goal**: Investigate and close remaining metrics differences between KernSmith's GDI backend and BMFont's output.

## Background

Phase 78BB achieved near-exact BMFont parity: 14/15 lineHeight exact, 15/15 base exact, 14/15 xadvance exact, kerning amounts exact on all shared pairs. The remaining gaps documented below are systematic differences rooted in architectural choices (GDI rendering path vs BMFont's proprietary outline renderer) and edge cases.

**Since this doc was written, gaps 6 and 7 as originally stated are no longer accurate:**

- **Gap 7 (GDI `lfHeight` sign) is fully resolved** and has been moved to the [Resolved](#resolved) section. The Phase 78C work is merged (`plan/done/`).
- **Gap 6 (atlas PNG channel configuration) shipped on the core/CLI path.** KernSmith no longer ignores `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl`. The `.bmfc` writer round-trip was closed on 2026-07-28. What remains under gap 6 is confined to the desktop UI, which drops `Channels` on generate and conflates it with channel *packing* on load, so the section has been rewritten to track only those.

Gaps 1-5 and 8 are unchanged.

## Remaining Gaps

### 1. xoffset +/-1 systematic -- BMFont's fixed 8x internal supersample

**Root cause**: BMFont renders its own outlines at 8x via `DrawGlyphFromOutline` using `GGO_NATIVE` + polygon fill, producing different sub-pixel positions than `GetGlyphOutlineW(GGO_GRAY8_BITMAP)`. These are fundamentally different rendering paths in the Windows GDI stack. The 8x supersample approach was tested and reverted because it makes other metrics worse when applied to `GGO_GRAY8_BITMAP` output.

**Status**: Not fixable without implementing BMFont's own outline renderer (GGO_NATIVE polygon fill + 8x rasterization). This would be a major undertaking for diminishing returns (+/-1 pixel).

### 2. yoffset +/-1-3 -- downstream of xoffset + bearingY rounding

Same root cause as xoffset. BMFont computes yoffset from its supersampled outline data, while KernSmith uses `gmptGlyphOrigin.Y` from the GDI rasterizer. The difference is inherent to the different rendering paths.

### 3. Bell MT lineHeight -1 at 16pt

`CreateFont` (positional parameters, used by BMFont) vs `CreateFontIndirectW` (LOGFONTW struct, used by KernSmith) edge case. Only affects one font at one size. Low priority.

### 4. Bell MT xadvance and kerning differences

Same root cause as xoffset -- BMFont's outline renderer produces different advance widths than `GetGlyphOutlineW`. The avg +1.31 at 48pt is an artifact of the different rendering paths.

### 5. Missing kerning pairs (Bahnschrift)

GPOS class 0 fix reduced but didn't fully resolve this. The remaining missing pairs come from an unknown source in BMFont's kerning pipeline. Possible causes:
- BMFont may expand class pairs differently or have additional fallback logic
- BMFont's 32-bit GPOS parser may handle edge cases differently than our implementation
- There may be additional kerning sources (kern table fallback after GPOS) that BMFont merges

### 6. Atlas PNG channel configuration -- core/CLI shipped, UI + `.bmfc` round-trip still open

**Core/CLI path: IMPLEMENTED.** `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` are parsed at `src/KernSmith/Config/BmfcConfigReader.cs:208-223` (plus `invA`/`invR`/`invG`/`invB` at `:224-239`), mapped by `ParseChannelContent` at `:457-467` (`0`=Glyph, `1`=Outline, `2`=GlyphAndOutline, `3`=Zero, `4`=One, `5`=Shadow -- `5` is a KernSmith extension with no BMFont.exe equivalent), and assigned to `options.Channels` at `:406-407`. The gate is `BmFont.ShouldApplyChannelConfig` (`src/KernSmith/BmFont.cs:1282-1283`): `options.Channels is { IsDefault: false }`, with `IsDefault` defined at `src/KernSmith/Config/ChannelConfig.cs:20-25`. Compositing runs at `BmFont.cs:672-683` via `ChannelCompositor.Build`, with per-channel resolution at `src/KernSmith/Atlas/ChannelCompositor.cs:148-151` and `ResolveChannel` at `:273-292`. The `alphaChnl`/`redChnl`/... descriptor is written by `src/KernSmith/Output/BmFontModelBuilder.cs:83-88`. CLI wiring: `BmfcParser.cs:53-54` and `GenerateCommand.cs:212-213`.

The old "gated to preserve effect fonts" caveat is **also** outdated: CHANGELOG 0.18.0 (#169) removed the effects exclusion, so the gate is now purely "a non-default channel config is present". Tests: `tests/KernSmith.Tests/BmFontChannelGateTests.cs:13-46` and `tests/KernSmith.Tests/Integration/ChannelGatingTests.cs:28-70`.

**Status: CLOSED (2026-07-29).** All four items below are resolved.

1. ~~**Desktop UI generation drops `Channels` entirely**~~ -- **RESOLVED**. `GenerationRequest.Channels` now carries the config, `MainViewModel` maps it from `Effects.Channels`, and `GenerationService` assigns `options.Channels = request.Channels`.
2. ~~**`ProjectService` mis-maps the two mechanisms**~~ -- **RESOLVED**. `ProjectService` now reads `effects.ChannelPackingEnabled = options.ChannelPacking` (it previously never read `ChannelPacking` at all, so `fourChnlPacked=1` did not tick its own checkbox) and preserves `options.Channels` separately.
3. ~~**UI save loses channel config**~~ -- **RESOLVED**. `BuildOptions` now sets both `Channels` and `ChannelPacking`; the latter was also being dropped on save.

> **Note on scope**: the UI still has **no per-channel content editor** -- the "Channels" expander contains only a "Channel Packing" checkbox. The fix is *preservation*, not editing: a `ChannelConfig` loaded from a project now survives load -> generate -> save unmodified instead of being silently discarded. Building an actual per-channel editor is a separate feature.
>
> Covered by `tests/KernSmith.Ui.Tests/ChannelConfigRoundTripTests.cs`, a new test project -- the UI services previously had **zero** automated coverage.
4. ~~**`.bmfc` writer never round-trips channel config**~~ -- **RESOLVED (2026-07-28)**. `BmfcConfigWriter.WriteChannelConfig` now emits `alphaChnl`/`redChnl`/`greenChnl`/`blueChnl` and `invA`-`invB` at the end of the `# output file` section, matching where BMFont's own `.bmfc` files place them. Nothing is written for a null or all-default `ChannelConfig`, so existing output stays byte-identical and an unset `Channels` does not become non-null on round-trip (which item 2 below would misread as "channel routing requested"). Covered by nine round-trip tests in `tests/KernSmith.Tests/Config/BmfcConfigReaderWriterTests.cs`.

> **Note**: the CHANGELOG 0.15.2 phrase "the UI/Gum path already respected" referred to the Gum integration **packages** (removed from this repo in 0.17.0), not `apps/KernSmith.Ui`.

### 9. `.bmfc` write-side round-trip gaps (RESOLVED 2026-07-28)

Found while closing gap 6 item 4 -- the same defect class (reader parses it, writer never emits it), across other options.

- **`aa` was written with the wrong meaning.** The writer emitted the AA *mode* (`aa=2` for `AntiAliasMode.Light`, else `aa=1`), while `BmfcConfigReader:130-133` reads `aa` as BMFont's **supersampling factor** -- and its own code comment said so. Because the corrective `superSample` extension was only written when `SuperSampleLevel != 1`, nothing cancelled it: `--anti-alias light` at the default supersample wrote `aa=2`, which reloaded as `SuperSampleLevel = 2` with the mode silently downgraded to `Grayscale`. That is a real pixel-output change on a path users hit, since `BmFontResult.ToFile` writes a companion `.bmfc` that gets fed back in. `aa` now carries `SuperSampleLevel`; `Light`/`Lcd` ride on a new `antiAlias` extension key; `superSample` is still *read* for older configs but no longer written, removing a fragile dependence on key ordering within the file.
- **`ShadowOpacity`, `SdfScale`, `HardShadow`, `VariationAxes`** were dropped on write. `.hiero` already persisted `ShadowOpacity` (`HieroConfigWriter.cs:166`) and `SdfScale` (`:178`), so `.bmfc` was the lossier of the two formats for those. `HardShadow` additionally degraded the `variantShadow` round trip, because `BmfcConfigReader:412` rebuilds the shadow `AtlasVariant` from `options.HardShadow`.

All new keys emit nothing at their defaults, so default `.bmfc` output is unchanged. Verified against the regression harness: output is pixel-identical to `main` (see the harness caveat in *Comparison Tooling* below).

### Still open on the write side (interop-only, deliberate)

These BMFont keys are parsed-then-ignored and are not re-emitted, so a `.bmfc` round-tripped through KernSmith loses them for BMFont.exe: `charSet`, `useUnicode`, `disableBoxChars`, `outputInvalidCharGlyph`, `renderFromOutline`, `useClearType`, `outBitDepth`, `textureCompression`, `widthPaddingFactor`, `autoFitFontSizeMin`/`Max`. Also `autoFitNumPages` maps to an option but is re-emitted as the KernSmith-only `autofit=1`. None affect KernSmith's own output.

### 8. Anti-aliasing gradient -- GGO_GRAY8_BITMAP vs GGO_NATIVE polygon fill

**Root cause**: Our GDI backend uses `GGO_GRAY8_BITMAP` which produces smooth anti-aliasing with many intermediate gray levels (GDI's 65-level grayscale remapped to 0-255). BMFont uses `GGO_NATIVE` to extract vector outlines and rasterizes polygons itself with an 8x internal supersample, producing sharper edges with fewer intermediate gray values. Side-by-side character comparison (Phase 78C testing) shows visibly more anti-aliasing tones in our output vs BMFont's crisper edges.

**Status**: Not fixable without implementing BMFont's `GGO_NATIVE` polygon extraction and manual scanline rasterization (Path A: `DrawGlyphFromOutline`). Same root cause as gaps 1-2 (xoffset/yoffset). This is the fundamental architectural difference between our approach and BMFont's.

## Resolved

### 7. GDI lfHeight sign -- cell height vs em height (RESOLVED in Phase 78C)

**Original root cause**: our GDI backend passed a negative `lfHeight` to `CreateFontIndirectW` (em height mode), while BMFont passes a positive `lfHeight` (cell height mode). For Georgia at size 56, this produced `tmHeight=65` (negative/em) vs `tmHeight=56` (positive/cell), because a negative `lfHeight` excludes internal leading.

**Fix**: `GdiRasterizer.CreateHFont()` now computes `LfHeight = (int)Math.Round((double)size * options.Dpi / 72)` -- positive, with integer-pixel rounding (rounding behavior documented at `src/KernSmith.Rasterizers.Gdi/GdiRasterizer.cs:339-341`, assignment at `:345`). There is no negation anywhere in the file. Note that `size` here is the `sizeOverride` parameter (`:337-342`), which every caller passes as `options.Size * aa` (`:122`, `:158`, `:199`, `:255`) -- not `options.Size` directly. This makes `lineHeight` and `base` match BMFont exactly for all tested fonts.

**Status**: Resolved and merged -- `phase-78c` lives in `plan/done/`, so the old "Fixed in Phase 78C branch" note is stale.

## Comparison Tooling

All tooling lives under `tests/bmfont-compare/`; see `tests/bmfont-compare/README.md` for the full per-tool breakdown. (The directory itself is gitignored -- only the tool projects, `README.md`, the Python scripts, and a handful of `.bmfc` configs are tracked.)

> **Known false positive (observed 2026-07-28)**: `regression_check.py` can report a small non-zero pixel diff that has nothing to do with the change under test. A run on a branch touching only `.bmfc` reader/writer code reported 42 differing pixels across `Font18Arial`, `Font24Arial` and `plain-nosmoothing`. Regenerating `main` into a clean `git worktree` and re-diffing showed **main vs main produced the same 41/37/36-pixel diff**, while **main vs branch produced 0**. Roughly half the pixels are in the harness's own GDI+ title label (`GenerateAll/Program.cs:519` draws it with `new Font("Arial", 11, FontStyle.Italic)`); the rest are ±8-11 alpha on glyph edges, i.e. beyond what `--tolerance 1` absorbs. Two back-to-back runs on the same commit are bit-identical, so the instability appears tied to process/system state around the harness's `git checkout` rather than to run-to-run randomness.
>
> **If you see a small diff, confirm it before believing it**: generate `main` into a separate worktree and diff main-vs-main. Note also that `diff_comparisons.py` only discovers `*.png` and `*.fnt` -- it never diffs `.bmfc`, so the harness gives **zero** coverage of config reader/writer changes regardless.

**Canonical entry point**

- `tests/bmfont-compare/regression_check.py` -- the main-vs-branch regression harness; this is what CLAUDE.md and `README.md:40-61` mean by "run a comparison". Stashes, checks out the base branch, generates, checks out the feature branch, regenerates, then diffs. Flags: `--base`, `--branch`, `--output`, `--tolerance`, `--skip-generate`. Exit codes: `0` = identical, `1` = differences, `2` = error. Usage: `python tests/bmfont-compare/regression_check.py`
- `tests/bmfont-compare/diff_comparisons.py` -- the diff step `regression_check.py` drives (`README.md:99-132`): magenta-highlighted (`#FF00FF`) pixel diffs of `comparison.png`-`comparison4.png` and the auto-discovered per-font `comparison-*.png`, plus line-by-line `.fnt` metadata diffs (skipping the version line). Ignores the top 41 rows by default (`--ignore-top 41`, the header label band).
- `tests/bmfont-compare/README.md` -- per-tool documentation, flags, and exit codes.

**Generators**

- `tests/bmfont-compare/GenerateAll/` -- drives **four** KernSmith backends (FreeType, GDI, DirectWrite, **stbtruetype** -- `GenerateAll/Program.cs:70-76`), plus BMFont64.exe when it is found (`:64-68`, `:122`). It iterates **every** `.bmfc` file in the input directory (`:59`, `:82`) -- roughly 20 configs in `gum-bmfont/`, not just the fire-effect and plain ones -- and additionally synthesizes real and synthetic bold/italic passes (`:177`, `:262`). Outputs `comparison.png`/`comparison2.png`/`comparison3.png`/`comparison4.png` (`:342-345`) plus a per-font `comparison-{config}.png` for every remaining config (`:444-449`). Usage: `dotnet run --framework net10.0-windows -- [bmfc-dir] [output-dir]` (`Program.cs:40-49`). **A single positional argument is read as the BMFC directory, not the output directory** -- passing only an output dir points the tool at a nonexistent config dir and exits 1 (`:51-55`).
- `tests/bmfont-compare/GenerateGdi/` -- regenerate KernSmith GDI output
- `tests/bmfont-compare/GenerateDirectWrite/` -- regenerate KernSmith DirectWrite output
- `tests/bmfont-compare/CompareGlyphs/` -- character-by-character visual comparison across all backends + BMFont64, outputs `comparison.png` (fire effects) and `comparison2.png` (plain white). Usage: `dotnet run --framework net10.0-windows -- <data-dir>`. Gracefully skips missing backends.

**Single-purpose diff scripts**

- `tests/bmfont-compare/diff_all_fonts.py` -- multi-font BMFont vs GDI diff
- `tests/bmfont-compare/diff_fnt.py` -- single-font comparison
- `tests/bmfont-compare/diff_images.py` -- visual atlas comparison

**Data directories**: `gum-bmfont/` (source `.bmfc` configs + BMFont64 reference output), `gum-gdi/`, `gum-freetype/`, `gum-directwrite/`, `bmfont/`, `kernsmith/`, and `output/` (generated output, baselines, and diff images).

## Potential Approaches

1. **Fixed 8x internal supersample** in GDI backend -- addresses xoffset, yoffset, likely Bell MT xadvance. Attempted and reverted in 78BB because it worsened metrics when applied to GGO_GRAY8_BITMAP. May need GGO_NATIVE + polygon fill approach instead.
2. **otmrcFontBox-based kerning scaling** -- switch from ppem/unitsPerEm to match BMFont's GPOS scaling formula. Shared change but only affects kerning math.
3. **Accept as known limitations** -- document that exact parity requires BMFont's proprietary outline renderer, which is outside scope.
