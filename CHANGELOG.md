# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **The experimental Native rasterizer is now close to feature-complete**, still entirely KernSmith's own managed C# and still unpublished (use a source build or the CLI's `--rasterizer native`):
  - **OpenType fonts with PostScript outlines** (`.otf`): CFF, CID-keyed CFF and CFF2. A malformed CFF table fails `LoadFont` with a `FontFormatException` instead of the old blanket rejection of every CFF font.
  - **Variable fonts** at the requested instance (`VariationAxes`, `.bmfc` `variationAxes`, CLI `--axis`): `fvar`/`avar` normalization, `gvar` outline deltas (composites and inferred deltas included) for TrueType, charstring `blend`/`vsindex` for CFF2, `HVAR` advances (phantom points for a TrueType font without `HVAR`) and `MVAR` line metrics. `SupportsVariableFonts` is true.
  - **Color fonts** with `ColorFont`: `COLR` version 1 paint graphs (solid fills, linear/radial/sweep gradients with pad/repeat/reflect, glyph clips, reused color glyphs, every transform, all 28 composite modes, clip boxes, and variable paints at the selected instance) and `COLR` version 0 layers, filled from the font's `CPAL` palettes into straight-alpha RGBA glyphs; `ColorPaletteIndex` selects the palette. Version 1 gradients and composites are computed in linear light, as the `COLR` spec requires; cyclic or invalid paints are skipped, and graphs too deep or large fall back to plain coverage. Glyphs with no color definition render as grayscale. `SupportsColorFonts` is true.
  - **Color bitmap glyphs** with `ColorFont`: `CBDT`/`CBLC` (index formats 1-5, PNG image formats 17-19) and `sbix` (`'png '` graphics, `'dupe'` records, origin offsets), so bitmap-only fonts such as Noto Color Emoji now load. Each glyph uses the smallest strike at or above the requested size (else the largest), scaled to that size — area averaging down, bilinear up — where the FreeType backend returns the strike's own size; at the strike size the two match. `COLR` wins over a bitmap, a bitmap over the outline; without `ColorFont` a bitmap-only font renders grayscale ink coverage. PNGs are decoded by the new **`KernSmith.Imaging`** project — our own pure managed, hardened PNG decoder (every color type and bit depth, Adam7, CRC and size checks, decompression-bomb limits; tested against the full PngSuite), not yet published.
  - **Synthetic bold and italic** as outline transforms matched to the FreeType backend: the same emboldening strength (`max(0.5px, ppem/24)`, advance grown to match) and the same 12° slant, sized from the styled outline so nothing is clipped. They apply to variable instances, color layers and glyph clips, and distance fields. Native reports `SupportsSyntheticBold` / `SupportsSyntheticItalic`, so core skips a caller's `BoldPostProcessor` / `ItalicPostProcessor` for it as for the other backends.
  - **Signed distance fields** (`Sdf`) computed from the outline, matching FreeType's SDF output (encoding, padding, bearings) and honoring `SdfSpread` and `SdfScale`. Overlapping contours leave no interior seam (FreeType's SDF has one). `SupportsSdf` is true, and the package tags advertise `sdf` again.
  - **LCD subpixel output** for `AntiAliasMode.Lcd`: RGBA glyphs with one subpixel per colour channel and alpha set to the strongest subpixel, so an LCD atlas is an RGBA page. By default (RGB order, one-pixel box filter) it matches the FreeType backend's LCD output per channel; `LcdSubpixelOrder` and `LcdFilter` (below) select BGR or vertical panels and other filters. Distance fields take precedence over LCD, and color glyphs ignore it, as on FreeType.
  - **Light auto-hinting**: an `AntiAlias = Light` request with `EnableHinting` on (`.bmfc` `antiAlias=light` + `useHinting=1`) is grid-fitted vertically — baseline, x-height, cap-height, ascender and descender blue zones measured from the font's own reference glyphs, horizontal strokes rounded to whole pixels, everything in between interpolated. Advances and horizontal positions are unchanged; TrueType and CFF outlines and color layers are hinted, LCD and distance fields never are. Fonts without Latin reference glyphs or `OS/2` heights render unhinted. Native now lists `None`, `Grayscale`, `Light` and `Lcd` in `SupportedAntiAliasModes`; default (`Grayscale`) output is unchanged.
- **Vector outlines, synthetic width and italic angle** (opt-in; default output of every backend is unchanged): `OutlineMode.Vector` (`.bmfc` `outlineMode=vector`, CLI `--outline-mode vector`, `BmFontBuilder.WithOutlineMode`) has the backend draw the outline layer from the glyph's vector outline through the new `IRasterizer.RasterizeOutlineLayer` (default `null`) — same width, color, corner shape, cells and offsets as the bitmap effect, composited the same way with shadow, gradient, channel routing, supersampling and height stretch, but with a crisp exact-area ring edge and solid narrow counters. `WidthPercent` (`widthPercent`, `--width-percent`) condenses or expands outlines, advances and kerning; `ItalicAngle` (`italicAngle`, `--italic-angle`, default 12°) sets the synthetic italic slant. The Native backend supports all three (`SupportsOutlineStroke`, and the new `SupportsWidthScale` / `SupportsItalicAngle`); other backends keep the bitmap outline and throw `NotSupportedException` for a non-default width or angle. `OutlineMode.Vector` with SDF is rejected; with `ColorFont`, a face with `COLR` or `CBDT`/`sbix` color glyphs keeps the bitmap outline for every glyph, and color bitmap glyphs ignore width and angle as they do bold and italic. FreeType's `SupportsOutlineStroke` is now `false` (it never stroked outlines; output unchanged).
- **Multi-channel signed distance fields**: `SdfMode` (`None`, `Sdf`, `Msdf`, `Mtsdf`) on `FontGeneratorOptions` and `RasterOptions` (with `EffectiveSdfMode`), `BmFontBuilder.WithSdfMode`, the `.bmfc` key `sdfMode=msdf|mtsdf` and CLI `--sdf-mode <none|sdf|msdf|mtsdf>`. MSDF writes RGBA atlases with `alphaChnl=4` and `sdfMode=` in the `.fnt` `kernsmith` line (MTSDF keeps the single-channel distance in alpha) and cannot be combined with effects, a fill color, channel packing or configuration, atlas variants or `ColorFont`; single-channel SDF with `ColorFont` keeps color glyphs in color, as on FreeType. `IRasterizerCapabilities.SupportsMsdf` (default false) gates it; only the Native backend supports it, with edge coloring, per-channel pseudo-distances and artifact correction checked against the true distance field. The existing `bool Sdf` keeps working unchanged.
- **LCD filter and subpixel order**: `LcdFilter` (`Default`, `Light`, `Smooth`, `None`) and `LcdSubpixelOrder` (`Rgb`, `Bgr`, `VRgb`, `VBgr`) on `FontGeneratorOptions` and `RasterOptions`, `BmFontBuilder.WithLcdFilter` / `WithLcdSubpixelOrder`, `.bmfc` keys `lcdFilter=` / `lcdOrder=` and CLI `--lcd-filter` / `--lcd-order`. The Native backend honors them: `Default` is `Light`, the one-pixel box it already used (existing LCD output is unchanged), `Smooth` the five-tap [1,2,3,2,1]/9 kernel, `None` unfiltered; `Bgr` swaps red and blue, and the vertical orders resolve rows in thirds for rotated panels. Other backends ignore them.
- **WOFF2 input.** Core decodes `.woff2` (including WOFF2 font collections, which become a TTC) before any backend sees the bytes, so every backend renders a WOFF2 exactly like the TTF it was made from. Brotli is decoded by KernSmith's own pure managed RFC 7932 decoder rather than `System.IO.Compression.BrotliDecoder`, so WOFF2 also works on browser-wasm, where the BCL decoder has no native library. Malformed, truncated or corrupt WOFF2 input throws `FontParsingException`. The desktop UI's Browse dialogs and drag-and-drop, and the Blazor WASM sample's file picker, accept `.woff2` files.
- **Trying the Native rasterizer from a source checkout** ([TESTING-NATIVE.md](TESTING-NATIVE.md)): the desktop UI lists **Native** in its Rasterizer dropdown (its tooltip marks it experimental), and its anti-alias checkbox and SDF checkbox become an **Anti-Alias** mode combo (None/Grayscale/Light/LCD) and a **Distance Field** combo (None/SDF/MSDF/MTSDF), each listing only what the selected backend supports, plus a color **Palette** index. The rasterizer, anti-alias mode, distance-field mode, palette and variation axes now reach generation and survive project save/load (they were dropped or collapsed before), changing the rasterizer regenerates, the Sample tab decodes SDF/MSDF/MTSDF pages into glyph shapes, and `--ui-rasterizer <backend>` renders the app's own Gum text through `KernSmithFontCreator` on that backend. The MonoGame minimal sample takes `--rasterizer native`.
- The desktop UI exposes the newer options ([TESTING-NATIVE.md](TESTING-NATIVE.md)): **LCD Filter** / **LCD Order** (enabled in LCD mode), **Width %** and **Italic Angle** (Effects > Font Style), outline **Mode** Effect/Vector (Effects > Outline) and **SDF Scale** (Effects > Advanced). Width, angle and Vector are enabled only on a backend that supports them (Native), and Vector only without a distance field; on other backends generation and project save use the defaults instead of throwing. All of them survive project save/load (SDF scale was reset to 1 on save before), and a `.hiero` save lists the ones Hiero cannot store.
- The CLI docs list the process exit codes.
- The Blazor WASM sample offers the Native backend alongside StbTrueType, and `?backend=native&font=<url>` generates on load without a click (for headless checks).
- Contributors: golden masters pin the Native rasterizer's exact output for every committed fixture font across sizes, anti-alias modes, distance fields, styles, variation instances and color, plus `BmFont.Generate` `.fnt` and atlas output (`KERNSMITH_UPDATE_GOLDENS=1` regenerates them locally, refused in CI); `NativeSimilarityMatrixTests` scores Native against FreeType and stb_truetype; a seeded fuzz harness (`NativeFuzzTests`) damages every fixture. BenchmarkDotNet gains `NativeComparison*` (Native vs StbTrueType vs FreeType, plus GDI and DirectWrite in the `net10.0-windows` build), `NativeFeature*` (each Native feature path next to the same work on FreeType and StbTrueType) and `Woff2DecodeBenchmarks`, every benchmark finds its fixture fonts from any working directory, and `benchmarks/KernSmith.ColdStart` measures a fresh process's first and steady-state `Generate` calls under JIT, ReadyToRun and NativeAOT. The `tests/bmfont-compare` harness gains configs for SDF scale 1, MSDF, CFF and CFF2 `.otf`, variable instances, COLR and CBDT color, LCD (BGR, vertical with the smooth filter, super sampled), hinting pairs, WOFF2, vector outlines, condensed width and italic angle.

### Changed

- **The StbTrueType backend is documented as for trusted fonts only.** stb_truetype does no bounds checking of font data, so a malformed font can crash the process instead of throwing; the README, the package README and the package description now say so and recommend the Native or FreeType backend for untrusted fonts. The README's "hardened font parsing" note now names core's parser and the Native backend specifically.
- **The StbTrueType backend now checks a font's structure against the OpenType spec before using it, and rejects non-conforming fonts with `FontParsingException`.** When a font loads, the backend checks the file header and table directory and every table it reads (`head`, `hhea`, `maxp`, `hmtx`, `cmap`, and `loca`/`glyf` or `CFF `); the first time a glyph is measured or rendered, it checks that glyph's outline data (its `glyf` record and components, or its charstring and subroutines), with limits on composite nesting, charstring work and outline points. A glyph bitmap over 16384 px per side fails with `RasterizationException` before it is allocated. A `cmap` entry naming a glyph the font does not have is treated as unmapped, and a font with no Unicode `cmap` now fails with `FontParsingException` instead of a plain `Exception`. Fonts that conform render exactly as before, except that a one-point off-curve contour (some Windows fonts draw their space glyphs this way) no longer makes stb_truetype read past its own buffer, which could give those glyphs a stray box in bold or italic. This is defense in depth, not a guarantee: StbTrueType is still for trusted fonts only.
- KernSmith core now resolves the requested anti-alias mode against the backend's `SupportedAntiAliasModes` before rendering: an unadvertised `Light` or `Lcd` is handed to the backend as `Grayscale` (silently), `None` always passes, and the caller's options are untouched. No built-in backend's output changes — GDI, DirectWrite and StbTrueType already rendered those modes as grayscale, and FreeType supports all four. A custom `IRasterizer` now only receives modes it advertises.
- LCD output is no longer super sampled: with `SuperSampleLevel > 1`, a backend rendering `Lcd` (Native, and the FreeType backend) is given the final size (and `SuperSample = 1`) instead of having its subpixel pattern box-filtered away. Backends that render `Lcd` as grayscale still supersample.
- `KernSmith.Fonts.Web` downloads the `.woff2` source of an `@font-face` rule when the CDN offers one (smaller), falling back to `.woff`; a source's format is read from its `format()` hint, else its file extension. The disk cache names files `.woff2` or `.woff` to match (older `.woff` cache files are still read).
- Native rasterizer performance: a new, independently derived coverage fill (one pooled per-row delta buffer resolved by a prefix sum, no edge copies, sorting or active-edge list) makes grayscale rasterization roughly 15-25% faster; `GetGlyphMetrics` (which `BmFont.QueryAtlasSize` uses) measures simple TrueType glyphs from their declared box without building their outlines, about 3x faster and allocation-free; per-glyph allocations drop about 70%. Grayscale output differs from before by at most one level on under 0.3% of pixels (float rounding of the same exact-area coverage).
- Native rasterizer feature paths are faster with byte-identical output: `None` (dropout control), color bitmap (`CBDT`/`sbix`) scaling and PNG decoding, light hinting, `COLR` v0 metrics, LCD filtering, SDF and MSDF fields, `COLR` v1 compositing, and CFF2 and synthetic-bold metrics. Per glyph, SDF is about 1.9x faster, MSDF 1.55x, `COLR` v1 2.1x, `COLR` v0 metrics 1.5x, color bitmaps and `None` 1.4x and LCD 1.2x. Per-glyph allocation drops about 80% for color bitmap glyphs, CFF2 metrics and bold metrics, and about a quarter for plain grayscale glyphs.
- Core pipeline performance, every backend, output byte-identical: MaxRects packing tests only the new free rectangles after each placement (6-21x fewer containment checks), and an `AutofitTexture` run whose size estimate fits reuses its verification pack instead of packing twice; GPOS pair kerning indexes coverage once and filters to the requested glyphs per glyph rather than per pair; the kerning merge, `name`/`hmtx`/`cmap` parsing and font-info bookkeeping allocate less; the outline effect reads its anti-aliased ring from a table, the compositor copies opaque pixels and tabulates blends onto empty ones, the super-sample downscale sweeps rows once, and the shadow blur computes four outputs per SIMD instruction where the hardware allows (same per-output arithmetic, scalar on browser-wasm). With the Native passes above, `BmFont.Generate` is 1.2-3.9x faster end to end on every backend (Roboto 32px: Native ASCII 1.22 → 0.37 ms and ASCII + Latin-1 3.95 → 1.02 ms; FreeType ASCII 1.65 → 0.74 ms; StbTrueType 1.38 → 0.60 ms; DirectWrite 2.61 → 1.97 ms; GDI 4.31 → 3.60 ms) and allocates 180-270 KB less per call; a fresh process's first call drops too (Native: 128 → 109 ms under JIT, 11.6 → 3.7 ms under NativeAOT).
- **PNG atlas encoding is 3-4x faster and the files 16-42% smaller**, with identical pixels. Pages are now written by KernSmith's own encoder over the runtime's zlib (`System.IO.Compression`) instead of StbImageWriteSharp, which the core package no longer depends on: Roboto 32px ASCII's 256x256 page encodes in 0.50 ms instead of 1.85 ms and Latin-1's in 0.80 ms instead of 3.0 ms, which was most of a `ToFile`/`GetPngData` call. The PNG bytes now come from the runtime's zlib, so the same pixels can encode to different bytes on .NET 8 and .NET 10.
- WOFF2's managed Brotli decoder is about 1.5x faster, bringing it to about 1.5x the BCL native decoder's time. Output is identical.
- `regression_check.py` (the `tests/bmfont-compare` harness) never compares against stale files, reports configs present on only one side as `NEW`/`REMOVED`, and fails when a (config, backend) pair that produced output on the base fails on the branch.
- Build and test tooling bumped: `Microsoft.SourceLink.GitHub` 10.0.403 (the Blazor WASM sample's own override included), `Microsoft.NET.Test.Sdk` 18.11.0, ASP.NET Core WebAssembly 10.0.14 for the Blazor WASM sample, `System.Drawing.Common` 10.0.14 for the bmfont-compare tools, and `actions/setup-java` v6 in CI. The library, rasterizer backends and CLI are unchanged.

### Fixed

- **`BmFontResult.ToFile` always wrote PNG pages**, whatever `TextureFormat` (or `AtlasEncoder`) the font was generated with, while the `.fnt` named `.tga`/`.dds` files — so `TextureFormat = Tga|Dds`, and the CLI's `--texture-format tga|dds` / `.bmfc` `textureFormat=tga|dds`, produced a `.fnt` pointing at files that did not exist. Pages are now encoded with the generation's encoder and named by its extension.
- **The space (and any other blank glyph) kept its super-sampled advance**: with `SuperSampleLevel` 2 or 4 its `xadvance` came out 2× or 4× too wide (Roboto 32: 12 or 24 instead of 6), on every backend. Blank glyphs are now scaled back down like inked ones.
- **Kerning and the table-derived `lineHeight`/`base` ignored `Dpi`.** Glyphs render at `Size × Dpi / 72` on every backend, but kerning amounts (all backends) and the OS/2 line metrics used for FreeType and DirectWrite were computed at 72 DPI, so at `Dpi = 144` kerning was half what it should be and FreeType glyphs overflowed a too-short line. Both now scale with `Dpi`; output at the default 72 is unchanged.
- **A shadow routed to its own channel (`ChannelContent.Shadow`) was also baked into the glyph**, so the Glyph channels carried the glyph plus its shadow and the shadow channel showed a shadow of that, at twice the offset. The routed shadow is now drawn only in its channel.
- **Combined-atlas batches (`BatchAtlasMode.Combined`) with different `Padding` per job** drew every glyph at the largest padding while each job's `.fnt` described its own, so a job with less padding pointed its quads up-left of the ink and cropped it. Each glyph is now drawn at its own job's padding.
- The XML `.fnt` declared `encoding="utf-16"` but is written as UTF-8, so loaders that honour the declaration (`XmlDocument.Load` on the file, XmlSerializer-based engine loaders) rejected it. It now declares `utf-8`. `BmFontReader.Read(byte[])` also skips a UTF-8 byte order mark, which made a BOM-prefixed XML `.fnt` fail as malformed text.
- `.bmfc` files were written in the current culture, so on sv-SE (U+2212 minus) a negative `fontSize` (match char height) or shadow offset could not be read back. They are written in the invariant culture, like the `.fnt`. Saving a `.hiero` config whose character set spans the surrogate block (e.g. `chars=0-65535`) no longer throws.
- CLI `generate --config`: the config's `powerOfTwo` and `dontIncludeKerningPairs` were always overridden by the CLI defaults (power-of-two textures, kerning on), and `--padding`, `--spacing` and `--max-texture` were ignored in favour of the config's values. Config values now stand unless the flag is given, and these flags override the config like the others. `--gradient-angle` and `--axis` now parse decimals in the invariant culture (under de-DE `--axis wght=87.5` read as 875).
- The desktop UI's saved projects lost the packing algorithm and fallback character (both reloaded as defaults), and some shadow opacities reloaded one percent lower (53% as 52%).
- **`lineHeight` (and sometimes `base`) was often one pixel taller than the requested size** (#266). With BMFont cell sizing the size becomes a fractional ppem, so the line box scaled back from it is exactly the size in theory but a hair above it in floating point for about half of all sizes, and the ceiling turned that into `size + 1` (Roboto 32 wrote 33). Line metrics now round up through the new public `RasterizerFontMetrics.CeilingToPixels`, which treats a value within one part per million of a whole pixel as that pixel; a genuinely fractional line box still rounds up so glyphs are not clipped. This changes `lineHeight`/`base` by −1 for the affected font/size pairs on FreeType, DirectWrite, StbTrueType and Native (GDI's metrics are whole pixels already), so text laid out from `lineHeight` tightens by a pixel for them; glyph pixels are unchanged. Native and FreeType, which could disagree with each other here, now agree.
- **Text and XML `.fnt` files depended on the machine's culture.** Numbers were formatted with the current culture, so on sv-SE (and other cultures with U+2212 minus) every negative offset and kerning amount was written with a non-ASCII minus, and on de-DE fractional `kernsmith`-line values (`advanceAdjustY`, `outlineThickness`, `axis_*`) with a decimal comma — files that `BmFontReader` and other BMFont readers on another machine misread (offsets became 0). `BmFontReader` likewise parsed integers in the current culture, failing on `-` under fa-IR or ar-SA. Both now always use the invariant culture; output on invariant/English cultures is unchanged.
- The MonoGame minimal sample drew its atlas as a solid white square: it uploaded straight-alpha `GetRgbaPixelData()` but drew with the premultiplied `BlendState.AlphaBlend`. It now uploads `GetPremultipliedRgbaPixelData()`.
- The published Blazor WASM sample could not generate: trimming removed the StbTrueType assembly, so auto-discovery found no backend. The sample now registers its backends explicitly, and the README's Blazor snippet does the same.
- The StbTrueType backend honours `SdfSpread`: its distance field falls from 128 on the glyph edge to 0 at the spread, with padding to hold it, as the FreeType backend's does, where it reached about 2 pixels whatever the spread. SDF glyphs from StbTrueType change: at the default spread of 8 the field is a quarter as steep and padded by 8 pixels. (#260)
- The StbTrueType backend's bold and italic SDF glyphs are no longer inverted: their distance field is above 128 inside the glyph and below it outside, as the regular path's and the FreeType backend's are, where it was the other way round.
- **The StbTrueType backend's synthetic-bold SDF glyphs had 1-pixel horizontal streaks** (#264): emboldening shifted each contour's start point and the duplicate closing vertex stb_truetype ends it with by different amounts, so the contour no longer closed and rows through the gap read as inside from the glyph's left edge (Roboto 32px: 52 of 94 ASCII glyphs). Coincident vertices now take the same shift, so contours stay closed; bold and bold-italic SDF glyphs lose the streaks, and non-SDF synthetic bold on StbTrueType can change by a few edge pixels where the gap used to be.
- **The StbTrueType backend's bold and italic SDF glyphs read overlapping contours as outside** (#263): its styled SDF path decided inside from the ray-crossing parity (even-odd), so where two contours overlap — fonts that ship them unmerged, such as Roboto Flex, or parts brought together by emboldening — solid ink came out below 128, a hole in the rendered glyph. It now uses the non-zero winding number, the TrueType fill rule and the test stb_truetype's own (regular) SDF uses. Glyphs without overlapping contours are unchanged.
- **GPOS kerning took a class pair's value over the font's specific exception** (#268): where a kerning lookup has a specific pair (PairPos format 1) and a later class subtable (format 2) also covering it, the later value overwrote the earlier one, though shapers (HarfBuzz) apply the first subtable that matches. Source Serif 4 kerned `V` `ä` -80 instead of -60 and `Y` `î` +30 instead of -20; Dancing Script `Ç` `a` -40 instead of -68. Now, within a lookup, the first matching subtable wins (an explicit zero exception included, so it cancels the class kern), and each lookup the `kern` feature lists applies once, with the values of several lookups adding up instead of the last one replacing the rest. This changes the `.fnt` kerning of fonts that have such conflicts on every backend; fonts without them, Arial, Georgia, Roboto's Latin and Noto Sans among them, are unchanged. Contextual positioning lookups are still not read.
- **`BmFont.QueryAtlasSize` under-estimated SDF atlases on FreeType and StbTrueType** (#262): their `GetGlyphMetrics` left out the distance field's padding (the spread on every side), so each estimated cell was `2 x spread` too small per axis — at the default spread, Roboto 32px ASCII came out at about a third of the real glyph area. Both now include it, as Native already did, so SDF metrics match the rasterized glyph box. Generated output is unchanged.
- **Damaged fonts could crash, hang or exhaust memory instead of failing with a KernSmith exception** (found by the new fuzz harness over every Native fixture and the WOFF2 path). Native: out-of-range `HVAR`/`MVAR`/`gvar`/item-variation-store offsets threw `OverflowException`/`ArgumentOutOfRangeException`, and hostile region, delta-row or shared-tuple counts overflowed or allocated gigabytes; a tiny `unitsPerEm`, oversized `COLR` v1 transforms or clip boxes, or a huge `SdfSpread`/DPI made glyphs too large to allocate or took minutes in the distance field — a glyph box over 64 ems, or a bitmap over 16384 px per side, is now a `RasterizationException`; a non-positive or non-finite `Size`, or negative `Dpi`, is an `ArgumentOutOfRangeException`; variation values beyond 16.16 range clamped to the wrong end on .NET 8 and NaN clamped to the minimum (now: nearest end, and NaN keeps the default). Core's font parser: a truncated table directory or TTC offset, `GPOS` subtable offsets past their table, `fvar` axis arrays overflowing `int`, and a `cmap` subtable at the table's last byte threw `ArgumentOutOfRangeException` (now `FontParsingException`, or the damaged optional subtable is skipped); coverage/class ranges ending at glyph 65535 looped forever; a `GPOS` pair-class subtable whose class counts push a record offset past 2^31 threw `ArgumentOutOfRangeException` (now skipped); overlapping `cmap` format 4 segments and format 12 groups starting past Unicode or spanning it repeatedly ran for billions of iterations.
- **Hardened parsing of malformed fonts and files** (security review). Core's GPOS kerning, `fvar` and `cmap` format 12 parsing are bounded so malformed tables can no longer hang the parser or exhaust memory; malformed WOFF 1.0 and WOFF2 input throws `FontParsingException` and no longer over-allocates; font family names are sanitized before they are written into `.fnt` files and default page names; `CharacterSet.FromRanges` (and `.bmfc` `chars=`) clamps ranges to U+0000-U+10FFFF; `BmFontReader.ReadBinary` reports malformed input as `FormatException`; `kernsmith convert` only copies atlas pages that stay inside the input and output directories. Output for well-formed fonts is unchanged.
- **FreeType rendered a scrambled instance for every variable-font request.** `SetVariationAxes` passed the axis coordinates to FreeType as 32-bit integers, but the bundled FreeType's `FT_Fixed` is pointer-sized, so it read them in pairs and pushed every axis to an extreme — even asking for the default values changed the glyphs. Coordinates are now marshalled at the correct width.
- `BmFontBuilder.FromConfig` now carries `SdfSpread` and `SdfScale` from the config; it dropped them, so builder-based generation (including the `tests/bmfont-compare` harness's `sdf.bmfc`) rendered SDF at the default spread 8 and scale 1.
- CLI `--instance <name>` was parsed but ignored, so the font rendered at its default instance. It now applies the named instance's axis values (individual `--axis` values still override them); an unknown name (or any name, on a font without named instances) is a generation error (exit code 4, as for a `--color-palette` the font lacks) that lists the font's instances.
- `kernsmith generate` crashed with "Unhandled exception" and a stack trace when the chosen backend rejected the font or an option (a `RasterizationException` or other `BmFontException`, or `NotSupportedException`, e.g. `--rasterizer stbtruetype --color-font`). It now prints a one-line `Error: Generation error (<backend> rasterizer): <message>` to stderr and exits with code 4.
- A `--color-palette` index the font does not have now exits with code 4 (generation error, naming the backend) on every backend: Native exited with 1 and FreeType crashed with an unhandled `FreeTypeException`; `FreeTypeRasterizer.SelectColorPalette` now throws `ArgumentOutOfRangeException` for it, as Native does. `list-rasterizers` labels `SupportsOutlineStroke` as "Vector Outline", and `generate --help` names every color format `--color-font` covers. Contributors: the Native allocation tests no longer fail intermittently in a full parallel run (a garbage collection inside the measured window was counted as allocation; they now measure in a window no collection paused).
- **Native's mono output (`AntiAlias = None`, `.bmfc` `useSmoothing=0`) erased thin strokes.** It thresholds coverage at 50%, so a stroke under half a pixel wide, or a one-pixel stroke split evenly over two pixels, vanished — Roboto's `l`, `I`, `|`, `.` and `:` came out blank at 6-8px, as did the ExtraLight CFF `L` and `T` of Source Code at 16px. It now applies dropout control after the TrueType specification's smart dropout rule: along every row and column of pixel centres, an inside run that lit no pixel turns on the pixel at its midpoint. Only pixels are added, only where ink was lost; grayscale output is unchanged.
- **Native's mono glyph boxes had empty edge rows and columns.** Thresholding leaves an edge row or column of the outward-rounded box empty wherever the outline covers under half of it — about nine glyphs in ten had one. Mono glyphs, color glyphs included, are now cropped to their ink after rendering: every lit pixel keeps its position relative to the pen, and the `.fnt` `xoffset`/`yoffset`/`width`/`height` shrink to fit.
- **Native placed TrueType glyphs by their `glyf` xMin instead of their `hmtx` left side bearing.** Where the two disagree, a glyph is now positioned by its left side bearing phantom point (pp1 = xMin - lsb), as the TrueType spec and FreeType do; a composite with a `USE_MY_METRICS` component takes that component's phantom point, and a variable font adds the varied `gvar` pp1 delta. Roboto has 52 such glyphs, each off by one font unit (e.g. `&` and `8`); fonts with larger mismatches were off by many pixels.
- **Native advanced a `USE_MY_METRICS` composite by its own `hmtx` entry instead of the component's.** The flagged component's advance now applies (its `HVAR` or phantom-point delta at a variation instance), as the TrueType spec intends and FreeType does. Static fonts in the test fixtures are unaffected; Roboto Flex at `wdth=25,wght=700` had 30 accented/Greek composites advancing differently from FreeType (by up to a few hundred units at other instances), and Dancing Script at `wght=700` two (`ï`, `₫`).
- **Native clipped or shifted TrueType glyphs whose `glyf` header box is stale.** It sized untransformed glyphs from the header box as stored, so a header that no longer contains the outline cut ink off. A header that misses a stored point by more than a unit, and the drawn outline too, is now replaced by the points' control box — the box FreeType always measures. Correct headers are used exactly as before (no golden master or harness output changes).
- **Native's Light hinting moved parts of some glyphs by 2-4 pixels** (`AntiAlias = Light` with `EnableHinting`): hairline curves such as a swash tail or a tilde were fitted as stacked one-pixel strokes, edges up to 0.02 em outside a blue zone (1.4 px at 72 px) were captured by it, and strokes up to 15% off the standard thickness were fitted at the standard width. Dancing Script's `Q` tail dropped 3 px at 8px, Source Serif 4's `Ã` tilde rose 3 px at 8px, its accents sank 2 px at 72px, and Source Code Black's `¤` stretched 4 px down at 72px. Zone capture and standard-width snapping now reach at most half a pixel, and no fitted point moves more than one pixel from the unhinted outline, so hinted ink stays within a row of the unhinted glyph's. Other Light-hinted glyphs can change by a row; Grayscale, mono, LCD and distance-field output is unchanged.
- **Native rounded an advance of exactly half a pixel to even** (`Math.Round`'s default), so Roboto `D` at 16px (1344 × 16 / 2048 = 10.5 px) advanced 10 where FreeType advances 11. An exact half pixel now rounds up, as FreeType's does; other advances are unchanged.
- **Native rejected `Dpi = 0`**, which FreeType reads as 72; Native now does the same (a negative `Dpi` is still an `ArgumentOutOfRangeException`).
- After **Load Project** the desktop UI kept showing the previous values in most of its controls, although the loaded values were the ones generated: the Size, Advance X and gradient Offset/Scale boxes, every slider (outline width, shadow offsets/blur/opacity/kernel/passes, gradient angle, SDF spread, gamma), every color row (fill, outline, shadow, gradient start/end), the Bold/Italic/Synthetic, Hinting, Outline, Shadow, Hard Shadow, Gradient, Cyclic, Channel Packing and Color Font checkboxes, and the Super Sample combo, so a checkbox showing the old state took two clicks to change (the first only re-applied the loaded value). Every control now shows the loaded value, the Browse File / System Font choice follows a loaded font file, and turning on Color Font now also unticks the Gradient checkbox it switches off. Opening a `.ttc` font collection (Browse, drag-and-drop or a project) failed with "Index was outside the bounds of the array"; it now loads, and the Face combo shows a project's face.
- `KernSmith.Fonts.Web` with `WebFontProvider.GoogleFonts` threw `InvalidOperationException` for every font: Google serves only `.woff2` to the browser User-Agent the package sends, and the package only accepted `.woff`.
- `KernSmith.Fonts.Web` could hang on malformed CSS from a CDN: an unclosed `url(` followed by a long run of whitespace made its CSS patterns backtrack catastrophically (20,000 spaces did not finish in 30 s). The patterns now run on .NET's non-backtracking regex engine, so parsing time is linear in the response size; well-formed CSS parses as before.
- `KernSmith.Fonts.Web` in the browser (Blazor WebAssembly) failed with a `NetworkError` in Firefox: the `User-Agent` it set became a non-CORS-safelisted `fetch()` header, and Bunny and Google Fonts reject the resulting preflight. In the browser it now leaves the User-Agent to the browser.
- Documentation: the backend tables, `COMPARISON.md`, `docs/rasterizers/`, the CLI docs, the reference docs and the `RasterizerBackend.Native` XML doc describe the Native backend as it now is; no backend is listed with an "outline stroke" any more (every backend's outline was core's bitmap effect); the backend package READMEs' samples set `Backend`, the property that exists, not `RasterizerBackend`; the CLI README no longer documents a `list-rasterizers --json` flag that does not exist, and its sample `.bmfc` uses the real flat `key=value` format instead of an invented sectioned one; StbTrueType is no longer said to reject CFF (it renders CFF `.otf` fonts and WOFF/WOFF2 wrapping them; only CFF2 is unsupported); FreeType's docs state that it renders `COLR` version 1 glyphs as plain grayscale and that its LCD glyphs are three times as wide.
- The super-sample downscale read past a glyph's own pixels when the glyph was narrower or shorter than `SuperSampleLevel` (a 1-pixel-wide stroke at 2x): its pitch padding or the next row was averaged in as ink, making such glyphs darker. Only the glyph's pixels are averaged now (#302).
- **The super-sample downscale dropped a glyph's last column and row of ink** whenever its super-sampled size was not a multiple of `SuperSampleLevel`, clipping right and bottom edges. The downscaled size now rounds up, as BMFont's does, so super-sampled glyphs can be one pixel wider or taller than before, with that edge ink kept (#294).
- **StbTrueType, GDI and DirectWrite super sampled twice.** Core renders at `Size × SuperSampleLevel` and box-filters down, but also passed `SuperSample = level` to the backend, which multiplied the size again and downscaled on its own — so `SuperSampleLevel = 2` rendered at 4x (16x at level 4) and rounded the bearings twice. The backend now gets `SuperSample = 1` for glyphs and renders once at the level, so super-sampled glyphs from these three backends change (slightly less smoothing, offsets rounded once); GDI now renders at `fontSize × aa`, as BMFont does. FreeType and Native, which ignore `SuperSample`, are unchanged, and so are `lineHeight`/`base` (#293).
- **Effects shrank with super sampling**: the outline, shadow and gradient were applied before the super-sample downscale, so `Outline = 2` at `SuperSampleLevel = 2` drew a 1-pixel outline (and a shadow offset or blur came out halved). Glyphs are now downscaled first, so effect sizes are output pixels at every level, as BMFont's outline is — super-sampled glyphs with an outline or shadow get wider rings, offsets and cells than before. Custom `PostProcessors` likewise now run on the downscaled glyph, and a `OutlineMode.Vector` layer is drawn at `Outline × level` and downscaled with the glyph (#300).

## [0.24.0] - 2026-10-18

### Added

- `FontGeneratorOptions.OutlineShape` (`Round`, `Square`, `Plus`) and `BmFontBuilder.WithOutlineShape`. `Square` fills diagonals (8-neighbour, Chebyshev distance), `Plus` is 4-neighbour (Manhattan distance), `Round` stays the default. Persisted in `.bmfc` as `outlineShape`. Also available as CLI `--outline-shape <round|square|plus>` and as an outline shape selector in the desktop UI (saved with projects). (#242)
- `README.md` has a Discord badge and link.

### Fixed

- Windows system font lookup no longer drops parentheses from family names, so fonts like `Mana Seed Title (monospace)` resolve by their exact name. (#243)

## [0.23.1] - 2026-10-17

### Fixed

- With `AntiAlias = None`, `Outline = N` now gives N fully opaque pixels around the glyph (a hard round threshold) instead of N-1 solid rings plus a 50% alpha ring. The soft ramp is unchanged when anti-aliasing is on. (#239)

## [0.23.0] - 2026-10-14

### Added

- Platform smoke test (`tests/KernSmith.PlatformSmoke`): one app that generates fonts with the StbTrueType, FreeType and Native backends on desktop, Android, iOS and Mac Catalyst, run in CI by the new `mobile-smoke.yml` workflow (desktop on Windows/Linux/macOS, Android emulator, iOS simulator, Mac Catalyst). iOS (simulator) and Mac Catalyst are verified for StbTrueType and Native. iOS apps must not reference `KernSmith.Rasterizers.FreeType`: FreeTypeSharp's static iOS library references libpng, so the app fails to link.
- `README.md` has a Platform Support table (platform × backend, marking what is verified versus expected) with mobile notes: register backends explicitly in trimmed/AOT apps, and load font bytes since there are no system fonts on mobile. Backend READMEs and docs list iOS as verified for StbTrueType and Native.

### Changed

- Platform docs corrected: the FreeType Android x86_64 fix is stated as 0.23.0 or later, Native and StbTrueType list their verified platforms, Blazor WASM is described as built in CI rather than verified, and the FreeType package description names its supported platforms.

### Fixed

- `KernSmith.Rasterizers.FreeType` failed on Android x86_64 with `DllNotFoundException` (`libm.so.6` not found): NuGet's RID fallback (android-x64 -> linux-x64) packaged FreeTypeSharp's desktop glibc `libfreetype.so` into the APK over the Android build. The package now ships `build/`/`buildTransitive/` targets that drop FreeTypeSharp's desktop native libraries from Android builds, which also removes the only library that failed Android 15+'s 16KB page-size alignment check (XA0141).

## [0.22.1] - 2026-10-12

### Changed

- Desktop UI dependencies bumped: `MonoGame.Framework.DesktopGL` 3.8.6 and the Gum family in lockstep to 2026.10.1.1 (`Gum.MonoGame`, `Gum.Themes.Editor.MonoGame`, `KernSmith.GumCommon`, `KernSmith.MonoGameGum`), with the UI's resulting nullable warnings fixed. The library, rasterizer backends and CLI are unchanged.
- Test and docs tooling bumped: `xunit.v3` 4.0.2, `xunit.runner.visualstudio` 4.0.1, `Microsoft.NET.Test.Sdk` 18.10.1, `coverlet.collector` 10.1.0, `docfx` 2.81.0.
- The docs site links the KernSmith Discord server.

### Fixed

- `THIRD-PARTY-NOTICES.md` versions updated for the UI dependency bumps (`MonoGame.Framework.DesktopGL` 3.8.6 with `MonoGame.Library.OpenAL` 1.24.3.4, Gum 2026.10.1.1), and the stale note about unreferenced `MonoGame.Extended`/`NativeFileDialogSharp` packages is removed.
- `COMPARISON.md`: the rasterizer Feature Support table is contiguous again (the WOFF note had split it mid-table, breaking the rows from Hinting down), and the Rasterization table lists the experimental Native backend.
- The CLI README documents the `--max-texture-size` alias of `--max-texture`.
- `FreeTypeRasterizer`'s public interface members carry `<inheritdoc />` like the other backends.
- The Native rasterizer's package tags no longer advertise `sdf`; it does not support SDF rendering.

## [0.22.0] - 2026-09-09

### Changed

- Dependencies bumped to the latest stable releases (#222): the Gum family in lockstep to 2026.9.2.1 (`Gum.MonoGame`, `Gum.Themes.Editor.MonoGame`, `KernSmith.GumCommon`, `KernSmith.MonoGameGum`), `xunit.v3` and `xunit.runner.visualstudio` 4.0.0, `Microsoft.NET.Test.Sdk` 18.10.0, `Microsoft.SourceLink.GitHub` 10.0.401, `MonoGame.Framework.DesktopGL` 3.8.5.1, `StbImageSharp` 2.30.16, `StbTrueTypeSharp` 1.26.13. The Blazor WASM sample moves to ASP.NET Core WebAssembly 10.0.12 and the bmfont-compare tools to `System.Drawing.Common` 10.0.12. Generated font output is unchanged (verified with the `tests/bmfont-compare` harness across every backend).
- `dotnet test` now runs in Microsoft.Testing.Platform mode (`global.json` `test.runner`), which xunit.v3 4.x requires on the .NET 10 SDK. Contributors invoke a project as `dotnet test --project <csproj>`; CI and the publish workflow use `--report-xunit-trx` in place of the VSTest `--logger trx` / `--blame-hang` flags.
- The desktop UI uses Gum's `AbsoluteWidth`/`AbsoluteHeight` in place of the now-obsolete `GetAbsoluteWidth()`/`GetAbsoluteHeight()`.
- Dependabot also scans the Blazor WASM sample and the bmfont-compare tool projects, which opt out of Central Package Management and were previously unmonitored.

### Fixed

- Documentation claimed the GDI, StbTrueType and Native backends reject WOFF fonts. They do not: WOFF1 is decompressed to sfnt by KernSmith core before any rasterizer sees the bytes, so every backend accepts a WOFF whose outlines it otherwise supports. The backend capability tables in `README.md`, `COMPARISON.md`, `docs/rasterizers/index.md`, the Native rasterizer README, `reference/REF-12-rasterizer-backends.md` and the `RasterizerBackend.Native` XML doc are corrected, and `WoffAllBackendsTests` now locks the behavior in across every backend. WOFF2 remains unsupported.
- `THIRD-PARTY-NOTICES.md` package versions, several stale "verified against KernSmith `<version>`" markers in `docs/` and `reference/`, the UI README dependency list, and the core NuGet package description are brought back in line with the code.

## [0.21.0] - 2026-08-28

### Added

- **`BmFontIncrementalSession` — add glyphs to an existing atlas at runtime without moving anything already placed** (phase 183, #208). `BmFont.BeginIncremental(fontData, options, overflowPolicy)` starts an empty session (the first `AddGlyphs` sizes the atlas exactly as `Generate` would); `BmFont.ResumeIncremental(fontData, options, existingModel, overflowPolicy)` recovers occupancy, character list and kerning from an existing `BmFontModel`/.fnt. `AddGlyphs(string | IEnumerable<int>)` returns a `GlyphAdditionResult`: per-glyph `AddedGlyph` with a reserved placement, the zero-copy `RawPixels` backend buffer, a lazy tightly-packed RGBA32 `Pixels` promotion ready for GPU upload (blit at `(X + Padding.Left, Y + Padding.Up)`), and a lazy premultiplied `PremultipliedPixels` view for premultiplied-alpha pipelines; plus the kerning-pair delta, failed/already-present codepoints, and current page geometry. The font is parsed once unfiltered, the rasterizer stays loaded, and the MaxRects free-rect state is held live, so a per-add costs work proportional to the glyphs added. No-fit behavior is caller-chosen via `AdditionOverflowPolicy`: `Grow` (POT-double the smaller page dimension up to `MaxTextureWidth/Height`, default), `NewPage`, or `Throw`. `CurrentModel` materializes a full `BmFontModel` that round-trips through the standard formatters. Not supported in a session (throws at Begin/Resume): `Variants`, `ChannelPacking`, `TargetRegion`, `CustomGlyphs`; a Skyline packing configuration falls back to MaxRects for additions. Usage, the blit contract, overflow policies and v1 limits are documented in `docs/core/incremental-glyph-addition.md`. Measured (BenchmarkDotNet, Arial ASCII @32px): a 1-glyph add into a warm ~95-char session is ~53x cheaper than a full regenerate (~74 us vs ~3.9 ms) and allocates ~2.6 KB vs ~1.8 MB.
- Incremental session review hardening: `AddGlyphs` batches are transactional (a mid-batch overflow rolls the session back), page growth exposes free space spanning the old page edge, and `ResumeIncremental` also validates size/outline.

### Changed

- The Native rasterizer's intentionally-exact horizontal-edge float comparisons now use `float.Equals` (bitwise-identical to `==`) so CodeQL's `cs/equality-on-floats` no longer flags them; output is unchanged.

## [0.20.0] - 2026-08-05

### Added

- `FontInfo.DesignMetrics` — per-glyph advance width and left side bearing, in unscaled font design units, keyed by codepoint. Backed by a new `hmtx` table parser in core. Lets consumers measure text width (e.g. word-wrap) without rasterizing, and avoids the small cross-size jitter a hinted, rasterized advance width can show. (#205)
- **The Native rasterizer is now selectable from the CLI**: `--rasterizer native`, and `list-rasterizers` reports it as available. The CLI project-references `KernSmith.Rasterizers.Native`, so auto-discovery registers it. (#146)
- `docs/rasterizers/native.md` documents the backend, and it now appears in the backend/capability tables in `README.md`, `COMPARISON.md`, `docs/index.md`, `docs/rasterizers/index.md` and the CLI README. Every entry states the same limits: **TrueType (`glyf`) outlines only** (a CFF/OTF font is rejected with a `RasterizationException`), no WOFF, hinting, SDF, outline stroke, synthetic bold/italic, variable fonts, color fonts or system fonts, and `None`/`Grayscale` anti-aliasing only. The package is still unpublished, so `RasterizerBackend.Native` resolves from a source build or the CLI, not from the released NuGet packages.

### Fixed

- **Synthetic bold/italic no longer silently vanishes on a backend that cannot apply it.** The core skipped a caller-supplied `BoldPostProcessor`/`ItalicPostProcessor` whenever `Bold`/`Italic` was set, assuming the rasterizer had already done the work — without ever checking `IRasterizerCapabilities.SupportsSyntheticBold`/`SupportsSyntheticItalic`. On Native, the first backend to report `false`, nobody applied the transform, so asking for bold produced *thinner* glyphs than not asking, with no error. The skip now requires the capability. The four shipped backends all report `true`, so their output is byte-identical.
- **A malformed `cmap` format 12 subtable is rejected instead of exhausting memory.** `numGroups` was read from the font and used to size three arrays with no bounds check, so a hostile or corrupt file escaped as `OverflowException`/`OutOfMemoryException` rather than the `FontFormatException` every other parse path raises. It is now validated against the bytes actually present before anything is allocated.
- **A composite glyph can no longer hang the native parser.** The `maxComponentDepth` limit bounds recursion *depth* but not total work: a chain where each glyph has two components pointing at the next never trips the depth guard yet expands to 2^depth nodes, so a table of a few dozen 22-byte glyphs pinned a core indefinitely. Glyph expansion is now also capped by a total component budget, orders of magnitude above any real composite.

### Changed

- `RasterizerBackend.Native`'s XML doc no longer says the backend is unusable — it now describes a working but experimental TrueType-only rasterizer, and enumerates what it does not support. (#146)

## [0.19.0] - 2026-07-30

### Fixed

- **Desktop UI no longer discards per-channel content routing.** A `.bmfc` loaded with `alphaChnl`/`redChnl`/... lost its routing on both generate and save, because `GenerationService` never assigned `Channels` and `ProjectService.BuildOptions` never persisted it. The config is now carried through load -> generate -> save unmodified. (The UI still has no per-channel editor; this is preservation, not editing.)
- **UI no longer conflates channel *content* with channel *packing*.** `ProjectService` set `ChannelPackingEnabled` from `options.Channels != null`, so loading a config with channel routing silently ticked the unrelated "Channel Packing" checkbox -- while `options.ChannelPacking` itself was never read, meaning a genuine `fourChnlPacked=1` did *not* tick it. Both directions are fixed, and `ChannelPacking` is now persisted on save.
- `BmfcConfigWriter` now writes the per-channel `.bmfc` keys (`alphaChnl`, `redChnl`, `greenChnl`, `blueChnl`, `invA`, `invR`, `invG`, `invB`), which `BmfcConfigReader` has always parsed. A `ChannelConfig` previously survived a read but was silently dropped on write, so a load/save round trip lost the channel routing. Nothing is emitted for an unset or all-default configuration, so `.bmfc` output stays byte-identical for fonts that do not use channel routing; a channel-configured font gains the eight keys.
- **`.bmfc` `aa` key no longer corrupts the anti-alias mode.** The writer emitted the AA mode into `aa` (`2` for `AntiAliasMode.Light`, otherwise `1`), but `aa` is BMFont's *supersampling factor* -- which is how `BmfcConfigReader` and BMFont.exe both read it. Generating with `--anti-alias light` and re-loading the emitted config silently produced `SuperSampleLevel = 2` with the mode downgraded to `Grayscale`, changing pixel output. `aa` now carries `SuperSampleLevel`, and `Light`/`Lcd` round-trip through a new `antiAlias` extension key (`None` is still conveyed by `useSmoothing=0`, `Grayscale` is the default). The now-redundant `superSample` extension is no longer written; it is still read, so configs written by earlier versions keep working.
- **Desktop UI tooltips no longer state the opposite of what the backends support.** Four tooltips claimed DirectWrite handled color and variable fonts -- `DirectWriteCapabilities` reports `false` for both and FreeType is the only backend supporting them -- and the SDF tooltip claimed FreeType-only when StbTrueType supports it too. The tooltips contradicted the enable/disable state of the very controls they described, since that logic reads the real `IRasterizerCapabilities`. The backend dropdown tooltip also omitted StbTrueType, which is in the dropdown.
- Documentation corrections: the `docs/rasterizers/` code samples set a nonexistent `RasterizerBackend` property instead of `FontGeneratorOptions.Backend` (10 samples across 5 pages, none of which compiled); GDI was listed as "System fonts only" for font formats despite loading TTF/OTF bytes; `benchmark-fonts` was missing from three command lists; and `RasterizerBackend.Native` was named in `docs/rasterizers/custom-backend.md`. `reference/REF-12` was corrected on FreeType packaging (a separate package, not built into core), backend pre-registration (nothing is pre-registered), and Native's publish status.
- `.bmfc` now persists `ShadowOpacity`, `SdfScale`, `HardShadow`, and `VariationAxes`, which were silently dropped on write despite `.hiero` persisting several of them. `HardShadow` also feeds the `variantShadow` round trip -- `BmfcConfigReader` rebuilds the shadow `AtlasVariant` from it -- so losing it degraded the variant too. All four emit nothing at their defaults.

### Changed

- **`RasterizerBackend.Native` now states that it is unfinished instead of advertising itself as a shipped backend.** Its XML doc described a working pure-C# rasterizer, and `RasterizerFactory.Create(RasterizerBackend.Native)` failed with the generic "install the corresponding NuGet package" advice -- but no such package is published, so the value type-checked, compiled, and then always threw with no way to tell why. The doc now says it is under development and points at the released backends, and the factory gives `Native` its own message. The enum value is retained so the API does not change shape when the backend ships. (#146)
- `KernSmith.Rasterizers.Native` is no longer packable. The publish workflow packs backends by explicit path so it was never released, but a solution-wide `dotnet pack` would have produced a package whose rendering methods throw.
- CI now runs `KernSmith.Rasterizers.Native.Tests` (65 tests that were built but never executed on CI) and the new `KernSmith.Ui.Tests`.

## [0.18.2] - 2026-07-28

### Added

- `BmFontResult.GetVariantFntText` — public serialization of a variant `BmFontModel` (e.g. a shadow `AtlasVariant`) to BMFont `.fnt` text. (#184)

## [0.18.1] - 2026-07-28

### Fixed

- `ShadowCoveragePostProcessor` now bakes the `AtlasVariantKind.ShadowSilhouette` glyph as white RGB with coverage in alpha, instead of black, so consumers can tint the shadow at draw time. (#175)

## [0.18.0] - 2026-07-27

### Added

- `AtlasVariant` — a shadow silhouette variant packed alongside the primary font glyphs, sharing atlas layout via a new `AtlasGroupBuilder` mechanism (general-purpose, not dropshadow-specific). (#172, #174)
- `ChannelContent.Shadow` for routing isolated shadow-coverage into its own atlas channel. (#167)

### Fixed

- `ShouldApplyChannelConfig` gate now honors `Channels` configuration when effects are active, instead of skipping channel routing. (#169)

## [0.17.0] - 2026-07-18

### Added

- `KernSmith.Fonts.Web` is now published to NuGet. It provides `WebFontSource`, an `IFontSource` implementation that fetches WOFF fonts from CSS-based font CDNs (Bunny Fonts, Google Fonts) by parsing `@font-face` responses — useful in browser/WASM scenarios (e.g. Blazor WebAssembly) where there is no filesystem.

### Changed

- Bumped `MonoGame.Framework.DesktopGL` to 3.8.5 and the Gum packages (`Gum.MonoGame`, `Gum.Themes.Editor.MonoGame`) to 2026.7.6.1.

### Removed

- The four Gum integration packages — `KernSmith.GumCommon`, `KernSmith.KniGum`, `KernSmith.MonoGameGum`, and `KernSmith.FnaGum` — are no longer built or published from this repository. They are now maintained and published from the upstream Gum repository (https://github.com/vchelaru/Gum) and consumed from NuGet.

### Fixed

- Corrected the rasterizer backend capability tables in `COMPARISON.md` to match the code: color and variable fonts are supported by FreeType (not DirectWrite, which stubs both), StbTrueType supports synthetic bold/italic, and DirectWrite antialiasing is ClearType-hinted grayscale rather than subpixel.

## [0.16.0] - 2026-07-12

### Added

- `BmFont.HintFontLocation(familyName, path)` — lets a consumer pre-populate the system font resolver's cache with a known file path for a family name, as a lighter-weight alternative to `RegisterFont` when the font doesn't need to be loaded into memory up front. Validated the same way as any other cache/seed entry (file exists, parsed family matches) before being trusted. (#152)
- [System Font Resolution](docs/core/system-font-resolution.md) docfx guide — documents the four-tier fallback chain, the built-in seed table, the heuristic filename-match tier, and what each diagnostic message means, with a focus on macOS (the one platform that regularly exercises every tier). (#155)

### Changed

- `DefaultSystemFontProvider` now logs a `Trace.TraceInformation` message at each font-resolution tier miss (invalid cache/hint/seed entry, no filename-narrowed heuristic candidates, heuristic candidates failing verification, and full directory scan fallback), so a consumer can identify which family names are paying for the expensive resolution tiers without profiling. (#152)

## [0.15.3] - 2026-07-12

### Fixed

- System font directory scans (`GetInstalledFonts()`, `GenerateFromSystem()`'s scan fallback) no longer parse GPOS kerning, `kern`, `fvar`, and color-glyph tables per font face when only the family/style name is needed. This cut a real-world 699-font directory scan from ~2.7-3.0s to ~600-650ms in local testing. (#139)
- `fc-list` (used for fast font discovery on Linux/macOS) failing to launch — most commonly on stock macOS, which doesn't ship fontconfig — is now logged via `Trace.TraceInformation` instead of silently falling back with no visibility. (#139)

## [0.15.2] - 2026-06-28

### Fixed

- Outline effect now draws the inner ring on enclosed glyph counters (e.g. `a`, `d`, `e`, `o`, `g`, `b`, `p`, `q`, `0`, `6`, `8`, `9`). The layered outline pipeline previously skipped all counter (hole) pixels, so interior outlines were missing — most visibly when combined with a drop shadow, where the shadow showed through the unoutlined hole. (#126)
- CLI now honors per-channel content declared in `.bmfc` config files (`alphaChnl`/`redChnl`/`greenChnl`/`blueChnl`), gated so configs without channel routing keep their baked effects unchanged. Previously the CLI `.bmfc` path silently ignored channel configuration that the UI/Gum path already respected.

## [0.15.1] - 2026-06-15

### Fixed

- Custom `FontGeneratorOptions.Channels` (`ChannelConfig`) that routed outline content into a channel spilled glyphs across multiple atlas pages where the default channel config fit on a single page, and `AutofitTexture` failed to consolidate the result. Atlas size estimation, the `AutofitTexture` verification pack, and the final pack now all account for the outline-expanded glyph dimensions, so the result stays on a single page when it fits. `Spacing` is now also honored on the custom-channel path. (#115)

## [0.15.0] - 2026-06-06

### Added

- Advanced effect, fill, and rendering options exposed across the core library, CLI, and desktop UI (phase 100):
  - Fill color to tint the base glyph body (`FillColorR`/`G`/`B`/`A`; CLI `--fill-color`)
  - Two-parameter shadow blur control (`ShadowBlurKernelSize`, `ShadowBlurPasses`; CLI `--shadow-blur-kernel`, `--shadow-blur-passes`)
  - Extended gradient controls — offset, scale, and cyclic tiling (`GradientOffset`, `GradientScale`, `GradientCyclic`; CLI `--gradient-offset`, `--gradient-scale`, `--gradient-cyclic`)
  - FreeType gamma correction (`Gamma`; CLI `--gamma`)
  - SDF spread and scale controls (`SdfSpread`, `SdfScale`; CLI `--sdf-spread`)
  - Global per-glyph horizontal advance adjustment (`AdvanceAdjustX`; CLI `--advance-x`)
  - All new options round-trip through `.bmfc` and `.hiero` config files
- API reference documentation under `docs/api-reference/` covering `BmFont`, the fluent builder, `FontGeneratorOptions`, `BmFontResult`, `BmFontModel`, and exception types

### Changed

- CI now also builds and tests against .NET 8.0 (in addition to .NET 10.0)

## [0.14.0] - 2026-06-03

### Added

- Hiero `.hiero` (libGDX) configuration file format support (read and write) — `HieroConfigReader` and `HieroConfigWriter` (`KernSmith` namespace)
- `ConfigFormatFactory.ReadConfig()` auto-detects config format (`.bmfc` / `.hiero`) by inspecting file content (extension used only when content is inconclusive); `ConfigFormatFactory.WriteConfig()` selects format by file extension
- `BmFont.FromConfig()` and `BmFontBuilder.FromConfig()` now auto-detect `.bmfc` and `.hiero` configs by inspecting file content (extension used only when content is inconclusive)
- Config-format auto-detection treats any non-`.hiero` extension (including no extension) as BMFont `.bmfc`, preserving backward compatibility (lenient `ConfigFormatFactory` behavior)
- `BmFontResult.ToHiero()` — export an equivalent Hiero `.hiero` config string (mirrors `ToBmfc()`)
- CLI: `--config` and `--save-config` accept `.hiero` files; `init -o foo.hiero` writes a `.hiero` config (no extension defaults to `.bmfc`; an explicit extension — even an unusual one like `.cfg` — is now kept verbatim instead of being suffixed with `.bmfc`, and BMFont content is written for any non-`.hiero` extension); `batch` processes mixed `.bmfc` and `.hiero` globs
- UI: Load/Save project dialogs and drag-and-drop accept `.hiero` files, with a warning when saving `.hiero` with settings it cannot represent (channel packing, variable axes, super sampling, color font)
- `AtlasPage.GetRgbaPixelData()` — returns RGBA32 pixel bytes (grayscale pages expand to `255,255,255,v` alpha-coverage layout) for straight-alpha GPU upload
- `AtlasPage.GetAlpha8PixelData()` — returns 8-bit alpha-coverage bytes (one byte per pixel) for single-channel/coverage textures and custom shaders
- `AtlasPage.GetPremultipliedRgbaPixelData()` — returns premultiplied RGBA32 bytes for premultiplied-alpha blend pipelines (e.g. MonoGame's default `BlendState.AlphaBlend`)
- `THIRD-PARTY-NOTICES.md` with third-party license attribution (FreeType FTL credit, MonoGame Ms-PL, SDL2/Zlib, OpenAL Soft LGPL, Gum/KNI, etc.); packaged into every NuGet package and shipped alongside the CLI and UI binaries
- Attribution for the FreeType-derived embolden (synthetic-bold) port in the StbTrueType backend — source header, package README, and `THIRD-PARTY-NOTICES.md`

### Changed

- Core `KernSmith` library is now Native AOT– and trimming-compatible (`IsAotCompatible` plus trim/AOT analyzers enabled)
- Assembly version is now read via a compile-time constant instead of runtime assembly reflection
- Under AOT/trimming, a rasterizer backend must be registered explicitly because auto-discovery is reflection-based — StbTrueType recommended. See [Native AOT and Trimming](docs/rasterizers/index.md#native-aot-and-trimming)

## [0.13.0] - 2026-05-04

### Changed

- **Breaking:** `FontGeneratorOptions.Size`, `RasterOptions.Size`, and `BmFontModel.Info.Size` (`InfoBlock.Size`) are now `float` (default `32f`) -- previously `int`. Existing code passing integer literals (e.g. `Size = 32`) continues to compile via implicit widening
- **Breaking:** `BmFontBuilder.WithSize(float)` -- previously `WithSize(int)`. Existing integer call sites still compile
- BMFont on-disk formats (text/XML/binary `.fnt`, `.bmfc`) still store integer size; formatters round fractional sizes at the write boundary using `Math.Round` (banker's rounding -- `10.5` rounds to `10`)
- FreeType, StbTrueType, and DirectWrite rasterizers honor fractional sizes natively. The GDI rasterizer rounds to the nearest integer because Win32 `LOGFONTW.lfHeight` is integer-only
- The CLI `--size` flag now accepts fractional values, e.g. `--size 10.5`. Use `.` as the decimal separator regardless of system locale -- `--size 10,5` is rejected

## [0.12.4] - 2026-04-10

### Added

- Version bump and documentation updates

## [0.12.3] - 2026-04-05

### Added

- Rasterizer auto-discovery — `RasterizerFactory` now auto-discovers built-in backends via `Type.GetType()` on first call to `Create()`, `GetAvailableBackends()`, or `IsRegistered()`. No manual `RuntimeHelpers` registration needed
- ILLink trimmer protection (`ILLink.Descriptors.xml`) for all 4 rasterizer packages, enabling correct behavior under AOT/trimming

### Removed

- Manual `RuntimeHelpers.RunModuleConstructor()` workarounds from 13 files (UI, CLI, GumCommon, Blazor WASM sample, 9 test files)
- Redundant `[DynamicDependency]` attribute from `StbTrueTypeRegistration` (replaced by `ILLink.Descriptors.xml`)
- `TestAssemblyInitializer.cs` (auto-discovery handles test rasterizer registration)

### Fixed

- Race condition in concurrent `RasterizerFactory.Create()` calls during discovery — replaced `Interlocked.CompareExchange` with double-checked locking so concurrent callers wait for discovery to complete

## [0.12.2] - 2026-04-03

### Fixed

- Rasterizer error message now says "No backends have been registered." instead of showing a confusing empty list ("Available backends: .")
- Gum integration packages (MonoGameGum, KniGum, FnaGum) now include FreeType backend automatically — users no longer need to manually reference KernSmith.Rasterizers.FreeType

## [0.12.1] - 2026-04-02

### Fixed

- StbTrueType synthetic bold collapsing counters (hollow sections in A, B, 4, e, @) — rewrote `EmboldenContour` to faithfully port FreeType's `FT_Outline_EmboldenXY` algorithm: halved strength with uniform offset, unnormalized bisector for natural damping at sharp angles, and min-segment/sin clamping to prevent thin feature collapse

## [0.12.0] - 2026-04-01

### Added

- `KernSmithFontCreator` now accepts an optional `RasterizerBackend` in the constructor, enabling Blazor WASM and other platforms to use StbTrueType without reimplementing the class
- `GumFontGenerator.Generate()` now accepts an optional `RasterizerBackend` parameter to override the default backend
- Synthetic bold/italic bitmap post-processors (`BoldPostProcessor`, `ItalicPostProcessor`) for any rasterizer backend
- Outline-level synthetic bold/italic for StbTrueType rasterizer via stb_truetype shape API
- Pixel-diff comparison tool for regression detection (`tests/bmfont-compare/diff_comparisons.py`)
- FontStashSharp technique insights distilled into native rasterizer phases 160-180

### Fixed

- SDF `GetFontMetrics` inconsistency — `aa` calculation now matches `RasterizeGlyph`/`GetGlyphMetrics` when SDF + SuperSample are both set
- `ItalicPostProcessor` now adjusts advance width to prevent glyph overlap
- Double-bold/italic guard when both `options.Bold` and `BoldPostProcessor` are active
- WOFF decompressor: destination bounds check and integer overflow on offset+length
- Cmap Format 12: entry limit (200k) and numGroups overflow guard to prevent resource exhaustion
- Table directory offset/length validation against file bounds
- GPOS parser: slice bounds checks on untrusted offsets
- Kern table: infinite loop guard when subtableLength == 0
- CLI: sanitize font family name in output path to prevent path traversal

### Changed

- BoldPostProcessor kernel falloff precomputed once per glyph, eliminating per-pixel `MathF.Sqrt` calls
- Generation pipeline consolidated from 6 `.Select().ToList()` chains into a single for-loop
- UI preview: raw pixel data uploaded directly to GPU via `Texture2D.SetData()`, eliminating PNG encode/decode round-trip (~800ms savings)
- UI effects toggles regenerate immediately (removed 300ms debounce); remaining debounce reduced to 10ms
- UI texture caching: atlas textures decoded once per generation, not per page/tab switch
- SDF no longer auto-disables when outline/shadow/gradient are enabled; shows informational warning instead

## [0.11.0] - 2026-04-01

### Added

- StbTrueType rasterizer backend -- pure C#, cross-platform, no native dependencies. Supports SDF rendering. Ideal for Blazor WASM, iOS AOT, and serverless
- Blazor WASM sample and validation — client-side font generation with StbTrueType backend, AOT compilation support, CI publish gate, native dependency scanning tests

### Fixed

- FreeType rasterizer not appearing in UI dropdown — missing assembly force-load after extraction to separate package
- Test suite race condition — `ResetForTesting()` could clear rasterizer registry while integration tests ran in parallel; added `[Collection("RasterizerFactory")]` to all rasterizer-dependent test classes
- CLI and UI README docs now list StbTrueType as an available rasterizer backend

## [0.10.4] - 2026-03-31

### Added

- Version bump and documentation updates

## [0.10.3] - 2026-03-30

### Added

- Rasterizer plugin template and sample project for custom backends
- Synthetic bold/italic CLI flags (`--synthetic-bold`, `--synthetic-italic`) and UI controls
- Guard against double bold/italic when font file is already styled
- DirectWrite synthetic bold/italic support
- Font registration API: `BmFont.RegisterFont()` for registering raw font data by family name, enabling `GenerateFromSystem()` on platforms without system font access (Blazor WASM, mobile, containers)
- `BmFont.UnregisterFont()` and `BmFont.ClearRegisteredFonts()` for managing registrations
- Registered fonts take priority over system fonts with automatic fallback
- Documentation pass: updated tooltips, READMEs, docfx docs, CHANGELOG, reference docs, and NuGet descriptions

### Fixed

- Space character now gets a transparent atlas entry when outline > 0, matching BMFont behavior
- Known limitations deferred to a later release: color fonts, variable fonts, channel-based outlines, and GDI `MatchCharHeight`

## [0.10.2] - 2026-03-28

### Changed

- Restructure docs: per-package integration pages, per-backend rasterizer pages
- Add sidebar navigation and top-nav dropdown for docs sections
- Fix Docs nav link to go to docs index instead of Core Library
- Add toc.yml and index.md to docs workflow path triggers

## [0.10.1] - 2026-03-28

### Fixed

- System font dropdown now always visible regardless of rasterizer backend
- Fast font discovery: Windows registry scan (~25ms), fc-list on Linux/macOS, directory scan fallback
- UI-thread marshaling for font list population

## [0.10.0] - 2026-03-28

### Added

- CLI `--rasterizer` flag to select backend (freetype, gdi, directwrite)
- CLI `list-rasterizers` command showing available backends and capabilities
- UI rasterizer dropdown with capability-aware option disabling
- Multi-package NuGet publishing — all packages (core, rasterizers, integrations) publish from a single workflow
- Split publish workflow into parallel ubuntu/windows pack jobs for faster CI
- NuGet metadata (readme, tags, authors, URL) for rasterizer packages
- Updated RELEASING.md with multi-package architecture docs
- Comprehensive UI guide for docs site (layout, workflows, shortcuts, all features)
- Docs for `list-rasterizers` CLI command
- Integrations and Rasterizers sections on docs landing page

### Fixed

- Docs site logo now links to site root from any page
- Docs sidebar shows all sections instead of hiding behind section scoping
- CLI tests use `dotnet exec` instead of `dotnet run` — fixes net10.0 test host hang and speeds up tests ~20x

### Changed

- Directory.Build.props defaults IsPackable=false; each package opts in explicitly
- Package READMEs use absolute GitHub URL instead of relative file path
- KniGum uses TargetFrameworks (plural) to fix pack failure
- FnaGum excluded from packing until build issues resolved

## [0.9.6] - 2026-03-28

### Added

- Pluggable rasterizer architecture — IRasterizer, IRasterizerCapabilities, RasterizerFactory, RasterizerBackend enum
- GDI rasterizer backend for BMFont pixel-perfect parity via Win32 P/Invoke (Windows-only)
- GDI parity fixes — TEXTMETRIC metrics, GetKerningPairs kerning, LoadSystemFont support
- DirectWrite rasterizer backend with sizing fixes
- Font sizing and DPI parity verification across all backends
- Super-sampling support across all rasterizer backends
- README for bmfont comparison tools

### Changed

- Channel settings added to the bundled fire comparison config for BMFont64 outline separation

## [0.9.5] - 2026-03-23

### Fixed

- Outline counter-fill: EDT outlines no longer expand into letter counters (holes in e/o/a/H) — uses BFS flood-fill to distinguish exterior vs counter pixels
- Synthetic bold counter bloat: replaced FT_GlyphSlot_Embolden with lighter FT_Outline_Embolden (ppem/36) to preserve counters at large sizes

### Changed

- Renamed reference/gum-forms-cheatsheet.md to REF-07-gum-forms-cheatsheet.md
- Updated REF-09 with synthetic bold/italic/outline rendering findings
- Moved completed plans (phase 76, 76B, docfx) to plan/done/

## [0.9.4] - 2026-03-22

### Fixed

- Publish workflow: add `-f net10.0` to `dotnet publish` for CLI and UI binaries (multi-target projects require explicit framework for self-contained publish)

## [0.9.3] - 2026-03-22

### Added

- GitHub Release workflow with CLI + UI binaries for 5 platforms (win-x64, win-arm64, linux-x64, osx-arm64, osx-x64)
- Manual dispatch trigger for publish workflow (Actions → Run workflow)
- RELEASING.md documenting the full release process
- publish.bat as CLI alternative for tagging releases

### Changed

- Bumped all GitHub Actions to latest versions (checkout v6, setup-dotnet v5, upload/download-artifact v7/v8)
- NuGet license uses PackageLicenseExpression instead of PackageLicenseFile

### Fixed

- CA1416 warnings: added [SupportedOSPlatform("windows")] to Registry-accessing methods
- CI Ubuntu hang: continue-on-error for known .NET test host hang
- NU5033: resolved duplicate license property conflict between Directory.Build.props and csproj

## [0.9.2] - 2026-03-22

### Added

- XML doc comments across core library (95%+ coverage), CLI tool, and UI app
- UI README.md with architecture overview and build instructions
- Namespace-level documentation (8 NamespaceDoc.cs files)
- docfx API reference site with GitHub Pages deployment
- GitHub Actions workflow for automatic doc site builds

### Changed

- Unified versioning: single `<Version>` in Directory.Build.props shared by all projects
- CLI and UI version displays now read from assembly (no more hardcoded versions)
- publish.bat uses wildcard for .nupkg filename

### Fixed

- CI hang on Ubuntu: added `--blame-hang` timeout to dotnet test
- LICENSE copyright updated to "KernSmith contributors"

## [0.9.1] - 2026-03-22

### Changed

- License changed from proprietary to MIT

## [0.9.0] - 2026-03-20

### Added

- Package icon added and wired into NuGet package
- CLI tool now has a Windows application icon
- `BmFont.FromConfig()` -- generate a bitmap font directly from a .bmfc config file
- `BmFontResult.FntText`, `.FntXml`, `.FntBinary` -- convenience properties for in-memory .fnt access
- `BmFontResult.GetPngData()`, `.GetTgaData()`, `.GetDdsData()` -- encode atlas pages to byte arrays
- `BmFontResult.ToBmfc()` -- round-trip config output from a generation result
- `BmFontBuilder.FromConfig()` -- load a .bmfc config as the builder starting point
- `AtlasPage.ToTga()`, `.ToDds()` -- encode individual atlas pages to TGA or DDS format
- `ToFile()` now also writes a .bmfc config file alongside .fnt and atlas pages

### Changed

- Version bumped from 0.8.0 to 0.9.0
- Assets moved to `assets/` folder (icon, favicons, social preview)
- .gitignore updated for generated font files
- License changed from MIT to proprietary
- Benchmark CI workflow changed to manual-only trigger

## [0.8.0] - 2026-03-20

### Added

- BMFont-compatible bitmap font generation from TTF/OTF/WOFF files
- Text, XML, and binary .fnt output formats
- PNG, TGA, and DDS texture atlas output
- Signed Distance Field (SDF) rendering
- Layered glyph effects: outline, gradient, shadow, glow
- Color font support (COLRv0/CPAL, sbix, CBDT)
- Variable font support
- Font subsetting with custom character sets
- Channel packing for multi-font atlas optimization
- Super-sampling for higher quality rasterization
- GPOS kerning pair extraction
- Multi-page atlas support with configurable max texture size
- In-memory pipeline with optional file output
- Fluent builder API for font generation configuration
- CLI tool for batch bitmap font generation
- Extended metadata in .fnt output
- BmFont .fnt reader for loading existing bitmap fonts
- Library: `BmfcConfigReader` and `BmfcConfigWriter` for programmatic .bmfc file handling
- CLI: `init` command to generate .bmfc config files from CLI flags
