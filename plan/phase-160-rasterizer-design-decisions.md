# Phase 160 — Pure C# Rasterizer: Design Decisions & Open Questions

> **Status**: Active
> **Created**: 2026-04-01
> **Supersedes**: Phase 34 (Custom Pure C# Rasterizer research)
> **Related**: Phases 161–179 (implementation phases)

## Goal

Resolve all open design decisions and architectural questions before beginning implementation of KernSmith's pure C# font rasterizer. This phase is the "decision record" — all subsequent phases reference it.

## Context

KernSmith currently has two rasterizer backends:
- **FreeTypeSharp** — full-featured, native C dependency
- **StbTrueTypeSharp** — pure C# wrapper around stb_truetype port, limited features

We are building a **third backend**: a fully custom, pure C# rasterizer owned entirely by KernSmith. Zero external NuGet dependencies for font rasterization. The goal is feature parity with FreeType over time, with the ability to do synthetic bold/italic/everything at the outline level.

## Project Structure Decision

**Decision**: Create `src/KernSmith.Rasterizers.Native/` as a new project.

- Namespace: `KernSmith.Rasterizers.Native`
- Registers as `RasterizerBackend.Native` (new enum value)
- Self-registers via `[ModuleInitializer]`
- Target: `net10.0`
- Must set `<IsTrimmable>true</IsTrimmable>`, `<IsAotCompatible>true</IsAotCompatible>`
- Zero NuGet dependencies (the entire point)
- WASM-compatible: no `Parallel.ForEach`, no `.Result`/`.Wait()`, no `Reflection.Emit`

## Architecture Decision

**Decision**: Modular pipeline with internal interfaces.

```
FontData (bytes)
  → BinaryFontReader (parse table directory + core tables)
    → TableProvider (lazy table access by tag)
      → OutlineDecoder (glyf/loca or CFF charstrings → GlyphOutline)
        → OutlineTransformer (synthetic bold/italic/stroke/variable interp — font units)
          → Scale to pixels
            → AutoHinter (optional — snap stems/zones to pixel grid)
              → CoverageRasterizer (signed-area trapezoid → byte[] bitmap)
                → RasterizedGlyph (output)
```

Internal interfaces (not public API — users interact via existing `IRasterizer`):
- `IOutlineDecoder` — TrueType vs CFF outline extraction
- `IOutlineTransformer` — chain of outline-level transforms
- `ICoverageCalculator` — the actual scanline rasterizer

## Rasterization Algorithm Decision

**Decision**: Signed-area trapezoid coverage (stb_truetype v2 / font-rs style).

Rationale:
- Exact pixel coverage without supersampling
- Single-pass, memory-efficient
- Battle-tested algorithm (used by stb_truetype, font-rs, ab-glyph)
- Naturally produces anti-aliased output
- Well-documented with reference implementations

Alternative considered: Oversampled scanline fill — simpler but 4-16x slower and higher memory.

## Outline Representation Decision

**Decision**: Universal cubic Bezier representation internally.

- TrueType quadratic Beziers are elevated to cubic on load (trivial: P0, P0+2/3*(P1-P0), P2+2/3*(P1-P2), P2)
- CFF outlines are already cubic
- Rasterizer only needs to handle cubic curves
- Synthetic transforms work uniformly on one representation
- Flatten to line segments only at rasterization time

## Bezier Flattening Decision

**Decision**: Adaptive De Casteljau subdivision with analytic step count.

- Use Raph Levien's analytic method to estimate subdivision count
- Fall back to recursive De Casteljau if needed
- Flatness tolerance: 0.25 pixels (configurable)

## Coordinate System Decision

**Decision**: Work in font units internally, scale to pixels at rasterization time.

- All outline transforms (bold, italic, etc.) happen in font units
- Scaling to pixels happens once, right before rasterization
- Avoids precision loss from premature rounding

## Open Questions

### Q1: Should we support CFF/CFF2 from the start or defer?

**Options**:
- A) TrueType-only MVP (faster to ship, matches phase 34 scope)
- B) CFF from the start (broader font compatibility, .otf files work)

**Recommendation**: A for Phase 161-165 (MVP), B starting Phase 166. Most bitmap font workflows use TTF. CFF support is important but not blocking.

### Q2: How should we handle hinting?

**Options**:
- A) Skip entirely (like stb_truetype) — unhinted output
- B) Implement auto-hinting (detect stems/blue zones, snap to grid)
- C) Full TrueType bytecode interpreter (massive effort)

**Recommendation**: A for MVP. B as Phase 174. C is out of scope (estimated 5,000-10,000 lines, 3-6 months).

### Q3: Should synthetic bold work at outline level or bitmap level?

**Decision**: Outline level (perpendicular normal offset).

Rationale: Higher quality, works with SDF, doesn't fill in counters at small sizes. Bitmap-level dilation is a fallback only for embedded bitmaps.

### Q4: SDF algorithm — brute force, EDT, or direct distance?

**Options**:
- A) Brute force (O(texels × edges)) — simple but slow for complex glyphs
- B) EDT on rasterized bitmap — fast but loses edge precision
- C) Direct distance computation against outline segments — best quality
- D) MSDF (multi-channel) — preserves sharp corners

**Recommendation**: C for single-channel SDF (Phase 169), D for MSDF (Phase 170). EDT as fast fallback option.

### Q5: What's the naming convention — "Native" or "Managed" or "KernSmith"?

**Decision**: `Native` (as in "native to KernSmith", not "native code").

Alternatives considered:
- `Managed` — could confuse with ".NET managed code" distinction
- `KernSmith` — redundant (it's already in the namespace)
- `Pure` — too generic
- `Builtin` — acceptable alternative

### Q6: Buffer management strategy?

**Decision**: `ArrayPool<byte>.Shared` for all per-glyph bitmap buffers.

- Critical for WASM (heap never shrinks)
- Rent/return pattern for coverage buffers and output bitmaps
- Document that callers must not hold references to internal buffers

### Q7: Thread safety model?

**Decision**: Single-threaded per instance. Document that `NativeRasterizer` instances are NOT thread-safe.

- Users who need parallel rasterization create one instance per thread
- Matches FreeType's `FT_Face` model
- Avoids locking overhead for the common case

### Q8: How do we handle overlapping contours?

**Decision**: Non-zero winding rule (TrueType standard).

The signed-area trapezoid method with cumulative sum naturally implements non-zero winding. Overlapping contours with consistent winding direction render correctly.

### Q9: Performance target?

**Target (initial, Phases 161-165)**: Within 3x of StbTrueTypeSharp for ASCII set at 32px.

- StbTrueType is already slower than FreeType
- 3x overhead is acceptable for the first implementation
- Phase 177 tightens this to 2x for production release

### Q10: What fonts do we test against?

**Decision**: Three tiers:
1. **Primary**: Roboto-Regular.ttf (already in test fixtures)
2. **Extended**: Noto Sans (complex composites), DejaVu Sans (wide coverage)
3. **Edge cases**: Fonts with deeply nested composites, overlapping contours, CFF outlines

## Decisions Log

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Project: `KernSmith.Rasterizers.Native` | Consistent with existing rasterizer project naming |
| D2 | Algorithm: Signed-area trapezoid coverage | Exact coverage, battle-tested, efficient |
| D3 | Internal representation: Cubic Beziers | Universal format, TrueType elevated trivially |
| D4 | Coordinate system: Font units until rasterization | Avoid precision loss |
| D5 | Hinting: Skip for MVP, auto-hint later | Matches stb_truetype, massive effort for full hinting |
| D6 | Synthetic bold: Outline-level normal offset | Higher quality than bitmap dilation |
| D7 | SDF: Direct distance computation | Best quality, MSDF for sharp corners |
| D8 | Buffers: ArrayPool<byte>.Shared | WASM compatibility, reduce GC pressure |
| D9 | Thread safety: Single-threaded per instance | Simple, matches FreeType model |
| D10 | CFF: Defer to Phase 166 | TTF-only MVP ships faster |
| D11 | ArrayPool for internal buffers only | Output BitmapData freshly allocated — no IDisposable on RasterizedGlyph |
| D12 | Edge direction: keep original winding | Signed-area algorithm uses direction for sign; matches stb_truetype/font-rs |
| D13 | SDF convention: inside = positive/bright | Matches stb_truetype and common shader expectations (inside > 128) |
| D14 | Hinting pipeline position: after pixel scaling | Auto-hinting needs pixel grid; runs between scale and rasterize |
| D15 | Core table parsing in Phase 161 | head, hhea, hmtx, OS/2, cmap parsed in scaffold phase, not deferred |

## What Phase 34 Contained (Now Superseded)

Phase 34 was a research/investigation document covering:
- glyf/loca/maxp table parsing → Now Phase 162
- Outline extraction + bezier flattening → Now Phase 163
- Scanline rasterizer core → Now Phase 164
- IRasterizer integration → Now Phase 165
- Validation testing → Now Phase 179

Phase 34 scoped OUT: CFF, hinting, variable fonts, color fonts, SDF, synthetic bold/italic. Phases 160-179 include all of these with a concrete implementation plan.

## References

- Phase 34 research (superseded): `plan/done/phase-34-custom-rasterizer.md`
- Existing IRasterizer interface: `src/KernSmith/Rasterizer/IRasterizer.cs`
- Existing rasterizer capabilities: `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs`
- Sample rasterizer: `samples/KernSmith.Rasterizer.Example/MyRasterizer.cs`
