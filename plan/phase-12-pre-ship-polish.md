# Phase 12 — Pre-Ship Polish

> **Status**: Planning
> **Created**: 2026-03-20
> **Goal**: Address all remaining quality, security, testing, and packaging gaps before the first stable NuGet release.

---

## Overview

This phase consolidates everything that needs attention before shipping KernSmith as a production NuGet package. Items are organized into five tracks that can be worked in parallel. The FT_Stroker compositing fix (previously tracked as a standalone plan) is included here as an optional quality improvement.

---

## Track A — Security Hardening

Issues identified by security audit. Prioritized by severity.

| # | Severity | Issue | File(s) | Fix |
|---|----------|-------|---------|-----|
| A1 | High | FreeTypeRasterizer Dispose: if `FT_Done_Face` throws, library + pinned handle leak | `Rasterizer/FreeTypeRasterizer.cs:421-437` | Wrap in sequential try/finally blocks to ensure face, library, and pinned handle are always freed |
| A2 | Medium | Bare catch blocks in system font scanning swallow all exceptions including OOM | `Font/DefaultSystemFontProvider.cs:148-157` | Narrow catches to `FontParsingException`, `IOException`, `UnauthorizedAccessException` |
| A3 | Medium | XXE defense-in-depth: `XDocument.Parse` on untrusted .fnt input | `Output/BmFontReader.cs:98` | Use `XmlReader.Create` with `DtdProcessing = DtdProcessing.Prohibit` |
| A4 | Medium | Bare catch-all in `CompositeWithFtStroker` swallows OOM/StackOverflow | `BmFont.cs:637-640` | Catch `Exception` instead of bare `catch`; re-throw fatal exceptions |
| A5 | Medium | No `ObjectDisposedException` guard on disposed FreeTypeRasterizer | `Rasterizer/FreeTypeRasterizer.cs:107` | Add `_disposed` check + throw at top of each public method |
| A6 | Low | Path traversal in `BmFont.Load`: malicious .fnt `file` field can read arbitrary files | `BmFont.cs:325` | Use `Path.GetFileName(pageEntry.File)` before `Path.Combine` |
| A7 | Low | WOFF `totalSfntSize` from untrusted input used for allocation without bounds check | `Font/WoffDecompressor.cs:64,88` | Add upper bound check (e.g., 100 MB) before allocation |
| A8 | Low | cmap format 12 `endCharCode` can cause huge dictionary allocation | `Font/TtfParser.cs:532-544` | Cap `endCharCode` at `0x10FFFF` per Unicode maximum |
| A9 | Low | No validation on `FontGeneratorOptions.Size` (0, negative, huge values) | `Config/FontGeneratorOptions.cs:12` | Validate `Size > 0 && Size <= 10000` in `BmFont.Generate` |
| A10 | Low | No validation on `MaxTextureWidth`/`MaxTextureHeight` (0 or negative) | `Config/FontGeneratorOptions.cs` | Validate positive values in `BmFont.Generate` |

---

## Track B — Test Coverage

208 tests currently pass with 0 skipped. The following gaps were identified.

### B1 — High Priority: Input Validation Tests

| # | Test | Description |
|---|------|-------------|
| B1.1 | Null input guards | `BmFont.Generate(null, ...)`, `BmFont.Load(null)`, `CharacterSet.FromChars(null)` should throw `ArgumentNullException` |
| B1.2 | Empty/garbage font data | `BmFont.Generate(new byte[0], 32)`, `BmFont.Generate(randomBytes, 32)` should throw `FontParsingException` |
| B1.3 | Builder without font | `BmFont.Builder().Build()` should throw `InvalidOperationException` |
| B1.4 | Guard condition tests | SDF + super sampling, channel packing + color font — verify `InvalidOperationException` |

### B2 — High Priority: Untested Output Formats

| # | Test | Description |
|---|------|-------------|
| B2.1 | DDS encoder | Create known pixel data, encode, verify header bytes and pixel layout |
| B2.2 | TGA encoder | Create known pixel data, encode, verify header and BGRA byte order |
| B2.3 | `ToFile()` integration | Write to temp directory, verify .fnt + .png files exist and are valid |

### B3 — Medium Priority: Feature Coverage

| # | Test | Description |
|---|------|-------------|
| B3.1 | SDF generation | Generate with `Sdf = true`, verify non-empty output with expected characteristics |
| B3.2 | Multi-page atlas | Large charset + small max texture → verify `pageCount > 1` and correct page assignments |
| B3.3 | EDT unit test | Small known image → verify computed distances match expected values |
| B3.4 | Corrupted .fnt input | Truncated binary, missing fields in text format → verify graceful error |
| B3.5 | WOFF2 unsupported | Verify clear `NotSupportedException` message |

### B4 — Low Priority: Feature Edge Cases

| # | Test | Description |
|---|------|-------------|
| B4.1 | CustomGlyph replacement | Verify `ApplyCustomGlyphs` injects custom glyph images |
| B4.2 | MatchCharHeight two-pass | Verify rescaling produces correct metrics |
| B4.3 | EqualizeCellHeights | Verify all glyphs padded to uniform height |
| B4.4 | Extended metadata reflection | Verify outline thickness extracted correctly from `OutlinePostProcessor` |
| B4.5 | Super-sampling metric accuracy | Verify `BearingX / level` truncation doesn't cause visible misalignment |

### B5 — Code Quality Fixes (discovered during test audit)

| # | Fix | File |
|---|-----|------|
| B5.1 | Add `ArgumentNullException.ThrowIfNull()` to all public methods in `BmFont.cs` | `BmFont.cs` |
| B5.2 | Add null guards to `CharacterSet.FromChars` | `Config/CharacterSet.cs` |
| B5.3 | Replace reflection in `BmFontModelBuilder` (`_outlineWidth` field access) with public property | `Output/BmFontModelBuilder.cs:161-163`, `Rasterizer/OutlinePostProcessor.cs` |
| B5.4 | Wrap FreeType native exceptions from `LoadFont` in `FontParsingException` | `Rasterizer/FreeTypeRasterizer.cs` |
| B5.5 | Remove dead assignment in `ReadBitmapGlyph` | `Rasterizer/FreeTypeNative.cs:121` |

---

## Track C — NuGet Package Readiness

| # | Priority | Item | Details |
|---|----------|------|---------|
| C1 | Critical | **Add LICENSE file** | Create `LICENSE` at repo root with MIT text. csproj declares MIT but no file exists. |
| C2 | Critical | **Fix placeholder URLs** | `PackageProjectUrl` and `RepositoryUrl` in csproj are `github.com/user/KernSmith` — replace with real URL |
| C3 | Important | **Enable XML doc generation** | Add `<GenerateDocumentationFile>true</GenerateDocumentationFile>` — consumers get no IntelliSense without this |
| C4 | Important | **Add SourceLink** | Add `Microsoft.SourceLink.GitHub` package + `<Deterministic>true</Deterministic>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>` |
| C5 | Important | **Add Copyright** | `<Copyright>Copyright (c) 2024-2026 KernSmith contributors</Copyright>` |
| C6 | Important | **Create CHANGELOG.md** | At minimum, entry for current version (0.8.0) |
| C7 | Important | **Add package icon** | Create or source a package icon, set `<PackageIcon>` in csproj |
| C8 | Important | **Add PackageReleaseNotes** | Either inline or point to CHANGELOG |
| C9 | Nice | **Add README badges** | CI status, NuGet version, license badge |
| C10 | Nice | **Set ContinuousIntegrationBuild in publish workflow** | `-p:ContinuousIntegrationBuild=true` in pack step |
| C11 | Nice | **Create CONTRIBUTING.md** | Useful if expecting external contributions |

---

## Track D — FT_Stroker Compositing Fix (Optional)

> Previously tracked as standalone plan `03-ft-stroker-fix.md`. The EDT-based outline is production-quality, so this is a quality improvement, not a blocker.

### Problem

The FT_Stroker path (`CompositeWithFtStroker` in `BmFont.cs`) is implemented but disabled (`useFtStroker = false` at line 144) due to compositing issues with post-processor effects.

### Root Causes (need investigation)

| # | Issue | Details |
|---|-------|---------|
| D1 | Silent failure swallowing | Bare `catch` at line 637-640 returns unoutlined glyph with no indication. Mixed outlined/non-outlined glyphs cause atlas overlap. |
| D2 | Offset calculation mismatch | `offsetX`/`offsetY` calculation assumes matching coordinate systems between FT_Stroker and EDT metrics. May not hold. |
| D3 | Advance not adjusted | `horiAdvance` comes from original glyph, not adjusted for outline expansion. Affects character spacing. |
| D4 | Glyph type limitations | Composite glyphs, bitmap-only fonts, bold/italic may need special handling. |

### Tasks

| # | Task | Effort |
|---|------|--------|
| D1 | Add diagnostic logging to `CompositeWithFtStroker` — print metrics for outline vs glyph | Small |
| D2 | Test `RasterizeOutline` in isolation — render single glyph, inspect bitmap | Small |
| D3 | Fix silent catch — fall back to EDT per-glyph instead of returning unoutlined glyph | Medium |
| D4 | Verify offset calculation with real FT_Stroker output metrics | Medium |
| D5 | Fix advance adjustment for outlined glyphs | Small |
| D6 | Test with composite glyphs, bitmap fonts, bold/italic | Medium |
| D7 | Integration test comparing FT_Stroker vs EDT output dimensions | Medium |
| D8 | Re-enable FT_Stroker path with proper feature flag | Small |

### Re-enable Criteria

1. Tasks D1-D5 complete
2. FT_Stroker output matches EDT output for at least ASCII glyphs
3. Gradient + outline combo produces correct results
4. No silent failures for standard Latin fonts

### Files

| File | Role |
|------|------|
| `src/KernSmith/KernSmith.cs:144` | `useFtStroker = false` — the disable line |
| `src/KernSmith/KernSmith.cs:548-646` | `CompositeWithFtStroker()` — compositing logic |
| `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs:214-341` | `RasterizeOutline()` — stroker rasterization |
| `src/KernSmith/Rasterizer/FreeTypeNative.cs` | P/Invoke bindings for FT_Stroker |

---

## Track E — API & Documentation Polish

| # | Item | Details |
|---|------|---------|
| E1 | Document thread safety | `IRasterizer` instances must not be shared across threads — add XML doc comments |
| E2 | Document `BmFont.Load` path ownership | Caller-provided rasterizer contract: if `Generate` throws after `LoadFont`, rasterizer is in loaded state |
| E3 | Consider sealing public types | `FontGeneratorOptions`, `CharacterSet` — prevents unexpected subclassing |
| E4 | `RasterizedGlyph.BitmapData` mutability | Consider `ReadOnlyMemory<byte>` or document that callers should not mutate |
| E5 | Add .editorconfig | Code style enforcement for contributors (also needed by Phase 11 solution restructure) |

---

## Suggested Execution Order

1. **Track C (C1-C2)** — Critical blockers: LICENSE file and URL fix (minutes of work)
2. **Track A (A1-A5)** — High/medium security fixes (can be done in parallel with Track B)
3. **Track B (B1, B5)** — Input validation guards + code quality fixes
4. **Track C (C3-C8)** — Important package metadata
5. **Track B (B2-B3)** — Fill critical test gaps
6. **Track E** — Documentation polish
7. **Track A (A6-A10)** — Low-severity security hardening
8. **Track B (B4)** — Edge case tests
9. **Track D** — FT_Stroker fix (optional, do if time permits)
10. **Track C (C9-C11)** — Nice-to-have packaging items

---

## Estimated Effort

| Track | Effort | Notes |
|-------|--------|-------|
| A — Security | 1-2 days | Mostly small targeted fixes |
| B — Tests | 2-3 days | ~30 new test cases across 8 test files |
| C — NuGet | 1 day | Metadata, LICENSE, CHANGELOG, icon |
| D — FT_Stroker | 2-3 days | Investigation-heavy, optional |
| E — API/Docs | 0.5 day | XML doc comments, sealing |
| **Total** | **5-7 days** (without Track D) | Track D adds 2-3 days if pursued |
