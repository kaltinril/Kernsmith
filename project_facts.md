# Project Facts

Source of truth for statements about the project. One short fact per line. Update in place; delete facts that become false.

## Identity

- Brand is stylized **KernSmith** (capital S).
- The GitHub repo is canonically `kaltinril/Kernsmith` (lowercase s) — per the API `full_name` and the git remote — so the brand and the repo name genuinely disagree.
- Jeremy's GitHub username: `kaltinril`. Vic = `vchelaru` (Gum author).
- Version source of truth is `Directory.Build.props` `<Version>`; all projects inherit it.

## Repo & Git

- `/tests/bmfont-compare/` is gitignored wholesale (`.gitignore` line 23).
- A small set of generic-font `.bmfc` configs there are tracked as force-added exceptions — as of 2026-08-02: `fire`, `plain`, `plain-nosmoothing`, `sdf`, `shadow-channel`, `shadow-variant`, `skyline-zerospacing`, `supersample`.
- A new `.bmfc` added with plain `git add` is silently dropped: `git status` does not even list it as untracked, so it looks committed but never reaches the repo.
- The remaining ~17 local configs reference machine-specific Gum fonts and are intentionally untracked.

## Ownership & Dependencies

- All four Gum integration projects (GumCommon, KniGum, MonoGameGum, FnaGum) were removed from this repo; Vic owns them, source lives in his repo under `Integrations/KernSmith/`.
- Core `KernSmith` stays Jeremy's (co-owned on NuGet, but authors/projectUrl remain kaltinril).
- The UI gets `KernSmith.MonoGameGum` transitively via `Gum.Themes.Editor.MonoGame`, not via a local ProjectReference.
- Dependabot vulnerability alerts are **enabled** (verified 2026-08-02: `GET /repos/kaltinril/Kernsmith/vulnerability-alerts` → 204; 404 would mean disabled).

## Feature State

- `.bmfc` channel config (`alphaChnl`/`redChnl`/`greenChnl`/`blueChnl`) is honored on the CLI/core path — shipped in 0.15.2 (PR #128); `BmfcConfigReader` parses them into `ChannelConfig`, gated by `BmFont.ShouldApplyChannelConfig`.
- The desktop UI also carries channel config end-to-end now (`EffectsViewModel.Channels` → `GenerationRequest.Channels` → `GenerationService` sets `options.Channels`); the older "UI discards per-channel config" gap is closed.

## CodeQL

- As of 2026-08-02 (after PRs #196 and #197): **4 open, 38 dismissed**.
- The 4 open are deliberate — `y0 == y1` in `ScanlineRasterizer.cs:79` and `OutlineFlattener.cs:163`, plus the two `OutlineFlattenerTests` assertions that pin the invariant.
- `paths` / `paths-ignore` do **nothing** for C# here: they apply only when CodeQL analyses *without building*, and `codeql.yml` builds the solution explicitly.
- Code-scanning dismissal comments are capped at **280 characters**; longer ones return HTTP 422, which looks like a generic failure if stderr is swallowed.
- A dismissal comment **cannot be edited in place** ("Alert is already dismissed", HTTP 400) — PATCH `state=open` first, then re-dismiss.
- PR-level CodeQL results live on `refs/pull/N/merge`. `refs/pull/N/head` has **no analyses at all**, so querying it returns a meaningless empty set that reads like a pass.
- The severity filter is active and measurable: analysis rule count is 128 (164 unfiltered).

## Regression Harness

- `tests/bmfont-compare/` exercises the CLI `GenerateAll` path only, not the UI `GenerationService`.
- `GenerateAll` skips a (config, backend) pair when the backend declines a capability the config requests — SDF on GDI/DirectWrite/Native.
- `dotnet run` exits 1 for **both** a build failure and a partial generation, so the two must be distinguished by building as a separate step.
