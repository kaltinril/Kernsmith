# Phase 17 — Rebrand to KernSmith

> **Status**: Planning
> **Created**: 2026-03-20
> **Goal**: Rename the entire project from "bmfontier" to "KernSmith" — namespaces, assemblies, directories, project files, docs, CLI commands, NuGet package, and all references.

---

## Naming Convention

| Context | Old | New |
|---------|-----|-----|
| NuGet package | `Bmfontier` | `KernSmith` |
| Root namespace | `Bmfontier` | `KernSmith` |
| Sub-namespaces | `Bmfontier.Font`, `Bmfontier.Atlas`, etc. | `KernSmith.Font`, `KernSmith.Atlas`, etc. |
| CLI namespace | `Bmfontier.Cli` | `KernSmith.Cli` |
| Test namespace | `Bmfontier.Tests` | `KernSmith.Tests` |
| Main API class | `BmFont` | `KernSmith` (static class) |
| Builder | `BmFontBuilder` | `KernSmithBuilder` |
| Result type | `BmFontResult` | `KernSmithResult` |
| Model type | `BmFontModel` | `KernSmithModel` |
| Reader | `BmFontReader` | `KernSmithReader` |
| Model builder | `BmFontModelBuilder` | `KernSmithModelBuilder` |
| Binary formatter | `BmFontBinaryFormatter` | `KernSmithBinaryFormatter` |
| Formatter interfaces | `IBmFontFormatter`, etc. | `IKernSmithFormatter`, etc. |
| Solution file | `Bmfontier.sln` | `KernSmith.sln` |
| Solution filters | `Bmfontier.*.slnf` | `KernSmith.*.slnf` |
| CLI exe name | `bmfontier` | `kernsmith` |
| CLI help text | `bmfontier generate ...` | `kernsmith generate ...` |
| Authors | `bmfontier contributors` | `KernSmith contributors` |
| .bmfc comments | `# bmfontier extensions` | `# kernsmith extensions` |

## What NOT to Rename

- **BMFont** — the AngelCode BMFont format name. Keep all references to "BMFont format", "BMFont-compatible", "BMFont .fnt files"
- **.fnt** — the file extension (it's the BMFont format)
- **.bmfc** — the config file extension (it's the BMFont config format)
- **BmFontReader** internal format parsing — comments about "BMFont text format", "BMFont XML format", "BMFont binary format" stay

---

## Scope

### Category 1: Directory Renames (8 directories)
- `src/Bmfontier/` → `src/KernSmith/`
- `tests/Bmfontier.Tests/` → `tests/KernSmith.Tests/`
- `tools/Bmfontier.Cli/` → `tools/KernSmith.Cli/`
- `benchmarks/Bmfontier.Benchmarks/` → `benchmarks/KernSmith.Benchmarks/`
- `samples/Bmfontier.Samples/` → `samples/KernSmith.Samples/`
- `apps/Bmfontier.Ui/` → `apps/KernSmith.Ui/`
- `apps/Bmfontier.Web/` → `apps/KernSmith.Web/`
- `apps/Bmfontier.Mobile/` → `apps/KernSmith.Mobile/`

### Category 2: File Renames (12+ files)
- `Bmfontier.sln` → `KernSmith.sln`
- `Bmfontier.All.slnf` → `KernSmith.All.slnf`
- `Bmfontier.Core.slnf` → `KernSmith.Core.slnf`
- `src/KernSmith/Bmfontier.csproj` → `src/KernSmith/KernSmith.csproj`
- `tests/KernSmith.Tests/Bmfontier.Tests.csproj` → `tests/KernSmith.Tests/KernSmith.Tests.csproj`
- `tools/KernSmith.Cli/Bmfontier.Cli.csproj` → `tools/KernSmith.Cli/KernSmith.Cli.csproj`
- `benchmarks/KernSmith.Benchmarks/Bmfontier.Benchmarks.csproj` → `benchmarks/KernSmith.Benchmarks/KernSmith.Benchmarks.csproj`
- `samples/KernSmith.Samples/Bmfontier.Samples.csproj` → `samples/KernSmith.Samples/KernSmith.Samples.csproj`
- `apps/KernSmith.Ui/Bmfontier.Ui.csproj` → `apps/KernSmith.Ui/KernSmith.Ui.csproj`
- `apps/KernSmith.Web/Bmfontier.Web.csproj` → `apps/KernSmith.Web/KernSmith.Web.csproj`
- `apps/KernSmith.Mobile/Bmfontier.Mobile.csproj` → `apps/KernSmith.Mobile/KernSmith.Mobile.csproj`
- Source file renames: BmFont.cs → KernSmith.cs, BmFontBuilder.cs → KernSmithBuilder.cs, etc.

### Category 3: Source Code (150+ .cs files)
- All `namespace Bmfontier` → `namespace KernSmith`
- All `namespace Bmfontier.*` → `namespace KernSmith.*`
- All `using Bmfontier` → `using KernSmith`
- All class/interface renames (BmFont → KernSmith, BmFontResult → KernSmithResult, etc.)
- All method calls updated

### Category 4: Project Files (8 .csproj + 2 .props)
- PackageId, Authors, Description, URLs
- ProjectReference paths
- InternalsVisibleTo
- IsPackable condition

### Category 5: Solution & Filter Files (3 files)
- All project paths and names

### Category 6: CI/CD (3 .yml files)
- Project paths in build/test/publish commands

### Category 7: Documentation (5+ .md files)
- README.md, CLAUDE.md, CLI README.md
- Plan docs (master-plan.md, active plans)

### Category 8: Test & Config Files
- .bat files (exe paths, comments)
- .bmfc files (comments referencing bmfontier extensions)
- CliTests.cs (project path reference)

---

## Implementation Order

1. **Rename source files** (BmFont.cs → KernSmith.cs, etc.)
2. **Update all namespaces and using statements** in .cs files
3. **Update class/interface/type names** in .cs files
4. **Rename directories** (src/Bmfontier → src/KernSmith, etc.)
5. **Rename .csproj files** and update their contents
6. **Update solution file** (paths, project names)
7. **Update solution filters**
8. **Update Directory.Build.props** (authors, URLs, IsPackable condition)
9. **Update CI/CD workflows**
10. **Update documentation** (README, CLAUDE.md, CLI README)
11. **Update .bat files** (paths, comments)
12. **Update .bmfc files** (extension comments)
13. **Update plan docs** (master-plan, active plans)
14. **Build and test**

---

## Success Criteria

1. `dotnet build` succeeds with 0 errors
2. `dotnet test` — all 208 tests pass
3. `kernsmith generate --help` works
4. `kernsmith batch tests/manual_testing/bmfc/*.bmfc --time` works
5. No remaining references to "Bmfontier" or "bmfontier" in source code (except BMFont format references)
6. NuGet package ID is `KernSmith`
7. All namespaces start with `KernSmith`
