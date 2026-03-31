# Phase 35 — FontStashSharp Rasterizer Plugin

> **Status**: Future (Recommend Defer Indefinitely)
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction), Phase 32 (StbTrueType rasterizer — hard prerequisite for comparison)
> **Related**: Phase 34 (Custom rasterizer)

## Goal

Add a rasterizer plugin backed by **FontStashSharp**, providing an alternative pure C# rasterization path that leverages FontStashSharp's higher-level font rendering capabilities.

## Recommendation: Defer Indefinitely

**FontStashSharp is NOT a rasterizer** — it is a font atlas and text rendering system that wraps rasterizers (StbTrueTypeSharp, FreeType, SixLabors.Fonts) via its `IFontLoader`/`IFontSource` abstraction.

Using it as a KernSmith rasterizer backend adds a layer of indirection with zero benefit:

```
KernSmith.IRasterizer -> FontStashSharpRasterizer -> IFontSource -> StbTrueTypeSharpSource -> StbTrueTypeSharp
```

vs Phase 32's direct approach:

```
KernSmith.IRasterizer -> StbTrueTypeRasterizer -> StbTrueTypeSharp
```

FontStashSharp's value-adds (atlas management, text layout, glyph caching, game engine integration) are all features KernSmith already has or does not need.

**The only scenario where this phase adds value**: if MonoGame/FNA users specifically want FontStashSharp interop (sharing font data at runtime). This should be driven by user demand, not proactive development.

**Dependency weight concern**: FontStashSharp would pull in: FontStashSharp.Base, FontStashSharp.Rasterizers.StbTrueTypeSharp, StbTrueTypeSharp, potentially Cyotek.Drawing.BitmapFont, StbImageSharp. Phase 32 only needs StbTrueTypeSharp — the additional dependencies provide zero benefit.

## Why FontStashSharp

[FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) is a mature, production-proven C# text rendering library (349 stars, Zlib license, last release Feb 2026). It uses StbTrueTypeSharp internally but adds:

- **Dynamic font atlas management** — automatic texture atlas creation and packing
- **Multiple font backends** — StbTrueTypeSharp (default) and FreeType (optional)
- **Text measurement and layout** — line breaking, alignment, wrapping
- **Texture atlas caching** — glyphs rendered on demand and cached
- **MonoGame/FNA/Stride integration** — designed for game engines

## Relationship to Phase 32 (StbTrueType)

| Aspect | Phase 32 (StbTrueType) | Phase 35 (FontStashSharp) |
|--------|----------------------|--------------------------|
| **Dependency** | StbTrueTypeSharp only | FontStashSharp (includes StbTrueTypeSharp) |
| **Level** | Low-level glyph rasterization | Higher-level font rendering |
| **Control** | Full control over each glyph | FontStashSharp manages rendering |
| **Weight** | Minimal | Heavier (pulls in atlas, layout, etc.) |
| **Use case** | KernSmith controls the pipeline | Leverage FontStashSharp's optimizations |
| **WASM** | Yes | Yes |

**Key question**: Does FontStashSharp add value over raw StbTrueTypeSharp for KernSmith's use case?

KernSmith already has its own atlas packer, metrics pipeline, and output formatters. FontStashSharp's atlas management and text layout features would be redundant. The main value would be if FontStashSharp provides better glyph rendering quality or performance than raw StbTrueTypeSharp calls.

## Investigation Plan

### Step 1: Evaluate FontStashSharp's glyph API

**Answer**: FontStashSharp.Base (separate repo: https://github.com/FontStashSharp/FontStashSharp.Base) exposes `IFontSource` with methods: `GetMetricsForSize()`, `GetGlyphId()`, `GetGlyphMetrics()`, `RasterizeGlyphBitmap()`, `GetGlyphKernAdvance()`, `CalculateScaleForTextShaper()`.

So yes, per-glyph APIs exist — but they delegate directly to StbTrueTypeSharp, adding no rendering quality or performance benefit.

### Step 2: Prototype

If FontStashSharp exposes useful glyph-level APIs:

```
src/KernSmith.Rasterizers.FontStashSharp/
+-- KernSmith.Rasterizers.FontStashSharp.csproj
+-- FontStashSharpRasterizer.cs
+-- FontStashSharpCapabilities.cs
+-- FontStashSharpRegistration.cs
```

### Step 3: Compare output

- Same font, same size, same charset
- Compare: Phase 32 (raw StbTrueType) vs Phase 35 (FontStashSharp) vs FreeType baseline
- Measure: rendering quality, metrics accuracy, performance

## Capabilities

```csharp
public sealed class FontStashSharpCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => false;
    public bool SupportsSdf => false;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool HandlesOwnSizing => false;
    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes =>
        [AntiAliasMode.None, AntiAliasMode.Normal];
}
```

## Testing (if phase is ever implemented)

- **Comparison tests against Phase 32 output** — expected to be pixel-identical since both ultimately call StbTrueTypeSharp
- **Performance benchmarks** — FontStashSharp wrapper vs direct StbTrueTypeSharp (Phase 32); expect FontStashSharp to be equal or slower due to indirection overhead
- **Metric accuracy tests** — verify glyph metrics match Phase 32 exactly

## Decision Framework

**Build this plugin IF:**
- Users in the MonoGame/FNA ecosystem specifically request FontStashSharp interop
- A concrete use case emerges for sharing FontStashSharp font data with KernSmith at runtime

**Skip this plugin IF (current recommendation):**
- FontStashSharp's value is in text layout/atlas (which KernSmith already has)
- Raw StbTrueTypeSharp produces equivalent output (it does — FontStashSharp delegates to it)
- The additional dependency weight is not justified
- No user demand materializes

## License

FontStashSharp uses the **Zlib license** — very permissive, compatible with MIT. No concerns.

## Sources

- [FontStashSharp on GitHub](https://github.com/FontStashSharp/FontStashSharp) — 349 stars, Zlib license
- [FontStashSharp.Base on GitHub](https://github.com/FontStashSharp/FontStashSharp.Base) — architecturally relevant package (1.2.2)
- [FontStashSharp.PlatformAgnostic on NuGet](https://www.nuget.org/packages/FontStashSharp.PlatformAgnostic) — the `FontStashSharp` package has been deprecated/renamed
- [FontStashSharp.Base on NuGet](https://www.nuget.org/packages/FontStashSharp.Base) — 1.2.2
- [FontStashSharp MonoGame integration](https://github.com/FontStashSharp/FontStashSharp/wiki)
- [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp) — underlying rasterizer
