using System.Buffers.Binary;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 179 — edge cases, each with its expected behaviour pinned: size and DPI extremes,
/// blank, unmapped and degenerate glyphs, huge coordinates, distance-field spreads, tiny
/// MSDF/LCD/hinted glyphs, out-of-range variation values and palette indexes. Every rendered
/// case also checks that <see cref="NativeRasterizer.GetGlyphMetrics"/> agrees with the bitmap.
/// </summary>
public class NativeEdgeCaseTests
{
    private static readonly AntiAliasMode[] CoverageModes = [AntiAliasMode.Grayscale, AntiAliasMode.None, AntiAliasMode.Lcd, AntiAliasMode.Light];

    /// <summary>Every render path: coverage modes, distance fields, synthetic styles.</summary>
    public static TheoryData<string> Modes() => ["gray", "mono", "lcd", "light", "sdf", "msdf", "mtsdf", "bold-italic"];

    // ------------------------------------------------------------ size and DPI extremes

    [Theory]
    [InlineData(0.5f, 1, 1)]
    [InlineData(1f, 1, 1)]
    [InlineData(2f, 2, 2)]
    [InlineData(500f, 313, 356)]
    [InlineData(2000f, 1252, 1422)]
    public void SizeExtremes_RenderTheGlyphScaled(float size, int width, int height)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        var glyph = Render(native, 'A', new RasterOptions { Size = size });

        (glyph.Width, glyph.Height).ShouldBe((width, height));
        glyph.BitmapData.ShouldContain(b => b != 0, "even a sub-pixel 'A' leaves some coverage");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(500f)]
    public void SizeExtremes_EveryModeRendersAndAgreesWithMetrics(float size)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        foreach (string mode in Modes())
            Render(native, 'g', Options(mode, size));
    }

    [Fact]
    public void Dpi_ScalesLikeSize()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        var atDpi = Render(native, 'A', new RasterOptions { Size = 32, Dpi = 144 });
        var atSize = Render(native, 'A', new RasterOptions { Size = 64 });

        atDpi.BitmapData.ShouldBe(atSize.BitmapData);
        atDpi.Metrics.ShouldBe(atSize.Metrics);
    }

    [Fact]
    public void Dpi_OneRendersASubPixelGlyph()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        var glyph = Render(native, 'A', new RasterOptions { Size = 32, Dpi = 1 });

        (glyph.Width, glyph.Height).ShouldBe((1, 1));
    }

    [Fact]
    public void Dpi_HighEnoughToPassTheSizeLimit_IsARasterizationError()
    {
        // 32 pt at 72000 DPI is 32000 px per em: 'A' would be about 20000 x 23000 px.
        using var native = Load(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = 32, Dpi = 72000 };

        var error = Should.Throw<RasterizationException>(() => native.RasterizeGlyph('A', options));
        error.Message.ShouldContain("16384", Case.Sensitive);
        Should.Throw<RasterizationException>(() => native.GetGlyphMetrics('A', options));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-32f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Size_NotAPositiveFiniteNumber_IsAnArgumentError(float size)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = size };

        Should.Throw<ArgumentOutOfRangeException>(() => native.RasterizeGlyph('A', options)).ParamName.ShouldBe("options");
        Should.Throw<ArgumentOutOfRangeException>(() => native.GetGlyphMetrics('A', options));
        Should.Throw<ArgumentOutOfRangeException>(() => native.GetFontMetrics(options));
    }

    [Fact]
    public void Dpi_Zero_RendersAt72_AsFreeTypeDoes()
    {
        var bytes = TestFonts.RobotoRegularBytes();
        using var native = Load(bytes);
        var atZero = new RasterOptions { Size = 32, Dpi = 0 };
        var at72 = new RasterOptions { Size = 32, Dpi = 72 };

        var glyph = Render(native, 'A', atZero);
        var expected = Render(native, 'A', at72);
        glyph.BitmapData.ShouldBe(expected.BitmapData);
        glyph.Metrics.ShouldBe(expected.Metrics);
        native.GetFontMetrics(atZero).ShouldBe(native.GetFontMetrics(at72));

        // The parity this mirrors: FreeType reads a zero resolution as 72.
        using IRasterizer freeType = new FreeType.FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.GetGlyphMetrics('A', atZero).ShouldBe(freeType.GetGlyphMetrics('A', at72));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-72)]
    [InlineData(int.MinValue)]
    public void Dpi_Negative_IsAnArgumentError(int dpi)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = 32, Dpi = dpi };

        Should.Throw<ArgumentOutOfRangeException>(() => native.RasterizeGlyph('A', options)).ParamName.ShouldBe("options");
        Should.Throw<ArgumentOutOfRangeException>(() => native.GetGlyphMetrics('A', options));
        Should.Throw<ArgumentOutOfRangeException>(() => native.GetFontMetrics(options));
    }

    // ------------------------------------------------------------ blank, .notdef and unmapped

    [Theory]
    [MemberData(nameof(Modes))]
    public void Space_IsABlankGlyphWithAnAdvance(string mode)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        var glyph = Render(native, ' ', Options(mode, 32));

        (glyph.Width, glyph.Height, glyph.Pitch).ShouldBe((0, 0, 0));
        glyph.BitmapData.ShouldBeEmpty();
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(0xE000)]     // private use, not in Roboto
    [InlineData(0x10FFFF)]   // last code point
    [InlineData(0x110000)]   // past Unicode
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void UnmappedCodepoint_ReturnsNull_NeverTheNotdefGlyph(int codepoint)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = 32 };

        native.RasterizeGlyph(codepoint, options).ShouldBeNull();
        native.GetGlyphMetrics(codepoint, options).ShouldBeNull();
    }

    // ------------------------------------------------------------ degenerate outlines

    [Theory]
    [MemberData(nameof(Modes))]
    public void ZeroContourGlyph_IsBlank(string mode)
    {
        // numberOfContours 0 with a header box: no outline at all.
        using var native = Load(RobotoWithGlyphA(Concat(I16(0), I16(0), I16(0), I16(500), I16(500))));

        var glyph = Render(native, 'A', Options(mode, 32));

        (glyph.Width, glyph.Height).ShouldBe((0, 0));
        glyph.Metrics.Advance.ShouldBeGreaterThan(0, "the advance still comes from hmtx");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void SinglePointContour_DrawsNothing(string mode)
    {
        using var native = Load(RobotoWithGlyphA(SimpleGlyph([(600, 600)])));

        AssertDrawsNothing(Render(native, 'A', Options(mode, 32)), mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CollinearContour_DrawsNothing(string mode)
    {
        using var native = Load(RobotoWithGlyphA(SimpleGlyph([(0, 0), (500, 500), (1000, 1000)])));

        AssertDrawsNothing(Render(native, 'A', Options(mode, 32)), mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ZeroAreaOutAndBackContour_DrawsNothing(string mode)
    {
        using var native = Load(RobotoWithGlyphA(SimpleGlyph([(100, 100), (1100, 100), (100, 100)])));

        AssertDrawsNothing(Render(native, 'A', Options(mode, 32)), mode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void HugeCoordinates_RenderAtTheirScaledSize(string mode)
    {
        // A triangle out to the largest int16 coordinate (glyf stores int16 deltas, so 32767 is
        // as far as one step from the origin reaches): 16 em at unitsPerEm 2048.
        using var native = Load(RobotoWithGlyphA(SimpleGlyph([(0, 0), (32767, 0), (0, 32767)])));

        var glyph = Render(native, 'A', Options(mode, 16));

        // 32767 units at 16/2048 px per unit is 256 px, plus at most a couple of pixels of
        // rounding (hinting, synthetic bold) and the distance-field padding.
        int padding = mode is "sdf" or "msdf" or "mtsdf" ? 2 * 4 : 0;
        glyph.Height.ShouldBeInRange(256 + padding, 258 + padding);
        glyph.BitmapData.ShouldContain(b => b != 0);
    }

    // ------------------------------------------------------------ distance-field spreads

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(0.4f)]
    public void SdfSpread_BelowOnePixel_PadsByOnePixel(float spread)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        var one = Render(native, 'A', new RasterOptions { Size = 32, Sdf = true, SdfSpread = 1 });
        var glyph = Render(native, 'A', new RasterOptions { Size = 32, Sdf = true, SdfSpread = spread });

        glyph.Metrics.ShouldBe(one.Metrics);
        glyph.BitmapData.ShouldBe(one.BitmapData);
    }

    [Theory]
    [InlineData(20000f)]
    [InlineData(1e9f)]
    [InlineData(float.PositiveInfinity)]
    public void SdfSpread_PaddingPastTheSizeLimit_IsARasterizationError(float spread)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        foreach (var mode in new[] { SdfMode.Sdf, SdfMode.Msdf, SdfMode.Mtsdf })
        {
            var options = new RasterOptions { Size = 32, SdfMode = mode, SdfSpread = spread };
            Should.Throw<RasterizationException>(() => native.RasterizeGlyph('A', options)).Codepoint.ShouldBe('A');
            Should.Throw<RasterizationException>(() => native.GetGlyphMetrics('A', options));
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void Msdf_TinyGlyphs_AreTheBoxPlusPadding(float size)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var plain = Render(native, 'A', new RasterOptions { Size = size });

        foreach (var mode in new[] { SdfMode.Msdf, SdfMode.Mtsdf })
        {
            var glyph = Render(native, 'A', new RasterOptions { Size = size, SdfMode = mode, SdfSpread = 4 });

            glyph.Format.ShouldBe(PixelFormat.Rgba32);
            (glyph.Width, glyph.Height).ShouldBe((plain.Width + 8, plain.Height + 8));
        }
    }

    [Fact]
    public void Lcd_AtOnePixel_RendersRgba()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        var glyph = Render(native, 'A', new RasterOptions { Size = 1, AntiAlias = AntiAliasMode.Lcd });

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.BitmapData.ShouldContain(b => b != 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Hinting_AtOneToSixPixels_KeepsEveryGlyphVisible(int size)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = true };

        foreach (char c in "AHOoxg")
            Render(native, c, options).BitmapData.ShouldContain(b => b != 0, $"'{c}' at {size}px");
    }

    // ------------------------------------------------------------ variation values

    [Theory]
    [InlineData(5000f, 1000f)]
    [InlineData(1e10f, 1000f)]                 // overflowed 16.16 conversion: clamped to the minimum on .NET 8
    [InlineData(float.PositiveInfinity, 1000f)]
    [InlineData(-100f, 100f)]
    [InlineData(-1e10f, 100f)]
    [InlineData(float.NegativeInfinity, 100f)]
    public void AxisValueOutOfRange_ClampsToTheNearestEnd(float requested, float clamped)
    {
        var axes = NativeVariableFontTests.FvarAxes(TestFonts.RobotoFlexBytes());

        var coordinates = AxisNormalizer.Normalize(axes, new Dictionary<string, float> { ["wght"] = requested }, avar: null);
        var expected = AxisNormalizer.Normalize(axes, new Dictionary<string, float> { ["wght"] = clamped }, avar: null);

        coordinates.ShouldBe(expected);
    }

    [Fact]
    public void AxisValueNaN_LeavesTheAxisAtItsDefault()
    {
        var axes = NativeVariableFontTests.FvarAxes(TestFonts.RobotoFlexBytes());

        var coordinates = AxisNormalizer.Normalize(axes, new Dictionary<string, float> { ["wght"] = float.NaN }, avar: null);

        coordinates.ShouldAllBe(c => c == 0f);
    }

    [Fact]
    public void OutOfRangeAndUnknownAxes_RenderLikeTheirClampedInstance()
    {
        var flex = TestFonts.RobotoFlexBytes();
        var axes = NativeVariableFontTests.FvarAxes(flex);
        var options = new RasterOptions { Size = 32 };

        RasterizedGlyph At(Dictionary<string, float>? values)
        {
            using var native = Load(flex);
            if (values is not null)
                native.SetVariationAxes(axes, values);
            return Render(native, 'A', options);
        }

        At(new() { ["wght"] = 5000 }).BitmapData.ShouldBe(At(new() { ["wght"] = 1000 }).BitmapData);
        At(new() { ["wght"] = -100 }).BitmapData.ShouldBe(At(new() { ["wght"] = 100 }).BitmapData);
        At(new() { ["ZZZZ"] = 5 }).BitmapData.ShouldBe(At(null).BitmapData, "an axis the font lacks is ignored");
    }

    // ------------------------------------------------------------ palettes

    [Theory]
    [InlineData(9)]   // Bungee Color has palettes 0-8
    [InlineData(99)]
    [InlineData(-1)]
    public void PaletteIndexOutOfRange_IsAnArgumentError_DirectlyAndThroughGenerate(int palette)
    {
        var bungee = TestFonts.BungeeColorBytes();
        using var native = Load(bungee);

        Should.Throw<ArgumentOutOfRangeException>(() => native.SelectColorPalette(palette)).ParamName.ShouldBe("paletteIndex");
        Should.Throw<ArgumentOutOfRangeException>(() => BmFont.Generate(bungee, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AB"),
            Backend = RasterizerBackend.Native,
            ColorFont = true,
            ColorPaletteIndex = palette,
        })).ParamName.ShouldBe("paletteIndex");
    }

    [Fact]
    public void PaletteIndex_LastPalette_Renders()
    {
        using var native = Load(TestFonts.BungeeColorBytes());

        native.SelectColorPalette(8);

        Render(native, 'A', new RasterOptions { Size = 32, ColorFont = true }).Format.ShouldBe(PixelFormat.Rgba32);
    }

    // ------------------------------------------------------------ helpers

    private static RasterOptions Options(string mode, float size) => mode switch
    {
        "gray" => new() { Size = size },
        "mono" => new() { Size = size, AntiAlias = AntiAliasMode.None },
        "lcd" => new() { Size = size, AntiAlias = AntiAliasMode.Lcd },
        "light" => new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = true },
        "sdf" => new() { Size = size, Sdf = true, SdfSpread = 4 },
        "msdf" => new() { Size = size, SdfMode = SdfMode.Msdf, SdfSpread = 4 },
        "mtsdf" => new() { Size = size, SdfMode = SdfMode.Mtsdf, SdfSpread = 4 },
        "bold-italic" => new() { Size = size, Bold = true, Italic = true },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>Rasterizes and checks the bitmap is self-consistent and matches <see cref="NativeRasterizer.GetGlyphMetrics"/>.</summary>
    private static RasterizedGlyph Render(NativeRasterizer native, int codepoint, RasterOptions options)
    {
        var glyph = native.RasterizeGlyph(codepoint, options).ShouldNotBeNull();
        native.GetGlyphMetrics(codepoint, options).ShouldBe(glyph.Metrics);

        int bytesPerPixel = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        glyph.Pitch.ShouldBe(glyph.Width * bytesPerPixel);
        glyph.BitmapData.Length.ShouldBe(glyph.Pitch * glyph.Height);
        (glyph.Metrics.Width, glyph.Metrics.Height).ShouldBe((glyph.Width, glyph.Height));
        return glyph;
    }

    /// <summary>
    /// A degenerate outline covers no area: coverage bitmaps are all zero, and distance fields
    /// never read as inside (the edge value 128 is the most a pixel touching the outline gets).
    /// </summary>
    private static void AssertDrawsNothing(RasterizedGlyph glyph, string mode)
    {
        if (mode is "sdf")
            glyph.BitmapData.ShouldAllBe(b => b <= 128);
        else if (mode is "msdf" or "mtsdf")
            Enumerable.Range(0, glyph.BitmapData.Length).Where(i => i % 4 != 3 || mode == "mtsdf")
                .ShouldAllBe(i => glyph.BitmapData[i] <= 128);
        else
            glyph.BitmapData.ShouldAllBe(b => b == 0);
    }

    private static NativeRasterizer Load(byte[] font)
    {
        var native = new NativeRasterizer();
        native.LoadFont(font);
        return native;
    }

    /// <summary>A simple glyf glyph: one closed contour of on-curve points, header box from the points.</summary>
    private static byte[] SimpleGlyph((short X, short Y)[] points)
    {
        var parts = new List<byte[]>
        {
            I16(1),
            I16(points.Min(p => p.X)), I16(points.Min(p => p.Y)), I16(points.Max(p => p.X)), I16(points.Max(p => p.Y)),
            U16(points.Length - 1), // endPtsOfContours[0]
            U16(0),                 // instructionLength
            points.Select(_ => (byte)0x01).ToArray(), // ON_CURVE; 16-bit signed deltas for x and y
        };
        short x = 0, y = 0;
        foreach (var p in points) { parts.Add(I16((short)(p.X - x))); x = p.X; }
        foreach (var p in points) { parts.Add(I16((short)(p.Y - y))); y = p.Y; }
        return Concat([.. parts]);
    }

    /// <summary>
    /// Roboto with glyph 'A' replaced: glyf rebuilt glyph by glyph, loca rewritten in the long
    /// format (and head.indexToLocFormat set to match).
    /// </summary>
    private static byte[] RobotoWithGlyphA(byte[] glyphData)
    {
        var roboto = TestFonts.RobotoRegularBytes();
        var face = NativeFontFace.Load(roboto);
        int target = face.GetGlyphIndex('A');
        var glyf = face.Tables.GetTable("glyf").Span;

        var newGlyf = new List<byte>();
        var loca = new byte[(face.Maxp.NumGlyphs + 1) * 4];
        for (int g = 0; g < face.Maxp.NumGlyphs; g++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(g * 4), (uint)newGlyf.Count);
            var (offset, length) = face.Loca!.GetGlyphRange(g);
            newGlyf.AddRange(g == target ? glyphData : glyf.Slice(offset, length).ToArray());
            while (newGlyf.Count % 4 != 0)
                newGlyf.Add(0);
        }

        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(face.Maxp.NumGlyphs * 4), (uint)newGlyf.Count);
        var head = face.Tables.GetTable("head").ToArray();
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1); // indexToLocFormat: long

        return SyntheticFonts.WithTables(roboto, new Dictionary<string, byte[]?>
        {
            ["glyf"] = [.. newGlyf],
            ["loca"] = loca,
            ["head"] = head,
        });
    }

    private static byte[] U16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        return buffer;
    }

    private static byte[] I16(short value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];
}
