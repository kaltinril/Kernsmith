# Phase 96: Documentation Audit

**Status:** In Progress
**Date:** 2026-04-03
**Branch:** phase/95-performance-and-bugs (implemented on current branch)

## Overview

Comprehensive audit of all project documentation — plan docs, reference docs, root-level docs, CLAUDE.md, CI/CD workflows, and test documentation. Cross-referenced claims against actual codebase to identify stale, missing, and inaccurate information.

## Implementation Status

### Completed (11 files changed)
- [x] **CLAUDE.md** — Fixed entry point path, expanded project structure table, corrected FreeTypeRasterizer location, updated target framework, fixed unsafe code reference, corrected dependencies, documented singular/plural namespace convention (C1-C5, H6, M1)
- [x] **README.md** — Removed false WOFF2 support claim (C6)
- [x] **COMPARISON.md** — Fixed WOFF2 status to unsupported, updated GUI status to supported (C7, H1)
- [x] **plan-project-structure.md** — Fixed exception names/namespaces, removed stale Packing folder, updated target framework, replaced FluentAssertions with Shouldly (C9)
- [x] **plan-api-design.md** — Marked Stream overload as not implemented (C10)
- [x] **phase-17-rebrand-kernsmith.md** — Updated status to "Partial" with explanation of what was deferred (H4)
- [x] **master-plan.md** — Fixed Phase 20 status contradiction (H8)
- [x] **REF-12-rasterizer-backends.md** — Added complete StbTrueType backend documentation (H2)
- [x] **REF-11-wasm-restrictions.md** — Updated pioneer claim to reflect proven WASM support (H3)
- [x] **REF-07-gum-forms-cheatsheet.md** — Added clarifying note about third-party scope (M3)
- [x] **BmFontException.cs** — Renamed to BmFontException.cs via git mv (M9)

### Validated but not implemented (needs future work)
- [ ] **H5** — CI framework matrix only tests .NET 10; net8.0 untested (works by luck on current runners)
- [ ] **M7** — No code quality/linting checks in CI
- [ ] **M8** — No dependency vulnerability scanning
- [ ] **M10** — Config classes lack dedicated unit tests (indirect coverage exists)
- [ ] **L7** — RobotoFlex-Variable.ttf unused in tests (only benchmarks)
- [ ] **L9** — RELEASING.md doesn't mention FnaGum exclusion from publish

### Not yet addressed
- [ ] **H7** — No formal API reference documentation
- [ ] **M2** — CHANGELOG phase references confuse external users
- [ ] **M4** — REF-12 DirectWrite stub status needs verification
- [ ] **M5** — REF-08 unclear KernSmith vs BMFont distinction
- [ ] **M6** — plan-rasterization.md doesn't acknowledge plugin architecture
- [ ] **M11** — Author attribution inconsistency
- [ ] **L1-L6, L8, L10** — Low-priority doc polish items

### Not an issue (validated as already correct)
- [x] **C8** — CHANGELOG version matches Directory.Build.props (both 0.12.1)

---

## CRITICAL Issues (Must Fix)

### C1. CLAUDE.md — Entry point path wrong
- Entry point listed as `src/KernSmith/KernSmith.cs` — **file doesn't exist**
- Actual entry points are `BmFont.cs` and `BmFontBuilder.cs`
- Fix: Update the "Project File References" table

### C2. CLAUDE.md — Project structure table incomplete
- Table only lists `src/KernSmith/` under src
- Missing 4 rasterizer projects: `KernSmith.Rasterizers.FreeType`, `KernSmith.Rasterizers.Gdi`, `KernSmith.Rasterizers.DirectWrite.TerraFX`, `KernSmith.Rasterizers.StbTrueType`
- Missing `integrations/` folder: `KernSmith.FnaGum`, `KernSmith.GumCommon`, `KernSmith.KniGum`, `KernSmith.MonoGameGum`
- Additional samples not mentioned: `KernSmith.Rasterizer.Example`, `KernSmith.Samples.BlazorWasm`
- Additional test subdirs not mentioned: `bmfont-compare`, `manual_testing`, `output`

### C3. CLAUDE.md — FreeTypeRasterizer location wrong
- Listed in `KernSmith.Rasterizer` namespace description as part of core library
- Actually lives in separate `src/KernSmith.Rasterizers.FreeType/` project since Phase 78

### C4. CLAUDE.md — Target framework incomplete
- Says ".NET 10.0" but project multi-targets `net8.0;net10.0` per `Directory.Build.props`

### C5. CLAUDE.md — Unsafe code reference stale
- References `FreeTypeRasterizer.cs` and `FreeTypeNative.cs` as core library files
- Both are in the separate `KernSmith.Rasterizers.FreeType` project now

### C6. README.md — False WOFF2 support claim
- Line 13 states "WOFF, and WOFF2 input" under features
- `WoffDecompressor.cs` line 49-50 throws `NotSupportedException` for WOFF2
- Fix: Remove WOFF2 from feature list or move to "Planned Features" section

### C7. COMPARISON.md — WOFF2 status wrong
- WOFF2 marked as "coming soon" (line 34)
- Should be marked as not supported since it throws at runtime

### C8. CHANGELOG.md — Version mismatch
- Latest entry is [0.12.1] dated 2026-04-02
- Git history shows 0.12.2 was merged (commit `3be20aa` "Bump version to 0.12.2", PR #56)
- Fix: Add 0.12.2 section or confirm this was a local-only bump

### C9. plan-project-structure.md — Multiple stale references
- Exception class name wrong: documents `KernSmithException`, actual is `BmFontException`
- Exception namespace wrong: documents `KernSmith.Exceptions`, actual is root `KernSmith`
- References `Packing/` folder that's actually `Atlas/`
- Documents `KernSmith.Packing` namespace that doesn't exist (it's `KernSmith.Atlas`)
- Target framework outdated: says `net8.0`, should be `net8.0;net10.0`
- Still references FluentAssertions (replaced by Shouldly in Phase 79)

### C10. plan-api-design.md — Phantom Stream overload
- Line 191 documents `Generate(Stream fontStream, FontGeneratorOptions options)`
- This overload was never implemented
- Actual overloads: `Generate(byte[], ...)`, `Generate(string, ...)`, `GenerateFromSystem(string, ...)`
- Fix: Remove or implement

---

## HIGH Issues

### H1. README/COMPARISON — GUI status misleading
- Both show GUI as "coming soon"
- `apps/KernSmith.Ui/` exists and is active with recent commits
- Fix: Update to reflect actual state (alpha/beta/available)

### H2. REF-12-rasterizer-backends.md — Missing StbTrueType backend
- Documents FreeType, GDI, and DirectWrite backends only
- StbTrueType (pure C#, WASM/AOT) is completely missing
- README already describes it; reference doc needs update

### H3. REF-11-wasm-restrictions.md — Outdated pioneer claim
- States "No community validation of StbTrueTypeSharp in Blazor WASM — KernSmith would be a pioneer"
- Blazor WASM sample exists and works (`samples/KernSmith.Samples.BlazorWasm/`)
- Fix: Update to reflect that WASM support is proven

### H4. phase-17-rebrand-kernsmith.md — Marked Complete but partially done
- Status says "Complete"
- Namespace rebrand was done (Bmfontier.* -> KernSmith.*)
- Type-name rebrand was NOT done (BmFont, BmFontBuilder, BmFontResult, BmFontModel all unchanged)
- Fix: Update status to "Partial" and document what was deferred and why

### H5. CI/CD — Framework matrix mismatch
- All workflows (ci.yml, publish.yml, benchmark.yml) only test .NET 10
- Projects target net8.0 and net10.0; MonoGameGum targets net8.0 and net9.0
- net8.0 and net9.0 binaries are built but never validated in CI
- Fix: Add net8.0 to CI test matrix

### H6. CLAUDE.md — FreeTypeSharp listed as core dependency
- Listed under Dependencies as `FreeTypeSharp 3.1.0`
- It's only a dependency of the separate `KernSmith.Rasterizers.FreeType` package
- Core library depends on `StbImageSharp` and `StbImageWriteSharp`

### H7. Reference docs — No formal API reference
- Reference directory is all research/background documentation
- No documentation for: `BmFont.Builder()` fluent API, `FontGeneratorOptions` properties, `BmFontModel` structure, exception types and when they're thrown
- Users must read source code to understand the API

### H8. master-plan.md — Phase 20 status contradiction
- master-plan.md lists Phase 20 as "Complete"
- phase-20-release-readiness.md line 3 says "Status: Planning"

---

## MEDIUM Issues

### M1. CLAUDE.md — Namespace singular vs plural undocumented
- Core uses `KernSmith.Rasterizer` (singular)
- Plugin packages use `KernSmith.Rasterizers.*` (plural)
- This distinction is not documented

### M2. CHANGELOG.md — Phase references confuse external users
- Entries reference internal phase numbers (Phase 33b, 37, 78D, 78G, etc.)
- External users have no context for what phases mean
- Fix: Remove phase references or add a legend

### M3. REF-07-gum-forms-cheatsheet.md — Scope mismatch
- Third-party GUM UI documentation from flatredball.com
- Doesn't belong in core `reference/` directory
- Fix: Move to `docs/ui/` or `apps/` subdirectory

### M4. REF-12 — DirectWrite stub status unclear
- States "Color fonts and variable fonts are stubbed but not yet implemented"
- Needs verification against current DirectWrite rasterizer code

### M5. REF-08-bmfont-internals.md — Unclear KernSmith vs BMFont distinction
- Documents BMFont's internal algorithms (GDI rendering, AddOutline, Windows metrics)
- Unclear which behaviors KernSmith replicates vs. which are just reference
- Fix: Add annotations clarifying what KernSmith implements

### M6. plan-rasterization.md — Doesn't acknowledge plugin architecture
- References FreeTypeRasterizer as default implementation in core
- Doesn't mention Phase 78 moved it to a plugin package

### M7. CI/CD — No code quality checks
- No linting, formatting (`dotnet format`), or static analysis in CI
- No CodeQL, SonarQube, or similar

### M8. CI/CD — No dependency vulnerability scanning
- No Dependabot alerts configured in workflows
- No SBOM generation for supply chain security

### M9. File naming — Old project name in exception file
- `BmFontException.cs` still has old "bmfontier" name
- Contains `BmFontException` class
- Fix: Rename file to `BmFontException.cs`

### M10. Tests — Config classes lack dedicated unit tests
- 23 classes in `src/KernSmith/Config/` with no dedicated tests
- Includes: `BmfcConfigReader`, `BmfcConfigWriter`, `FontGeneratorOptions`, `ChannelConfig`, `Padding`, `Spacing`, `BatchOptions`, `BatchJob`, `PipelineMetrics`, `FontCache`

### M11. README/COMPARISON — Author attribution inconsistency
- `Directory.Build.props` defaults to "KernSmith contributors"
- Individual .csproj files override to "Kaltinril (Jeremy Swartwood)"
- Not documented which is canonical

---

## LOW Issues

### L1. README — Custom glyphs feature undocumented
- Feature list mentions "Custom glyphs" but no usage examples provided
- `CustomGlyphs` property exists on options but is discoverable only via source code

### L2. REF-01 — FreeTypeSharp version/download metrics may be stale
- Document dated 2026-03-18, states "latest v3.1.0 (Feb 2026)"
- NuGet download counts (~84,200) are point-in-time snapshots

### L3. REF-09 — ppem formula needs code verification
- Formula: `effectivePpem = fontSize * unitsPerEm / (usWinAscent + usWinDescent)`
- Should be verified against actual implementation

### L4. Tests — DefaultSystemFontProvider untested
- No tests for system font discovery (platform-dependent, harder to test in CI)

### L5. Tests — Exception classes untested
- `AtlasPackingException`, `BmFontException`, `FontParsingException`, `RasterizationException` have no scenario tests showing when each is thrown

### L6. Tests — Post-processor effects lack unit tests
- `OutlinePostProcessor`, `ShadowPostProcessor`, `GradientPostProcessor`, `BoldPostProcessor`, `ItalicPostProcessor`, `HeightStretchPostProcessor` only tested via integration
- Current regression baseline approach covers behavior but no isolated unit tests

### L7. Tests — RobotoFlex-Variable.ttf fixture unused
- Fixture exists in `tests/KernSmith.Tests/Fixtures/` but isn't explicitly referenced in any test

### L8. CI/CD — Publish workflow has fragile release notes
- Hardcoded package links in GitHub Release template
- Will break if packages are added or removed

### L9. RELEASING.md — FnaGum exclusion undocumented
- `publish.yml` explicitly excludes `KernSmith.FnaGum` with comment "until build issues are resolved"
- RELEASING.md doesn't mention this exclusion

### L10. Directory.Build.props — No documentation comment
- No XML comment explaining this file is the version source of truth
- New developers may not understand its role

---

## Positive Findings

- **Test suite is excellent** — ~87% estimated coverage, consistent `Method_Scenario_Expected` naming, well-organized by concern, 114 CLI tests
- **CLI docs are thorough** — 664 lines in README, all flags documented and cross-referenced with tests
- **Samples are accurate** — Working code matching documented API patterns
- **RELEASING.md** aligns well with actual CI/CD workflow behavior
- **All file paths in CI workflows** are correct
- **No obsolete tests** found — suite is actively maintained
- **Shouldly migration** fully complete — zero FluentAssertions references remain in code
- **Fixture strategy is solid** — Multiple font types (regular, color emoji, variable) for different testing needs
- **LICENSE** is correct MIT with proper attribution

---

## Suggested Fix Order

1. **CLAUDE.md fixes** (C1-C5, H6, M1) — highest impact, daily working reference
2. **README/COMPARISON fixes** (C6, C7, H1) — public-facing, user trust
3. **CHANGELOG update** (C8) — version consistency
4. **Plan doc corrections** (C9, C10, H4, H8) — developer onboarding
5. **Reference doc updates** (H2, H3, M3-M5) — accuracy for research
6. **CI/CD improvements** (H5, M7, M8) — build reliability
7. **File renames and cleanup** (M9) — consistency
8. **Test coverage gaps** (M10, L4-L7) — quality
9. **Low-priority doc polish** (L1-L3, L8-L10) — nice to have
