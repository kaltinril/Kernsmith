# Releasing KernSmith

## Version Source of Truth

The version lives in one place: `Directory.Build.props` → `<Version>`.
All projects (library, CLI, UI) inherit it automatically.

## Multi-Package Architecture

KernSmith ships as a family of NuGet packages. Projects under `src/` and
`integrations/` each produce a separate `.nupkg`. The core library has no
dependencies on sibling packages; extension packages depend on the core
(or on each other) via `ProjectReference`.

### How it works

- **During development** — projects reference each other with `<ProjectReference>`.
- **At pack time** — `dotnet pack` automatically converts each `ProjectReference`
  into a NuGet dependency (e.g. `KernSmith >= 0.9.6`) in the resulting `.nupkg`.
- **For consumers** — installing a leaf package like `KernSmith.FnaGum` from NuGet
  automatically pulls in all transitive dependencies (`KernSmith.GumCommon` →
  `KernSmith`). Users only install the one package they need.

> **Never reference your own packages via `PackageReference`** in this repo.
> Always use `ProjectReference` — the NuGet dependency chain is generated
> automatically when you pack.

### Package dependency graph

```
KernSmith                                    (core — no sibling deps)
├── KernSmith.Rasterizers.FreeType          → KernSmith
├── KernSmith.Rasterizers.Gdi               → KernSmith
├── KernSmith.Rasterizers.DirectWrite.TerraFX → KernSmith
├── KernSmith.Rasterizers.StbTrueType       → KernSmith
├── KernSmith.GumCommon                     → KernSmith, FreeType
│   ├── KernSmith.FnaGum                    → GumCommon (gets KernSmith + FreeType transitively)
│   ├── KernSmith.KniGum                    → GumCommon
│   └── KernSmith.MonoGameGum              → GumCommon
```

### Adding a new package

1. Create the project under `src/` or `integrations/` (e.g. `src/KernSmith.NewThing/`).
2. Add a `<PackageId>` and `<Description>` in the `.csproj`.
3. Add a `<ProjectReference>` to whichever sibling(s) it depends on.
4. The shared `<Version>` from `Directory.Build.props` is inherited — all
   packages in the family stay version-locked.
5. Add `<IsPackable>true</IsPackable>` to the `.csproj` — `Directory.Build.props`
   defaults to `false`, so each package project must opt in explicitly.

### Packability rules

`Directory.Build.props` defaults `IsPackable` to `false`. Each library project
opts in by setting `<IsPackable>true</IsPackable>` in its own `.csproj`:

- **`src/` projects** — core library and rasterizer backends.
- **`integrations/` projects** — Gum/MonoGame/FNA/KNI integration packages.
- **Everything else** (tests, tools, samples, benchmarks, apps) — not packable.

### Packing locally

```bash
# Pack all packages at once (packs every project where IsPackable=true)
dotnet pack KernSmith.sln -c Release --output ./nupkg

# Pack a single package
dotnet pack src/KernSmith -c Release --output ./nupkg
dotnet pack integrations/KernSmith.MonoGameGum -c Release --output ./nupkg
```

## Workflow

### Feature Development
1. Create a feature branch from `main`
2. Make changes, commit, push, create PR
3. **Do NOT bump the version in feature PRs** (unless this PR is the release)
4. Merge PR after review/CI passes

### Cutting a Release
1. Create a PR that bumps `<Version>` in `Directory.Build.props` and updates `CHANGELOG.md`
2. Merge the PR to main
3. Trigger the publish — pick **one**:
   - **GitHub UI (recommended):** Actions → Publish Release → Run workflow → enter version → click Run
   - **Command line:** `git tag v0.12.3 && git push origin v0.12.3`
   - **Local script:** `scripts\publish.bat 0.12.3`
4. The workflow automatically:
   - Validates the version matches `Directory.Build.props`
   - Builds CLI and UI binaries for all platforms (win-x64, win-arm64, linux-x64, osx-arm64, osx-x64)
   - Packs and publishes **all** NuGet packages (from `src/` and `integrations/`)
   - Creates a git tag (manual dispatch only — tag push already has one)
   - Creates a GitHub Release page with downloadable binaries (.zip for Windows, .tar.gz for Linux/macOS)

## Version Scheme

[Semantic Versioning](https://semver.org/):
- **MAJOR** (1.0.0) — breaking API changes
- **MINOR** (0.10.0) — new features, backwards compatible
- **PATCH** (0.9.1) — bug fixes, docs, internal changes

## Checklist

- [ ] Version bumped in `Directory.Build.props`
- [ ] `CHANGELOG.md` updated
- [ ] PR merged to main
- [ ] Run "Publish Release" workflow from GitHub Actions
- [ ] Verify workflow completed successfully
- [ ] Verify all packages appear on nuget.org
- [ ] Verify GitHub Release page has binaries for all platforms
