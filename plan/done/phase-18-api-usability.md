# Phase 18: API Usability

**Status:** Complete

## Goal

Add convenience APIs to reduce boilerplate for common workflows: config-driven generation, in-memory format access, and round-trip config output.

## What was implemented

- `BmFont.FromConfig(string bmfcPath)` and `BmFont.FromConfig(BmfcConfig config)` -- one-liner generation from .bmfc files
- `BmFontResult.FntText`, `.FntXml`, `.FntBinary` -- convenience properties that wrap the existing formatter calls
- `BmFontResult.GetPngData()`, `.GetTgaData()`, `.GetDdsData()` -- encode all atlas pages (or a single page by index) to byte arrays
- `BmFontResult.ToBmfc()` -- serialize the generation options back to .bmfc format
- `BmFontBuilder.FromConfig(string bmfcPath)` and `.FromConfig(BmfcConfig config)` -- seed the builder from an existing config, then override individual settings
- `AtlasPage.ToTga()`, `.ToDds()` -- per-page encoding to TGA and DDS formats
- `ToFile()` now writes a .bmfc config file alongside .fnt and atlas pages when source options are available
