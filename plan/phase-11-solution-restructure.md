# Phase 11 — Solution Restructure & Multi-Project Foundation

> **Status**: Complete
> **Created**: 2026-03-20
> **Goal**: Restructure the repository into a well-organized multi-project solution that supports the current projects and lays groundwork for future UI, web, and mobile projects. The NuGet library is the primary artifact — all other projects consume it, never duplicate it.

---

## Guiding Principles

1. **The NuGet package is the primary product.** Every other project (CLI, UI, web, mobile, tests, benchmarks, samples) references it via `ProjectReference`. No logic duplication.
2. **net10.0 everywhere.** All projects target net10.0 unless a specific dependency blocks it (investigated per-project).
3. **Placeholder projects must not break `dotnet build`.** Any scaffolded future project must either compile clean (empty Program.cs, no unresolvable refs) or be excluded from the default solution build via solution filters or build conditions.

---

## Current State

### Existing Projects (5)

| Project | Path | Type | TFM | Solution Folder |
|---------|------|------|-----|-----------------|
| Bmfontier | `src/Bmfontier/` | Class Library (NuGet) | net8.0 | src |
| Bmfontier.Tests | `tests/Bmfontier.Tests/` | xUnit Test Suite | net8.0 | tests |
| Bmfontier.Cli | `samples/Bmfontier.Cli/` | Console Exe | net8.0 | samples |
| Bmfontier.Samples | `samples/Bmfontier.Samples/` | Console Exe | net10.0 | samples |
| Bmfontier.Benchmarks | `benchmarks/Bmfontier.Benchmarks/` | Console Exe | net8.0 | benchmarks |

### Existing Dependencies

```
Bmfontier (library) — THE PRIMARY ARTIFACT
  ├── FreeTypeSharp 3.1.0
  └── StbImageWriteSharp 1.16.7

Bmfontier.Tests ──> Bmfontier (ProjectReference)
  ├── xunit 2.9.3
  ├── FluentAssertions 8.9.0
  ├── coverlet.collector 6.0.4
  ├── Microsoft.NET.Test.Sdk 17.14.1
  └── xunit.runner.visualstudio 3.1.4

Bmfontier.Cli ──> Bmfontier (ProjectReference)
  (no extra packages)

Bmfontier.Benchmarks ──> Bmfontier (ProjectReference)
  └── BenchmarkDotNet 0.14.0

Bmfontier.Samples ──> Bmfontier (ProjectReference)
  (no extra packages)
```

### Issues Found

1. **No `Directory.Build.props`** — common settings duplicated across every .csproj
2. **No `global.json`** — no SDK version pinning
3. **No `nuget.config`** — relying on default feeds
4. **No `.editorconfig`** — no shared code style enforcement
5. **TFM inconsistency** — most projects on net8.0, Samples already on net10.0; all should be net10.0
6. **CLI lives under `samples/`** — it's a first-class tool, not a sample
7. **No centralized package management** — package versions could drift between projects
8. **CI is .NET 8 only** — needs updating for net10.0 and future workloads; currently Bmfontier.Samples (net10.0) will **fail** in CI
9. **FreeTypeSharp WASM gap** — no investigation done yet on alternatives for web target
10. **LangVersion inconsistency** — only the library and benchmarks set `LangVersion=latest`; Tests, CLI, Samples rely on TFM default
11. **CLAUDE.md is stale** — does not mention Bmfontier.Samples project; project organization table needs updating
12. **No `tools/` or `apps/` directories exist yet** — only `src/`, `tests/`, `samples/`, `benchmarks/`

---

## Target State

### Proposed Directory Layout

```
bmfontier/
├── Bmfontier.sln
├── Directory.Build.props          (NEW — shared build settings)
├── Directory.Packages.props       (NEW — central package management)
├── global.json                    (NEW — pin SDK version)
├── .editorconfig                  (NEW — code style)
├── nuget.config                   (NEW — explicit feed config)
│
├── src/
│   └── Bmfontier/                 (existing — core NuGet library, THE PRIMARY ARTIFACT)
│
├── tools/
│   ├── Bmfontier.Cli/             (MOVED from samples/ — first-class CLI tool)
│   └── (future tools TBD)
│
├── samples/
│   └── Bmfontier.Samples/         (existing — usage examples)
│
├── tests/
│   └── Bmfontier.Tests/           (existing)
│
├── benchmarks/
│   └── Bmfontier.Benchmarks/      (existing)
│
├── apps/                          (NEW — future, placeholder scaffolds)
│   ├── Bmfontier.Ui/              (GUM UI + MonoGame desktop)
│   ├── Bmfontier.Web/             (KNI web target)
│   └── Bmfontier.Mobile/          (Android, macOS, etc.)
│
├── plan/                          (existing)
└── reference/                     (existing)
```

### Proposed Solution Folders

```
Bmfontier.sln
├── src/
│   └── Bmfontier
├── tools/
│   └── Bmfontier.Cli
├── samples/
│   └── Bmfontier.Samples
├── tests/
│   └── Bmfontier.Tests
├── benchmarks/
│   └── Bmfontier.Benchmarks
└── apps/                          (added when projects are created)
    ├── Bmfontier.Ui
    ├── Bmfontier.Web
    └── Bmfontier.Mobile
```

---

## Phases

### Phase 1 — Build Infrastructure & net10.0 Migration

Add shared build files and unify all projects on net10.0.

- [ ] **Investigate net10.0 compatibility** for all key dependencies before migrating:
  - FreeTypeSharp 3.1.0 — does it work on net10.0? Any native binary issues?
  - StbImageWriteSharp 1.16.7
  - BenchmarkDotNet 0.14.0
  - xunit 2.9.3 + related test infra
  - FluentAssertions 8.9.0
  - Document any blockers; if a dep doesn't support net10.0, determine workaround or pin that project to net8.0
- [ ] Create `global.json` pinning SDK to 10.0.x (with rollForward policy)
- [ ] Create `Directory.Build.props` at repo root
  - Common properties: TargetFramework=net10.0, LangVersion=latest, Nullable=enable, ImplicitUsings=enable
  - Common metadata: Authors, License, RepositoryUrl, Copyright
  - Conditional: IsPackable=false for non-library projects
- [ ] Create `Directory.Packages.props` for central package management
  - Pin all NuGet package versions in one place
  - Enable `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
- [ ] Create `.editorconfig` with C# conventions matching existing code style
- [ ] Create `nuget.config` with explicit nuget.org feed
- [ ] Migrate all .csproj files to net10.0 (strip duplicated settings — they inherit from Directory.Build.props)
- [ ] Update CI workflows to install .NET 10 SDK
- [ ] Verify: `dotnet build` and `dotnet test` still pass on all platforms

### Phase 2 — Promote CLI to First-Class Tool

Move the CLI out of samples/ — it's a standalone tool, not an example.

- [ ] Create `tools/` directory
- [ ] Move `samples/Bmfontier.Cli/` to `tools/Bmfontier.Cli/`
- [ ] Update project reference paths in the .csproj
- [ ] Update `Bmfontier.sln` — move project to `tools` solution folder
- [ ] Update `InternalsVisibleTo` in the library .csproj (verify — assembly name unchanged, path irrelevant)
- [ ] Update CI workflows if they reference the CLI path directly
- [ ] Update CLAUDE.md project organization table
- [ ] Verify: full build + test pass

### Phase 3 — CI/CD Hardening

Update CI to support the restructured solution and prepare for future multi-framework projects.

- [ ] Update `ci.yml` for net10.0 SDK and solution-level build
- [ ] Add solution filter support so CI can exclude `apps/` projects that aren't buildable yet
- [ ] Consider adding `dotnet format --verify-no-changes` check (once .editorconfig is in place)
- [ ] Consider adding code coverage reporting (coverlet is already referenced)
- [ ] Update `publish.yml` for any path changes and net10.0

### Phase 4 — Investigate FreeTypeSharp Alternatives for Web/WASM

FreeTypeSharp ships native binaries for Windows, macOS, Linux, Android, iOS, tvOS — but **NOT WebAssembly**. Before scaffolding the web project, we need to understand our options.

- [ ] **Research FreeTypeSharp WASM status** — has the project added WASM support? Any forks that have?
- [ ] **Evaluate FreeType compiled to WASM** — can libfreetype be compiled to WASM via Emscripten and called from .NET WASM?
- [ ] **Survey alternative WASM-compatible rasterizers** — are there font rasterization libraries that run natively in WASM? (e.g., fontdue-rs compiled to WASM, HarfBuzz WASM builds, browser Canvas API via JS interop)
- [ ] **Evaluate server-side rasterization** — web UI sends font + options to a backend API that runs the full Bmfontier pipeline and returns results. Simplest option; no rasterizer porting needed.
- [ ] **Evaluate IRasterizer abstraction sufficiency** — does the current `IRasterizer` interface cleanly allow swapping in an alternative rasterizer, or does it leak FreeType assumptions?
- [ ] **Document findings and recommendation** in a dedicated plan doc (`plan/phase-13-wasm-rasterization.md`)
- [ ] **Decision gate**: based on findings, decide which approach to pursue before Phase 5 scaffolding of the web project

### Phase 5 — Future Project Scaffolding (apps/)

Create placeholder project structure for future UI/Web/Mobile projects. These are empty scaffolds — they must compile clean and not break `dotnet build` on the solution.

**Build safety rules for placeholders:**
- Each placeholder gets a minimal `Program.cs` (e.g., `Console.WriteLine("Not yet implemented");`) so it compiles
- Package references that require unavailable workloads are commented out with a `<!-- TODO -->` note
- Any project that cannot compile clean is excluded from the default solution build via a `.slnf` filter file OR a build condition in `Directory.Build.props`
- CI must pass with all placeholder projects in the solution

**Tasks:**
- [ ] Create `apps/` directory
- [ ] Scaffold `apps/Bmfontier.Ui/Bmfontier.Ui.csproj`
  - MonoGame + GUM UI references (verify they support net10.0)
  - `ProjectReference` to `../../src/Bmfontier/Bmfontier.csproj`
  - Target: net10.0 (cross-platform desktop — Windows, Linux, macOS)
  - Output type: Exe
  - Minimal Program.cs placeholder
- [ ] Scaffold `apps/Bmfontier.Web/Bmfontier.Web.csproj`
  - KNI framework references (verify availability)
  - `ProjectReference` to Bmfontier library
  - Target: TBD (depends on Phase 4 investigation and KNI's WASM support)
  - Minimal Program.cs placeholder
  - **Blocked on Phase 4** — approach for rasterization must be decided first
- [ ] Scaffold `apps/Bmfontier.Mobile/Bmfontier.Mobile.csproj`
  - Multi-target: net10.0-android, net10.0-maccatalyst, etc.
  - `ProjectReference` to Bmfontier library
  - May require `dotnet workload install` in CI
  - Minimal Program.cs placeholder
- [ ] Add all new projects to `Bmfontier.sln` under `apps` solution folder
- [ ] Create `.slnf` solution filter files:
  - `Bmfontier.Core.slnf` — library + tests + CLI + benchmarks (default dev)
  - `Bmfontier.All.slnf` — everything including apps
- [ ] Update CI to build using `Bmfontier.Core.slnf` by default; `Bmfontier.All.slnf` only when app workloads are available
- [ ] Create individual plan docs for each app project (separate from this phase doc)

---

## Architectural Considerations

### NuGet Library as the Single Source of Truth

All projects consume the Bmfontier library via `ProjectReference`. No project should duplicate font parsing, rasterization, atlas packing, or output formatting logic. If a consuming project needs functionality that doesn't exist in the library, the library gets extended — not the consuming project.

```
                    ┌─────────────────┐
                    │   Bmfontier     │
                    │  (NuGet pkg)    │
                    └───────┬─────────┘
          ┌─────────┬───────┼───────┬──────────┐
          │         │       │       │          │
       CLI      Tests  Benchmarks  Samples   Apps
       (tool)   (test)  (perf)    (examples)  (UI/Web/Mobile)
```

### net10.0 Migration

All projects move to net10.0. This is a hard requirement. If a dependency doesn't support net10.0:
1. Check for an updated version that does
2. Check if it works anyway (many netstandard2.0/2.1 packages work fine on newer TFMs)
3. If truly blocked, document and escalate — don't silently stay on net8.0

### FreeType + WASM Limitation

Dedicated investigation in **Phase 4**. The core library's `IRasterizer` interface is the key abstraction — if it's clean enough, a WASM-compatible rasterizer can be swapped in without touching the rest of the pipeline. If it leaks FreeType assumptions, the interface needs refactoring as part of Phase 4.

### MonoGame + GUM UI Stack

The desktop UI tool will use:
- **MonoGame** for rendering (cross-platform OpenGL/DirectX)
- **GUM UI** for layout and widgets (MonoGame-based UI framework)

Both are available as NuGet packages. The UI project will need:
- MonoGame.Framework.DesktopGL (for cross-platform)
- GUM + related packages
- Content pipeline setup for MonoGame assets

### Solution Filter Files (.slnf)

Required to keep placeholder projects from blocking day-to-day development:
- `Bmfontier.Core.slnf` — library + tests + CLI + benchmarks + samples (daily dev, CI default)
- `Bmfontier.All.slnf` — everything including apps (only when workloads installed)

---

## Dependency Graph (Target State)

```
Bmfontier (core library — net10.0, NuGet package)
├── FreeTypeSharp
└── StbImageWriteSharp

Bmfontier.Tests ──> Bmfontier (ProjectReference)
Bmfontier.Cli ──> Bmfontier (ProjectReference)
Bmfontier.Benchmarks ──> Bmfontier (ProjectReference)
Bmfontier.Samples ──> Bmfontier (ProjectReference)

Bmfontier.Ui ──> Bmfontier (ProjectReference)
├── MonoGame.Framework.DesktopGL
└── Gum (+ related packages)

Bmfontier.Web ──> Bmfontier (ProjectReference or via API — depends on Phase 4)
└── KNI framework

Bmfontier.Mobile ──> Bmfontier (ProjectReference)
└── MonoGame or MAUI (TBD)
```

---

## Related Plans

- **phase-12-pre-ship-polish.md** — sibling active plan; independent of this restructure
- **plan/done/phase-01 through phase-10** — completed phases archived in `done/`
- **plan/done/plan-project-structure.md** — original project structure plan (superseded by this doc)

---

## Out of Scope

- Actually implementing UI/Web/Mobile features (separate plan docs per project)
- Changing the core library's public API surface
- Adding new font formats or effects
- Phase 12 (pre-ship polish) is independent of this restructure
