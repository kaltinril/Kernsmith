# Phase 31 — StbTrueType Managed Rasterizer Plugin

> **Status**: Planning
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction)
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39), Phase 32 (WASM validation)

## Goal

Add a pure C# rasterizer backend using **StbTrueTypeSharp** so KernSmith can generate bitmap fonts on platforms without native library support (Blazor WASM, iOS AOT, serverless, containers).

## Why StbTrueTypeSharp

| Criterion | StbTrueTypeSharp | SixLabors.Fonts | Typography/PixelFarm | Roll Our Own |
|-----------|-----------------|-----------------|---------------------|--------------|
| **Pure C#** | Yes | Yes | Yes | Yes |
| **License** | Public Domain | Split (free for OSS/transitive) | MIT | N/A |
| **Rasterizes glyphs** | Yes | No (outlines only) | Yes (via PixelFarm) | Yes |
| **Hinting** | No | Yes | No | No (months of work) |
| **Dependencies** | Zero | Heavy | Very heavy | Zero |
| **Production proven** | Yes (FontStashSharp) | Yes | Limited | No |
| **NuGet package** | Yes (StbTrueTypeSharp) | Yes | No (rasterizer) | N/A |
| **Integration effort** | Days | Weeks (need scanline rasterizer) | Weeks (extract from PixelFarm) | Months |
| **Maintenance burden** | Low (stable port) | Low | High | Very high |

**Decision: StbTrueTypeSharp** — Public Domain, zero dependencies, proven quality via FontStashSharp, can be vendored. The main trade-off is no hinting, which matters most at small sizes (< 16px). For bitmap font generation where users typically pick sizes >= 16px, this is acceptable.

### Alternatives Evaluated and Rejected

**SixLabors.Fonts**: Provides glyph outlines via `IGlyphRenderer` but does NOT rasterize — we'd still need to write a scanline rasterizer. Split license adds complexity for downstream users. Overkill as a dependency since KernSmith already parses TTF tables.

**Typography/PixelFarm**: Has a pure C# rasterizer (MiniAgg-based) but it's deeply embedded in the PixelFarm ecosystem. Not available as a clean NuGet package. Extracting just the rasterizer would be significant work.

**Roll our own**: KernSmith already parses TTF tables but would need `glyf`/`loca`/`cmap` table parsing (~1 week), a scanline rasterizer (~1-2 weeks), composite glyph support, and edge case handling. Total: 2-4 weeks without hinting, 3-6 months with hinting. The TrueType hinting VM alone is ~15,000+ lines (FreeType reference). Not justified when StbTrueTypeSharp exists as a proven, Public Domain solution.

**SharpFont**: Abandoned. Still depends on native FreeType. Doesn't solve the problem.

**NRasterizer / LunarFonts**: Too small/incomplete/abandoned for production use.

### References for Rolling Our Own (Future — Phase 33)

- [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) — Production C# font rendering using StbTrueTypeSharp, reference architecture
- [Coding Adventure: Rendering Text (Sebastian Lague)](https://www.youtube.com/watch?v=LaYPoMPRSlk) — Visual walkthrough of TTF parsing and bezier rasterization
- [How Do Fonts Work? (Reducible)](https://www.youtube.com/watch?v=SO83KQuuZvg) — Deep dive into font rasterization algorithms
- [Implementing a Font Reader and Rasterizer from Scratch](https://handmade.network/forums/articles/t/7330-implementing_a_font_reader_and_rasterizer_from_scratch%252C_part_1__ttf_font_reader.) — Step-by-step TTF parser and rasterizer tutorial
- [NRasterizer](https://github.com/vidstige/NRasterizer) — Simple pure C# TTF rasterizer (Apache-2.0), reference implementation
- [VectSharp Text Rendering](https://giorgiobianchini.com/VectSharp/text.html) — C# vector graphics library with font rendering
- [Adobe Community: Font Parsing Algorithms](https://community.adobe.com/questions-94/where-can-i-find-font-parsing-or-text-rasterization-algorithm-1502180) — Discussion of rasterization algorithm resources
- [Efficient Text Rendering in C# (StackOverflow)](https://stackoverflow.com/questions/61584477/efficient-text-rendering-on-bitmap-in-c-sharp-with-system-drawing) — Performance techniques for bitmap text rendering
- [Font Rasterization (Wikipedia)](https://en.wikipedia.org/wiki/Font_rasterization) — Overview of rasterization techniques, hinting, anti-aliasing
- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — Original C reference implementation (~5,000 lines)

## Feature Parity

| Feature | FreeType | StbTrueType | Gap Impact |
|---------|----------|-------------|------------|
| TTF rasterization | Yes | Yes | None |
| Anti-aliasing | Yes | Yes | None |
| Font metrics | Yes | Yes | None |
| Legacy kern table | Yes | Yes | None |
| GPOS kerning | Via FT | No | **None** — KernSmith has its own GPOS parser |
| TrueType hinting | Yes | No | Lower quality at small sizes (< 16px) |
| Synthetic bold/italic | Yes | No | Must implement in post-processing or skip |
| COLR/CPAL color fonts | Yes | No | Feature gap — acceptable for fallback |
| Variable font axes | Yes | No | Feature gap — acceptable for fallback |
| SDF rendering | Yes | No | Must implement via post-processing or skip |
| Outline stroking | Yes (FT_Stroker) | No | Use EDT-based outline (already exists) |
| OTF/CFF outlines | Yes | No | TTF only — acceptable for fallback |
| System font loading | Yes | No | `SupportsSystemFonts = false` |
| TTC multi-face | Yes | Yes | None |

**Key insight**: KernSmith's own TTF parser already handles GPOS kerning, so StbTrueType's lack of GPOS is not a gap. The main gaps (hinting, color fonts, variable fonts, SDF) are acceptable for a "works everywhere" fallback — users needing those features use the FreeType backend.

## Implementation Plan

### Step 1: Create project

```
src/KernSmith.Rasterizers.StbTrueType/
├── KernSmith.Rasterizers.StbTrueType.csproj
├── StbTrueTypeRasterizer.cs
├── StbTrueTypeCapabilities.cs
└── StbTrueTypeRegistration.cs
```

**Project file:**
- Target: `net8.0;net10.0`
- Dependencies: `KernSmith` (core) + `StbTrueTypeSharp` (latest)
- NuGet package: `KernSmith.Rasterizers.StbTrueType`
- Namespace: `KernSmith.Rasterizers.StbTrueType`
- No `AllowUnsafeBlocks` needed (pure managed)

### Step 2: Implement `IRasterizer`

```csharp
public sealed class StbTrueTypeRasterizer : IRasterizer
{
    // LoadFont — parse TTF bytes via StbTrueType.CreateFont()
    // RasterizeGlyph — StbTrueType.GetCodepointBitmap() for greyscale
    // GetGlyphMetrics — StbTrueType.GetCodepointHMetrics() + bbox
    // GetFontMetrics — StbTrueType.GetFontVMetrics()
    // GetKerningPairs — return null (KernSmith handles GPOS separately)
    // LoadSystemFont — throw NotSupportedException
    // SetVariationAxes — throw NotSupportedException
    // SelectColorPalette — throw NotSupportedException
}
```

**Capabilities:**
```csharp
public sealed class StbTrueTypeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => false;
    public bool SupportsSdf => false;         // Could add post-process SDF later
    public bool SupportsOutlineStroke => false; // Use EDT outline effect instead
    public bool SupportsSystemFonts => false;
    public bool HandlesOwnSizing => false;
}
```

### Step 3: Add `[ModuleInitializer]` registration

```csharp
internal static class StbTrueTypeRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
    }
}
```

### Step 4: Add `StbTrueType` to `RasterizerBackend` enum

Add a new enum value to `RasterizerBackend` in the core library.

### Step 5: Backend auto-detection

Update `RasterizerFactory` to support auto-detection:
- If user specifies `Backend = RasterizerBackend.Auto`, prefer FreeType when available, fall back to StbTrueType
- Clear error if no backend is registered

### Step 6: Testing

- Run existing test suite with `Backend = RasterizerBackend.StbTrueType`
- Add comparison tests: FreeType vs StbTrueType output for same font/size
- Document expected differences (hinting, anti-aliasing quality)
- Test with `Roboto-Regular.ttf` fixture

## Success Criteria

- [ ] `StbTrueTypeRasterizer` implements full `IRasterizer` interface
- [ ] Registered via `[ModuleInitializer]` (same pattern as GDI/DirectWrite)
- [ ] Generates valid BMFont output for ASCII + extended Unicode
- [ ] No native dependencies — runs on any .NET platform
- [ ] Glyph metrics are within acceptable tolerance of FreeType output
- [ ] All existing non-FreeType-specific tests pass

## Sources

- [StbTrueTypeSharp on GitHub](https://github.com/StbSharp/StbTrueTypeSharp) — Public Domain C# port
- [StbTrueTypeSharp on NuGet](https://www.nuget.org/packages/StbTrueTypeSharp)
- [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) — Production validation of StbTrueTypeSharp
- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — Original C implementation
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39) — Feature request
- [Phase 78E — Plugin Template](done/phase-78e-plugin-template.md) — Plugin pattern to follow
