# Phase 32b -- StbTrueType Documentation & Publishing

> **Status**: Complete
> **Created**: 2026-03-31
> **Depends on**: Phase 32 (StbTrueType rasterizer implementation)
> **Related**: Phase 30, Phase 32

## Goal

Complete the documentation and CI/CD publishing setup for the StbTrueType rasterizer plugin so it's properly discoverable, documented, and automatically published to NuGet. Also fix a pre-existing gap where the FreeType plugin package is missing from the same publish/release/docs infrastructure.

## Background

FreeType is a **separate packable project** (`IsPackable=true` in its csproj) with no `ProjectReference` from `src/KernSmith/KernSmith.csproj`. It does NOT ship transitively with the core KernSmith package. Despite this, FreeType was never added to `publish.yml` pack steps, release notes, or the RELEASING.md dependency graph. This phase fixes that gap for both FreeType and StbTrueType.

## Tasks

### 1. Add missing reference docs to master-plan.md

The reference documents table (`plan/master-plan.md`, line ~194) lists REF-01 through REF-10 but is missing three entries. Add them in numeric order:

- **REF-08**: `reference/REF-08-bmfont-internals.md` -- BMFont internals documentation (currently skipped between REF-07 and REF-09)
- **REF-11**: `reference/REF-11-wasm-restrictions.md` -- WASM/AOT platform restrictions research (from Phase 31)
- **REF-12**: `reference/REF-12-rasterizer-backends.md` -- Rasterizer backends documentation (from Phase 30/32)

### 2. Add FreeType and StbTrueType to publish.yml

**pack-cross-platform job** (line ~116): Add `dotnet pack` lines for both FreeType and StbTrueType after the core KernSmith pack and before the Gum integration packs:

```
dotnet pack src/KernSmith.Rasterizers.FreeType/KernSmith.Rasterizers.FreeType.csproj --configuration Release --no-build --output ./nupkg
dotnet pack src/KernSmith.Rasterizers.StbTrueType/KernSmith.Rasterizers.StbTrueType.csproj --configuration Release --no-build --output ./nupkg
```

Both are cross-platform packages, so they belong in `pack-cross-platform`, not `pack-windows`.

**create-release job** (line ~194): Add NuGet package links for both in the release notes body, after DirectWrite and before GumCommon:

```
- [KernSmith.Rasterizers.FreeType](https://www.nuget.org/packages/KernSmith.Rasterizers.FreeType/${{ needs.validate.outputs.version }})
- [KernSmith.Rasterizers.StbTrueType](https://www.nuget.org/packages/KernSmith.Rasterizers.StbTrueType/${{ needs.validate.outputs.version }})
```

### 3. Create and update DocFX documentation

**New file**: `docs/rasterizers/stbtruetype.md`

Follow the same structure as `docs/rasterizers/freetype.md` with these sections:

- **Title**: `# StbTrueType`
- **Intro**: Managed-only rasterizer using stb_truetype (C# port). No native dependencies. Ideal for WASM, AOT, and trimmed deployments.
- **Platform**: Cross-platform -- Linux, macOS, Windows, WASM/Blazor, NativeAOT. No native binaries required.
- **Installation**: `dotnet add package KernSmith.Rasterizers.StbTrueType` -- auto-registers via `[ModuleInitializer]`.
- **Usage**: Show `RasterizerBackend.StbTrueType` enum selection. Mention auto-registration.
- **Capabilities**: TTF/OTF input, hinting, anti-aliasing, synthetic bold/italic, super sampling, SDF rendering. Fully managed -- no P/Invoke.
- **Limitations**: No WOFF/WOFF2 (no decompressor), no color fonts, no variable font axes, no outline stroke. Glyph quality may differ slightly from FreeType.
- **When to Use**: Use for WASM/Blazor, NativeAOT trimmed deployments, or any environment where native FreeType binaries are unavailable or undesirable.

**Update**: `docs/rasterizers/index.md`

- **Opening paragraph** (line 1): Currently says "Two additional Windows-only backends". Update to "Three additional backends (two Windows-only, one cross-platform managed)".
- **Backends table**: Add row: `| [StbTrueType](stbtruetype.md) | KernSmith.Rasterizers.StbTrueType | Cross-platform (managed) |`
- **Capability comparison table**: Add StbTrueType column with appropriate Yes/No values.
- **Auto-registration paragraph** (line ~30): Currently only mentions GDI and DirectWrite. Add StbTrueType.
- **Decision tree**: Add entry before "Not sure?": "Need WASM/Blazor or NativeAOT without native binaries? Use StbTrueType."
- **Code example**: Add commented-out line: `// RasterizerBackend = RasterizerBackend.StbTrueType  // managed, no native deps`

**Update**: `docs/rasterizers/toc.yml`

Add StbTrueType entry after DirectWrite and before "Writing a Custom Backend":

```yaml
- name: StbTrueType
  href: stbtruetype.md
```

**Update**: `docs/toc.yml` (root)

The root toc.yml (lines ~22-30) lists rasterizer children. Add StbTrueType after DirectWrite:

```yaml
- name: StbTrueType
  href: rasterizers/stbtruetype.md
```

**Update**: `docs/index.md`

Line ~23 says "DirectWrite and GDI rasterizer backends (Windows)". Update to mention StbTrueType as a cross-platform managed backend alongside the Windows backends.

### 4. Update RELEASING.md package dependency graph

Add both FreeType and StbTrueType to the package dependency graph (line ~30). FreeType is a separate packable project, not a transitive dependency of the core package:

```
KernSmith                                    (core -- no sibling deps)
├── KernSmith.Rasterizers.FreeType          -> KernSmith
├── KernSmith.Rasterizers.Gdi               -> KernSmith
├── KernSmith.Rasterizers.DirectWrite.TerraFX -> KernSmith
├── KernSmith.Rasterizers.StbTrueType       -> KernSmith
├── KernSmith.GumCommon                     -> KernSmith
│   ├── KernSmith.FnaGum                    -> GumCommon (gets KernSmith transitively)
│   ├── KernSmith.KniGum                    -> GumCommon
│   └── KernSmith.MonoGameGum              -> GumCommon
```

### 5. Update README.md and core csproj description

**Update**: `README.md`

The rasterizer backends table (lines ~40-47) lists only FreeType, GDI, and DirectWrite. Add a row for StbTrueType:

```
| StbTrueType | KernSmith.Rasterizers.StbTrueType | Cross-platform (managed, no native deps) |
```

**Update**: `src/KernSmith/KernSmith.csproj`

Line ~7 description says "FreeType, GDI, and DirectWrite available as separate packages". Update to "FreeType, GDI, DirectWrite, and StbTrueType available as separate packages".

### 6. Add CHANGELOG.md entry

Add an entry under `[Unreleased]` for Phase 32:

```markdown
### Added
- StbTrueType rasterizer backend — managed-only, cross-platform, no native dependencies (Phase 32)
```

## Success Criteria

- [x] master-plan.md reference table includes REF-08, REF-11, REF-12
- [x] publish.yml packs both FreeType and StbTrueType NuGet packages
- [x] publish.yml release notes list both FreeType and StbTrueType
- [x] docs/rasterizers/stbtruetype.md exists with full content (SDF listed as supported)
- [x] docs/rasterizers/index.md updated: opening paragraph, backends table, capability table, auto-registration paragraph, decision tree
- [x] docs/rasterizers/toc.yml includes StbTrueType
- [x] docs/toc.yml (root) includes StbTrueType in rasterizer children
- [x] docs/index.md mentions StbTrueType as cross-platform backend
- [x] RELEASING.md dependency graph includes both FreeType and StbTrueType
- [x] README.md rasterizer backends table includes StbTrueType
- [x] src/KernSmith/KernSmith.csproj description mentions StbTrueType
- [x] CHANGELOG.md has Phase 32 entry under [Unreleased]
