using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="StemEdges"/> tells a straight vertical stem at a glyph's extreme x from a bowl's
/// extremum or a terminal's short cut: only two facing stems fuse across a touching pair.
/// </summary>
public class StemEdgesTests
{
    private const float Slope = 0.07f;

    [Fact]
    public void ARectangle_IsAStemOnBothSides()
    {
        StemEdges.Detect(GlyphOutline.FromCommands(TestOutlines.Rect(1f, 0f, 1.8f, 5f)), Slope).ShouldBe((true, true));
    }

    [Fact]
    public void ABowl_IsAStemOnNeitherSide()
    {
        // The ring's extrema are nodes between two curves: no straight segment reaches them.
        var ring = Ring(2.5f, 2.5f, outer: 2f, inner: 1.3f);
        StemEdges.Detect(ring, Slope).ShouldBe((false, false));
    }

    [Fact]
    public void ABarsEnd_IsNotAStem()
    {
        // A 'T': the bar's right end is a vertical cut one bar thick, a fifth of the height.
        var t = GlyphOutline.FromCommands(TestOutlines.Polygon(
            (0f, 4f), (5f, 4f), (5f, 5f), (0f, 5f)));
        var stem = GlyphOutline.FromCommands(TestOutlines.Rect(2.1f, 0f, 2.9f, 4f));
        var glyph = GlyphOutline.FromCommands([.. t.Commands, .. stem.Commands]);
        StemEdges.Detect(glyph, Slope).ShouldBe((false, false));
    }

    [Fact]
    public void AStemBesideABowl_IsAStemOnThatSideOnly()
    {
        // A 'd': the stem on the right spans the whole height, the bowl on the left is round.
        var bowl = Ring(2.2f, 2f, outer: 2f, inner: 1.3f);
        var stem = GlyphOutline.FromCommands(TestOutlines.Rect(3.4f, 0f, 4.2f, 7f));
        var glyph = GlyphOutline.FromCommands([.. bowl.Commands, .. stem.Commands]);
        StemEdges.Detect(glyph, Slope).ShouldBe((false, true));
    }

    [Fact]
    public void AShortStem_UnderTheHeightShare_IsNotAStem()
    {
        // A stem a quarter of the glyph's height at its right edge (a serif's foot, a '7''s
        // flag end) is not what fuses a pair.
        var tall = GlyphOutline.FromCommands(TestOutlines.Rect(0f, 0f, 1f, 8f));
        var foot = GlyphOutline.FromCommands(TestOutlines.Rect(1f, 0f, 3f, 1.5f));
        var glyph = GlyphOutline.FromCommands([.. tall.Commands, .. foot.Commands]);
        StemEdges.Detect(glyph, Slope).ShouldBe((true, false));
    }

    private static GlyphOutline Ring(float cx, float cy, float outer, float inner) =>
        GlyphOutline.FromCommands([.. Circle45(cx, cy, outer, clockwise: true), .. Circle45(cx, cy, inner, clockwise: false)]);

    private static OutlineCommand[] Circle45(float cx, float cy, float r, bool clockwise)
    {
        const float k = 0.5522847498f;
        float step = clockwise ? -MathF.PI / 2f : MathF.PI / 2f;
        float a = MathF.PI / 4f;
        var commands = new List<OutlineCommand> { OutlineCommand.Move(cx + (r * MathF.Cos(a)), cy + (r * MathF.Sin(a))) };
        for (int i = 0; i < 4; i++)
        {
            float b = a + step;
            float sign = clockwise ? -1f : 1f;
            float c1x = cx + (r * MathF.Cos(a)) + (sign * k * r * -MathF.Sin(a));
            float c1y = cy + (r * MathF.Sin(a)) + (sign * k * r * MathF.Cos(a));
            float c2x = cx + (r * MathF.Cos(b)) - (sign * k * r * -MathF.Sin(b));
            float c2y = cy + (r * MathF.Sin(b)) - (sign * k * r * MathF.Cos(b));
            float ex = i == 3 ? commands[0].EndX : cx + (r * MathF.Cos(b));
            float ey = i == 3 ? commands[0].EndY : cy + (r * MathF.Sin(b));
            commands.Add(OutlineCommand.Cubic(c1x, c1y, c2x, c2y, ex, ey));
            a = b;
        }

        commands.Add(OutlineCommand.CloseAt(commands[0].EndX, commands[0].EndY));
        return [.. commands];
    }
}
