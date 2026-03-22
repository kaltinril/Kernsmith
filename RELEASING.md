# Releasing KernSmith

## Version Source of Truth

The version lives in one place: `Directory.Build.props` → `<Version>`.
All projects (library, CLI, UI) inherit it automatically.

## Workflow

### Feature Development
1. Create a feature branch from `main`
2. Make changes, commit, push, create PR
3. **Do NOT bump the version in feature PRs** (unless this PR is the release)
4. Merge PR after review/CI passes

### Cutting a Release
1. Create a PR that bumps `<Version>` in `Directory.Build.props` and updates `CHANGELOG.md`
2. Merge the PR to main
3. Go to **GitHub Actions → Publish Release → Run workflow**
4. Enter the version number (e.g., `0.9.2`) and click **Run workflow**
5. The workflow automatically:
   - Validates the version matches `Directory.Build.props`
   - Builds CLI and UI binaries for all platforms (win-x64, win-arm64, linux-x64, osx-arm64, osx-x64)
   - Publishes the NuGet package
   - Creates a git tag (`v0.9.2`)
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
- [ ] Verify package appears on nuget.org
- [ ] Verify GitHub Release page has binaries for all platforms
