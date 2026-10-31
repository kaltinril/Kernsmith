using KernSmith.Output;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// Every backend renders at <c>Size × Dpi / 72</c> pixels per em, so everything core derives from
/// font units — kerning amounts and the table-derived line metrics — must scale with Dpi too, or
/// glyphs and their spacing disagree as soon as Dpi is not 72.
/// </summary>
[Collection("RasterizerFactory")]
public class DpiMetricsTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontResult Generate(RasterizerBackend backend, int dpi) =>
        BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 96,
            Dpi = dpi,
            Characters = CharacterSet.FromChars("AVTo"),
            Backend = backend,
        });

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
    public void Kerning_ScalesWithDpiLikeTheGlyphs(RasterizerBackend backend)
    {
        var at72 = Generate(backend, 72).Model;
        var at144 = Generate(backend, 144).Model;

        // The glyphs do double...
        var a72 = at72.Characters.Single(c => c.Id == 'A').XAdvance;
        var a144 = at144.Characters.Single(c => c.Id == 'A').XAdvance;
        a144.ShouldBeInRange(2 * a72 - 1, 2 * a72 + 1);

        // ...so the kerning between them must too.
        var to72 = at72.KerningPairs.Single(k => k.First == 'T' && k.Second == 'o').Amount;
        var to144 = at144.KerningPairs.Single(k => k.First == 'T' && k.Second == 'o').Amount;
        to72.ShouldBeLessThan(-1);
        to144.ShouldBeInRange(2 * to72 - 1, 2 * to72 + 1);
    }

    [Fact]
    public void LineMetrics_FromFontTables_ScaleWithDpi()
    {
        // FreeType reports no font metrics of its own, so core computes lineHeight and base
        // from the OS/2 table.
        var at144 = Generate(RasterizerBackend.FreeType, 144).Model;
        var at72 = Generate(RasterizerBackend.FreeType, 72).Model;

        // Each is a ceiling of its own exact value, so doubling can differ by one.
        at144.Common.LineHeight.ShouldBeInRange(2 * at72.Common.LineHeight - 1, 2 * at72.Common.LineHeight);
        at144.Common.Base.ShouldBeInRange(2 * at72.Common.Base - 1, 2 * at72.Common.Base);

        // The baseline sits where the glyphs put it: 'A' stands on it at both resolutions.
        var a72 = at72.Characters.Single(c => c.Id == 'A');
        var a144 = at144.Characters.Single(c => c.Id == 'A');
        (a144.YOffset + a144.Height).ShouldBeInRange(at144.Common.Base - 1, at144.Common.Base + 1);
        (a72.YOffset + a72.Height).ShouldBeInRange(at72.Common.Base - 1, at72.Common.Base + 1);
    }
}
