using KernSmith.Font;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// GPOS pair kerning of a variable font follows the selected instance: a ValueRecord's
/// XAdvDevice VariationIndex adds its GDEF ItemVariationStore delta (#291). Expected values are
/// fontTools' (<c>varLib.instancer</c>) for the same instance.
/// </summary>
public sealed class VariableKerningTests
{
    private static readonly string FixtureDir = Path.Join(AppContext.BaseDirectory, "Fixtures");

    private const string Sample = "ATVWYLPToaevy.,rf";

    // fontTools: every kerned pair among Sample's characters, summed over the 'kern' lookups of
    // varLib.instancer.instantiateVariableFont(font, location).
    private const string DancingScriptBold =
        "AA:-8 AL:12 AT:-32 AV:-20 AW:-20 AY:-26 Aa:-51 Ae:-51 Af:-51 Ao:-51 Ar:-51 Av:-51 Ay:-51 LL:15 LP:-9 LT:-11 Le:-12 Lo:-13 Lr:-32 Lv:-20 Ly:-21 P.:-80 PA:-13 PL:-19 PP:-10 PV:42 PW:42 PY:45 Pa:-66 Pe:-53 Pf:-13 Po:-66 Pr:-20 Pv:-21 Py:-31 T.:-44 TA:-64 TL:-51 TP:14 TT:38 TV:39 TW:39 TY:37 Ta:-105 Te:-103 Tf:-59 To:-110 Tr:-97 Tv:-67 Ty:-74 V.:-19 VL:-18 VP:-13 Va:-25 Ve:-23 Vf:-13 Vo:-26 Vr:-19 Vv:-19 Vy:-20 W.:-19 WL:-18 WP:-13 Wa:-25 We:-23 Wf:-13 Wo:-26 Wr:-19 Wv:-19 Wy:-20 Y.:-11 YA:-5 Ya:-20 Ye:-20 Yf:-11 Yo:-22 Yr:-22 Yv:-18 Yy:-19 aY:-26 eY:-24 oY:-39 rY:-30 vY:-29 yY:-26";

    private const string DancingScript533 =
        ".T:-6 .V:-7 .W:-7 .Y:-15 .r:-12 .v:-6 .y:-7 AA:-7 AL:11 AT:-29 AV:-18 AW:-18 AY:-24 Aa:-50 Ae:-50 Af:-50 Ao:-50 Ar:-50 Av:-50 Ay:-50 LL:7 LP:-8 LT:-10 Le:-14 Lo:-15 Lr:-38 Lv:-23 Ly:-24 P,:-42 P.:-76 PA:-17 PL:-18 PP:-10 PV:41 PW:41 PY:44 Pa:-72 Pe:-54 Pf:-13 Po:-63 Pr:-20 Pv:-26 Py:-34 T,:-22 T.:-40 TA:-56 TL:-50 TP:11 TT:39 TV:38 TW:38 TY:37 Ta:-94 Te:-93 Tf:-51 To:-97 Tr:-93 Tv:-59 Ty:-63 V,:-12 V.:-19 VL:-19 VP:-14 VT:-3 Va:-28 Ve:-26 Vf:-15 Vo:-29 Vr:-21 Vv:-22 Vy:-23 W,:-12 W.:-19 WL:-19 WP:-14 WT:-3 Wa:-28 We:-26 Wf:-15 Wo:-29 Wr:-21 Wv:-22 Wy:-23 Y,:-6 Y.:-11 YA:-5 YP:-3 Ya:-22 Ye:-22 Yf:-13 Yo:-24 Yr:-24 Yv:-21 Yy:-22 aY:-29 eV:-4 eW:-4 eY:-21 oY:-36 rY:-33 vY:-31 yY:-24";

    // wght 780, wdth 60, opsz 30, GRAD 50, slnt -4: five axes at once, each remapped by avar.
    private const string RobotoFlexFiveAxes =
        ",A:5 ,T:-69 ,V:-57 ,W:-44 ,Y:-80 ,e:-2 ,f:-3 ,o:-2 ,v:-42 ,y:-42 .A:5 .T:-69 .V:-57 .W:-44 .Y:-80 .e:-2 .f:-3 .o:-2 .v:-42 .y:-42 A,:5 A.:5 AA:1 AT:-106 AV:-41 AW:-65 AY:-43 Aa:6 Ae:-2 Af:-3 Ao:-9 Av:-44 Ay:-44 L,:4 L.:4 LA:15 LT:-216 LV:-48 LW:-114 LY:-62 La:1 Lf:1 Lv:-110 Ly:-110 P,:-90 P.:-90 PA:-48 PT:-2 PV:-4 PW:-5 PY:-12 Pa:-8 Pe:-6 Pf:1 Po:-7 T,:-170 T.:-170 TA:-76 TT:12 TV:8 TW:12 TY:7 Ta:-112 Te:-104 Tf:1 To:-104 Tr:-67 Tv:-69 Ty:-69 V,:-64 V.:-64 VA:-39 VL:-1 VP:-1 VT:6 VV:16 VW:12 VY:17 Va:-38 Ve:-28 Vf:-6 Vo:-36 Vr:-29 Vv:-7 Vy:-7 W,:-108 W.:-108 WA:-60 WL:-1 WP:-1 WT:11 WV:-1 WW:5 WY:8 Wa:-45 We:-40 Wf:-8 Wo:-40 Wr:-23 Wv:-7 Wy:-7 Y,:-83 Y.:-83 YA:-44 YL:-1 YP:-1 YT:7 YV:17 YW:10 YY:12 Ya:-143 Ye:-73 Yf:-42 Yo:-141 Yr:-65 Yv:-45 Yy:-45 av:-18 ay:-18 e,:3 e.:3 eA:7 eT:-16 eV:-7 eW:-4 eY:-14 er:1 ev:-13 ey:-13 f,:-4 f.:-4 fa:-2 fe:-18 ff:6 fv:17 fy:17 o,:-2 o.:-2 ov:-22 oy:-22 r,:-97 r.:-97 ra:-32 re:-13 rf:13 ro:-13 rv:11 ry:11 v,:-107 v.:-107 va:-29 ve:-19 vf:10 vo:-21 vr:1 vv:2 vy:2 y,:-107 y.:-107 ya:-29 ye:-19 yf:10 yo:-21 yr:1 yv:2 yy:2";

    [Fact]
    public void Generate_VariableFontKerning_FollowsTheSelectedInstance()
    {
        // Dancing Script kerns "To" by -86 units at wght 400 and -110 at wght 700 (1000 units per em).
        var font = File.ReadAllBytes(Path.Join(FixtureDir, "DancingScript-Variable.ttf"));
        int Kerning(float weight)
        {
            var options = new FontGeneratorOptions
            {
                Size = 100,
                Characters = CharacterSet.FromChars("To"),
                Kerning = true,
                VariationAxes = new Dictionary<string, float> { ["wght"] = weight },
            };
            return BmFont.Generate(font, options).Model.KerningPairs
                .Single(k => k.First == 'T' && k.Second == 'o').Amount;
        }

        var regular = Kerning(400);
        var bold = Kerning(700);

        ((double)bold / regular).ShouldBe(110.0 / 86.0, 0.07);
        bold.ShouldBeLessThan(regular);
    }

    [Fact]
    public void ReadFontInfo_VariableFont_ReportsDefaultInstanceKerning()
    {
        // ReadFontInfo takes no axes: its kerning is the default instance's (wght 400), in font
        // units -- "To" is -86 there (fontTools), -110 at wght 700. Documented on the method.
        var info = BmFont.ReadFontInfo(File.ReadAllBytes(Path.Join(FixtureDir, "DancingScript-Variable.ttf")));

        info.VariationAxes!.Single(a => a.Tag == "wght").DefaultValue.ShouldBe(400f);
        info.KerningPairs.Single(k => k.LeftCodepoint == 'T' && k.RightCodepoint == 'o').XAdvanceAdjustment.ShouldBe(-86);
    }

    [Fact]
    public void Generate_BackendWithoutVariableFonts_KeepsDefaultInstanceKerning()
    {
        // Such a backend draws the default instance whatever axes are asked for, so the
        // kerning has to stay there with the glyphs.
        Assert.SkipUnless(RasterizerFactory.IsRegistered(RasterizerBackend.StbTrueType), "StbTrueType is not available in this build.");
        var font = File.ReadAllBytes(Path.Join(FixtureDir, "DancingScript-Variable.ttf"));
        IReadOnlyList<KerningEntry> Kerning(Dictionary<string, float>? axes) =>
            BmFont.Generate(font, new FontGeneratorOptions
            {
                Size = 100,
                Characters = CharacterSet.FromChars(Sample),
                Kerning = true,
                Backend = RasterizerBackend.StbTrueType,
                VariationAxes = axes,
            }).Model.KerningPairs;

        var atDefault = Kerning(null);
        atDefault.ShouldNotBeEmpty();
        Kerning(new() { ["wght"] = 700 }).ShouldBe(atDefault);
    }

    [Fact]
    public void DancingScript_KerningAtWeight700_MatchesFontTools() =>
        Kerning("DancingScript-Variable.ttf", new() { ["wght"] = 700 }).ShouldBe(DancingScriptBold);

    [Fact]
    public void DancingScript_KerningBetweenMasters_MatchesFontTools() =>
        Kerning("DancingScript-Variable.ttf", new() { ["wght"] = 533 }).ShouldBe(DancingScript533);

    [Fact]
    public void RobotoFlex_KerningAcrossSeveralAxes_MatchesFontTools() =>
        Kerning("RobotoFlex-Variable.ttf", new() { ["wght"] = 780, ["wdth"] = 60, ["opsz"] = 30, ["GRAD"] = 50, ["slnt"] = -4 })
            .ShouldBe(RobotoFlexFiveAxes);

    [Theory]
    [InlineData("DancingScript-Variable.ttf")]
    [InlineData("RobotoFlex-Variable.ttf")]
    public void DefaultInstance_KerningIsUnchanged(string fontFile)
    {
        var font = File.ReadAllBytes(Path.Join(FixtureDir, fontFile));
        var axes = new TtfParser(font).VariationAxes!;
        var plain = new TtfFontReader().ReadFont(font).KerningPairs;
        plain.ShouldNotBeEmpty();

        // No coordinates, every axis at its default, an axis the font does not have, and NaN.
        var atDefaults = axes.ToDictionary(a => a.Tag, a => a.DefaultValue);
        new TtfFontReader { VariationCoordinates = new Dictionary<string, float>() }.ReadFont(font).KerningPairs.ShouldBe(plain);
        new TtfFontReader { VariationCoordinates = atDefaults }.ReadFont(font).KerningPairs.ShouldBe(plain);
        new TtfFontReader { VariationCoordinates = new Dictionary<string, float> { ["ZZZZ"] = 5, ["wght"] = float.NaN } }
            .ReadFont(font).KerningPairs.ShouldBe(plain);
    }

    [Fact]
    public void OutOfRangeWeight_ClampsToTheAxisEnd()
    {
        Kerning("DancingScript-Variable.ttf", new() { ["wght"] = 5000 }).ShouldBe(DancingScriptBold);
        Kerning("DancingScript-Variable.ttf", new() { ["wght"] = float.PositiveInfinity }).ShouldBe(DancingScriptBold);
    }

    [Theory]
    [InlineData("GDEF", 1)]
    [InlineData("GDEF", 2)]
    [InlineData("avar", 3)]
    [InlineData("fvar", 4)]
    public void DamagedVariationTables_NeverThrow(string tag, int seed)
    {
        // Truncations and random byte damage: a broken store, region list, delta row or
        // segment map means "no delta" (or a wrong one), never a crash.
        var rng = new Random(seed);
        var font = File.ReadAllBytes(Path.Join(FixtureDir, "DancingScript-Variable.ttf"));
        var table = GposEquivalenceTests.FindTable(font, tag);
        table.Length.ShouldBeGreaterThan(16);
        var requested = new HashSet<int>(Sample.Select(c => (int)c));
        var axes = new Dictionary<string, float> { ["wght"] = 650 };

        for (var trial = 0; trial < 150; trial++)
        {
            byte[] damaged;
            if (trial % 3 == 0)
            {
                damaged = table[..rng.Next(0, table.Length)];
            }
            else
            {
                damaged = (byte[])table.Clone();
                var edits = rng.Next(1, 6);
                for (var e = 0; e < edits; e++)
                {
                    // Mostly in the headers and offset arrays, where a wrong byte redirects a read.
                    var at = rng.Next(2) == 0 ? rng.Next(0, Math.Min(64, damaged.Length)) : rng.Next(0, damaged.Length);
                    damaged[at] = rng.Next(3) == 0 ? (byte)0xFF : (byte)rng.Next(256);
                }
            }

            var broken = GposEquivalenceTests.WithTable(font, tag, damaged);
            Should.NotThrow(() => new TtfParser(broken, 0, requested, variationCoordinates: axes), $"{tag} seed {seed} trial {trial}");
        }
    }

    [Fact]
    public void DamagedDeviceOffsets_NeverThrow()
    {
        // GPOS damage lands on device offsets and VariationIndex tables among everything else.
        var rng = new Random(7);
        var font = File.ReadAllBytes(Path.Join(FixtureDir, "DancingScript-Variable.ttf"));
        var gpos = GposEquivalenceTests.FindTable(font, "GPOS");
        var requested = new HashSet<int>(Sample.Select(c => (int)c));
        var axes = new Dictionary<string, float> { ["wght"] = 650 };

        for (var trial = 0; trial < 150; trial++)
        {
            var damaged = (byte[])gpos.Clone();
            var edits = rng.Next(1, 40);
            for (var e = 0; e < edits; e++)
                damaged[rng.Next(0, damaged.Length)] = rng.Next(2) == 0 ? (byte)0xFF : (byte)rng.Next(256);

            var broken = GposEquivalenceTests.WithTable(font, "GPOS", damaged);
            Should.NotThrow(() => new TtfParser(broken, 0, requested, variationCoordinates: axes), $"trial {trial}");
        }
    }

    /// <summary>The kerned pairs among <see cref="Sample"/>'s characters, as "LR:units", sorted ordinally.</summary>
    private static string Kerning(string fontFile, Dictionary<string, float> axes)
    {
        var font = File.ReadAllBytes(Path.Join(FixtureDir, fontFile));
        var pairs = new TtfFontReader { VariationCoordinates = axes }.ReadFont(font).KerningPairs
            .Where(p => p.LeftCodepoint < 128 && p.RightCodepoint < 128
                        && Sample.Contains((char)p.LeftCodepoint) && Sample.Contains((char)p.RightCodepoint))
            .Select(p => $"{(char)p.LeftCodepoint}{(char)p.RightCodepoint}:{p.XAdvanceAdjustment}")
            .Order(StringComparer.Ordinal);
        return string.Join(' ', pairs);
    }
}
