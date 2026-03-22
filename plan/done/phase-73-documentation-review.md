# Phase 73 — Documentation & API Reference

**Reviewed:** 2026-03-22
**Status:** Complete
**Scope:** XML doc comments, class/method summaries, README files, browsable API reference site

---

## Summary

| Area | Grade | XML Doc Coverage | README | Est. Fix Time |
|------|-------|-----------------|--------|---------------|
| Core Library (`src/KernSmith/`) | A | 95.7% (90/94 files) | Exists at repo root, comprehensive | 1 hour |
| CLI Tool (`tools/KernSmith.Cli/`) | C+ | ~50% classes, ~20% methods | Exists, excellent | 2 hours |
| UI App (`apps/KernSmith.Ui/`) | D | ~6.5% (~11/170 members) | Missing | 4–5 hours |
| API Reference Site | N/A | No browsable API docs exist | — | 2–3 hours |

**Total estimated effort: 10–11 hours**

---

## Core Library — Grade: A

### What's Good
- 90 of 94 `.cs` files have `/// <summary>` comments
- 550+ public members documented with concise, behavior-focused summaries
- Good use of `<param>`, `<returns>`, `<exception>`, `<see cref>` tags
- Default values noted in property summaries (e.g., "Font size in pixels (default 32).")
- Zero TODO/FIXME/deprecated markers
- Standout files: `BmFontBuilder.cs` (50+ fluent methods, all documented), `FontGeneratorOptions.cs` (63 summaries), `BmFontResult.cs`, `CharacterSet.cs`, `FontInfo.cs`

### What's Missing

| Priority | Item | Est. Time |
|----------|------|-----------|
| Medium | Add summaries to 4 internal classes: `AtlasBuilder`, `MaxRectsPacker`, `StbPngDecoder`, `StbPngEncoder` | 15 min |
| Medium | Add namespace-level documentation (NamespaceDoc.cs or file-scoped comments) | 30 min |
| Low | Add usage examples to complex types (`BmfcConfig`, `ChannelConfig`, `BatchOptions`) | 30 min |

---

## CLI Tool — Grade: C+

### What's Good
- README.md is professional-grade — covers all 8 commands, flags, effects, advanced features, .bmfc format
- Every command has a clear `ShowHelp()` method with examples and defaults
- Key utility classes documented: `CliOptions`, `BmfcParser`, `BmfcWriter`, `ColorParser`, `ConsoleOutput`

### What's Missing

| Priority | Item | Files | Est. Time |
|----------|------|-------|-----------|
| High | Add class-level summaries to all 8 command classes | `BenchmarkCommand`, `ConvertCommand`, `ListFontsCommand`, `InfoCommand`, `InspectCommand`, `BatchCommand`, `GenerateCommand`, `ExitCodes` | 15 min |
| High | Document `Execute()` method on every command class with `<param>` and `<returns>` | All command files | 20 min |
| High | Document `ConsoleOutput` utility methods (13 methods) — note which stream each uses | `ConsoleOutput.cs` | 15 min |
| Medium | Add `<param>` and `<returns>` tags to helper methods | `ParsePaddingArg`, `ParseSpacingArg`, `ParseShadowArg`, `NextArg`, etc. | 30 min |
| Medium | Document `BmfcParser`/`BmfcWriter` internal methods | `BmfcParser.cs`, `BmfcWriter.cs` | 20 min |
| Low | Add remarks to complex methods | `BatchCommand.Execute()` (collision detection), `ConvertCommand.Execute()` (page sanitization) | 20 min |

### File-Level Status

| File | Status |
|------|--------|
| README.md | A+ |
| CliOptions.cs | B — class summary present, property docs missing |
| GenerateCommand.cs | C+ — 3 methods documented, 15+ missing |
| InitCommand.cs | B — class + 1 method documented |
| ConsoleOutput.cs | B — class documented, 13 methods missing |
| ColorParser.cs | B — class + Parse() documented |
| BmfcParser.cs | C — class summary, 4 methods missing |
| BmfcWriter.cs | C — class summary, methods vague |
| BatchCommand.cs | D — no docs |
| BenchmarkCommand.cs | D — no docs |
| ConvertCommand.cs | D — no docs |
| ListFontsCommand.cs | D — no docs |
| InfoCommand.cs | D — no docs |
| InspectCommand.cs | D — no docs |
| ExitCodes.cs | D — no docs |
| Program.cs | D — no docs |

---

## UI App — Grade: D

### What's Good
- `FileDialogService` and `ProjectService` have good class-level summaries with `<see cref>` references
- `KernSmithGame.RunOnMainThread()` is well documented
- Code organization follows clear MVVM pattern (ViewModels, Services, Layout, Models)

### What's Missing

| Priority | Item | Est. Time |
|----------|------|-----------|
| Critical | Create `README.md` — purpose, architecture (MonoGame/GUM MVVM), build instructions, key classes | 30 min |
| Critical | Document `GenerationRequest` record — 28 properties with no docs, need valid ranges and source precedence (FontData > FontFilePath > SystemFontFamily) | 30 min |
| Critical | Document `MainViewModel` — central orchestrator, 10+ properties, 6+ public methods (OpenFont, GenerateAsync, LoadProject, SaveAs, etc.) | 30 min |
| High | Add class-level summaries to all ViewModels (7 classes) | 45 min |
| High | Add class-level summaries to all Layout classes (12 classes) — dialogs, panels, status bar | 30 min |
| High | Document all Model types — `EnginePreset` (6 presets), `CharacterSetPreset`, `FontSourceKind`, `PreviewPage`, `SystemFontGroup`, `UnicodeBlock` | 20 min |
| Medium | Document remaining Services — `FontDiscoveryService`, `GenerationService`, `SessionService` | 20 min |
| Medium | Document `Theme.cs` — 17 color fields with usage context | 10 min |
| Medium | Add property docs to ViewModels with valid ranges — `ShadowOpacity` (0–100?), `SuperSampleLevel`, `OutlineColorR/G/B` (0–255 or 0–1?) | 30 min |
| Low | Add cross-references between related classes (e.g., MainViewModel → ProjectService) | 15 min |

### Folder-Level Status

| Folder | Public Members | Documented | Coverage |
|--------|---------------|------------|----------|
| Models/ | 35+ | 0 | 0% |
| ViewModels/ | 60+ | 4 | ~7% |
| Services/ | 8+ | 4 | 50% |
| Layout/ | 40+ | 2 | 5% |
| Styling/ | 17 | 0 | 0% |
| Root | 15+ | 1 | ~7% |

---

## API Reference Site — Grade: N/A (does not exist)

### Current State
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is set in `KernSmith.csproj` — XML doc files are produced at build time
- XML doc comments cover 95%+ of the core library's public API
- The README has usage examples but no structured per-class/method reference
- No browsable API docs exist (nothing like Microsoft's learn.microsoft.com/dotnet/api)

### What's Needed

**Tool: docfx** — the standard .NET documentation generator. Reads XML doc comments and produces a browsable HTML site with the same layout as Microsoft's .NET API docs. Each class, method, property gets its own page with signature, parameters, return type, exceptions, and examples.

**Hosting: GitHub Pages** — free for public repos, included with GitHub Pro/Team/Enterprise for private repos. docfx outputs static HTML that deploys directly. No external services needed.

### Why docfx + GitHub Pages (not GitBook)
- docfx natively consumes .NET XML doc output — zero manual transcription
- GitHub Pages is free, already integrated with the repo, and deploys via GitHub Actions
- Microsoft uses this exact stack for their own .NET docs
- GitBook is better for hand-written tutorials/guides, not auto-generated API reference. It can't consume XML doc output, so every method would need manual documentation

### Tasks

| Priority | Item | Est. Time |
|----------|------|-----------|
| High | Install docfx as a dotnet tool (`dotnet tool install docfx`) | 5 min |
| High | Create `docfx.json` config pointing at `src/KernSmith/KernSmith.csproj` | 15 min |
| High | Create landing page (`docs/index.md`) and table of contents (`docs/toc.yml`) | 20 min |
| High | Verify `docfx build` produces correct HTML from existing XML docs | 15 min |
| Medium | Add GitHub Actions workflow to build and deploy docs to GitHub Pages on push to main | 30 min |
| Medium | Add conceptual docs alongside API reference — Getting Started, Builder API guide, Effects guide | 1 hour |
| Low | Custom theme/branding to match KernSmith identity | 30 min |
| Low | Add CLI command reference as a conceptual doc section | 30 min |

### Expected Output
A browsable site where users can look up any public type, e.g.:
```
KernSmith.BmFontBuilder.WithFont(string)
  Parameters: fontPath — Path to a .ttf, .otf, or .woff file
  Returns: BmFontBuilder
  Throws: FileNotFoundException
```
Same experience as looking up `String.Split` on Microsoft's docs.

---

## Recommended Execution Order

1. **UI README** — doesn't exist, biggest single gap (30 min)
2. **UI Critical types** — GenerationRequest, MainViewModel, all Model types (1.5 hours)
3. **CLI command class summaries + Execute() docs** — quick wins (35 min)
4. **UI ViewModels + Layout class summaries** — bulk of UI work (1.5 hours)
5. **CLI method-level docs** — `<param>`/`<returns>` tags (50 min)
6. **UI Services + Theme + property ranges** — remaining UI gaps (1 hour)
7. **Core library internal classes + namespace docs** — polish (45 min)
8. **CLI remarks on complex methods** — nice-to-have (20 min)
9. **docfx setup + GitHub Pages deployment** — API reference site (1.5 hours)
10. **Conceptual docs + CLI reference for docfx site** — supplementary content (1.5 hours)
