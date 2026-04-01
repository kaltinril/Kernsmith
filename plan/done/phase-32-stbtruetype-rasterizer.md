# Phase 32 — StbTrueType Managed Rasterizer Plugin

> **Status**: Complete
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction), Phase 31 (WASM restrictions research)
> **Blocks**: Phase 33 (WASM validation)
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39), Phase 33 (WASM validation)

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

### References for Rolling Our Own (Future — Phase 34)

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
| SDF rendering | Yes | Yes | None — StbTrueTypeSharp supports SDF via `stbtt_GetCodepointSDF()` and `stbtt_GetGlyphSDF()` |
| Outline stroking | Yes (FT_Stroker) | No | Use EDT-based outline (already exists) |
| OTF/CFF outlines | Yes | No | TTF only — acceptable for fallback |
| System font loading | Yes | No | `SupportsSystemFonts = false` |
| TTC multi-face | Yes | Yes | None |

**Key insight**: KernSmith's own TTF parser already handles GPOS kerning, so StbTrueType's lack of GPOS is not a gap. The main gaps (hinting, color fonts, variable fonts) are acceptable for a "works everywhere" fallback — users needing those features use the FreeType backend.

## Implementation Plan

### Step 1: Create project

```
src/KernSmith.Rasterizers.StbTrueType/
├── KernSmith.Rasterizers.StbTrueType.csproj
├── StbTrueTypeRasterizer.cs
├── StbTrueTypeCapabilities.cs
├── StbTrueTypeRegistration.cs
└── README.md
```

**Project file:**
- Target: `net8.0;net10.0`
- Dependencies: `KernSmith` (core) + `StbTrueTypeSharp` (1.26.12)
- NuGet package: `KernSmith.Rasterizers.StbTrueType`
- Namespace: `KernSmith.Rasterizers.StbTrueType`
- `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` — StbTrueTypeSharp is pure C# (no native P/Invoke) but uses unsafe pointer arithmetic extensively. AllowUnsafeBlocks is required.
- `<IsTrimmable>true</IsTrimmable>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` — required for Blazor WASM trimming compatibility
- `<IsAotCompatible>true</IsAotCompatible>` — enables AOT analyzers; AOT is critical for acceptable WASM performance (interpreter mode is extremely slow)
- Add project to `KernSmith.sln`
- Create `README.md` for NuGet package (follow GDI plugin pattern)

### Step 2: Implement `IRasterizer`

> **Note:** C# method names in StbTrueTypeSharp may differ from the C API names (e.g., `stbtt_GetCodepointBitmap`). Study [FontStashSharp.Rasterizers.StbTrueTypeSharp](https://github.com/FontStashSharp/FontStashSharp) source for correct C# API usage patterns.

```csharp
public sealed class StbTrueTypeRasterizer : IRasterizer, IDisposable
{
    // LoadFont — parse TTF bytes via StbTrueType.CreateFont()
    // RasterizeGlyph — StbTrueType.GetCodepointBitmap() for greyscale
    // RasterizeAll(IEnumerable<int>, RasterOptions) — loop over codepoints
    // GetGlyphMetrics — StbTrueType.GetCodepointHMetrics() + bbox (avoid full rasterization)
    // GetFontMetrics — StbTrueType.GetFontVMetrics()
    // GetKerningPairs — return null (KernSmith handles GPOS separately)
    // LoadSystemFont — throw NotSupportedException
    // SetVariationAxes — throw NotSupportedException
    // SelectColorPalette — throw NotSupportedException
    // Dispose — IRasterizer requires IDisposable; StbTrueType.FontInfo doesn't need
    //           disposal but implement proper dispose pattern for the interface contract
}
```

**Capabilities:**
```csharp
public sealed class StbTrueTypeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => false;
    public bool SupportsSdf => true;              // Via stbtt_GetCodepointSDF() / stbtt_GetGlyphSDF()
    public bool SupportsOutlineStroke => false;    // Use EDT outline effect instead
    public bool SupportsSystemFonts => false;
    public bool HandlesOwnSizing => false;
    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes =>
        [AntiAliasMode.None, AntiAliasMode.Grayscale];
}
```

### Step 3: Add `[ModuleInitializer]` registration

```csharp
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
internal static class StbTrueTypeRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
    }
}
#pragma warning restore CA2255
```

### Step 4: Add `StbTrueType` to `RasterizerBackend` enum

Add a new enum value to `RasterizerBackend` in the core library.

### Step 5: Testing

See expanded testing strategy below.

## Implementation Details

### Memory Pinning

`ReadOnlyMemory<byte>` from `LoadFont` must be pinned or copied to a `byte[]` that outlives the stb `FontInfo` lifetime. The font data buffer must remain valid for the entire lifetime of the rasterizer instance. Use `GCHandle.Alloc(..., GCHandleType.Pinned)` or copy to a long-lived array.

In WASM, `GCHandle.Alloc(Pinned)` fragments the heap permanently (WASM linear memory never shrinks). Prefer `ArrayPool<byte>` for glyph bitmap buffers to enable buffer reuse and prevent permanent heap growth.

### Scale Factor

Use `stbtt_ScaleForMappingEmToPixels` (not `stbtt_ScaleForPixelHeight`) for FreeType parity. This gives ppem-based scaling that matches FreeType's `FT_Set_Char_Size` behavior.

### Coordinate System

stb_truetype uses a y-up coordinate system (y increases upward), while KernSmith `GlyphMetrics` expects screen coordinates. Ensure correct `BearingX` / `BearingY` mapping:
- `BearingX` = `ix0` (left side bearing in pixels)
- `BearingY` = `-iy0` (negate because stb y-up vs screen y-down)

### SuperSample

Render at `Size * SuperSample` scale, then downscale the resulting bitmap using box filter averaging. This matches the FreeType rasterizer's super-sampling approach.

### AntiAliasMode.None

stb_truetype always produces grayscale output. For `AntiAliasMode.None`, threshold the grayscale bitmap to 1-bit: any pixel >= 128 becomes 255, otherwise 0.

### EnableHinting

stb_truetype has no hinting support. When `EnableHinting` is set, ignore silently. The capabilities object reports this limitation so callers can check before relying on hinting.

### Bold / Italic

Synthetic bold and italic are not supported by stb_truetype. Check capabilities before applying. Throw `NotSupportedException` if bold/italic is requested and the capability is not available.

### Bitmap Data Ownership

stb_truetype allocates bitmap memory internally via `Marshal.AllocHGlobal`. Wrap every allocation in `try/finally` with `Marshal.FreeHGlobal` to prevent leaks — especially critical in WASM where the heap never shrinks. Copy the bitmap data to a managed `byte[]` before freeing the unmanaged buffer. Do not return pointers to stb-owned memory.

### Threading Constraints

The StbTrueType rasterizer must not use any blocking or parallel primitives:
- No `Parallel.ForEach` — throws `PlatformNotSupportedException` in single-threaded WASM (dotnet/runtime#43411)
- No `.Result`, `.Wait()`, `.WaitAll()` — deadlocks the single browser thread
- No `Thread.Sleep` — deadlocks the browser tab
- `Task.Run` does not offload to a separate thread in WASM — it schedules on the same thread

These constraints apply to the rasterizer implementation itself. Blazor UI code calling the rasterizer should yield periodically via `Task.Delay(1)` (not `Task.Yield()`, which does not reliably yield to the browser render pipeline).

## Testing Strategy

### Factory Registration Tests
- Follow `GdiRasterizerTests.cs` pattern with `[Collection("RasterizerFactory")]`
- Verify `RasterizerFactory.Create(RasterizerBackend.StbTrueType)` returns correct type
- Verify registration via `[ModuleInitializer]`

### Capabilities Verification Tests
- `SupportsSdf` returns `true`
- `SupportsColorFonts` returns `false`
- `SupportsVariableFonts` returns `false`
- `SupportsSystemFonts` returns `false`
- `SupportsOutlineStroke` returns `false`

### Font Loading Tests
- Load `Roboto-Regular.ttf` from test fixtures
- Reject invalid/corrupt font data gracefully
- Handle TTC files (multi-face)
- `LoadSystemFont` throws `NotSupportedException`

### Glyph Rasterization Tests
- Rasterize ASCII 'A' — verify non-empty bitmap with expected dimensions
- Missing codepoint — verify returns empty/null glyph data
- Space character — verify zero-width bitmap with correct advance

### Metrics Accuracy Tests
- Compare glyph metrics against FreeType baseline
- Tolerance: +/-1 pixel for dimensions, +/-1 pixel for bearings
- Verify font-level metrics (ascent, descent, line gap)

### SDF Rendering Tests
- Rasterize glyph with SDF enabled
- Verify output contains distance field values (not just binary/grayscale)
- Validate against known StbTrueTypeSharp SDF bug (StbSharp/StbTrueTypeSharp#1) — check for visual artifacts at glyph edges

### End-to-End Tests
- `BmFont.Generate()` with `Backend = RasterizerBackend.StbTrueType`
- Verify valid BMFont .fnt output
- Verify atlas PNG is generated

### Unsupported Feature Handling
- Bold style — verify appropriate error/exception
- ColorFont options — verify appropriate error/exception
- VariationAxes — verify `NotSupportedException`

## Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Memory management with unsafe pointers | High | Must pin font data for stb lifetime; use `GCHandle` or dedicated copy |
| Thread safety | Medium | StbTrueType FontInfo is not safe for concurrent use; document single-threaded requirement |
| Bitmap data ownership | Medium | Always copy stb-allocated bitmap to managed `byte[]` before freeing |
| Coordinate system mapping errors | High | This is the #1 source of bugs; thorough testing against FreeType baseline |
| StbTrueTypeSharp SDF quality bug | Medium | Known open bug (StbSharp/StbTrueTypeSharp#1, since 2020). Test SDF output quality early and document any visual artifacts |
| No WASM community validation | Medium | KernSmith would be the first known user of StbTrueTypeSharp in Blazor WASM. Budget time for discovering platform-specific issues |
| WASM interpreter performance | Medium | Interpreter mode is extremely slow (5-10s for simple ops). Recommend AOT compilation for consumers doing heavy rasterization |
| API name differences from C | Low | Study FontStashSharp source for correct C# API patterns |

## Success Criteria

- [x] `StbTrueTypeRasterizer` implements full `IRasterizer` interface (including `IDisposable`)
- [x] `RasterizeAll` loop implementation works for batch rasterization
- [x] `GetGlyphMetrics` implemented for performance (avoids full rasterization)
- [x] Registered via `[ModuleInitializer]` (same pattern as GDI/DirectWrite)
- [x] Generates valid BMFont output for ASCII + extended Unicode
- [x] No native dependencies — runs on any .NET platform
- [x] Glyph metrics are within acceptable tolerance of FreeType output
- [x] SDF rendering works via `stbtt_GetCodepointSDF()` / `stbtt_GetGlyphSDF()`
- [x] All existing non-FreeType-specific tests pass

## References

- [StbTrueTypeSharp on NuGet](https://www.nuget.org/packages/StbTrueTypeSharp) — version 1.26.12, Public Domain
- [StbTrueTypeSharp on GitHub](https://github.com/StbSharp/StbTrueTypeSharp) — Public Domain C# port
- [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) — Production validation of StbTrueTypeSharp
- [FontStashSharp.Rasterizers.StbTrueTypeSharp](https://github.com/FontStashSharp/FontStashSharp) — Reference implementation for C# API usage patterns
- [stb_truetype.h](https://github.com/nothings/stb/blob/master/stb_truetype.h) — Original C implementation (~5,000 lines)
- [StbTrueTypeSharp SDF support](https://github.com/StbSharp/StbTrueTypeSharp/issues/1) — SDF API discussion
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39) — Feature request
- [Phase 78E — Plugin Template](done/phase-78e-plugin-template.md) — Plugin pattern to follow
