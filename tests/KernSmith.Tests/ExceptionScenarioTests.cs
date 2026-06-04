using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Scenario tests that exercise the KernSmith exception hierarchy: each exception type carries
/// its message, wraps inner exceptions, exposes its scenario-specific properties, and is thrown
/// from real generation paths where expected.
/// </summary>
[Collection("RasterizerFactory")]
public class ExceptionScenarioTests
{
    // == Hierarchy ==========================================================

    [Fact]
    public void AllKernSmithExceptions_DeriveFromBmFontException()
    {
        new FontParsingException("x").ShouldBeAssignableTo<BmFontException>();
        new RasterizationException("x").ShouldBeAssignableTo<BmFontException>();
        new AtlasPackingException("x").ShouldBeAssignableTo<BmFontException>();
        new BmFontException("x").ShouldBeAssignableTo<Exception>();
    }

    // == BmFontException ====================================================

    [Fact]
    public void BmFontException_PreservesMessageAndInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new BmFontException("boom", inner);

        ex.Message.ShouldBe("boom");
        ex.InnerException.ShouldBeSameAs(inner);
    }

    // == FontParsingException ===============================================

    [Fact]
    public void FontParsingException_TableConstructor_SetsTableTagAndOffset()
    {
        var ex = new FontParsingException("GPOS", 128, "bad offset");

        ex.TableTag.ShouldBe("GPOS");
        ex.Offset.ShouldBe(128);
        ex.Message.ShouldContain("GPOS");
        ex.Message.ShouldContain("128");
    }

    [Fact]
    public void FontParsingException_MessageConstructor_LeavesTableTagNull()
    {
        var ex = new FontParsingException("plain");

        ex.TableTag.ShouldBeNull();
        ex.Offset.ShouldBeNull();
    }

    [Fact]
    public void Generate_WithGarbageBytes_ThrowsFontParsingException()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        Should.Throw<FontParsingException>(() => BmFont.Generate(garbage, 32));
    }

    [Fact]
    public void GenerateFromSystem_WithUnknownFamily_ThrowsFontParsingException()
    {
        var unknown = "NoSuchFontFamily_" + Guid.NewGuid().ToString("N");

        Should.Throw<FontParsingException>(() => BmFont.GenerateFromSystem(unknown, 32));
    }

    // == RasterizationException =============================================

    [Fact]
    public void RasterizationException_CodepointConstructor_SetsCodepointAndFormatsHex()
    {
        var ex = new RasterizationException(0x0041, "no glyph");

        ex.Codepoint.ShouldBe(0x0041);
        ex.Message.ShouldContain("U+0041");
    }

    [Fact]
    public void RasterizationException_MessageConstructor_LeavesCodepointNull()
    {
        new RasterizationException("plain").Codepoint.ShouldBeNull();
    }

    // == AtlasPackingException ==============================================

    [Fact]
    public void AtlasPackingException_GlyphConstructor_SetsDimensionsAndMaxSize()
    {
        var ex = new AtlasPackingException(64, 80, 32);

        ex.GlyphWidth.ShouldBe(64);
        ex.GlyphHeight.ShouldBe(80);
        ex.MaxTextureSize.ShouldBe(32);
        ex.Message.ShouldContain("64x80");
    }

    [Fact]
    public void AtlasPackingException_MessageConstructor_LeavesDimensionsNull()
    {
        var ex = new AtlasPackingException("plain");

        ex.GlyphWidth.ShouldBeNull();
        ex.GlyphHeight.ShouldBeNull();
        ex.MaxTextureSize.ShouldBeNull();
    }
}

/// <summary>
/// Smoke tests for <see cref="DefaultSystemFontProvider"/>. System font discovery is
/// platform-dependent (and may find nothing in CI), so these only assert robust, non-throwing
/// behavior rather than specific fonts being present.
/// </summary>
public class DefaultSystemFontProviderTests
{
    [Fact]
    public void GetInstalledFonts_DoesNotThrow_AndReturnsNonNull()
    {
        var provider = new DefaultSystemFontProvider();

        var fonts = provider.GetInstalledFonts();

        fonts.ShouldNotBeNull();
    }

    [Fact]
    public void GetInstalledFonts_IsCached_ReturnsSameInstance()
    {
        var provider = new DefaultSystemFontProvider();

        var first = provider.GetInstalledFonts();
        var second = provider.GetInstalledFonts();

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void LoadFont_UnknownFamily_ReturnsNull()
    {
        var provider = new DefaultSystemFontProvider();
        var unknown = "NoSuchFontFamily_" + Guid.NewGuid().ToString("N");

        var result = provider.LoadFont(unknown);

        result.ShouldBeNull();
    }
}
