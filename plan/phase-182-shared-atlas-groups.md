# Phase 182 — Shared Atlas Groups

> **Status**: Complete
> **Tracks**: [GitHub issue #175](https://github.com/kaltinril/Kernsmith/issues/175)
> **Supersedes**: [Phase 181](phase-181-shadow-outline-atlas-variants.md) (dropshadow-only framing, too narrow)

## Problem

Two separate needs both boil down to "pack N independent glyph renderings into one shared atlas PNG":

1. **Dropshadow variant** (original ask, phase 181) — same font, same character set, a second rendering (flat silhouette) that a consumer draws translated/tinted. Wanted on the same PNG as the primary glyphs so drawing glyph+shadow never switches texture.
2. **Multi-font text** — mixing two entirely different fonts in one string also switches texture (and often render state) per font today. Same underlying want: multiple independent glyph sets, one shared atlas.

Building the dropshadow variant as a one-off (what PR #176 did — its own separate PNG per variant) solves neither generally and would need redoing when multi-font packing comes up.

## Goal

A generic **shared atlas group**: multiple independent glyph sources (a font's primary glyphs, a dropshadow variant, a second font's glyphs, whatever) submit rects into one packing pass, producing one shared atlas + `PageEntry` list, with each source still getting back its own complete `BmFontModel`/`.fnt`.

First concrete consumer: the dropshadow variant from phase 181, now built correctly on top of this instead of its own PNG. Multi-font packing is not built in this phase but the mechanism must not need a redesign to support it later.

## Design

- **`AtlasGroupBuilder`** (name tentative): accepts N glyph-rect sources, each tagged. Packs all rects in one `IAtlasPacker.Pack()` call, then splits placements back by tag to build each source's `BmFontModel`.
- **`BmFontModelBuilder`** currently derives `Common.ScaleW/ScaleH/Pages`/`PageEntry` filenames from one `PackResult` assumed uniform across all its `CharEntry`s (confirmed while building PR #176). Needs a path that takes a shared `PageEntry` list + a filtered placement subset instead.
- **`AtlasSizeEstimator`** needs to size for the combined rect set across all group members, not just one source.
- Dropshadow variant becomes: primary font glyphs + shadow silhouette glyphs (same character set), submitted as two sources to one group. Everything else from phase 181's design carries over unchanged (`AtlasVariant` config, `ExtendedMetadata` sibling fields, CLI/`.bmfc` surface, one variant per `Kind` in v1).
- Multi-font packing shape (config surface, how two `FontGeneratorOptions` combine) is explicitly **not** designed here — just don't paint the mechanism into a corner that can't support it.

## Non-Goals

- Not implementing multi-font packing itself in this phase.
- Not reworking `IAtlasPacker`'s rect/pack API — `Pack()` already takes a plain rect list, that's sufficient.

## Open Questions

None currently.

## Affected Files

*(Updated to match what actually shipped.)*

- `src/KernSmith/Output/BmFontModelBuilder.cs` — shared-`PageEntry` build path
- `src/KernSmith/Atlas/AtlasGroupBuilder.cs` (new) — rect submission + placement splitting by source
- `src/KernSmith/BmFont.cs` — combines each source's rects and calls `AtlasSizeEstimator.Estimate(combinedRects, groupSizingOptions)` (`:774`). **`AtlasSizeEstimator.cs` itself was not modified** — the combining happens at the call site
- `src/KernSmith/Config/AtlasVariant.cs`, `FontGeneratorOptions.cs` — carried over from phase 181
- `src/KernSmith/Output/Model/ExtendedMetadata.cs` — `VariantOf`/`Variants`, including XML/binary round-trip (gap left open in PR #176)
- `tools/KernSmith.Cli/` — `.bmfc`/CLI surface, carried over from phase 181
- `src/KernSmith/Rasterizer/ShadowCoveragePostProcessor.cs` — the shipped shadow-silhouette implementation. It is an internal `IGlyphPostProcessor`, **not** an `IGlyphEffect`; there is no `ShadowSilhouetteEffect.cs`

## Implementation Checklist (TDD, red→green per step)

- [x] `AtlasGroupBuilder` — packs rects from 2 tagged sources into one `PackResult`, splits placements back by tag
- [x] `BmFontModelBuilder` shared-`PageEntry` path — two `BmFontModel`s built from one `PackResult`, both referencing the same `PageEntry` list, each only its own `CharEntry`s
- [x] `AtlasSizeEstimator` combined sizing — estimate grows to fit both sources' rects
- [x] Shadow silhouette rasterization (from phase 181, if not reused from PR #176) — no offset/color baked in
- [x] Dropshadow variant end-to-end via `AtlasGroupBuilder` — primary + shadow on one shared PNG, no bleed between them
- [x] `ExtendedMetadata` sibling fields — round-trip through text **and** XML/binary writers/readers (finish what PR #176 left incomplete)
- [x] CLI/`.bmfc` — variant config parses, writes `name-shadow.fnt` sharing the primary's `.png`
- [x] Confirm no-variant, no-group generation stays byte-identical to today

## Testing

- xUnit + Shouldly per checklist item
- Atlas-packing change → run `tests/bmfont-compare/regression_check.py`, not just unit tests. Add/reuse a `.bmfc` config exercising the dropshadow variant.
- Verify the shared PNG actually contains both sources' glyphs at non-overlapping regions (visual/pixel check, not just placement math)
