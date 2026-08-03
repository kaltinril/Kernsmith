# KernSmith -- Master Plan

> **Status**: Current release **v0.19.0**. The latest landed work is **Phase 165 -- Native Rasterizer: IRasterizer Integration & Metrics** (the Native backend is now **functional end to end for TrueType fonts**: full `IRasterizer`, glyph/font metrics within ±1px of FreeType, CFF rejected with a clear `RasterizationException`, and Native surfaced in the CLI, docs and the `tests/bmfont-compare` harness). **Next up: Phase 166 -- CFF/Type2 charstring interpreter**, which turns that CFF rejection into OTF support.
>
> ⚠️ **Open core defect found in Phase 165**: synthetic bold/italic silently no-op on backends that report `SupportsSyntheticBold/Italic = false` (Native is the first). Tracked as **Issue 5** in [phase-150-deferred-rasterizer-issues.md](phase-150-deferred-rasterizer-issues.md).
>
> **Complete** (all archived in `done/`): Phases 1-18 (there is no Phase 19), 20, 21 + 21R, 30-34 (including 32b, 32c, 32d and 33b), 37, 55, 60-86 (including 74b, 75L, 76b, 77b, the full 78 series, and 81-85), 90, 95, 96, 97, 100, 105, 161, 162, 163, 164, 165, 185. Phase 34 is Complete (Superseded) by the 160-180 native rasterizer series; Phase 35 rejected (FontStashSharp is just a stbTrueTypeSharp wrapper); Phase 36 superseded by Phase 110; Phase 98 rejected (invalid bug report). Phase 182 is complete but its doc still lives in `plan/`.
>
> **In flight**: Phase 160 (active design record for the native rasterizer series); Phase 250 (UI cleanup -- Phase 1 landed with changed scope, Phase 2's Generate-bar issue needs re-validation, Phases 3-5 largely done); Phase 100b (P2/P3 landed, P4/P5 deferred); Phases 99 and 150 (planning).
>
> **Future / deferred**: Phase 50 (in-memory layer retention); Phase 110 (partially done -- the core post-processor architecture is complete, the remainder is future); Phases 111 and 112 (deferred / not started); Phases 166-180 (native rasterizer implementation, all Future); Phase 181 (superseded by 182); Phase 200 (FontCrafter -- not started); Phase 300 (Perf 6 B/C carried from Phase 95).
>
> **Date**: 2026-08-01

---

## Project Summary

**KernSmith** is a cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF files. It combines FreeTypeSharp for glyph rasterization with our own TTF table parsers (for GPOS kerning, OS/2 metadata, etc.), packs glyphs into texture atlases, and outputs industry-standard BMFont `.fnt` + `.png` pairs. The entire pipeline operates in-memory by default with zero disk I/O required.

---

## Decisions Made

Architecture decisions and their rationale live in **[../project_decisions.md](../project_decisions.md)** —
rasterization backend, TTF table parsing, texture packing, API shape, licensing, memory
model, error hierarchy and UI framework. They are not restated here.

---

## High-Level Pipeline

```
Input (font file bytes or system font path)
  |
  v
Font Loading Layer
  +-- FreeTypeSharp: load font, get face handle
  +-- Our TTF Parser: read tables (cmap, kern, GPOS, name, OS/2, head, hhea, hmtx)
  |
  v
Font Metrics & Kerning
  +-- FreeTypeSharp: per-glyph metrics (advance, bearing, bbox), kern table kerning
  +-- Our Parser: GPOS kerning pairs, font metadata, Unicode ranges, OS/2 metrics
  +-- Merged into unified FontInfo model
  |
  v
Glyph Rasterization
  +-- FreeTypeSharp: render each requested glyph to bitmap buffer
      (configurable: size, DPI, AA mode, SDF)
  |
  v
Texture Atlas Packing
  +-- Our MaxRects packer: arrange glyph bitmaps into atlas pages
      (configurable: max texture size, padding, spacing, power-of-2)
  |
  v
BMFont Model (in-memory)
  +-- InfoBlock, CommonBlock, Pages[], Characters[], KerningPairs[]
  |
  v
Output Layer
  +-- .ToString()       -> BMFont text format (default)
  +-- .ToXml()          -> BMFont XML format
  +-- .ToBinary()       -> BMFont binary format
  +-- .ToFile(path)     -> write .fnt + .png files to disk
  +-- .GetAtlasBytes()  -> raw PNG bytes for each page
```

### Data Flow Responsibilities

| Component | Source | Responsibility |
|-----------|--------|----------------|
| **FreeTypeSharp** | Native FreeType | Load font face, rasterize glyph bitmaps, provide scaled glyph metrics (advance, bearing, bbox), kern table kerning via `FT_Get_Kerning`, SDF rendering, synthetic bold/italic |
| **Our TTF Parser** | Pure C# | Read GPOS kerning pairs, OS/2 table metadata (weight class, typo metrics, x-height, cap height, panose), name table strings, cmap (Unicode coverage), head/hhea/hmtx tables |
| **Packer** | Our C# | MaxRects or Skyline bin packing, multi-page overflow, glyph sorting, padding/spacing handling |
| **Atlas Builder** | Our C# | Compose rasterized glyph bitmaps into atlas page images, PNG encoding |
| **BMFont Writer** | Our C# | Populate in-memory BMFont model, serialize to text/XML/binary formats |

---

## Active Plans

> **Table scope**: this table lists only plan docs that still live in `plan/`. Anything archived under `plan/done/` belongs in [Completed Phases](#completed-phases-archived-in-done) below -- do not add `done/` rows here. Phases 181 and 182 are already closed (superseded / complete) but their docs have not been archived yet, so they are still listed here.

| # | Document | Description | Status |
|---|----------|-------------|--------|
| 50 | [In-Memory Layer Retention](phase-50-layer-retention.md) | Optionally retain per-glyph effect layer bitmaps in memory for engine-side compositing | Future |
| 99 | [BMFont Parity Remaining Gaps](phase-99-bmfont-parity-gaps.md) | Investigate and close remaining metrics differences from Phase 78BB | Planning |
| 100b | [Deferred Advanced Features](phase-100b-deferred-advanced-features.md) | Deferred advanced effects (SdfScale/AdvanceAdjustY done; outline wobble + native render mode deferred) | Partial |
| 110 | [Post-Processing Enhancements](phase-110-post-processing-enhancements.md) | Post-processing pipeline enhancements; the core post-processor architecture landed in Phase 32d | Partially done (core architecture complete; remainder future) |
| 111 | [Texture Fill for Glyphs](phase-111-texture-fill.md) | Design for texture/pattern fill effects on glyphs; no `TextureFillEffect` exists yet | Deferred / not started |
| 112 | [Shader Fill for Glyphs](phase-112-shader-fill.md) | Design for shader-computed (per-pixel) fill effects on glyphs; no `ShaderFillEffect` exists yet | Deferred / not started |
| 150 | [Deferred Rasterizer Issues](phase-150-deferred-rasterizer-issues.md) | Color fonts, variable fonts, native DW kerning, GDI MatchCharHeight bug (deferred from Phase 78G) | Planning |
| 181 | [Atlas Variants: Padded Silhouette Regions](phase-181-shadow-outline-atlas-variants.md) | GitHub #175 — superseded by Phase 182 (dropshadow-only framing was too narrow) | Superseded |
| 182 | [Shared Atlas Groups](phase-182-shared-atlas-groups.md) | GitHub #175 — generic mechanism to pack N independent glyph sources (dropshadow variant, later multi-font) into one shared atlas PNG; shipped in 0.18.0-0.18.2 | Complete |
| 200 | [FontCrafter & Platform Rasterizers](phase-200-fontcrafter-and-platform-rasterizers.md) | FontCrafter product concept and platform-specific rasterizer distribution | Not started / Future |
| 250 | [UI Cleanup & Polish](phase-250-ui-cleanup.md) | Collapsible sections, consistent grids, shared UI helpers, spacing polish. Day-to-day snapshot: [ui-cleanup-progress.md](ui-cleanup-progress.md) | In progress (Phase 1 landed with changed scope, Phases 3-5 largely done; Phase 2's Generate-bar issue needs re-validation) |
| 300 | [Deferred Performance Work](phase-300-deferred-performance.md) | Perf 6 Phases B/C carried from Phase 95 -- pool atlas page & per-glyph buffers; needs IDisposable/ownership redesign (Perf 10 excluded -- proven impossible) | Deferred / Future |

### Native Rasterizer Series (Phases 160-180)

Pure C# TTF/OTF rasterizer initiative; see Phase 160 for design decisions. Phases 161 (scaffold), 162 (glyph table parsers), 163 (outline extraction), 164 (scanline rasterizer) and 165 (IRasterizer integration) are complete (see done/).

**The backend is now functional end to end for TrueType fonts** -- selectable as `RasterizerBackend.Native` / `--rasterizer native`, metrics within ±1px of FreeType, and covered by the `tests/bmfont-compare` harness. CFF/OTF fonts are still rejected with a `RasterizationException`; **Phase 166 (CFF charstring interpreter) is next** and lifts that restriction. Synthetic bold/italic, outline/stroke, SDF, variable and color fonts remain unimplemented (Phases 167-172) -- see Phase 150 Issue 5 for the core gate bug that makes `Bold`/`Italic` silently no-op on Native today.

| # | Document | Description | Status |
|---|----------|-------------|--------|
| 160 | [Rasterizer Design Decisions](phase-160-rasterizer-design-decisions.md) | Rasterizer design decisions / overview (decision record for the series) | Active |
| 166 | [CFF/Type2 Charstring Interpreter](phase-166-cff-charstring-interpreter.md) | CFF/Type2 charstring interpreter (OTF outlines) | **Next** |
| 167 | [Synthetic Bold & Italic](phase-167-synthetic-bold-italic.md) | Synthetic bold + italic transforms | Future |
| 168 | [Synthetic Outline/Stroke](phase-168-synthetic-outline-stroke.md) | Synthetic outline/stroke | Future |
| 169 | [SDF Generation](phase-169-sdf-generation.md) | SDF generation (native backend) | Future |
| 170 | [MSDF Generation](phase-170-msdf-generation.md) | MSDF generation | Future |
| 171 | [Variable Font Support](phase-171-variable-font-support.md) | Variable font support (native backend) | Future |
| 172 | [Color Font Support](phase-172-color-font-support.md) | Color font support / COLR-CPAL (native backend) | Future |
| 173 | [LCD Subpixel Rendering](phase-173-lcd-subpixel-rendering.md) | LCD subpixel rendering | Future |
| 174 | [Auto-Hinting](phase-174-auto-hinting.md) | Auto-hinting | Future |
| 175 | [GSUB Layout](phase-175-gsub-layout.md) | GSUB layout (ligatures/substitution) | Future |
| 176 | [Supersampling + Height Stretch](phase-176-supersampling-height-stretch.md) | Supersampling + height stretch (native integration) | Future |
| 177 | [Performance Optimization](phase-177-performance-optimization.md) | Performance optimization | Future |
| 178 | [WOFF2 Decompression](phase-178-woff-decompression.md) | WOFF2 decompression | Future |
| 179 | [Validation / Golden Masters](phase-179-validation-golden-masters.md) | Validation / golden masters | Future |
| 180 | [Innovation Research](phase-180-innovation-research.md) | Innovation research (ongoing experiments) | Future (Ongoing Research) |

---

## Completed Phases (archived in `done/`)

| # | Document | Description |
|---|----------|-------------|
| 01 | [MVP](done/phase-01-mvp.md) | End-to-end pipeline: TTF -> rasterize -> pack -> BMFont text + PNG |
| 02 | [Complete](done/phase-02-complete.md) | XML/binary output, Skyline packer, SDF, system fonts, variable fonts |
| 03 | [Ecosystem](done/phase-03-ecosystem.md) | WOFF/WOFF2, channel packing, CLI, benchmarks, color fonts, subsetting |
| 04 | [Deferred/Future](done/phase-04-deferred-future.md) | fvar parser, BMFont reader, gradient post-processor, variable font axis API |
| 05 | [Full CLI Tool](done/phase-05-cli-tool.md) | 5 commands, .bmfc config, full option coverage |
| 06 | [BMFont Parity](done/phase-06-bmfont-parity.md) | TGA, super sampling, shadow, autofit, fallback glyph, 10+ parity features |
| 07 | [Extended Metadata](done/phase-07-extended-metadata.md) | SDF spread, gradient, shadow, outline metadata in .fnt (text/XML/binary) |
| 08 | [Optimal Atlas Sizing](done/phase-08-optimal-atlas-sizing.md) | Mathematical atlas size prediction replacing brute-force trial-and-error |
| 09 | [Outline Overhaul](done/phase-09-outline-overhaul.md) | EDT-based anti-aliased outlines with outline color support |
| 10 | [Layered Rendering](done/phase-10-layered-rendering.md) | IGlyphEffect compositing replacing order-dependent post-processor chain |
| 11 | [Solution Restructure](done/phase-11-solution-restructure.md) | Multi-project foundation, net10.0 migration, CLI promotion, app scaffolding |
| 12 | [Pre-Ship Polish](done/phase-12-pre-ship-polish.md) | Security hardening, 65 tests, NuGet packaging, XML docs, API polish |
| 13 | [Batch CLI](done/phase-13-batch-cli.md) | Batch command, .bmfc multi-file processing, collision detection |
| 14 | [Benchmarking & Profiling](done/phase-14-benchmarking-profiling.md) | 50+ benchmarks, PipelineMetrics, CLI --time/--profile, benchmark command |
| 15 | [Library Performance](done/phase-15-library-performance.md) | FontCache, GenerateBatch API, static SystemFontProvider -- 18 fonts in 196ms |
| 16 | [BMFont .bmfc Compatibility](done/phase-16-bmfc-compatibility.md) | Standard BMFont key=value format, drop legacy INI, same files work in both tools |
| 17 | [Rebrand to KernSmith](done/phase-17-rebrand-kernsmith.md) | Full project rename from bmfontier to KernSmith |
| 18 | [API Usability](done/phase-18-api-usability.md) | FromConfig, convenience properties, GetPngData, ToBmfc, Builder.FromConfig, init CLI command |
| 20 | [Release Readiness](done/phase-20-release-readiness.md) | Version alignment, package icon, dotnet pack, CI verification, GitHub polish, first NuGet publish |
| 21 | [Atlas Output Modes](done/phase-21-atlas-output-modes.md) | Combined batch atlas, render-to-existing-PNG, atlas size query & constraints |
| 21R | [Atlas Output Modes Review](done/phase-21-review-findings.md) | Code review findings from Phase 21 implementation |
| 30 | [WASM Rasterization](done/phase-30-wasm-rasterization.md) | Extract FreeTypeRasterizer from core library into standalone plugin package |
| 31 | [WASM Platform Restrictions Research](done/phase-31-wasm-restrictions-research.md) | Research WASM/AOT platform restrictions affecting rasterizer strategy |
| 32 | [StbTrueType Managed Rasterizer](done/phase-32-stbtruetype-rasterizer.md) | Pure C# rasterizer plugin using StbTrueTypeSharp for WASM/AOT support |
| 32b | [StbTrueType Docs & Publishing](done/phase-32b-stbtruetype-docs-publishing.md) | Documentation, DocFX pages, and CI/CD publishing for StbTrueType plugin |
| 32c | [StbTrueType Validation Fixes](done/phase-32c-stbtruetype-fixes.md) | Bug fixes, missing guards, and test gaps from Phase 30-32 validation |
| 32d | [StbTrueType Synthetic Bold & Italic](done/phase-32d-stbtruetype-synthetic-bold-italic.md) | Outline-level synthetic bold/italic using stb_truetype shape API |
| 33 | [WASM Integration and Validation](done/phase-33-wasm-validation.md) | Validate KernSmith + StbTrueType works in Blazor WASM |
| 33B | [FontCreator Backend Support](done/phase-33b-fontcreator-backend-support.md) | Backend selection for `KernSmithFontCreator` / `GumFontGenerator` instead of reimplementing to swap rasterizers |
| 34 | [Custom Pure C# Rasterizer](done/phase-34-custom-rasterizer.md) | Complete (Superseded) -- research into a fully custom pure C# TTF rasterizer; superseded by the Phase 160-180 native rasterizer series |
| 35 | [FontStashSharp Rasterizer](done/phase-35-fontstashsharp-rasterizer.md) | Rejected -- FontStashSharp is just a stbTrueTypeSharp wrapper; useful techniques distilled into Phases 160-180 |
| 36 | [Bitmap Bold & Italic Post-Processing](done/phase-36-bitmap-bold-italic-postprocessing.md) | Superseded -- absorbed into Phase 110 |
| 37 | [QA, Security & Performance Fixes](done/phase-37-qa-security-perf-fixes.md) | Correctness, security hardening, and perf fixes from full codebase review |
| 55 | [UI Core Library Prerequisites](done/phase-55-ui-core-library-prerequisites.md) | API additions needed by the UI: font reader, builder methods, FontInfo expansion |
| 60 | [UI MVP](done/phase-60-ui-mvp.md) | MonoGame + GUM UI app: project scaffold, three-panel layout, font loading, basic generation |
| 61 | [Font Loading & Character Selection](done/phase-61-ui-font-character-selection.md) | System font browser, BMFont-style character grid, Unicode block sidebar, text-based selection |
| 62 | [Effects System UI](done/phase-62-ui-effects-system.md) | Outline, shadow, gradient controls with interactive angle/offset pads, channel config |
| 63 | [Atlas & Texture Configuration](done/phase-63-ui-atlas-texture-config.md) | Atlas size, padding/spacing, packing algorithm, output format, metrics display |
| 64 | [Live Preview & Visualization](done/phase-64-ui-preview-visualization.md) | Atlas preview with zoom/pan, glyph inspector, sample text, kerning visualization |
| 65 | [Project Management & File Operations](done/phase-65-ui-project-file-operations.md) | Menu system, save/load .bmfc, export, import, undo/redo, recent files |
| 66 | [Advanced Features](done/phase-66-ui-advanced-features.md) | Variable fonts, SDF, custom glyphs, batch generation, font inspector, color fonts |
| 67 | [Workflow & UX Polish](done/phase-67-ui-workflow-ux-polish.md) | Guided workflow, engine presets, contextual help, drag-and-drop, themes |
| 68 | [Platform, Performance & Accessibility](done/phase-68-ui-platform-performance.md) | Background generation, cross-platform, keyboard accessibility, error handling, packaging |
| 69 | [Final Polish & Release Prep](done/phase-69-ui-final-polish.md) | UI consistency, about dialog, status bar, accent headers, panel backgrounds |
| 70 | [UI Manual Review](done/phase-70-ui-manual-review.md) | Manual review and testing of UI application |
| 71 | [UI Stabilization](done/phase-71-ui-stabilization.md) | UI bug fixes and stabilization |
| 72 | [UI Issues Round 2](done/phase-72-ui-issues-round2.md) | Fix remaining UI issues from manual testing -- 21 issues resolved |
| 73 | [Documentation Review](done/phase-73-documentation-review.md) | XML doc comments, class/method summaries, README gaps across library, CLI, and UI |
| 74 | [MIT License](done/phase-74-mit-license.md) | Switch all license references to MIT |
| 74B | [License Attribution & Compliance](done/phase-74b-license-attribution-compliance.md) | Third-party license attribution for redistributed/linked dependencies |
| 75 | [DocFX Docs Site Fixes](done/phase-75-docs-site-fixes.md) | Fix issues found on the deployed DocFX documentation site |
| 75L | [DocFX Logo Fix](done/phase-docfx-logo-fix.md) | Fix oversized navbar logo on DocFX documentation site |
| 76 | [Metrics Parity with BMFont](done/phase-76-metrics-parity.md) | Investigate and fix glyph metric differences between KernSmith and BMFont output |
| 76B | [Outline and Italic Fixes](done/phase-76b-outline-and-italic-fixes.md) | Fix outline rendering and italic glyph clipping issues |
| 77 | [Color Picker Dialog](done/phase-77-color-picker-dialog.md) | Build a reusable color picker dialog that opens when clicking a color swatch |
| 77B | [Force Size & Remove Presets](done/phase-77b-atlas-size-auto-mode.md) | Replace engine presets with Force Size checkbox |
| 78 | [Pluggable Rasterizer Backends](done/phase-78-pluggable-rasterizers.md) | Pluggable rasterizer architecture with GDI and DirectWrite backends |
| 78A | [Rasterizer Foundation](done/phase-78a-rasterizer-foundation.md) | IRasterizer interface, factory, capability system |
| 78B | [GDI Backend](done/phase-78b-gdi-backend.md) | GDI-based rasterizer for BMFont output parity (Windows-only) |
| 78BB | [GDI Parity Fixes](done/phase-78bb-gdi-parity.md) | GDI parity fixes -- metrics, kerning, sizing bypass |
| 78C | [DirectWrite Backend](done/phase-78c-directwrite-backend.md) | DirectWrite-based rasterizer (Windows-only) |
| 78CC | [Font Sizing & DPI Gaps](done/phase-78cc-sizing-dpi-gaps.md) | Verified sizing/DPI parity across backends |
| 78D | [CLI & UI Integration](done/phase-78d-cli-ui-integration.md) | Wire rasterizer selection into CLI and UI |
| 78E | [Plugin Template](done/phase-78e-plugin-template.md) | Template for third-party rasterizer plugins |
| 78F | [Space Outline Rendering](done/phase-78f-space-outline-glyph.md) | Space gets transparent atlas entry when outline > 0 |
| 78G | [Remaining Issues](done/phase-78g-remaining-issues.md) | Color fonts, variable fonts, synthetic bold/italic, rounding differences |
| 78S | [Documentation & Code Quality](done/phase-78s-documentation.md) | XML doc comments, UI tooltips, CLI help text, READMEs |
| 79 | [Replace FluentAssertions with Shouldly](done/phase-79-replace-fluentassertions.md) | Replace FluentAssertions (paid licensing) with Shouldly across test suite |
| 80 | [Atlas Preview Rendering](done/phase-80-atlas-preview-rendering.md) | Fix atlas preview rendering quality in UI to match saved PNG |
| 81 | [Hiero Format Support](done/phase-81-hiero-format-support.md) | Hiero `.hiero` config format specification and design decisions |
| 82 | [Hiero Core Library](done/phase-82-hiero-core-library.md) | Add `.hiero` config read/write to the NuGet library |
| 83 | [Hiero UI Changes](done/phase-83-hiero-ui-changes.md) | Update UI for `.hiero` file dialogs, drag-drop, project service |
| 84 | [Hiero CLI Changes](done/phase-84-hiero-cli-changes.md) | Update CLI for `.hiero` format auto-detection and batch support |
| 85 | [Hiero Documentation](done/phase-85-hiero-documentation.md) | Document `.hiero` support in README, CLI docs, samples |
| 86 | [RegisterFont File-Path Overload](done/phase-86-register-font-file-path-overload.md) | Add string filePath overload to KernSmithFontCreator.RegisterFont using TitleContainer.OpenStream |
| 90 | [Native AOT Compliance](done/phase-90-aot-compliance.md) | Native AOT / trimming compatibility for core library -- analyzers enabled, version reflection replaced with compile-time constant, RasterizerFactory reflection contained (Option A); AOT consumers register backends explicitly |
| 95 | [Performance Optimization & Bug Fixes](done/phase-95-performance-and-bugs.md) | Fix confirmed bugs (outline field, batch encoding, options mutation) and optimize generation performance |
| 96 | [Documentation Audit](done/phase-96-documentation-audit.md) | Audit of plan, reference, root-level, and CI docs against the actual codebase; stale/missing/inaccurate claims corrected |
| 97 | [Rasterizer Auto-Discovery](done/phase-97-rasterizer-auto-discovery.md) | Auto-discover rasterizer backends via Type.GetType(); remove 13 manual workarounds; add ILLink trimmer protection |
| 98 | [Outline Advance Bug](done/phase-98-outline-advance-bug.md) | Rejected -- outline not adjusting xadvance is correct BMFont behavior, not a bug |
| 100 | [Hiero Advanced Features](done/phase-100-hiero-advanced-features.md) | Advanced Hiero features requiring new KernSmith properties |
| 105 | [Text Layout Engine](done/phase-105-text-layout-engine.md) | Core text layout engine + framework rendering examples; pixel format helpers resolved |
| 161 | [Native Rasterizer Scaffold](done/phase-161-native-project-scaffold.md) | Pure C# binary font reader, table directory parser, core table parsers (head/hhea/hmtx/OS2/cmap/maxp), NativeRasterizer IRasterizer shell -- Complete |
| 162 | [glyf/loca/maxp Parsers](done/phase-162-glyf-loca-maxp-parsers.md) | Pure C# `loca` + `glyf` outline parsers (simple + composite with transforms, implicit on-curve midpoints) and the `maxp` v1.0 extended profile -- Complete |
| 163 | [Outline Extraction](done/phase-163-outline-extraction.md) | `ParsedGlyph` -> normalized MoveTo/LineTo/CubicTo/Close commands, exact quadratic-to-cubic elevation, font-unit -> pixel transform, adaptive De Casteljau flattening to directed `EdgeSegment`s -- Complete |
| 164 | [Scanline Rasterizer](done/phase-164-scanline-rasterizer.md) | Signed-area trapezoid coverage over an active-edge table, pooled `area`/`cover` buffers, Grayscale/None anti-aliasing, `RasterResult` output; SSIM > 0.95 vs StbTrueType (perf benchmark deferred) -- Complete |
| 165 | [IRasterizer Integration](done/phase-165-irasterizer-integration.md) | Full `IRasterizer` on the Native backend (cmap -> glyf -> outline -> `GlyphBox` -> flatten -> scanline), OS/2-based font metrics, `GlyphBox` sizing, CFF rejection; metrics within ±1px of FreeType and `.fnt` base/lineHeight exact. Native surfaced in CLI/docs/comparison harness. Exposed Phase 150 Issue 5 -- Complete |
| 185 | [Font Sourcing](done/phase-185-font-sourcing.md) | IFontSource abstraction + KernSmith.Fonts.Web package for web font CDNs (WOFF) -- Complete |

### Topical Plan Docs (archived in `done/`)

These detailed docs were used during implementation and remain as reference material.

| Document | Description |
|----------|-------------|
| [Vision](done/kernsmith-vision.md) | Original project vision and goals |
| [Data Types](done/plan-data-types.md) | All shared types, interfaces, and error handling (source of truth) |
| [Project Structure](done/plan-project-structure.md) | Solution layout, namespace mapping, dependencies |
| [API Design](done/plan-api-design.md) | Public API surface, builder pattern, configuration types |
| [Font Parsing](done/plan-font-parsing.md) | FreeTypeSharp usage, TTF parser scope, GPOS parsing |
| [Rasterization](done/plan-rasterization.md) | Glyph rasterization pipeline, FreeTypeRasterizer |
| [Texture Packing](done/plan-texture-packing.md) | MaxRects/Skyline algorithms, multi-page strategy |
| [Output Formats](done/plan-output-formats.md) | BMFont model classes, text/XML/binary serialization |
| [Testing](done/plan-testing.md) | xUnit test strategy, test fonts, validation criteria |
| [Implementation Order](done/plan-implementation-order.md) | Original phased task breakdown with dependency graphs (Phases 1-4) |
| [Future Phases](done/plan-phase-future.md) | Deferred items + Phases 5-7 tracking |
| [CLI Tool](done/plan-cli.md) | Full CLI plan -- BMFont.exe replacement |
| [BMFont Parity](done/plan-bmfont-parity.md) | 15 missing features from BMFont.exe |
| [Color Fonts](done/plan-color-fonts.md) | COLRv0/CPAL, sbix, CBDT support |
| [Font Subsetting](done/plan-font-subsetting.md) | Logical subsetting -- filter cmap/kern/GPOS |
| [Extended Metadata](done/plan-extended-metadata.md) | SDF spread, gradient, shadow metadata in .fnt |
| [Bug Fixes](done/plan-bug-fixes.md) | All applied bug fixes |


---

## Reference Documents

| # | Document | Description |
|---|----------|-------------|
| REF-01 | [Font Library Comparison](../reference/REF-01-font-library-comparison.md) | Evaluation of .NET font libraries |
| REF-02 | [FreeTypeSharp Evaluation](../reference/REF-02-freetypesharp-evaluation.md) | Detailed FreeTypeSharp capabilities and gaps |
| REF-03 | [TTF Font Reference](../reference/REF-03-ttf-font-reference.md) | TrueType font format reference |
| REF-04 | [Other Font Formats](../reference/REF-04-other-font-formats-reference.md) | WOFF, OTF, and other format details |
| REF-05 | [BMFont Format Reference](../reference/REF-05-bmfont-format-reference.md) | BMFont file format specification |
| REF-06 | [Texture Packing Reference](../reference/REF-06-texture-packing-reference.md) | Rectangle packing algorithm research |
| REF-07 | [GUM Forms Cheatsheet](../reference/REF-07-gum-forms-cheatsheet.md) | GUM/Forms UI framework quick reference |
| REF-08 | [BMFont Internals](../reference/REF-08-bmfont-internals.md) | BMFont internals documentation |
| REF-09 | [Font Metrics and Sizing](../reference/REF-09-font-metrics-and-sizing.md) | Font metrics, sizing, synthetic bold/italic, outline rendering |
| REF-10 | [Hiero Format Reference](../reference/REF-10-hiero-format-reference.md) | Hiero `.hiero` configuration file format specification |
| REF-11 | [WASM Restrictions](../reference/REF-11-wasm-restrictions.md) | WASM/AOT platform restrictions research |
| REF-12 | [Rasterizer Backends](../reference/REF-12-rasterizer-backends.md) | Rasterizer backends documentation |

---

## Resolved Decisions

Settled questions now live in the source-of-truth files rather than this plan:
**[../project_facts.md](../project_facts.md)** for the stack, target-framework matrix,
package identity, namespaces and license; **[../project_decisions.md](../project_decisions.md)**
for the choices and their reasons.

---

## Disallowed Technologies

> **Do not use these packages or libraries.**

| Package | Reason | Alternative |
|---------|--------|-------------|
| **FluentAssertions** | Moved to paid/commercial licensing. Removed in Phase 79. | **Shouldly** (MIT, `using Shouldly;`) |
| **SixLabors.*** | Split license, incompatible with this project's MIT-only dependency rule. | StbImageSharp / StbImageWriteSharp |

---

## Glossary

| Term | Definition |
|------|-----------|
| **BMFont** | Bitmap font format created by AngelCode. The `.fnt` descriptor + `.png` atlas pair. |
| **cmap** | Character-to-glyph mapping table in TTF/OTF fonts. |
| **GPOS** | Glyph Positioning table in OpenType fonts. Contains kerning (and other positioning) data that supersedes the legacy kern table. |
| **kern** | Legacy kerning table in TrueType fonts. Simpler than GPOS but increasingly rare in modern fonts. |
| **MaxRects** | Rectangle bin packing algorithm by Jukka Jylanki (2010). Maintains a list of free rectangles, splits on placement, prunes contained rects. |
| **BSSF** | BestShortSideFit -- a MaxRects heuristic that minimizes the leftover space on the shorter side of the fit. |
| **Skyline** | Rectangle packing algorithm that maintains a 1D height map. Simpler and faster than MaxRects. |
| **SDF** | Signed Distance Field. A technique for resolution-independent font rendering. Each texel stores the distance to the nearest glyph edge. |
| **P/Invoke** | Platform Invocation Services. .NET mechanism for calling native C functions from managed code. |
| **26.6 fixed point** | FreeType's internal number format. The value is a 32-bit integer where the lower 6 bits are the fractional part. Divide by 64 to get the pixel value. |
