---
name: release
description: "Cut a KernSmith release: bump version, update CHANGELOG/RELEASING.md, audit docs, commit, push, PR, merge, and remind to publish. Use this whenever the user says 'release', 'cut a release', 'bump version', 'prepare release', 'new version', or any variation of wanting to ship a new version. Also trigger when the user types /release."
argument-hint: "<version> (e.g., 0.10.4)"
---

# Release

Automate the full KernSmith release workflow from version bump through PR merge.

## Important

Run all git commands directly (e.g., `git status`) without prefixing `cd <path> &&`. The Bash tool already runs in the project's working directory, and the `cd` prefix breaks permission allow-list matching on `Bash(git *)`.

## Input

`$ARGUMENTS` should be the version number (e.g., `0.10.4`). If not provided, read the current version from `Directory.Build.props` and ask the user what the new version should be.

## Steps

### 1. Validate and prepare

```bash
# Get current version for reference
grep '<Version>' Directory.Build.props
# Make sure we're starting clean
git status
```

If there are uncommitted changes, warn the user and stop — don't proceed with dirty working tree.

### 2. Branch from latest main

```bash
git checkout main
git pull
git checkout -b version/<version>
```

### 3. Bump version

Edit `Directory.Build.props` — change `<Version>X.Y.Z</Version>` to the new version. This is the single source of truth; all projects inherit it.

### 4. Update CHANGELOG.md

Move everything under `## [Unreleased]` into a new version section. The result should look like:

```markdown
## [Unreleased]

## [<version>] - <today's date YYYY-MM-DD>

### Added
- (entries that were under Unreleased)

### Fixed
- (entries that were under Unreleased)
```

If `[Unreleased]` is empty, add a single entry: `- Version bump and documentation updates`

### 5. Update RELEASING.md

Update any version number examples in `RELEASING.md` to use the new version (e.g., command-line examples, tag examples). These are illustrative — keep them current so the doc stays useful.

### 6. Documentation audit

Spawn an Explore agent to check whether all documentation layers are consistent with the current codebase. The agent should check:

- **XML doc comments**: Do all public methods/types added since the last release have `<summary>` tags?
- **Root README.md**: Does the features list reflect current capabilities?
- **CLI README** (`tools/KernSmith.Cli/README.md`): Are all CLI commands and flags documented?
- **UI README** (`apps/KernSmith.Ui/README.md`): Does the feature list match current UI?
- **DocFX docs** (`docs/`): Are doc pages consistent with the code?
- **Reference docs** (`reference/`): Any stale references?
- **NuGet descriptions**: Do `.csproj` `<Description>` fields reflect current package capabilities?

Report findings to the user. If there are gaps, list them and ask whether to fix them now or defer. Do not auto-fix — the user decides scope.

### 7. Build and test

Run the full build and test suite to catch issues before shipping.

```bash
dotnet build KernSmith.sln -c Release
dotnet test KernSmith.sln -c Release --no-build
```

If either fails, stop and report the error to the user. Do not proceed to commit with a broken build.

### 8. Commit

Invoke the `/commit` skill to handle staging, message drafting, and committing. It will follow all project conventions (selective staging, no Co-Authored-By, HEREDOC formatting, phase doc updates).

### 9. Push and PR

```bash
git push -u origin version/<version>
```

Create the PR. Body should be summary bullets only — no test plan section, no "Generated with Claude Code" footer.

```bash
gh pr create --title "Bump version to <version>" --body "$(cat <<'EOF'
## Summary
- Version bump to <version> in Directory.Build.props
- Move Unreleased CHANGELOG entries to <version> section
- Update RELEASING.md version examples
EOF
)"
```

### 10. Merge

```bash
gh pr merge <pr-number> --merge
```

### 11. Remind about publishing

After the merge, tell the user:

> PR merged. To publish, go to **GitHub Actions → Publish Release → Run workflow** and enter version `<version>`. Or from the command line:
> ```
> git tag v<version> && git push origin v<version>
> ```

Also switch back to main and pull so the local repo is up to date:

```bash
git checkout main
git pull
```

## Edge cases

- **Dirty working tree**: Warn and stop. Don't mix release changes with uncommitted work.
- **Branch already exists**: If `version/<version>` already exists, warn the user and ask how to proceed.
- **Empty Unreleased section**: Add a minimal changelog entry rather than leaving the version section empty.
- **Merge conflicts**: If the PR can't auto-merge, tell the user and stop — don't force-merge.
- **No version argument**: Ask the user for the version number. Show the current version for reference.
