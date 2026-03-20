using System.Runtime.InteropServices;
using Bmfontier.Font.Models;
using Bmfontier.Font.Tables;
using FreeTypeSharp;

namespace Bmfontier.Rasterizer;

internal sealed class FreeTypeRasterizer : IRasterizer
{
    private FreeTypeLibrary? _library;
    private GCHandle _pinnedFontData;
    private unsafe FT_FaceRec_* _face;
    private bool _disposed;

    public unsafe void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        _library = new FreeTypeLibrary();

        // Pin a copy of the font data so FreeType can access it for the lifetime of the face.
        var fontBytes = fontData.ToArray();
        _pinnedFontData = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);

        FT_FaceRec_* face;
        var error = FT.FT_New_Memory_Face(
            _library.Native,
            (byte*)_pinnedFontData.AddrOfPinnedObject(),
            (IntPtr)fontBytes.Length,
            (IntPtr)faceIndex,
            &face);

        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        _face = face;
    }

    /// <summary>
    /// Selects a color palette by index for CPAL-based color fonts.
    /// Must be called after <see cref="LoadFont"/> and before rasterization.
    /// </summary>
    /// <param name="paletteIndex">Zero-based palette index.</param>
    internal unsafe void SelectColorPalette(int paletteIndex)
    {
        if (_face == null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");

        var error = FreeTypeNative.FT_Palette_Select(_face, (ushort)paletteIndex, IntPtr.Zero);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);
    }

    /// <summary>
    /// Applies variation axis coordinates to the loaded face for variable fonts.
    /// Must be called after <see cref="LoadFont"/> and before rasterization.
    /// </summary>
    /// <param name="fvarAxes">The axes defined in the font's fvar table, in order.</param>
    /// <param name="userAxes">User-specified axis tag/value pairs (e.g., "wght" = 700).</param>
    internal unsafe void SetVariationAxes(
        IReadOnlyList<VariationAxis> fvarAxes,
        Dictionary<string, float> userAxes)
    {
        if (_face == null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");

        if (fvarAxes.Count == 0)
            return;

        // Build the coordinate array in fvar axis order.
        // Axes not specified by the user get their default value.
        // FT_Fixed is a 16.16 fixed-point integer: value * 65536.
        var numAxes = fvarAxes.Count;
        var coords = stackalloc int[numAxes];

        for (var i = 0; i < numAxes; i++)
        {
            var axis = fvarAxes[i];
            var value = userAxes.TryGetValue(axis.Tag, out var userValue)
                ? userValue
                : axis.DefaultValue;

            // Clamp to the axis range.
            value = Math.Clamp(value, axis.MinValue, axis.MaxValue);

            coords[i] = (int)(value * 65536.0f);
        }

        var error = FreeTypeNative.FT_Set_Var_Design_Coordinates(
            _face, (uint)numAxes, coords);

        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);
    }

    public unsafe RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        if (_face == null || _library == null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");

        // Set character size: FreeType expects size in 26.6 fixed-point (multiply by 64).
        var sizeF26D6 = (IntPtr)(options.Size * 64);
        var error = FT.FT_Set_Char_Size(_face, sizeF26D6, sizeF26D6, (uint)options.Dpi, (uint)options.Dpi);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        // Get glyph index for the codepoint.
        var glyphIndex = FT.FT_Get_Char_Index(_face, (UIntPtr)codepoint);
        if (glyphIndex == 0)
            return null; // Missing glyph

        // Determine load flags based on anti-alias mode and hinting.
        var loadFlags = options.ColorFont
            ? (FT_LOAD)FreeTypeNative.FT_LOAD_COLOR
            : FT_LOAD.FT_LOAD_DEFAULT;

        if (!options.EnableHinting)
            loadFlags |= (FT_LOAD)FreeTypeNative.FT_LOAD_NO_HINTING;

        // Load the glyph.
        error = FT.FT_Load_Glyph(_face, glyphIndex, loadFlags);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        var slot = _face->glyph;

        // Apply bold embolden if requested.
        if (options.Bold)
            FT.FT_GlyphSlot_Embolden(slot);

        // Apply italic oblique if requested.
        if (options.Italic)
            FT.FT_GlyphSlot_Oblique(slot);

        // Determine render mode.
        // When SDF is enabled, FT_RENDER_MODE_SDF (value 6) produces an 8-bit bitmap where
        // 128 = on the glyph edge, >128 = inside, <128 = outside. The bitmap dimensions
        // may be larger than normal rendering to include the signed-distance field padding.
        // SDF takes priority over the anti-alias setting since SDF output is inherently smooth.
        var renderMode = options.Sdf
            ? FT_Render_Mode_.FT_RENDER_MODE_SDF
            : options.AntiAlias switch
            {
                AntiAliasMode.None => FT_Render_Mode_.FT_RENDER_MODE_MONO,
                AntiAliasMode.Light => FT_Render_Mode_.FT_RENDER_MODE_LIGHT,
                AntiAliasMode.Lcd => FT_Render_Mode_.FT_RENDER_MODE_LCD,
                _ => FT_Render_Mode_.FT_RENDER_MODE_NORMAL
            };

        // Render the glyph to bitmap.
        error = FT.FT_Render_Glyph(slot, renderMode);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        ref var bitmap = ref slot->bitmap;
        var bitmapWidth = (int)bitmap.width;
        var bitmapHeight = (int)bitmap.rows;
        var pitch = bitmap.pitch;

        // Extract metrics from 26.6 fixed-point values.
        ref var metrics = ref slot->metrics;
        var glyphMetrics = new GlyphMetrics(
            BearingX: F26Dot6ToRounded(metrics.horiBearingX),
            BearingY: F26Dot6ToRounded(metrics.horiBearingY),
            Advance: F26Dot6ToRounded(metrics.horiAdvance),
            Width: F26Dot6ToRounded(metrics.width),
            Height: F26Dot6ToRounded(metrics.height));

        // Copy bitmap data.
        byte[] bitmapData;
        if (bitmapWidth == 0 || bitmapHeight == 0)
        {
            // Zero-size glyph (e.g., space character).
            bitmapData = Array.Empty<byte>();
        }
        else
        {
            var absPitch = Math.Abs(pitch);
            bitmapData = new byte[absPitch * bitmapHeight];
            var src = (IntPtr)bitmap.buffer;
            for (var row = 0; row < bitmapHeight; row++)
            {
                Marshal.Copy(src + row * pitch, bitmapData, row * absPitch, absPitch);
            }
        }

        // Swap BGRA to RGBA when FreeType returns a color bitmap.
        if (bitmap.pixel_mode == FT_Pixel_Mode_.FT_PIXEL_MODE_BGRA)
        {
            for (var i = 0; i < bitmapData.Length; i += 4)
                (bitmapData[i], bitmapData[i + 2]) = (bitmapData[i + 2], bitmapData[i]);
        }

        // Determine pixel format based on the FreeType pixel mode.
        var format = bitmap.pixel_mode == FT_Pixel_Mode_.FT_PIXEL_MODE_BGRA
            ? PixelFormat.Rgba32
            : PixelFormat.Grayscale8;

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = (int)glyphIndex,
            BitmapData = bitmapData,
            Width = bitmapWidth,
            Height = bitmapHeight,
            Pitch = Math.Abs(pitch),
            Metrics = glyphMetrics,
            Format = format
        };
    }

    /// <summary>
    /// Rasterizes just the outline border of a glyph using FT_Stroker.
    /// Returns an RGBA glyph with the specified outline color, or null if the glyph is missing.
    /// </summary>
    internal unsafe RasterizedGlyph? RasterizeOutline(int codepoint, RasterOptions options, int outlineWidth, byte r, byte g, byte b)
    {
        if (_face == null || _library == null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");

        var sizeF26D6 = (IntPtr)(options.Size * 64);
        var error = FT.FT_Set_Char_Size(_face, sizeF26D6, sizeF26D6, (uint)options.Dpi, (uint)options.Dpi);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        var glyphIndex = FT.FT_Get_Char_Index(_face, (UIntPtr)codepoint);
        if (glyphIndex == 0)
            return null;

        // Load glyph outline (no bitmap, no hinting for cleaner strokes).
        var loadFlags = (FT_LOAD)FreeTypeNative.FT_LOAD_NO_BITMAP;
        if (!options.EnableHinting)
            loadFlags |= (FT_LOAD)FreeTypeNative.FT_LOAD_NO_HINTING;

        error = FT.FT_Load_Glyph(_face, glyphIndex, loadFlags);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        var slot = _face->glyph;

        if (options.Bold)
            FT.FT_GlyphSlot_Embolden(slot);
        if (options.Italic)
            FT.FT_GlyphSlot_Oblique(slot);

        // Get a copy of the glyph.
        error = FreeTypeNative.FT_Get_Glyph(slot, out var ftGlyph);
        if (error != FT_Error.FT_Err_Ok)
            throw new FreeTypeException(error);

        // Create and configure the stroker.
        error = FreeTypeNative.FT_Stroker_New((IntPtr)_library.Native, out var stroker);
        if (error != FT_Error.FT_Err_Ok)
        {
            FreeTypeNative.FT_Done_Glyph(ftGlyph);
            throw new FreeTypeException(error);
        }

        try
        {
            // Radius in 26.6 fixed-point.
            var radius = outlineWidth * 64;
            FreeTypeNative.FT_Stroker_Set(stroker, radius,
                FreeTypeNative.FT_STROKER_LINECAP_ROUND,
                FreeTypeNative.FT_STROKER_LINEJOIN_ROUND, 0);

            // Stroke the outer border.
            error = FreeTypeNative.FT_Glyph_StrokeBorder(ref ftGlyph, stroker, false, true);
            if (error != FT_Error.FT_Err_Ok)
                throw new FreeTypeException(error);

            // Rasterize to bitmap (FT_RENDER_MODE_NORMAL = 0).
            error = FreeTypeNative.FT_Glyph_To_Bitmap(ref ftGlyph, 0, IntPtr.Zero, true);
            if (error != FT_Error.FT_Err_Ok)
                throw new FreeTypeException(error);

            // Read bitmap data from the FT_BitmapGlyphRec_.
            FreeTypeNative.ReadBitmapGlyph(ftGlyph,
                out var bmpLeft, out var bmpTop,
                out var bmpRows, out var bmpWidth, out var bmpPitch, out var bmpBuffer);

            if (bmpWidth == 0 || bmpRows == 0)
            {
                return new RasterizedGlyph
                {
                    Codepoint = codepoint,
                    GlyphIndex = (int)glyphIndex,
                    BitmapData = Array.Empty<byte>(),
                    Width = 0,
                    Height = 0,
                    Pitch = 0,
                    Metrics = new Font.Models.GlyphMetrics(
                        BearingX: bmpLeft,
                        BearingY: bmpTop,
                        Advance: F26Dot6ToRounded(slot->metrics.horiAdvance),
                        Width: 0,
                        Height: 0),
                    Format = PixelFormat.Rgba32
                };
            }

            // Convert grayscale outline bitmap to RGBA with the specified color.
            var absPitch = Math.Abs(bmpPitch);
            var rgbaData = new byte[bmpWidth * bmpRows * 4];

            for (var row = 0; row < bmpRows; row++)
            {
                for (var col = 0; col < bmpWidth; col++)
                {
                    var srcAlpha = Marshal.ReadByte(bmpBuffer, row * absPitch + col);
                    if (srcAlpha == 0) continue;

                    var dstIdx = (row * bmpWidth + col) * 4;
                    rgbaData[dstIdx + 0] = r;
                    rgbaData[dstIdx + 1] = g;
                    rgbaData[dstIdx + 2] = b;
                    rgbaData[dstIdx + 3] = srcAlpha;
                }
            }

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = (int)glyphIndex,
                BitmapData = rgbaData,
                Width = bmpWidth,
                Height = bmpRows,
                Pitch = bmpWidth * 4,
                Metrics = new Font.Models.GlyphMetrics(
                    BearingX: bmpLeft,
                    BearingY: bmpTop,
                    Advance: F26Dot6ToRounded(slot->metrics.horiAdvance),
                    Width: bmpWidth,
                    Height: bmpRows),
                Format = PixelFormat.Rgba32
            };
        }
        finally
        {
            FreeTypeNative.FT_Stroker_Done(stroker);
            FreeTypeNative.FT_Done_Glyph(ftGlyph);
        }
    }

    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        var results = new List<RasterizedGlyph>();
        foreach (var cp in codepoints)
        {
            var glyph = RasterizeGlyph(cp, options);
            if (glyph != null)
                results.Add(glyph);
        }
        return results;
    }

    /// <summary>
    /// Convert a 26.6 fixed-point value to a rounded integer: (value + 32) >> 6.
    /// </summary>
    private static int F26Dot6ToRounded(IntPtr value)
    {
        var v = (long)value;
        return (int)((v + 32) >> 6);
    }

    public unsafe void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_face != null)
        {
            FT.FT_Done_Face(_face);
            _face = null;
        }

        _library?.Dispose();
        _library = null;

        if (_pinnedFontData.IsAllocated)
            _pinnedFontData.Free();
    }
}
