using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 162 — <c>loca</c>/<c>glyf</c> parsing and the <c>maxp</c> v1.0 profile fields.
/// </summary>
public class GlyphTableTests
{
    private static NativeFontFace LoadRoboto() => NativeFontFace.Load(TestFonts.RobotoRegularBytes());

    // ---------------------------------------------------------------- maxp

    [Fact]
    public void Maxp_ExtendedProfileFieldsArePopulated()
    {
        var face = LoadRoboto();

        // Roboto is a v1.0 TrueType maxp, so the extended profile is present and non-trivial.
        face.Maxp.MaxPoints.ShouldBeGreaterThan((ushort)0);
        face.Maxp.MaxContours.ShouldBeGreaterThan((ushort)0);
        face.Maxp.MaxCompositePoints.ShouldBeGreaterThan((ushort)0);
        face.Maxp.MaxCompositeContours.ShouldBeGreaterThan((ushort)0);
        face.Maxp.MaxComponentElements.ShouldBeGreaterThan((ushort)0);
        face.Maxp.MaxComponentDepth.ShouldBeGreaterThan((ushort)0);
    }

    [Fact]
    public void Maxp_ComponentDepthLimitIsCappedAtSixtyFour()
    {
        // A font declaring an absurd depth is clamped to the hard cap.
        var maxp = new MaxpTable(100, 0, 0, 0, 0, 0, 9999);
        maxp.ComponentDepthLimit.ShouldBe(64);

        // A font declaring nothing (v0.5, or a zeroed field) still gets a usable limit.
        var v05 = new MaxpTable(100, 0, 0, 0, 0, 0, 0);
        v05.ComponentDepthLimit.ShouldBe(64);

        // A sane declared depth is honoured verbatim.
        var sane = new MaxpTable(100, 0, 0, 0, 0, 0, 5);
        sane.ComponentDepthLimit.ShouldBe(5);
    }

    [Fact]
    public void Maxp_VersionZeroPointFive_LeavesExtendedFieldsZero()
    {
        // version 0.5 (0x00005000) + numGlyphs, and nothing else.
        byte[] data = [0x00, 0x00, 0x50, 0x00, 0x01, 0x2C];

        var maxp = MaxpTable.Parse(data);

        maxp.NumGlyphs.ShouldBe((ushort)300);
        maxp.MaxPoints.ShouldBe((ushort)0);
        maxp.MaxComponentDepth.ShouldBe((ushort)0);
    }

    // ---------------------------------------------------------------- loca

    [Fact]
    public void Loca_ShortFormat_OffsetsAreDoubled()
    {
        // Short loca stores offset/2: entries 0, 16, 16, 40 => byte offsets 0, 32, 32, 80.
        byte[] data = Bytes(U16(0), U16(16), U16(16), U16(40));

        var loca = LocaTable.Parse(data, numGlyphs: 3, longFormat: false);

        loca.GlyphCount.ShouldBe(3);
        loca.GetGlyphRange(0).ShouldBe((0, 32));
        loca.GetGlyphRange(1).ShouldBe((32, 0));   // empty glyph
        loca.GetGlyphRange(2).ShouldBe((32, 48));
    }

    [Fact]
    public void Loca_LongFormat_OffsetsAreVerbatim()
    {
        byte[] data = Bytes(U32(0), U32(32), U32(32), U32(80));

        var loca = LocaTable.Parse(data, numGlyphs: 3, longFormat: true);

        loca.GlyphCount.ShouldBe(3);
        loca.GetGlyphRange(0).ShouldBe((0, 32));
        loca.GetGlyphRange(1).ShouldBe((32, 0));
        loca.GetGlyphRange(2).ShouldBe((32, 48));
    }

    [Fact]
    public void Loca_DecreasingOffsets_Throw()
    {
        byte[] data = Bytes(U32(0), U32(64), U32(32));

        Should.Throw<FontFormatException>(() => LocaTable.Parse(data, numGlyphs: 2, longFormat: true));
    }

    [Fact]
    public void Loca_OffsetBeyondIntRange_ThrowsFontFormatException()
    {
        // A hostile font can declare a long-format offset above int.MaxValue. Offsets are
        // narrowed to int to slice the glyf table, so this must be rejected at parse time
        // rather than wrapping to a negative offset that slips past the later range check.
        byte[] data = Bytes(U32(0), U32(0x8000_0000));

        Should.Throw<FontFormatException>(() => LocaTable.Parse(data, numGlyphs: 1, longFormat: true));
    }

    [Fact]
    public void Glyf_GlyphRangeBeyondTable_ThrowsFontFormatException()
    {
        // GetGlyph documents FontFormatException for malformed data. An offset past the end
        // of glyf must surface as that, not as a raw slicing ArgumentOutOfRangeException.
        var loca = LocaTable.Parse(Bytes(U32(0), U32(4096)), numGlyphs: 1, longFormat: true);
        var glyf = new GlyfTable(new byte[64], loca, maxComponentDepth: 4);

        Should.Throw<FontFormatException>(() => glyf.GetGlyph(0));
    }

    [Fact]
    public void Loca_GlyphIndexOutOfRange_Throws()
    {
        var face = LoadRoboto();
        var loca = face.Loca;
        loca.ShouldNotBeNull();

        Should.Throw<ArgumentOutOfRangeException>(() => loca!.GetGlyphRange(loca.GlyphCount));
        Should.Throw<ArgumentOutOfRangeException>(() => loca!.GetGlyphRange(-1));
    }

    [Fact]
    public void Loca_CoversEveryGlyphInMaxp()
    {
        var face = LoadRoboto();
        var loca = face.Loca;
        loca.ShouldNotBeNull();

        loca!.GlyphCount.ShouldBe(face.Maxp.NumGlyphs);
    }

    // ---------------------------------------------------------------- glyf (real font)

    [Fact]
    public void Glyf_CapitalA_HasContoursAndPoints()
    {
        var face = LoadRoboto();

        var glyph = face.GetGlyph(face.GetGlyphIndex('A'));

        glyph.IsComposite.ShouldBeFalse();
        glyph.IsEmpty.ShouldBeFalse();
        // 'A' is a triangle-with-crossbar outline plus the counter: at least 2 contours.
        glyph.Contours.Length.ShouldBeGreaterThanOrEqualTo(2);
        glyph.PointCount.ShouldBeGreaterThan(4);
        glyph.Contours.ShouldAllBe(c => c.Points.Length >= 3);
    }

    [Fact]
    public void Glyf_CapitalA_BoundingBoxAgreesWithPoints()
    {
        var face = LoadRoboto();

        var glyph = face.GetGlyph(face.GetGlyphIndex('A'));

        glyph.XMin.ShouldBeLessThan(glyph.XMax);
        glyph.YMin.ShouldBeLessThan(glyph.YMax);

        // Every parsed point must sit inside the glyph's declared box, which in turn
        // must sit inside the font's global box from head.
        foreach (var contour in glyph.Contours)
        {
            foreach (var point in contour.Points)
            {
                point.X.ShouldBeInRange(glyph.XMin, glyph.XMax);
                point.Y.ShouldBeInRange(glyph.YMin, glyph.YMax);
            }
        }

        glyph.XMin.ShouldBeGreaterThanOrEqualTo(face.Head.XMin);
        glyph.YMin.ShouldBeGreaterThanOrEqualTo(face.Head.YMin);
        glyph.XMax.ShouldBeLessThanOrEqualTo(face.Head.XMax);
        glyph.YMax.ShouldBeLessThanOrEqualTo(face.Head.YMax);
    }

    [Fact]
    public void Glyf_Space_IsEmptyNotAnError()
    {
        var face = LoadRoboto();

        var glyph = face.GetGlyph(face.GetGlyphIndex(' '));

        glyph.IsEmpty.ShouldBeTrue();
        glyph.Contours.ShouldBeEmpty();
        glyph.PointCount.ShouldBe(0);
    }

    [Fact]
    public void Glyf_LowercaseO_HasOffCurvePoints()
    {
        var face = LoadRoboto();

        var glyph = face.GetGlyph(face.GetGlyphIndex('o'));

        // A round glyph is built from quadratic curves, so off-curve control points exist.
        glyph.Contours.SelectMany(c => c.Points).ShouldContain(p => !p.OnCurve);
    }

    [Fact]
    public void Glyf_NoTwoConsecutiveOffCurvePointsRemain()
    {
        var face = LoadRoboto();

        // Implicit on-curve midpoints must be materialised for every glyph in the font,
        // so no contour may contain two adjacent off-curve points (including the wrap).
        for (int glyphIndex = 0; glyphIndex < face.Maxp.NumGlyphs; glyphIndex++)
        {
            var glyph = face.GetGlyph(glyphIndex);
            foreach (var contour in glyph.Contours)
            {
                var points = contour.Points;
                for (int i = 0; i < points.Length; i++)
                {
                    var current = points[i];
                    var next = points[(i + 1) % points.Length];
                    if (!current.OnCurve && !next.OnCurve)
                        throw new Xunit.Sdk.XunitException(
                            $"Glyph {glyphIndex} has consecutive off-curve points at index {i}.");
                }
            }
        }
    }

    [Fact]
    public void Glyf_AllGlyphsParseWithinMaxpLimits()
    {
        var face = LoadRoboto();

        for (int glyphIndex = 0; glyphIndex < face.Maxp.NumGlyphs; glyphIndex++)
        {
            var glyph = face.GetGlyph(glyphIndex);

            // Midpoint insertion can add points beyond the stored count, so compare
            // contours (never synthesised) against the declared maxima.
            int contourLimit = glyph.IsComposite ? face.Maxp.MaxCompositeContours : face.Maxp.MaxContours;
            glyph.Contours.Length.ShouldBeLessThanOrEqualTo(contourLimit);
        }
    }

    [Fact]
    public void Glyf_AccentedGlyphIsCompositeAndAssembled()
    {
        var face = LoadRoboto();

        // Find an accented character that the font builds as a composite.
        int[] candidates = ['Ã', 'Ä', 'É', 'Ñ', 'ü', 'é'];
        var composite = candidates
            .Select(face.GetGlyphIndex)
            .Where(index => index > 0)
            .Select(face.GetGlyph)
            .FirstOrDefault(g => g.IsComposite);

        composite.ShouldNotBeNull("Roboto-Regular should build at least one accented glyph as a composite.");
        composite!.IsEmpty.ShouldBeFalse();
        // Base letter + accent mark => more than one contour, with real point data.
        composite.Contours.Length.ShouldBeGreaterThanOrEqualTo(2);
        composite.PointCount.ShouldBeGreaterThan(6);
    }

    [Fact]
    public void Glyf_GlyphIndexOutOfRange_Throws()
    {
        var face = LoadRoboto();

        Should.Throw<ArgumentOutOfRangeException>(() => face.GetGlyph(face.Maxp.NumGlyphs));
    }

    // ---------------------------------------------------------------- glyf (synthetic)

    [Fact]
    public void Glyf_SimpleGlyph_DecodesDeltaEncodedCoordinates()
    {
        var (glyf, loca) = BuildSyntheticGlyf();
        var table = new GlyfTable(glyf, loca, maxComponentDepth: 64);

        var glyph = table.GetGlyph(0);

        glyph.IsComposite.ShouldBeFalse();
        glyph.Contours.Length.ShouldBe(1);
        var points = glyph.Contours[0].Points;
        points.Length.ShouldBe(4);
        points.ShouldAllBe(p => p.OnCurve);
        points[0].ShouldBe(new GlyphPoint(0, 0, true));
        points[1].ShouldBe(new GlyphPoint(100, 0, true));
        points[2].ShouldBe(new GlyphPoint(100, 100, true));
        points[3].ShouldBe(new GlyphPoint(0, 100, true));
    }

    [Fact]
    public void Glyf_CompositeGlyph_AppliesScaleThenUnscaledOffset()
    {
        var (glyf, loca) = BuildSyntheticGlyf();
        var table = new GlyfTable(glyf, loca, maxComponentDepth: 64);

        var glyph = table.GetGlyph(1);

        glyph.IsComposite.ShouldBeTrue();
        glyph.Contours.Length.ShouldBe(1);
        var points = glyph.Contours[0].Points;
        // Component scaled by 0.5, then translated by the (unscaled) offset (+50, -25).
        points[0].ShouldBe(new GlyphPoint(50, -25, true));
        points[1].ShouldBe(new GlyphPoint(100, -25, true));
        points[2].ShouldBe(new GlyphPoint(100, 25, true));
        points[3].ShouldBe(new GlyphPoint(50, 25, true));
    }

    [Fact]
    public void Glyf_SelfReferencingComposite_HitsDepthLimit()
    {
        // Glyph 0 is a composite that references itself.
        byte[] component = Bytes(
            U16(0x0002),        // ARGS_ARE_XY_VALUES (byte args)
            U16(0),             // glyphIndex -> itself
            [0x00, 0x00]);      // arg1, arg2 (int8 each)
        byte[] glyph = Bytes(I16(-1), I16(0), I16(0), I16(10), I16(10), component);
        var loca = LocaTable.Parse(Bytes(U32(0), U32((uint)glyph.Length)), numGlyphs: 1, longFormat: true);
        var table = new GlyfTable(glyph, loca, maxComponentDepth: 4);

        Should.Throw<FontFormatException>(() => table.GetGlyph(0));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Builds a two-glyph <c>glyf</c> table: glyph 0 is a 100x100 square (simple),
    /// glyph 1 is a composite that places glyph 0 at half scale, offset (+50, -25).
    /// </summary>
    private static (byte[] Glyf, LocaTable Loca) BuildSyntheticGlyf()
    {
        byte[] simple = Bytes(
            I16(1),                                     // numberOfContours
            I16(0), I16(0), I16(100), I16(100),         // xMin, yMin, xMax, yMax
            U16(3),                                     // endPtsOfContours[0]
            U16(0),                                     // instructionLength
            [0x01, 0x01, 0x01, 0x01],                   // flags: all ON_CURVE_POINT
            I16(0), I16(100), I16(0), I16(-100),        // x deltas
            I16(0), I16(0), I16(100), I16(0));          // y deltas

        byte[] composite = Bytes(
            I16(-1),                                    // numberOfContours (composite)
            I16(50), I16(-25), I16(100), I16(25),       // xMin, yMin, xMax, yMax
            U16(0x0001 | 0x0002 | 0x0008),              // ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES | WE_HAVE_A_SCALE
            U16(0),                                     // glyphIndex -> the square
            I16(50), I16(-25),                          // dx, dy
            I16(8192));                                 // F2Dot14 0.5

        byte[] glyf = Bytes(simple, composite);
        byte[] locaData = Bytes(U32(0), U32((uint)simple.Length), U32((uint)glyf.Length));
        return (glyf, LocaTable.Parse(locaData, numGlyphs: 2, longFormat: true));
    }

    private static byte[] Bytes(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] U16(ushort value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] I16(short value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] U32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }
}
