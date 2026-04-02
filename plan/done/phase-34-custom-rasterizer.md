# Phase 34 — Custom Pure C# Rasterizer (From Scratch)

> **Status**: Complete (Superseded)
> **Created**: 2026-03-30
> **Closed**: 2026-04-01
> **Depends on**: Phase 30 (FreeType extraction), Phase 32 (StbTrueType plugin for comparison)
> **Related**: Phase 32 (StbTrueType)
> **Superseded by**: Phases 160–179 (comprehensive pure C# rasterizer plan)

## Goal

Investigate and potentially implement a fully custom, pure C# TTF rasterizer owned entirely by KernSmith. This would eliminate all third-party rasterization dependencies and give complete control over the rendering pipeline.

## Motivation

- **Zero dependencies** — no StbTrueTypeSharp, no FreeTypeSharp, no licensing concerns whatsoever
- **Full control** — optimize specifically for bitmap font atlas generation (not general-purpose text rendering)
- **Educational value** — deep understanding of the rasterization pipeline
- **Customization** — can add KernSmith-specific optimizations (batch glyph rendering, atlas-aware rasterization)

## Scope Boundary

### IN scope

- TrueType outlines only (glyf table, quadratic beziers)
- Simple and composite glyphs
- Grayscale8 output
- Font metrics (ascent, descent, lineHeight)
- Glyph metrics (bearingX, bearingY, advance, width, height)

### OUT of scope (explicit)

- **CFF/CFF2 outlines** (cubic beziers, Type 2 charstrings) — throw `RasterizationException` with clear message for .otf with OTTO magic
- **Hinting/grid-fitting** — skip entirely (matches stb_truetype approach)
- **Variable font axis application**
- **Color fonts** (COLR/CPAL)
- **SDF rendering** (defer to post-processors or other backends)
- **Synthetic bold/italic** (outline dilation/shear — complex, defer)

## IRasterizerCapabilities Mapping

```
SupportsColorFonts = false
SupportsVariableFonts = false
SupportsSdf = false
SupportsOutlineStroke = false
HandlesOwnSizing = false
SupportsSystemFonts = false
SupportedAntiAliasModes = [None, Grayscale]
```

## WASM Constraints

The custom rasterizer must satisfy the same WASM constraints as Phase 32 (see Phase 31 research — `reference/REF-11-wasm-restrictions.md`):

- **No native dependencies** — pure C# only (already the goal)
- **No `Parallel.ForEach`** — throws `PlatformNotSupportedException` in single-threaded WASM
- **No `.Result` / `.Wait()` / `Thread.Sleep`** — deadlocks the browser thread
- **Use `ArrayPool<byte>`** for glyph bitmap buffers — WASM heap never shrinks, so buffer reuse prevents permanent growth
- **Set `IsTrimmable` and `IsAotCompatible`** in the csproj — required for Blazor WASM publishing
- **No `Reflection.Emit` or `DynamicMethod`** — blocked in AOT

## What KernSmith Already Has

KernSmith's TTF parser already handles these tables:
- `head` — font header, units per em
- `hhea` — horizontal header, ascent/descent
- `hmtx` — horizontal metrics (advance widths, left side bearings)
- `cmap` — character-to-glyph mapping
- `kern` — legacy kerning pairs
- `GPOS` — OpenType kerning pairs
- `OS/2` — weight class, panose, typo metrics, x-height, cap height
- `name` — font family name, style, version strings

**What's missing for rasterization:**
- `glyf` — glyph outline data (quadratic bezier contours, composite glyphs)
- `loca` — glyph data offsets (index into `glyf` table)
- `maxp` — glyph count, max points/contours, max component depth
- Scanline rasterizer / coverage calculation
- Composite glyph assembly (glyphs made of other glyphs with transforms)

## Component Inventory

### `maxp` table parser
Needed for glyph count, max points/contours, max component depth.

### `loca` table parser
Two formats: short (uint16*2) and long (uint32), selected by `HeadTable.IndexToLocFormat` (already parsed).

### `glyf` table parser

**Simple glyphs:**
- `numberOfContours` (int16), `endPtsOfContours`, `instructionLength` (skip), flags (packed with repeat), x-coords (delta-encoded), y-coords (delta-encoded)

**Composite glyphs:**
- Component flags, glyph index, transform args, optional scale/rotation matrix

**Flag byte encoding:**
- bit 0 = on-curve
- bit 1 = x-short
- bit 2 = y-short
- bit 3 = repeat
- bit 4 = x-same-or-positive
- bit 5 = y-same-or-positive

### Contour winding direction
Outer = clockwise, inner = counter-clockwise (non-zero winding rule).

### Implicit on-curve points
Between consecutive off-curve points, insert midpoint. **THIS IS A COMMON IMPLEMENTATION PITFALL.**

### Phantom points
TrueType adds 4 phantom points per glyph (for metrics).

### Coordinate system
TrueType = Y-up, bitmap output = Y-down (must flip).

## Complexity Breakdown

| Component | Effort | Lines (est.) | Notes |
|-----------|--------|-------------|-------|
| `glyf` table parser | 1 week | 300-500 | Quadratic bezier contours, on/off-curve points, flags |
| `loca` table parser | 1 day | 50-100 | Short (16-bit) and long (32-bit) formats |
| `maxp` table parser | 1 day | 50-100 | Glyph count, max points/contours, max component depth |
| Bezier curve flattening | 2-3 days | 100-200 | Quadratic (TTF) is straightforward. CFF/cubic is out of scope. |
| Scanline rasterizer | 1-2 weeks | 500-1000 | The core algorithm. See Approach B below. |
| Composite glyphs | 3-5 days | 200-300 | Recursive glyph assembly with affine transforms |
| Font scaling/metrics | 2-3 days | 100-200 | Convert from font units to pixel coordinates |
| Anti-aliasing | Included | — | Both rasterizer approaches produce AA output naturally |
| **Total (no hinting)** | **3-5 weeks** | **~1500-2500** | Optimistic for first-time implementation. stb_truetype.h rasterization core alone is ~800 lines of dense C. |
| TrueType hinting VM | 3-6 months | 15,000+ | **NOT recommended** — enormous complexity |

## Rasterization Approaches

### Approach A: Oversampled Scanline Fill

1. Scale glyph outlines to Nx target size (e.g., 4x)
2. For each scanline, find edge crossings using winding number rule
3. Fill between crossings
4. Downsample NxN blocks to get coverage values (anti-aliasing)

**Pros:** Simple to understand and implement
**Cons:** Higher memory usage (N^2 per glyph), slower than exact coverage

### Approach B: Signed-Area Trapezoid (stb_truetype approach) — RECOMMENDED

1. Gather directed edges from glyph outlines, sort by top vertex
2. For each pixel-tall scanline, maintain an active edge list
3. For each active edge, compute signed-trapezoid areas extending rightward
4. Coverage values accumulate via cumulative sum — no oversampling needed

**Pros:** Exact coverage, single pass, memory efficient, battle-tested algorithm
**Cons:** More complex math, harder to debug

**Recommendation:** Approach B — it's what stb_truetype uses and produces superior quality at the same performance cost.

## Core Algorithm Pseudocode (Approach B — Signed-Area Trapezoid)

### Edge generation
- For each contour, iterate points. Lines produce one edge. Quadratic beziers flatten via adaptive subdivision (tolerance: 0.5 pixels).
- Each edge: directed segment (x0,y0) to (x1,y1), sort so y0 <= y1, store inversion flag.

### Edge sorting
- Sort all edges by minimum Y coordinate (y_top).

### Active edge list
- For each scanline (pixel row), add edges where y_top <= current_y, remove where y_bottom <= current_y.
- For each active edge, compute X intersection.

### Coverage calculation
- For each active edge crossing a pixel, compute signed area of trapezoid.
- Accumulate left-to-right via cumulative sum.
- Clamp to [0, 255] for 8-bit output.

### Reference
stb_truetype.h `stbtt__rasterize_sorted_edges` (~line 3400), `stbtt__fill_active_edges_new`, `stbtt__handle_clipped_edge`, `stbtt_FlattenCurves`.

### What About Hinting?

TrueType hinting is a full bytecode virtual machine (~40 opcodes) that modifies glyph outlines to snap to pixel boundaries. FreeType's implementation is ~15,000 lines. Auto-hinting (for fonts without hint programs) adds another ~10,000 lines.

**Decision: Skip hinting.** stb_truetype also skips hinting and produces acceptable results. At typical bitmap font sizes (16px+), the quality difference is minimal. Users needing hinted output use the FreeType backend.

## Sub-Phase Breakdown

| Sub-phase | Description |
|-----------|-------------|
| **34A** | glyf/loca/maxp table parsing + unit tests |
| **34B** | Outline extraction (bezier flattening, implicit on-curve points, composite glyphs) |
| **34C** | Scanline rasterizer core (active edge list, coverage accumulation) |
| **34D** | IRasterizer integration (LoadFont, RasterizeGlyph, metrics, registration) |
| **34E** | Validation and comparison testing |

## Implementation Plan

### Step 1: `glyf`, `loca`, and `maxp` table parsers (Sub-phase 34A)
- Add to existing `KernSmith.Font.Tables` namespace
- Parse simple glyphs (contours with on/off-curve points)
- Parse composite glyphs (component references with transforms)
- Verify glyph count matches `maxp.numGlyphs`

### Step 2: Outline extraction (Sub-phase 34B)
- Convert parsed glyph data to a list of bezier contours
- Scale from font units to pixel coordinates
- Insert implicit on-curve midpoints between consecutive off-curve points
- Apply composite glyph transforms

### Step 3: Scanline rasterizer (Sub-phase 34C)
- Implement signed-area trapezoid algorithm (Approach B)
- Output: greyscale bitmap (byte array, one byte per pixel)
- Use stb_truetype.h as reference (~800 lines of core algorithm code)

### Step 4: IRasterizer implementation (Sub-phase 34D)
- Create `KernSmith.Rasterizers.Custom/` project
- Implement full `IRasterizer` interface
- Register via `[ModuleInitializer]`
- Set `<IsTrimmable>true</IsTrimmable>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`, and `<IsAotCompatible>true</IsAotCompatible>` in the csproj

### Step 5: Validation (Sub-phase 34E)
- Compare output against FreeType and StbTrueType baselines
- Measure performance against both
- Test edge cases (see Testing Strategy below)

## Existing Infrastructure to Leverage

- **TtfParser**: table directory, `GetTable(uint tag)`, binary parsing helpers
- **HeadTable.IndexToLocFormat**: already parsed, needed by loca parser
- **RasterizerFactory.Register()**: registration point
- **Sample rasterizer**: `samples/KernSmith.Rasterizer.Example/MyRasterizer.cs`

## Testing Strategy

### Metrics comparison
For reference fonts at multiple sizes, verify `GlyphMetrics` match FreeType within +/-1 pixel.

### Bitmap similarity
SSIM or PSNR comparison at 12px, 16px, 24px, 48px, 96px (threshold: SSIM > 0.95).

### Golden master regression tests
Render fixed glyphs at fixed sizes, save as reference.

### Edge cases
- Empty glyphs (space)
- Composite glyphs (accented chars)
- Deeply nested composites
- Overlapping contours
- Very small (8px) and very large (200px) sizes
- Negative bearings

### Table parsing
- Verify glyph count matches `maxp.numGlyphs`
- Verify contour counts correct

### Performance
- Time per glyph, compare against FreeType (target: within 3-5x)

### Reference fonts
- Roboto (`tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf`)
- Noto Sans
- DejaVu Sans

## Known Risks

- **Edge cases in glyf format**: overlapping contours, degenerate beziers, zero-length contours
- **Composite glyph recursion**: need depth limit (suggest 64, matching FreeType)
- **Numeric precision**: scaling produces fractional values, inconsistent rounding = off-by-one pixels
- **Memory allocation**: per-glyph coverage buffers, 10,000+ chars = allocation pressure (consider buffer reuse)
- **Thread safety**: document single-threaded assumption
- **Endianness**: TrueType is big-endian, use `BinaryPrimitives.ReadUInt32BigEndian` (matching existing TtfParser)
- **CFF rejection**: if user loads .otf with CFF outlines (OTTO magic), throw clear `RasterizationException`

## Decision Framework

**Build from scratch IF:**
- StbTrueTypeSharp becomes unmaintained or has a blocking bug
- We need KernSmith-specific rasterization optimizations
- We want to add features stb_truetype doesn't support (e.g., CFF outlines) without FreeType
- Educational/portfolio value is prioritized

**Don't build from scratch IF:**
- StbTrueTypeSharp works well enough (most likely scenario)
- The 3-5 week effort is better spent on user-facing features
- We don't need capabilities beyond what stb_truetype provides
- Phase 34 does not include SDF rendering (out of scope), making it less capable than Phase 32's StbTrueType backend for SDF use cases

## Specification References

- [Apple TrueType Reference Manual](https://developer.apple.com/fonts/TrueType-Reference-Manual/)
- [Microsoft OpenType glyf table](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf)
- [Microsoft OpenType loca table](https://learn.microsoft.com/en-us/typography/opentype/spec/loca)
- [Microsoft OpenType maxp table](https://learn.microsoft.com/en-us/typography/opentype/spec/maxp)
- [FreeType source (reference)](https://gitlab.freedesktop.org/freetype/freetype/-/tree/master/src/)
- [Raph Levien font-rs](https://github.com/raphlinus/font-rs)
- Anti-Grain Geometry rasterizer (Maxim Shemanarev)

## Other References

- [Coding Adventure: Rendering Text (Sebastian Lague)](https://www.youtube.com/watch?v=LaYPoMPRSlk) — Visual walkthrough of TTF parsing and bezier rasterization
- [How Do Fonts Work? (Reducible)](https://www.youtube.com/watch?v=SO83KQuuZvg) — Deep dive into font rasterization algorithms
- [Implementing a Font Reader and Rasterizer from Scratch](https://handmade.network/forums/articles/t/7330-implementing_a_font_reader_and_rasterizer_from_scratch%252C_part_1__ttf_font_reader.) — Step-by-step TTF parser and rasterizer tutorial
- [NRasterizer](https://github.com/vidstige/NRasterizer) — Simple pure C# TTF rasterizer (Apache-2.0)
- [VectSharp Text Rendering](https://giorgiobianchini.com/VectSharp/text.html) — C# vector graphics library with font rendering
- [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) — Production reference for StbTrueTypeSharp usage patterns
- [Adobe Community: Font Parsing Algorithms](https://community.adobe.com/questions-94/where-can-i-find-font-parsing-or-text-rasterization-algorithm-1502180) — Discussion of rasterization algorithm resources
- [Efficient Text Rendering in C# (StackOverflow)](https://stackoverflow.com/questions/61584477/efficient-text-rendering-on-bitmap-in-c-sharp-with-system-drawing) — Performance techniques
- [Font Rasterization (Wikipedia)](https://en.wikipedia.org/wiki/Font_rasterization) — Overview of rasterization techniques, hinting, anti-aliasing
- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — Original C reference (~5,000 lines, extremely well-commented)
- [Sean Barrett's Rasterizer Algorithm](https://nothings.org/gamedev/rasterize/) — Algorithm explanation by stb author
