using KernSmith.Font;
using KernSmith.Font.Woff2;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;
using Stb = StbTrueTypeSharp.StbTrueType;

namespace KernSmith.Tests.Rasterizer.StbStructure;

/// <summary>
/// No false rejections: every font fixture the test suite uses (and Arial and Georgia, when the
/// machine has them) passes the StbTrueType backend's structure check font-wide and glyph by
/// glyph, and the backend's output for them is exactly what stb_truetype renders unchecked.
/// </summary>
public class FontStructureConformanceTests
{
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        string root = Path.Join(AppContext.BaseDirectory, "Fixtures");
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                 || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                                 || p.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                                 || p.EndsWith(".woff", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(root, path));
        return data;
    }

    [Fact]
    public void FixtureList_CoversTrueTypeCffAndWoff2()
    {
        var names = Fixtures().Select(row => row.Data).ToList();
        names.ShouldContain("Roboto-Regular.ttf");
        names.ShouldContain("SourceSerif4-Regular.otf");
        names.ShouldContain("NotoSans-Regular-Latin-CFF.woff2");
        names.Count.ShouldBeGreaterThanOrEqualTo(15);
    }

    /// <summary>
    /// Every fixture passes, except a font with no outlines stb_truetype can read (CFF2, or
    /// bitmap-only color): stb_truetype itself refuses those at <c>stbtt_InitFont</c>, and the
    /// check refuses exactly those, naming the missing table.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public unsafe void Fixture_PassesEveryCheck(string fixture)
    {
        byte[] font = Load(Path.Join(AppContext.BaseDirectory, "Fixtures", fixture));
        bool stbCanRead;
        fixed (byte* data = font)
        {
            using var info = new Stb.stbtt_fontinfo();
            stbCanRead = Stb.stbtt_InitFont(info, data, 0) != 0;
        }

        if (stbCanRead)
        {
            AssertConforms(font);
        }
        else
        {
            var ex = Should.Throw<FontParsingException>(() => FontStructureValidator.Validate(font, 0));
            ex.TableTag.ShouldBe("CFF ");
            ex.Message.ShouldContain("neither glyf nor CFF outlines", Case.Sensitive);
        }
    }

    [Fact]
    public void OnlyFontsWithoutReadableOutlines_AreRefused()
    {
        var refused = Fixtures().Select(row => row.Data)
            .Where(f => !new SfntProbe(Load(Path.Join(AppContext.BaseDirectory, "Fixtures", f))).HasOutlinesStbReads)
            .ToList();

        refused.ShouldBe(["NotoColorEmoji.ttf", "SourceCodeVF-Upright.otf"], ignoreOrder: true);
    }

    private readonly struct SfntProbe(byte[] font)
    {
        public bool HasOutlinesStbReads => Has("glyf") || Has("CFF ");

        private bool Has(string tag)
        {
            int numTables = (font[4] << 8) | font[5];
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + (16 * i);
                if (font[rec] == tag[0] && font[rec + 1] == tag[1] && font[rec + 2] == tag[2] && font[rec + 3] == tag[3])
                    return true;
            }

            return false;
        }
    }

    [Theory]
    [InlineData("arial.ttf")]
    [InlineData("georgia.ttf")]
    public void SystemFont_PassesEveryCheck(string file)
    {
        string path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
        Assert.SkipUnless(File.Exists(path), $"{file} is not installed on this machine.");
        AssertConforms(File.ReadAllBytes(path));
    }

    private static void AssertConforms(byte[] font)
    {
        var validator = FontStructureValidator.Validate(font, 0);
        for (int g = 0; g < validator.NumGlyphs; g++)
            validator.EnsureGlyph(g);
    }

    private static byte[] Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ? Woff2Decoder.Decompress(bytes)
            : path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ? WoffDecompressor.Decompress(bytes)
            : bytes;
    }

    // ------------------------------------------------------------ output unchanged

    public static TheoryData<string, bool> OutlineFixtures() => new()
    {
        { "Roboto-Regular.ttf", false },
        { "Roboto-Regular.ttf", true },
        { "NotoSans-Regular-Latin.ttf", false },
        { "DancingScript-Variable.ttf", false },
        { "SourceSerif4-Regular.otf", false },
        { "SourceSerif4-Regular.otf", true },
        { "NotoSans-Regular-Latin-CFF.otf", false },
    };

    /// <summary>
    /// The backend checks structure and resolves glyph indices before calling stb_truetype; the
    /// bitmaps, offsets and advances it returns must equal the unchecked codepoint-API calls
    /// it made before the check existed.
    /// </summary>
    [Theory]
    [MemberData(nameof(OutlineFixtures))]
    public unsafe void BackendOutput_EqualsUncheckedStbTrueType(string fixture, bool sdf)
    {
        byte[] font = File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Fixtures", fixture));
        using var rasterizer = new StbTrueTypeRasterizer();
        rasterizer.LoadFont(font);
        var options = new RasterOptions { Size = 32, Sdf = sdf, SdfSpread = 8 };

        fixed (byte* data = font)
        {
            using var info = new Stb.stbtt_fontinfo();
            Stb.stbtt_InitFont(info, data, 0).ShouldBe(1);
            float scale = Stb.stbtt_ScaleForMappingEmToPixels(info, 32);
            int compared = 0;
            for (int cp = 0x20; cp <= 0x7E; cp++)
            {
                var glyph = rasterizer.RasterizeGlyph(cp, options);
                if (Stb.stbtt_FindGlyphIndex(info, cp) == 0)
                {
                    glyph.ShouldBeNull();
                    continue;
                }

                glyph.ShouldNotBeNull();
                int advance, lsb;
                Stb.stbtt_GetCodepointHMetrics(info, cp, &advance, &lsb);
                glyph.Metrics.Advance.ShouldBe((int)Math.Round(advance * scale));

                int w, h, xoff, yoff;
                byte* bitmap = sdf
                    ? Stb.stbtt_GetCodepointSDF(info, scale, cp, 8, 128, 16f, &w, &h, &xoff, &yoff)
                    : Stb.stbtt_GetCodepointBitmap(info, scale, scale, cp, &w, &h, &xoff, &yoff);
                try
                {
                    byte[] expected = bitmap == null || w == 0 || h == 0 ? [] : new ReadOnlySpan<byte>(bitmap, w * h).ToArray();
                    glyph.BitmapData.ShouldBe(expected, $"U+{cp:X4}");
                    if (expected.Length > 0)
                    {
                        glyph.Width.ShouldBe(w);
                        glyph.Metrics.BearingX.ShouldBe(xoff);
                        glyph.Metrics.BearingY.ShouldBe(-yoff);
                        compared++;
                    }
                }
                finally
                {
                    if (bitmap != null)
                        Stb.stbtt_FreeBitmap(bitmap, null);
                }
            }

            compared.ShouldBeGreaterThan(80);
        }
    }
}
