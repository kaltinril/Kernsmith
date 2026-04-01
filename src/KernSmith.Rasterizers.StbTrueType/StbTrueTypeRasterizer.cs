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
    public void LoadSystemFont(string familyName) =>
        throw new NotSupportedException(
            "StbTrueType rasterizer does not support loading system fonts by name. Use LoadFont with font bytes instead.");

    /// <inheritdoc />
    public unsafe RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        if (options.Bold)
            throw new NotSupportedException("StbTrueType rasterizer does not support bold rendering.");
        if (options.Italic)
            throw new NotSupportedException("StbTrueType rasterizer does not support italic rendering.");

        int aa = Math.Max(1, options.SuperSample);
        float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
        float scale = Stb.stbtt_ScaleForMappingEmToPixels(_fontInfo!, effectiveSize);

        int glyphIndex = Stb.stbtt_FindGlyphIndex(_fontInfo!, codepoint);
        if (glyphIndex == 0)
            return null;

        int advance, lsb;
        Stb.stbtt_GetCodepointHMetrics(_fontInfo!, codepoint, &advance, &lsb);

        if (options.Sdf)
            return RasterizeSdfGlyph(codepoint, glyphIndex, scale, advance, lsb, aa);

        // Get bitmap box for bearing metrics.
        int ix0, iy0, ix1, iy1;
        Stb.stbtt_GetCodepointBitmapBox(_fontInfo!, codepoint, scale, scale, &ix0, &iy0, &ix1, &iy1);

        int bearingX = ix0 / aa;
        int bearingY = -iy0 / aa;
        int scaledAdvance = (int)Math.Round(advance * scale) / aa;

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

        int aa = Math.Max(1, options.SuperSample);
        float effectiveSize = options.Size * options.Dpi / 72.0f * aa;
        float scale = Stb.stbtt_ScaleForMappingEmToPixels(_fontInfo!, effectiveSize);

        int glyphIndex = Stb.stbtt_FindGlyphIndex(_fontInfo!, codepoint);
        if (glyphIndex == 0)
            return null;

        int advance, lsb;
        Stb.stbtt_GetCodepointHMetrics(_fontInfo!, codepoint, &advance, &lsb);

        int ix0, iy0, ix1, iy1;
        Stb.stbtt_GetCodepointBitmapBox(_fontInfo!, codepoint, scale, scale, &ix0, &iy0, &ix1, &iy1);

        return new GlyphMetrics(
            BearingX: ix0 / aa,
            BearingY: -iy0 / aa,
            Advance: (int)Math.Round(advance * scale) / aa,
            Width: (ix1 - ix0) / aa,
            Height: (iy1 - iy0) / aa);
    }

    /// <inheritdoc />
    public unsafe RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        int aa = Math.Max(1, options.SuperSample);
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
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options) => null;

    /// <summary>
    /// Not supported. StbTrueType does not support variable fonts.
    /// </summary>
    public void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes) =>
        throw new NotSupportedException("StbTrueType rasterizer does not support variable fonts.");

    /// <summary>
    /// Not supported. StbTrueType does not support color fonts.
    /// </summary>
    public void SelectColorPalette(int paletteIndex) =>
        throw new NotSupportedException("StbTrueType rasterizer does not support color fonts.");

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
                    BearingX: (int)Math.Round(lsb * scale) / aa,
                    BearingY: 0,
                    Advance: (int)Math.Round(advance * scale) / aa,
                    Width: 0,
                    Height: 0),
                Format = PixelFormat.Grayscale8
            };
        }

        try
        {
            var bitmapData = new byte[width * height];
            new ReadOnlySpan<byte>(bitmap, width * height).CopyTo(bitmapData);

            int bearingX = xoff / aa;
            int bearingY = -yoff / aa;
            int scaledAdvance = (int)Math.Round(advance * scale) / aa;

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

    private static byte[] DownscaleBitmap(byte[] source, ref int width, ref int height, int aa)
    {
        int newWidth = width / aa;
        int newHeight = height / aa;
        var downscaled = new byte[newWidth * newHeight];
        int aaSq = aa * aa;

        for (int dy = 0; dy < newHeight; dy++)
        {
            for (int dx = 0; dx < newWidth; dx++)
            {
                int sum = 0;
                for (int sy = 0; sy < aa; sy++)
                    for (int sx = 0; sx < aa; sx++)
                        sum += source[(dy * aa + sy) * width + (dx * aa + sx)];
                downscaled[dy * newWidth + dx] = (byte)(sum / aaSq);
            }
        }

        width = newWidth;
        height = newHeight;
        return downscaled;
    }
}
