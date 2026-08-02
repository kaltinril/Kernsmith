using System.Globalization;
using System.Text;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 165 — proves the native backend's metrics agree with an established backend rather
/// than merely being self-consistent.
/// </summary>
/// <remarks>
/// Glyph metrics are compared against FreeType with hinting disabled: the native rasterizer has
/// no hinter, so a hinted FreeType would legitimately disagree by a pixel or two at small sizes
/// and the comparison would measure hinting, not correctness. Font metrics are compared through
/// the generated .fnt, because FreeType returns null from <c>GetFontMetrics</c> and defers to the
/// shared OS/2 calculation — there is no rasterizer-level value on its side to compare against.
/// </remarks>
public class NativeMetricsAgreementTests
{
    /// <summary>A spread of straight, curved, diagonal, descending and punctuation glyphs.</summary>
    private const string GlyphSample =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?'\"()[]{}<>@#$%&*-+=/\\|~^";

    public static TheoryData<float> Sizes => [12f, 16f, 24f, 32f, 48f, 96f];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void GlyphMetrics_MatchFreeType_WithinOnePixel(float size)
    {
        var options = new RasterOptions { Size = size, Dpi = 72, EnableHinting = false };
        var bytes = TestFonts.RobotoRegularBytes();

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var failures = new StringBuilder();

        foreach (char character in GlyphSample)
        {
            var expected = freeType.GetGlyphMetrics(character, options);
            if (expected is null)
                continue;

            var actual = native.GetGlyphMetrics(character, options);
            if (actual is null)
            {
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': native returned null\n");
                continue;
            }

            Compare(failures, character, "advance", expected.Value.Advance, actual.Value.Advance);
            Compare(failures, character, "bearingX", expected.Value.BearingX, actual.Value.BearingX);
            Compare(failures, character, "bearingY", expected.Value.BearingY, actual.Value.BearingY);
        }

        failures.ToString().ShouldBeEmpty($"glyph metrics disagree with FreeType at {size}px");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void RasterizedGlyphMetrics_MatchFreeType_WithinOnePixel(float size)
    {
        var options = new RasterOptions { Size = size, Dpi = 72, EnableHinting = false };
        var bytes = TestFonts.RobotoRegularBytes();

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var failures = new StringBuilder();

        foreach (char character in GlyphSample)
        {
            var expected = freeType.RasterizeGlyph(character, options);
            if (expected is null)
                continue;

            var actual = native.RasterizeGlyph(character, options);
            if (actual is null)
            {
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': native returned null\n");
                continue;
            }

            Compare(failures, character, "width", expected.Width, actual.Width);
            Compare(failures, character, "height", expected.Height, actual.Height);
            Compare(failures, character, "advance", expected.Metrics.Advance, actual.Metrics.Advance);

            // Bearings are only meaningful once there is a bitmap to place.
            if (expected.Width == 0 || actual.Width == 0)
                continue;

            Compare(failures, character, "bearingX", expected.Metrics.BearingX, actual.Metrics.BearingX);
            Compare(failures, character, "bearingY", expected.Metrics.BearingY, actual.Metrics.BearingY);
        }

        failures.ToString().ShouldBeEmpty($"rasterized metrics disagree with FreeType at {size}px");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void FontMetrics_MatchFreeType_WithinOnePixel(float size)
    {
        // FreeType returns null from GetFontMetrics, so its ascent/lineHeight are whatever the
        // shared OS/2 calculation produces. Comparing the generated .fnt is therefore the only
        // apples-to-apples font-metric comparison against it — and it is the one that matters,
        // since it is what consumers of the font actually see.
        var bytes = TestFonts.RobotoRegularBytes();

        static FontGeneratorOptions Options(RasterizerBackend backend, float size) => new()
        {
            Size = size,
            Characters = CharacterSet.Ascii,
            Backend = backend
        };

        var native = BmFontReader.ReadText(
            BmFont.Generate(bytes, Options(RasterizerBackend.Native, size)).FntText).Common;
        var freeType = BmFontReader.ReadText(
            BmFont.Generate(bytes, Options(RasterizerBackend.FreeType, size)).FntText).Common;

        Math.Abs(native.Base - freeType.Base).ShouldBeLessThanOrEqualTo(1,
            $"base at {size}px: FreeType {freeType.Base}, native {native.Base}");
        Math.Abs(native.LineHeight - freeType.LineHeight).ShouldBeLessThanOrEqualTo(1,
            $"lineHeight at {size}px: FreeType {freeType.LineHeight}, native {native.LineHeight}");
    }

    [Fact]
    public void FontMetrics_AreProportionalToSize()
    {
        // Guards the comparison above from passing on degenerate values: metrics must actually
        // scale, so a backend returning zeros (or a fixed number) cannot slip through.
        var bytes = TestFonts.RobotoRegularBytes();
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var small = native.GetFontMetrics(new RasterOptions { Size = 16 });
        var large = native.GetFontMetrics(new RasterOptions { Size = 64 });

        small.ShouldNotBeNull();
        large.ShouldNotBeNull();
        small!.Ascent.ShouldBeGreaterThan(0);
        large!.Ascent.ShouldBeGreaterThan(small.Ascent * 3);
        large.LineHeight.ShouldBeGreaterThan(large.Ascent);
    }

    private static void Compare(StringBuilder failures, char character, string field, int expected, int actual)
    {
        if (Math.Abs(expected - actual) <= 1)
            return;

        failures.Append(CultureInfo.InvariantCulture,
            $"'{character}' {field}: expected {expected}, got {actual}\n");
    }
}
