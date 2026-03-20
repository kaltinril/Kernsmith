# bmfontier -- Rasterization

> Part of the [Master Plan](master-plan.md).
> Related: [Font Parsing](plan-font-parsing.md), [API Design](plan-api-design.md), [Texture Packing](plan-texture-packing.md)

> **Data Types:** All types used in this document (`RasterizedGlyph`, `GlyphMetrics`, `RasterOptions`, `PixelFormat`) are defined in [plan-data-types.md](plan-data-types.md).

---

## IRasterizer Interface

Rasterization is abstracted behind `IRasterizer` so the engine can be swapped. The default implementation uses FreeTypeSharp, but the interface allows replacing it with SkiaSharp, a custom software rasterizer, or anything else.

> `IRasterizer` and `IGlyphPostProcessor` interfaces are defined in [plan-data-types.md](plan-data-types.md#interfaces).
> `RasterizedGlyph` class is defined in [plan-data-types.md](plan-data-types.md#rasterization-types).

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

> `RasterizedGlyph` instances are immutable. Post-processors create NEW instances with modified data. Use `new RasterizedGlyph(...)` with updated fields. If dimensions change (e.g., padding adds border pixels), the new instance must have updated `Width`, `Height`, `Pitch`, and `BitmapData`.

---

## IGlyphPostProcessor Interface

> `IGlyphPostProcessor` interface is defined in [plan-data-types.md](plan-data-types.md#interfaces).

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

The default `FreeTypeRasterizer` implementation uses FreeTypeSharp.

The `FreeTypeRasterizer` constructor takes no arguments. Call `LoadFont()` before rasterizing.
Internally it creates an `FT_Library` instance. `LoadFont()` pins the font data and creates an `FT_Face`.

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

> FreeType's glyph slot bitmap buffer is overwritten on each `FT_Load_Glyph` call. The rasterizer MUST copy the bitmap data to a new managed `byte[]` via `Marshal.Copy` before returning. The returned `RasterizedGlyph.BitmapData` is a managed array that can outlive the rasterizer.

> `FT_RENDER_MODE_MONO` produces 1-bit-per-pixel packed data. The `FreeTypeRasterizer` normalizes this to 8-bit grayscale (0x00 or 0xFF per pixel) before returning. All consumers can assume `PixelFormat.Grayscale8` for standard rendering modes.

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

> `RasterOptions` is a subset of `FontGeneratorOptions`, extracted by the pipeline for the rasterizer's use. The mapping is: `options.Size -> rasterOptions.Size`, `options.Dpi -> rasterOptions.Dpi`, `options.AntiAlias -> rasterOptions.AntiAlias`, etc.

---

## Memory Management and IDisposable

FreeTypeSharp allocates native resources (FreeType library handle, face handles). The rasterizer must implement `IDisposable` to clean these up.

> FreeTypeSharp types: Use `FreeTypeLibrary` for the library handle and raw `IntPtr` for face handles (obtained via `FT.FT_New_Memory_Face`). Do NOT use `FreeTypeFaceFacade`.

```csharp
public class FreeTypeRasterizer : IRasterizer
{
    private readonly FreeTypeLibrary _library;
    private IntPtr _face;              // raw FT_Face handle
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_face != IntPtr.Zero)
                FT.FT_Done_Face(_face);
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

**Important**: FreeTypeSharp's `FreeTypeFaceFacade` has a potential memory leak (noted in the [FreeTypeSharp Evaluation](../reference/REF-02-freetypesharp-evaluation.md)). We manage the FreeType library and face lifecycle ourselves rather than relying on the facade.

---

## Error Handling

- **Missing glyph**: `RasterizeGlyph` returns `null`. `RasterizeAll` skips missing glyphs.
- **FreeType error**: Throw `RasterizationException` with the FreeType error code and codepoint.
- **Font not loaded**: Throw `InvalidOperationException` if `RasterizeGlyph` is called before `LoadFont`.
- **Zero-size glyph** (e.g., space): Return a `RasterizedGlyph` with `Width=0, Height=0, BitmapData=Array.Empty<byte>()` but valid `Metrics` (advance is non-zero for space).

---

## Implementation References

- **FreeTypeSharp API surface and known issues**: See [REF-02-freetypesharp-evaluation.md](../reference/REF-02-freetypesharp-evaluation.md)
- **FreeType bitmap struct fields**: `FT_Bitmap_.buffer`, `rows`, `width`, `pitch`, `pixel_mode`
- **Glyph positioning fields**: `FT_GlyphSlotRec_.bitmap_left` (-> BearingX), `bitmap_top` (-> BearingY)
- **26.6 fixed-point conversion**: Divide by 64 and round. `(value + 32) >> 6` for rounding, `value >> 6` for truncation.
