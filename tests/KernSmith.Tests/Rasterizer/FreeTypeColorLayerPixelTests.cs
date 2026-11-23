using FreeTypeSharp;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Pixel-level checks of <c>COLR</c> version 0 glyphs against an oracle built from raw FreeType
/// calls: each layer loaded, styled and rendered alone, its coverage expanded to 8 bits, the
/// layers blended bottom to top. FreeType's own blend reads a 1-bit layer as if it were 8-bit, so
/// under <see cref="AntiAliasMode.None"/> only the oracle says what the pixels should be.
/// </summary>
public class FreeTypeColorLayerPixelTests
{
    private const int FirstPrintable = 33;
    private const int LastPrintable = 126;

    // Bungee Color draws its letters and digits from COLR layers; a sweep comparing fewer glyphs
    // than this has stopped exercising the layer path.
    private const int MinColorGlyphs = 36;

    private static readonly byte[] Bungee =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Color", "BungeeColor-Regular.ttf"));

    private sealed record ColorBitmap(int Left, int Top, int Width, int Height, byte[] Rgba);

    private static RasterOptions Options(
        int size,
        AntiAliasMode antiAlias,
        bool hinted = true,
        bool bold = false,
        bool italic = false,
        bool sdf = false) =>
        new()
        {
            Size = size,
            Dpi = 72,
            ColorFont = true,
            AntiAlias = antiAlias,
            EnableHinting = hinted,
            Bold = bold,
            Italic = italic,
            Sdf = sdf,
        };

    private static int LoadFlags(RasterOptions options)
    {
        var flags = 0;
        if (options.AntiAlias == AntiAliasMode.None && !options.Sdf)
            flags |= FreeTypeNative.FT_LOAD_TARGET_MONO;
        else if (options.AntiAlias == AntiAliasMode.Light && options.EnableHinting)
            flags |= FreeTypeNative.FT_LOAD_TARGET_LIGHT;
        if (!options.EnableHinting)
            flags |= FreeTypeNative.FT_LOAD_NO_HINTING;
        return flags;
    }

    private static unsafe void WithFace(RasterOptions options, Action<IntPtr> action)
    {
        using var library = new FreeTypeLibrary();
        fixed (byte* data = Bungee)
        {
            FT_FaceRec_* face;
            FT.FT_New_Memory_Face(library.Native, data, (IntPtr)Bungee.Length, IntPtr.Zero, &face)
                .ShouldBe(FT_Error.FT_Err_Ok);
            try
            {
                var size = (IntPtr)(options.Size * 64);
                FT.FT_Set_Char_Size(face, size, size, (uint)options.Dpi, (uint)options.Dpi)
                    .ShouldBe(FT_Error.FT_Err_Ok);
                action((IntPtr)face);
            }
            finally
            {
                FT.FT_Done_Face(face);
            }
        }
    }

    /// <summary>The glyph's layers blended from raw FreeType renders; null for a glyph with no layers.</summary>
    private static unsafe ColorBitmap? BlendLayers(int codepoint, RasterOptions options)
    {
        ColorBitmap? result = null;
        WithFace(options, facePointer =>
        {
            var face = (FT_FaceRec_*)facePointer;
            var glyph = FT.FT_Get_Char_Index(face, (UIntPtr)codepoint);

            var records = new List<(uint Glyph, uint Color)>();
            var iterator = default(FreeTypeNative.FT_LayerIterator);
            while (FreeTypeNative.FT_Get_Color_Glyph_Layer(face, glyph, out var layerGlyph, out var layerColor, ref iterator))
                records.Add((layerGlyph, layerColor));
            if (records.Count == 0)
                return;

            byte* palette = null;
            FreeTypeNative.FT_Palette_Select(face, 0, (IntPtr)(&palette)).ShouldBe(FT_Error.FT_Err_Ok);

            var mono = options.AntiAlias == AntiAliasMode.None && !options.Sdf;
            var layers = new List<(byte[] Coverage, int Left, int Top, int Width, int Height, uint Color)>();
            foreach (var (layerGlyph, color) in records)
            {
                FT.FT_Load_Glyph(face, layerGlyph, (FT_LOAD)LoadFlags(options)).ShouldBe(FT_Error.FT_Err_Ok);
                var slot = face->glyph;
                if (slot->outline.n_points == 0)
                    continue;

                if (options.Bold)
                {
                    var strength = Math.Max(32, (int)face->size->metrics.y_ppem * 64 / 24);
                    FreeTypeNative.FT_Outline_Embolden(&slot->outline, strength).ShouldBe(FT_Error.FT_Err_Ok);
                }
                if (options.Italic)
                    FT.FT_GlyphSlot_Oblique(slot);

                FT.FT_Render_Glyph(slot, mono ? FT_Render_Mode_.FT_RENDER_MODE_MONO : FT_Render_Mode_.FT_RENDER_MODE_NORMAL)
                    .ShouldBe(FT_Error.FT_Err_Ok);

                ref var bitmap = ref slot->bitmap;
                int width = (int)bitmap.width, height = (int)bitmap.rows;
                var coverage = new byte[width * height];
                for (var y = 0; y < height; y++)
                {
                    var row = bitmap.buffer + y * bitmap.pitch;
                    for (var x = 0; x < width; x++)
                    {
                        coverage[y * width + x] = mono
                            ? (byte)((row[x / 8] & (0x80 >> (x % 8))) != 0 ? 255 : 0)
                            : row[x];
                    }
                }
                layers.Add((coverage, slot->bitmap_left, slot->bitmap_top, width, height, color));
            }

            if (layers.Count == 0)
            {
                result = new ColorBitmap(0, 0, 0, 0, []);
                return;
            }

            var left = layers.Min(l => l.Left);
            var top = layers.Max(l => l.Top);
            var right = layers.Max(l => l.Left + l.Width);
            var bottom = layers.Min(l => l.Top - l.Height);
            int boxWidth = right - left, boxHeight = top - bottom;

            // Source-over on premultiplied BGRA with FreeType's truncating divisions.
            var pixels = new byte[boxWidth * boxHeight * 4];
            foreach (var layer in layers)
            {
                int b = 0, g = 0, r = 0, alpha = 255;
                if (layer.Color != 0xFFFF)
                {
                    var entry = palette + layer.Color * 4;
                    (b, g, r, alpha) = (entry[0], entry[1], entry[2], entry[3]);
                }

                for (var y = 0; y < layer.Height; y++)
                {
                    var dst = ((top - layer.Top + y) * boxWidth + layer.Left - left) * 4;
                    for (var x = 0; x < layer.Width; x++, dst += 4)
                    {
                        var fa = alpha * layer.Coverage[y * layer.Width + x] / 255;
                        var inverse = 255 - fa;
                        pixels[dst] = (byte)(pixels[dst] * inverse / 255 + b * fa / 255);
                        pixels[dst + 1] = (byte)(pixels[dst + 1] * inverse / 255 + g * fa / 255);
                        pixels[dst + 2] = (byte)(pixels[dst + 2] * inverse / 255 + r * fa / 255);
                        pixels[dst + 3] = (byte)(pixels[dst + 3] * inverse / 255 + fa);
                    }
                }
            }

            result = new ColorBitmap(left, top, boxWidth, boxHeight, ToStraightRgba(pixels));
        });
        return result;
    }

    /// <summary>FreeType's own COLR path: the base glyph loaded with FT_LOAD_COLOR and rendered.</summary>
    private static unsafe ColorBitmap FreeTypeOwnBlend(int codepoint, RasterOptions options)
    {
        ColorBitmap? result = null;
        WithFace(options, facePointer =>
        {
            var face = (FT_FaceRec_*)facePointer;
            var glyph = FT.FT_Get_Char_Index(face, (UIntPtr)codepoint);
            FT.FT_Load_Glyph(face, glyph, (FT_LOAD)(LoadFlags(options) | FreeTypeNative.FT_LOAD_COLOR))
                .ShouldBe(FT_Error.FT_Err_Ok);
            var slot = face->glyph;
            var mode = options.AntiAlias == AntiAliasMode.Light
                ? FT_Render_Mode_.FT_RENDER_MODE_LIGHT
                : FT_Render_Mode_.FT_RENDER_MODE_NORMAL;
            FT.FT_Render_Glyph(slot, mode).ShouldBe(FT_Error.FT_Err_Ok);

            ref var bitmap = ref slot->bitmap;
            bitmap.pixel_mode.ShouldBe(FT_Pixel_Mode_.FT_PIXEL_MODE_BGRA);
            int width = (int)bitmap.width, height = (int)bitmap.rows;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var row = bitmap.buffer + y * bitmap.pitch;
                for (var x = 0; x < width * 4; x++)
                    pixels[y * width * 4 + x] = row[x];
            }
            result = new ColorBitmap(slot->bitmap_left, slot->bitmap_top, width, height, ToStraightRgba(pixels));
        });
        return result!;
    }

    private static byte[] ToStraightRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
            if (a == 0)
                continue;
            var half = a / 2;
            rgba[i] = (byte)Math.Min(255, (r * 255 + half) / a);
            rgba[i + 1] = (byte)Math.Min(255, (g * 255 + half) / a);
            rgba[i + 2] = (byte)Math.Min(255, (b * 255 + half) / a);
            rgba[i + 3] = (byte)a;
        }
        return rgba;
    }

    /// <summary>Compares every printable glyph with <paramref name="expectedFor"/> and reports the damage.</summary>
    private static void ShouldMatchEveryGlyph(RasterOptions options, Func<int, ColorBitmap?> expectedFor)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(Bungee);

        int compared = 0, badGlyphs = 0, alphaDiffs = 0, colorDiffs = 0, totalPixels = 0;
        string? first = null;
        for (var codepoint = FirstPrintable; codepoint <= LastPrintable; codepoint++)
        {
            var expected = expectedFor(codepoint);
            if (expected == null)
                continue;
            var actual = rasterizer.RasterizeGlyph(codepoint, options);
            actual.ShouldNotBeNull();
            compared++;
            totalPixels += expected.Width * expected.Height;

            if (actual.Width != expected.Width || actual.Height != expected.Height
                || actual.Metrics.BearingX != expected.Left || actual.Metrics.BearingY != expected.Top)
            {
                badGlyphs++;
                first ??= $"'{(char)codepoint}': expected box {expected.Width}x{expected.Height} at ({expected.Left},{expected.Top}) "
                    + $"but was {actual.Width}x{actual.Height} at ({actual.Metrics.BearingX},{actual.Metrics.BearingY})";
                continue;
            }

            actual.Format.ShouldBe(PixelFormat.Rgba32);
            var bad = false;
            for (var i = 0; i < expected.Rgba.Length; i += 4)
            {
                var alphaDiffers = actual.BitmapData[i + 3] != expected.Rgba[i + 3];
                var colorDiffers = actual.BitmapData[i] != expected.Rgba[i]
                    || actual.BitmapData[i + 1] != expected.Rgba[i + 1]
                    || actual.BitmapData[i + 2] != expected.Rgba[i + 2];
                if (!alphaDiffers && !colorDiffers)
                    continue;

                bad = true;
                if (alphaDiffers)
                    alphaDiffs++;
                if (colorDiffers)
                    colorDiffs++;
                first ??= $"'{(char)codepoint}' pixel ({i / 4 % expected.Width},{i / 4 / expected.Width}): "
                    + $"expected RGBA({expected.Rgba[i]},{expected.Rgba[i + 1]},{expected.Rgba[i + 2]},{expected.Rgba[i + 3]}) "
                    + $"but was RGBA({actual.BitmapData[i]},{actual.BitmapData[i + 1]},{actual.BitmapData[i + 2]},{actual.BitmapData[i + 3]})";
            }
            if (bad)
                badGlyphs++;
        }

        compared.ShouldBeGreaterThanOrEqualTo(MinColorGlyphs);
        badGlyphs.ShouldBe(0,
            $"{badGlyphs} of {compared} glyphs differ; {alphaDiffs} alpha and {colorDiffs} color diffs in {totalPixels} pixels; first: {first}");
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(12, false)]
    [InlineData(24, true)]
    [InlineData(24, false)]
    [InlineData(48, true)]
    [InlineData(48, false)]
    public void UnstyledNone_IsTheBlendOfOneBitLayers(int size, bool hinted)
    {
        var options = Options(size, AntiAliasMode.None, hinted);
        ShouldMatchEveryGlyph(options, codepoint => BlendLayers(codepoint, options));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(48)]
    public void UnstyledNone_AlphaIsBilevel(int size)
    {
        var options = Options(size, AntiAliasMode.None);
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(Bungee);

        int partial = 0, opaque = 0;
        for (var codepoint = FirstPrintable; codepoint <= LastPrintable; codepoint++)
        {
            var glyph = rasterizer.RasterizeGlyph(codepoint, options);
            if (glyph == null || glyph.Format != PixelFormat.Rgba32)
                continue;
            for (var i = 3; i < glyph.BitmapData.Length; i += 4)
            {
                if (glyph.BitmapData[i] == 255)
                    opaque++;
                else if (glyph.BitmapData[i] != 0)
                    partial++;
            }
        }

        opaque.ShouldBeGreaterThan(0);
        partial.ShouldBe(0, "Bungee's palette is opaque, so a bilevel glyph has only alpha 0 and 255");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void None_MetricsMatchTheBitmap(bool bold, bool italic)
    {
        var options = Options(24, AntiAliasMode.None, bold: bold, italic: italic);
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(Bungee);

        for (var codepoint = FirstPrintable; codepoint <= LastPrintable; codepoint++)
        {
            var metrics = rasterizer.GetGlyphMetrics(codepoint, options);
            var glyph = rasterizer.RasterizeGlyph(codepoint, options);
            if (glyph == null)
                continue;
            metrics.ShouldBe(glyph.Metrics, $"codepoint {codepoint}");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void None_BlankGlyphHasAnEmptyBox(bool bold, bool italic)
    {
        var options = Options(24, AntiAliasMode.None, bold: bold, italic: italic);
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(Bungee);

        var space = rasterizer.RasterizeGlyph(' ', options);

        space.ShouldNotBeNull();
        (space.Width, space.Height).ShouldBe((0, 0));
    }

    [Theory]
    [InlineData(AntiAliasMode.Grayscale, 12, true)]
    [InlineData(AntiAliasMode.Grayscale, 24, false)]
    [InlineData(AntiAliasMode.Grayscale, 48, true)]
    [InlineData(AntiAliasMode.Light, 12, true)]
    [InlineData(AntiAliasMode.Light, 24, false)]
    [InlineData(AntiAliasMode.Light, 48, true)]
    public void UnstyledGrayscaleAndLight_StayFreeTypesOwnBlend(AntiAliasMode mode, int size, bool hinted)
    {
        var options = Options(size, mode, hinted);
        ShouldMatchEveryGlyph(options, codepoint =>
            BlendLayers(codepoint, options) == null ? null : FreeTypeOwnBlend(codepoint, options));
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(48, true)]
    [InlineData(48, false)]
    public void Oracle_ReproducesFreeTypesOwnGrayscaleBlend(int size, bool hinted)
    {
        var options = Options(size, AntiAliasMode.Grayscale, hinted);
        ShouldMatchEveryGlyph(options, codepoint => BlendLayers(codepoint, options));
    }

    [Theory]
    [InlineData(true, false, 24)]
    [InlineData(false, true, 24)]
    [InlineData(true, true, 24)]
    [InlineData(true, false, 48)]
    [InlineData(false, true, 48)]
    [InlineData(true, true, 48)]
    public void StyledNone_IsTheBlendOfStyledOneBitLayers(bool bold, bool italic, int size)
    {
        var options = Options(size, AntiAliasMode.None, bold: bold, italic: italic);
        ShouldMatchEveryGlyph(options, codepoint => BlendLayers(codepoint, options));
    }

    [Theory]
    [InlineData(true, false, 24)]
    [InlineData(false, true, 24)]
    [InlineData(true, true, 24)]
    [InlineData(true, false, 48)]
    [InlineData(false, true, 48)]
    [InlineData(true, true, 48)]
    public void StyledLcd_IsTheBlendOfStyledGrayLayers(bool bold, bool italic, int size)
    {
        var options = Options(size, AntiAliasMode.Lcd, bold: bold, italic: italic);
        ShouldMatchEveryGlyph(options, codepoint => BlendLayers(codepoint, options));
    }

    [Theory]
    [InlineData(true, false, 24)]
    [InlineData(false, true, 24)]
    [InlineData(true, true, 24)]
    [InlineData(true, false, 48)]
    [InlineData(false, true, 48)]
    [InlineData(true, true, 48)]
    public void StyledSdf_IsTheBlendOfStyledGrayLayers(bool bold, bool italic, int size)
    {
        var options = Options(size, AntiAliasMode.Grayscale, bold: bold, italic: italic, sdf: true);
        ShouldMatchEveryGlyph(options, codepoint => BlendLayers(codepoint, options));
    }

    [Theory]
    [InlineData(AntiAliasMode.Lcd, false)]
    [InlineData(AntiAliasMode.Grayscale, true)]
    public void UnstyledLcdAndSdf_KeepFreeTypesGrayLayerBlend(AntiAliasMode mode, bool sdf)
    {
        var options = Options(48, mode, sdf: sdf);
        ShouldMatchEveryGlyph(options, codepoint => BlendLayers(codepoint, options));
    }
}
