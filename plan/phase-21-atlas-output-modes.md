# Phase 21 — Atlas Output Modes

> **Status**: Complete
> **Created**: 2026-03-21
> **Goal**: Add combined batch atlas output, render-to-existing-PNG, and atlas size query/constraint API for richer atlas control.

---

## Problem / Motivation

The current atlas system handles single-font generation well and supports multi-page overflow, autofit sizing, and multiple encodings. However, three common game-engine workflows are not yet supported:

1. **Combined batch textures** — `GenerateBatch()` produces separate atlas PNGs per font. Game engines often want a single shared texture containing multiple font sizes or families to reduce draw calls and texture binds.

2. **Compositing into existing textures** — Engines that manage their own master sprite sheet want to render a font atlas into a specific region of an existing PNG, not into a standalone file.

3. **Pre-generation size queries** — Users need to know how large an atlas will be before committing to generation, and want explicit control over dimension constraints (square, power-of-2, fixed-width) beyond what the current estimator exposes.

---

## Feature 1: Combined Batch Atlas

### Current Behavior

`GenerateBatch()` returns a `BmFontResult[]` where each result has its own atlas page(s). Each font is packed independently.

### Proposed API

```csharp
// New option on FontGeneratorOptions (or a new BatchOptions type)
public enum BatchAtlasMode
{
    /// <summary>Each font gets its own atlas texture(s). Current behavior.</summary>
    Separate,

    /// <summary>All fonts share the same atlas texture(s).</summary>
    Combined
}

// On a new BatchGeneratorOptions or as an overload parameter
public class BatchGeneratorOptions
{
    /// <summary>Per-font options. One entry per font to generate.</summary>
    public IReadOnlyList<FontGeneratorOptions> Fonts { get; set; }

    /// <summary>
    /// Whether fonts get separate or combined atlas textures.
    /// Default: Separate (preserves current behavior).
    /// </summary>
    public BatchAtlasMode AtlasMode { get; set; } = BatchAtlasMode.Separate;

    /// <summary>
    /// Maximum texture dimensions for the combined atlas.
    /// Only used when AtlasMode is Combined.
    /// </summary>
    public int MaxTextureWidth { get; set; } = 4096;
    public int MaxTextureHeight { get; set; } = 4096;
}

// New overload on BmFont
public static BmFontResult[] GenerateBatch(
    IReadOnlyList<byte[]> fontDataList,
    BatchGeneratorOptions options);

// Usage
var batchOptions = new BatchGeneratorOptions
{
    Fonts = new[]
    {
        new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii },
        new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii },
    },
    AtlasMode = BatchAtlasMode.Combined,
    MaxTextureWidth = 2048,
    MaxTextureHeight = 2048
};

BmFontResult[] results = BmFont.GenerateBatch(
    new[] { robotoBytes, robotoBytes }, batchOptions);

// Both results reference the same shared page texture(s)
// results[0].GetPngData(0) == results[1].GetPngData(0)  (same bytes)
```

### Technical Approach

1. **Rasterize all fonts independently** — each font still goes through FreeType rasterization separately, producing its own glyph bitmaps and metrics.
2. **Merge glyph lists before packing** — collect all rasterized glyphs from all fonts into a single list, tagged with their source font index.
3. **Pack into shared atlas pages** — run the atlas packer once across all glyphs. Each glyph records which page it lands on.
4. **Build per-font BmFontResult** — each result references the shared page texture(s). The `.fnt` character entries point to the correct (x, y) in the shared atlas. Page filenames are shared.
5. **SharedAtlasPages** — introduce an internal `SharedAtlasPages` object that multiple `BmFontResult` instances can reference, avoiding duplicated PNG byte arrays.

Constraints:
- All fonts in a combined batch must use the same texture format (PNG/TGA/DDS). Validate at entry.
- Padding and spacing should be consistent. Use the maximum padding/spacing across all font options.
- If glyphs overflow the max texture size, overflow pages are shared too.

---

## Feature 2: Render to Existing PNG at Target Rectangle

### Current Behavior

Atlas output always produces new standalone PNG files. There is no way to composite into an existing image.

### Proposed API

```csharp
/// <summary>
/// Defines a rectangular region within an existing texture where the atlas should be placed.
/// </summary>
public class AtlasTargetRegion
{
    /// <summary>Path to the existing PNG file to composite into.</summary>
    public string SourcePngPath { get; set; }

    /// <summary>Top-left X coordinate of the target rectangle.</summary>
    public int X { get; set; }

    /// <summary>Top-left Y coordinate of the target rectangle.</summary>
    public int Y { get; set; }

    /// <summary>Width of the target rectangle. Glyphs are packed within this width.</summary>
    public int Width { get; set; }

    /// <summary>Height of the target rectangle. Glyphs are packed within this height.</summary>
    public int Height { get; set; }
}

// New option on FontGeneratorOptions
public class FontGeneratorOptions
{
    // ... existing properties ...

    /// <summary>
    /// When set, the atlas is rendered into the specified region of an existing PNG
    /// instead of creating a new standalone texture. The modified PNG is included
    /// in the result. Default: null (standalone atlas).
    /// </summary>
    public AtlasTargetRegion? TargetRegion { get; set; }
}

// Usage
var options = new FontGeneratorOptions
{
    Size = 24,
    Characters = CharacterSet.Ascii,
    TargetRegion = new AtlasTargetRegion
    {
        SourcePngPath = "spritesheet.png",
        X = 512,
        Y = 0,
        Width = 512,
        Height = 512
    }
};

var result = BmFont.Generate(fontBytes, options);

// result.GetPngData(0) contains the full modified spritesheet
// Character positions in the .fnt are offset by (512, 0)
```

### Technical Approach

1. **Load existing PNG** — read the source PNG into a pixel buffer. Need a PNG decoder (StbImageSharp or similar; evaluate dependency cost).
2. **Pack within constrained bounds** — set the packer's available area to (Width, Height) from the target region. Glyphs that do not fit trigger overflow to a new standalone page (not composited).
3. **Composite onto source** — blit the packed atlas pixels onto the source image at (X, Y). Use simple overwrite (not alpha blending) for the glyph region.
4. **Offset .fnt coordinates** — all character X/Y positions in the BMFont output are offset by (TargetRegion.X, TargetRegion.Y) so they reference the correct position in the full texture.
5. **Page metadata** — the page entry in the .fnt file references the original PNG filename, not a new one.
6. **In-memory variant** — also support `byte[] SourcePngData` instead of a file path, for fully in-memory workflows.

Constraints:
- Only PNG is supported for the source image (TGA/DDS sources are not planned).
- If the target rectangle is larger than the source image, throw an `ArgumentException`.
- The source image must be RGBA. If it is RGB, convert to RGBA before compositing.

---

## Feature 3: Atlas Size Query and Constraints

### Current Behavior

Phase 8 added `AtlasSizeEstimator` which predicts atlas dimensions internally. The estimator supports power-of-2 rounding and non-square optimization, but these are not exposed as user-facing options. Users cannot query the required size without running the full generation pipeline.

### Proposed API

```csharp
/// <summary>
/// Controls how atlas dimensions are constrained.
/// </summary>
public class AtlasSizeConstraints
{
    /// <summary>
    /// Force the atlas to be square (width = height = max of both).
    /// Default: false.
    /// </summary>
    public bool ForceSquare { get; set; }

    /// <summary>
    /// Round both dimensions up to the nearest power of 2.
    /// Default: false (current behavior uses exact fit).
    /// </summary>
    public bool ForcePowerOfTwo { get; set; }

    /// <summary>
    /// Fix the atlas width to this value. Height is calculated to fit all glyphs.
    /// When set, ForceSquare is ignored. 0 means no constraint.
    /// Default: 0.
    /// </summary>
    public int FixedWidth { get; set; }
}

// On FontGeneratorOptions
public class FontGeneratorOptions
{
    // ... existing properties ...

    /// <summary>
    /// Atlas dimension constraints. Default: null (no constraints, current behavior).
    /// </summary>
    public AtlasSizeConstraints? SizeConstraints { get; set; }
}

/// <summary>
/// Result of an atlas size query.
/// </summary>
public class AtlasSizeInfo
{
    /// <summary>Estimated width in pixels.</summary>
    public int Width { get; }

    /// <summary>Estimated height in pixels.</summary>
    public int Height { get; }

    /// <summary>Number of atlas pages required.</summary>
    public int PageCount { get; }

    /// <summary>Total number of glyphs that will be rendered.</summary>
    public int GlyphCount { get; }

    /// <summary>
    /// Estimated packing efficiency (0.0 to 1.0).
    /// Ratio of glyph area to total atlas area.
    /// </summary>
    public double EstimatedEfficiency { get; }
}

// New static method on BmFont
public static AtlasSizeInfo QueryAtlasSize(byte[] fontData, FontGeneratorOptions options);

// Usage — query before generating
var options = new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii,
    SizeConstraints = new AtlasSizeConstraints
    {
        ForcePowerOfTwo = true,
        ForceSquare = true
    }
};

AtlasSizeInfo info = BmFont.QueryAtlasSize(robotoBytes, options);
Console.WriteLine($"Atlas: {info.Width}x{info.Height}, {info.PageCount} page(s)");
// Output: Atlas: 512x512, 1 page(s)

// Then generate with the same options
var result = BmFont.Generate(robotoBytes, options);
```

### Technical Approach

1. **AtlasSizeConstraints processing** — apply constraints after the estimator calculates raw dimensions:
   - `ForcePowerOfTwo`: round width and height up to next power of 2.
   - `ForceSquare`: set both dimensions to `Math.Max(width, height)`. If combined with power-of-2, apply power-of-2 first, then square.
   - `FixedWidth`: set width to the fixed value, then estimate height by dividing total glyph area by the fixed width (with overhead factor). Round height up to power-of-2 if `ForcePowerOfTwo` is set.

2. **QueryAtlasSize implementation** — runs the lightweight path:
   - Load font via FreeType (needed for glyph metrics).
   - Resolve character set to glyph list.
   - Get per-glyph metrics (advance, bearing, bbox) without rasterizing.
   - Run `AtlasSizeEstimator` with constraints applied.
   - Return `AtlasSizeInfo` without producing any bitmaps.

3. **Thread constraints through packer** — `AtlasBuilder` and the packer implementations receive the final constrained dimensions. The packer already accepts width/height; constraints just change what values are passed in.

4. **Backward compatibility** — `SizeConstraints = null` preserves all current behavior. Existing `MaxTextureWidth`/`MaxTextureHeight` options continue to work and act as upper bounds even when constraints are applied.

---

## Files That Would Change

| File | Change |
|------|--------|
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Add `TargetRegion` and `SizeConstraints` properties |
| `src/KernSmith/Config/BatchGeneratorOptions.cs` | New file — batch options with `AtlasMode` |
| `src/KernSmith/Config/BatchAtlasMode.cs` | New file — enum for Separate/Combined |
| `src/KernSmith/Config/AtlasTargetRegion.cs` | New file — target region for render-to-existing |
| `src/KernSmith/Config/AtlasSizeConstraints.cs` | New file — size constraint options |
| `src/KernSmith/Atlas/AtlasSizeEstimator.cs` | Apply size constraints after estimation |
| `src/KernSmith/Atlas/AtlasBuilder.cs` | Support compositing onto existing image; handle constrained dimensions |
| `src/KernSmith/Output/BmFontResult.cs` | Support shared atlas pages across batch results |
| `src/KernSmith/Output/AtlasSizeInfo.cs` | New file — query result type |
| `src/KernSmith/BmFont.cs` | Add `QueryAtlasSize()`, new `GenerateBatch()` overload, target region threading |
| `src/KernSmith/Output/FileWriter.cs` | Handle shared page filenames in combined mode |
| `src/KernSmith/Output/BmFontModelBuilder.cs` | Offset character positions for target region |
| `tests/KernSmith.Tests/` | Tests for all three features |

---

## Considerations

- **Backward compatibility** — all new options default to null/off. Existing code is unaffected.
- **Memory** — combined batch mode holds all glyph bitmaps for all fonts in memory simultaneously during packing. For large character sets across many fonts, this could be significant. Document the trade-off.
- **PNG decoding dependency** — render-to-existing-PNG requires reading PNG files, which the library currently does not do (it only writes PNGs via StbImageWriteSharp). Evaluate StbImageSharp (MIT, same family) or a minimal decoder. Avoid heavy dependencies.
- **Texture format constraints** — combined batch requires all fonts to use the same format. Target region only supports PNG sources. These limitations should be validated early with clear error messages.
- **Channel packing interaction** — combined batch with channel packing needs careful handling. If each font uses a different channel, combined packing must respect channel assignments per glyph.
- **Overflow behavior** — in target region mode, if glyphs do not fit the rectangle, the library should throw `AtlasPackingException` rather than silently overflowing to a second page (which would not be composited into the source image).
- **Thread safety** — `QueryAtlasSize` should be safe to call concurrently since it is read-only and produces no side effects.
- **Estimation accuracy** — `QueryAtlasSize` uses the estimator, not actual packing, so the reported size is an estimate. The actual generation may produce slightly different dimensions. Document this clearly.
