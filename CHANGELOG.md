# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.10.0] - 2026-03-28

### Added

- Multi-package NuGet publishing — all packages (core, rasterizers, integrations) publish from a single workflow
- Split publish workflow into parallel ubuntu/windows pack jobs for faster CI
- NuGet metadata (readme, tags, authors, URL) for rasterizer packages
- Updated RELEASING.md with multi-package architecture docs
- Comprehensive UI guide for docs site (layout, workflows, shortcuts, all features)
- Docs for `list-rasterizers` CLI command
- Integrations and Rasterizers sections on docs landing page

### Fixed

- Docs site logo now links to site root from any page
- Docs sidebar shows all sections instead of hiding behind section scoping
- CLI tests use `dotnet exec` instead of `dotnet run` — fixes net10.0 test host hang and speeds up tests ~20x

### Changed

- Directory.Build.props defaults IsPackable=false; each package opts in explicitly
- Package READMEs use absolute GitHub URL instead of relative file path
- KniGum uses TargetFrameworks (plural) to fix pack failure
- FnaGum excluded from packing until build issues resolved

## [0.9.6] - 2026-03-28

### Added

- DirectWrite rasterizer backend with sizing fixes (Phase 78C)
- Rasterizer abstraction foundation — pluggable IRasterizer backends (Phase 78A)
- README for bmfont comparison tools

### Changed

- Channel settings added to fire.bmfc for BMFont64 outline separation
- Phase 99 created for remaining BMFont parity gaps

## [0.9.5] - 2026-03-23

### Fixed

- Outline counter-fill: EDT outlines no longer expand into letter counters (holes in e/o/a/H) — uses BFS flood-fill to distinguish exterior vs counter pixels
- Synthetic bold counter bloat: replaced FT_GlyphSlot_Embolden with lighter FT_Outline_Embolden (ppem/36) to preserve counters at large sizes

### Changed

- Renamed reference/gum-forms-cheatsheet.md to REF-07-gum-forms-cheatsheet.md
- Updated REF-09 with synthetic bold/italic/outline rendering findings
- Moved completed plans (phase 76, 76B, docfx) to plan/done/

## [0.9.4] - 2026-03-22

### Fixed

- Publish workflow: add `-f net10.0` to `dotnet publish` for CLI and UI binaries (multi-target projects require explicit framework for self-contained publish)

## [0.9.3] - 2026-03-22

### Added

- GitHub Release workflow with CLI + UI binaries for 5 platforms (win-x64, win-arm64, linux-x64, osx-arm64, osx-x64)
- Manual dispatch trigger for publish workflow (Actions → Run workflow)
- RELEASING.md documenting the full release process
- publish.bat as CLI alternative for tagging releases

### Changed

- Bumped all GitHub Actions to latest versions (checkout v6, setup-dotnet v5, upload/download-artifact v7/v8)
- NuGet license uses PackageLicenseExpression instead of PackageLicenseFile

### Fixed

- CA1416 warnings: added [SupportedOSPlatform("windows")] to Registry-accessing methods
- CI Ubuntu hang: continue-on-error for known .NET test host hang
- NU5033: resolved duplicate license property conflict between Directory.Build.props and csproj

## [0.9.2] - 2026-03-22

### Added

- XML doc comments across core library (95%+ coverage), CLI tool, and UI app
- UI README.md with architecture overview and build instructions
- Namespace-level documentation (8 NamespaceDoc.cs files)
- docfx API reference site with GitHub Pages deployment
- GitHub Actions workflow for automatic doc site builds

### Changed

- Unified versioning: single `<Version>` in Directory.Build.props shared by all projects
- CLI and UI version displays now read from assembly (no more hardcoded versions)
- publish.bat uses wildcard for .nupkg filename

### Fixed

- CI hang on Ubuntu: added `--blame-hang` timeout to dotnet test
- LICENSE copyright updated to "KernSmith contributors"

## [0.9.1] - 2026-03-22

### Changed

- License changed from proprietary to MIT

## [0.9.0] - 2026-03-20

### Added

- Package icon added and wired into NuGet package
- CLI tool now has a Windows application icon
- `BmFont.FromConfig()` -- generate a bitmap font directly from a .bmfc config file
- `BmFontResult.FntText`, `.FntXml`, `.FntBinary` -- convenience properties for in-memory .fnt access
- `BmFontResult.GetPngData()`, `.GetTgaData()`, `.GetDdsData()` -- encode atlas pages to byte arrays
- `BmFontResult.ToBmfc()` -- round-trip config output from a generation result
- `BmFontBuilder.FromConfig()` -- load a .bmfc config as the builder starting point
- `AtlasPage.ToTga()`, `.ToDds()` -- encode individual atlas pages to TGA or DDS format
- `ToFile()` now also writes a .bmfc config file alongside .fnt and atlas pages

### Changed

- Version bumped from 0.8.0 to 0.9.0
- Assets moved to `assets/` folder (icon, favicons, social preview)
- .gitignore updated for generated font files
- License changed from MIT to proprietary
- Benchmark CI workflow changed to manual-only trigger

## [0.8.0] - 2026-03-20

### Added

- BMFont-compatible bitmap font generation from TTF/OTF/WOFF files
- Text, XML, and binary .fnt output formats
- PNG, TGA, and DDS texture atlas output
- Signed Distance Field (SDF) rendering
- Layered glyph effects: outline, gradient, shadow, glow
- Color font support (COLRv0/CPAL, sbix, CBDT)
- Variable font support
- Font subsetting with custom character sets
- Channel packing for multi-font atlas optimization
- Super-sampling for higher quality rasterization
- GPOS kerning pair extraction
- Multi-page atlas support with configurable max texture size
- In-memory pipeline with optional file output
- Fluent builder API for font generation configuration
- CLI tool for batch bitmap font generation
- Extended metadata in .fnt output
- BmFont .fnt reader for loading existing bitmap fonts
- Library: `BmfcConfigReader` and `BmfcConfigWriter` for programmatic .bmfc file handling
- CLI: `init` command to generate .bmfc config files from CLI flags
