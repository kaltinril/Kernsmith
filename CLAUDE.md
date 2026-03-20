# bmfontier

## Project Purpose

Cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF/OTF/WOFF files. Combines FreeTypeSharp for rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont .fnt + .png pairs. In-memory by default.

## Project Organization

| Folder | Purpose |
|--------|---------|
| `src/Bmfontier/` | **Main library** — the NuGet package |
| `tests/Bmfontier.Tests/` | **xUnit + FluentAssertions test suite** |
| `samples/Bmfontier.Cli/` | **Reference CLI tool** |
| `benchmarks/Bmfontier.Benchmarks/` | **BenchmarkDotNet performance benchmarks** |
| `plan/` | **Technical plan docs** — architecture, data types, implementation order |
| `reference/` | **Reference docs** — TTF spec, BMFont format, algorithm research |

## Context Management

- **NEVER read large doc/plan files in the main context window.** Delegate to agents.
- **Multi-file edits MUST go to coder agents.** Main context is for orchestration only.
- **Batch doc updates into a single agent call.**
- **Why**: Reading 6+ large markdown files inline causes context compaction.

## Agent Instructions

### When Working on This Project

1. **Read plan docs first** — `plan/done/plan-data-types.md` is the single source of truth for types and interfaces
2. **Follow existing patterns** — check 2-3 nearby files before writing new code
3. **Never hardcode credentials** — use environment variables or `.env` + appropriate library
4. **Test with real data** — test font is at `tests/Bmfontier.Tests/Fixtures/Roboto-Regular.ttf`

### Key Conventions

- **Language**: C# / .NET 8.0 (LTS)
- **Nullable**: enabled
- **Unsafe**: allowed only in FreeType interop (`FreeTypeRasterizer.cs`)
- **Testing**: xUnit + FluentAssertions
- **Dependencies**: FreeTypeSharp 3.1.0, StbImageWriteSharp 1.16.7
- **License**: Proprietary (see LICENSE)

### Namespace Rules

- `Bmfontier` (root): entry point, config types, exceptions, enums
- `Bmfontier.Font`: font reading, TTF parsing
- `Bmfontier.Font.Models`: FontInfo, KerningPair, GlyphMetrics
- `Bmfontier.Font.Tables`: HeadTable, HheaTable, Os2Metrics, NameInfo
- `Bmfontier.Rasterizer`: IRasterizer, FreeTypeRasterizer, post-processors
- `Bmfontier.Atlas`: IAtlasPacker, packers, encoder, AtlasBuilder
- `Bmfontier.Output`: formatters, FileWriter, BmFontResult
- `Bmfontier.Output.Model`: BmFontModel, InfoBlock, CommonBlock, etc.
- Files in `Config/` and `Exceptions/` use the ROOT `Bmfontier` namespace

### Project File References

| What | Location |
|------|----------|
| Entry point | `src/Bmfontier/BmFont.cs` |
| Plan docs | `plan/` (start with `master-plan.md`) |
| Data types (source of truth) | `plan/plan-data-types.md` |
| Implementation order | `plan/plan-implementation-order.md` |
| Tests | `tests/Bmfontier.Tests/` |
| CI/CD | `.github/workflows/` |
