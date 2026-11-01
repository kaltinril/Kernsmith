using System.Buffers;
using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>How <see cref="StemFitter"/> fits one axis.</summary>
/// <param name="Zones">Alignment zones along the axis (empty for the x axis).</param>
/// <param name="ZoneTolerance">How far outside a zone an edge may sit and still snap to it.</param>
/// <param name="MaxStem">The thickest pair of edges treated as one stroke, in pixels.</param>
/// <param name="StandardStem">The font's standard stroke thickness along this axis, in pixels; 0 when unknown.</param>
/// <param name="Mono">True when the result is thresholded at 50%: a stroke takes whole pixels, never a fraction.</param>
/// <param name="AlignPhase">True to choose a global sub-pixel shift that lets the most stroke
/// ink round the same way (the x axis, where there are no zones to anchor to).</param>
/// <param name="MaxShift">The furthest, in pixels, a placed edge may move from its unhinted position.</param>
/// <param name="MinCounter">A gap between two features at least this wide, in pixels, keeps at least one whole pixel.</param>
internal sealed record StemFitParameters(
    IReadOnlyList<PixelZone> Zones,
    float ZoneTolerance,
    float MaxStem,
    float StandardStem,
    bool Mono,
    bool AlignPhase,
    float MaxShift,
    float MinCounter);

/// <summary>
/// Grid fitting of one axis of a pixel-space outline, written for the y axis (Y up); the x axis
/// is fitted by transposing the outline first (<see cref="OutlineAxes.Transpose"/>). It is the
/// <c>Normal</c> hinter's engine: edges found by <see cref="GridFitter.FindEdges"/> are snapped
/// to zones or paired into strokes, placed on the grid, and every other point follows through a
/// monotone piecewise-linear map of its coordinate.
/// </summary>
/// <remarks>
/// <para>
/// A stroke keeps its ink. In grayscale a stroke's thickness is not rounded: it is placed where
/// a whole-pixel stroke of the nearest whole width would sit, centred in that span, so a stroke
/// under a pixel thick lands inside one pixel column (or row) with its true coverage instead of
/// straddling two at half strength, and a wider one has one solid pixel and one fringe rather
/// than two fringes. In mono a stroke becomes exactly its rounded width, at least one pixel,
/// with both edges on pixel boundaries, so the 50% threshold cannot drop or double it.
/// </para>
/// <para>
/// INVARIANT: every point that is not on a placed edge moves by one non-decreasing
/// piecewise-linear function of its coordinate through the placed edges, so the fitted outline
/// cannot cross itself; a gap between two features that was at least <see cref="StemFitParameters.MinCounter"/>
/// wide stays at least one pixel wide, so counters survive the threshold.
/// </para>
/// </remarks>
internal static class StemFitter
{
    /// <summary>Candidate sub-pixel shifts tried by phase alignment, per pixel.</summary>
    private const int PhaseSteps = 32;

    /// <summary>
    /// The shortest edge, in pixels, that counts. Shorter than the light fitter's: a bowl's
    /// extremum is often a flat a few font units long (Roboto's 'o' has a 0.15 px one at 9 ppem),
    /// and dropping it loses the bowl's side altogether, since its neighbours are then not
    /// tangent nodes either.
    /// </summary>
    private const float MinEdgeLength = 0.05f;

    /// <summary>A straight edge shorter than this is placed only as part of a stroke, like a
    /// round one: a sliver of flat on a bowl or a bar's end must not drag the glyph by itself.</summary>
    private const float MinLoneEdgeLength = 0.7f;

    /// <summary>
    /// Two edges with ink on the same side and positions this close, in pixels, are one edge.
    /// Features at different heights that nearly line up — an 'S' terminal and the bowl above
    /// it, the arms of an 'E' — cannot each be snapped by one monotone map of the axis; merged,
    /// they snap together, which is also how they read.
    /// </summary>
    private const float MergeDistance = 0.3f;

    /// <summary>
    /// Fitted coordinates land on a 1/64 px grid (FreeType's 26.6 resolution). A placed edge
    /// computed as original + (target - edge position) can miss its whole-pixel target by a
    /// float rounding error, and a top edge at 4.0000005 costs an empty bitmap row.
    /// </summary>
    private const float Resolution = 64f;

    /// <summary>Penalty per pixel of global shift, in pixels of weighted stroke movement.</summary>
    private const float PhaseShiftPenalty = 0.25f;

    /// <summary>
    /// How far, as a fraction of the standard thickness, a stroke may be from it and still be
    /// fitted as the standard. Nominally equal stems differ by a unit or two (Roboto's 'h' and
    /// 'l' stems are 185 and 186 units); a stroke a quarter pixel thinner than the standard —
    /// the bars of Roboto's '$' at 12 ppem, 0.88 px against 1.13 — is a thinner stroke, and
    /// widening it 28% to the standard distorts the glyph and its width.
    /// </summary>
    private const float StandardStemTolerance = 0.1f;

    /// <summary>Fits an outline's y axis.</summary>
    /// <param name="outline">The outline in pixels, Y up.</param>
    /// <param name="p">The axis parameters.</param>
    /// <returns>The fitted outline, or null when there was nothing to fit.</returns>
    public static GlyphOutline? Fit(GlyphOutline outline, StemFitParameters p) => Fit(outline, p, out _);

    /// <summary>Fits an outline's y axis, reporting the global phase shift it chose.</summary>
    public static GlyphOutline? Fit(GlyphOutline outline, StemFitParameters p, out float phase)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(p);
        phase = 0f;

        var commands = outline.Commands;
        var edges = GridFitter.FindEdges(commands, out var aliases, MinEdgeLength, halfTangents: true);
        if (edges.Count == 0)
            return null;

        RefineRoundEdges(commands, edges);
        edges = MergeAlignedEdges(edges);
        Pair(edges, p.MaxStem);

        // 1. Blue zones: each zone edge is its own feature.
        var map = new MonotoneMap(p.MaxShift, p.MinCounter);
        int feature = 0;
        foreach (var edge in edges)
        {
            if (ZoneTarget(edge, p) is { } target && map.TryAdd(edge.Y, target, ++feature, zone: true))
                edge.Hinted = target;
        }

        // 2. The global phase, chosen before any stroke is placed.
        var stems = new List<GridFitter.Edge>();
        foreach (var edge in edges)
        {
            if (edge.IsTop && edge.Partner is not null)
                stems.Add(edge);
        }

        if (p.AlignPhase && map.Count == 0)
            map.BaseShift = phase = Phase(stems, p);

        // 3. Strokes: those hanging off a placed edge first, then the longest.
        stems.Sort((a, b) =>
        {
            int anchored = Anchored(b).CompareTo(Anchored(a));
            return anchored != 0 ? anchored : StemLength(b).CompareTo(StemLength(a));
        });
        GridFitter.Edge? anchor = null;
        foreach (var top in stems)
            PlaceStem(top, top.Partner!, p, map, ++feature, ref anchor);

        // 4. Lone straight edges round to the nearest pixel line from where they were, not from
        // where the map would carry them: a bar's end or a serif keeps its own place, and the
        // map interpolates between it and the strokes.
        foreach (var edge in edges)
        {
            if (edge.Hinted is not null || edge.IsRound || edge.Length < MinLoneEdgeLength)
                continue;

            float target = GridMath.Round(edge.Y + map.BaseShift);
            if (map.TryAdd(edge.Y, target, ++feature, zone: false))
                edge.Hinted = target;
        }

        if (map.Count == 0 && map.BaseShift == 0f)
            return null;

        return Apply(commands, edges, aliases, map);
    }

    /// <summary>The widths, in the outline's units, of its straight strokes along this axis,
    /// each with the stroke's length: the input to a standard-width measurement.</summary>
    public static List<(float Width, float Length)> StraightStemWidths(GlyphOutline outline, float maxStem)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var edges = GridFitter.FindEdges(outline.Commands, out _, MinEdgeLength, halfTangents: true);
        Pair(edges, maxStem);

        var widths = new List<(float, float)>();
        foreach (var edge in edges)
        {
            if (edge.IsTop && edge.Partner is { } bottom && !edge.IsRound && !bottom.IsRound)
                widths.Add((edge.Y - bottom.Y, MathF.Min(edge.Length, bottom.Length)));
        }

        return widths;
    }

    /// <summary>
    /// Moves each single-node round edge from the node to the curve's true extremum. A bowl's
    /// outline often turns within the slope tolerance at a node that is not quite the extremum
    /// (Roboto's 'S' bulges 0.2 px past its node at 9 ppem); placing the node would leave the
    /// real ink that far off the grid, and for a stroke under a pixel wide that is a quarter of
    /// it. The node keeps its offset from the extremum, so the extremum is what lands on target.
    /// </summary>
    private static void RefineRoundEdges(OutlineCommand[] commands, List<GridFitter.Edge> edges)
    {
        foreach (var edge in edges)
        {
            if (!edge.IsRound || edge.Points.Count != 1 || edge.Points[0] % 3 != 0)
                continue;

            int index = edge.Points[0] / 3;
            float extreme = edge.Y;
            if (Adjacent(commands, index, incoming: true) is { } before)
                extreme = Beyond(extreme, CubicExtremumY(before), edge.IsTop);
            if (Adjacent(commands, index, incoming: false) is { } after)
                extreme = Beyond(extreme, CubicExtremumY(after), edge.IsTop);
            edge.Y = extreme;
        }
    }

    /// <summary>
    /// Pairs each edge with the nearest opposite edge on its ink side into a stroke, keeping
    /// mutual best matches, as <see cref="GridFitter.Pair"/> does — but a pair of round edges
    /// need not overlap along the stroke: a bowl's outer and inner extrema sit at different
    /// heights (the 'e' has its bar between them), and their extents, which are only the
    /// neighbouring control points' reach, may not meet; the same goes for the short flat many
    /// fonts put at an extremum. They pair when the gap between their extents is within the
    /// stroke's thickness.
    /// </summary>
    private static void Pair(List<GridFitter.Edge> edges, float maxStem)
    {
        var best = new GridFitter.Edge?[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            float nearest = float.MaxValue;
            foreach (var other in edges)
            {
                if (other.IsTop == edge.IsTop)
                    continue;

                float distance = edge.IsTop ? edge.Y - other.Y : other.Y - edge.Y;
                if (distance <= 0f || distance > maxStem || distance >= nearest)
                    continue;

                float overlap = MathF.Min(edge.XMax, other.XMax) - MathF.Max(edge.XMin, other.XMin);
                bool overlaps = overlap > 0f && overlap >= 0.3f * MathF.Min(edge.Length, other.Length);
                bool curved = edge.IsRound || other.IsRound || MathF.Min(edge.Length, other.Length) < MinLoneEdgeLength;
                if (!overlaps && !(curved && overlap > -distance))
                    continue;

                nearest = distance;
                best[i] = other;
            }
        }

        for (int i = 0; i < edges.Count; i++)
        {
            if (best[i] is { } partner && best[edges.IndexOf(partner)] == edges[i])
                edges[i].Partner = partner;
        }
    }

    /// <summary>Merges same-side edges within <see cref="MergeDistance"/> of each other into one
    /// at their length-weighted position; a merged edge is straight if any part was.</summary>
    private static List<GridFitter.Edge> MergeAlignedEdges(List<GridFitter.Edge> edges)
    {
        if (edges.Count < 2)
            return edges;

        edges.Sort((a, b) => a.Y.CompareTo(b.Y));
        var merged = new List<GridFitter.Edge>(edges.Count);
        var used = new bool[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
                continue;

            var seed = edges[i];
            List<GridFitter.Edge>? group = null;
            for (int j = i + 1; j < edges.Count && edges[j].Y - seed.Y <= MergeDistance; j++)
            {
                if (used[j] || edges[j].IsTop != seed.IsTop)
                    continue;
                group ??= [seed];
                group.Add(edges[j]);
                used[j] = true;
            }

            if (group is null)
            {
                merged.Add(seed);
                continue;
            }

            float weight = 0f, position = 0f, xMin = float.MaxValue, xMax = float.MinValue;
            bool round = true;
            var points = new List<int>();
            var controls = new List<(int Control, int Node)>();
            foreach (var edge in group)
            {
                float w = MathF.Max(edge.Length, 1e-3f);
                weight += w;
                position += w * edge.Y;
                xMin = MathF.Min(xMin, edge.XMin);
                xMax = MathF.Max(xMax, edge.XMax);
                round &= edge.IsRound;
                points.AddRange(edge.Points);
                controls.AddRange(edge.TangentControls);
            }

            merged.Add(new GridFitter.Edge(position / weight, xMin, xMax, seed.IsTop, round, points, controls));
        }

        return merged;
    }

    private static float Beyond(float current, float? candidate, bool isTop) =>
        candidate is { } c && (isTop ? c > current : c < current) ? c : current;

    /// <summary>The cubic segment arriving at (or leaving) the endpoint of command <paramref name="index"/>, if it is one.</summary>
    private static (float P0, float C1, float C2, float P3)? Adjacent(OutlineCommand[] commands, int index, bool incoming)
    {
        if (incoming)
        {
            var c = commands[index];
            if (c.Type == OutlineCommandType.MoveTo)
            {
                // The contour's last segment, when it ends exactly on the start.
                int last = index + 1;
                while (last < commands.Length && commands[last].Type is OutlineCommandType.LineTo or OutlineCommandType.CubicTo)
                    last++;
                last--;
                if (last <= index || commands[last].Type != OutlineCommandType.CubicTo
                    || !commands[last].EndX.Equals(c.EndX) || !commands[last].EndY.Equals(c.EndY))
                    return null;
                c = commands[last];
                return (commands[last - 1].EndY, c.Ctrl1Y, c.Ctrl2Y, c.EndY);
            }

            if (c.Type != OutlineCommandType.CubicTo || index == 0)
                return null;
            return (commands[index - 1].EndY, c.Ctrl1Y, c.Ctrl2Y, c.EndY);
        }

        if (index + 1 >= commands.Length || commands[index + 1].Type != OutlineCommandType.CubicTo)
            return null;
        var next = commands[index + 1];
        return (commands[index].EndY, next.Ctrl1Y, next.Ctrl2Y, next.EndY);
    }

    /// <summary>The interior y extremum of a cubic nearest its ends' span, or null when it has none inside (0, 1).</summary>
    private static float? CubicExtremumY((float P0, float C1, float C2, float P3) s)
    {
        double p0 = s.P0, p1 = s.C1, p2 = s.C2, p3 = s.P3;
        double a = -p0 + (3 * p1) - (3 * p2) + p3;
        double b = 2 * (p0 - (2 * p1) + p2);
        double c = p1 - p0;
        float? result = null;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) >= 1e-12)
                Take(-c / b);
            return result;
        }

        double discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0)
            return null;
        double root = Math.Sqrt(discriminant);
        Take((-b + root) / (2 * a));
        Take((-b - root) / (2 * a));
        return result;

        void Take(double t)
        {
            if (!(t > 0 && t < 1))
                return;
            double mt = 1 - t;
            float value = (float)((mt * mt * mt * p0) + (3 * mt * mt * t * p1) + (3 * mt * t * t * p2) + (t * t * t * p3));
            // Two interior extrema: keep the one farther from the segment's end span, the bulge.
            result = result is { } r && Math.Abs(r - p3) > Math.Abs(value - p3) ? r : value;
        }
    }

    private static bool Anchored(GridFitter.Edge top) => top.Hinted is not null || top.Partner!.Hinted is not null;

    private static float StemLength(GridFitter.Edge top) => MathF.Min(top.Length, top.Partner!.Length);

    /// <summary>
    /// The global shift in [-½, ½] that moves the strokes least once each is placed on the
    /// grid, weighted by stroke length, with a small penalty on the shift itself so a glyph with
    /// nothing to gain stays put. It mirrors the placement pass: the longest stroke (the anchor)
    /// is placed under the shift, and every other stroke is placed from where the anchor's own
    /// move carries it, so stroke pitches round as a whole rather than each stroke rounding on
    /// its own. Evaluated on a fixed grid of candidates, so it is deterministic.
    /// </summary>
    private static float Phase(List<GridFitter.Edge> stems, StemFitParameters p)
    {
        if (stems.Count == 0)
            return 0f;

        GridFitter.Edge anchor = stems[0];
        foreach (var stem in stems)
        {
            if (StemLength(stem) > StemLength(anchor))
                anchor = stem;
        }

        float best = 0f, bestCost = float.MaxValue;
        for (int i = -PhaseSteps / 2; i <= PhaseSteps / 2; i++)
        {
            float s = (float)i / PhaseSteps;
            float anchorCentre = (anchor.Y + anchor.Partner!.Y) / 2f;
            float carried = PlacedCentre(anchorCentre + s, anchor, p) - anchorCentre;
            float cost = (PhaseShiftPenalty * MathF.Abs(s)) + (StemLength(anchor) * MathF.Abs(carried));
            foreach (var top in stems)
            {
                if (top == anchor)
                    continue;
                float centre = (top.Y + top.Partner!.Y) / 2f;
                cost += StemLength(top) * MathF.Abs(PlacedCentre(centre + carried, top, p) - centre);
            }

            // Ties go to the smaller shift, which the ascending |s| scan from zero below picks.
            if (cost < bestCost - 1e-6f || (cost < bestCost + 1e-6f && MathF.Abs(s) < MathF.Abs(best)))
            {
                bestCost = cost;
                best = s;
            }
        }

        return best;
    }

    /// <summary>Where a stroke's centre lands when placed around <paramref name="centre"/>.</summary>
    private static float PlacedCentre(float centre, GridFitter.Edge top, StemFitParameters p)
    {
        var (fitted, span) = StemSpan(top.Y - top.Partner!.Y, p);
        return PlacedBottom(centre, fitted, span) + (fitted / 2f);
    }

    /// <summary>
    /// Places one stroke. A stroke with an edge already placed hangs its other edge off it. The
    /// first free stroke is the anchor, placed where the map carries it. Later free strokes
    /// keep the anchor's phase: the distance between corresponding edges rounds to whole
    /// pixels, so the stems of an 'h' or 'm' all sit the same way in their columns. In mono,
    /// where a stroke widens to a whole pixel, it is the distance from the anchor's far edge to
    /// the stroke's far edge that rounds, so the widening grows into the counter rather than
    /// the glyph's width — a 0.8 px bowl pair whose outer edges are 4.4 px apart stays 4 px
    /// wide (1-2-1), not 5.
    /// </summary>
    private static void PlaceStem(GridFitter.Edge top, GridFitter.Edge bottom, StemFitParameters p, MonotoneMap map, int feature, ref GridFitter.Edge? anchor)
    {
        if (top.Hinted is not null && bottom.Hinted is not null)
            return;

        var (width, span) = StemSpan(top.Y - bottom.Y, p);

        if (top.Hinted is { } fixedTop)
        {
            if (map.TryAdd(bottom.Y, fixedTop - width, feature, zone: false))
                bottom.Hinted = fixedTop - width;
            return;
        }

        if (bottom.Hinted is { } fixedBottom)
        {
            if (map.TryAdd(top.Y, fixedBottom + width, feature, zone: false))
                top.Hinted = fixedBottom + width;
            return;
        }

        float original = (top.Y + bottom.Y) / 2f;
        float centre = map.Eval(original);
        float ideal;
        if (anchor is { Hinted: { } anchorTop, Partner.Hinted: { } anchorBottom } && anchor != top)
        {
            if (!p.Mono)
                ideal = anchorBottom + GridMath.Round(bottom.Y - anchor.Partner.Y);
            else if (original > (anchor.Y + anchor.Partner.Y) / 2f)
                ideal = anchorBottom + GridMath.Round(top.Y - anchor.Partner.Y) - width;
            else
                ideal = anchorTop - GridMath.Round(anchor.Y - bottom.Y);
        }
        else
        {
            ideal = PlacedBottom(centre, width, span);
        }

        float step = centre - (width / 2f) >= ideal ? 1f : -1f;
        ReadOnlySpan<float> candidates = [ideal, ideal + step, ideal - step];
        foreach (float candidate in candidates)
        {
            if (map.TryAdd(bottom.Y, candidate, top.Y, candidate + width, feature))
            {
                bottom.Hinted = candidate;
                top.Hinted = candidate + width;
                anchor ??= top;
                return;
            }
        }
    }

    /// <summary>
    /// A stroke's fitted thickness and the whole-pixel span it is positioned as: in mono both
    /// are the rounded width (at least one pixel); in grayscale the thickness is kept (snapped
    /// to the standard when close, so equal strokes stay equal) and only the span is rounded.
    /// </summary>
    private static (float Width, float Span) StemSpan(float width, StemFitParameters p)
    {
        float standard = p.StandardStem;
        float basis = standard > 0f && MathF.Abs(width - standard) <= StandardStemTolerance * standard ? standard : width;
        float span = MathF.Max(1f, GridMath.Round(basis));
        return p.Mono ? (span, span) : (basis, span);
    }

    /// <summary>
    /// Where a stroke's bottom edge goes. A stroke under a pixel thick is centred in the pixel
    /// its centre falls in, so its ink is one column (or row) at its true coverage. A thicker
    /// stroke gets one edge on a pixel boundary and the fraction over a whole pixel on the other
    /// side — one solid run and one fringe, not two fringes — on whichever side moves its
    /// centre less. A whole-pixel stroke (mono) is centred on the span nearest its centre.
    /// </summary>
    private static float PlacedBottom(float centre, float width, float span)
    {
        if (width < 1f)
            return MathF.Floor(centre) + ((1f - width) / 2f);

        float spanBottom = GridMath.Round(centre - (span / 2f));
        if (width.Equals(span))
            return spanBottom;

        float bottomOnGrid = GridMath.Round(centre - (width / 2f));
        float topOnGrid = GridMath.Round(centre + (width / 2f)) - width;
        float errorBottom = MathF.Abs(bottomOnGrid + (width / 2f) - centre);
        float errorTop = MathF.Abs(topOnGrid + (width / 2f) - centre);
        return errorTop < errorBottom ? topOnGrid : bottomOnGrid;
    }

    /// <summary>The pixel line a zone snaps the edge to, or null when it is in no zone.</summary>
    private static float? ZoneTarget(GridFitter.Edge edge, StemFitParameters p)
    {
        float? target = null;
        float best = float.MaxValue;
        foreach (var zone in p.Zones)
        {
            if (zone.IsTop != edge.IsTop)
                continue;

            float low = MathF.Min(zone.Flat, zone.Overshoot) - p.ZoneTolerance;
            float high = MathF.Max(zone.Flat, zone.Overshoot) + p.ZoneTolerance;
            if (edge.Y < low || edge.Y > high)
                continue;

            float toFlat = MathF.Abs(edge.Y - zone.Flat);
            float distance = MathF.Min(toFlat, MathF.Abs(edge.Y - zone.Overshoot));
            if (distance >= best)
                continue;

            best = distance;
            target = toFlat <= MathF.Abs(edge.Y - zone.Overshoot) ? zone.SnappedFlat : zone.SnappedOvershoot;
        }

        return target;
    }

    /// <summary>Moves every point: placed edges by their own shift, the rest through the map.</summary>
    private static GlyphOutline Apply(OutlineCommand[] commands, List<GridFitter.Edge> edges, List<(int Alias, int Canonical)> aliases, MonotoneMap map)
    {
        int slotCount = commands.Length * 3;
        float[] yBuffer = ArrayPool<float>.Shared.Rent(slotCount);
        bool[] placedBuffer = ArrayPool<bool>.Shared.Rent(slotCount);
        try
        {
            return Apply(commands, edges, aliases, map, yBuffer.AsSpan(0, slotCount), placedBuffer.AsSpan(0, slotCount));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(yBuffer);
            ArrayPool<bool>.Shared.Return(placedBuffer);
        }
    }

    private static GlyphOutline Apply(
        OutlineCommand[] commands, List<GridFitter.Edge> edges, List<(int Alias, int Canonical)> aliases, MonotoneMap map, Span<float> y, Span<bool> placed)
    {
        placed.Clear();

        foreach (var edge in edges)
        {
            if (edge.Hinted is not { } hinted)
                continue;

            foreach (int slot in edge.Points)
            {
                float original = GridFitter.Y(commands, slot);
                y[slot] = original.Equals(edge.Y) ? hinted : original + (hinted - edge.Y);
                placed[slot] = true;
            }

            // A control on the tangent at the edge's end goes level with its point, so the
            // curve cannot bulge a sliver past the pixel line the edge now sits on.
            foreach (var (control, node) in edge.TangentControls)
            {
                if (placed[control])
                    continue;
                y[control] = y[node];
                placed[control] = true;
            }
        }

        for (int i = 0; i < commands.Length; i++)
        {
            int slots = commands[i].Type == OutlineCommandType.CubicTo ? 3 : 1;
            for (int k = 0; k < slots; k++)
            {
                int slot = (i * 3) + k;
                if (!placed[slot])
                    y[slot] = map.Eval(GridFitter.Y(commands, slot));
            }
        }

        foreach (var (alias, canonical) in aliases)
            y[alias] = y[canonical];

        for (int slot = 0; slot < y.Length; slot++)
            y[slot] = MathF.Round(y[slot] * Resolution) / Resolution;

        var fitted = new OutlineCommand[commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            var c = commands[i];
            fitted[i] = c.Type == OutlineCommandType.CubicTo
                ? c with { EndY = y[i * 3], Ctrl1Y = y[(i * 3) + 1], Ctrl2Y = y[(i * 3) + 2] }
                : c with { EndY = y[i * 3] };
        }

        return GlyphOutline.FromCommands(fitted);
    }

    /// <summary>
    /// The fitted coordinates placed so far, as a non-decreasing piecewise-linear map from
    /// original to fitted coordinate. Outside the keys a coordinate shifts with the nearest key;
    /// with no keys at all it shifts by <see cref="BaseShift"/>.
    /// </summary>
    private sealed class MonotoneMap(float maxShift, float minCounter)
    {
        private readonly List<float> _from = [];
        private readonly List<float> _to = [];
        private readonly List<int> _feature = [];
        private readonly List<bool> _zone = [];

        public int Count => _from.Count;

        /// <summary>The global shift applied where no key decides.</summary>
        public float BaseShift { get; set; }

        public float Eval(float y)
        {
            int n = _from.Count;
            if (n == 0)
                return y + BaseShift;

            int index = _from.BinarySearch(y);
            if (index >= 0)
                return _to[index];

            int above = ~index;
            if (above == 0)
                return y + (_to[0] - _from[0]);
            if (above == n)
                return y + (_to[n - 1] - _from[n - 1]);

            float t = (y - _from[above - 1]) / (_from[above] - _from[above - 1]);
            return _to[above - 1] + (t * (_to[above] - _to[above - 1]));
        }

        public bool TryAdd(float from, float to, int feature, bool zone) => TryAdd([(from, to)], feature, zone);

        public bool TryAdd(float from1, float to1, float from2, float to2, int feature) => TryAdd([(from1, to1), (from2, to2)], feature, zone: false);

        /// <summary>
        /// Adds the keys together, or none of them when one would move further than the cap,
        /// reorder two fitted coordinates, close a counter, or (zones excepted) collapse a gap
        /// that was under the counter threshold to less than half of itself.
        /// </summary>
        private bool TryAdd(ReadOnlySpan<(float From, float To)> keys, int feature, bool zone)
        {
            foreach (var (f, t) in keys)
            {
                if (MathF.Abs(t - f) > maxShift)
                    return false;
            }

            Span<int> inserted = stackalloc int[keys.Length];
            int insertedCount = 0;
            bool accepted = true;
            foreach (var (f, t) in keys)
            {
                int index = _from.BinarySearch(f);
                if (index >= 0)
                {
                    if (MathF.Abs(_to[index] - t) > 1e-4f)
                    {
                        accepted = false;
                        break;
                    }

                    continue;
                }

                _from.Insert(~index, f);
                _to.Insert(~index, t);
                _feature.Insert(~index, feature);
                _zone.Insert(~index, zone);
                inserted[insertedCount++] = ~index;
            }

            for (int i = 1; accepted && i < _from.Count; i++)
            {
                float gap = _from[i] - _from[i - 1];
                float minimum;
                if (_feature[i] == _feature[i - 1])
                    minimum = 0f;
                else if (_zone[i] && _zone[i - 1])
                    minimum = 0f;
                else if (gap >= minCounter)
                    minimum = 1f;
                else
                    minimum = 0.5f * gap;

                if (_to[i] - _to[i - 1] < minimum - 1e-4f)
                    accepted = false;
            }

            if (!accepted)
            {
                for (int k = insertedCount - 1; k >= 0; k--)
                {
                    _from.RemoveAt(inserted[k]);
                    _to.RemoveAt(inserted[k]);
                    _feature.RemoveAt(inserted[k]);
                    _zone.RemoveAt(inserted[k]);
                }
            }

            return accepted;
        }
    }
}
