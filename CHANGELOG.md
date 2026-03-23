# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
