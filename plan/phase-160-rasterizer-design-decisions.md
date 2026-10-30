# Phase 160 — Pure C# Rasterizer: Design Decisions & Open Questions

> **Status**: Active — design record for the Native rasterizer; the remaining work and open decisions are in [master-plan.md § Native rasterizer completion](master-plan.md#native-rasterizer-completion)
> **Created**: 2026-04-01
> **Supersedes**: Phase 34 (Custom Pure C# Rasterizer research)
> **Related**: Phases 161–180 (implementation phases)

## Goal

Resolve all open design decisions and architectural questions before beginning implementation of KernSmith's pure C# font rasterizer. This phase is the "decision record" — all subsequent phases reference it.

## Implementation Status

- **Complete** (`plan/done/`): Phases 161–165 (tables, `glyf`/`loca`, outlines and flattening, the scanline fill, `IRasterizer` integration), 166 (staged pipeline, CFF/CFF2), 167 (synthetic bold/italic), 168 (vector outlines, width, italic angle), 169–170 (SDF, MSDF), 171 (variations), 172 (COLR and bitmap color), 173 (LCD), 174 (auto-hinting) and 178 (WOFF2).
- **Open**: 177 (optimisation) and 179 (validation and release); 175 and 176 await Jeremy's decision. The master plan lists them. The staged pipeline below exists and every stage is filled in ([Phase 166 Part 1](done/phase-166-cff-charstring-interpreter.md#part-1--staged-pipeline-refactor-wave-0)).

## Context

KernSmith already had four rasterizer backends before this work (`RasterizerBackend` in `src/KernSmith/Config/RasterizerBackend.cs`):
- **FreeType** (FreeTypeSharp) — full-featured, native C dependency
- **Gdi** — Windows-only, wraps GDI
- **DirectWrite** (TerraFX) — Windows-only, high quality
- **StbTrueTypeSharp** — pure C# wrapper around stb_truetype port, limited features

We are building a **fifth backend**, `Native`: a fully custom, pure C# rasterizer owned entirely by KernSmith. It is the only backend with **zero external rasterization dependencies** — no native libraries and no third-party font NuGet packages. The goal is feature parity with FreeType over time, with the ability to do synthetic bold/italic/everything at the outline level.

## Project Structure Decision

**Decision**: Create `src/KernSmith.Rasterizers.Native/` as a new project.

- Namespace: `KernSmith.Rasterizers.Native`
- Registers as `RasterizerBackend.Native` (new enum value)
- Registration is dual-path: self-registers via `[ModuleInitializer]` (`NativeRegistration.cs:9`) AND is reflection auto-discovered by `RasterizerFactory` (`RasterizerFactory.cs:23`), with an `ILLink.Descriptors.xml` trim root so the initializer survives trimming
- Target: `net8.0;net10.0` (multi-targets with the rest of the repo)
- Must set `<IsTrimmable>true</IsTrimmable>`, `<IsAotCompatible>true</IsAotCompatible>`
- No `<AllowUnsafeBlocks>`: the backend contains no `unsafe` code and builds without the flag, so none can creep in
- **Pure managed C# we write, end to end** — no P/Invoke, no native libraries, zero NuGet dependencies, and no new third-party packages without Jeremy's sign-off. This also binds any core code Native depends on (e.g. WOFF2's Brotli decoder). It is the entire point: Native must keep working on browser-wasm and iOS/NativeAOT (see Native Rasterizer in `project_decisions.md`)
- WASM-compatible: no `.Result`/`.Wait()`, no `Reflection.Emit`. Parallelism is allowed where it helps, but behind an opt-out option, on by default where supported and forced off on browser-wasm and any other single-threaded target (see Native Rasterizer in `project_decisions.md`); not implemented yet — Phase 177 measures it first

## Architecture Decision

**Decision**: Modular pipeline with internal interfaces.

```
FontData (bytes)
  → BinaryFontReader (parse table directory + core tables)
    → TableProvider (lazy table access by tag)
      → OutlineDecoder (glyf/loca or CFF charstrings → GlyphOutline; variation deltas applied here)
        → OutlineTransformer (synthetic width/bold/italic — font units)
          → Bounding box computed from the transformed outline
            → Scale to pixels
              → AutoHinter (optional — snap stems/zones to pixel grid)
                → Render-mode dispatch: coverage (signed-area trapezoid) / SDF / MSDF / LCD / color (COLR layers and paint graphs, CBDT/sbix bitmaps); vector outline layer
                  → RasterizedGlyph (output)
```

Internal interfaces (not public API — users interact via existing `IRasterizer`):
- `IOutlineDecoder` — TrueType vs CFF outline extraction
- `IGlyphTransform` — chain of outline-level transforms
- `IGlyphRenderer` — render-mode dispatch target; `CoverageRenderer` wraps the scanline rasterizer

## Rasterization Algorithm Decision

**Decision**: Exact signed-area coverage accumulation, derived independently (see the `ScanlineRasterizer` class doc comment): each edge adds its signed height to a per-row delta buffer, split between the column it crosses and the next, and one prefix sum per row yields the coverage. Rows are filled in bands with the edges walked in input order — no sorting or active-edge list.

Rationale:
- Exact pixel coverage without supersampling
- Single-pass, memory-efficient (one pooled band buffer)
- Non-zero winding falls out of the signed sum
- Naturally produces anti-aliased output; `None` thresholds it and adds dropout control

Alternative considered: Oversampled scanline fill — simpler but 4-16x slower and higher memory.

> **FontStashSharp insight:** stb_truetype internally offers two rasterization algorithms (a fast/rough one and a slower/accurate one). Consider exposing a similar quality tier system — e.g., a fast scanline mode for previews and a high-quality coverage mode for final output — selectable via `RasterOptions`.

## Outline Representation Decision

**Decision**: Universal cubic Bezier representation internally.

- TrueType quadratic Beziers are elevated to cubic on load (trivial: P0, P0+2/3*(P1-P0), P2+2/3*(P1-P2), P2)
- CFF outlines are already cubic
- Rasterizer only needs to handle cubic curves
- Synthetic transforms work uniformly on one representation
- Flatten to line segments only at rasterization time

## Bezier Flattening Decision

**Decision**: Adaptive De Casteljau subdivision with analytic step count.

- Recursive De Casteljau subdivision until the control points sit within the flatness tolerance of the chord (`OutlineFlattener`, depth capped at 16)
- Flatness tolerance: 0.25 pixels (configurable; LCD and stroking flatten finer)

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

**Decision**: A for the MVP, then B: Phase 174's y-only auto-hinter runs for `Light` requests. C is out of scope (estimated 5,000-10,000 lines, 3-6 months).

### Q3: Should synthetic bold work at outline level or bitmap level?

**Decision**: Outline level (perpendicular normal offset).

Rationale: Higher quality, works with SDF, doesn't fill in counters at small sizes. Embedded color bitmaps (`CBDT`/`sbix`) are left unstyled, as on FreeType.

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

**Decision**: Pool internal per-glyph scratch buffers; never pool the output bitmap.

- Critical for WASM (heap never shrinks)
- Scratch buffers (the coverage delta buffer, dropout-control buckets and crossings, simple-`glyf` decode scratch, extractor and flattener temporaries) come from `ArrayPool<T>.Shared` or a per-decoder scratch; nothing internal escapes to the caller
- A glyph allocates what it keeps — the output `RasterizedGlyph.BitmapData` (freshly allocated, D11, because the caller owns it), its outline commands, the exact edge array the fill takes and small result objects; composite `glyf` glyphs still go through `ParsedGlyph`

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
- Met: Phase 177's harness measures Native within 2x of StbTrueType for ASCII at 32px (faster at larger sizes)

### Q10: What fonts do we test against?

**Decision**: Committed OFL/Apache fixtures only, so every test runs everywhere:
1. **Primary**: Roboto-Regular.ttf
2. **Extended**: Noto Sans (and its CFF build), Source Serif 4 (CFF), Source Code VF (CFF2), Roboto Flex and Dancing Script (variable), Bungee Color and Reem Kufi Ink (COLR), Noto Color Emoji (CBDT)
3. **Edge cases**: hand-built synthetic tables (composites, overlapping contours, CID-keyed CFF, variable COLRv1, CBDT/sbix) and the fuzz harness

## Decisions Log

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Project: `KernSmith.Rasterizers.Native` | Consistent with existing rasterizer project naming |
| D2 | Algorithm: exact signed-area coverage accumulation, derived independently | Exact coverage, efficient, ours |
| D3 | Internal representation: Cubic Beziers | Universal format, TrueType elevated trivially |
| D4 | Coordinate system: Font units until rasterization | Avoid precision loss |
| D5 | Hinting: none for the MVP, then a y-only auto-hinter for `Light` (Phase 174); no bytecode interpreter | Full bytecode hinting is a massive effort |
| D6 | Synthetic bold: Outline-level normal offset | Higher quality than bitmap dilation |
| D7 | SDF: Direct distance computation | Best quality, MSDF for sharp corners |
| D8 | Buffers: internal scratch pooled (`ArrayPool<T>.Shared`) or reused; output bitmap not pooled | WASM compatibility, reduce GC pressure |
| D9 | Thread safety: Single-threaded per instance | Simple, matches FreeType model |
| D10 | CFF: Defer to Phase 166 | TTF-only MVP ships faster |
| D11 | ArrayPool for internal buffers only | Output BitmapData freshly allocated — no IDisposable on RasterizedGlyph |
| D12 | Edge direction: keep original winding | The signed-area fill uses direction for sign |
| D13 | SDF convention: inside = positive/bright | Matches stb_truetype and common shader expectations (inside > 128) |
| D14 | Hinting pipeline position: after pixel scaling | Auto-hinting needs pixel grid; runs between scale and rasterize |
| D15 | Core table parsing in Phase 161 | head, hhea, hmtx, maxp, OS/2, cmap parsed in scaffold phase, not deferred |

## What Phase 34 Contained (Now Superseded)

Phase 34 was a research/investigation document covering:
- glyf/loca/maxp table parsing → Complete: maxp's `numGlyphs` landed in Phase 161; glyf/loca plus maxp's extended profile fields landed in Phase 162
- Outline extraction + bezier flattening → Complete: landed in Phase 163
- Scanline rasterizer core → Complete: landed in Phase 164
- IRasterizer integration → Complete: landed in Phase 165
- Validation testing → Now Phase 179

Phase 34 scoped OUT: CFF, hinting, variable fonts, color fonts, SDF, synthetic bold/italic. Phases 160-179 include all of these with a concrete implementation plan.

## References

- Phase 34 research (superseded): `plan/done/phase-34-custom-rasterizer.md`
- Existing IRasterizer interface: `src/KernSmith/Rasterizer/IRasterizer.cs`
- Existing rasterizer capabilities: `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs`
- Sample rasterizer: `samples/KernSmith.Rasterizer.Example/MyRasterizer.cs`
