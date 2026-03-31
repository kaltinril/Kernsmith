# Phase 34 — FontStashSharp Rasterizer Plugin

> **Status**: Future
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction)
> **Related**: Phase 31 (StbTrueType), Phase 33 (Custom rasterizer)

## Goal

Add a rasterizer plugin backed by **FontStashSharp**, providing an alternative pure C# rasterization path that leverages FontStashSharp's higher-level font rendering capabilities.

## Why FontStashSharp

[FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) is a mature, production-proven C# text rendering library (349 stars, Zlib license, last release Feb 2026). It uses StbTrueTypeSharp internally but adds:

- **Dynamic font atlas management** — automatic texture atlas creation and packing
- **Multiple font backends** — StbTrueTypeSharp (default) and FreeType (optional)
- **Text measurement and layout** — line breaking, alignment, wrapping
- **Texture atlas caching** — glyphs rendered on demand and cached
- **MonoGame/FNA/Stride integration** — designed for game engines

## Relationship to Phase 31 (StbTrueType)

| Aspect | Phase 31 (StbTrueType) | Phase 34 (FontStashSharp) |
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

- Can we extract individual glyph bitmaps (not rendered text)?
- Does it expose per-glyph metrics (advance, bearing, bbox)?
- Can we bypass its atlas system and just get raw glyph bitmaps?
- What rendering quality improvements does it add over raw StbTrueTypeSharp?

### Step 2: Prototype

If FontStashSharp exposes useful glyph-level APIs:

```
src/KernSmith.Rasterizers.FontStashSharp/
├── KernSmith.Rasterizers.FontStashSharp.csproj
├── FontStashSharpRasterizer.cs
├── FontStashSharpCapabilities.cs
└── FontStashSharpRegistration.cs
```

### Step 3: Compare output

- Same font, same size, same charset
- Compare: Phase 31 (raw StbTrueType) vs Phase 34 (FontStashSharp) vs FreeType baseline
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
}
```

## Decision Framework

**Build this plugin IF:**
- FontStashSharp provides measurably better glyph quality than raw StbTrueTypeSharp
- FontStashSharp exposes glyph-level APIs that KernSmith can use
- Users in the MonoGame/FNA ecosystem want FontStashSharp interop
- Performance improvements justify the additional dependency

**Skip this plugin IF:**
- FontStashSharp's value is in text layout/atlas (which KernSmith already has)
- Raw StbTrueTypeSharp produces equivalent output
- The additional dependency weight isn't justified
- FontStashSharp doesn't expose individual glyph bitmap APIs

## License

FontStashSharp uses the **Zlib license** — very permissive, compatible with MIT. No concerns.

## Sources

- [FontStashSharp on GitHub](https://github.com/FontStashSharp/FontStashSharp) — 349 stars, Zlib license
- [FontStashSharp on NuGet](https://www.nuget.org/packages/FontStashSharp)
- [FontStashSharp MonoGame integration](https://github.com/FontStashSharp/FontStashSharp/wiki)
- [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp) — underlying rasterizer
