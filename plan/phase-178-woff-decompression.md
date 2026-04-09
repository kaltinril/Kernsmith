# Phase 178 — Native Rasterizer: WOFF/WOFF2 Decompression

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 161 (binary reader, table directory)

## Goal

Handle WOFF and WOFF2 compressed font files natively in the rasterizer, so users can load web fonts directly without external decompression.

## Background

WOFF (Web Open Font Format) wraps sfnt fonts with per-table zlib compression. WOFF2 uses Brotli compression with additional preprocessing. The main KernSmith pipeline already handles WOFF decompression before passing to rasterizers, but native support means the rasterizer can work standalone.

## Research Notes (2026-04-09)

### Why WOFF2 isn't just "add Brotli decompression"

Brotli decompression itself is trivial — `System.IO.Compression.BrotliDecoder` ships with .NET (since Core 2.1). The complexity is in the **WOFF2 pre-processing transforms**. Before Brotli-compressing, the WOFF2 encoder applies content-aware transforms to certain tables. After Brotli decompression, the output is NOT a valid sfnt — it must be reverse-transformed:

1. **`glyf`/`loca` reconstruction** (the hard part) — WOFF2 re-encodes glyph outlines using "triplet encoding" with separate substreams for flags, points, composites, bounding boxes, and instructions. All must be decoded back to standard glyf format, then loca is reconstructed from decoded glyph sizes.
2. **`hmtx` transform** — simpler optimization for horizontal metrics.
3. **UIntBase128 variable-length integers** in the table directory.
4. **All tables may be merged into a single Brotli stream** (unlike WOFF1 where each table is a separate zlib stream).

Full pipeline: `WOFF2 bytes → Brotli decompress → reverse glyf/loca/hmtx transforms → reconstruct valid sfnt bytes`

### Reference implementation: LayoutFarm/Typography (MIT)

GitHub: https://github.com/LayoutFarm/Typography

This is a managed C# font library (MIT license) that has a **complete WOFF2 decoder**:

- **Main file**: `Typography.OpenFont/WebFont/Woff2Reader.cs` — header parsing, table directory, glyf/loca reconstruction, triplet encoding
- **Key internals**: `TransformedGlyf` class with `ReconstructGlyfTable()`, `BuildSimpleGlyphStructure()`, `ReadCompositeGlyph()`, `TripleEncodingTable` (full 128-entry lookup per W3C spec), `255UInt16` variable-length decoding, bbox/instruction stream handling
- **Brotli is decoupled**: injected via delegate (`BrotliDecompressStreamFunc`), so their Brotli code isn't needed — we'd use .NET's built-in `BrotliDecoder`
- **License**: MIT (2019-present, WinterDev) — compatible with KernSmith's MIT license
- **Maturity**: Typography has been around since ~2015 with active use; WOFF2 path is likely well-exercised

### WOFF2 spec is frozen

WOFF2 is a **W3C Recommendation finalized in 2018**. The spec will not change — no new features, no breaking changes. The triplet encoding, transform tables, and UIntBase128 format are all permanent. This is relevant because it means there's no ongoing maintenance burden from spec evolution, regardless of implementation approach.

### Implementation approach (unresolved: copy/adapt vs rewrite-from-reference)

**Option A — Extract and adapt `Woff2Reader.cs` (recommended)**
- Copy the file, strip Typography's type dependencies, have it output raw sfnt bytes (which `WoffDecompressor.Decompress()` already expects)
- Add MIT attribution in file header
- Pro: battle-tested triplet encoding and substream reconstruction; fiddly edge cases (composite glyphs, bboxes, instruction streams) already handled
- Pro: frozen spec means no need to track upstream changes
- Pro: bugs found in their code benefit us; bugs in a rewrite are ours alone
- Con: need to untangle ~5-8 supporting type dependencies (Glyf, GlyphLocations, Glyph, GlyphPointF, Bounds, ByteOrderSwappingBinaryReader)

**Option B — Rewrite using Typography + W3C spec as reference**
- Implement from scratch in `WoffDecompressor.cs`, consulting their code for the tricky parts
- Pro: cleaner integration, no type adaptation needed, output is directly raw sfnt bytes
- Pro: simpler code since we don't need to populate glyph objects — just reassemble bytes
- Con: risk of subtle reimplementation bugs in triplet encoding (128-entry lookup table, variable-length coordinate decoding)
- Con: more effort for equivalent result

**Either way**: no new NuGet dependencies needed. Brotli and Deflate are both in `System.IO.Compression` (BCL).

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
