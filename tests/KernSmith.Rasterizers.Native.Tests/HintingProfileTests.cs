using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Hinting;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="HintingProfile"/>: what a face tells the <c>Normal</c> hinter — bytecode presence
/// by FreeType's rule, the integer-ppem flag, <c>hdmx</c>, <c>gasp</c> and the x-height ratio —
/// on the fixtures and on the Windows fonts that carry all of it.
/// </summary>
public class HintingProfileTests
{
    private static HintingProfile Fixture(string file) =>
        HintingProfile.For(NativeFontFace.Load(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(TestFonts.RobotoRegularPath)!, file))));

    [Fact]
    public void Roboto_IsAnAutohintFace_DespiteBitThree()
    {
        // Roboto sets head.flags bit 3 but has no fpgm, prep or glyph instructions: FreeType
        // auto-hints it at the fractional ppem, and so does Normal. No hdmx, no gasp: nothing
        // in this work touches it (NativeOutputCharacterizationTests pins its bytes).
        var roboto = Fixture("Roboto-Regular.ttf");

        roboto.HasBytecode.ShouldBeFalse();
        roboto.IntegerPpemPreferred.ShouldBeTrue();
        roboto.PrefersIntegerPpem.ShouldBeFalse();
        roboto.Hdmx.ShouldBeNull();
        roboto.Gasp.ShouldBeNull();
        roboto.HintPpem(9.1f).ShouldBe(9.1f);
        roboto.HdmxAdvance(9, 1).ShouldBeNull();
        roboto.GridFitsAt(6f).ShouldBeTrue();
        roboto.XHeightRoundUp(HintTuning.Default).ShouldBe(HintTuning.Default.XHeightRoundUp);
        roboto.XHeightRatio.ShouldBe(1082f / 2048f, 0.0001f);
    }

    [Fact]
    public void NotoSans_HasBytecode_ButNoIntegerPpemFlag()
    {
        // ttfautohint instructions without bit 3: FreeType's interpreter runs at the fractional ppem.
        var noto = Fixture("NotoSans-Regular-Latin.ttf");

        noto.HasBytecode.ShouldBeTrue();
        noto.IntegerPpemPreferred.ShouldBeFalse();
        noto.PrefersIntegerPpem.ShouldBeFalse();
        noto.HintPpem(8.8f).ShouldBe(8.8f);
        noto.Gasp.ShouldNotBeNull();
        noto.GridFitsAt(6f).ShouldBeTrue();
    }

    [Fact]
    public void CffFace_HasNoBytecode()
    {
        var serif = HintingProfile.For(NativeFontFace.Load(TestFonts.SourceSerifOtfBytes()));

        serif.HasBytecode.ShouldBeFalse();
        serif.PrefersIntegerPpem.ShouldBeFalse();
    }

    [Fact]
    public void PrepOnly_CountsAsBytecode_ByFreeTypesRule()
    {
        // Dancing Script has a prep table and nothing else; FreeType sends it to the TrueType
        // interpreter rather than the auto-hinter, so it counts, but without bit 3 it stays at
        // the fractional ppem. Roboto Flex has prep and bit 3, so it is fitted at round(ppem).
        var dancing = Fixture("DancingScript-Variable.ttf");
        dancing.HasBytecode.ShouldBeTrue();
        dancing.IntegerPpemPreferred.ShouldBeFalse();
        dancing.PrefersIntegerPpem.ShouldBeFalse();

        var flex = Fixture("RobotoFlex-Variable.ttf");
        flex.HasBytecode.ShouldBeTrue();
        flex.IntegerPpemPreferred.ShouldBeTrue();
        flex.PrefersIntegerPpem.ShouldBeTrue();
        flex.HintPpem(9.03f).ShouldBe(9f);
        flex.Hdmx.ShouldBeNull();
    }

    [Fact]
    public void Autohint_TellsTheHinterNothing()
    {
        var none = HintingProfile.Autohint;

        none.HasBytecode.ShouldBeFalse();
        none.PrefersIntegerPpem.ShouldBeFalse();
        none.HintPpem(11.52f).ShouldBe(11.52f);
        none.HdmxAdvance(12, 0).ShouldBeNull();
        none.GridFitsAt(8f).ShouldBeTrue();
        none.XHeightRoundUp(HintTuning.Default).ShouldBe(2f / 3f);
    }

    [Fact]
    public void IntegerPpem_RoundsHalfUp_AndNeverBelowOne()
    {
        var profile = HintingProfile.From(hasBytecode: true, integerPpemPreferred: true);

        profile.HintPpem(11.52f).ShouldBe(12f);
        profile.HintPpem(11.49f).ShouldBe(11f);
        profile.HintPpem(11.5f).ShouldBe(12f);
        profile.HintPpem(0.2f).ShouldBe(1f);
        profile.XHeightRoundUp(HintTuning.Default).ShouldBe(0.5f);
        HintingProfile.From(hasBytecode: true, integerPpemPreferred: false).HintPpem(11.52f).ShouldBe(11.52f);
        HintingProfile.From(hasBytecode: false, integerPpemPreferred: true).HintPpem(11.52f).ShouldBe(11.52f);
    }

    public static TheoryData<string, bool, float> WindowsFonts => new()
    {
        { "arial.ttf", true, 1062f / 2048f },
        { "verdana.ttf", false, 1117f / 2048f },
        { "tahoma.ttf", true, 1117f / 2048f },
        { "georgia.ttf", true, 986f / 2048f },
    };

    [Theory]
    [MemberData(nameof(WindowsFonts))]
    public void WindowsFont_IsABytecodeFace_WithGaspAndMaybeHdmx(string file, bool hasHdmx, float xHeightRatio)
    {
        var face = NativeFontFace.Load(NativeBytecodeFontTests.SystemFont(file));
        var profile = HintingProfile.For(face);

        profile.HasBytecode.ShouldBeTrue();
        profile.IntegerPpemPreferred.ShouldBeTrue();
        profile.PrefersIntegerPpem.ShouldBeTrue();
        profile.HintPpem(11.52f).ShouldBe(12f);
        profile.XHeightRatio.ShouldBe(xHeightRatio, 0.0001f);
        profile.XHeightRoundUp(HintTuning.Default).ShouldBe(0.5f);

        // The Microsoft gasp shape: nothing grid-fitted at 8 ppem and under.
        profile.Gasp.ShouldNotBeNull();
        profile.GridFitsAt(7.68f).ShouldBeFalse();
        profile.GridFitsAt(8f).ShouldBeFalse();
        profile.GridFitsAt(8.64f).ShouldBeTrue();
        profile.GridFitsAt(12f).ShouldBeTrue();
        profile.GridFitsAt(20f).ShouldBeTrue();

        int a = face.GetGlyphIndex('a');
        if (hasHdmx)
        {
            profile.Hdmx.ShouldNotBeNull();
            profile.HdmxAdvance(12, a).ShouldNotBeNull();
            profile.HdmxAdvance(12, face.Maxp.NumGlyphs).ShouldBeNull();
        }
        else
        {
            profile.Hdmx.ShouldBeNull();
            profile.HdmxAdvance(12, a).ShouldBeNull();
        }
    }

    [Fact]
    public void HdmxAdvance_IsOnlyOfferedToAnIntegerPpemFace()
    {
        // A record without bit 3 (or without bytecode) would describe advances at a ppem the
        // glyph is not drawn at, so the profile withholds it.
        var face = NativeFontFace.Load(NativeBytecodeFontTests.SystemFont("arial.ttf"));
        var hdmx = HintingProfile.For(face).Hdmx!;

        HintingProfile.From(true, true, hdmx).HdmxAdvance(12, face.GetGlyphIndex('a')).ShouldNotBeNull();
        HintingProfile.From(true, false, hdmx).HdmxAdvance(12, face.GetGlyphIndex('a')).ShouldBeNull();
        HintingProfile.From(false, true, hdmx).HdmxAdvance(12, face.GetGlyphIndex('a')).ShouldBeNull();
    }
}
