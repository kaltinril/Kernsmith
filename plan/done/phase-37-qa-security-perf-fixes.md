# Phase 37 — QA, Security & Performance Fixes

> **Status**: Complete
> **Created**: 2026-04-01
> **Depends on**: Phase 32d (synthetic bold/italic)
> **Source**: Full codebase review — correctness, security, efficiency agents

## Goal

Fix validated bugs, security vulnerabilities, and performance issues discovered during a comprehensive code review.

---

## Correctness Fixes

### C1 (HIGH): GetFontMetrics ignores SDF mode — inconsistent `aa` calculation

**File**: `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs:278`

`RasterizeGlyph` (line 77) and `GetGlyphMetrics` (line 198) both use:
```csharp
int aa = options.Sdf ? 1 : Math.Max(1, options.SuperSample);
```
But `GetFontMetrics` (line 278) uses:
```csharp
int aa = Math.Max(1, options.SuperSample);
```
When `Sdf = true` and `SuperSample > 1`, font-level metrics (ascent, descent, lineHeight) are computed at a different scale than per-glyph metrics, causing misalignment.

**Fix**: Change line 278 to `int aa = options.Sdf ? 1 : Math.Max(1, options.SuperSample);`

### C2 (MEDIUM): ItalicPostProcessor does not adjust Advance

**File**: `src/KernSmith/Rasterizer/ItalicPostProcessor.cs:76-82`

The bitmap width increases from shear but advance stays the same. Consecutive italic characters can overlap. FreeType also doesn't adjust advance for oblique, so this may be intentional — but for bitmap font output where the advance determines spacing, it should be adjusted.

**Fix**: Increase advance by `extraWidth` or document as known behavior.

### C3 (MEDIUM): No guard against double-bold/italic

**File**: `src/KernSmith/BmFont.cs:239`

The post-processor filter skips `OutlinePostProcessor`, `GradientPostProcessor`, `ShadowPostProcessor` but not `BoldPostProcessor` or `ItalicPostProcessor`. If a user sets `options.Bold = true` AND adds `new BoldPostProcessor()`, bold is applied twice.

**Fix**: Add `BoldPostProcessor` and `ItalicPostProcessor` to the skip filter, or log a warning.

---

## Security Fixes

### S1 (HIGH): WOFF decompressor missing destination bounds check

**File**: `src/KernSmith/Font/WoffDecompressor.cs:131`

No validation that `dataOffset + entry.OrigLength <= result.Length` before writing. Crafted WOFF with mismatched `totalSfntSize` vs individual table lengths causes uncontrolled exception.

**Fix**: Add bounds check before the `AsSpan` call.

### S2 (HIGH): Cmap Format 12 resource exhaustion

**File**: `src/KernSmith/Font/TtfParser.cs:568-586`

A group with `startCharCode=0, endCharCode=0x10FFFF` iterates 1.1M times when `_requestedCodepoints` is null. Multiple such groups can exhaust memory.

**Fix**: Add a maximum cmap entry count limit (e.g., 200k). Also validate `numGroups` against remaining subtable size.

### S3 (MEDIUM): Integer overflow in WOFF bounds check

**File**: `src/KernSmith/Font/WoffDecompressor.cs:125`

`entry.Offset + entry.CompLength` can wrap negative for large `int` values, bypassing the `> woffData.Length` check.

**Fix**: Use `(long)entry.Offset + entry.CompLength > woffData.Length`.

### S4 (MEDIUM): Table directory offsets not validated

**File**: `src/KernSmith/Font/TtfParser.cs:132-140`

Table offsets and lengths stored without checking they fit within `_data`. Causes uncontrolled exceptions on malformed fonts.

**Fix**: Skip entries where `tableOffset < 0 || tableLength < 0 || tableOffset + tableLength > data.Length`.

### S5 (MEDIUM): GPOS parser missing slice bounds checks

**File**: `src/KernSmith/Font/TtfParser.cs:754-780`

Multiple `Slice` calls on untrusted offsets from GPOS table without bounds validation. Also `extensionOffset` cast from `uint32` to `int` can go negative.

**Fix**: Add bounds guards before each `Slice` call in the GPOS chain.

### S6 (MEDIUM): Font name used in CLI output path without sanitization

**File**: `tools/KernSmith.Cli/Commands/GenerateCommand.cs` / `src/KernSmith/Output/FileWriter.cs:75`

Font family name from TTF `name` table used in output path. Path traversal sequences possible.

**Fix**: Sanitize with `Path.GetInvalidFileNameChars()` stripping.

### S7 (LOW): Kern table infinite loop if subtableLength == 0

**File**: `src/KernSmith/Font/TtfParser.cs:716`

`offset += subtableLength` never advances when `subtableLength == 0`, causing infinite loop.

**Fix**: `if (subtableLength < 6) break;`

---

## Performance Fixes

### P1 (HIGH): BoldPostProcessor MathF.Sqrt in inner loop

**File**: `src/KernSmith/Rasterizer/BoldPostProcessor.cs:78`

`MathF.Sqrt(distSq)` called per-pixel per-kernel-tap. For strength=2, up to 25 sqrt calls per pixel.

**Fix**: Precompute a kernel lookup table of `(kx, ky, falloff)` tuples before the pixel loops.

### P2 (MEDIUM): BoxBlur O(radius) per pixel

**File**: `src/KernSmith/Rasterizer/ShadowPostProcessor.cs:197-234`

Inner `k` loop iterates `2*radius+1` times per pixel per pass. A sliding-window running sum makes this O(1) per pixel regardless of radius.

**Fix**: Replace inner loop with sliding-window accumulator.

### P3 (MEDIUM): Repeated .Select().ToList() in main pipeline

**File**: `src/KernSmith/BmFont.cs:218-255`

Up to 6 intermediate list allocations per generation run. Each creates a new `List<RasterizedGlyph>`.

**Fix**: Consolidate into a single for-loop that transforms each glyph through all applicable steps.

---

## Success Criteria

- [ ] All existing tests pass
- [ ] C1: SDF + SuperSample metrics test added and passes
- [ ] S1-S2: Malformed font parsing throws `FontParsingException` instead of uncontrolled crashes
- [ ] S7: Kern table with subtableLength=0 does not hang
- [ ] P1: BoldPostProcessor no longer calls Sqrt per-pixel
- [ ] Comparison images generated to verify no visual regressions
