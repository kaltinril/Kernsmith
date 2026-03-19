# bmfontier -- Rasterization

> Part of the [Master Plan](master-plan.md).
> Related: [Font Parsing](plan-font-parsing.md), [API Design](plan-api-design.md), [Texture Packing](plan-texture-packing.md)

---

## IRasterizer Interface

Rasterization is abstracted behind `IRasterizer` so the engine can be swapped. The default implementation uses FreeTypeSharp, but the interface allows replacing it with SkiaSharp, a custom software rasterizer, or anything else.

```csharp
public interface IRasterizer : IDisposable
{
    /// Rasterize a single glyph at the configured size/options.
    RasterizedGlyph RasterizeGlyph(int codepoint, RasterOptions options);

    /// Rasterize all requested glyphs. Implementations may batch for performance.
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);
}

public class RasterizedGlyph
{
    public int Codepoint { get; }
    public int GlyphIndex { get; }
    public byte[] BitmapData { get; }       // Raw pixel data (8-bit grayscale or 32-bit RGBA)
    public int Width { get; }
    public int Height { get; }
    public int Pitch { get; }               // Bytes per row (may include padding)
    public GlyphMetrics Metrics { get; }    // Bearing, advance, etc.
}
```

---

## Rasterization Pipeline

Rasterization is not a single step -- it is a pipeline:

```
Codepoint
  |
  v
IRasterizer.RasterizeGlyph()          -- produces raw bitmap
  |
  v
IGlyphPostProcessor[0].Process()      -- e.g., outline generation
  |
  v
IGlyphPostProcessor[1].Process()      -- e.g., SDF conversion
  |
  v
IGlyphPostProcessor[N].Process()      -- any additional effects
  |
  v
Final RasterizedGlyph                 -- ready for atlas packing
```

Post-processors are optional. If none are configured, the raw rasterized bitmap goes directly to the packer.

---

## IGlyphPostProcessor Interface

```csharp
/// Optional post-processing step applied after rasterization.
/// Post-processors form a pipeline: each receives the output of the previous step.
public interface IGlyphPostProcessor
{
    RasterizedGlyph Process(RasterizedGlyph glyph);
}
```

Example implementations:

- **`OutlinePostProcessor`** -- generates an outline around glyphs using FreeType's `FT_Stroker_*` API or custom dilation.
- **`SdfPostProcessor`** -- converts a binary/grayscale glyph bitmap to a signed distance field.
- **`BlurPostProcessor`** -- applies a Gaussian blur for glow/shadow effects.
- **`PaddingPostProcessor`** -- adds extra padding around glyphs for effects that expand the bitmap.

Users can chain multiple post-processors:

```csharp
var options = new FontGeneratorOptions
{
    Size = 48,
    PostProcessors = new IGlyphPostProcessor[]
    {
        new OutlinePostProcessor(width: 2),
        new SdfPostProcessor(spread: 8),
    },
};
```

---

## FreeTypeSharp Rasterization Details

The default `FreeTypeRasterizer` implementation uses FreeTypeSharp:

### API Call Sequence

1. `FT_New_Memory_Face` -- load font from byte array (or `FT_New_Face` from file path).
2. `FT_Set_Char_Size` -- set the requested size and DPI.
3. For each codepoint:
   - `FT_Get_Char_Index` -- map codepoint to glyph index.
   - `FT_Load_Glyph` -- load glyph data.
   - Optionally `FT_GlyphSlot_Embolden` / `FT_GlyphSlot_Oblique` -- synthetic bold/italic.
   - `FT_Render_Glyph` with the configured render mode.
   - Read bitmap from `FT_GlyphSlot_.bitmap` -- copy buffer, rows, width, pitch.
   - Read metrics from `FT_Glyph_Metrics_` -- width, height, horiBearingX/Y, horiAdvance (26.6 fixed point, divide by 64).

### Render Modes

| Mode | `FT_Render_Mode` | Output |
|------|-------------------|--------|
| No anti-aliasing | `FT_RENDER_MODE_MONO` | 1-bit per pixel |
| Grayscale AA | `FT_RENDER_MODE_NORMAL` | 8-bit grayscale |
| Light AA | `FT_RENDER_MODE_LIGHT` | 8-bit grayscale (lighter hinting) |
| LCD subpixel | `FT_RENDER_MODE_LCD` | 3x width, RGB subpixels |
| SDF | `FT_RENDER_MODE_SDF` | 8-bit distance field (FreeType 2.13+) |

### RasterOptions

```csharp
public class RasterOptions
{
    public int Size { get; set; } = 32;
    public int Dpi { get; set; } = 72;
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;
    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public bool Sdf { get; set; } = false;
    public int FaceIndex { get; set; } = 0;
}
```

---

## Memory Management and IDisposable

FreeTypeSharp allocates native resources (FreeType library handle, face handles). The rasterizer must implement `IDisposable` to clean these up.

```csharp
public class FreeTypeRasterizer : IRasterizer
{
    private readonly FTLibrary _library;
    private FTFace _face;
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            _face.Dispose();
            _library.Dispose();
            _disposed = true;
        }
    }
}
```

In `BmFont.Generate()`, the rasterizer is wrapped in a `using` block:

```csharp
using var rasterizer = options.Rasterizer ?? new FreeTypeRasterizer();
```

**Important**: FreeTypeSharp's `FreeTypeFaceFacade` has a potential memory leak (noted in the [FreeTypeSharp Evaluation](freetypesharp-evaluation.md)). We manage the FreeType library and face lifecycle ourselves rather than relying on the facade.
