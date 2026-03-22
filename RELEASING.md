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
1. Create a branch: `git checkout -b release/v0.9.2`
2. Bump `<Version>` in `Directory.Build.props`
3. Add a new section to `CHANGELOG.md` with the date and changes
4. Commit: `Bump version to 0.9.2`
5. Push, create PR, merge

### Tagging & Publishing
1. After the release PR merges, pull main: `git checkout main && git pull`
2. Tag: `git tag v0.9.2`
3. Push the tag: `git push origin v0.9.2`
4. The `publish.yml` workflow triggers automatically and pushes to NuGet

## Version Scheme

[Semantic Versioning](https://semver.org/):
- **MAJOR** (1.0.0) — breaking API changes
- **MINOR** (0.10.0) — new features, backwards compatible
- **PATCH** (0.9.1) — bug fixes, docs, internal changes

## Checklist

- [ ] `Directory.Build.props` version bumped
- [ ] `CHANGELOG.md` updated with new section
- [ ] Release PR merged to main
- [ ] Tag created and pushed
- [ ] Verify publish workflow completed on GitHub Actions
- [ ] Verify package appears on nuget.org
