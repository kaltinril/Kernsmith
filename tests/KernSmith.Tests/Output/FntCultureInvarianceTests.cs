using System.Globalization;
using KernSmith.Output;
using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// The text and XML <c>.fnt</c> formats are machine-readable files read on other machines (and
/// by engines that expect ASCII digits, '-' and '.'), so they are written and read in the
/// invariant culture whatever the thread's culture: sv-SE formats negatives with U+2212 and
/// de-DE writes decimals with a comma.
/// </summary>
public sealed class FntCultureInvarianceTests
{
    public static TheoryData<string> Cultures() => new() { "sv-SE", "de-DE", "fa-IR", "ar-SA" };

    private static BmFontModel CreateModel() => new()
    {
        Info = new InfoBlock("TestFont", 32, false, false, true, true, false, 100, "", 1,
            new Padding(0, 0, 0, 0), new Spacing(1, 1), Outline: 2),
        Common = new CommonBlock(38, 30, 256, 256, 1, false),
        Pages = new[] { new PageEntry(0, "TestFont_0.png") },
        Characters = new[]
        {
            new CharEntry(65, 10, 20, 16, 24, -1, -2, 18, 0, 15),
            new CharEntry(86, 30, 20, 16, 24, 1, 3, 15, 0, 15),
        },
        KerningPairs = new[] { new KerningEntry(65, 86, -2), new KerningEntry(86, 65, -12) },
        Extended = new ExtendedMetadata
        {
            GeneratorVersion = "1.0.0",
            OutlineThickness = 1.5f,
            ShadowOffsetX = -3,
            ShadowOffsetY = 2,
            AdvanceAdjustY = -0.25f,
            VariationAxes = new Dictionary<string, float> { ["wght"] = 432.5f, ["wdth"] = -12.75f },
        },
    };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Formatters_UnderCulture_WriteInvariantText(string culture)
    {
        var model = CreateModel();
        var (invariantText, invariantXml) = InCulture(CultureInfo.InvariantCulture,
            () => (new TextFormatter().FormatText(model), new XmlFormatter().FormatText(model)));
        invariantText.ShouldContain("yoffset=-2", Case.Sensitive);
        invariantText.ShouldContain("advanceAdjustY=-0.25", Case.Sensitive);
        invariantText.ShouldContain("axis_wght=432.5", Case.Sensitive);

        var (text, xml) = InCulture(new CultureInfo(culture),
            () => (new TextFormatter().FormatText(model), new XmlFormatter().FormatText(model)));

        text.ShouldBe(invariantText);
        xml.ShouldBe(invariantXml);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Reader_UnderCulture_ReadsInvariantText(string culture)
    {
        var model = CreateModel();
        var text = InCulture(CultureInfo.InvariantCulture, () => new TextFormatter().FormatText(model));
        var xml = InCulture(CultureInfo.InvariantCulture, () => new XmlFormatter().FormatText(model));

        var (fromText, fromXml) = InCulture(new CultureInfo(culture),
            () => (BmFontReader.ReadText(text), BmFontReader.ReadXml(xml)));

        foreach (var read in new[] { fromText, fromXml })
        {
            read.Characters.ShouldBe(model.Characters);
            read.KerningPairs.ShouldBe(model.KerningPairs);
            read.Extended.ShouldNotBeNull();
            read.Extended.ShadowOffsetX.ShouldBe(-3);
            read.Extended.AdvanceAdjustY.ShouldBe(-0.25f);
            read.Extended.VariationAxes.ShouldNotBeNull();
            read.Extended.VariationAxes["wdth"].ShouldBe(-12.75f);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Generate_UnderCulture_RoundTripsThroughReaderElsewhere(string culture)
    {
        var font = File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.StbTrueType,
            AdvanceAdjustY = 1.5f,
        };

        var (model, text, xml) = InCulture(new CultureInfo(culture), () =>
        {
            var result = BmFont.Generate(font, options);
            return (result.Model, result.ToString(), result.ToXml());
        });
        model.KerningPairs.ShouldContain(k => k.Amount < 0);
        model.Characters.ShouldContain(c => c.YOffset < 0 || c.XOffset < 0);

        // Another machine, another culture: the file must read back exactly.
        var (fromText, fromXml) = InCulture(CultureInfo.InvariantCulture,
            () => (BmFontReader.ReadText(text), BmFontReader.ReadXml(xml)));
        foreach (var read in new[] { fromText, fromXml })
        {
            read.Characters.ShouldBe(model.Characters);
            read.KerningPairs.ShouldBe(model.KerningPairs);
            read.Extended.ShouldNotBeNull();
            read.Extended.AdvanceAdjustY.ShouldBe(1.5f);
        }
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
