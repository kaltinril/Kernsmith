using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 173 — the low-pass filters that turn 3x horizontal coverage into LCD subpixel values.
/// </summary>
public class LcdFilterTests
{
    public static TheoryData<string> Filters => ["PixelBox", "Triangle", "Identity"];

    private static LcdFilterKernel ByName(string name) => name switch
    {
        "PixelBox" => LcdFilterKernel.PixelBox,
        "Triangle" => LcdFilterKernel.Triangle,
        "Identity" => LcdFilterKernel.Identity,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public void Taps_SumToTheDenominator_SoTheFilterConservesEnergy(string name)
    {
        var filter = ByName(name);
        filter.Weights.Sum().ShouldBe(filter.Denominator);
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void Taps_AreSymmetric_SoTheFilterDoesNotShiftTheGlyph(string name)
    {
        var weights = ByName(name).Weights;
        weights.Count.ShouldBe((2 * ByName(name).Radius) + 1);
        for (int i = 0; i < weights.Count; i++)
            weights[i].ShouldBe(weights[weights.Count - 1 - i]);
    }

    [Fact]
    public void Kernels_AreTheDocumentedOnes()
    {
        LcdFilterKernel.PixelBox.Weights.ShouldBe([1, 1, 1]);
        LcdFilterKernel.PixelBox.Denominator.ShouldBe(3);
        LcdFilterKernel.Triangle.Weights.ShouldBe([1, 2, 3, 2, 1]);
        LcdFilterKernel.Triangle.Denominator.ShouldBe(9);
        LcdFilterKernel.Identity.Weights.ShouldBe([1]);
        LcdFilterKernel.Identity.Denominator.ShouldBe(1);
        LcdFilterKernel.Identity.Radius.ShouldBe(0);
    }

    [Theory]
    [InlineData(LcdFilter.Default, "PixelBox")]
    [InlineData(LcdFilter.Light, "PixelBox")]
    [InlineData(LcdFilter.Smooth, "Triangle")]
    [InlineData(LcdFilter.None, "Identity")]
    public void PublicPresets_MapToTheirKernels(LcdFilter filter, string kernel)
    {
        // Default is Native's own choice: the pixel box, which keeps LCD output unchanged from
        // before the option existed and matches the FreeType backend per channel.
        LcdFilterKernel.For(filter).ShouldBeSameAs(ByName(kernel));
    }

    [Fact]
    public void For_RejectsAnUndefinedPreset()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LcdFilterKernel.For((LcdFilter)99));
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void Sample_LeavesAUniformRunUnchanged(string name)
    {
        var filter = ByName(name);
        byte[] row = Enumerable.Repeat((byte)173, 15).ToArray();

        filter.Sample(row, 7).ShouldBe((byte)173);
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void Sample_OfAnImpulse_ReproducesTheKernel(string name)
    {
        var filter = ByName(name);
        var row = new byte[11];
        row[5] = 255;

        for (int i = 0; i < row.Length; i++)
        {
            int offset = i - 5;
            int expected = Math.Abs(offset) <= filter.Radius
                ? (int)Math.Round(255.0 * filter.Weights[offset + filter.Radius] / filter.Denominator, MidpointRounding.AwayFromZero)
                : 0;
            filter.Sample(row, i).ShouldBe((byte)expected, $"subpixel {i}");
        }
    }

    [Fact]
    public void Sample_ReadsZeroOutsideTheRow()
    {
        byte[] row = [255, 255, 255];

        // Index -1 and 3 sit just outside; only one real neighbour contributes to each.
        LcdFilterKernel.PixelBox.Sample(row, -1).ShouldBe((byte)85);
        LcdFilterKernel.PixelBox.Sample(row, 3).ShouldBe((byte)85);
        LcdFilterKernel.PixelBox.Sample(row, 0).ShouldBe((byte)170);
        LcdFilterKernel.PixelBox.Sample(row, -2).ShouldBe((byte)0);
    }

    [Fact]
    public void Constructor_RejectsAnEvenKernelOrANonPositiveDenominator()
    {
        Should.Throw<ArgumentException>(() => new LcdFilterKernel([1, 1], 2));
        Should.Throw<ArgumentOutOfRangeException>(() => new LcdFilterKernel([1, 1, 1], 0));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.2f)]
    [InlineData(0.37f)]
    [InlineData(0.5f)]
    public void PixelBox_EqualsThreeThirdPixelShiftedRenders(float shift)
    {
        // The "Harmony" description of LCD rendering: render the glyph three times at whole-pixel
        // resolution with the outline moved +1/3, 0 and -1/3 px, and take each render as one
        // channel. The coverage of a one-pixel window is the sum of its three one-third windows,
        // so the pixel-box filter over 3x coverage must give the same numbers.
        var outline = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(1.3f + shift, 0),
            OutlineCommand.Line(6.85f + shift, 0),
            OutlineCommand.Line(4.1f + shift, 5),
            OutlineCommand.CloseAt(1.3f + shift, 0),
        ]);
        var toPixels = new OutlineTransform(1f, 0f);
        const int width = 10, height = 6, top = -6;

        var edges = OutlineFlattener.Flatten(outline, toPixels);
        var tripled = edges.Select(e => new EdgeSegment(e.X0 * 3f, e.Y0, e.X1 * 3f, e.Y1)).ToArray();
        var subpixels = ScanlineRasterizer.Rasterize(tripled, width * 3, height, 0f, top).Bitmap;

        for (int c = 0; c < 3; c++)
        {
            // Channel c samples the window [p + (c - 1)/3, p + 1 + (c - 1)/3]: the outline
            // moved by (1 - c)/3 px, rendered at whole-pixel resolution.
            float move = (1 - c) / 3f;
            var moved = edges.Select(e => new EdgeSegment(e.X0 + move, e.Y0, e.X1 + move, e.Y1)).ToArray();
            var channel = ScanlineRasterizer.Rasterize(moved, width, height, 0f, top).Bitmap;

            for (int y = 0; y < height; y++)
            {
                var row = subpixels.AsSpan(y * width * 3, width * 3);
                for (int p = 0; p < width; p++)
                {
                    int filtered = LcdFilterKernel.PixelBox.Sample(row, (3 * p) + c);
                    int direct = channel[(y * width) + p];
                    Math.Abs(filtered - direct).ShouldBeLessThanOrEqualTo(1, $"pixel ({p},{y}) channel {c}");
                }
            }
        }
    }
}
