using System.Globalization;
using Shouldly;

namespace KernSmith.Tests.Config;

/// <summary>
/// <c>.bmfc</c> and <c>.hiero</c> configs are read back with the invariant culture (and on
/// other machines), so they must be written in it too: sv-SE formats a negative integer with
/// U+2212, which the reader's invariant <c>int.Parse</c> rejects.
/// </summary>
public sealed class ConfigCultureInvarianceTests
{
    public static TheoryData<string> Cultures() => new() { "sv-SE", "de-DE", "fa-IR", "ar-SA" };

    private static BmfcConfig CreateConfig() => new()
    {
        FontName = "Arial",
        Options = new FontGeneratorOptions
        {
            Size = 24,
            MatchCharHeight = true,
            ShadowOffsetX = -2,
            ShadowOffsetY = -3,
            Characters = CharacterSet.FromChars("AB"),
        },
    };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void BmfcWriter_UnderCulture_WritesInvariantTextThatReadsBack(string culture)
    {
        var invariant = InCulture(CultureInfo.InvariantCulture, () => BmfcConfigWriter.Write(CreateConfig()));
        invariant.ShouldContain("fontSize=-24", Case.Sensitive);
        invariant.ShouldContain("shadowOffsetX=-2", Case.Sensitive);

        var written = InCulture(new CultureInfo(culture), () => BmfcConfigWriter.Write(CreateConfig()));

        written.ShouldBe(invariant);
        var read = BmfcConfigReader.Parse(written).Options;
        read.MatchCharHeight.ShouldBeTrue();
        read.Size.ShouldBe(24);
        read.ShadowOffsetX.ShouldBe(-2);
        read.ShadowOffsetY.ShouldBe(-3);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void HieroWriter_UnderCulture_WritesInvariantText(string culture)
    {
        var invariant = InCulture(CultureInfo.InvariantCulture, () => HieroConfigWriter.Write(CreateConfig()));

        var written = InCulture(new CultureInfo(culture), () => HieroConfigWriter.Write(CreateConfig()));

        written.ShouldBe(invariant);
    }

    private static T InCulture<T>(CultureInfo culture, Func<T> work)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return work();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
