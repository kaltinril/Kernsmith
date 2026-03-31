# Phase 33 — Custom Pure C# Rasterizer (From Scratch)

> **Status**: Future / Research
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction), Phase 31 (StbTrueType plugin for comparison)
> **Related**: Phase 31 (StbTrueType), Phase 34 (FontStashSharp)

## Goal

Investigate and potentially implement a fully custom, pure C# TTF rasterizer owned entirely by KernSmith. This would eliminate all third-party rasterization dependencies and give complete control over the rendering pipeline.

## Motivation

- **Zero dependencies** — no StbTrueTypeSharp, no FreeTypeSharp, no licensing concerns whatsoever
- **Full control** — optimize specifically for bitmap font atlas generation (not general-purpose text rendering)
- **Educational value** — deep understanding of the rasterization pipeline
- **Customization** — can add KernSmith-specific optimizations (batch glyph rendering, atlas-aware rasterization)

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
- Scanline rasterizer / coverage calculation
- Composite glyph assembly (glyphs made of other glyphs with transforms)

## Complexity Breakdown

| Component | Effort | Lines (est.) | Notes |
|-----------|--------|-------------|-------|
| `glyf` table parser | 1 week | 300-500 | Quadratic bezier contours, on/off-curve points, flags |
| `loca` table parser | 1 day | 50-100 | Short (16-bit) and long (32-bit) formats |
| Bezier curve flattening | 2-3 days | 100-200 | Quadratic (TTF) is straightforward. Cubic (OTF/CFF) adds more. |
| Scanline rasterizer | 1-2 weeks | 500-1000 | The core algorithm. Two approaches below. |
| Composite glyphs | 3-5 days | 200-300 | Recursive glyph assembly with affine transforms |
| Font scaling/metrics | 2-3 days | 100-200 | Convert from font units to pixel coordinates |
| Anti-aliasing | Included | — | Both rasterizer approaches produce AA output naturally |
| **Total (no hinting)** | **3-5 weeks** | **~1500-2500** | — |
| TrueType hinting VM | 3-6 months | 15,000+ | **NOT recommended** — enormous complexity |

## Rasterization Approaches

### Approach A: Oversampled Scanline Fill

1. Scale glyph outlines to N× target size (e.g., 4×)
2. For each scanline, find edge crossings using winding number rule
3. Fill between crossings
4. Downsample N×N blocks to get coverage values (anti-aliasing)

**Pros:** Simple to understand and implement
**Cons:** Higher memory usage (N² per glyph), slower than exact coverage

### Approach B: Signed-Area Trapezoid (stb_truetype approach)

1. Gather directed edges from glyph outlines, sort by top vertex
2. For each pixel-tall scanline, maintain an active edge list
3. For each active edge, compute signed-trapezoid areas extending rightward
4. Coverage values accumulate via cumulative sum — no oversampling needed

**Pros:** Exact coverage, single pass, memory efficient, battle-tested algorithm
**Cons:** More complex math, harder to debug

**Recommendation:** Approach B — it's what stb_truetype uses and produces superior quality at the same performance cost.

### What About Hinting?

TrueType hinting is a full bytecode virtual machine (~40 opcodes) that modifies glyph outlines to snap to pixel boundaries. FreeType's implementation is ~15,000 lines. Auto-hinting (for fonts without hint programs) adds another ~10,000 lines.

**Decision: Skip hinting.** stb_truetype also skips hinting and produces acceptable results. At typical bitmap font sizes (16px+), the quality difference is minimal. Users needing hinted output use the FreeType backend.

## Implementation Plan (if pursued)

### Step 1: `glyf` and `loca` table parsers
- Add to existing `KernSmith.Font.Tables` namespace
- Parse simple glyphs (contours with on/off-curve points)
- Parse composite glyphs (component references with transforms)

### Step 2: Outline extraction
- Convert parsed glyph data to a list of bezier contours
- Scale from font units to pixel coordinates
- Apply composite glyph transforms

### Step 3: Scanline rasterizer
- Implement signed-area trapezoid algorithm
- Output: greyscale bitmap (byte array, one byte per pixel)
- Use stb_truetype.h as reference (~500-1000 lines of algorithm code)

### Step 4: IRasterizer implementation
- Create `KernSmith.Rasterizers.Native/` project (or similar name)
- Implement full `IRasterizer` interface
- Register via `[ModuleInitializer]`

### Step 5: Validation
- Compare output against FreeType and StbTrueType baselines
- Measure performance against both
- Test edge cases: composite glyphs, very small sizes, very large sizes, empty glyphs

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

## References

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
