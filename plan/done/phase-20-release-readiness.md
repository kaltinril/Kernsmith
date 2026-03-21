# Phase 20 — Release Readiness

> **Status**: Planning
> **Created**: 2026-03-20
> **Goal**: Resolve all remaining packaging, versioning, and polish items needed to publish KernSmith as a public NuGet package.

---

## Items

### A — Shipping Blockers

| # | Item | Details |
|---|------|---------|
| A1 | **Fix version mismatch** | `KernSmith.csproj` says 0.8.0 but CHANGELOG has 0.9.0 entries. Align to 0.9.0 (or 1.0.0 if ready for stable). |
| A2 | **Package icon** | csproj has `<PackageIcon>icon.png</PackageIcon>` but no actual icon file. Either create/source one or remove the tag to unblock `dotnet pack`. |
| A3 | **Verify `dotnet pack`** | Run `dotnet pack src/KernSmith/KernSmith.csproj -c Release` and confirm it produces a clean .nupkg with no warnings. Inspect the package contents (README, LICENSE, XML docs, DLLs). |
| A4 | **Gitignore generated output** | Add patterns to `.gitignore` for generated font files in the repo root (e.g., `/*.fnt`, `/*.png` at root level) to prevent accidental commits of test output. |

### B — Verification

| # | Item | Details |
|---|------|---------|
| B1 | **Test the NuGet package** | Install the locally-packed .nupkg in a fresh console project. Verify: types resolve, IntelliSense shows XML docs, `BmFont.Generate()` produces valid output. |
| B2 | **CI workflow smoke test** | Trigger the CI workflow manually or via a test push. Confirm build + test passes on all 3 OS targets (Windows, Ubuntu, macOS). |
| B3 | **Verify SourceLink** | After packing, confirm the .nupkg has SourceLink metadata: `dotnet sourcelink test <nupkg-path>` or inspect the .pdb. |

### C — GitHub Repo Polish

| # | Item | Details |
|---|------|---------|
| C1 | **Repo description** | Set the GitHub repo description to match the NuGet package description. |
| C2 | **Repo topics** | Add topics: `bmfont`, `bitmap-font`, `font`, `game-dev`, `dotnet`, `csharp`, `texture-atlas`, `sdf`, `freetype`. |
| C3 | **Social preview image** | Optional — create a preview image showing sample output for the repo's Open Graph card. |

### D — First Release

| # | Item | Details |
|---|------|---------|
| D1 | **Decide version** | 0.9.0 (pre-release/beta) or 1.0.0 (stable). If 1.0.0, audit the public API surface for anything that should change before committing to semver stability. |
| D2 | **Git tag** | Create `v0.9.0` (or `v1.0.0`) tag. The publish workflow triggers on version tags. |
| D3 | **GitHub release** | Create a GitHub release with the tag, paste CHANGELOG entry as release notes. |
| D4 | **NuGet publish** | Either let the CI publish workflow handle it, or manually `dotnet nuget push`. Requires NuGet API key configured as a GitHub secret. |

### E — Sample Project (Nice-to-Have)

| # | Item | Details |
|---|------|---------|
| E1 | **Console sample** | Update `samples/KernSmith.Samples/` with a minimal example that generates a font and writes to disk. Should work out of the box with `dotnet run`. |
| E2 | **MonoGame sample** | Optional — a minimal MonoGame project that loads a .bmfc, generates in memory, creates a Texture2D. Could live in `samples/` or be a separate repo. |

---

## Suggested Execution Order

1. **A1** — Fix version (1 minute)
2. **A4** — Gitignore patterns (1 minute)
3. **A2** — Package icon (create simple icon or remove tag)
4. **A3** — Run dotnet pack, fix any issues
5. **B1** — Test the package locally
6. **B2** — CI smoke test
7. **B3** — SourceLink verification
8. **D1** — Decide version number
9. **C1-C2** — Repo metadata
10. **E1** — Update console sample
11. **D2-D4** — Tag, release, publish

---

## Estimated Effort

| Track | Effort |
|-------|--------|
| A — Blockers | 30 minutes |
| B — Verification | 1 hour |
| C — Repo polish | 15 minutes |
| D — Release | 30 minutes |
| E — Samples | 1-2 hours |
