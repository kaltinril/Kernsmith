using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Fully custom, pure C# rasterizer backend owned entirely by KernSmith. Zero external
/// dependencies. Cross-platform and WASM/AOT-friendly.
/// </summary>
/// <remarks>
/// Renders TrueType (<c>glyf</c>) outlines only: the font is parsed in-house, outlines are
/// flattened to line segments and filled with a signed-area scanline rasterizer. CFF/OTF
/// (PostScript) outlines are rejected at load time until Phase 166 adds a CFF interpreter.
///
/// Synthetic bold/italic (Phase 167), outline stroking (Phase 168), SDF (Phase 169), variable
/// fonts (Phase 171) and color fonts (Phase 172) are not implemented; the corresponding
/// <see cref="IRasterizerCapabilities"/> flags are all false. <see cref="RasterOptions.Bold"/>
/// and <see cref="RasterOptions.Italic"/> are ignored rather than rejected, so a font
/// configuration that works on other backends still generates here — just without the styling.
/// <see cref="RasterOptions.SuperSample"/> is likewise ignored: with
/// <see cref="IRasterizerCapabilities.HandlesOwnSizing"/> false the pipeline already renders
/// at the supersampled size and box-filters the result back down, exactly as it does for the
/// FreeType backend.
///
/// Instances are NOT thread-safe (Phase 160, D9). Create one instance per thread for
/// parallel rasterization.
/// </remarks>
public sealed class NativeRasterizer : IRasterizer
{
    private static readonly IRasterizerCapabilities CapabilitiesInstance = new NativeCapabilities();

    private NativeFontFace? _face;
    private bool _disposed;

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => CapabilitiesInstance;

    /// <inheritdoc />
    /// <exception cref="RasterizationException">If the font uses CFF (PostScript) outlines.</exception>
    public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_face is not null)
            throw new InvalidOperationException("Font already loaded. Create a new NativeRasterizer instance.");

        var face = NativeFontFace.Load(fontData, faceIndex);

        // Fail here rather than on the first glyph: a CFF face parses fine and would otherwise
        // render an entire atlas of nothing before anyone noticed.
        if (!face.HasGlyfOutlines)
            throw new RasterizationException(
                "The native rasterizer renders TrueType ('glyf') outlines only, and this font uses "
                + "CFF/CFF2 (PostScript) outlines — typically a .otf file. CFF outline support is "
                + "Phase 166; until then use the FreeType or StbTrueType backend for this font.");

        _face = face;
    }

    /// <summary>
    /// Not supported. The native rasterizer cannot load system fonts by name.
    /// </summary>
    public void LoadSystemFont(string familyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException(
            "Native rasterizer does not support loading system fonts by name. Use LoadFont with font bytes instead.");
    }

    /// <inheritdoc />
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        RejectUnsupported(options);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        float scale = PixelScale(options);
        int advance = ScaleAdvance(glyphIndex, scale);

        var parsed = LoadGlyph(codepoint, glyphIndex);
        var outline = OutlineExtractor.Extract(parsed);
        var box = GlyphBox.Compute(parsed, outline, scale);

        if (outline.IsEmpty || box.IsEmpty)
            return BlankGlyph(codepoint, glyphIndex, advance);

        // Baseline on row 0: the box's own Y origin then places the bitmap against it.
        var edges = OutlineFlattener.Flatten(outline, new OutlineTransform(scale, 0f));
        var raster = ScanlineRasterizer.Rasterize(
            edges,
            box.Width,
            box.Height,
            box.MinX,
            box.MinY,
            options.AntiAlias,
            box.BearingX,
            box.BearingY);

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = raster.Bitmap,
            Width = raster.Width,
            Height = raster.Height,
            Pitch = raster.Width,
            Metrics = new GlyphMetrics(
                BearingX: raster.BearingX,
                BearingY: raster.BearingY,
                Advance: advance,
                Width: raster.Width,
                Height: raster.Height),
            Format = PixelFormat.Grayscale8
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(codepoints);

        var results = new List<RasterizedGlyph>();
        foreach (int codepoint in codepoints)
        {
            var glyph = RasterizeGlyph(codepoint, options);
            if (glyph is not null)
                results.Add(glyph);
        }

        return results;
    }

    /// <inheritdoc />
    public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        RejectUnsupported(options);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        float scale = PixelScale(options);
        int advance = ScaleAdvance(glyphIndex, scale);

        var parsed = LoadGlyph(codepoint, glyphIndex);
        var outline = OutlineExtractor.Extract(parsed);
        var box = GlyphBox.Compute(parsed, outline, scale);

        if (outline.IsEmpty || box.IsEmpty)
            return new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: advance, Width: 0, Height: 0);

        return new GlyphMetrics(
            BearingX: box.BearingX,
            BearingY: box.BearingY,
            Advance: advance,
            Width: box.Width,
            Height: box.Height);
    }

    /// <inheritdoc />
    public RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);

        double scale = PixelScale(options);

        // OS/2 usWinAscent/usWinDescent, not hhea: these are what GDI reports as
        // tmAscent/tmDescent and therefore what bmfont.exe writes as base/lineHeight. Using
        // them reproduces, to the pixel, the shared calculation the pipeline falls back to for
        // FreeType, so swapping backends does not shift the line box. hhea is the fallback for
        // the rare face with no usable OS/2 metrics.
        //
        // Ceiling, not rounding: a line box a fraction of a pixel short clips the glyphs it is
        // supposed to contain.
        var os2 = _face!.Os2;
        if (os2.WinAscent > 0)
        {
            return new RasterizerFontMetrics
            {
                Ascent = (int)Math.Ceiling(os2.WinAscent * scale),
                Descent = (int)Math.Ceiling(os2.WinDescent * scale),
                LineHeight = (int)Math.Ceiling((os2.WinAscent + os2.WinDescent) * scale)
            };
        }

        var hhea = _face.Hhea;
        return new RasterizerFontMetrics
        {
            Ascent = (int)Math.Ceiling(hhea.Ascender * scale),
            Descent = (int)Math.Ceiling(Math.Abs((double)hhea.Descender) * scale),
            LineHeight = (int)Math.Ceiling((hhea.Ascender - hhea.Descender + hhea.LineGap) * scale)
        };
    }

    /// <summary>
    /// Returns null to let the shared GPOS/kern parser handle kerning, the same as the
    /// StbTrueType backend.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _face = null;
    }

    private void EnsureFontLoaded()
    {
        if (_face is null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");
    }

    /// <summary>The glyph index a codepoint maps to, or 0 when unmapped.</summary>
    internal int GetGlyphIndex(int codepoint)
    {
        EnsureFontLoaded();
        return _face!.GetGlyphIndex(codepoint);
    }

    /// <summary>The parsed font face. Internal access for tests and later-phase decoders.</summary>
    internal NativeFontFace? Face => _face;

    /// <summary>
    /// Pixels per font unit. <see cref="IRasterizerCapabilities.HandlesOwnSizing"/> is false,
    /// so <see cref="RasterOptions.Size"/> is already the em size the pipeline wants; only the
    /// point-to-pixel DPI conversion is applied on top of it.
    /// </summary>
    private float PixelScale(RasterOptions options) =>
        options.Size * options.Dpi / 72f / _face!.Head.UnitsPerEm;

    private int ScaleAdvance(int glyphIndex, float scale) =>
        (int)Math.Round(_face!.Hmtx.GetAdvanceWidth(glyphIndex) * (double)scale);

    private ParsedGlyph LoadGlyph(int codepoint, int glyphIndex)
    {
        try
        {
            return _face!.GetGlyph(glyphIndex);
        }
        catch (FontFormatException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
    }

    private static void RejectUnsupported(RasterOptions options)
    {
        if (options.ColorFont)
            throw new NotSupportedException("Native rasterizer does not support color font rendering.");

        if (options.Sdf)
            throw new NotSupportedException("Native rasterizer does not support SDF rendering.");
    }

    /// <summary>A whitespace glyph: a real advance, but nothing to draw.</summary>
    private static RasterizedGlyph BlankGlyph(int codepoint, int glyphIndex, int advance) =>
        new()
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = [],
            Width = 0,
            Height = 0,
            Pitch = 0,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: advance, Width: 0, Height: 0),
            Format = PixelFormat.Grayscale8
        };
}
