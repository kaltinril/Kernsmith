# Phase 300 -- Deferred Performance Work

> **Status**: Deferred / Future
> **Size**: Large
> **Created**: 2026-06-03
> **Origin**: [Phase 95 -- Performance Optimization & Bug Fixes](done/phase-95-performance-and-bugs.md)
> **Goal**: Bucket for performance optimizations that were proven viable but intentionally deferred because they require an API redesign (buffer-ownership model). Output must remain byte-identical.

---

## Overview

Phase 95 completed Perf 6 **Phase A** (ArrayPool for the safe local scratch buffers in
`OutlineEffect`, `OutlinePostProcessor`, `ShadowEffect`, `ShadowPostProcessor`, and `GlyphCompositor`).
Two further sub-phases of Perf 6 -- **Phase B** (pool atlas page buffers) and **Phase C**
(pool the per-glyph `RasterizedGlyph.BitmapData` in the transform pipeline) -- were deliberately
**not** done. They are real, workable optimizations (the biggest remaining LOH allocation wins), but
they cannot be implemented safely against the current API surface: the buffers in question escape
into public, caller-held objects. Returning such a buffer to an `ArrayPool` risks a use-after-return
where a caller still holds the array. Closing that gap requires an `IDisposable` / buffer-ownership
redesign, which is larger and riskier than the rest of Phase 95 -- hence this deferred bucket rather
than dropping the items on the floor.

**Not included here**: Perf 10 (integer alpha blending). See [Excluded](#excluded-perf-10-integer-alpha-blending)
below -- it was proven mathematically impossible in Phase 95 and is closed permanently. Do **not**
carry it forward.

## Priority Rankings

Each item is ranked 1 (low) to 5 (high) on three dimensions.

| # | Item | Ease | Break Risk | Importance | Status |
|---|------|------|------------|------------|--------|
| 1 | Perf 6 Phase B -- pool atlas page buffers | 2 | 4 | 4 | Deferred |
| 2 | Perf 6 Phase C -- pool per-glyph bitmap buffers | 2 | 4 | 3 | Deferred |

**Legend**: Ease = ease to implement (5=easy). Break Risk = chance of breaking other things (5=high risk). Importance = importance to implement (5=critical).

---

## Perf 6 Phase B -- Pool atlas page buffers

### What it is

Atlas page pixel buffers are the single largest allocations in the pipeline -- 1-4 MB each, landing
on the Large Object Heap (LOH) and pressuring Gen 2 GC. A typical multi-page generation allocates a
fresh `byte[]` per page, uses it during compositing/encoding, then discards it. Pooling these buffers
(rent before the atlas build, return after encoding completes) would remove the biggest source of LOH
garbage in a generation run.

### Files / types involved

| File | Role |
|------|------|
| `src/KernSmith/Atlas/AtlasPage.cs` | Holds the buffer as `public required byte[] PixelData { get; init; }` -- this is the escape point |
| `src/KernSmith/Atlas/AtlasBuilder.cs` | Allocates and fills page buffers |
| `src/KernSmith/Atlas/ChannelCompositor.cs` | Allocates/composites channel-packed page buffers |
| `src/KernSmith/Atlas/ChannelPackedAtlasBuilder.cs` | Channel-packed page build path |

### Why it was deferred

`AtlasPage.PixelData` is a **public buffer handed to callers**: it flows out through `BmFontResult`,
to GPU-upload paths in the integrations, and to encoders. Once an `AtlasPage` is returned from the
build, the library no longer controls the lifetime of `PixelData` -- a consumer may hold it, upload it
to a texture, or read it long after generation. If the library returns that array to
`ArrayPool<byte>.Shared`, a later rent could hand the same array to unrelated code that overwrites it
while the consumer is still reading -- a classic use-after-return data-corruption hazard. There is no
ownership tracking today to know when the page buffer is safe to reclaim.

### Sketch of the approach

- Introduce a buffer-ownership model. Options:
  - **`AtlasPage : IDisposable`** -- the page owns a rented buffer and returns it on `Dispose()`.
    `BmFontResult` (and any other holder) becomes responsible for disposal; the public contract
    changes from "you may keep `PixelData` forever" to "valid until the result/page is disposed".
  - Or an explicit `OwnsBuffer` / detach flag so callers that need a long-lived copy can opt out of
    pooling (the library hands back a freshly-allocated, non-pooled array in that case).
- Rent at the build level (per page), composite into the rented buffer, encode, then return on
  disposal. Use the **requested length** for all loop bounds and pixel math, never `array.Length`
  (rented arrays are oversized).
- Audit every consumer of `AtlasPage.PixelData` (encoders, `BmFontResult`, integrations, GPU upload)
  and update them for the new lifetime contract.

### Risks

- **Public API / contract break** -- existing consumers assume `PixelData` lives as long as they hold
  the page. Adding `IDisposable` or a detach flag is a breaking change to the documented contract and
  must be versioned/communicated accordingly.
- **Use-after-dispose** -- the inverse of use-after-return: if disposal happens before a consumer is
  done (e.g. async GPU upload), reads corrupt. Needs clear ownership rules and tests.
- **Integrations / samples** -- MonoGameGum / KniGum / GumCommon and the samples consume page pixels;
  all need review.

### Validation requirement

The change must be **output-neutral**: the bmfont-compare regression harness
(`tests/bmfont-compare/regression_check.py`) must pass with **exit 0 -- 0 diffs** across every backend
(150/150 FNT files identical, all atlas pages pixel-identical). Pooling only changes where bytes come
from, never their values.

---

## Perf 6 Phase C -- Pool per-glyph `RasterizedGlyph.BitmapData`

### What it is

Each step of the per-glyph transform pipeline (effects composite, post-processors, super-sample
downscale) constructs a **new** `RasterizedGlyph` with a fresh `byte[] BitmapData`, discarding the
previous instance's buffer. For a large run this churns substantial Gen 0/1 garbage. Returning the
*intermediate* buffer to an `ArrayPool` before replacing it would eliminate most of that churn.

### Files / types involved

| File | Role |
|------|------|
| `src/KernSmith/Rasterizer/RasterizedGlyph.cs` | Holds `public required byte[] BitmapData { get; init; }` -- immutable, no ownership tracking |
| `src/KernSmith/BmFont.cs` | The merged per-glyph transform loop (from Phase 37) that replaces glyph instances |
| `src/KernSmith/Rasterizer/GlyphCompositor.cs`, effect/post-processor classes | Produce successive `RasterizedGlyph` instances |

### Why it was deferred

The **final** glyph buffer flows into the public atlas pages (and from there into `BmFontResult` and
the public API). `RasterizedGlyph` is an immutable `sealed class` with `required init` properties and
**no ownership tracking** -- there is no way at the point of replacement to distinguish an
*intermediate* buffer (safe to pool: it is about to be thrown away) from the *final* buffer (must not
be pooled: it is copied into / referenced by the atlas page that escapes to the caller). Pooling
indiscriminately would return a live, caller-visible buffer to the pool. This is the same
escape-to-public-API hazard as Phase B, one layer earlier in the pipeline, and it shares the same
blocker: the design needs a way to express buffer ownership / "this buffer is intermediate".

### Sketch of the approach

- Add ownership tracking to the transform pipeline so each loop iteration can return the **previous**
  glyph's buffer to `ArrayPool<byte>.Shared` *only* when that buffer is provably intermediate (a new
  instance was produced this step and the old one is no longer referenced).
- The **final** glyph buffer that feeds atlas packing must be excluded -- either by copying it into the
  (pooled or owned) atlas page buffer from Phase B, or by marking it non-poolable.
- Likely depends on / pairs with Phase B's ownership model so that "final glyph buffer -> atlas page
  buffer" has a single, clear ownership handoff rather than two competing pooling schemes.
- Loop bounds use the requested length, never `array.Length`; clear-before-read only where a buffer is
  read before being fully written.

### Risks

- **Use-after-return on caller-held pixels** -- if the final-vs-intermediate classification is wrong by
  even one glyph, a buffer still referenced by an atlas page gets recycled and corrupted.
- **Coupling to Phase B** -- doing C well almost certainly requires B's ownership model first;
  attempting C alone re-introduces an ad-hoc lifetime scheme.
- **Parallel pipeline interaction** -- the per-glyph loop is parallelized (Perf 6b); pooling must be
  thread-safe (`ArrayPool<T>.Shared` is, but the rent/return bookkeeping per glyph must not race).

### Validation requirement

Same bar as Phase B: the bmfont-compare regression harness must pass **exit 0 -- 0 diffs** across all
backends and FNT metadata. The optimization only changes buffer provenance, not pixel or metric values.

---

## Excluded: Perf 10 (integer alpha blending)

**Closed permanently in Phase 95 -- proven mathematically impossible. Do NOT carry forward.**

Phase 95 ran a brute-force sweep of all 4.28 billion `(srcC, srcA, dstC, dstA)` combinations and found
~4.3M channel results where the float alpha-over blend and *any* integer equivalent diverge (IEEE-754
per-step rounding crosses integer boundaries ~0.1% of the time). A byte-identical integer rewrite of
the blend cannot exist, so the three blend sites (`GlyphCompositor`, `OutlinePostProcessor`,
`ShadowPostProcessor`) intentionally remain `float`. This item is recorded here only to prevent it
being re-opened; it is not deferred work.

---

## Implementation Order (when picked up)

1. **Perf 6 Phase B first** -- establish the atlas page buffer-ownership model (`IDisposable` /
   ownership flag). This is the foundational redesign.
2. **Perf 6 Phase C second** -- build per-glyph intermediate-buffer pooling on top of Phase B's
   ownership model, with the final glyph buffer handed off cleanly to the (owned) atlas page buffer.
3. After each phase, run the full build (net8/net10) + test suite, then the bmfont-compare regression
   harness, and require **0 diffs** before considering the phase done.
