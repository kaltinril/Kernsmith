using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticStyleTransformTests;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 168 — condensed/extended, small caps and a font-derived italic angle as outline
/// transforms.
/// </summary>
public class ShapeTransformTests
{
    // ---- Width scale -------------------------------------------------------------------------

    [Fact]
    public void WidthScale_ScalesXOnly_AndTheAdvance()
    {
        var glyph = new DecodedGlyph(Circle(500, 500, 400, clockwise: true), 1000f, new OutlineBounds(100, 100, 900, 900));

        var condensed = new WidthScaleTransform(0.5f).Apply(glyph);

        var bounds = OutlineBounds.Tight(condensed.Outline);
        bounds.XMin.ShouldBe(50f, 0.01f);
        bounds.XMax.ShouldBe(450f, 0.01f);
        bounds.YMin.ShouldBe(100f, 0.01f);
        bounds.YMax.ShouldBe(900f, 0.01f);
        condensed.AdvanceWidth.ShouldBe(500f);
        condensed.DeclaredBounds.ShouldBeNull();
    }

    [Fact]
    public void WidthScale_ScalesTheAdvanceOfABlankGlyph()
    {
        new WidthScaleTransform(1.5f).Apply(new DecodedGlyph(GlyphOutline.Empty, 400f)).AdvanceWidth.ShouldBe(600f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void WidthScale_RejectsANonPositiveScale(float scale)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new WidthScaleTransform(scale));
    }

    // ---- Small caps --------------------------------------------------------------------------

    [Fact]
    public void SmallCaps_ScaleIsXHeightOverCapHeight()
    {
        // Roboto OS/2 v4: sxHeight 1082, sCapHeight 1456 (read independently from the file).
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());

        SmallCapsTransform.ScaleFor(face.Os2).ShouldBe(1082f / 1456f);
    }

    [Theory]
    [InlineData(0, 1456)]
    [InlineData(1082, 0)]
    public void SmallCaps_HasNoScale_WithoutBothHeights(short xHeight, short capHeight)
    {
        SmallCapsTransform.ScaleFor(new Os2Table(1, 0, 0, 0, 0, xHeight, capHeight)).ShouldBeNull();
    }

    [Fact]
    public void SmallCaps_ScalesUniformly_FromThePenOrigin()
    {
        // A capital-height box lands on the x-height, still sitting on the baseline.
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        float scale = SmallCapsTransform.ScaleFor(face.Os2)!.Value;
        var glyph = new DecodedGlyph(Square(100, 0, 1100, 1456, clockwise: true), 1300f);

        var small = new SmallCapsTransform(scale).Apply(glyph);

        var bounds = OutlineBounds.Tight(small.Outline);
        bounds.YMin.ShouldBe(0f);
        bounds.YMax.ShouldBe(1082f, 0.01f);
        bounds.XMin.ShouldBe(100f * scale, 0.01f);
        bounds.XMax.ShouldBe(1100f * scale, 0.01f);
        small.AdvanceWidth.ShouldBe(1300f * scale, 0.01f);
    }

    // ---- Italic angle ------------------------------------------------------------------------

    [Fact]
    public void ItalicAngle_NegativeLeansRight()
    {
        ItalicTransform.FromItalicAngle(-12f).Shear.ShouldBe(0.21256f, 0.0001f);
        ItalicTransform.FromItalicAngle(-12f).Shear.ShouldBe(ItalicTransform.DefaultShear, 0.0001f);
        ItalicTransform.FromItalicAngle(0f).Shear.ShouldBe(0f);
        ItalicTransform.FromItalicAngle(8f).Shear.ShouldBeLessThan(0f);
    }

    [Fact]
    public void ItalicAngle_ShearsByItsTangent()
    {
        var glyph = new DecodedGlyph(Square(0, 0, 100, 1000, clockwise: true), 200f);

        var slanted = ItalicTransform.FromItalicAngle(-20f).Apply(glyph);

        OutlineBounds.Tight(slanted.Outline).XMax.ShouldBe(100f + (1000f * MathF.Tan(20f * MathF.PI / 180f)), 0.01f);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(75f)]
    [InlineData(-90f)]
    public void ItalicAngle_RejectsDegenerateAngles(float degrees)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ItalicTransform.FromItalicAngle(degrees));
    }

    // ---- Composition -------------------------------------------------------------------------

    [Fact]
    public void Transforms_ComposeInThePhase168Order()
    {
        // Condensed -> small caps -> bold -> italic -> stroke: each later transform sees the
        // earlier ones' geometry and advance.
        var glyph = new DecodedGlyph(Square(0, 0, 1000, 1000, clockwise: true), 1000f);
        IGlyphTransform[] chain =
        [
            new WidthScaleTransform(0.5f),
            new SmallCapsTransform(0.5f),
            new BoldTransform(100f),
            ItalicTransform.FromItalicAngle(0f),
            new StrokeTransform(20f, StrokeJoin.Miter, OutlineStroker.DefaultMiterLimit, 0.5f),
        ];

        foreach (var transform in chain)
            glyph = transform.Apply(glyph);

        // 1000 -> 500 wide -> 250x500 -> +100 -> 350x600 -> stroke ±10.
        OutlineBounds.Tight(glyph.Outline).ShouldBe(new OutlineBounds(-10, -10, 360, 610));
        glyph.AdvanceWidth.ShouldBe(350f);
    }
}
