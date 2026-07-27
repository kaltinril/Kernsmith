# Phase 181 — Atlas Variants: Padded Silhouette Regions (Shadow/Outline/etc.)

> **Status**: Superseded by [Phase 182](phase-182-shared-atlas-groups.md) — dropshadow-only variant packing was too narrow; the same "pack N independent glyph sets into one shared atlas" need also applies to mixing multiple fonts in one PNG (avoid texture/render-state switches when drawing). Phase 182 covers both.
> **Tracks**: [GitHub issue #175](https://github.com/kaltinril/Kernsmith/issues/175)
> **Related**: `ChannelContent.Shadow` (PR #167). Phase 50 (in-memory layer retention) is a different approach, not folded in here. PR #176 is a first-draft implementation of this doc (separate PNG per variant) — needs rework against Phase 182's shared-atlas design before merging.

## Problem

Gum bakes dropshadow into the glyph PNG. Three problems:

1. Offset baked in → changing it means regenerating the atlas
2. Negative offsets paint over already-drawn neighbors. Glyph+shadow is one baked sprite, drawn per-character in sequence, so a shadow reaching left/up lands on top of the previous letter or line that already drew. This is a draw-order problem, not a texture-padding problem — separating shadow into its own sprite lets the consumer draw all shadows first, then all glyphs on top, so ordering stops mattering.
3. `ChannelContent.Shadow` fixes tint but still requires a custom shader to unpack the channel — no plain sprite-batch draw

## Goal

Pack a second (Nth) character-set rendering into the same shared atlas, sampleable as an ordinary RGBA glyph. First target: dropshadow silhouette — flat coverage shape, no baked offset/color. Consumer translates and tints it at draw time; runtime translation doesn't touch texture sampling, so no atlas change needed for that. Generalizes later to outline/small-caps/bold (different geometry, same mechanism) — not building those now.

Existing single-variant `Generate()` path stays untouched when no variant is requested.

## Design

- **Variant region**: extra character set rendered into the same atlas page(s), packed alongside the primary glyphs in one shared bin-packing pass. Produces an extra `BmFontModel` per variant, same `PageEntry`s as the primary. Each variant cell is sized to its own ink bounds (e.g. a blurred silhouette can be bigger than the letter) using the same atlas-wide `Padding` every glyph already gets — no new packer concept needed.
- **Config** (recommendation, needs sign-off):

```csharp
public sealed record AtlasVariant(string Name, AtlasVariantKind Kind, int BlurRadius = 0, bool HardShadow = false);
public enum AtlasVariantKind { ShadowSilhouette = 0 }
```
`FontGeneratorOptions.Variants`, `BmFontResult.VariantModels["name"]` in-memory dict. CLI/`.bmfc` also write each variant as its own physical `.fnt` (`name-shadow.fnt` etc.) sharing the primary's `.png`.

- **Sibling discoverability**: stock BMFont has no "part of a group" field. Add `VariantOf` / `Variants` to `ExtendedMetadata` so one `.fnt` points at the other instead of relying on filename convention.
- **`.bmfc`/CLI**: supported from v1, not API-only.
- **One variant per `Kind`** in v1 (no multiple blur radii etc. yet).
- **`AtlasSizeEstimator`** must account for variant cell area (can exceed the base glyph's, e.g. blur), or page sizing will undercount.

## Open Questions

None currently.

## Affected Files

- `src/KernSmith/Config/AtlasVariant.cs` (new), `FontGeneratorOptions.cs`
- `src/KernSmith/Atlas/ChannelCompositor.cs` or new `VariantCompositor.cs`
- `src/KernSmith/Atlas/AtlasSizeEstimator.cs`
- `src/KernSmith/Output/BmFontModelBuilder.cs`, `BmFontResult.cs`
- `src/KernSmith/BmFont.cs`
- `src/KernSmith/Rasterizer/ShadowSilhouetteEffect.cs` (new, or `ShadowEffect` flat-mode)
- `src/KernSmith/Output/Model/ExtendedMetadata.cs` — `VariantOf`/`Variants` fields
- `tools/KernSmith.Cli/` — `.bmfc` parsing + CLI flags for `AtlasVariant`
- `src/KernSmith/Output/FileWriter.cs` — write variant `.fnt` files alongside primary

## Implementation Checklist (TDD, red→green per step)

- [ ] `AtlasVariant`/`AtlasVariantKind` types — no-op when `Variants` is null
- [ ] Shadow silhouette rasterization — no offset/color baked in
- [ ] Variant compositing into shared atlas — primary glyph pixels unaffected, variant cell sized to its own ink bounds + standard `Padding`
- [ ] Variant `BmFontModel` construction — one `CharEntry` per codepoint, shares `PageEntry`s
- [ ] `AtlasSizeEstimator` variant-aware sizing
- [ ] `ExtendedMetadata` sibling fields — variant `.fnt` and primary `.fnt` each reference the other
- [ ] CLI/`.bmfc` — variant config parses, writes `name-shadow.fnt` alongside the primary
- [ ] End-to-end: sample `VariantModels["shadow"]` region, verify it matches the rasterized silhouette with no bleed from neighbors

## Testing

- xUnit + Shouldly per checklist item
- Atlas-packing change → run `tests/bmfont-compare/regression_check.py`, not just unit tests. Add a `.bmfc` config exercising `AtlasVariant` since none of the existing ones will.
- Confirm no-variant generation stays byte-identical before/after
