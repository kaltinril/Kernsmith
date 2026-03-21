# KernSmith

## Project Purpose

Cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF/OTF/WOFF files. Combines FreeTypeSharp for rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont .fnt + .png/.tga/.dds pairs. Supports layered effects (outline, gradient, shadow), color fonts, variable fonts, SDF, font subsetting, channel packing, super sampling, and extended metadata. In-memory by default.

## Project Organization

| Folder | Purpose |
|--------|---------|
| `src/KernSmith/` | **Main library** — the NuGet package |
| `tests/KernSmith.Tests/` | **xUnit + FluentAssertions test suite** |
| `tools/KernSmith.Cli/` | **CLI tool** for bitmap font generation |
| `samples/KernSmith.Samples/` | **Usage examples** |
| `benchmarks/KernSmith.Benchmarks/` | **BenchmarkDotNet performance benchmarks** |
| `apps/` | **Future app projects** — Ui, Web, Mobile (placeholders) |
| `plan/` | **Technical plan docs** — active plans; completed plans archived in `plan/done/` |
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
4. **Test with real data** — test font is at `tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf`

### Key Conventions

- **Language**: C# / .NET 10.0
- **Nullable**: enabled
- **Unsafe**: allowed only in FreeType interop (`FreeTypeRasterizer.cs`, `FreeTypeNative.cs`)
- **Testing**: xUnit + FluentAssertions
- **Dependencies**: FreeTypeSharp 3.1.0, StbImageWriteSharp 1.16.7
- **License**: Proprietary (see LICENSE)

### Namespace Rules

- `KernSmith` (root): entry point, config types, exceptions, enums
- `KernSmith.Font`: font reading, TTF parsing
- `KernSmith.Font.Models`: FontInfo, KerningPair, GlyphMetrics
- `KernSmith.Font.Tables`: HeadTable, HheaTable, Os2Metrics, NameInfo
- `KernSmith.Rasterizer`: IRasterizer, FreeTypeRasterizer, post-processors, effects (IGlyphEffect), GlyphCompositor
- `KernSmith.Atlas`: IAtlasPacker, packers, encoders (PNG/TGA/DDS), AtlasBuilder, AtlasSizeEstimator, ChannelCompositor
- `KernSmith.Output`: formatters, FileWriter, BmFontResult, BmFontReader, BmFontModelBuilder
- `KernSmith.Output.Model`: BmFontModel, InfoBlock, CommonBlock, ExtendedMetadata, etc.
- Files in `Config/` and `Exceptions/` use the ROOT `KernSmith` namespace

### Project File References

| What | Location |
|------|----------|
| Entry point | `src/KernSmith/KernSmith.cs` |
| Plan docs | `plan/` (start with `master-plan.md`) |
| Data types (source of truth) | `plan/done/plan-data-types.md` |
| Implementation order | `plan/done/plan-implementation-order.md` |
| Tests | `tests/KernSmith.Tests/` |
| CI/CD | `.github/workflows/` |
