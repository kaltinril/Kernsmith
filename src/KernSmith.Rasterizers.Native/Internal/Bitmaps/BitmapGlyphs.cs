using KernSmith.Imaging;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Bitmaps;

/// <summary>
/// A glyph's PNG bitmap chosen for a request: the PNG, where it sits in its strike, and the scale
/// from the strike to the requested size.
/// </summary>
/// <param name="Png">The PNG bytes; its header has been read and matches <paramref name="Width"/>/<paramref name="Height"/>.</param>
/// <param name="Left">The bitmap's left edge relative to the pen, in strike pixels.</param>
/// <param name="Top">The bitmap's top edge above the baseline, in strike pixels.</param>
/// <param name="Width">Bitmap columns.</param>
/// <param name="Height">Bitmap rows.</param>
/// <param name="ScaleX">Requested pixels per strike pixel, horizontally.</param>
/// <param name="ScaleY">Requested pixels per strike pixel, vertically.</param>
internal readonly record struct BitmapGlyphImage(ReadOnlyMemory<byte> Png, double Left, double Top, int Width, int Height, double ScaleX, double ScaleY);

/// <summary>
/// A face's color bitmap glyphs, from <c>CBLC</c>/<c>CBDT</c> and <c>sbix</c> strikes, and the
/// strike choice for each request.
/// </summary>
/// <remarks>
/// <para>
/// Strike choice, per glyph (strikes need not cover the same glyphs): among the strikes that
/// have a PNG for the glyph, the smallest whose ppem is at or above the requested pixels per em,
/// else the largest — the downscale-from-the-next-larger strike both chapters recommend. Ties
/// keep table order, <c>CBDT</c> strikes before <c>sbix</c>.
/// </para>
/// <para>
/// Placement: a <c>CBDT</c> bitmap sits at its stored bearings. An <c>sbix</c> bitmap's
/// bottom-left corner sits at its origin offsets from the glyph's design-space origin, which is
/// the pen position when the glyph has no <c>glyf</c> contour and the glyph box's
/// (xMin, yMin) when it has one (CFF outlines are not consulted).
/// </para>
/// <para>
/// Malformed table headers fail <see cref="TryLoad"/>; malformed per-glyph records only remove
/// that glyph's bitmap (see <see cref="CbdtTable"/>, <see cref="SbixTable"/>). A PNG that will not
/// decode, or whose size disagrees with its <c>CBDT</c> metrics, is a
/// <see cref="RasterizationException"/>.
/// </para>
/// </remarks>
internal sealed class BitmapGlyphs
{
    /// <summary>The largest glyph PNG decoded. CBDT metrics cap bitmaps at 255 px; sbix has no cap of its own.</summary>
    internal static readonly PngDecodeLimits PngLimits = new(4096, 4096, 4096L * 4096);

    private readonly CbdtTable? _cbdt;
    private readonly SbixTable? _sbix;
    private readonly GlyfTable? _glyf;
    private readonly int _unitsPerEm;

    private BitmapGlyphs(CbdtTable? cbdt, SbixTable? sbix, GlyfTable? glyf, int unitsPerEm, bool isBitmapOnly)
    {
        IsBitmapOnly = isBitmapOnly;
        _cbdt = cbdt;
        _sbix = sbix;
        _glyf = glyf;
        _unitsPerEm = unitsPerEm;
    }

    /// <summary>True when the face has no outlines at all: bitmaps are the only way to draw it.</summary>
    public bool IsBitmapOnly { get; }

    /// <summary>
    /// Reads a face's bitmap tables. Null when it has none, or — for a face with outlines — when
    /// they are malformed (the outlines then draw, as for a broken <c>COLR</c>).
    /// </summary>
    /// <param name="face">The face.</param>
    /// <exception cref="FontFormatException">If a face with no outlines has malformed or empty bitmap tables.</exception>
    public static BitmapGlyphs? TryLoad(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        var tables = face.Tables;
        bool bitmapOnly = !face.HasGlyfOutlines && !tables.IsCff;

        try
        {
            CbdtTable? cbdt = null;
            SbixTable? sbix = null;
            if (tables.TryGetTable("CBLC") is { } cblc && tables.TryGetTable("CBDT") is { } cbdtData)
                cbdt = CbdtTable.Parse(cblc, cbdtData);
            if (tables.TryGetTable("sbix") is { } sbixData)
                sbix = SbixTable.Parse(sbixData, face.Maxp.NumGlyphs);

            if ((cbdt?.StrikeCount ?? 0) + (sbix?.StrikeCount ?? 0) == 0)
            {
                if (bitmapOnly)
                    throw new FontFormatException("The font has no outlines and no usable color bitmap strikes.");
                return null;
            }

            return new BitmapGlyphs(cbdt, sbix, face.Glyf, face.Head.UnitsPerEm, bitmapOnly);
        }
        catch (FontFormatException) when (!bitmapOnly)
        {
            return null;
        }
    }

    /// <summary>True when any strike has a PNG for the glyph.</summary>
    public bool HasBitmap(int glyphIndex)
    {
        for (int s = 0; s < (_cbdt?.StrikeCount ?? 0); s++)
        {
            if (_cbdt!.TryLocate(s, glyphIndex, out _))
                return true;
        }

        for (int s = 0; s < (_sbix?.StrikeCount ?? 0); s++)
        {
            if (_sbix!.TryLocate(s, glyphIndex, out _))
                return true;
        }

        return false;
    }

    /// <summary>Chooses the glyph's strike for <paramref name="pixelsPerEm"/> and places its bitmap.</summary>
    /// <returns>False when no strike has a PNG for the glyph.</returns>
    /// <exception cref="RasterizationException">If the chosen PNG's header is invalid, too large, or disagrees with its metrics.</exception>
    public bool TryGetImage(int glyphIndex, double pixelsPerEm, out BitmapGlyphImage image)
    {
        image = default;
        if (!TryChooseStrike(glyphIndex, pixelsPerEm, out var choice))
            return false;

        if (choice.IsCbdt)
        {
            _cbdt!.TryLocate(choice.Strike, glyphIndex, out var bitmap);
            var header = ReadHeader(glyphIndex, bitmap.Png);
            if (header.Width != bitmap.Width || header.Height != bitmap.Height)
                throw new RasterizationException(
                    $"Glyph {glyphIndex}'s CBDT PNG is {header.Width}x{header.Height} but its metrics say {bitmap.Width}x{bitmap.Height}.");

            image = new BitmapGlyphImage(bitmap.Png, bitmap.BearingX, bitmap.BearingY, bitmap.Width, bitmap.Height,
                pixelsPerEm / _cbdt.PpemX(choice.Strike), pixelsPerEm / _cbdt.PpemY(choice.Strike));
            return true;
        }

        _sbix!.TryLocate(choice.Strike, glyphIndex, out var sbixBitmap);
        var sbixHeader = ReadHeader(glyphIndex, sbixBitmap.Png);
        int ppem = _sbix.Ppem(choice.Strike);
        double left = sbixBitmap.OriginX, bottom = sbixBitmap.OriginY;
        if (ContourOrigin(glyphIndex) is { } corner)
        {
            left += corner.XMin * (double)ppem / _unitsPerEm;
            bottom += corner.YMin * (double)ppem / _unitsPerEm;
        }

        image = new BitmapGlyphImage(sbixBitmap.Png, left, bottom + sbixHeader.Height, sbixHeader.Width, sbixHeader.Height,
            pixelsPerEm / ppem, pixelsPerEm / ppem);
        return true;
    }

    private bool TryChooseStrike(int glyphIndex, double pixelsPerEm, out Choice choice)
    {
        Choice? above = null, below = null;
        void Consider(Choice candidate)
        {
            if (candidate.Ppem >= pixelsPerEm)
            {
                if (above is null || candidate.Ppem < above.Value.Ppem)
                    above = candidate;
            }
            else if (below is null || candidate.Ppem > below.Value.Ppem)
            {
                below = candidate;
            }
        }

        for (int s = 0; s < (_cbdt?.StrikeCount ?? 0); s++)
        {
            if (_cbdt!.TryLocate(s, glyphIndex, out _))
                Consider(new Choice(IsCbdt: true, s, _cbdt.PpemY(s)));
        }

        for (int s = 0; s < (_sbix?.StrikeCount ?? 0); s++)
        {
            if (_sbix!.TryLocate(s, glyphIndex, out _))
                Consider(new Choice(IsCbdt: false, s, _sbix.Ppem(s)));
        }

        choice = above ?? below ?? default;
        return above is not null || below is not null;
    }

    /// <summary>The glyph box corner an sbix bitmap is placed from, or null when the glyph has no <c>glyf</c> contour.</summary>
    private (short XMin, short YMin)? ContourOrigin(int glyphIndex)
    {
        if (_glyf is null)
            return null;

        try
        {
            var glyph = _glyf.GetGlyph(glyphIndex);
            return glyph.IsEmpty ? null : (glyph.XMin, glyph.YMin);
        }
        catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
        {
            throw new RasterizationException($"Glyph {glyphIndex}'s outline, which places its sbix bitmap, is malformed: {ex.Message}");
        }
    }

    private static PngHeader ReadHeader(int glyphIndex, ReadOnlyMemory<byte> png)
    {
        try
        {
            var header = PngDecoder.ReadHeader(png.Span);
            if (header.Width > PngLimits.MaxWidth || header.Height > PngLimits.MaxHeight)
                throw new RasterizationException($"Glyph {glyphIndex}'s bitmap is {header.Width}x{header.Height}, larger than {PngLimits.MaxWidth}x{PngLimits.MaxHeight}.");
            return header;
        }
        catch (PngFormatException ex)
        {
            throw new RasterizationException($"Glyph {glyphIndex}'s bitmap is not a valid PNG: {ex.Message}");
        }
    }

    /// <summary>Decodes a chosen image's pixels.</summary>
    /// <exception cref="RasterizationException">If the PNG does not decode.</exception>
    public static PngImage Decode(int glyphIndex, BitmapGlyphImage image)
    {
        try
        {
            return PngDecoder.Decode(image.Png.Span, PngLimits);
        }
        catch (PngFormatException ex)
        {
            throw new RasterizationException($"Glyph {glyphIndex}'s bitmap is not a valid PNG: {ex.Message}");
        }
    }

    private readonly record struct Choice(bool IsCbdt, int Strike, int Ppem);
}
