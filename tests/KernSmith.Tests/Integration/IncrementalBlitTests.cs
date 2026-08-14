using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Phase 183 — the documented <see cref="AddedGlyph"/> blit contract: manually
/// compositing added glyph bytes at <c>(X + Padding.Left, Y + Padding.Up)</c> onto an
/// existing page reproduces exactly what <see cref="AtlasBuilder"/> would produce for the
/// union set with the same placements (asserted via placement injection, not a full
/// regenerate — a regenerate would repack and move everything).
/// </summary>
[Collection("RasterizerFactory")]
public class IncrementalBlitTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // Asymmetric non-zero padding and non-zero spacing: an offset bug (swapped Left/Up,
    // padding applied twice, padding omitted) cannot produce byte-equal pages.
    private static FontGeneratorOptions Options() => new()
    {
        Size = 32,
        Padding = new Padding(1, 2, 3, 4), // Up=1, Right=2, Down=3, Left=4
        Spacing = new Spacing(2, 3),
        AutofitTexture = false,
        // Pin the page geometry: a 256-wide page holds "ASDF" on one shelf with plenty of
        // free width left, so adding "QW" is guaranteed to fit the same single page.
        SizeConstraints = new AtlasSizeConstraints { FixedWidth = 256, ForcePowerOfTwo = true },
    };

    private sealed record Scenario(
        BmFontResult Generated,
        AtlasPage Page,
        GlyphAdditionResult Addition,
        Padding Padding);

    /// <summary>
    /// Generate("ASDF") on one fixed page, then ResumeIncremental + AddGlyphs("QW") with the
    /// Throw overflow policy — any growth would silently invalidate the page-buffer
    /// comparison, so it must fail loudly instead.
    /// </summary>
    private static Scenario Run()
    {
        var fontData = LoadTestFont();
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var generated = BmFont.Generate(fontData, genOptions);

        generated.Pages.Count.ShouldBe(1);
        var page = generated.Pages[0];
        page.Format.ShouldBe(PixelFormat.Grayscale8);

        using var session = BmFont.ResumeIncremental(
            fontData, Options(), generated.Model, AdditionOverflowPolicy.Throw);
        var addition = session.AddGlyphs("QW");

        addition.Added.Count.ShouldBe(2);
        addition.PageGrown.ShouldBeFalse();
        addition.PageCount.ShouldBe(1);
        addition.PageWidth.ShouldBe(page.Width);
        addition.PageHeight.ShouldBe(page.Height);

        // Guard against a vacuous comparison: the added glyphs must carry real coverage.
        foreach (var g in addition.Added)
            g.RawPixels.ShouldContain(b => b != 0);

        return new Scenario(generated, page, addition, Options().Padding);
    }

    /// <summary>The documented contract: RawPixels rows at (X + Padding.Left, Y + Padding.Up), honoring Pitch.</summary>
    private static byte[] BlitRawOntoGrayscalePage(Scenario s)
    {
        var actual = (byte[])s.Page.PixelData.Clone();
        foreach (var g in s.Addition.Added)
        {
            var destX = g.X + s.Padding.Left;
            var destY = g.Y + s.Padding.Up;
            for (var row = 0; row < g.Height; row++)
            {
                Array.Copy(
                    g.RawPixels, row * g.Pitch,
                    actual, (destY + row) * s.Page.Width + destX,
                    g.Width);
            }
        }
        return actual;
    }

    [Fact]
    public void ManualRawPixelsBlit_ReproducesAtlasBuilderPageForUnionPlacements()
    {
        var s = Run();

        var actual = BlitRawOntoGrayscalePage(s);
        actual.ShouldNotBe(s.Page.PixelData); // the blit must have changed something

        // Expected page: AtlasBuilder over the union glyph list with the original ASDF
        // placements plus the session's QW placements injected verbatim.
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var asdfGlyphs = BmFont.RasterizeFont(LoadTestFont(), genOptions).Glyphs;

        var unionGlyphs = new List<RasterizedGlyph>(asdfGlyphs);
        foreach (var g in s.Addition.Added)
        {
            unionGlyphs.Add(new RasterizedGlyph
            {
                Codepoint = g.Codepoint,
                GlyphIndex = 0,
                BitmapData = g.RawPixels,
                Width = g.Width,
                Height = g.Height,
                Pitch = g.Pitch,
                Metrics = new GlyphMetrics(0, 0, 0, g.Width, g.Height),
                Format = g.RawFormat,
            });
        }

        var placements = new List<GlyphPlacement>();
        foreach (var c in s.Generated.Model.Characters)
            placements.Add(new GlyphPlacement(c.Id, c.Page, c.X, c.Y));
        foreach (var g in s.Addition.Added)
            placements.Add(new GlyphPlacement(g.Codepoint, g.PageIndex, g.X, g.Y));

        var packResult = new PackResult
        {
            Placements = placements,
            PageCount = 1,
            PageWidth = s.Page.Width,
            PageHeight = s.Page.Height,
        };

        var expected = AtlasBuilder.Build(unionGlyphs, packResult, s.Padding, new StbPngEncoder());
        expected.Count.ShouldBe(1);
        expected[0].Format.ShouldBe(PixelFormat.Grayscale8);

        actual.ShouldBe(expected[0].PixelData);
    }

    [Fact]
    public void ManualRgbaPixelsBlit_EqualsPromotionOfTheGrayscaleBlit()
    {
        var s = Run();

        // Path A: blit RawPixels on the grayscale page, then promote the whole page to
        // RGBA the way AtlasPage.GetRgbaPixelData does: (255, 255, 255, coverage).
        var gray = BlitRawOntoGrayscalePage(s);
        var promoted = new byte[gray.Length * 4];
        for (var i = 0; i < gray.Length; i++)
        {
            promoted[i * 4] = 255;
            promoted[i * 4 + 1] = 255;
            promoted[i * 4 + 2] = 255;
            promoted[i * 4 + 3] = gray[i];
        }

        // Path B: promote the page first, then blit the lazy RGBA Pixels (tightly packed,
        // Width * 4 bytes per row) at the same documented offset.
        var rgba = s.Page.GetRgbaPixelData();
        foreach (var g in s.Addition.Added)
        {
            var destX = g.X + s.Padding.Left;
            var destY = g.Y + s.Padding.Up;
            var rowBytes = g.Width * 4;
            for (var row = 0; row < g.Height; row++)
            {
                Array.Copy(
                    g.Pixels, row * rowBytes,
                    rgba, ((destY + row) * s.Page.Width + destX) * 4,
                    rowBytes);
            }
        }

        rgba.ShouldBe(promoted);
    }
}
