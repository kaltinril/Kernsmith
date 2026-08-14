# Phase 183 — Runtime Glyph Addition with Stable Packing

> **Status**: Implemented on `feature/incremental-glyph-addition` — pending review/merge
> **Tracks**: [GitHub issue #208](https://github.com/kaltinril/Kernsmith/issues/208)
> **Related**: [Phase 180 §8c](phase-180-innovation-research.md) "Progressive/Streaming Atlas Generation" — this is that idea minus eviction

## The Ask

Vic (issue #208):

> Users should be able to generate fonts by passing multiple characters at once (such as ASDF), but then they should be able to add new characters (such as Q) and get:
> 1. The bytes for just the letter Q
> 2. A location for where the letter Q should be added to an existing atlas, which would be defined by a list of rectangles

The intent behind it: **add letters and styles dynamically at runtime**, get back pixel bytes plus a suggested position, and blit those bytes straight into a live GPU texture. Existing glyphs must not move — "stable packing."

First consumer is Vic's `KernSmith.GumMonogame`, which would write the returned bytes into a read/write `RenderTarget2D`.

## How you generate a glyph today

Two public paths. Both verified against Georgia at size 32 (StbTrueType backend).

**A — full generate, one character.** In-memory, gives image bytes + `.fnt`:

```csharp
var result = BmFont.Generate(fontBytes, new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.FromChars("Q"),
    Backend = RasterizerBackend.StbTrueType,
});
byte[] png    = result.GetPngData(0);
string fntTxt = result.FntText;         // also ToXml() / ToBinary()
```
→ `char id=81 x=0 y=0 width=19 height=26 xoffset=1 yoffset=6 xadvance=21`, 64×64 page.

**B — rasterizer only, raw glyph bytes.** No atlas, no `.fnt`:

```csharp
using var r = RasterizerFactory.Create(RasterizerBackend.StbTrueType);
r.LoadFont(fontBytes);
var q = r.RasterizeGlyph('Q', new RasterOptions { Size = 32 });
// q.BitmapData, Width, Height, Pitch, Metrics
```
→ `22x30 pitch=22 Grayscale8 advance=24`.

### Why B does not satisfy the ask today

Same font, same nominal size, **different glyph** (19×26 vs 22×30). The gaps:

| Gap | Detail |
|---|---|
| **Effective ppem** | `Generate` treats `Size` as BMFont cell height and back-solves ppem as `Size * UnitsPerEm / (WinAscent + WinDescent)` (`BmFont.cs:227-239`). `RasterizeGlyph` takes `Size` literally. ~15% error for Georgia; font-specific. Skipped when `Capabilities.HandlesOwnSizing` (GDI). |
| **Layered effects** | `GlyphCompositor`, `IGlyphEffect`, `OutlineEffect`/`ShadowEffect`/`GradientEffect` are all `internal`. Only Bold/Italic post-processors are public. |
| **Supersample / SDF downscale** | Backend renders at `Size * renderScale` (`BmFont.cs:252-254`); downscale is the private `SuperSampleDownscale` (`BmFont.cs:1385`). |
| **Cell equalization** | `EqualizeCellHeights` pads to `glyphs.Max(g => g.Height)` **of that call** (`BmFont.cs:343-345`). |

And packing cannot do it at all: `IAtlasPacker.Pack` takes the whole set, sorts height-descending, has no occupancy concept (`MaxRectsPacker.cs:14-18`, `SkylinePacker.cs:10-14`). `AtlasTargetRegion` (`BmFont.cs:515-607`) renders into an existing PNG but packs its region from scratch.

## Research findings (verified in code)

These change the design from the initial sketch — mostly in our favor.

### F1. The glyph pipeline is already extracted

`BmFont.RasterizeFont(byte[] fontData, FontGeneratorOptions options, string? systemFontName)` (`BmFont.cs:112`, `internal static`) is self-contained and runs the **entire** matching pipeline: effective-ppem resolution → variation axes / color palette → rasterize → effects composite → post-processors → supersample downscale → cell equalization. Returns `RasterizationResult` (internal) carrying the processed `RasterizedGlyph`s + `EffectiveSize` + rasterizer metrics/kerning.

**Part 1 is therefore a thin public wrapper over `RasterizeFont`, not an extraction refactor.** No effect/compositor visibility changes needed — they stay internal behind the wrapper. Two pipeline changes needed: an equalize-height *override* (see F3), and a **prepare/rasterize split** so the session doesn't re-do per-call setup (see Performance). Half the plumbing already exists: `options.Rasterizer` injects a caller-owned rasterizer and `RasterizeFont` already skips disposing it (`BmFont.cs:379-381`).

### F2. Occupancy is fully recoverable from a `.fnt`

Exact geometry, from `BmFontModelBuilder.cs:148-168` and `BmFont.cs:506-510`:

- packed `GlyphRect` = glyph + padding + spacing
- `CharEntry.X/Y` = packed rect origin (`placement.X/Y`, verbatim)
- `CharEntry.Width/Height` = glyph + padding (spacing **not** included)
- glyph pixels are blitted at `placement + padding.Up/Left` (`AtlasBuilder.cs:65-66`)

→ **occupied rect = `(Char.X, Char.Y, Char.Width + Spacing.Horizontal, Char.Height + Spacing.Vertical)`**

Padding and spacing are both serialized in the `.fnt` info block (`InfoBlock.Padding/Spacing`), and `BmFontReader.Read` handles text/XML/binary. So a `.fnt` alone gives complete occupancy — no caller-supplied rect list needed (though we can accept one).

### F3. Equalized cell height is recoverable too

With `EqualizeCellHeights` on, every `CharEntry.Height` equals `maxGlyphHeight + padV` — so the original target height is `existing Char.Height - pad.Up - pad.Down` (any char). The wrapper passes this as the equalize target instead of the batch max. Policy: a new glyph *shorter* than the cell pads up normally; a new glyph *taller* than the cell **throws** (`InvalidOperationException`) — it would have changed the original atlas, which stable packing forbids. Document the workaround (regenerate).

### F4. MaxRects incremental packing is nearly free

`MaxRectsPacker.SplitFreeRects` + `PruneContainedRects` (`MaxRectsPacker.cs:101+`) already implement free-rect subtraction against an arbitrary placed rect. `PackInto` is:

1. Seed free list with `(0, 0, pageWidth, pageHeight)` per existing page.
2. For each occupied rect (sorted canonically — by `(PageIndex, Y, X)` — for cross-run determinism), run `SplitFreeRects` + prune.
3. Sort new glyphs height-desc (same comparator as `Pack`), `TryPlace` each (BSSF, already deterministic given free-list state).

No new geometry code. Skyline **cannot** reconstruct its state from occupied rects (holes under the skyline are lost) — `SkylinePacker.PackInto` throws `NotSupportedException`; `.bmfc` `packer=skyline` sessions fall back to MaxRects for additions (placements stay valid; only the placement *strategy* for new glyphs differs).

Known tradeoff, stated up front: incremental placements are worse than a full repack. That is inherent to never moving anything.

### F5. Kerning delta is a deterministic recompute

`BmFontModelBuilder.cs:170-204`: pairs come from `fontInfo.KerningPairs` (GPOS/kern parse) or rasterizer-provided pre-scaled pairs, filtered to the generated set, scaled by `round(XAdvanceAdjustment * metricSize / UnitsPerEm)` where `metricSize = effectiveSize ?? options.Size`. Delta for added set `N` against existing set `E`: pairs where one side ∈ `N` and both sides ∈ `E ∪ N`. Same formula, same rounding.

### F6. `.bmfc` is the config source of truth — and it's complete enough

Writer/reader round-trip covers everything pixel-affecting: backend (`rasterizer=`), packer, SDF (`useSdf`/`sdfScale`/`sdfSpread`), `MatchCharHeight` (negative `fontSize`), `useFixedHeight`, `useHinting`, gamma, fill color, effects, supersample, variation axes, DPI, faceIndex (`BmfcConfigWriter.cs`, `BmfcConfigReader.cs:95-460`). `BmFontResult.ToBmfc()` already exists.

A **`.fnt` alone** is *not* sufficient to reconstruct settings: `ExtendedMetadata` lacks backend, `MatchCharHeight`, `EqualizeCellHeights`, gamma, hinting, fill color — and backend/`MatchCharHeight` change pixels outright. **Design rule: options (`FontGeneratorOptions` or `.bmfc`) are the settings input; the `.fnt`/model is the occupancy input.** No new config type needed.

### F7. The subsetted font parse is a session trap

`Generate` parses with `RequestedCodepoints` = the charset (`BmFont.cs:135-136`), which filters the **cmap** (`TtfParser.cs:613,672`) and, via `_relevantGlyphIndices` derived from that filtered cmap (`TtfParser.cs:587-588`), also filters **kern/GPOS parsing**. A session that parsed the font subsetted to its initial charset would silently drop later-added characters (`Resolve` finds no cmap entry → glyph "missing") and lose their kerning pairs. **The session must parse the font once with no codepoint filter** — a one-time cost at `BeginIncremental` that buys parse-free `AddGlyphs` forever after.

## Design

### Primary API — incremental session

Runtime use is a session: font loaded once, packer state held live (no reconstruction), repeated cheap additions.

```csharp
public sealed class BmFontIncrementalSession : IDisposable
{
    // Entry points (on BmFont):
    //   BmFont.BeginIncremental(byte[] fontData, FontGeneratorOptions options)
    //   BmFont.ResumeIncremental(byte[] fontData, FontGeneratorOptions options,
    //                            BmFontModel existing)          // cross-process: occupancy from .fnt
    //   BmFont.ResumeIncremental(byte[] fontData, FontGeneratorOptions options,
    //                            IReadOnlyList<AtlasRect> occupied, int pageW, int pageH, int pages)

    GlyphAdditionResult AddGlyphs(string characters);            // batch; also (IEnumerable<int>)
    BmFontModel CurrentModel { get; }                            // full updated model → FntText etc.
}

public sealed class GlyphAdditionResult
{
    IReadOnlyList<AddedGlyph> Added;               // raw bytes + placement, caller blits
    IReadOnlyList<KerningEntry> NewKerning;        // delta only
    IReadOnlyList<int> FailedCodepoints;           // missing from font
    IReadOnlyList<int> AlreadyPresent;             // re-adds are a no-op, reported not thrown
    bool PageGrown; int PageWidth, PageHeight, PageCount;  // caller must reallocate/copy if changed
}

public sealed class AddedGlyph
{
    int Codepoint; int Width, Height;
    byte[] Pixels;                                 // RGBA32, lazy white+alpha promote (AtlasPage.GetRgbaPixelData logic);
                                                   // ready for RenderTarget2D.SetData<Color> — the default path
    byte[] RawPixels; PixelFormat RawFormat; int Pitch;  // zero-copy backend buffer (usually Grayscale8) for max-perf callers
    // Blit target: pixels go at (X + Padding.Left, Y + Padding.Up) — same as AtlasBuilder
    int PageIndex, X, Y;
    CharEntry Char;                                // ready-to-use fnt entry (padded dims, offsets, advance)
}
```

Notes:

- `AddGlyphs` internally: rasterize the new codepoints through the cached pipeline state (with equalize-target override per F3) → `PackInto` against the live free-rect state → build `CharEntry`s + kerning delta → append to held model.
- `BeginIncremental` + first `AddGlyphs("ASDF")` replaces `Generate` for this flow; the session computes initial page size via `AtlasSizeEstimator` exactly as `Generate` does. A session can also *start* from a normal `Generate` result via `ResumeIncremental(…, result.Model)`.
- Raw pixel bytes, not encoded PNG; KernSmith never re-encodes a page here. Matches the `RenderTarget2D` consumer.
- Unsupported in-session for v1 (throw at `BeginIncremental`): `Variants`, `ChannelPacking`, `TargetRegion`, `CustomGlyphs`. Each has batch-global interactions; lift restrictions individually later if asked.

### Performance requirements (v1, not follow-up)

The consumer calls `AddGlyphs` from a game loop — a text box receives a character the atlas doesn't have and the glyph should be blittable within the same frame. Per-add work must be proportional to the glyphs being added, never to the font or the atlas.

**The session caches, built once at `Begin`/`Resume`:**

| Cached | Kills this per-add cost |
|---|---|
| `FontInfo` from one **unfiltered** parse (F7) | Full TTF/WOFF parse (cmap + GPOS/kern + tables) — the dominant setup cost |
| Live `IRasterizer` with font loaded (via the existing `options.Rasterizer` hook, session-owned/disposed) | Backend creation + `LoadFont` (FreeType face / DWrite factory) per call |
| `effectiveSize`, `RasterOptions`, active effects/post-processor lists | Recomputed option plumbing (`BmFont.cs:227-303`) |
| Kerning index: `codepoint → pairs touching it`, built from `FontInfo.KerningPairs` | O(all pairs) scan per add (`BmFontModelBuilder.cs:189-204`) — GPOS fonts reach tens of thousands of pairs |
| Free-rect lists per page (already in the design) | Occupancy reconstruction per add |
| Present-codepoint `HashSet` | O(chars) duplicate scan |

This forces the `RasterizeFont` split named in F1: an internal prepare step (parse, rasterizer, options resolution) and a per-add step (rasterize + effects + downscale + equalize for given codepoints). `Generate` calls prepare+add once; behavior unchanged — the split must be a pure refactor gated by the existing test suite.

**Hot-path rules for `AddGlyphs`:**

- **Zero-copy raw path**: `AddedGlyph.RawPixels` *is* the pipeline's `RasterizedGlyph.BitmapData` — no clone, no page-buffer composite, no encode. `Pixels` (the RGBA default, per Q-resolution 7) lazily expands once on first access and caches; callers who read only `RawPixels` never pay it. Padding is applied by the caller at blit time (offsets documented on `AddedGlyph`).
- Kerning delta via the index: O(pairs touching the new codepoints), both directions, filtered against the present-set hash.
- `CurrentModel` appends to held lists; it must not rebuild the model per add. `FntText` etc. are rendered on demand only (they're already lazy on `BmFontResult`; same discipline here).
- No LINQ/closure allocation in the per-add path; presized lists; reuse the session's scratch collections.
- Rasterization stays serial (backend thread-safety); the effects/downscale pass reuses the existing `Parallel.For` — but skip the parallel dispatch below a small glyph count (the common case is 1-2 glyphs, where task overhead exceeds the work).
- `PackInto` cost is O(new × freeRects); free-list size is bounded by fragmentation, not atlas history — no per-add growth pathology. Growth/new-page events only add strips/reset lists.

**Benchmark gate** (BenchmarkDotNet, `benchmarks/KernSmith.Benchmarks`): `AddGlyphs` of 1 glyph into a warm ~95-char session, vs full `Generate` of 96 chars, ASCII Georgia + a large-GPOS font. Targets: per-add ≥ ~50× cheaper than regenerate, allocations proportional to added glyphs only, and a `MemoryDiagnoser` figure in the PR. `PipelineMetrics` gets `AddGlyphs` phase timings so consumers can see the same numbers.

### No-fit policy (decided)

The caller picks via `AdditionOverflowPolicy { Throw, Grow, NewPage }`:

- **`Throw`** — `AtlasPackingException` on no-fit; fixed-size consumers.
- **`Grow`** — POT-double the smaller dimension (matching `AutofitTexture` behavior) up to `MaxTextureWidth/Height`, then `AtlasPackingException`. Existing placements remain valid; MaxRects just gains free strips. `PageGrown = true` tells the caller to reallocate the texture and copy old contents at (0,0) — cheap on GPU.
- **`NewPage`** — add a page; never fails. Multi-page is a supported path (the UI preview already handles N pages).

Default: `Grow` (closest to existing `AutofitTexture` semantics).

### Packer API

```csharp
// IAtlasPacker — new method with a default implementation that throws NotSupportedException.
PackResult PackInto(IReadOnlyList<GlyphRect> newGlyphs, IReadOnlyList<OccupiedRect> occupied,
                    int pageCount, int maxWidth, int maxHeight);
```

MaxRects implements per F4. Session holds the live free-rect state between calls and only uses occupied-rect reconstruction on `ResumeIncremental`. Invariant test: resume-from-model then add must produce identical placements to a live session that did the same adds (same free-list canonical order guarantees this).

### Gum side (not this repo)

Vic updates `KernSmith.GumMonogame`: keep a `BmFontIncrementalSession` per font, blit `AddedGlyph.Pixels` into the `RenderTarget2D` at `(X + pad.Left, Y + pad.Up)`, merge `Char`/`NewKerning` into his lookup, handle `PageGrown` by reallocating.

## Resolved Decisions

1. Input contract → options are settings truth, model/`.fnt` is occupancy truth; both `Begin` and `Resume` offered.
2. Bytes format → raw pixels + `PixelFormat`; caller blits.
3. No-fit policy → **caller's choice**: `Throw` / `Grow` / `NewPage` (see decided section above).
4. Multi-page → **must be supported**; the UI project already previews N pages. Consumers that can't handle `page > 0` simply don't pick `NewPage`.
5. Styles → **v1: identical settings only** (one session per style). **V2: mixed settings in one atlas** — likely fusing this with phase 182 group packing; may need multiple `.fnt` files per atlas or a new syntax. V2 shape deliberately not designed yet.
6. Stable-packing contract → **accepted**. It was Jeremy's proposal to Vic offline; Vic understands one-off-added glyphs won't sit where a full repack of the combined set would put them.

7. Pixel format at the blit boundary → **both**: `Pixels` = RGBA32 default (lazy white+alpha promote, ready for `SetData<Color>`); `RawPixels` + `RawFormat` = the zero-copy backend buffer for max-perf callers. No existing generation flag covers this — the atlas path auto-detects RGBA (`AtlasBuilder.cs:28-31`); we reuse `AtlasPage.GetRgbaPixelData`'s promote logic as a lazy property instead.
8. V2 shape → the atlas is "a spot to pick a rectangle from that's already in GPU memory *for all fonts*": V2 must pack **any font with any attributes** (bold, italic, color, size, …) into one shared atlas. That is exactly phase 182's multi-font ambition fused with this session — and phase 182 already produces one `BmFontModel` per source sharing a `PageEntry` list, so "multiple `.fnt` files per atlas" is the likely V2 syntax. Still deliberately not designed in this phase.

No open questions remain.

## Non-Goals

- Glyph eviction / LRU (phase 180 §8c remainder); repacking/compacting; multi-font shared atlases; `Variants`/`ChannelPacking`/`TargetRegion`/`CustomGlyphs` in-session (v1).

## Implementation Checklist (TDD, red→green per step)

Dependency order; each step is a shippable commit.

- [x] `OccupiedRect` type + `IAtlasPacker.PackInto` default-throws; `MaxRectsPacker.PackInto`: seed → subtract occupied (canonical sort) → place new. Tests: no overlap with occupied or each other; in-bounds; deterministic across runs; empty-occupied equals fresh pack of same rects (geometry lives in the shared `MaxRectsState`)
- [x] Occupancy extraction from `BmFontModel` (per F2 formula, spacing from `InfoBlock`) — lives in `ResumeIncremental`; covered by the resume-vs-live invariant test
- [x] `RasterizeFont` prepare/per-add split (pure refactor into `PreparedRasterization`; `Generate` behavior unchanged)
- [x] `RasterizeFont` equalize-target override param. Tests: shorter glyph pads to target; taller throws
- [x] `BmFontIncrementalSession` + `BeginIncremental`/`AddGlyphs` core + F7 guard tests; adds are canonicalized to ascending codepoint order so batch order matches `Generate`'s height-sort tie-break
- [x] `AddedGlyph.Pixels` RGBA lazy promote
- [x] `CharEntry` + kerning delta + `CurrentModel` append; kerning tests use V/W because Q has too few pairs in the test font
- [x] `ResumeIncremental(model)` + resume-vs-live invariant
- [x] Overflow: `Grow`/`NewPage`/`Throw` policies, `PageGrown` flag
- [x] v1 unsupported-options guard at `BeginIncremental`
- [x] Benchmark: warm 1-glyph add **74.2 µs / 2.57 KB** vs full 96-char generate **3,927 µs / 1,783 KB** (~53×, gate met); resume reconstruction ~3.8 ms one-time. **Deferred**: `PipelineMetrics` phases for `AddGlyphs` (adds API surface — decide with Vic's consumption pattern) and a second large-GPOS benchmark font (no guaranteed-present candidate)
- [x] Blit round-trip integration test — byte-equal via placement injection
- [x] Docs: `docs/core/incremental-glyph-addition.md` + toc/index/api-reference links + XML docs

## Affected Files

- `src/KernSmith/Atlas/IAtlasPacker.cs`, `MaxRectsPacker.cs` (+ `OccupiedRect`) — `PackInto`
- `src/KernSmith/BmFont.cs` — `BeginIncremental`/`ResumeIncremental`; `RasterizeFont` prepare/per-add split + equalize-target override
- `src/KernSmith/BmFontIncrementalSession.cs` (new) — cached prepare state, kerning index, live free-rects; result types (new, likely `Output/`)
- `src/KernSmith/Output/BmFontModelBuilder.cs` — reuse char-entry/kerning construction for deltas (may need an internal helper split, not a behavior change)
- `benchmarks/KernSmith.Benchmarks/` — warm-session add vs regenerate benchmark
- `tests/KernSmith.Tests/Atlas/`, `tests/KernSmith.Tests/Integration/` — per checklist
