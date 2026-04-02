using System.Runtime.InteropServices;
using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using Stb = StbTrueTypeSharp.StbTrueType;

namespace KernSmith.Rasterizers.StbTrueType;

/// <summary>
/// Pure C# rasterizer backend using stb_truetype. Cross-platform, no native dependencies.
/// Ideal for Blazor WASM, iOS AOT, and serverless scenarios.
/// </summary>
public sealed class StbTrueTypeRasterizer : IRasterizer
{
    private static readonly IRasterizerCapabilities StbCapabilitiesInstance = new StbTrueTypeCapabilities();

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => StbCapabilitiesInstance;

    private Stb.stbtt_fontinfo? _fontInfo;
    private byte[]? _fontData;
    private GCHandle _pinnedFontData;
    private bool _disposed;

    /// <inheritdoc />
    public unsafe void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_fontInfo is not null)
            throw new InvalidOperationException("Font already loaded. Create a new StbTrueTypeRasterizer instance.");

        _fontData = fontData.ToArray();
        _pinnedFontData = GCHandle.Alloc(_fontData, GCHandleType.Pinned);

        try
        {
            var dataPtr = (byte*)_pinnedFontData.AddrOfPinnedObject();
            var offset = Stb.stbtt_GetFontOffsetForIndex(dataPtr, faceIndex);
            if (offset < 0)
                throw new FontParsingException($"Failed to find font at face index {faceIndex}.");

            _fontInfo = new Stb.stbtt_fontinfo();
            if (Stb.stbtt_InitFont(_fontInfo, dataPtr, offset) == 0)
                throw new FontParsingException("Failed to initialize font with stb_truetype.");
        }
        catch
        {
            _fontInfo = null;
            if (_pinnedFontData.IsAllocated)
                _pinnedFontData.Free();
            _fontData = null;
            throw;
        }
    }

    /// <summary>
    /// Not supported. StbTrueType cannot load system fonts by name.
    /// </summary>
    public void LoadSystemFont(string familyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException(
            "StbTrueType rasterizer does not support loading system fonts by name. Use LoadFont with font bytes instead.");
    }

    /// <inheritdoc />
    public unsafe RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        if (options.ColorFont)
            throw new NotSupportedException("StbTrueType rasterizer does not support color font rendering.");

        // SDF is resolution-independent; supersampling is meaningless for distance fields.
        int aa = options.Sdf ? 1 : Math.Max(1, options.SuperSample);
        float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
        float scale = Stb.stbtt_ScaleForMappingEmToPixels(_fontInfo!, effectiveSize);

        int glyphIndex = Stb.stbtt_FindGlyphIndex(_fontInfo!, codepoint);
        if (glyphIndex == 0)
            return null;

        int advance, lsb;
        Stb.stbtt_GetCodepointHMetrics(_fontInfo!, codepoint, &advance, &lsb);

        if (options.Sdf)
        {
            if (options.Bold || options.Italic)
                return RasterizeStyledSdfGlyph(codepoint, glyphIndex, scale, advance, lsb, aa, options);
            return RasterizeSdfGlyph(codepoint, glyphIndex, scale, advance, lsb, aa);
        }

        if (options.Bold || options.Italic)
            return RasterizeStyledGlyph(codepoint, glyphIndex, scale, advance, lsb, aa, options);

        // Get bitmap box for bearing metrics.
        int ix0, iy0, ix1, iy1;
        Stb.stbtt_GetCodepointBitmapBox(_fontInfo!, codepoint, scale, scale, &ix0, &iy0, &ix1, &iy1);

        int bearingX = (int)Math.Round((double)ix0 / aa);
        int bearingY = (int)Math.Round((double)-iy0 / aa);
        int scaledAdvance = (int)Math.Round(advance * scale / aa);

        // Rasterize the glyph bitmap.
        int width, height, xoff, yoff;
        byte* bitmap = Stb.stbtt_GetCodepointBitmap(
            _fontInfo!, scale, scale, codepoint, &width, &height, &xoff, &yoff);

        if (bitmap == null || width == 0 || height == 0)
        {
            if (bitmap != null)
                Stb.stbtt_FreeBitmap(bitmap, null);

            // Whitespace glyph (e.g., space): valid advance, zero-size bitmap.
            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = [],
                Width = 0,
                Height = 0,
                Pitch = 0,
                Metrics = new GlyphMetrics(
                    BearingX: bearingX,
                    BearingY: bearingY,
                    Advance: scaledAdvance,
                    Width: 0,
                    Height: 0),
                Format = PixelFormat.Grayscale8
            };
        }

        try
        {
            var bitmapData = new byte[width * height];
            new ReadOnlySpan<byte>(bitmap, width * height).CopyTo(bitmapData);

            // Anti-alias mode None: threshold at 128.
            if (options.AntiAlias == AntiAliasMode.None)
            {
                for (int i = 0; i < bitmapData.Length; i++)
                    bitmapData[i] = bitmapData[i] >= 128 ? (byte)255 : (byte)0;
            }

            // Downscale bitmap by averaging aa x aa blocks when supersampling.
            if (aa > 1)
                bitmapData = DownscaleBitmap(bitmapData, ref width, ref height, aa);

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = bitmapData,
                Width = width,
                Height = height,
                Pitch = width,
                Metrics = new GlyphMetrics(
                    BearingX: bearingX,
                    BearingY: bearingY,
                    Advance: scaledAdvance,
                    Width: width,
                    Height: height),
                Format = PixelFormat.Grayscale8
            };
        }
        finally
        {
            Stb.stbtt_FreeBitmap(bitmap, null);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        var results = new List<RasterizedGlyph>();
        foreach (var cp in codepoints)
        {
            var glyph = RasterizeGlyph(cp, options);
            if (glyph is not null)
                results.Add(glyph);
        }

        return results;
    }

    /// <inheritdoc />
    public unsafe GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        // SDF is resolution-independent; supersampling is meaningless for distance fields.
        int aa = options.Sdf ? 1 : Math.Max(1, options.SuperSample);
        float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
        float scale = Stb.stbtt_ScaleForMappingEmToPixels(_fontInfo!, effectiveSize);

        int glyphIndex = Stb.stbtt_FindGlyphIndex(_fontInfo!, codepoint);
        if (glyphIndex == 0)
            return null;

        int advance, lsb;
        Stb.stbtt_GetCodepointHMetrics(_fontInfo!, codepoint, &advance, &lsb);

        if (options.Bold || options.Italic)
            return GetStyledGlyphMetrics(codepoint, glyphIndex, scale, advance, aa, options);

        int ix0, iy0, ix1, iy1;
        Stb.stbtt_GetCodepointBitmapBox(_fontInfo!, codepoint, scale, scale, &ix0, &iy0, &ix1, &iy1);

        return new GlyphMetrics(
            BearingX: (int)Math.Round((double)ix0 / aa),
            BearingY: (int)Math.Round((double)-iy0 / aa),
            Advance: (int)Math.Round(advance * scale / aa),
            Width: (int)Math.Round((double)(ix1 - ix0) / aa),
            Height: (int)Math.Round((double)(iy1 - iy0) / aa));
    }

    private unsafe GlyphMetrics? GetStyledGlyphMetrics(
        int codepoint, int glyphIndex, float scale, int advance, int aa, RasterOptions options)
    {
        Stb.stbtt_vertex* vertices;
        int numVerts = Stb.stbtt_GetCodepointShape(_fontInfo!, codepoint, &vertices);
        if (numVerts == 0)
        {
            int scaledAdvance = (int)Math.Round(advance * scale / aa);
            return new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: scaledAdvance, Width: 0, Height: 0);
        }

        try
        {
            float effectiveSize = options.Size * options.Dpi / 72.0f * aa;

            if (options.Bold)
            {
                float strengthPixels = effectiveSize / 24.0f;
                float strengthFontUnits = strengthPixels / scale;
                OutlineTransforms.ApplyEmbolden(vertices, numVerts, strengthFontUnits);
            }

            if (options.Italic)
                OutlineTransforms.ApplyItalicShear(vertices, numVerts);

            var (x0, y0, x1, y1) = OutlineTransforms.ComputeBoundingBox(vertices, numVerts, scale, scale);
            int width = Math.Max(0, x1 - x0);
            int height = Math.Max(0, y1 - y0);

            int scaledAdv = (int)Math.Round(advance * scale / aa);
            if (options.Bold)
            {
                float strengthPixels = effectiveSize / 24.0f;
                scaledAdv += (int)Math.Round(strengthPixels / aa);
            }

            return new GlyphMetrics(
                BearingX: (int)Math.Round((double)x0 / aa),
                BearingY: (int)Math.Round((double)-y0 / aa),
                Advance: scaledAdv,
                Width: (int)Math.Round((double)width / aa),
                Height: (int)Math.Round((double)height / aa));
        }
        finally
        {
            Stb.stbtt_FreeShape(_fontInfo!, vertices);
        }
    }

    /// <inheritdoc />
    public unsafe RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        int aa = options.Sdf ? 1 : Math.Max(1, options.SuperSample);
        float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
        float scale = Stb.stbtt_ScaleForMappingEmToPixels(_fontInfo!, effectiveSize);

        int ascent, descent, lineGap;
        Stb.stbtt_GetFontVMetrics(_fontInfo!, &ascent, &descent, &lineGap);

        return new RasterizerFontMetrics
        {
            Ascent = (int)Math.Ceiling(ascent * scale / aa),
            Descent = (int)Math.Ceiling(Math.Abs(descent) * scale / aa),
            LineHeight = (int)Math.Ceiling((ascent - descent + lineGap) * scale / aa)
        };
    }

    /// <summary>
    /// Returns null to let the shared GPOS/kern parser handle kerning scaling.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return null;
    }

    /// <summary>
    /// Not supported. StbTrueType does not support variable fonts.
    /// </summary>
    public void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException("StbTrueType rasterizer does not support variable fonts.");
    }

    /// <summary>
    /// Not supported. StbTrueType does not support color fonts.
    /// </summary>
    public void SelectColorPalette(int paletteIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException("StbTrueType rasterizer does not support color fonts.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_fontInfo is not null)
        {
            _fontInfo.Dispose();
            _fontInfo = null;
        }

        if (_pinnedFontData.IsAllocated)
            _pinnedFontData.Free();

        _fontData = null;
    }

    // ── Private helpers ─────────────────────────────────────────────

    private void EnsureFontLoaded()
    {
        if (_fontInfo is null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");
    }

    private unsafe RasterizedGlyph? RasterizeSdfGlyph(
        int codepoint, int glyphIndex, float scale, int advance, int lsb, int aa)
    {
        int width, height, xoff, yoff;
        byte* bitmap = Stb.stbtt_GetCodepointSDF(
            _fontInfo!, scale, codepoint, 5, 128, 64.0f,
            &width, &height, &xoff, &yoff);

        if (bitmap == null || width == 0 || height == 0)
        {
            if (bitmap != null)
                Stb.stbtt_FreeSDF(bitmap, null);

            // Whitespace glyph with SDF.
            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = [],
                Width = 0,
                Height = 0,
                Pitch = 0,
                Metrics = new GlyphMetrics(
                    BearingX: (int)Math.Round(lsb * scale / aa),
                    BearingY: 0,
                    Advance: (int)Math.Round(advance * scale / aa),
                    Width: 0,
                    Height: 0),
                Format = PixelFormat.Grayscale8
            };
        }

        try
        {
            var bitmapData = new byte[width * height];
            new ReadOnlySpan<byte>(bitmap, width * height).CopyTo(bitmapData);

            int bearingX = (int)Math.Round((double)xoff / aa);
            int bearingY = (int)Math.Round((double)-yoff / aa);
            int scaledAdvance = (int)Math.Round(advance * scale / aa);

            // Downscale bitmap by averaging aa x aa blocks when supersampling.
            if (aa > 1)
                bitmapData = DownscaleBitmap(bitmapData, ref width, ref height, aa);

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = bitmapData,
                Width = width,
                Height = height,
                Pitch = width,
                Metrics = new GlyphMetrics(
                    BearingX: bearingX,
                    BearingY: bearingY,
                    Advance: scaledAdvance,
                    Width: width,
                    Height: height),
                Format = PixelFormat.Grayscale8
            };
        }
        finally
        {
            Stb.stbtt_FreeSDF(bitmap, null);
        }
    }

    private unsafe RasterizedGlyph? RasterizeStyledSdfGlyph(
        int codepoint, int glyphIndex, float scale, int advance, int lsb, int aa,
        RasterOptions options)
    {
        Stb.stbtt_vertex* vertices;
        int numVerts = Stb.stbtt_GetCodepointShape(_fontInfo!, codepoint, &vertices);
        if (numVerts == 0)
        {
            // Whitespace glyph: valid advance, zero-size bitmap.
            int scaledAdvance = (int)Math.Round(advance * scale / aa);
            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = [],
                Width = 0,
                Height = 0,
                Pitch = 0,
                Metrics = new GlyphMetrics(
                    BearingX: 0,
                    BearingY: 0,
                    Advance: scaledAdvance,
                    Width: 0,
                    Height: 0),
                Format = PixelFormat.Grayscale8
            };
        }

        try
        {
            float effectiveSize = options.Size * options.Dpi / 72.0f * aa;

            if (options.Bold)
            {
                float strengthPixels = effectiveSize / 24.0f;
                float strengthFontUnits = strengthPixels / scale;
                OutlineTransforms.ApplyEmbolden(vertices, numVerts, strengthFontUnits);
            }

            if (options.Italic)
                OutlineTransforms.ApplyItalicShear(vertices, numVerts);

            const int padding = 5;
            const byte onEdgeValue = 128;
            const float pixelDistScale = 64.0f;

            byte[]? bitmapData = StbTrueTypeSdfVendored.GetGlyphSdfFromVertices(
                vertices, numVerts, scale, padding, onEdgeValue, pixelDistScale,
                out int width, out int height, out int xoff, out int yoff);

            if (bitmapData == null || width == 0 || height == 0)
            {
                // Empty glyph after SDF rendering.
                int scaledAdvance = (int)Math.Round(advance * scale / aa);
                return new RasterizedGlyph
                {
                    Codepoint = codepoint,
                    GlyphIndex = glyphIndex,
                    BitmapData = [],
                    Width = 0,
                    Height = 0,
                    Pitch = 0,
                    Metrics = new GlyphMetrics(
                        BearingX: (int)Math.Round(lsb * scale / aa),
                        BearingY: 0,
                        Advance: scaledAdvance,
                        Width: 0,
                        Height: 0),
                    Format = PixelFormat.Grayscale8
                };
            }

            int bearingX = (int)Math.Round((double)xoff / aa);
            int bearingY = (int)Math.Round((double)-yoff / aa);
            int scaledAdv = (int)Math.Round(advance * scale / aa);

            // Bold increases advance slightly (matches FreeType behavior).
            if (options.Bold)
            {
                float strengthPixels = effectiveSize / 24.0f;
                scaledAdv += (int)Math.Round(strengthPixels / aa);
            }

            // Downscale bitmap by averaging aa x aa blocks when supersampling.
            if (aa > 1)
                bitmapData = DownscaleBitmap(bitmapData, ref width, ref height, aa);

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = bitmapData,
                Width = width,
                Height = height,
                Pitch = width,
                Metrics = new GlyphMetrics(
                    BearingX: bearingX,
                    BearingY: bearingY,
                    Advance: scaledAdv,
                    Width: width,
                    Height: height),
                Format = PixelFormat.Grayscale8
            };
        }
        finally
        {
            Stb.stbtt_FreeShape(_fontInfo!, vertices);
        }
    }

    private unsafe RasterizedGlyph? RasterizeStyledGlyph(
        int codepoint, int glyphIndex, float scale, int advance, int lsb, int aa,
        RasterOptions options)
    {
        Stb.stbtt_vertex* vertices;
        int numVerts = Stb.stbtt_GetCodepointShape(_fontInfo!, codepoint, &vertices);
        if (numVerts == 0)
        {
            // Whitespace glyph (e.g., space): valid advance, zero-size bitmap.
            int scaledAdvance = (int)Math.Round(advance * scale / aa);
            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = [],
                Width = 0,
                Height = 0,
                Pitch = 0,
                Metrics = new GlyphMetrics(
                    BearingX: 0,
                    BearingY: 0,
                    Advance: scaledAdvance,
                    Width: 0,
                    Height: 0),
                Format = PixelFormat.Grayscale8
            };
        }

        try
        {
            if (options.Bold)
            {
                // Bold strength in font units: ppem/24 converted to font units.
                // FreeType uses ppem/24 in 26.6 fixed point; we compute equivalent font units.
                float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
                float strengthPixels = effectiveSize / 24.0f;
                float strengthFontUnits = strengthPixels / scale;
                OutlineTransforms.ApplyEmbolden(vertices, numVerts, strengthFontUnits);
            }

            if (options.Italic)
            {
                OutlineTransforms.ApplyItalicShear(vertices, numVerts);
            }

            // Compute bbox from modified vertices.
            var (x0, y0, x1, y1) = OutlineTransforms.ComputeBoundingBox(vertices, numVerts, scale, scale);

            int width = x1 - x0;
            int height = y1 - y0;

            if (width <= 0 || height <= 0)
            {
                // Zero-area glyph after transforms.
                int scaledAdvance = (int)Math.Round(advance * scale / aa);
                return new RasterizedGlyph
                {
                    Codepoint = codepoint,
                    GlyphIndex = glyphIndex,
                    BitmapData = [],
                    Width = 0,
                    Height = 0,
                    Pitch = 0,
                    Metrics = new GlyphMetrics(
                        BearingX: 0,
                        BearingY: 0,
                        Advance: scaledAdvance,
                        Width: 0,
                        Height: 0),
                    Format = PixelFormat.Grayscale8
                };
            }

            int bearingX = (int)Math.Round((double)x0 / aa);
            int bearingY = (int)Math.Round((double)-y0 / aa);
            int scaledAdv = (int)Math.Round(advance * scale / aa);

            // Bold increases advance slightly (matches FreeType behavior).
            if (options.Bold)
            {
                float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
                float strengthPixels = effectiveSize / 24.0f;
                scaledAdv += (int)Math.Round(strengthPixels / aa);
            }

            // Allocate bitmap and rasterize using the low-level API.
            var pixels = new byte[width * height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var bitmap = new Stb.stbtt__bitmap();
                bitmap.w = width;
                bitmap.h = height;
                bitmap.stride = width;
                bitmap.pixels = (byte*)handle.AddrOfPinnedObject();

                Stb.stbtt_Rasterize(&bitmap, 0.35f, vertices, numVerts,
                    scale, scale, 0, 0, x0, y0, 1, null, false);

                // Anti-alias mode None: threshold at 128.
                if (options.AntiAlias == AntiAliasMode.None)
                {
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = pixels[i] >= 128 ? (byte)255 : (byte)0;
                }

                var bitmapData = pixels;

                // Downscale bitmap by averaging aa x aa blocks when supersampling.
                if (aa > 1)
                    bitmapData = DownscaleBitmap(bitmapData, ref width, ref height, aa);

                return new RasterizedGlyph
                {
                    Codepoint = codepoint,
                    GlyphIndex = glyphIndex,
                    BitmapData = bitmapData,
                    Width = width,
                    Height = height,
                    Pitch = width,
                    Metrics = new GlyphMetrics(
                        BearingX: bearingX,
                        BearingY: bearingY,
                        Advance: scaledAdv,
                        Width: width,
                        Height: height),
                    Format = PixelFormat.Grayscale8
                };
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            Stb.stbtt_FreeShape(_fontInfo!, vertices);
        }
    }

    private static byte[] DownscaleBitmap(byte[] source, ref int width, ref int height, int aa)
    {
        // Use ceiling division so remainder pixels on right/bottom edges are included.
        int newWidth = (width + aa - 1) / aa;
        int newHeight = (height + aa - 1) / aa;
        var downscaled = new byte[newWidth * newHeight];

        for (int dy = 0; dy < newHeight; dy++)
        {
            for (int dx = 0; dx < newWidth; dx++)
            {
                int sum = 0;
                int count = 0;
                for (int sy = 0; sy < aa; sy++)
                {
                    int srcY = dy * aa + sy;
                    if (srcY >= height) break;
                    for (int sx = 0; sx < aa; sx++)
                    {
                        int srcX = dx * aa + sx;
                        if (srcX >= width) break;
                        sum += source[srcY * width + srcX];
                        count++;
                    }
                }

                downscaled[dy * newWidth + dx] = count > 0 ? (byte)(sum / count) : (byte)0;
            }
        }

        width = newWidth;
        height = newHeight;
        return downscaled;
    }
}
