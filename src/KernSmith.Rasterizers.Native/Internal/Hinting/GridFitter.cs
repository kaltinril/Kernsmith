using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>
/// Vertical (y-only) grid fitting of a pixel-space outline, Y up: horizontal edges are found,
/// snapped to blue zones or paired into strokes rounded to whole pixels, and every other point
/// is moved by interpolating between the fitted edges.
/// </summary>
/// <remarks>
/// <para>
/// An <em>edge</em> is a run of the outline that travels horizontally — a near-horizontal line
/// or flat curve, or a curve point whose tangent is horizontal (the top of an 'o'). Which side
/// its ink is on follows from its direction of travel and the outline's winding, so the fitter
/// works for TrueType's clockwise and CFF's counter-clockwise outer contours alike.
/// </para>
/// <para>
/// Fitting places edges in priority order: edges inside a blue zone snap to the zone's pixel
/// line; a stroke (a bottom edge and the top edge above it, within <see cref="ScaledHintMetrics.MaxStem"/>,
/// overlapping horizontally) gets a whole-pixel thickness of at least one pixel, measured from
/// an already-snapped edge when it has one and centred on its interpolated position otherwise;
/// remaining flat edges round to the nearest pixel line. An edge is only placed if the fitted
/// heights stay in the same order as the originals, so nothing collapses or crosses.
/// </para>
/// <para>
/// INVARIANT: every point that is not on a placed edge moves by one monotone piecewise-linear
/// function of its height, through the placed edges. A strictly increasing map of y alone
/// cannot make an outline intersect itself, which per-contour interpolation cannot promise.
/// X coordinates are never touched, so advances and horizontal positions are unchanged.
/// </para>
/// <para>
/// INVARIANT: no placed edge moves more than <see cref="MaxShift"/> (one pixel). Every other
/// point moves by a blend of two neighbouring edges' shifts, or with the nearest end, so no
/// point moves further — light hinting snaps a glyph to the grid, it does not reshape it. A
/// placement that would need more (a stroke stacked on a stroke that already moved, a zone
/// or rounding that pulls a feature a pixel or more) is refused and the edge is interpolated.
/// </para>
/// </remarks>
internal static class GridFitter
{
    /// <summary>Largest |dy/dx| that still counts as horizontal (about 4°).</summary>
    private const float SlopeTolerance = 0.07f;

    /// <summary>Shortest edge, in pixels, worth fitting.</summary>
    private const float MinEdgeLength = 0.2f;

    /// <summary>Adjacent horizontal runs closer than this in height are one edge.</summary>
    private const float MergeTolerance = 0.25f;

    /// <summary>The furthest, in pixels, fitting may move an edge from its unhinted height.</summary>
    private const float MaxShift = 1f;

    /// <summary>
    /// The furthest, in pixels, a stroke may be from the font's standard thickness and still be
    /// fitted as one: past that it is a different stroke, and its own width is rounded.
    /// </summary>
    private const float MaxStandardStemDeviation = 0.5f;

    /// <summary>
    /// Fits an outline to the pixel grid.
    /// </summary>
    /// <param name="outline">The outline in pixels, Y up (baseline at y = 0).</param>
    /// <param name="metrics">The font's zones and stem width at this size.</param>
    /// <returns>The fitted outline, or null when there was nothing to fit.</returns>
    public static GlyphOutline? Fit(GlyphOutline outline, ScaledHintMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentNullException.ThrowIfNull(metrics);

        var commands = outline.Commands;
        var edges = FindEdges(commands, out var aliases);
        if (edges.Count == 0)
            return null;

        Pair(edges, metrics.MaxStem);
        var keys = new KeyMap();

        // 1. Blue zones.
        foreach (var edge in edges)
        {
            if (ZoneTarget(edge, metrics) is { } target && keys.TryAddZone(edge.Y, target))
                edge.Hinted = target;
        }

        // 2. Strokes: those hanging off a snapped edge first, then the longest.
        var stems = edges.Where(e => e.IsTop && e.Partner is not null).ToList();
        stems.Sort((a, b) =>
        {
            int anchored = Anchored(b).CompareTo(Anchored(a));
            return anchored != 0 ? anchored : StemLength(b).CompareTo(StemLength(a));
        });
        foreach (var top in stems)
            PlaceStem(top, top.Partner!, metrics, keys);

        // 3. Lone flat edges round to the nearest pixel line where they now sit.
        foreach (var edge in edges)
        {
            if (edge.Hinted is not null || edge.IsRound)
                continue;

            float target = GridMath.Round(keys.Eval(edge.Y));
            if (keys.TryAdd(edge.Y, target))
                edge.Hinted = target;
        }

        return keys.Count == 0 ? null : Apply(commands, edges, aliases, keys);
    }

    /// <summary>The thickness of every horizontal stroke in an outline, in its own units
    /// (tolerances assume roughly pixel-sized units).</summary>
    public static List<float> HorizontalStemWidths(GlyphOutline outline, float maxStem)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var edges = FindEdges(outline.Commands, out _);
        Pair(edges, maxStem);

        var widths = new List<float>();
        foreach (var edge in edges)
        {
            if (edge.IsTop && edge.Partner is { } bottom)
                widths.Add(edge.Y - bottom.Y);
        }

        return widths;
    }

    private static bool Anchored(Edge top) => top.Hinted is not null || top.Partner!.Hinted is not null;

    private static float StemLength(Edge top) => MathF.Min(top.Length, top.Partner!.Length);

    private static void PlaceStem(Edge top, Edge bottom, ScaledHintMetrics metrics, KeyMap keys)
    {
        if (top.Hinted is not null && bottom.Hinted is not null)
            return;

        float width = StemWidth(top.Y - bottom.Y, metrics.StandardStem);

        if (top.Hinted is { } fixedTop)
        {
            if (keys.TryAdd(bottom.Y, fixedTop - width))
                bottom.Hinted = fixedTop - width;
            return;
        }

        if (bottom.Hinted is { } fixedBottom)
        {
            if (keys.TryAdd(top.Y, fixedBottom + width))
                top.Hinted = fixedBottom + width;
            return;
        }

        // Centre the rounded stroke on where its middle now sits, falling back to the next
        // pixel line over if that would crowd an edge already placed.
        float ideal = keys.Eval((top.Y + bottom.Y) / 2f) - (width / 2f);
        float nearest = GridMath.Round(ideal);
        float step = ideal >= nearest ? 1f : -1f;
        ReadOnlySpan<float> candidates = [nearest, nearest + step, nearest - step];
        foreach (float candidate in candidates)
        {
            if (keys.TryAdd(bottom.Y, candidate, top.Y, candidate + width))
            {
                bottom.Hinted = candidate;
                top.Hinted = candidate + width;
                return;
            }
        }
    }

    /// <summary>
    /// A stroke's fitted thickness: whole pixels, at least one, rounded from the font's standard
    /// thickness when the stroke is close to it so equal strokes stay equal. Close is relative
    /// to the standard, but never more than <see cref="MaxStandardStemDeviation"/>: at large
    /// sizes a percentage of the standard spans pixels, and fitting a visibly thinner stroke
    /// at the standard width moves its edges by a pixel or more.
    /// </summary>
    private static float StemWidth(float width, float standard)
    {
        float tolerance = MathF.Min(0.1f + (0.15f * standard), MaxStandardStemDeviation);
        float basis = standard > 0f && MathF.Abs(width - standard) <= tolerance ? standard : width;
        return MathF.Max(1f, GridMath.Round(basis));
    }

    /// <summary>The pixel line a zone snaps the edge to, or null when it is in no zone.</summary>
    private static float? ZoneTarget(Edge edge, ScaledHintMetrics metrics)
    {
        float? target = null;
        float best = float.MaxValue;
        foreach (var zone in metrics.Zones)
        {
            if (zone.IsTop != edge.IsTop)
                continue;

            float low = MathF.Min(zone.Flat, zone.Overshoot) - metrics.ZoneTolerance;
            float high = MathF.Max(zone.Flat, zone.Overshoot) + metrics.ZoneTolerance;
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

    /// <summary>
    /// Pairs each edge with the nearest opposite edge on its ink side that overlaps it
    /// horizontally, keeping only mutual best matches: those are the strokes.
    /// </summary>
    private static void Pair(List<Edge> edges, float maxStem)
    {
        var best = new Edge?[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            float nearest = float.MaxValue;
            foreach (var other in edges)
            {
                if (other.IsTop == edge.IsTop)
                    continue;

                // A top edge's ink is below it, so its stroke ends at a bottom edge further down.
                float distance = edge.IsTop ? edge.Y - other.Y : other.Y - edge.Y;
                if (distance <= 0f || distance > maxStem || distance >= nearest)
                    continue;

                float overlap = MathF.Min(edge.XMax, other.XMax) - MathF.Max(edge.XMin, other.XMin);
                if (overlap <= 0f || overlap < 0.3f * MathF.Min(edge.Length, other.Length))
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

    /// <summary>Moves every point: placed edges by their own shift, the rest through the map.</summary>
    private static GlyphOutline Apply(OutlineCommand[] commands, List<Edge> edges, List<(int Alias, int Canonical)> aliases, KeyMap keys)
    {
        var y = new float[commands.Length * 3];
        var placed = new bool[y.Length];

        foreach (var edge in edges)
        {
            if (edge.Hinted is not { } hinted)
                continue;

            foreach (int slot in edge.Points)
            {
                float original = Y(commands, slot);
                y[slot] = original.Equals(edge.Y) ? hinted : original + (hinted - edge.Y);
                placed[slot] = true;
            }

            // A control on the horizontal tangent at the edge's end goes exactly level with its
            // point, so the curve cannot bulge a sliver past the pixel line the edge now sits on.
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
                    y[slot] = keys.Eval(Y(commands, slot));
            }
        }

        foreach (var (alias, canonical) in aliases)
            y[alias] = y[canonical];

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

    // ---- Edge detection ---------------------------------------------------------------------

    /// <summary>
    /// Finds the horizontal edges of every contour. Points are addressed as slots,
    /// <c>command * 3 + k</c> with k = 0 the endpoint, 1 and 2 a cubic's controls.
    /// </summary>
    /// <param name="commands">The outline.</param>
    /// <param name="aliases">Slots that duplicate a contour's start point (its Close, and a last
    /// segment ending exactly on the start) and must move with it.</param>
    private static List<Edge> FindEdges(OutlineCommand[] commands, out List<(int Alias, int Canonical)> aliases)
    {
        aliases = [];
        var edges = new List<Edge>();
        bool clockwise = SignedArea(commands) <= 0f;

        int i = 0;
        while (i < commands.Length)
        {
            if (commands[i].Type != OutlineCommandType.MoveTo)
            {
                i++;
                continue;
            }

            int end = i + 1;
            while (end < commands.Length && commands[end].Type is OutlineCommandType.LineTo or OutlineCommandType.CubicTo)
                end++;

            ContourEdges(commands, i, end, clockwise, edges, aliases);
            i = end < commands.Length && commands[end].Type == OutlineCommandType.Close ? end + 1 : end;
        }

        return edges;
    }

    private static void ContourEdges(
        OutlineCommand[] commands, int start, int end, bool clockwise, List<Edge> edges, List<(int Alias, int Canonical)> aliases)
    {
        int startSlot = start * 3;
        if (end < commands.Length && commands[end].Type == OutlineCommandType.Close)
            aliases.Add((end * 3, startSlot));

        var segments = new List<Segment>(end - start);
        int previous = startSlot;
        for (int j = start + 1; j < end; j++)
        {
            int slot = j * 3;
            bool closesOnStart = j == end - 1 && SamePoint(commands, slot, startSlot);
            if (closesOnStart)
                aliases.Add((slot, startSlot));

            int to = closesOnStart ? startSlot : slot;
            segments.Add(commands[j].Type == OutlineCommandType.CubicTo
                ? new Segment(previous, to, slot + 1, slot + 2)
                : new Segment(previous, to, -1, -1));
            previous = to;
        }

        if (previous != startSlot)
            segments.Add(new Segment(previous, startSlot, -1, -1));

        int n = segments.Count;
        if (n < 2)
            return;

        var horizontal = new bool[n];
        for (int k = 0; k < n; k++)
            horizontal[k] = IsHorizontal(commands, segments[k]);

        var runs = new List<Run>();
        for (int k = 0; k < n; k++)
        {
            var segment = segments[k];
            if (horizontal[k])
            {
                var run = new Run(segment.From, segment.To, X(commands, segment.To) - X(commands, segment.From), round: segment.IsCubic);
                run.AddPoint(commands, segment.From, onCurve: true);
                run.AddPoint(commands, segment.To, onCurve: true);
                if (segment.IsCubic)
                {
                    run.AddPoint(commands, segment.Ctrl1, onCurve: false);
                    run.AddPoint(commands, segment.Ctrl2, onCurve: false);
                }

                var before = segments[(k + n - 1) % n];
                if (before.IsCubic)
                    run.AddTangentControl(commands, before.Ctrl2, segment.From);
                var after = segments[(k + 1) % n];
                if (after.IsCubic)
                    run.AddTangentControl(commands, after.Ctrl1, segment.To);

                runs.Add(run);
            }

            var next = segments[(k + 1) % n];
            if (!horizontal[k] && !horizontal[(k + 1) % n] && HorizontalTangent(commands, segment, next, out float direction, out float xLow, out float xHigh))
            {
                var run = new Run(segment.To, segment.To, direction, round: true);
                run.AddPoint(commands, segment.To, onCurve: true);
                run.Extend(xLow, xHigh);
                if (segment.IsCubic)
                    run.AddTangentControl(commands, segment.Ctrl2, segment.To);
                if (next.IsCubic)
                    run.AddTangentControl(commands, next.Ctrl1, segment.To);
                runs.Add(run);
            }
        }

        // Join consecutive runs that continue each other in the same direction at the same height.
        var merged = new List<Run>(runs.Count);
        foreach (var run in runs)
        {
            if (merged.Count > 0 && merged[^1].Continues(run))
                merged[^1].Absorb(run);
            else
                merged.Add(run);
        }

        if (merged.Count > 1 && merged[^1].Continues(merged[0]))
        {
            merged[^1].Absorb(merged[0]);
            merged.RemoveAt(0);
        }

        foreach (var run in merged)
        {
            if (run.XMax - run.XMin < MinEdgeLength)
                continue;

            edges.Add(new Edge(run.MeanY, run.XMin, run.XMax, isTop: run.Direction > 0f == clockwise, run.Round, run.Points, run.TangentControls));
        }
    }

    private static bool IsHorizontal(OutlineCommand[] commands, Segment segment)
    {
        float dx = MathF.Abs(X(commands, segment.To) - X(commands, segment.From));
        if (dx < 1e-4f)
            return false;

        float yFrom = Y(commands, segment.From), yTo = Y(commands, segment.To);
        float low = MathF.Min(yFrom, yTo), high = MathF.Max(yFrom, yTo);
        if (segment.IsCubic)
        {
            float c1 = Y(commands, segment.Ctrl1), c2 = Y(commands, segment.Ctrl2);
            low = MathF.Min(low, MathF.Min(c1, c2));
            high = MathF.Max(high, MathF.Max(c1, c2));
        }

        return high - low <= SlopeTolerance * dx;
    }

    /// <summary>
    /// True when the curve passes horizontally through the point joining two segments (a
    /// curve extremum such as the top of an 'o'): both tangents near horizontal, same way.
    /// </summary>
    private static bool HorizontalTangent(
        OutlineCommand[] commands, Segment incoming, Segment outgoing, out float direction, out float xLow, out float xHigh)
    {
        int node = incoming.To;
        float nx = X(commands, node), ny = Y(commands, node);

        int before = incoming.IsCubic ? FirstDistinct(commands, node, incoming.Ctrl2, incoming.Ctrl1, incoming.From) : incoming.From;
        int after = outgoing.IsCubic ? FirstDistinct(commands, node, outgoing.Ctrl1, outgoing.Ctrl2, outgoing.To) : outgoing.To;

        float inX = nx - X(commands, before), inY = ny - Y(commands, before);
        float outX = X(commands, after) - nx, outY = Y(commands, after) - ny;

        direction = MathF.Sign(inX);
        xLow = MathF.Min(nx, MathF.Min(X(commands, before), X(commands, after)));
        xHigh = MathF.Max(nx, MathF.Max(X(commands, before), X(commands, after)));

        return MathF.Abs(inX) > 1e-4f
            && MathF.Abs(outX) > 1e-4f
            && MathF.Sign(inX) == MathF.Sign(outX)
            && MathF.Abs(inY) <= SlopeTolerance * MathF.Abs(inX)
            && MathF.Abs(outY) <= SlopeTolerance * MathF.Abs(outX);
    }

    /// <summary>The first slot whose point differs from <paramref name="node"/>'s, for a tangent.</summary>
    private static int FirstDistinct(OutlineCommand[] commands, int node, int a, int b, int c) =>
        !SamePoint(commands, a, node) ? a : !SamePoint(commands, b, node) ? b : c;

    /// <summary>
    /// Twice the outline's signed area over its point polygon (controls included), Y up:
    /// negative when the outer contours run clockwise, as TrueType's do.
    /// </summary>
    private static float SignedArea(OutlineCommand[] commands)
    {
        double area = 0;
        double startX = 0, startY = 0, lastX = 0, lastY = 0;
        bool open = false;

        foreach (var c in commands)
        {
            switch (c.Type)
            {
                case OutlineCommandType.MoveTo:
                    if (open)
                        area += (lastX * startY) - (startX * lastY);
                    startX = lastX = c.EndX;
                    startY = lastY = c.EndY;
                    open = true;
                    break;
                case OutlineCommandType.CubicTo:
                    Add(c.Ctrl1X, c.Ctrl1Y);
                    Add(c.Ctrl2X, c.Ctrl2Y);
                    Add(c.EndX, c.EndY);
                    break;
                default:
                    Add(c.EndX, c.EndY);
                    break;
            }
        }

        if (open)
            area += (lastX * startY) - (startX * lastY);

        return (float)area;

        void Add(double x, double y)
        {
            area += (lastX * y) - (x * lastY);
            lastX = x;
            lastY = y;
        }
    }

    private static bool SamePoint(OutlineCommand[] commands, int a, int b) =>
        X(commands, a).Equals(X(commands, b)) && Y(commands, a).Equals(Y(commands, b));

    private static float X(OutlineCommand[] commands, int slot)
    {
        var c = commands[slot / 3];
        return (slot % 3) switch { 0 => c.EndX, 1 => c.Ctrl1X, _ => c.Ctrl2X };
    }

    private static float Y(OutlineCommand[] commands, int slot)
    {
        var c = commands[slot / 3];
        return (slot % 3) switch { 0 => c.EndY, 1 => c.Ctrl1Y, _ => c.Ctrl2Y };
    }

    /// <summary>One segment of a contour, by slot; controls are -1 for a line.</summary>
    private readonly record struct Segment(int From, int To, int Ctrl1, int Ctrl2)
    {
        public bool IsCubic => Ctrl1 >= 0;
    }

    /// <summary>A horizontal stretch of one contour while it is being assembled.</summary>
    private sealed class Run(int startNode, int endNode, float direction, bool round)
    {
        private float _ySum;
        private int _onCurve;

        public int StartNode { get; } = startNode;

        public int EndNode { get; private set; } = endNode;

        public float Direction { get; } = direction;

        public bool Round { get; private set; } = round;

        public List<int> Points { get; } = [];

        public List<(int Control, int Node)> TangentControls { get; } = [];

        public float XMin { get; private set; } = float.MaxValue;

        public float XMax { get; private set; } = float.MinValue;

        public float MeanY => _ySum / _onCurve;

        public void AddPoint(OutlineCommand[] commands, int slot, bool onCurve)
        {
            if (!Points.Contains(slot))
                Points.Add(slot);

            Extend(X(commands, slot), X(commands, slot));
            if (onCurve)
            {
                _ySum += Y(commands, slot);
                _onCurve++;
            }
        }

        /// <summary>Records a neighbouring curve's control when it leaves <paramref name="node"/> horizontally.</summary>
        public void AddTangentControl(OutlineCommand[] commands, int control, int node)
        {
            float dx = MathF.Abs(X(commands, control) - X(commands, node));
            float dy = MathF.Abs(Y(commands, control) - Y(commands, node));
            if (dx > 1e-4f && dy <= SlopeTolerance * dx)
                TangentControls.Add((control, node));
        }

        public void Extend(float low, float high)
        {
            XMin = MathF.Min(XMin, low);
            XMax = MathF.Max(XMax, high);
        }

        public bool Continues(Run next) =>
            EndNode == next.StartNode
            && MathF.Sign(Direction) == MathF.Sign(next.Direction)
            && MathF.Abs(MeanY - next.MeanY) <= MergeTolerance;

        public void Absorb(Run other)
        {
            foreach (int slot in other.Points)
            {
                if (!Points.Contains(slot))
                    Points.Add(slot);
            }

            TangentControls.AddRange(other.TangentControls);
            _ySum += other._ySum;
            _onCurve += other._onCurve;
            Extend(other.XMin, other.XMax);
            Round &= other.Round;
            EndNode = other.EndNode;
        }
    }

    /// <summary>A horizontal edge: where it is, which side its ink is on, and where it goes.</summary>
    private sealed class Edge(float y, float xMin, float xMax, bool isTop, bool isRound, List<int> points, List<(int Control, int Node)> tangentControls)
    {
        /// <summary>Its height before fitting (mean of its on-curve points).</summary>
        public float Y { get; } = y;

        public float XMin { get; } = xMin;

        public float XMax { get; } = xMax;

        public float Length => XMax - XMin;

        /// <summary>True when the ink is below the edge (the top of a stroke).</summary>
        public bool IsTop { get; } = isTop;

        /// <summary>True when the edge is a curve's horizontal stretch rather than a straight line.</summary>
        public bool IsRound { get; } = isRound;

        public List<int> Points { get; } = points;

        /// <summary>Controls of the neighbouring curves that leave the edge horizontally, with
        /// the edge point each belongs to.</summary>
        public List<(int Control, int Node)> TangentControls { get; } = tangentControls;

        /// <summary>The opposite edge of the stroke this edge bounds, if any.</summary>
        public Edge? Partner { get; set; }

        /// <summary>Its fitted height, once placed.</summary>
        public float? Hinted { get; set; }
    }

    /// <summary>
    /// The fitted heights placed so far, as an increasing piecewise-linear map from original to
    /// fitted height; heights outside it shift with the nearest end. It is strictly increasing
    /// except between two zone edges, which may share a pixel line: a suppressed overshoot
    /// lands on its zone's flat line by design.
    /// </summary>
    private sealed class KeyMap
    {
        private readonly List<float> _from = [];
        private readonly List<float> _to = [];
        private readonly List<bool> _zone = [];

        public int Count => _from.Count;

        public float Eval(float y)
        {
            int n = _from.Count;
            if (n == 0)
                return y;

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

        public bool TryAdd(float from, float to) => TryAdd([(from, to)], zone: false);

        public bool TryAddZone(float from, float to) => TryAdd([(from, to)], zone: true);

        public bool TryAdd(float from1, float to1, float from2, float to2) => TryAdd([(from1, to1), (from2, to2)], zone: false);

        /// <summary>
        /// Adds the keys together, or none of them when one would move its height more than
        /// <see cref="MaxShift"/>, or bring two fitted heights closer than half their original
        /// distance (capped at half a pixel) or reorder them.
        /// </summary>
        private bool TryAdd(ReadOnlySpan<(float From, float To)> keys, bool zone)
        {
            foreach (var (f, t) in keys)
            {
                if (MathF.Abs(t - f) > MaxShift)
                    return false;
            }

            var from = new List<float>(_from);
            var to = new List<float>(_to);
            var zones = new List<bool>(_zone);

            foreach (var (f, t) in keys)
            {
                int index = from.BinarySearch(f);
                if (index >= 0)
                {
                    // The same height placed twice must agree.
                    if (MathF.Abs(to[index] - t) > 1e-4f)
                        return false;
                    continue;
                }

                from.Insert(~index, f);
                to.Insert(~index, t);
                zones.Insert(~index, zone);
            }

            for (int i = 1; i < from.Count; i++)
            {
                float minimum = zones[i] && zones[i - 1] ? 0f : 0.5f * MathF.Min(from[i] - from[i - 1], 1f);
                if (to[i] - to[i - 1] < minimum)
                    return false;
            }

            _from.Clear();
            _from.AddRange(from);
            _to.Clear();
            _to.AddRange(to);
            _zone.Clear();
            _zone.AddRange(zones);
            return true;
        }
    }
}
