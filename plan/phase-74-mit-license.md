# Phase 74: Switch to MIT License

> **Status**: Complete
> **Created**: 2026-03-22

## Purpose
Change all license references from proprietary to MIT across the entire project.

## Tasks

### Wave 1 — Legal File
- [x] **LICENSE** — Replace proprietary text with standard MIT license text, update copyright to `Copyright (c) 2026 Kaltinril (Jeremy Swartwood)`

### Wave 2 — Project Configuration
- [x] **src/KernSmith/KernSmith.csproj** (line 13) — Change `<PackageLicenseFile>LICENSE</PackageLicenseFile>` to `<PackageLicenseExpression>MIT</PackageLicenseExpression>`
- [x] **src/KernSmith/KernSmith.csproj** (line 21) — Can remove the `<None Include="../../LICENSE" Pack="true" PackagePath="/" />` line since SPDX expression replaces it (or keep it for inclusion — your call)

### Wave 3 — Documentation
- [x] **README.md** (line ~498) — Change `Proprietary. See [LICENSE](LICENSE) for details.` to `MIT. See [LICENSE](LICENSE) for details.`
- [x] **CLAUDE.md** (line 48) — Change `**License**: Proprietary (see LICENSE)` to `**License**: MIT (see LICENSE)`
- [x] **CHANGELOG.md** — Add entry noting license changed back to MIT

### Wave 4 — Plan Docs
- [x] **plan/master-plan.md** (line 22) — Already says MIT for "Our code: MIT" — confirm it's correct
- [x] **plan/master-plan.md** (line 278) — Update Decision #3 to mark as resolved: MIT
- [x] **plan/phase-69-ui-final-polish.md** (lines 180, 218) — Remove `[LICENSE-DEPENDENT]` notes, resolve as MIT
- [x] **plan/done/kernsmith-vision.md** (line 134) — Already says open source — confirm correct

### Wave 5 — Memory
- [x] **memory/project_licensing.md** — Update to reflect MIT decision is finalized

### Wave 6 — Cleanup
- [x] Delete stale generated nuspec: `src/KernSmith/obj/Release/KernSmith.0.9.0.nuspec` (will regenerate on next build)
- [x] Verify `.github/workflows/publish.yml` doesn't need changes (it doesn't)
