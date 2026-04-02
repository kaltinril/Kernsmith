# Phase 178 — Native Rasterizer: WOFF/WOFF2 Decompression

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 161 (binary reader, table directory)

## Goal

Handle WOFF and WOFF2 compressed font files natively in the rasterizer, so users can load web fonts directly without external decompression.

## Background

WOFF (Web Open Font Format) wraps sfnt fonts with per-table zlib compression. WOFF2 uses Brotli compression with additional preprocessing. The main KernSmith pipeline already handles WOFF decompression before passing to rasterizers, but native support means the rasterizer can work standalone.

## Scope

### WOFF 1.0
- Magic: `wOFF` (0x774F4646)
- Header: sfntVersion, length, numTables, totalSfntSize, etc.
- Per-table: tag, offset, compLength, origLength, origChecksum
- Decompression: zlib (deflate) via `System.IO.Compression.DeflateStream`
- If compLength == origLength, table is stored uncompressed

### WOFF 2.0
- Magic: `wOF2` (0x774F4632)
- Uses Brotli compression (available in .NET via `System.IO.Compression.BrotliDecoder`)
- Additional preprocessing transforms before compression:
  - Transform type 0 for `glyf` and `loca`: triplet encoding, reconstructed loca
  - Transform type 0 for `hmtx`: proportional/monospaced optimization
- Table directory uses variable-length integers (UIntBase128)
- Tables may be merged into a single Brotli stream

### WOFF2 Transform: glyf/loca Reconstruction
The most complex part. WOFF2 re-encodes glyf data:
1. Separate streams for flags, glyphs, composites, bboxes, instructions
2. Triplet encoding for glyph points (more compact than raw glyf)
3. loca table is reconstructed from decoded glyf sizes
4. Must decode back to standard glyf/loca format

### Integration

- Detect font format in `NativeRasterizer.LoadFont` by checking first 4 bytes:
  - `0x00010000` or `OTTO` → raw sfnt (existing path)
  - `wOFF` → WOFF 1.0 → decompress → parse as sfnt
  - `wOF2` → WOFF 2.0 → decompress + detransform → parse as sfnt
- The decompressed sfnt bytes feed into the existing table parser

### Dependencies

- `DeflateStream` (System.IO.Compression) — built into .NET, no NuGet needed
- `BrotliDecoder` (System.IO.Compression) — built into .NET 6+, no NuGet needed
- Both are part of the base class library — this maintains zero-NuGet-dependency goal

## Testing

- Load WOFF 1.0 font: verify tables decompressed correctly
- Load WOFF 2.0 font: verify Brotli decompression and glyf transform
- Compare output: WOFF/WOFF2 font should produce identical rasterization as raw TTF/OTF
- Round-trip: decompress → parse → rasterize → compare with original font
- Edge cases: WOFF with uncompressed tables, WOFF2 with transformed glyf

## Success Criteria

- [ ] WOFF 1.0 fonts load and rasterize correctly
- [ ] WOFF 2.0 fonts load and rasterize correctly (including glyf transform)
- [ ] Output matches raw sfnt version of same font
- [ ] No external NuGet dependencies added (uses BCL only)
- [ ] All tests pass
