# Releasing KernSmith

## Version Source of Truth

The version lives in one place: `Directory.Build.props` → `<Version>`.
All projects (library, CLI, UI) inherit it automatically.

## Workflow

### Feature Development
1. Create a feature branch from `main`
2. Make changes, commit, push, create PR
3. **Do NOT bump the version in feature PRs**
4. Merge PR after review/CI passes

### Cutting a Release
1. On `main`, run the publish script: `scripts\publish.bat 0.9.2`
2. This bumps the version in `Directory.Build.props`, commits, tags, and pushes
3. The `publish.yml` GitHub Actions workflow triggers automatically and:
   - Builds CLI and UI binaries for all platforms (win-x64, win-arm64, linux-x64, osx-arm64, osx-x64)
   - Publishes the NuGet package
   - Creates a GitHub Release page with downloadable binaries (.zip for Windows, .tar.gz for Linux/macOS)

### Manual Steps (alternative to publish script)
1. Bump `<Version>` in `Directory.Build.props`
2. Commit: `git commit -am "Bump version to 0.9.2"`
3. Tag: `git tag v0.9.2`
4. Push: `git push && git push origin v0.9.2`

## Version Scheme

[Semantic Versioning](https://semver.org/):
- **MAJOR** (1.0.0) — breaking API changes
- **MINOR** (0.10.0) — new features, backwards compatible
- **PATCH** (0.9.1) — bug fixes, docs, internal changes

## Checklist

- [ ] Run `scripts\publish.bat <version>` (or bump, commit, tag, push manually)
- [ ] Verify publish workflow completed on GitHub Actions
- [ ] Verify package appears on nuget.org
- [ ] Verify GitHub Release page has binaries for all platforms
