# Project Facts

Source of truth for statements about the project. One short fact per line. Update in place; delete facts that become false. No status counts or inventories that drift — record what is load-bearing.

## Identity

- Brand is stylized **KernSmith** (capital S).
- The GitHub repo is canonically `kaltinril/Kernsmith` (lowercase s) — per the API `full_name` and the git remote — so the brand and the repo name genuinely disagree.
- Jeremy's GitHub username: `kaltinril`. Vic = `vchelaru` (Gum author).
- Version source of truth is `Directory.Build.props` `<Version>`; all projects inherit it, and no csproj carries its own `PackageVersion`.
- `publish.yml` fires on a pushed `v*` tag **and** on `workflow_dispatch` — unlike some sibling repos, tagging here really does publish.
- License: MIT (see `LICENSE`).

## Stack & Conventions

- C# / .NET, multi-targeting `net8.0;net10.0`, so building the solution needs both SDKs.
- Projects are deliberately **not** unified on one TFM. `Directory.Build.props` sets the `net8.0;net10.0` default, used by core, `KernSmith.Fonts.Web`, and the FreeType/StbTrueType/Native backends. The exceptions: `Rasterizers.Gdi` is `net8.0-windows;net10.0-windows`; `Rasterizers.DirectWrite.TerraFX` is `net10.0-windows` only; `KernSmith.Cli` adds both `-windows` variants; `KernSmith.Ui` is `net10.0;net10.0-windows`; `Samples.BlazorWasm` is `net10.0` only.
- NuGet package ID and main API class are both `KernSmith`.
- Nullable reference types are enabled.
- Testing is xUnit + Shouldly.
- Core library dependencies: StbImageSharp, StbImageWriteSharp. FreeTypeSharp belongs to `KernSmith.Rasterizers.FreeType`, not the core library.
- `AllowUnsafeBlocks` is set in the `src/KernSmith.Rasterizers.*` backends and the `tests/bmfont-compare/GenerateAll` harness — never in the core library.

## Namespaces

- `KernSmith` (root): entry point, config types, exceptions, enums. Files in `Config/` and `Exceptions/` use this root namespace.
- `KernSmith.Font`: font reading, TTF parsing. `.Models`: FontInfo, KerningPair, GlyphMetrics. `.Tables`: HeadTable, HheaTable, Os2Metrics, NameInfo.
- `KernSmith.Rasterizer` (singular): IRasterizer, post-processors, effects (IGlyphEffect), GlyphCompositor.
- `KernSmith.Rasterizers.*` (plural): rasterizer backend packages.
- `KernSmith.Atlas`: IAtlasPacker, packers, encoders (PNG/TGA/DDS), AtlasBuilder, AtlasSizeEstimator, ChannelCompositor.
- `KernSmith.Output`: formatters, FileWriter, BmFontResult, BmFontReader, BmFontModelBuilder. `.Model`: BmFontModel, InfoBlock, CommonBlock, ExtendedMetadata.

## Repo & Git

- `/tests/bmfont-compare/` is gitignored wholesale; a handful of generic-font `.bmfc` configs there are tracked as force-added exceptions, and the rest reference machine-specific Gum fonts and are intentionally untracked.
- A new `.bmfc` added with plain `git add` is silently dropped: `git status` does not even list it as untracked, so it looks committed but never reaches the repo.

## Ownership & Dependencies

- All four Gum integration projects (GumCommon, KniGum, MonoGameGum, FnaGum) were removed from this repo; Vic owns them, source lives in his repo under `Integrations/KernSmith/`.
- Core `KernSmith` stays Jeremy's (co-owned on NuGet, but authors/projectUrl remain kaltinril).
- The UI has no local ProjectReference to the Gum integrations — it gets `KernSmith.MonoGameGum` transitively via `Gum.Themes.Editor.MonoGame`.
- Dependabot vulnerability alerts are **enabled** (`GET /repos/kaltinril/Kernsmith/vulnerability-alerts` → 204; 404 would mean disabled).

## Feature State

- `.bmfc` channel config (`alphaChnl`/`redChnl`/`greenChnl`/`blueChnl`) is parsed by `BmfcConfigReader` into `ChannelConfig` and honored on the CLI/core path (shipped 0.15.2, PR #128).
- The desktop UI carries channel config end-to-end (`EffectsViewModel.Channels` → `GenerationRequest.Channels` → `GenerationService` sets `options.Channels`).
- The Native rasterizer renders TrueType (`glyf`) outlines end to end as of Phases 164-165, and is exposed in the CLI (`--rasterizer native`) and the README as clearly-labeled experimental. CFF/OTF (PostScript) outlines are rejected at load time until Phase 166 adds a CFF interpreter; no WOFF, hinting, SDF, outline stroke, or synthetic bold/italic yet; only `AntiAlias.None` / `Grayscale`. Not published to NuGet.

## Build & Test Gotchas

- CliTests run a hardcoded Debug CLI path (`tests/KernSmith.Tests/Cli/CliTests.cs`) — use plain `dotnet test`, not `-c Release --no-build`, which yields false CLI failures.
- The regression harness exercises the CLI `GenerateAll` path only, not the UI `GenerationService`; verify UI-generation changes separately.
- `GenerateAll` skips a (config, backend) pair when the backend declines a capability the config requests — e.g. SDF on GDI/DirectWrite/Native.
- `dotnet run` exits 1 for **both** a build failure and a partial generation, so the two must be distinguished by building as a separate step.
- Local NuGet validation needs `packageSourceMapping`: route `KernSmith*` to the local feed and `*` to nuget.org in the consumer `nuget.config`.

## CodeQL

- The only open alerts are the deliberate `y0 == y1` ones — `ScanlineRasterizer.cs` and `OutlineFlattener.cs`, plus the two `OutlineFlattenerTests` assertions that pin the invariant. Everything else is fixed or dismissed.
- `paths` / `paths-ignore` do **nothing** for C# here: they apply only when CodeQL analyses *without building*, and `codeql.yml` builds the solution explicitly.
- Code-scanning dismissal comments are capped at **280 characters**; longer ones return HTTP 422, which looks like a generic failure if stderr is swallowed.
- A dismissal comment **cannot be edited in place** ("Alert is already dismissed", HTTP 400) — PATCH `state=open` first, then re-dismiss.
- PR-level CodeQL results live on `refs/pull/N/merge`. `refs/pull/N/head` has **no analyses at all**, so querying it returns a meaningless empty set that reads like a pass.

## File Map

| What | Location |
|------|----------|
| Entry point | `src/KernSmith/BmFont.cs` |
| Plan docs | `plan/` (start with `master-plan.md`) |
| Data types (source of truth) | `plan/done/plan-data-types.md` |
| Implementation order | `plan/done/plan-implementation-order.md` |
| Tests | `tests/KernSmith.Tests/` |
| Test font fixture | `tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf` |
| Regression/output comparison | `tests/bmfont-compare/regression_check.py` |
| CI/CD | `.github/workflows/` |
| Docs site | kaltinril.github.io/Kernsmith |
