# Phase 179 — Native Rasterizer: Validation, Golden Masters & Release

> **Status**: In progress — robustness, golden masters, the similarity matrix and the cross-cutting docs are done; remaining: performance validation (after Phase 177), an AOT Blazor WASM run, a library usage sample, and the release decision (publish Native, with `KernSmith.Imaging`, to NuGet?)
> **Created**: 2026-04-01
> **Depends on**: Phases 166–174, 177 and 178 (175 and 176 only if Jeremy keeps them)

## Goal

Comprehensive validation of the Native rasterizer against FreeType and StbTrueType baselines, establishment of golden master regression tests, and the release decision (publishing to NuGet is Jeremy's call — the project is `IsPackable=false` today).

## Scope

### Golden masters (done)

`NativeGoldenMasterTests` (with `NativeGoldenModeTests`, `NativeGoldenDistanceFieldTests`, `NativeGoldenFeatureTests`, which run in parallel) pins Native's exact output; any changed byte fails. The matrix is `Goldens/GoldenCatalog.cs`:

- Every committed outline fixture (Roboto, Noto Sans and its CFF build, Source Serif 4, Source Code VF, Roboto Flex and its Latin subset, Dancing Script, Bungee Color and Reem Kufi Ink as plain outlines) × 8/12/16/24/48/96 px × `None`/`Grayscale`/`Light` (hinted)/`Lcd`; Noto Color Emoji (bitmap-only) has its own color, coverage and distance-field goldens.
- SDF/MSDF/MTSDF at 16 and 32 px for every font; bold, italic and bold-italic at 16 and 48 px; styles through mono, Light, LCD, SDF and MSDF; 12pt at 96 dpi.
- Variation instances: Roboto Flex wght 100 and 900, Source Code VF wght 900 (CFF2), Dancing Script wght 700, through grayscale, mono, Light, LCD, SDF and MSDF.
- Color: Bungee palettes 0 and 6 and Reem Kufi Ink (COLR v1) at 16/48/96 px, plus color under Light, mono, bold-italic and SDF.
- Pipeline: `BmFont.Generate` configurations (`.fnt` text with the version blanked and LF line endings, plus atlas pixels). The four fonts with a `.woff2` twin must reproduce the TTF/OTF golden exactly from the WOFF2 — asserted, not stored twice.

Storage (about 0.6 MB): `Goldens/native-goldens.json` holds one SHA-256 per case (pass/fail) plus an 8-hex-digit fingerprint per glyph (or per `.fnt`/page) that names what changed; `Goldens/fnt/` holds every pipeline case's `.fnt`; `Goldens/png/` holds reference contact sheets for key cases and a few atlases, for review. Hashes cover pixels and metrics, never PNG bytes (deflate output differs between runtimes).

On a mismatch the test lists the changed glyphs with their new box, bearings and advance, writes `actual.png` under `GoldenFailures/<case>/` in the test output folder and, where a reference PNG exists, `expected.png`, a magenta `diff.png` and the largest level difference; pipeline cases also write both `.fnt` files and name the first differing line. Hash-only cases have no expected image.

Regenerate after a deliberate output change, then review the manifest and PNG diff (never in CI — the mode throws when `CI` is set):

```
KERNSMITH_UPDATE_GOLDENS=1 dotnet test --project tests/KernSmith.Rasterizers.Native.Tests -- --filter-class "*NativeGolden*"
```

Determinism: identical on net8.0 and net10.0 (x64). Native's arithmetic is float with no SIMD-width-dependent reductions (the SDF splat's lane-wise `Vector<float>` path is held bit-identical to its scalar fallback by test); its only libm calls (`Math.Pow`/`Sin`/`Cos`/`Tan`/`Atan2`, in double, for COLR v1's sRGB tables, transforms and sweep angles, vector-outline round joins and the italic shear) are rounded to float or bytes, so a last-ulp libm difference between Windows, glibc and Apple can flip a value only at an exact rounding boundary. arm64 is unverified locally; CI's macOS leg (arm64) is the first check.

### Similarity matrix (done)

`NativeSimilarityMatrixTests` measures every fixture font (Roboto Flex Latin is left out: same outlines as Roboto Flex) × mode × size against FreeType, and grayscale/mono against stb_truetype, plus SDF, synthetic styles, variation instances and COLR v0 against FreeType: mean and worst-glyph SSIM over printable ASCII (SDF: the shorter golden sample; Reem Kufi adds Arabic), advance exact, bearings and box within one pixel. It asserts floors set below these numbers with margin and writes the full per-(font, mode, size) table to `similarity-matrix.md` in the test output folder. Not compared, by design: MSDF/MTSDF (no other backend), COLR v1 (FreeType's backend renders it grayscale), stb SDF (different encoding), stb Light/LCD (stb renders both as grayscale), stb with CFF2 or variations. FreeType runs unhinted except for Light, where both sides hint.

Grayscale mean SSIM vs FreeType / vs stb_truetype:

| Font | 8px | 12px | 16px | 24px | 48px | 96px |
|---|---:|---:|---:|---:|---:|---:|
| roboto | 0.995 / 0.998 | 0.995 / 0.995 | 0.996 / 0.992 | 0.998 / 0.996 | 0.998 / 0.997 | 0.999 / 0.998 |
| notosans | 0.996 / 0.999 | 0.998 / 0.998 | 0.996 / 0.993 | 0.999 / 0.995 | 0.998 / 0.997 | 0.999 / 0.998 |
| notosans-cff | 0.997 / 0.996 | 0.999 / 0.997 | 0.999 / 0.999 | 0.997 / 0.997 | 0.999 / 0.999 | 0.999 / 0.999 |
| sourceserif | 0.993 / 0.992 | 0.996 / 0.993 | 0.998 / 0.996 | 0.999 / 0.998 | 0.998 / 0.997 | 0.999 / 0.999 |
| sourcecode | 0.988 | 0.992 | 0.997 | 0.998 | 0.998 | 0.999 |
| robotoflex | 0.991 / 0.997 | 0.996 / 0.994 | 0.993 / 0.988 | 0.997 / 0.998 | 0.997 / 0.996 | 0.998 / 0.998 |
| dancingscript | 0.999 / 1.000 | 0.998 / 1.000 | 0.997 / 1.000 | 0.993 / 0.998 | 0.996 / 0.994 | 0.997 / 0.996 |
| bungee | 0.999 / 1.000 | 0.998 / 0.999 | 0.999 / 0.999 | 0.999 / 0.999 | 0.997 / 0.999 | 0.999 / 0.996 |
| reemkufi | 0.999 / 1.000 | 0.995 / 1.000 | 0.994 / 1.000 | 0.997 / 0.998 | 0.999 / 0.995 | 0.998 / 0.998 |

All rows, worst font per size:

| Case | vs | Sizes | Mean SSIM (worst font) by size | Worst glyph | Advance exact | Bearings ±1 | Box ±1 |
|---|---|---|---|---:|---:|---:|---:|
| none | FreeType | 8/12/16/24/48/96 | 0.459 · 0.657 · 0.750 · 0.866 · 0.947 · 0.969 | -0.57 (sourcecode 12px (=)) | 86% | 99% | 93% |
| none | Stb | 8/12/16/24/48/96 | 0.154 · 0.453 · 0.661 · 0.853 · 0.948 · 0.975 | 0.00 (roboto 8px (")) | 98% | 98% | 35% |
| grayscale | FreeType | 8/12/16/24/48/96 | 0.988 · 0.992 · 0.993 · 0.993 · 0.996 · 0.997 | 0.83 (roboto 12px (%)) | 86% | 100% | 100% |
| grayscale | Stb | 8/12/16/24/48/96 | 0.992 · 0.993 · 0.988 · 0.995 · 0.994 · 0.996 | 0.77 (robotoflex 16px (5)) | 98% | 100% | 100% |
| light | FreeType | 8/12/16/24/48/96 | 0.487 · 0.680 · 0.728 · 0.819 · 0.894 · 0.901 | -0.81 (reemkufi 12px (=)) | 57% | 91% | 69% |
| lcd | FreeType | 8/12/16/24/48/96 | 0.990 · 0.994 · 0.994 · 0.996 · 0.999 · 0.998 | 0.89 (sourcecode 8px (.)) | 86% | 100% | 99% |
| sdf | FreeType | 16/32 | 0.999 · 0.994 | 0.93 (bungee 32px (y)) | 94% | 100% | 100% |
| bold | FreeType | 16/48 | 0.993 · 0.993 | 0.82 (dancingscript 48px (ñ)) | 95% | 99% | 98% |
| italic | FreeType | 16/48 | 0.995 · 0.994 | 0.87 (roboto 16px (6)) | 96% | 87% | 83% |
| bold-italic | FreeType | 16/48 | 0.992 · 0.995 | 0.75 (reemkufi 16px (و)) | 95% | 85% | 76% |
| wght | FreeType | 12/24/48 | 0.992 · 0.995 · 0.996 | 0.87 (sourcecode 12px (%)) | 96% | 100% | 100% |
| color (COLR v0) | FreeType | 16/48/96 | 0.990 · 0.996 · 0.998 | 0.94 (bungee 16px (8)) | 96% | 100% | 100% |

Known differences behind the low columns:

- **Light** — FreeType runs the font's own hinting (TrueType bytecode, the CFF hinter) and moves advances; Native auto-hints y only, keeps unhinted advances and moves no point more than a pixel, so the two can round an edge a pixel apart in opposite directions.
- **None** — FreeType's mono renderer samples pixel centres; Native thresholds coverage at 50%; both apply dropout control, so one-pixel strokes land a row or column apart. stb has no dropout control (Dancing Script loses its hairlines). Native crops mono boxes to their ink, stb keeps the anti-aliased box (Box ±1 vs stb).
- **Advances vs FreeType** — FreeType rounds a scaled advance to 26.6 before rounding to pixels: Roboto's digits at 8px (1151 units = 4.496 px) become 5 px there and 4 px in Native and stb. stb agrees exactly except at exact half-pixel ties (Roboto Flex `e` at 16px is 8.5 px), which the StbTrueType backend rounds to even and Native, like FreeType, rounds up.
- **Italic boxes** — FreeType sizes a slanted glyph from its control box, Native from the tight outline (bearings/box at 48px).
- **Lcd** — the FreeType backend returns its raw three-bytes-per-pixel buffer labelled `Grayscale8`; the matrix splits it into R/G/B planes and divides its width by three.

### Robustness (done)

- **Hostile input** — `NativeFuzzTests` damages each fixture (Roboto, SourceSerif4 CFF, SourceCodeVF CFF2, RobotoFlex, BungeeColor COLR v0, ReemKufiInk COLR v1, and the `.woff2` fixtures through core) table by table — truncation, bit flips, offset corruption, count inflation, random byte runs — and runs `LoadFont`, font and glyph metrics and `RasterizeGlyph` under every render mode, a variation instance and a palette switch, plus `BmFont.Generate` for the core path. Deterministic per seed; `KERNSMITH_FUZZ_ITERATIONS` / `KERNSMITH_FUZZ_SEED` run longer sweeps. Allowed outcomes: success, `FontFormatException` / `FontParsingException` / `RasterizationException`, the palette `ArgumentOutOfRangeException`, and core's packer `InvalidOperationException` for a glyph larger than the page. Each bug found has a minimised regression test (`NativeHostileInputTests`, `TtfParserHostileInputTests`).
- **Limits** — a glyph box over 64 ems, or a bitmap over 16384 px per side after distance-field padding, is a `RasterizationException` (`GlyphLimits`); non-positive or non-finite `Size` and negative `Dpi` are an `ArgumentOutOfRangeException` (`Dpi` 0 means 72, as on FreeType).
- **Edge cases** — `NativeEdgeCaseTests` pins sizes 0.5-2000 px, DPI extremes, blank/unmapped/zero-contour/single-point/collinear/zero-area glyphs, int16-extreme coordinates, SDF spreads 0/1/huge/NaN, tiny MSDF, LCD at 1 px, hinting at 1-6 px, out-of-range and NaN axis values, and out-of-range palettes.
- **Thread safety** — core never shares a rasterizer: each `Generate` (and each `GenerateBatch` job) creates its own and rasterizes on one thread; `NativeThreadSafetyTests` checks concurrent and parallel-batch output is byte-identical to serial.
- **NativeAOT / trim** — `tests/KernSmith.PlatformSmoke` publishes with `PublishAot=true` (win-x64) with zero trim/AOT warnings and passes its WOFF2, CFF, CFF2, COLR v0/v1 and MSDF checks.

### Edge Case Testing

- Empty glyphs (space, zero-width)
- Composite glyphs with deep nesting (accent stacking)
- Overlapping contours
- Very small sizes (8px) — verify no crashes
- Very large sizes (200px) — verify no overflow
- Degenerate curves (zero-length, duplicate points)
- Fonts with unusual table ordering
- CFF fonts (if Phase 166 complete)
- Variable fonts at axis extremes (if Phase 171 complete)
- Corrupt/truncated font data — verify graceful errors, not crashes

### Unsupported Feature Behavior

Whatever is still unsupported at release must behave deliberately, and the docs must say which cases are silent fallbacks and which throw. Today's behavior, to be re-verified at release:
- An anti-alias mode a backend does not advertise → core renders it as `Grayscale`, silently (decided; Native advertises all four)
- `OutlineMode.Vector` on a color face (`ColorFont`) or a custom glyph → core's bitmap `OutlineEffect`, silently; with a distance field → `InvalidOperationException`
- `'jpg '`/`'tiff'` `sbix` graphics and non-PNG bitmap formats → skipped (the outline, or nothing, draws); a distance field without `ColorFont` on a bitmap-only face → `NotSupportedException`
- System fonts by name → `NotSupportedException` from `LoadSystemFont`
- MSDF/MTSDF with `ColorFont`, effects, fill color, channel packing/config or variants → core throws `InvalidOperationException`
- Hinting outside `Light` → unhinted, silently (decided)
- GSUB → no substitution (Phase 175 awaits Jeremy's decision)

### Performance Validation

- Benchmark against targets from Phase 177
- Memory usage profiling (peak, average, allocation count)
- WASM performance test (if applicable)

### Documentation

- Blazor WASM: the published sample (interpreted) generates with Native and StbTrueType in headless Chrome, all checks pass, no console errors; README table shows ✅. Still to run: the AOT build (`RunAOTCompilation`, needs the `wasm-tools` workload)
- A library usage example in `samples/` (the MonoGame minimal sample already takes `--rasterizer native`)
- Keep the backend tables, `docs/rasterizers/native.md`, the Native README and REF-12 current as the open decisions land (the known differences vs FreeType are in the similarity matrix above)

### Release Preparation

- `NativeCapabilities` accurately reflects every supported feature and AA mode
- Publish `KernSmith.Imaging` with Native (Native references it; both are `IsPackable=false` today)
- Native and `KernSmith.Imaging` build without `AllowUnsafeBlocks` (decided, see `project_decisions.md`)
- Ensure all analyzer warnings resolved (trimming, AOT, nullable)
- Code review pass for security (no buffer overflows, bounds checking)
- License headers on all new files (MIT)
- Final manual visual inspection of output at multiple sizes

### Regression coverage

- Golden masters live in the Native test project; any changed byte fails
- `regression_check.py` runs with every config the feature phases added (`otf-plain`, `msdf`, `variable-wght700`, `color-colr`, `lcd`, hinting pairs, `woff2-plain`); each must show Native output present, not just "no diff"

## Success Criteria

- [x] SSIM > 0.95 for standard rendering across all reference fonts/sizes (mean per font and size, grayscale vs FreeType and stb)
- [ ] Glyph metrics are correct by the font's own glyph math, with FreeType as the reference approximation: within ±1 pixel of FreeType, and every difference beyond that explained, validated against the font data and signed off by Jeremy (open: italic boxes and FreeType's 26.6 advance rounding — see the similarity matrix)
- [x] Golden master tests established and passing
- [x] All edge cases handled without crashes
- [ ] Performance meets Phase 177 targets
- [x] No trimming/AOT/nullable warnings
- [ ] Documentation complete
- [ ] Ready for production use as `RasterizerBackend.Native`
