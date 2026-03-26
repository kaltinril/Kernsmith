# KernSmith -- Master Plan

> **Status**: Phases 1-21, 55, 60-69, 72-77, 77B, 79, 80 complete. Phase 30 (WASM) is future/exploratory. Phase 78A complete. 78B-78E planned.
> **Date**: 2026-03-24

---

## Project Summary

**KernSmith** is a cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF files. It combines FreeTypeSharp for glyph rasterization with our own TTF table parsers (for GPOS kerning, OS/2 metadata, etc.), packs glyphs into texture atlases, and outputs industry-standard BMFont `.fnt` + `.png` pairs. The entire pipeline operates in-memory by default with zero disk I/O required.

---

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Font rasterization** | FreeTypeSharp (MIT, wraps FreeType 2.13.2 via P/Invoke) | Industry-standard rasterizer, small native footprint (~12 MB), MIT license, supports SDF, hinting, AA modes. Use everything it exposes -- metrics, kerning (kern table), glyph bitmaps. |
| **TTF table parsing** | Our own pure C# parser | FreeTypeSharp cannot expose GPOS kerning pairs, OS/2 metadata, name table strings, or variable font axes. We parse the tables FreeTypeSharp cannot reach. No additional dependencies. |
| **Texture packing** | MaxRects (BestShortSideFit) primary, Skyline as fast mode | MaxRects achieves 93-97% packing efficiency. Skyline is 2-5x faster with 2-5% less efficiency. Our own implementation based on public domain reference code. |
| **API design** | In-memory model first, output methods on top | Core pipeline produces a format-agnostic model. `.ToString()`, `.ToXml()`, `.ToBinary()`, `.ToFile()` render it. Zero disk I/O by default. |
| **Licensing** | MIT open source, no paid/restrictive dependencies | FreeTypeSharp: MIT. FreeType native: FreeType License (BSD-like). Our code: MIT. SixLabors: explicitly excluded (split license). |
| **Cross-platform** | Anywhere .NET + FreeType native binaries run | Windows, macOS, Linux, Android, iOS, tvOS via FreeTypeSharp's bundled natives. No Linux ARM64 or WASM (FreeTypeSharp gap). |

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

| # | Document | Description | Status |
|---|----------|-------------|--------|
| 30 | [WASM Rasterization](phase-30-wasm-rasterization.md) | Live investigation of WASM-compatible rasterizers (prior research was preliminary) | Future |
| 50 | [In-Memory Layer Retention](phase-50-layer-retention.md) | Optionally retain per-glyph effect layer bitmaps in memory for engine-side compositing | Future |
| 77B | [Force Size & Remove Presets](done/phase-77b-atlas-size-auto-mode.md) | Replace engine presets with Force Size checkbox | Complete |
| 80 | [Atlas Preview Rendering](done/phase-80-atlas-preview-rendering.md) | Fix atlas preview rendering quality in UI to match saved PNG | Complete |
| 81 | [Hiero Format Support](phase-81-hiero-format-support.md) | Hiero `.hiero` config format specification and design decisions | Planning |
| 82 | [Hiero Core Library](phase-82-hiero-core-library.md) | Add `.hiero` config read/write to the NuGet library | Planning |
| 83 | [Hiero UI Changes](phase-83-hiero-ui-changes.md) | Update UI for `.hiero` file dialogs, drag-drop, project service | Planning |
| 84 | [Hiero CLI Changes](phase-84-hiero-cli-changes.md) | Update CLI for `.hiero` format auto-detection and batch support | Planning |
| 85 | [Hiero Documentation](phase-85-hiero-documentation.md) | Document `.hiero` support in README, CLI docs, samples | Planning |
| 79 | [Replace FluentAssertions with Shouldly](done/phase-79-replace-fluentassertions.md) | Replace FluentAssertions (paid licensing) with Shouldly across test suite | Complete |
| 78 | [Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md) | Pluggable rasterizer architecture with GDI and DirectWrite backends | In Progress |
| 78A | [Rasterizer Foundation](done/phase-78a-rasterizer-foundation.md) | IRasterizer interface, factory, capability system | Complete |
| 78B | [GDI Backend](phase-78b-gdi-backend.md) | GDI-based rasterizer for BMFont output parity (Windows-only) | Planning |
| 78C | [DirectWrite Backend](phase-78c-directwrite-backend.md) | DirectWrite-based rasterizer (Windows-only) | Planning |
| 78D | [CLI & UI Integration](phase-78d-cli-ui-integration.md) | Wire rasterizer selection into CLI and UI | Planning |
| 78E | [Plugin Template](phase-78e-plugin-template.md) | Template for third-party rasterizer plugins | Planning (deferred) |
| 100 | [Hiero Advanced Features](phase-100-hiero-advanced-features.md) | Advanced Hiero features requiring new KernSmith properties | Future |

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
| 13 | [Batch CLI](done/phase-13-batch-cli.md) | Batch command, .bmfc multi-file processing, collision detection |
| 14 | [Benchmarking & Profiling](done/phase-14-benchmarking-profiling.md) | 50+ benchmarks, PipelineMetrics, CLI --time/--profile, benchmark command |
| 15 | [Library Performance](done/phase-15-library-performance.md) | FontCache, GenerateBatch API, static SystemFontProvider -- 18 fonts in 196ms |
| 12 | [Pre-Ship Polish](done/phase-12-pre-ship-polish.md) | Security hardening, 65 tests, NuGet packaging, XML docs, API polish |
| 16 | [BMFont .bmfc Compatibility](done/phase-16-bmfc-compatibility.md) | Standard BMFont key=value format, drop legacy INI, same files work in both tools |
| 17 | [Rebrand to KernSmith](done/phase-17-rebrand-kernsmith.md) | Full project rename from bmfontier to KernSmith |
| 18 | [API Usability](done/phase-18-api-usability.md) | FromConfig, convenience properties, GetPngData, ToBmfc, Builder.FromConfig, init CLI command |
| 20 | [Release Readiness](done/phase-20-release-readiness.md) | Version alignment, package icon, dotnet pack, CI verification, GitHub polish, first NuGet publish |
| 21 | [Atlas Output Modes](done/phase-21-atlas-output-modes.md) | Combined batch atlas, render-to-existing-PNG, atlas size query & constraints |
| 55 | [UI Core Library Prerequisites](done/phase-55-ui-core-library-prerequisites.md) | API additions needed by the UI: font reader, builder methods, FontInfo expansion |
| 69 | [Final Polish & Release Prep](done/phase-69-ui-final-polish.md) | UI consistency, about dialog, status bar, accent headers, panel backgrounds |
| 72 | [UI Issues Round 2](done/phase-72-ui-issues-round2.md) | Fix remaining UI issues from manual testing — 21 issues resolved |
| 74 | [MIT License](done/phase-74-mit-license.md) | Switch all license references to MIT |
| 75 | [DocFX Docs Site Fixes](done/phase-75-docs-site-fixes.md) | Fix issues found on the deployed DocFX documentation site |
| 60 | [UI MVP](done/phase-60-ui-mvp.md) | MonoGame + GUM UI app: project scaffold, three-panel layout, font loading, basic generation |
| 61 | [Font Loading & Character Selection](done/phase-61-ui-font-character-selection.md) | System font browser, BMFont-style character grid, Unicode block sidebar, text-based selection |
| 62 | [Effects System UI](done/phase-62-ui-effects-system.md) | Outline, shadow, gradient controls with interactive angle/offset pads, channel config |
| 63 | [Atlas & Texture Configuration](done/phase-63-ui-atlas-texture-config.md) | Atlas size, padding/spacing, packing algorithm, output format, metrics display |
| 64 | [Live Preview & Visualization](done/phase-64-ui-preview-visualization.md) | Atlas preview with zoom/pan, glyph inspector, sample text, kerning visualization |
| 65 | [Project Management & File Operations](done/phase-65-ui-project-file-operations.md) | Menu system, save/load .bmfc, export, import, undo/redo, recent files |
| 66 | [Advanced Features](done/phase-66-ui-advanced-features.md) | Variable fonts, SDF, custom glyphs, batch generation, font inspector, color fonts |
| 67 | [Workflow & UX Polish](done/phase-67-ui-workflow-ux-polish.md) | Guided workflow, engine presets, contextual help, drag-and-drop, themes |
| 68 | [Platform, Performance & Accessibility](done/phase-68-ui-platform-performance.md) | Background generation, cross-platform, keyboard accessibility, error handling, packaging |
| 70 | [UI Manual Review](done/phase-70-ui-manual-review.md) | Manual review and testing of UI application |
| 71 | [UI Stabilization](done/phase-71-ui-stabilization.md) | UI bug fixes and stabilization |
| 73 | [Documentation Review](done/phase-73-documentation-review.md) | XML doc comments, class/method summaries, README gaps across library, CLI, and UI |
| 74 | [MIT License](phase-74-mit-license.md) | Switch all license references from proprietary to MIT |
| 76 | [Metrics Parity with BMFont](done/phase-76-metrics-parity.md) | Investigate and fix glyph metric differences between KernSmith and BMFont output |
| 76B | [Outline and Italic Fixes](done/phase-76b-outline-and-italic-fixes.md) | Fix outline rendering and italic glyph clipping issues |
| 77 | [Color Picker Dialog](done/phase-77-color-picker-dialog.md) | Build a reusable color picker dialog that opens when clicking a color swatch |

### Topical Plan Docs (archived in `done/`)

These detailed docs were used during implementation and remain as reference material.

| Document | Description |
|----------|-------------|
| [Vision](done/KernSmith-vision.md) | Original project vision and goals |
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
| REF-09 | [Font Metrics and Sizing](../reference/REF-09-font-metrics-and-sizing.md) | Font metrics, sizing, synthetic bold/italic, outline rendering |
| REF-10 | [Hiero Format Reference](../reference/REF-10-hiero-format-reference.md) | Hiero `.hiero` configuration file format specification |

---

## Resolved Decisions

| # | Question | Decision | Details |
|---|----------|----------|---------|
| 1 | **PNG encoding library** | **StbImageWriteSharp** (public domain) | Confirmed. See [done/plan-project-structure.md](done/plan-project-structure.md). |
| 2 | **Target framework** | **net10.0** | Migrated from net8.0 in Phase 11. All projects unified on net10.0 via Directory.Build.props. |
| 3 | **Project license** | **MIT** | Finalized as MIT open source (2026-03-22). See Phase 74. |
| 4 | **NuGet package name** | **KernSmith** | Package ID `KernSmith`, main API class `KernSmith`. |
| 5 | **FreeTypeSharp usage boundary** | Use everything it can do | Our parser only covers what FreeTypeSharp cannot (GPOS, OS/2, name, cmap). No duplication. |
| 6 | **Unsafe code policy** | `AllowUnsafeBlocks` in main project | Isolated to FreeType interop (`FreeTypeRasterizer.cs`, `FreeTypeNative.cs`). Rest is safe C#. |
| 7 | **FreeType memory** | Manual lifecycle via `IDisposable` | Pin font data with `GCHandle`. Do NOT use `FreeTypeFaceFacade`. See [done/plan-rasterization.md](done/plan-rasterization.md). |
| 8 | **Test framework** | **xUnit** + Shouldly | FluentAssertions replaced with Shouldly in Phase 79 (FluentAssertions moved to paid licensing). See [done/plan-testing.md](done/plan-testing.md). |
| 9 | **Error handling** | Custom exception hierarchy | `FontParsingException`, `RasterizationException`, `AtlasPackingException`. See [done/plan-data-types.md](done/plan-data-types.md). |
| 10 | **UI framework** | **MonoGame (DesktopGL) + GUM UI + MonoGame.Extended** | Cross-platform, game-engine-native rendering, GUM provides Forms controls with MVVM binding. Code-only (no XAML, no GUM editor). NativeFileDialogSharp for OS file dialogs. Evaluated Avalonia, WPF, MAUI — chose MonoGame+GUM for alignment with target audience (game developers). For future web deployment, KNI (API-compatible MonoGame fork) provides Blazor WebGL — swap is NuGet-only, no code changes. Web rasterization tracked in Phase 30. |

---

## Disallowed Technologies

> **Do not use these packages or libraries.** Any agent working on this project must avoid introducing these dependencies.

| Package | Reason | Alternative |
|---------|--------|-------------|
| **FluentAssertions** | Moving to paid/commercial licensing (2026). Removed in Phase 79. | **Shouldly** (MIT, `using Shouldly;`) |

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
