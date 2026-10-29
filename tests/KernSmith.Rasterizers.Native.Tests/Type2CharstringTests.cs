using System.Globalization;
using System.Text;
using KernSmith.Rasterizers.Native.Internal.Cff;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 166 Part 2 — the Type 2 / CFF2 charstring interpreter, operator by operator, against
/// hand-computed outlines (Adobe TN#5177 and the OpenType CFF2 chapter).
/// </summary>
public class Type2CharstringTests
{
    private static readonly CharstringContext Cff = new() { DefaultWidthX = 500, NominalWidthX = 600 };

    private static CharstringOutline Run(byte[] charstring, CharstringContext? context = null) =>
        Type2Charstring.Interpret(charstring, context ?? Cff);

    private static string Path(byte[] charstring, CharstringContext? context = null) =>
        Describe(Run(charstring, context).Commands);

    /// <summary>"M x,y L x,y C x,y x,y x,y Z" — compact, exact, and readable in a failure message.</summary>
    internal static string Describe(IEnumerable<OutlineCommand> commands)
    {
        var text = new StringBuilder();
        foreach (var c in commands)
        {
            if (text.Length > 0)
                text.Append(' ');

            switch (c.Type)
            {
                case OutlineCommandType.MoveTo: text.Append("M ").Append(P(c.EndX, c.EndY)); break;
                case OutlineCommandType.LineTo: text.Append("L ").Append(P(c.EndX, c.EndY)); break;
                case OutlineCommandType.CubicTo:
                    text.Append("C ").Append(P(c.Ctrl1X, c.Ctrl1Y)).Append(' ')
                        .Append(P(c.Ctrl2X, c.Ctrl2Y)).Append(' ').Append(P(c.EndX, c.EndY));
                    break;
                case OutlineCommandType.Close: text.Append('Z'); break;
            }
        }

        return text.ToString();

        static string P(float x, float y) =>
            string.Create(CultureInfo.InvariantCulture, $"{x:0.###},{y:0.###}");
    }

    // ---- Numbers ---------------------------------------------------------------------------

    [Fact]
    public void Numbers_DecodeEveryEncoding()
    {
        // 1-byte (-107..107), 2-byte positive (108..1131) and negative, int16 (28), 16.16 fixed (255).
        Path(Cs.Of(100, 200, T2.RMoveTo, -500, new Short(20000), T2.RLineTo, new Fixed(1.5), -1131, T2.RLineTo, T2.EndChar))
            .ShouldBe("M 100,200 L -400,20200 L -398.5,19069 Z");
    }

    [Fact]
    public void TruncatedNumber_Throws()
    {
        Should.Throw<FontFormatException>(() => Run([28, 0x01]));
        Should.Throw<FontFormatException>(() => Run([255, 0, 0]));
        Should.Throw<FontFormatException>(() => Run([247]));
    }

    // ---- Movement and lines ----------------------------------------------------------------

    [Fact]
    public void Square_IsOneClosedContour()
    {
        Path(Cs.Of(10, 20, T2.RMoveTo, 30, 0, 0, 40, -30, 0, T2.RLineTo, T2.EndChar))
            .ShouldBe("M 10,20 L 40,20 L 40,60 L 10,60 Z");
    }

    [Fact]
    public void HLineToAndVLineTo_Alternate()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 20, 30, T2.HLineTo, 5, 6, T2.VLineTo, T2.EndChar))
            .ShouldBe("M 0,0 L 10,0 L 10,20 L 40,20 L 40,25 L 46,25 Z");
    }

    [Fact]
    public void HMoveToAndVMoveTo_MoveOneAxis()
    {
        Path(Cs.Of(10, T2.HMoveTo, 5, T2.HLineTo, 20, T2.VMoveTo, 5, T2.VLineTo, T2.EndChar))
            .ShouldBe("M 10,0 L 15,0 Z M 15,20 L 15,25 Z");
    }

    [Fact]
    public void MoveTo_ClosesThePreviousContour_AndAMoveWithNothingDrawnEmitsNothing()
    {
        // The first two movetos draw nothing, so only the third opens a contour.
        Path(Cs.Of(5, 5, T2.RMoveTo, 10, 10, T2.RMoveTo, 1, 1, T2.RMoveTo, 10, T2.HLineTo, T2.EndChar))
            .ShouldBe("M 16,16 L 26,16 Z");
        Run(Cs.Of(5, 5, T2.RMoveTo, T2.EndChar)).Commands.ShouldBeEmpty();
    }

    // ---- Curves ----------------------------------------------------------------------------

    [Fact]
    public void RRCurveTo_TakesSixOperandsPerCurve()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 0, 10, 10, 0, 10, 0, 10, -10, 0, -10, -10, T2.RRCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 10,0 20,10 20,20 C 20,30 10,30 0,20 Z");
    }

    [Fact]
    public void HHCurveTo_WithAndWithoutLeadingDy()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, T2.HHCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 10,0 15,5 25,5 Z");
        Path(Cs.Of(0, 0, T2.RMoveTo, 3, 10, 5, 5, 10, 1, 2, 3, 4, T2.HHCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 10,3 15,8 25,8 C 26,8 28,11 32,11 Z");
    }

    [Fact]
    public void VVCurveTo_WithAndWithoutLeadingDx()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, T2.VVCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 0,10 5,15 5,25 Z");
        Path(Cs.Of(0, 0, T2.RMoveTo, 3, 10, 5, 5, 10, T2.VVCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 3,10 8,15 8,25 Z");
    }

    [Fact]
    public void HVCurveTo_AlternatesAndTakesAFinalExtraOperand()
    {
        // 4 operands: horizontal start, vertical end.
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, T2.HVCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 10,0 15,5 15,15 Z");
        // 5 operands: the last curve ends with dx too.
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, 2, T2.HVCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 10,0 15,5 17,15 Z");
        // 9 operands: horizontal-start then vertical-start, the second taking the extra dy.
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, 10, 5, 5, 10, 2, T2.HVCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 10,0 15,5 15,15 C 15,25 20,30 30,32 Z");
    }

    [Fact]
    public void VHCurveTo_AlternatesAndTakesAFinalExtraOperand()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, T2.VHCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 0,10 5,15 15,15 Z");
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, 2, T2.VHCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 0,10 5,15 15,17 Z");
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 5, 5, 10, 10, 5, 5, 10, 2, T2.VHCurveTo, T2.EndChar))
            .ShouldBe("M 0,0 C 0,10 5,15 15,15 C 25,15 30,20 32,30 Z");
    }

    [Fact]
    public void RCurveLine_IsCurvesThenALine()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 1, 2, 3, 4, 5, 6, 7, 8, T2.RCurveLine, T2.EndChar))
            .ShouldBe("M 0,0 C 1,2 4,6 9,12 L 16,20 Z");
    }

    [Fact]
    public void RLineCurve_IsLinesThenACurve()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 1, 2, 3, 4, 5, 6, 7, 8, T2.RLineCurve, T2.EndChar))
            .ShouldBe("M 0,0 L 1,2 C 4,6 9,12 16,20 Z");
    }

    // ---- Flex ------------------------------------------------------------------------------

    [Fact]
    public void Flex_IsTwoCurves_AndIgnoresTheFlexDepth()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 1, 10, 2, 10, 3, 10, -3, 10, -2, 10, -1, 50, T2.Flex, T2.EndChar))
            .ShouldBe("M 0,0 C 10,1 20,3 30,6 C 40,3 50,1 60,0 Z");
    }

    [Fact]
    public void HFlex_KeepsTheEndsOnTheStartingY()
    {
        // dx1 dx2 dy2 dx3 dx4 dx5 dx6
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 10, 5, 10, 10, 10, 10, T2.HFlex, T2.EndChar))
            .ShouldBe("M 0,0 C 10,0 20,5 30,5 C 40,5 50,0 60,0 Z");
    }

    [Fact]
    public void HFlex1_ReturnsToTheStartingY()
    {
        // dx1 dy1 dx2 dy2 dx3 dx4 dx5 dy5 dx6
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 1, 10, 4, 10, 10, 10, -2, 10, T2.HFlex1, T2.EndChar))
            .ShouldBe("M 0,0 C 10,1 20,5 30,5 C 40,5 50,3 60,0 Z");
    }

    [Fact]
    public void Flex1_FinalPointFollowsTheDominantAxis()
    {
        // Horizontal-dominant: the last operand is dx6 and y returns to the start.
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 1, 10, 2, 10, 3, 10, -1, 10, -1, 10, T2.Flex1, T2.EndChar))
            .ShouldBe("M 0,0 C 10,1 20,3 30,6 C 40,5 50,4 60,0 Z");
        // Vertical-dominant: the last operand is dy6 and x returns to the start.
        Path(Cs.Of(0, 0, T2.RMoveTo, 1, 10, 2, 10, 3, 10, -1, 10, -1, 10, 10, T2.Flex1, T2.EndChar))
            .ShouldBe("M 0,0 C 1,10 3,20 6,30 C 5,40 4,50 0,60 Z");
    }

    // ---- Width -----------------------------------------------------------------------------

    [Fact]
    public void Width_OddStemOperands_IsNominalPlusTheFirst()
    {
        Run(Cs.Of(-50, 10, 20, T2.HStem, 0, 0, T2.RMoveTo, T2.EndChar)).Width.ShouldBe(550);
    }

    [Fact]
    public void Width_ExtraMoveToOperand_IsNominalPlusTheFirst()
    {
        var outline = Run(Cs.Of(25, 10, 20, T2.RMoveTo, 5, T2.HLineTo, T2.EndChar));
        outline.Width.ShouldBe(625);
        Describe(outline.Commands).ShouldBe("M 10,20 L 15,20 Z");

        Run(Cs.Of(25, 10, T2.HMoveTo, 5, T2.VLineTo, T2.EndChar)).Width.ShouldBe(625);
        Run(Cs.Of(-50, T2.EndChar)).Width.ShouldBe(550);
    }

    [Fact]
    public void Width_NoneEncoded_IsDefaultWidthX()
    {
        Run(Cs.Of(10, 20, T2.HStem, 10, 20, T2.RMoveTo, 5, T2.HLineTo, T2.EndChar)).Width.ShouldBe(500);
        Run(Cs.Of(T2.EndChar)).Width.ShouldBe(500);
    }

    [Fact]
    public void Width_IsNotParsedInCff2()
    {
        // CFF2 has no width operand: a lone endchar-free charstring just ends.
        var cff2 = new CharstringContext { IsCff2 = true, DefaultWidthX = 500 };
        Run(Cs.Of(10, 20, T2.RMoveTo, 5, T2.HLineTo), cff2).Width.ShouldBe(0);
        Path(Cs.Of(10, 20, T2.RMoveTo, 5, T2.HLineTo), cff2).ShouldBe("M 10,20 L 15,20 Z");
    }

    // ---- Hints -----------------------------------------------------------------------------

    [Fact]
    public void HintMask_SkipsOneByteForUpToEightStems_CountingTheImplicitVStem()
    {
        // 2 hstems + 1 implicit vstem = 3 stems → 1 mask byte. The mask byte 0x0E would be
        // endchar if it were executed instead of skipped.
        var outline = Run(Cs.Of(0, 10, 20, 10, T2.HStemHm, 5, 10, T2.HintMask, new Raw(0x0E),
            0, 0, T2.RMoveTo, 10, T2.HLineTo, T2.EndChar));
        outline.StemCount.ShouldBe(3);
        Describe(outline.Commands).ShouldBe("M 0,0 L 10,0 Z");
    }

    [Fact]
    public void HintMask_SkipsTwoBytesForNineStems()
    {
        // 8 hstems + 1 implicit vstem = 9 stems → 2 mask bytes; the second (0x0E) must be skipped too.
        var stems = Enumerable.Range(0, 16).Select(i => (object)(i % 2 == 0 ? 10 : 5)).ToArray();
        var outline = Run(Cs.Of([.. stems, T2.HStemHm, 3, 4, T2.HintMask, new Raw(0xFF, 0x0E),
            0, 0, T2.RMoveTo, 10, T2.HLineTo, T2.EndChar]));
        outline.StemCount.ShouldBe(9);
        Describe(outline.Commands).ShouldBe("M 0,0 L 10,0 Z");
    }

    [Fact]
    public void CntrMask_CountsStemsLikeHintMask()
    {
        var outline = Run(Cs.Of(0, 10, T2.HStemHm, 5, 10, 30, 10, T2.VStemHm, T2.CntrMask, new Raw(0x0E),
            0, 0, T2.RMoveTo, 10, T2.HLineTo, T2.EndChar));
        outline.StemCount.ShouldBe(3);
        Describe(outline.Commands).ShouldBe("M 0,0 L 10,0 Z");
    }

    [Fact]
    public void HintMask_WithOddOperandsFirst_TakesTheWidth()
    {
        var outline = Run(Cs.Of(40, 0, 10, T2.HintMask, new Raw(0x80), 0, 0, T2.RMoveTo, 10, T2.HLineTo, T2.EndChar));
        outline.Width.ShouldBe(640);
        outline.StemCount.ShouldBe(1);
    }

    [Fact]
    public void HintMask_TruncatedMaskBytes_Throws()
    {
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 10, 20, 10, T2.HStemHm, T2.HintMask)));
    }

    // ---- Subroutines -----------------------------------------------------------------------

    [Theory]
    [InlineData(1239, 107)]
    [InlineData(1240, 1131)]
    [InlineData(33899, 1131)]
    [InlineData(33900, 32768)]
    [InlineData(0, 107)]
    public void SubroutineBias_FollowsTheCountThresholds(int count, int bias)
    {
        CffIndex.SubroutineBias(count).ShouldBe(bias);
    }

    [Theory]
    [InlineData(1, -107)]
    [InlineData(1240, -1131)]
    [InlineData(33900, -32768)]
    public void CallSubr_AppliesTheBiasForTheIndexSize(int count, int operand)
    {
        // Subr 0 draws the line; every other subr is a bare return, so only a correctly biased
        // call draws anything.
        var subrs = new byte[count][];
        subrs[0] = Cs.Of(10, T2.HLineTo, T2.Return);
        for (int i = 1; i < count; i++)
            subrs[i] = T2.Return;

        var local = Index(subrs);
        local.Count.ShouldBe(count);
        var context = new CharstringContext { LocalSubrs = local };

        Path(Cs.Of(0, 0, T2.RMoveTo, operand, T2.CallSubr, T2.EndChar), context).ShouldBe("M 0,0 L 10,0 Z");
    }

    [Fact]
    public void CallGSubr_UsesTheGlobalIndex_AndSubroutinesShareTheStack()
    {
        // The global subr pushes operands that the caller's operator consumes.
        var context = new CharstringContext
        {
            GlobalSubrs = Index([Cs.Of(7, 8, T2.Return)]),
            LocalSubrs = Index([Cs.Of(99, T2.HLineTo, T2.Return)]),
        };

        Path(Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallGSubr, T2.RLineTo, T2.EndChar), context).ShouldBe("M 0,0 L 7,8 Z");
    }

    [Fact]
    public void EndCharInsideASubroutine_EndsTheGlyph()
    {
        var context = new CharstringContext { LocalSubrs = Index([Cs.Of(10, T2.HLineTo, T2.EndChar)]) };
        Path(Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallSubr, 50, T2.VLineTo, T2.EndChar), context).ShouldBe("M 0,0 L 10,0 Z");
    }

    [Fact]
    public void OutOfRangeSubroutine_Throws()
    {
        var context = new CharstringContext { LocalSubrs = Index([T2.Return]) };
        Should.Throw<FontFormatException>(() => Run(Cs.Of(-106, T2.CallSubr), context));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(-108, T2.CallSubr), context));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(-107, T2.CallGSubr), context));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(new Fixed(-106.5), T2.CallSubr), context));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(T2.CallSubr), context));
    }

    [Fact]
    public void SubroutineNesting_AllowsTenLevels_AndRejectsMore()
    {
        // Subr i calls subr i+1; the last draws. A charstring calling subr 0 nests `depth` deep.
        static CharstringContext Chain(int depth)
        {
            var subrs = new byte[depth][];
            for (int i = 0; i < depth - 1; i++)
                subrs[i] = Cs.Of(i + 1 - 107, T2.CallSubr, T2.Return);
            subrs[depth - 1] = Cs.Of(10, T2.HLineTo, T2.Return);
            return new CharstringContext { LocalSubrs = Index(subrs) };
        }

        Path(Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar), Chain(10)).ShouldBe("M 0,0 L 10,0 Z");
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar), Chain(11)));
    }

    [Fact]
    public void RecursiveSubroutine_Throws()
    {
        var context = new CharstringContext { LocalSubrs = Index([Cs.Of(-107, T2.CallSubr, T2.Return)]) };
        Should.Throw<FontFormatException>(() => Run(Cs.Of(-107, T2.CallSubr), context));
    }

    [Fact]
    public void ExponentialSubroutineFanOut_IsBounded()
    {
        // Each of 10 levels calls the next 40 times: 40^10 calls if run to completion. The
        // interpreter's work budget must stop it quickly instead of hanging.
        var subrs = new byte[10][];
        for (int level = 0; level < 9; level++)
        {
            var calls = new List<object>();
            for (int c = 0; c < 40; c++)
                calls.AddRange([level + 1 - 107, T2.CallSubr]);
            calls.Add(T2.Return);
            subrs[level] = Cs.Of([.. calls]);
        }

        // The leaf draws nothing, so only the work budget — not the command or coordinate caps — can stop it.
        subrs[9] = T2.Return;
        var context = new CharstringContext { LocalSubrs = Index(subrs) };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, T2.RMoveTo, -107, T2.CallSubr, T2.EndChar), context))
            .Message.ShouldContain("operations", Case.Sensitive);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    // ---- Stack limits ----------------------------------------------------------------------

    [Fact]
    public void Cff_StackHolds48Operands_AndRejects49()
    {
        var ok = Enumerable.Repeat((object)1, 48).Append(T2.RLineTo).ToArray();
        Run(Cs.Of([0, 0, T2.RMoveTo, .. ok, T2.EndChar])).Commands.Length.ShouldBe(26);

        var overflow = Enumerable.Repeat((object)1, 49).Append(T2.RLineTo).ToArray();
        Should.Throw<FontFormatException>(() => Run(Cs.Of([0, 0, T2.RMoveTo, .. overflow])));
    }

    [Fact]
    public void Cff2_StackHolds513Operands_AndRejects514()
    {
        var cff2 = new CharstringContext { IsCff2 = true };
        var ok = Enumerable.Repeat((object)1, 512).Append(T2.RLineTo).ToArray();
        Run(Cs.Of([0, 0, T2.RMoveTo, .. ok]), cff2).Commands.Length.ShouldBe(258);

        var overflow = Enumerable.Repeat((object)1, 514).Append(T2.RLineTo).ToArray();
        Should.Throw<FontFormatException>(() => Run(Cs.Of([0, 0, T2.RMoveTo, .. overflow]), cff2));
    }

    [Fact]
    public void StackUnderflow_Throws()
    {
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, T2.RMoveTo, 1, T2.RLineTo)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, T2.RMoveTo, 1, 2, 3, T2.RRCurveTo)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(1, T2.RMoveTo)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, T2.RMoveTo, 1, 2, 3, 4, 5, T2.Flex)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(T2.Add)));
    }

    [Fact]
    public void ReservedOperator_Throws()
    {
        Should.Throw<FontFormatException>(() => Run([0]));
        Should.Throw<FontFormatException>(() => Run([2]));
        Should.Throw<FontFormatException>(() => Run([12, 99]));
        Should.Throw<FontFormatException>(() => Run([12]));
    }

    [Fact]
    public void HugeCoordinates_AreRejected()
    {
        // Repeated maximal lines run far outside any real em square; the bitmap they would
        // demand is the hazard, so the interpreter refuses them.
        var lines = new List<object> { 0, 0, T2.RMoveTo };
        for (int i = 0; i < 40; i++)
            lines.AddRange([new Short(32000), 0, T2.RLineTo]);
        lines.Add(T2.EndChar);

        Should.Throw<FontFormatException>(() => Run(Cs.Of([.. lines])));
    }

    // ---- Arithmetic and storage ------------------------------------------------------------

    [Fact]
    public void Arithmetic_ComputesOperands()
    {
        // (2 + 3) * 4 = 20 ; 30 - 10 = 20 → rmoveto 20 20
        Path(Cs.Of(2, 3, T2.Add, 4, T2.Mul, 30, 10, T2.Sub, T2.RMoveTo, 9, 3, T2.Div, T2.HLineTo, T2.EndChar))
            .ShouldBe("M 20,20 L 23,20 Z");
        // neg, abs, sqrt
        Path(Cs.Of(5, T2.Neg, 7, T2.Neg, T2.Abs, T2.RMoveTo, 16, T2.Sqrt, T2.HLineTo, T2.EndChar))
            .ShouldBe("M -5,7 L -1,7 Z");
    }

    [Fact]
    public void Logic_ProducesZeroOrOne()
    {
        // and(1,0)=0, or(1,0)=1 ; not(0)=1, eq(4,4)=1
        Path(Cs.Of(1, 0, T2.And, 1, 0, T2.Or, T2.RMoveTo, 0, T2.Not, 4, 4, T2.Eq, T2.RLineTo, T2.EndChar))
            .ShouldBe("M 0,1 L 1,2 Z");
    }

    [Fact]
    public void StackOperators_DupExchDropIndexRoll()
    {
        // 3 dup → 3 3 ; 1 2 exch → 2 1 ; 9 drop → (gone)
        Path(Cs.Of(3, T2.Dup, T2.RMoveTo, 1, 2, T2.Exch, 9, T2.Drop, T2.RLineTo, T2.EndChar))
            .ShouldBe("M 3,3 L 5,4 Z");
        // 10 20 30  1 index → 10 20 30 20
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 20, 30, 1, T2.Index, T2.RLineTo, T2.EndChar))
            .ShouldBe("M 0,0 L 10,20 L 40,40 Z");
        // 1 2 3  3 1 roll → 3 1 2
        Path(Cs.Of(0, 0, T2.RMoveTo, 1, 2, 3, 3, 1, T2.Roll, 0, T2.RLineTo, T2.EndChar))
            .ShouldBe("M 0,0 L 3,1 L 5,1 Z");
    }

    [Fact]
    public void PutGetIfElse_UseTheTransientArray()
    {
        // put 42 in slot 5, get it back; ifelse picks s1 when v1 <= v2.
        Path(Cs.Of(42, 5, T2.Put, 5, T2.Get, 7, 8, 1, 2, T2.IfElse, T2.RMoveTo, 7, 8, 2, 1, T2.IfElse, T2.HLineTo, T2.EndChar))
            .ShouldBe("M 42,7 L 50,7 Z");
        Should.Throw<FontFormatException>(() => Run(Cs.Of(1, 32, T2.Put)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(-1, T2.Get)));
    }

    [Fact]
    public void UnsupportedOrInvalidArithmetic_Throws()
    {
        Should.Throw<FontFormatException>(() => Run(Cs.Of(T2.Random)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(1, 0, T2.Div)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(-4, T2.Sqrt)));
    }

    [Fact]
    public void DotSection_IsIgnored()
    {
        Path(Cs.Of(0, 0, T2.RMoveTo, T2.DotSection, 10, T2.HLineTo, T2.EndChar)).ShouldBe("M 0,0 L 10,0 Z");
    }

    // ---- CFF2 blend / vsindex --------------------------------------------------------------

    private static readonly CharstringContext Cff2TwoRegions = new() { IsCff2 = true, RegionCounts = [2, 1] };

    [Fact]
    public void Blend_DefaultInstance_KeepsTheDefaultValues()
    {
        // The OpenType CFF2 example: "50 50 100 1 blend 0 rmoveto" with k = 2.
        Path(Cs.Of(50, 50, 100, 1, T2.Blend, 0, T2.RMoveTo, 500, -100, -200, 1, T2.Blend, T2.HLineTo), Cff2TwoRegions)
            .ShouldBe("M 50,0 L 550,0 Z");
    }

    [Fact]
    public void Blend_AppliesRegionScalars_WhenAnInstanceSuppliesThem()
    {
        var context = new CharstringContext
        {
            IsCff2 = true,
            RegionCounts = [2],
            BlendScalars = _ => [0.5, 0.25],
        };

        // 50 + 50*0.5 + 100*0.25 = 100 ; two values blended at once (n = 2).
        Path(Cs.Of(50, 10, 50, 100, 0, 0, 2, T2.Blend, T2.RMoveTo, 1, T2.HLineTo), context)
            .ShouldBe("M 100,10 L 101,10 Z");
    }

    [Fact]
    public void VsIndex_SelectsTheRegionCount()
    {
        // vsindex 1 → k = 1, so "120 52 1 blend" consumes one delta.
        Path(Cs.Of(1, T2.VsIndex, 0, 0, T2.RMoveTo, 120, 52, 1, T2.Blend, T2.HLineTo), Cff2TwoRegions)
            .ShouldBe("M 0,0 L 120,0 Z");
        Should.Throw<FontFormatException>(() => Run(Cs.Of(5, T2.VsIndex, 1, 1, 1, T2.Blend), Cff2TwoRegions));
    }

    /// <summary>Per-vsindex scalars for <see cref="Cff2TwoRegions"/>: ItemVariationData 0 has two regions, 1 has one.</summary>
    private static CharstringContext Cff2Instance(int privateVsIndex = 0) => new()
    {
        IsCff2 = true,
        RegionCounts = [2, 1],
        VsIndex = privateVsIndex,
        BlendScalars = vsIndex => vsIndex == 0 ? [0.75, 0.5] : [0.75],
    };

    [Fact]
    public void Blend_SpecExamples_OneAndTwoRegions()
    {
        // OpenType CFF2 chapter: "120 52 1 blend hlineto" at scalar 0.75 is "159 hlineto";
        // with two regions, "120 52 36 1 blend hlineto" at scalars 0.75, 0.5 is "177 hlineto".
        Path(Cs.Of(1, T2.VsIndex, 0, 0, T2.RMoveTo, 120, 52, 1, T2.Blend, T2.HLineTo), Cff2Instance())
            .ShouldBe("M 0,0 L 159,0 Z");
        Path(Cs.Of(0, 0, T2.RMoveTo, 120, 52, 36, 1, T2.Blend, T2.HLineTo), Cff2Instance())
            .ShouldBe("M 0,0 L 177,0 Z");
    }

    [Fact]
    public void Blend_UsesThePrivateDictVsIndex_UntilTheCharstringOverridesIt()
    {
        // Private DICT vsindex 1 → one region at 0.75; a charstring vsindex 0 → two regions.
        Path(Cs.Of(0, 0, T2.RMoveTo, 120, 52, 1, T2.Blend, T2.HLineTo), Cff2Instance(privateVsIndex: 1))
            .ShouldBe("M 0,0 L 159,0 Z");
        Path(Cs.Of(0, T2.VsIndex, 0, 0, T2.RMoveTo, 120, 52, 36, 1, T2.Blend, T2.HLineTo), Cff2Instance(privateVsIndex: 1))
            .ShouldBe("M 0,0 L 177,0 Z");
    }

    [Fact]
    public void Blend_SeveralBlendsBeforeOneOperator_EachBlendsItsOwnValues()
    {
        // "Advanced uses of blend": two n = 1 blends feeding one rlineto.
        // dx = 10 + 4*0.75 + 8*0.5 = 17; dy = 20 + 0*0.75 + (-12)*0.5 = 14.
        Path(Cs.Of(0, 0, T2.RMoveTo, 10, 4, 8, 1, T2.Blend, 20, 0, -12, 1, T2.Blend, T2.RLineTo), Cff2Instance())
            .ShouldBe("M 0,0 L 17,14 Z");
    }

    [Fact]
    public void Blend_InASubroutine_SharesTheCallersStackAndRegions()
    {
        // The spec example's subroutine, at the instance where region 0 is fully on.
        var context = new CharstringContext
        {
            IsCff2 = true,
            RegionCounts = [2],
            BlendScalars = _ => [1, 0],
            LocalSubrs = Index([Cs.Of(50, 50, 100, 1, T2.Blend, 0, T2.RMoveTo, 500, -100, -200, 1, T2.Blend, T2.HLineTo, T2.Return)]),
        };

        Path(Cs.Of(-107, T2.CallSubr), context).ShouldBe("M 100,0 L 500,0 Z");
    }

    [Fact]
    public void Blend_WithTooFewOperands_Throws()
    {
        Should.Throw<FontFormatException>(() => Run(Cs.Of(1, 2, T2.Blend), Cff2TwoRegions));
    }

    [Fact]
    public void BlendAndVsIndex_AreReservedInCff()
    {
        Should.Throw<FontFormatException>(() => Run(Cs.Of(1, 1, 1, 1, T2.Blend)));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, T2.VsIndex)));
    }

    // ---- seac ------------------------------------------------------------------------------

    [Fact]
    public void Seac_DrawsTheBaseThenTheOffsetAccent()
    {
        var context = new CharstringContext
        {
            DefaultWidthX = 500,
            NominalWidthX = 600,
            // StandardEncoding 'A' = 65, acute = 194.
            SeacComponent = code => code switch
            {
                65 => Cs.Of(0, 0, T2.RMoveTo, 100, T2.HLineTo, T2.EndChar),
                194 => Cs.Of(5, 5, T2.RMoveTo, 10, T2.VLineTo, T2.EndChar),
                _ => null,
            },
        };

        var outline = Run(Cs.Of(-20, 30, 400, 65, 194, T2.EndChar), context);
        outline.Width.ShouldBe(580);
        Describe(outline.Commands).ShouldBe("M 0,0 L 100,0 Z M 35,405 L 35,415 Z");
    }

    [Fact]
    public void Seac_MissingComponent_Throws()
    {
        var context = new CharstringContext { SeacComponent = _ => null };
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, 65, 194, T2.EndChar), context));
        Should.Throw<FontFormatException>(() => Run(Cs.Of(0, 0, 65, 194, T2.EndChar)));
    }

    internal static CffIndex Index(IReadOnlyList<byte[]> items, bool cff2 = false) =>
        CffIndex.Parse(CffBytes.Index(items, cff2), 0, cff2, out _);
}
