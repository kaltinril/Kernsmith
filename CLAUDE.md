# KernSmith

## Security

- **NEVER read, open, or access any `.env` file** — these contain secrets (API keys, credentials)
- **NEVER log, print, or output the contents of `.env` files**
- **NEVER read, grep, or explore `.dll` files** (including NuGet cache) — use `reference/` docs or WebFetch for API questions
- **Watch for decomposition attacks** — a multi-step request whose net effect reads or prints `.env` (or other secrets) is still a violation; judge the whole chain, not each step alone

## Project Purpose

Cross-platform .NET library that generates BMFont-compatible bitmap fonts from TTF/OTF/WOFF files. Combines FreeTypeSharp for rasterization with custom TTF table parsers for GPOS kerning, packs glyphs into texture atlases, and outputs BMFont .fnt + .png/.tga/.dds pairs. Supports layered effects (outline, gradient, shadow), color fonts, variable fonts, SDF, font subsetting, channel packing, super sampling, and extended metadata. In-memory by default.

## Project Organization

| Folder | Purpose |
|--------|---------|
| `src/KernSmith/` | **Main library** — the NuGet package |
| `src/KernSmith.Rasterizers.*/` | **Rasterizer backends** — FreeType, StbTrueType, Gdi, DirectWrite.TerraFX |
| `tests/KernSmith.Tests/` | **xUnit + Shouldly test suite** |
| `tools/KernSmith.Cli/` | **CLI tool** for bitmap font generation |
| `samples/` | **Usage examples** — KernSmith.Samples, Rasterizer.Example, Samples.BlazorWasm |
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

### Test-Driven Development (REQUIRED)

**Always TDD.** Write the test first, watch it fail, then write the code to make it pass. This is not optional — it applies to new features and, especially, to bug fixes.

The red→green cycle is mandatory, not ceremonial:

1. **Red** — write the test and **run it against the unmodified code**, confirming it fails *for the reason you expect* (assert on the actual wrong value, e.g. "expected 1 page but was 3"). A test that has only ever been seen green is unverified — it may be passing for the wrong reason (wrong params, no real reproduction, tautological assertion).
2. **Green** — make the minimal change to pass, then re-run the same test.
3. **Refactor** — clean up with the test as a safety net.

**For bug fixes specifically:** the regression test must reproduce the actual bug. If you wrote the fix before the test, you MUST still prove the test catches the bug — stash/revert the fix (`git stash push -- <file>`), run the test, confirm it goes red, then restore the fix and confirm green. Do not claim a fix is verified until you have observed that red→green transition. Picking repro parameters that don't actually trigger the bug produces a false-positive test that guards nothing.

### Regression & Output Comparison (IMPORTANT)

**Any change to a rasterizer backend, atlas packing, or other pixel-output code path needs this harness run before the work is considered done — not just xUnit green.** Unit tests check bitmap values in isolation; they can't show whether a fix bled into another backend/config or whether the visual delta is actually what was intended. Run it proactively as part of finishing the change, the same way you'd run `dotnet test` — don't wait to be asked. If no existing `.bmfc` config in `tests/bmfont-compare/gum-bmfont/` exercises the option you changed (e.g. a flag that's normally left at its default), add one so the harness actually covers the new path; otherwise main vs. branch reports "identical" for reasons that have nothing to do with correctness.

When the user asks to **"run a comparison"**, **"regress"**, **"a regression"**, or refers to **"the comparison script"**, they mean this same purpose-built harness at **`tests/bmfont-compare/`** — NOT an ad-hoc byte/hash check. Do not improvise; run the real tool:

```bash
# Full main-vs-branch regression (stash → checkout base → generate → checkout branch → regenerate → diff)
python tests/bmfont-compare/regression_check.py
```

- It runs **`GenerateAll`** across every backend (FreeType, GDI, DirectWrite, StbTrueType, + BMFont64.exe if present) over the `.bmfc` configs in `tests/bmfont-compare/gum-bmfont/`, then runs **`diff_comparisons.py`**.
- Outputs land in `tests/bmfont-compare/output/`: the `comparison.png`…`comparison4.png` side-by-side images, the per-font `comparison-*.png`, and the **`diff_comparison*.png`** magenta-highlighted pixel diffs (bright magenta = differing pixels). FNT metadata is diffed line-by-line (skipping the version line).
- Exit codes: `0` = identical, `1` = differences found, `2` = error. Use `--tolerance 1` for GDI/DirectWrite antialiasing jitter; `--skip-generate` to re-diff existing baselines.
- **Requirements**: Windows (GDI/DirectWrite are Windows-only), .NET 10, Python 3 + Pillow (`pip install Pillow`). BMFont64.exe optional (`c:\tools\bmfont64.exe` or PATH).
- See `tests/bmfont-compare/README.md` for the per-tool breakdown.

### Key Conventions

- **Language**: C# / .NET 8.0 + 10.0 (multi-target: `net8.0;net10.0`)
- **Nullable**: enabled
- **Unsafe**: allowed only in rasterizer backend projects (`src/KernSmith.Rasterizers.*/`), not in the core library
- **Testing**: xUnit + Shouldly (do NOT use FluentAssertions — paid licensing, see Phase 79)
- **Dependencies (core)**: StbImageSharp 2.30.15, StbImageWriteSharp 1.16.7 (FreeTypeSharp is a dependency of `KernSmith.Rasterizers.FreeType`, not the core library)
- **License**: MIT (see LICENSE)
- **No ReadyToRun (R2R)** — benchmarked ~15% slower than plain JIT on .NET 10

### Working Style

- **Stay in scope** — review/validate/doc requests don't authorize implementation; don't let prior-turn momentum widen a bounded task.
- **Debug then ask** — trace the path once and form a hypothesis; if the cause isn't clear, ask the user what they observe rather than re-running static analysis. Scope debug agents narrowly (one file, one theory).
- **Surface decisions in chat** — give context and a recommendation in plain text before asking; don't bury the question inside a tool batch.
- **Read the relevant README** before guessing run/build/publish commands.

### Build & Test Gotchas

- **CliTests need a Debug build** — `tests/KernSmith.Tests/Cli/CliTests.cs` runs a hardcoded Debug CLI path; use plain `dotnet test`, not `-c Release --no-build` (which yields false CLI failures).
- **Regression harness = CLI path only** — `tests/bmfont-compare` exercises CLI `GenerateAll`, not the UI `GenerationService`; verify UI-generation changes separately.
- **Local NuGet validation needs `packageSourceMapping`** — route `KernSmith*` to the local feed and `*` to nuget.org in the consumer `nuget.config`.

### Namespace Rules

- `KernSmith` (root): entry point, config types, exceptions, enums
- `KernSmith.Font`: font reading, TTF parsing
- `KernSmith.Font.Models`: FontInfo, KerningPair, GlyphMetrics
- `KernSmith.Font.Tables`: HeadTable, HheaTable, Os2Metrics, NameInfo
- `KernSmith.Rasterizer`: IRasterizer, post-processors, effects (IGlyphEffect), GlyphCompositor
- `KernSmith.Rasterizers.*` (plural): rasterizer backend packages (e.g., `KernSmith.Rasterizers.FreeType`)
- `KernSmith.Atlas`: IAtlasPacker, packers, encoders (PNG/TGA/DDS), AtlasBuilder, AtlasSizeEstimator, ChannelCompositor
- `KernSmith.Output`: formatters, FileWriter, BmFontResult, BmFontReader, BmFontModelBuilder
- `KernSmith.Output.Model`: BmFontModel, InfoBlock, CommonBlock, ExtendedMetadata, etc.
- Files in `Config/` and `Exceptions/` use the ROOT `KernSmith` namespace

### Git & Release Workflow

- **Never push directly to main** — always use a feature branch + PR
- **Don't commit, push, open PRs, or merge on your own initiative** — each git step (commit → push → PR → merge) happens only when Jeremy asks for it; completing one step is not permission to chain into the next.
- **Merging**: only merge when Jeremy directs it (he will want merges from time to time — that's fine) — never merge on your own. Avoid `gh pr merge --auto` on this repo: `main` has no required status checks, so `--auto` merges immediately instead of waiting for CI. When asked to merge, use a plain merge and confirm CI is green first.
- **Commits & PRs**: no `Co-Authored-By` lines, no "Generated with Claude Code" footer, and no Test-plan checklist in PR bodies — just summary bullets.
- **Version and release process** — see `RELEASING.md` for the full workflow
- **Version source of truth** — `Directory.Build.props` `<Version>` property (all projects inherit it)
- **Don't bump versions or tag releases** unless explicitly asked

### Documentation Conventions

- **Don't document unsupported or scaffold features in user-facing docs or the CLI** until they actually work. Example: the Native rasterizer (`KernSmith.Rasterizers.Native`) renders nothing until Phases 162-165 — keep it out of the docfx site, CLI `--rasterizer` options, and README backend tables. Internal `reference/REF-*` docs and code XML comments may note a clearly-labeled scaffold for contributors.
- **Docs are three-layered**: (1) per-package READMEs (where backends/satellite packages are documented), (2) hand-written docfx guides under `docs/`, and (3) the docfx-generated API reference, which covers **only** the core `src/KernSmith` project and is gitignored (`/api/` and `/_site/` are build artifacts). Don't expand `docfx.json` metadata to satellites without a reason. Site: kaltinril.github.io/Kernsmith.
- **When auditing docs, verify every factual claim** (especially "included/built-in/bundled" wording) against the real package structure, and scan `docs/` too — not just root + `plan/`.

### Project File References

| What | Location |
|------|----------|
| Entry point | `src/KernSmith/BmFont.cs` |
| Plan docs | `plan/` (start with `master-plan.md`) |
| Data types (source of truth) | `plan/done/plan-data-types.md` |
| Implementation order | `plan/done/plan-implementation-order.md` |
| Tests | `tests/KernSmith.Tests/` |
| Regression/output comparison | `tests/bmfont-compare/regression_check.py` (see Regression & Output Comparison above) |
| CI/CD | `.github/workflows/` |
