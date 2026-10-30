using KernSmith.Font;
using Shouldly;
using static KernSmith.Tests.Font.Woff2TestSupport;

namespace KernSmith.Tests.Font;

/// <summary>
/// A WOFF2 wrapping a CFF (PostScript-outline) font: WOFF2 transforms only <c>glyf</c>/<c>loca</c>
/// and <c>hmtx</c>, so the <c>CFF </c> table must come back byte for byte and every backend must
/// render the decoded font exactly like the source <c>.otf</c>. The fixture is a Latin subset of
/// Noto Sans' CFF build, compressed by fontTools (see FONT-LICENSES.md).
/// </summary>
[Collection("RasterizerFactory")]
public class Woff2CffTests
{
    private const string Otf = "NotoSans-Regular-Latin-CFF.otf";
    private const string Woff2File = "NotoSans-Regular-Latin-CFF.woff2";
    private const string TestText = "AWTVaeogy.,!?0123ÀÉÑçöÅÿ";

    [Fact]
    public void Decompress_CffWoff2_EveryTableMatchesTheOtf()
    {
        var expected = ReadTables(Fixture(Otf));
        var actual = ReadTables(WoffDecompressor.Decompress(Fixture(Woff2File)));

        expected.ShouldContainKey("CFF ");
        actual.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        foreach (var (tag, otfBytes) in expected)
        {
            var expectedBytes = otfBytes;
            var actualBytes = actual[tag];
            if (tag == "head")
            {
                // checkSumAdjustment covers the whole file, which legitimately differs; and the
                // encoder sets head.flags bit 11 ("font data is lossless-transformed") on every
                // WOFF2 it writes, even one whose only outline table is stored as is.
                actualBytes = (byte[])actualBytes.Clone();
                expectedBytes = (byte[])otfBytes.Clone();
                expectedBytes.AsSpan(8, 4).CopyTo(actualBytes.AsSpan(8));
                expectedBytes[16] |= 0x08;
            }

            actualBytes.ShouldBe(expectedBytes, $"table '{tag}' should round-trip byte for byte");
        }
    }

    public static TheoryData<RasterizerBackend> Backends()
    {
        var data = new TheoryData<RasterizerBackend> { RasterizerBackend.FreeType, RasterizerBackend.StbTrueType };
#if WINDOWS
        data.Add(RasterizerBackend.Gdi);
#endif
#if DIRECTWRITE
        data.Add(RasterizerBackend.DirectWrite);
#endif
        return data;
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void Generate_FromCffWoff2_MatchesTheOtf(RasterizerBackend backend)
    {
        var options = new FontGeneratorOptions
        {
            Size = 28,
            Characters = CharacterSet.FromChars(TestText),
            Backend = backend,
            Kerning = true,
        };

        var fromOtf = BmFont.Generate(Fixture(Otf), options);
        var fromWoff2 = BmFont.Generate(Fixture(Woff2File), options);

        fromOtf.Model.Characters.Count.ShouldBeGreaterThan(TestText.Length - 2, $"{backend} should render the test text");
        fromOtf.Pages[0].PixelData.Any(b => b != 0).ShouldBeTrue($"{backend} rendered a blank atlas from the OTF");

        fromWoff2.FntText.ShouldBe(fromOtf.FntText, $"{backend}: .fnt should match the OTF");
        fromWoff2.Pages.Count.ShouldBe(fromOtf.Pages.Count);
        for (var i = 0; i < fromOtf.Pages.Count; i++)
            fromWoff2.Pages[i].PixelData.ShouldBe(fromOtf.Pages[i].PixelData, $"{backend}: page {i} pixels");
    }
}
