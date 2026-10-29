using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="AntiAliasMode.None"/> glyph boxes fit the ink. The outline box rounded outwards
/// keeps every partially covered pixel, but thresholding at 50% leaves an edge row or column
/// empty whenever the outline covers under half of it; FreeType's mono boxes have no such rows.
/// </summary>
public class MonoGlyphBoxTests
{
    private static readonly Dictionary<string, float> Black = new() { ["wght"] = 900 };

    public static TheoryData<float, bool> RobotoSizes => new()
    {
        { 8f, false }, { 12f, false }, { 16f, false }, { 24f, false }, { 32f, false }, { 48f, false },
        { 8f, true }, { 12f, true }, { 16f, true }, { 24f, true }, { 32f, true }, { 48f, true },
    };

    [Theory]
    [InlineData('L')]
    [InlineData('T')]
    public void HeavyCff2Glyph_At16px_HasFreeTypesBox(char character)
    {
        // Source Code wght 900 at 16px: the outward box was 11 rows with row 0 empty, FreeType's 10.
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var options = new RasterOptions { Size = 16, AntiAlias = AntiAliasMode.None };

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), Black);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);
        native.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), Black);

        var expected = freeType.RasterizeGlyph(character, options)!;
        var actual = native.RasterizeGlyph(character, options)!;

        EmptyEdges(actual).ShouldBeEmpty();
        (actual.Metrics.BearingX, actual.Metrics.BearingY, actual.Width, actual.Height)
            .ShouldBe((expected.Metrics.BearingX, expected.Metrics.BearingY, expected.Width, expected.Height));
    }

    [Theory]
    [MemberData(nameof(RobotoSizes))]
    public void Roboto_HasNoEmptyEdgeRowsOrColumns(float size, bool hinting)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, EnableHinting = hinting };
        var failures = new List<string>();

        for (char c = '!'; c <= '~'; c++)
        {
            var glyph = native.RasterizeGlyph(c, options)!;
            string empty = EmptyEdges(glyph);
            if (empty.Length > 0)
                failures.Add($"'{c}' {glyph.Width}x{glyph.Height} empty {empty}");
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    [InlineData(16f)]
    [InlineData(24f)]
    [InlineData(32f)]
    [InlineData(48f)]
    public void Roboto_InkBounds_AreWithinOnePixelOfFreeTypeMono(float size)
    {
        // Unhinted: FreeType would run Roboto's own hinting instructions, Native its auto-hinter.
        // Below 10px FreeType's mono output drops dots that Native's dropout control keeps.
        var bytes = TestFonts.RobotoRegularBytes();
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, EnableHinting = false };
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);
        var failures = new List<string>();

        for (char c = '!'; c <= '~'; c++)
        {
            var expected = Ink(freeType.RasterizeGlyph(c, options)!);
            var actual = Ink(native.RasterizeGlyph(c, options)!);
            if (Math.Abs(actual.Left - expected.Left) > 1 || Math.Abs(actual.Top - expected.Top) > 1
                || Math.Abs(actual.Right - expected.Right) > 1 || Math.Abs(actual.Bottom - expected.Bottom) > 1)
            {
                failures.Add($"'{c}' native {actual} freetype {expected}");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(16f)]
    [InlineData(32f)]
    public void GetGlyphMetrics_MatchesRasterizeGlyph(float size)
    {
        var failures = new List<string>();
        foreach (var (name, bytes, axes) in new (string, byte[], Dictionary<string, float>?)[]
        {
            ("Roboto", TestFonts.RobotoRegularBytes(), null),
            ("Source Serif", TestFonts.SourceSerifOtfBytes(), null),
            ("Source Code wght 900", TestFonts.SourceCodeVariableOtfBytes(), Black),
        })
        {
            using IRasterizer native = new NativeRasterizer();
            native.LoadFont(bytes);
            if (axes is not null)
                native.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), axes);

            foreach (bool bold in new[] { false, true })
            {
                var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, Bold = bold, ForceSyntheticBold = bold };
                for (char c = '!'; c <= '~'; c++)
                {
                    var rendered = native.RasterizeGlyph(c, options)!;
                    var measured = native.GetGlyphMetrics(c, options)!.Value;
                    if (measured != rendered.Metrics)
                        failures.Add($"{name} bold={bold} '{c}': measured {measured}, rendered {rendered.Metrics}");
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(16f)]
    [InlineData(32f)]
    public void ColorLayers_HaveNoTransparentEdgeRowsOrColumns(float size)
    {
        // COLR version 0 (Bungee Color): the canvas is the union of the layer boxes, which
        // thresholding leaves with transparent edges just as it does a plain glyph's box.
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(TestFonts.BungeeColorBytes());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, ColorFont = true };
        var failures = new List<string>();

        for (char c = 'A'; c <= 'Z'; c++)
        {
            var glyph = native.RasterizeGlyph(c, options)!;
            glyph.Format.ShouldBe(PixelFormat.Rgba32, $"'{c}'");
            int left = glyph.Width, top = glyph.Height, right = -1, bottom = -1;
            for (int y = 0; y < glyph.Height; y++)
            {
                for (int x = 0; x < glyph.Width; x++)
                {
                    if (glyph.BitmapData[(y * glyph.Pitch) + (x * 4) + 3] == 0)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }

            if (left != 0 || top != 0 || right != glyph.Width - 1 || bottom != glyph.Height - 1)
                failures.Add($"'{c}' {glyph.Width}x{glyph.Height} ink x {left}-{right} y {top}-{bottom}");
            if (native.GetGlyphMetrics(c, options) != glyph.Metrics)
                failures.Add($"'{c}' GetGlyphMetrics {native.GetGlyphMetrics(c, options)} != {glyph.Metrics}");
        }

        failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(9f)]
    [InlineData(16f)]
    [InlineData(40f)]
    public void CroppedToInk_IsTheOutwardBoxRenderMinusEmptyEdges(float size)
    {
        // Nothing but empty rows and columns may go: every lit pixel, dropout control's included,
        // stays exactly where it was relative to the pen.
        foreach (char c in "!\"'(),-./:;<>@AOQTVWXY^_`gijlvwy{|}~")
        {
            var (edges, box) = GlyphEdges(c, size);
            var full = ScanlineRasterizer.Rasterize(edges, box.Width, box.Height, box.MinX, box.MinY, AntiAliasMode.None, box.BearingX, box.BearingY);
            var cropped = ScanlineRasterizer.RasterizeMono(edges, box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY);

            int left = cropped.BearingX - full.BearingX, top = full.BearingY - cropped.BearingY;
            left.ShouldBeGreaterThanOrEqualTo(0, $"'{c}'");
            top.ShouldBeGreaterThanOrEqualTo(0, $"'{c}'");
            for (int y = 0; y < full.Height; y++)
            {
                for (int x = 0; x < full.Width; x++)
                {
                    int cx = x - left, cy = y - top;
                    bool inside = cx >= 0 && cy >= 0 && cx < cropped.Width && cy < cropped.Height;
                    byte expected = full.Bitmap[(y * full.Width) + x];
                    byte actual = inside ? cropped.Bitmap[(cy * cropped.Width) + cx] : (byte)0;
                    actual.ShouldBe(expected, $"'{c}' pixel ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public void DropoutPixelInAnEdgeColumn_IsKept()
    {
        // A 0.3px hairline in the right half of column 0: under 50% everywhere, so only dropout
        // control lights column 0, and column 1 is empty. Rounding the outline's left edge
        // (0.6) to the nearest pixel would have cut the hairline off; cropping to ink keeps it.
        EdgeSegment[] edges =
        [
            new(0.6f, 0f, 0.9f, 0f),
            new(0.9f, 0f, 0.9f, 4f),
            new(0.9f, 4f, 0.6f, 4f),
            new(0.6f, 4f, 0.6f, 0f),
        ];

        var result = ScanlineRasterizer.RasterizeMono(edges, 2, 4, 0f, 0f, bearingX: 5, bearingY: 7);

        result.Width.ShouldBe(1);
        result.Height.ShouldBe(4);
        result.BearingX.ShouldBe(5);
        result.BearingY.ShouldBe(7);
        result.Bitmap.ShouldAllBe(v => v == 255);
    }

    [Fact]
    public void BlankRender_KeepsItsBox()
    {
        // Nothing to crop to: the box stays as given rather than collapsing to 0x0.
        var result = ScanlineRasterizer.RasterizeMono([], 3, 2, 0f, 0f, bearingX: 1, bearingY: 2);

        (result.Width, result.Height, result.BearingX, result.BearingY).ShouldBe((3, 2, 1, 2));
        result.Bitmap.ShouldAllBe(v => v == 0);
    }

    [Fact]
    public void RasterizeMono_AllocatesOnlyTheCroppedBitmap()
    {
        var (edges, box) = GlyphEdges('O', 96f);
        ScanlineRasterizer.RasterizeMono(edges, box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY); // warm the pools

        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = ScanlineRasterizer.RasterizeMono(edges, box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Bitmap.ShouldContain(v => v > 0);
        allocated.ShouldBeLessThanOrEqualTo((result.Width * result.Height) + 128, "scratch allocated per glyph");
    }

    private static (EdgeSegment[] Edges, GlyphBox Box) GlyphEdges(char character, float pixelSize)
    {
        var face = Internal.NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(character)));
        float scale = pixelSize / face.Head.UnitsPerEm;
        var box = GlyphBox.Compute(outline, null, scale);
        return (OutlineFlattener.Flatten(outline, new OutlineTransform(scale, 0f), includeHorizontal: true), box);
    }

    /// <summary>Ink bounds relative to the pen and baseline (x right, y up); right/bottom exclusive.</summary>
    private static (int Left, int Top, int Right, int Bottom) Ink(RasterizedGlyph glyph)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                if (glyph.BitmapData[(y * glyph.Pitch) + x] == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return left == int.MaxValue
            ? (0, 0, 0, 0)
            : (glyph.Metrics.BearingX + left, glyph.Metrics.BearingY - top, glyph.Metrics.BearingX + right, glyph.Metrics.BearingY - bottom);
    }

    /// <summary>Which edges of a non-blank bitmap hold no ink: "" when it fits its ink.</summary>
    private static string EmptyEdges(RasterizedGlyph glyph)
    {
        var (left, top, right, bottom) = Ink(glyph);
        if (left == right)
            return glyph.Width == 0 ? "" : "blank";

        string empty = "";
        if (left > glyph.Metrics.BearingX)
            empty += " left";
        if (top < glyph.Metrics.BearingY)
            empty += " top";
        if (right < glyph.Metrics.BearingX + glyph.Width)
            empty += " right";
        if (bottom > glyph.Metrics.BearingY - glyph.Height)
            empty += " bottom";
        return empty;
    }
}
