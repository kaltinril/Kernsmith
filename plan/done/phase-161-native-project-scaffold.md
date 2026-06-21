# Phase 161 — Native Rasterizer: Project Scaffold & Binary Reader

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 160 (design decisions)

## Goal

Create the `KernSmith.Rasterizers.Native` project with the binary font reader foundation, table directory parser, and core infrastructure.

## Scope

### Project Setup
- Create `src/KernSmith.Rasterizers.Native/KernSmith.Rasterizers.Native.csproj`
  - `net10.0`, `<IsTrimmable>true</IsTrimmable>`, `<IsAotCompatible>true</IsAotCompatible>`
  - Reference only `KernSmith` core project (for `IRasterizer`, types)
  - Zero external NuGet dependencies
- Add to solution file
- Add `Native` value to `RasterizerBackend` enum in `src/KernSmith/Config/RasterizerBackend.cs`
- Create `FontFormatException` in `src/KernSmith/Exceptions/FontFormatException.cs` (root `KernSmith` namespace per convention)
- Create test project `tests/KernSmith.Rasterizers.Native.Tests/`

### Binary Reader
- `FontReader` class — wraps `ReadOnlyMemory<byte>` with big-endian reading helpers
  - `ReadUInt8`, `ReadInt8`
  - `ReadUInt16`, `ReadInt16` (big-endian)
  - `ReadUInt32`, `ReadInt32` (big-endian)
  - `ReadFixed` (16.16 fixed point)
  - `ReadFWord`, `ReadUFWord` (font design units)
  - `ReadF2Dot14` (2.14 fixed point, for composite glyph transforms)
  - `ReadTag` (4-byte ASCII tag)
  - `ReadBytes(int count)`
  - `Seek(int offset)`, `Skip(int count)`
  - Position tracking, bounds checking
- Use `BinaryPrimitives.ReadUInt16BigEndian` etc. from `System.Buffers.Binary`
- All methods work on `ReadOnlySpan<byte>` — zero allocation

### Table Directory Parser
- Parse the offset table (sfnt version, numTables, searchRange, entrySelector, rangeShift)
- Validate sfnt version: `0x00010000` (TrueType) or `OTTO` (CFF)
- Parse table records: tag, checksum, offset, length
- `TableProvider` class — lazy access to raw table bytes by tag
- Throw `FontFormatException` for invalid/corrupt data

### Font Validation
- Verify required tables exist: `head`, `cmap`, `hhea`, `hmtx`, `maxp`, `name`, `OS/2`, `post`
- For TrueType: verify `glyf` and `loca` exist
- For CFF: verify `CFF ` or `CFF2` exists (but defer CFF parsing to Phase 166)

### Core Table Parsers

The Native rasterizer needs its own parsers for tables required by downstream phases:

- **`head` table**: `unitsPerEm`, `indexToLocFormat`, `macStyle`, global bounding box
  - Required by Phase 162 (loca format selection) and Phase 163 (scaling)
- **`hhea` table**: `ascender`, `descender`, `lineGap`, `numberOfHMetrics`
  - Required by Phase 165 (font metrics)
- **`hmtx` table**: per-glyph `advanceWidth` and `leftSideBearing`
  - Required by Phase 165 (glyph metrics)
- **`OS/2` table**: `sTypoAscender`, `sTypoDescender`, `usWinAscent`, `usWinDescent`, `sxHeight`, `sCapHeight`
  - Required by Phase 165 (font metrics) and Phase 168 (small caps)
- **`cmap` table**: Format 4 (BMP) and Format 12 (full Unicode) subtables
  - Required by Phase 165 (codepoint → glyph index lookup)

These are self-contained parsers (not shared with the core `KernSmith` font reading pipeline) using the span-based binary reader from this phase.

### NativeRasterizer Shell
- Create `NativeRasterizer : IRasterizer` class with stubbed methods
- Implement `IRasterizerCapabilities` (initially minimal — `SupportedAntiAliasModes = [None, Grayscale]`, everything else false)
- Register via `[ModuleInitializer]` as `RasterizerBackend.Native`
- `LoadFont` stores font bytes and initializes `TableProvider`

## Implementation Notes

- The existing `TtfParser` in `KernSmith.Font` parses many tables already, but the Native rasterizer needs its OWN parser because:
  1. It must be self-contained (no dependency on shared font reading code paths)
  2. It needs lower-level access to raw table bytes for glyf/loca
  3. Different performance characteristics (zero-copy span-based)
- However, the Native rasterizer CAN reuse the existing parsed `FontInfo` for metrics/kerning that the core pipeline already provides

## Testing

- Binary reader: round-trip tests for all data types
- Table directory: parse Roboto-Regular.ttf, verify all expected tables found
- Table checksums: validate checksums match
- Invalid data: verify `FontFormatException` for truncated/corrupt files
- Edge cases: TTC fonts (multiple faces), fonts with unusual table ordering

## Success Criteria

- [ ] `KernSmith.Rasterizers.Native` project builds with zero NuGet dependencies
- [ ] Binary reader correctly handles big-endian data
- [ ] Table directory correctly parsed for Roboto-Regular.ttf
- [ ] `NativeRasterizer` registers and is selectable via `RasterizerBackend.Native`
- [ ] `RasterizerBackend.Native` enum value added
- [ ] `FontFormatException` created
- [ ] Core table parsers (`head`, `hhea`, `hmtx`, `OS/2`, `cmap`) produce correct values for Roboto-Regular.ttf
- [ ] All tests pass
- [ ] Trimming and AOT analyzers produce no warnings
