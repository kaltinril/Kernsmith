# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
