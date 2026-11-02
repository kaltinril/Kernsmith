namespace KernSmith.HintParity.Legibility;

/// <summary>Ink components (8-connected) and enclosed counters (4-connected background) of a thresholded glyph.</summary>
internal readonly record struct Topology(int Components, int Holes);

/// <summary>
/// The building blocks of the legibility score, each a pure function of glyph bitmaps so it can
/// be checked on synthetic input. All take <see cref="GlyphBitmap"/>s in the parity tool's
/// convention: alpha 0..255, <see cref="GlyphBitmap.XOffset"/> from the pen, and
/// <see cref="GlyphBitmap.BaselineTop"/> from the baseline (y down).
/// </summary>
internal static class LegibilityMetrics
{
    /// <summary>The 50% threshold a reader's eye applies: a counter filled past it reads as closed.</summary>
    public const int ReadThreshold = 128;

    /// <summary>Glyphs whose leftmost run at 75% of the height is a plain vertical stem. Not 'b'
    /// or 'd': at that height the bowl's bottom joins the stem, and the run spans both.</summary>
    public const string StemGlyphs = "ilI|HnmhrLEFT";

    /// <summary>Flat-topped x-height letters (no overshoot) and round ones (overshoot by design).</summary>
    public const string FlatXHeightTops = "mnruxz";
    public const string RoundXHeightTops = "oces";

    /// <summary>Letters standing flat on the baseline.</summary>
    public const string FlatBaselines = "hilmnrxzHILMNEFTZ";

    /// <summary>Flat-topped capitals.</summary>
    public const string FlatCapTops = "EFHILTZBDPRK";

    /// <summary>Stem rhythm is measured on this word: alternating stems and counters, no round letters.</summary>
    public const string RhythmWord = "minimum";

    /// <summary>Adjacent-pair spacing is measured over this text.</summary>
    public const string SpacingText = "the quick brown fox jumps over the lazy dog HAMBURGEFONSTIV Illegal minimum";

    // ---- ink concentration ----------------------------------------------------------------------

    /// <summary>
    /// Σv² / (255·Σv) over every pixel: 1 when all ink is in fully covered pixels, lower the
    /// more of it sits in half tones. Blur spreads the same ink over more, fainter pixels, which
    /// lowers the effective contrast a reader gets; this is that loss as one number.
    /// </summary>
    public static double InkConcentration(IEnumerable<GlyphBitmap> glyphs)
    {
        double sum = 0, squares = 0;
        foreach (var g in glyphs)
        {
            foreach (byte v in g.Pixels)
            {
                sum += v;
                squares += (double)v * v;
            }
        }

        return sum <= 0 ? 0 : squares / (255.0 * sum);
    }

    // ---- topology -------------------------------------------------------------------------------

    /// <summary>Ink components and holes of the glyph thresholded at <paramref name="threshold"/>.</summary>
    public static Topology TopologyOf(GlyphBitmap g, int threshold = ReadThreshold)
    {
        if (g.IsEmpty)
            return new Topology(0, 0);

        // One pixel of background border, so the outside is a single 4-connected component.
        int w = g.Width + 2, h = g.Height + 2;
        var ink = new bool[w * h];
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                ink[((y + 1) * w) + x + 1] = g.At(x, y) >= threshold;

        var seen = new bool[w * h];
        int components = 0, background = 0;
        var stack = new Stack<int>();
        for (int i = 0; i < ink.Length; i++)
        {
            if (seen[i])
                continue;

            bool isInk = ink[i];
            if (isInk) components++; else background++;
            seen[i] = true;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % w, py = p / w;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        if (!isInk && dx != 0 && dy != 0)
                            continue; // background is 4-connected
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int n = (ny * w) + nx;
                        if (seen[n] || ink[n] != isInk)
                            continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }
        }

        return new Topology(components, Math.Max(0, background - 1));
    }

    /// <summary>The contrast band topology is judged over: a feature that survives only at the
    /// faintest cut has no margin, one that survives at the darkest is unmistakable.</summary>
    public static readonly int[] TopologyThresholds = [80, ReadThreshold, 176];

    /// <summary>
    /// The share of <see cref="TopologyThresholds"/> at which the glyph has its true topology
    /// (<paramref name="truth"/>, from the high-resolution outline): 1 when its counters and
    /// strokes survive with contrast to spare, 1/3 when a dot or a counter is just barely
    /// there, 0 when it is gone or merged at every cut.
    /// </summary>
    public static double TopologyPreservation(GlyphBitmap rendered, Topology truth)
    {
        int matches = 0;
        foreach (int threshold in TopologyThresholds)
        {
            if (TopologyOf(rendered, threshold) == truth)
                matches++;
        }

        return (double)matches / TopologyThresholds.Length;
    }

    // ---- separability ----------------------------------------------------------------------------

    /// <summary>
    /// How different two glyph images are to a reader who may misjudge their position: the
    /// smallest, over shifts of up to <paramref name="maxShift"/> pixels, of Σ|a−b| / (Σa+Σb),
    /// 0 for the same image and 1 for images that share no ink.
    /// </summary>
    public static double Separation(GlyphBitmap a, GlyphBitmap b, int maxShift = 2)
    {
        if (a.IsEmpty && b.IsEmpty)
            return 0;
        if (a.IsEmpty || b.IsEmpty)
            return 1;

        int pad = maxShift + 1;
        int left = Math.Min(a.XOffset, b.XOffset) - pad;
        int top = Math.Min(a.BaselineTop, b.BaselineTop) - pad;
        int right = Math.Max(a.XOffset + a.Width, b.XOffset + b.Width) + pad;
        int bottom = Math.Max(a.BaselineTop + a.Height, b.BaselineTop + b.Height) + pad;
        int w = right - left, h = bottom - top;
        var ca = Paste(a, left, top, w, h);
        var cb = Paste(b, left, top, w, h);

        double total = 0;
        foreach (byte v in ca) total += v;
        foreach (byte v in cb) total += v;
        if (total <= 0)
            return 0; // two blank boxes (a space-like glyph with a box but no ink) are the same image

        double best = double.MaxValue;
        for (int dy = -maxShift; dy <= maxShift; dy++)
        {
            for (int dx = -maxShift; dx <= maxShift; dx++)
            {
                double diff = 0;
                for (int y = 0; y < h; y++)
                {
                    int sy = y - dy;
                    for (int x = 0; x < w; x++)
                    {
                        int sx = x - dx;
                        int bv = sx < 0 || sy < 0 || sx >= w || sy >= h ? 0 : cb[(sy * w) + sx];
                        diff += Math.Abs(ca[(y * w) + x] - bv);
                    }
                }

                best = Math.Min(best, diff);
            }
        }

        return Math.Min(1.0, best / total);
    }

    // ---- the high-resolution ideal ---------------------------------------------------------------

    /// <summary>
    /// Box-filters a glyph rendered at <paramref name="factor"/>× down to 1×, binning by absolute
    /// pixel position (floor division of the pen- and baseline-relative coordinates), so the
    /// result is what an ideal renderer would produce for the same outline at the same place.
    /// </summary>
    public static GlyphBitmap Downsample(GlyphBitmap big, int factor)
    {
        int advance = (int)Math.Round(big.XAdvance / (double)factor, MidpointRounding.AwayFromZero);
        if (big.IsEmpty)
            return big with { XAdvance = advance };

        int x0 = FloorDiv(big.XOffset, factor);
        int x1 = FloorDiv(big.XOffset + big.Width - 1, factor);
        int y0 = FloorDiv(big.BaselineTop, factor);
        int y1 = FloorDiv(big.BaselineTop + big.Height - 1, factor);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var sums = new double[w * h];
        for (int y = 0; y < big.Height; y++)
        {
            int ty = FloorDiv(big.BaselineTop + y, factor) - y0;
            for (int x = 0; x < big.Width; x++)
            {
                int tx = FloorDiv(big.XOffset + x, factor) - x0;
                sums[(ty * w) + tx] += big.At(x, y);
            }
        }

        var pixels = new byte[w * h];
        double area = (double)factor * factor;
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)Math.Clamp((int)Math.Round(sums[i] / area), 0, 255);

        return new GlyphBitmap(big.Id, w, h, x0, big.YOffset, y0, advance, pixels);
    }

    /// <summary>
    /// Structural similarity of a rendered glyph to its ideal, forgiving a translation of one
    /// pixel in each axis (a hinter may legitimately move a glyph that far) but not a change
    /// of shape: the best SSIM over the nine shifts.
    /// </summary>
    public static double Fidelity(GlyphBitmap rendered, GlyphBitmap ideal)
    {
        if (rendered.IsEmpty && ideal.IsEmpty)
            return 1;
        if (rendered.IsEmpty || ideal.IsEmpty)
            return 0;

        const int pad = 2;
        int left = Math.Min(rendered.XOffset, ideal.XOffset) - pad;
        int top = Math.Min(rendered.BaselineTop, ideal.BaselineTop) - pad;
        int right = Math.Max(rendered.XOffset + rendered.Width, ideal.XOffset + ideal.Width) + pad;
        int bottom = Math.Max(rendered.BaselineTop + rendered.Height, ideal.BaselineTop + ideal.Height) + pad;
        int w = right - left, h = bottom - top;
        var a = Paste(ideal, left, top, w, h);

        double best = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var b = Paste(rendered with { XOffset = rendered.XOffset + dx, BaselineTop = rendered.BaselineTop + dy }, left, top, w, h);
                best = Math.Max(best, GlyphComparison.ComputeSsim(a, b, w, h));
            }

        return Math.Clamp(best, 0, 1);
    }

    // ---- alignment -------------------------------------------------------------------------------

    /// <summary>
    /// How cleanly a glyph's top (or bottom) edge sits on a pixel line: the outermost row's
    /// coverage relative to the stroke under it, over the columns that stroke occupies. 1 for
    /// an edge on the grid; 0.4 for a flat top that spills 40% into the row above.
    /// </summary>
    public static double EdgeCrispness(GlyphBitmap g, bool top)
    {
        if (g.IsEmpty)
            return 1;

        int outer = top ? 0 : g.Height - 1;
        int inner = top ? 1 : g.Height - 2;
        if (g.Height < 2)
            return RowMean(g, outer) / 255.0;

        double edge = 0, reference = 0;
        for (int x = 0; x < g.Width; x++)
        {
            int below = g.At(x, inner);
            if (below == 0)
                continue;
            int v = g.At(x, outer);
            edge += v;
            reference += Math.Max(v, below);
        }

        return reference <= 0 ? RowMean(g, outer) / 255.0 : Math.Clamp(edge / reference, 0, 1);
    }

    /// <summary>
    /// Alignment of the font's horizontal reference lines: half the crispness of flat tops and
    /// bottoms at the x-height, cap height and baseline, half the share of letters whose
    /// top (or bottom) row — the first row a reader sees, at the 50% threshold — is the modal
    /// row for that line. A round letter that pokes one row past its flat neighbours, or a flat
    /// top smeared over two rows, both lower it.
    /// </summary>
    public static double Alignment(IReadOnlyDictionary<int, GlyphBitmap> glyphs)
    {
        var crisp = new List<double>();
        foreach (char c in FlatXHeightTops + FlatCapTops)
            if (Inked(glyphs, c) is { } g) crisp.Add(EdgeCrispness(g, top: true));
        foreach (char c in FlatBaselines)
            if (Inked(glyphs, c) is { } g) crisp.Add(EdgeCrispness(g, top: false));

        var shares = new List<double>();
        AddModeShare(shares, glyphs, FlatXHeightTops + RoundXHeightTops, top: true);
        AddModeShare(shares, glyphs, FlatBaselines, top: false);
        AddModeShare(shares, glyphs, FlatCapTops, top: true);

        double crispness = crisp.Count == 0 ? 1 : crisp.Average();
        double modeShare = shares.Count == 0 ? 1 : shares.Average();
        return (0.5 * crispness) + (0.5 * modeShare);
    }

    private static void AddModeShare(List<double> shares, IReadOnlyDictionary<int, GlyphBitmap> glyphs, string set, bool top)
    {
        var rows = new List<int>();
        foreach (char c in set)
        {
            if (Inked(glyphs, c) is not { } g)
                continue;
            int? row = top ? FirstReadRow(g) : LastReadRow(g);
            if (row is { } r)
                rows.Add(r);
        }

        if (rows.Count < 3)
            return;
        int mode = rows.GroupBy(r => r).OrderByDescending(grp => grp.Count()).ThenBy(grp => grp.Key).First().Count();
        shares.Add((double)mode / rows.Count);
    }

    /// <summary>The first row (baseline-relative) with a pixel at or over the read threshold.</summary>
    private static int? FirstReadRow(GlyphBitmap g)
    {
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.At(x, y) >= ReadThreshold)
                    return g.BaselineTop + y;
        return null;
    }

    private static int? LastReadRow(GlyphBitmap g)
    {
        for (int y = g.Height - 1; y >= 0; y--)
            for (int x = 0; x < g.Width; x++)
                if (g.At(x, y) >= ReadThreshold)
                    return g.BaselineTop + y;
        return null;
    }

    // ---- stems ------------------------------------------------------------------------------------

    /// <summary>
    /// 1 / (1 + 4·variance) of the stem width in read pixels (≥ 128) across the stem glyphs,
    /// measured on the leftmost run at 75% of each glyph's height (below crossbars and arches).
    /// Equal stems — every one the same number of dark columns — score 1; a font where some
    /// stems fall on a column and others straddle two scores lower.
    /// </summary>
    public static double StemUniformity(IReadOnlyDictionary<int, GlyphBitmap> glyphs)
    {
        var widths = StemReadWidths(glyphs).Select(s => (double)s.ReadWidth).ToList();
        if (widths.Count < 2)
            return 1;
        double mean = widths.Average();
        double variance = widths.Sum(w => (w - mean) * (w - mean)) / widths.Count;
        return 1 / (1 + (4 * variance));
    }

    /// <summary>Per stem glyph: the leftmost run's width in pixels ≥ 128 and its summed coverage.</summary>
    public static List<(char Glyph, int ReadWidth, double Coverage)> StemReadWidths(IReadOnlyDictionary<int, GlyphBitmap> glyphs)
    {
        var result = new List<(char, int, double)>();
        foreach (char c in StemGlyphs)
        {
            if (Inked(glyphs, c) is not { } g || g.Height < 2)
                continue;

            int row = Math.Min(g.Height - 1, (int)(g.Height * 0.75));
            int start = -1;
            for (int x = 0; x < g.Width; x++)
            {
                if (g.At(x, row) >= 64)
                {
                    start = x;
                    break;
                }
            }

            if (start < 0)
                continue;

            int read = 0;
            double coverage = 0;
            int end = start;
            while (end < g.Width && g.At(end, row) >= 64)
            {
                coverage += g.At(end, row) / 255.0;
                if (g.At(end, row) >= ReadThreshold)
                    read++;
                end++;
            }

            if (start > 0) coverage += g.At(start - 1, row) / 255.0;
            if (end < g.Width) coverage += g.At(end, row) / 255.0;
            result.Add((c, read, coverage));
        }

        return result;
    }

    // ---- spacing ----------------------------------------------------------------------------------

    /// <summary>Sets a string with the glyphs' own advances (no kerning), ink combined by max; null when nothing is inked.</summary>
    public static GlyphBitmap? Typeset(IReadOnlyDictionary<int, GlyphBitmap> glyphs, string text)
    {
        int pen = 0;
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        var placed = new List<(GlyphBitmap Glyph, int X)>();
        foreach (char c in text)
        {
            if (!glyphs.TryGetValue(c, out var g))
                continue;
            if (!g.IsEmpty)
            {
                placed.Add((g, pen));
                left = Math.Min(left, pen + g.XOffset);
                top = Math.Min(top, g.BaselineTop);
                right = Math.Max(right, pen + g.XOffset + g.Width);
                bottom = Math.Max(bottom, g.BaselineTop + g.Height);
            }

            pen += g.XAdvance;
        }

        if (placed.Count == 0)
            return null;

        int w = right - left, h = bottom - top;
        var pixels = new byte[w * h];
        foreach (var (g, x) in placed)
        {
            int ox = x + g.XOffset - left, oy = g.BaselineTop - top;
            for (int y = 0; y < g.Height; y++)
                for (int gx = 0; gx < g.Width; gx++)
                {
                    int i = ((oy + y) * w) + ox + gx;
                    pixels[i] = Math.Max(pixels[i], g.At(gx, y));
                }
        }

        return new GlyphBitmap(0, w, h, left, 0, top, pen, pixels);
    }

    /// <summary>
    /// Evenness of the set text against a reference rendering of the same font (at
    /// <paramref name="referenceScale"/>× resolution, so its stem positions and gaps are
    /// fractional truths): the mean of (1) 1/(1+3·excess) where excess is how much the
    /// stem-pitch spread of "minimum" exceeds the reference's — stems that jitter in and out of
    /// phase with the grid break the rhythm; (2) the share of adjacent letter pairs that do
    /// not touch when the reference keeps at least half a pixel between them; and (3)
    /// 1/(1+error) where error is the mean gap difference from the reference beyond the half
    /// pixel integer advances cannot avoid.
    /// </summary>
    public static double Spacing(IReadOnlyDictionary<int, GlyphBitmap> rendered, IReadOnlyDictionary<int, GlyphBitmap> reference, int referenceScale = 1)
    {
        var detail = SpacingDetail(rendered, reference, referenceScale);
        return (detail.PitchTerm + (1 - detail.TouchShare) + detail.GapTerm) / 3;
    }

    public static (double PitchTerm, double TouchShare, double GapTerm, double PitchStd, double ReferencePitchStd, int Touches, int Pairs) SpacingDetail(
        IReadOnlyDictionary<int, GlyphBitmap> rendered, IReadOnlyDictionary<int, GlyphBitmap> reference, int referenceScale)
    {
        double renderedStd = PitchStd(rendered, 1);
        double referenceStd = PitchStd(reference, referenceScale);
        double pitchTerm = 1 / (1 + (3 * Math.Max(0, renderedStd - referenceStd)));

        int pairs = 0, touches = 0;
        double gapError = 0;
        for (int i = 0; i + 1 < SpacingText.Length; i++)
        {
            char a = SpacingText[i], b = SpacingText[i + 1];
            if (Inked(rendered, a) is not { } ra || Inked(rendered, b) is not { } rb
                || Inked(reference, a) is not { } fa || Inked(reference, b) is not { } fb)
                continue;

            pairs++;
            double renderedGap = ReadGap(ra, rb);
            double referenceGap = ReadGap(fa, fb) / referenceScale;
            if (renderedGap <= 0 && referenceGap >= 0.5)
                touches++;
            gapError += Math.Max(0, Math.Abs(renderedGap - referenceGap) - 0.5);
        }

        double touchShare = pairs == 0 ? 0 : (double)touches / pairs;
        double gapTerm = pairs == 0 ? 1 : 1 / (1 + (gapError / pairs));
        return (pitchTerm, touchShare, gapTerm, renderedStd, referenceStd, touches, pairs);
    }

    /// <summary>Population standard deviation of the stem pitch, in 1× pixels, along <see cref="RhythmWord"/>; 0 when under two pitches.</summary>
    private static double PitchStd(IReadOnlyDictionary<int, GlyphBitmap> glyphs, int scale)
    {
        if (Typeset(glyphs, RhythmWord) is not { } word || Inked(glyphs, 'x') is not { } x)
            return 0;

        // 60% down the x-height: through the stems, under the arches of m/n/u.
        int row = x.BaselineTop + (int)Math.Floor(x.Height * 0.6) - word.BaselineTop;
        if (row < 0 || row >= word.Height)
            return 0;

        var centres = new List<double>();
        int start = -1;
        for (int px = 0; px <= word.Width; px++)
        {
            bool on = px < word.Width && word.At(px, row) >= ReadThreshold;
            if (on && start < 0)
                start = px;
            else if (!on && start >= 0)
            {
                centres.Add((start + px - 1) / 2.0 / scale);
                start = -1;
            }
        }

        if (centres.Count < 3)
            return 0;
        var pitches = new List<double>();
        for (int i = 1; i < centres.Count; i++)
            pitches.Add(centres[i] - centres[i - 1]);
        double mean = pitches.Average();
        return Math.Sqrt(pitches.Sum(p => (p - mean) * (p - mean)) / pitches.Count);
    }

    /// <summary>
    /// The gap a reader sees between two letters set in sequence: from the last column of the
    /// first with a pixel at the read threshold to the first such column of the next, in
    /// pixels (negative when they overlap). Faint fringe columns do not count as the edge.
    /// </summary>
    private static double ReadGap(GlyphBitmap a, GlyphBitmap b)
    {
        int rightA = ReadColumn(a, fromLeft: false);
        int leftB = ReadColumn(b, fromLeft: true);
        if (rightA < 0) rightA = a.Width - 1;
        if (leftB < 0) leftB = 0;
        return (a.XAdvance - (a.XOffset + rightA + 1)) + b.XOffset + leftB;
    }

    private static int ReadColumn(GlyphBitmap g, bool fromLeft)
    {
        for (int i = 0; i < g.Width; i++)
        {
            int x = fromLeft ? i : g.Width - 1 - i;
            for (int y = 0; y < g.Height; y++)
                if (g.At(x, y) >= ReadThreshold)
                    return x;
        }

        return -1;
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static GlyphBitmap? Inked(IReadOnlyDictionary<int, GlyphBitmap> glyphs, char c) =>
        glyphs.TryGetValue(c, out var g) && !g.IsEmpty ? g : null;

    private static double RowMean(GlyphBitmap g, int row)
    {
        double sum = 0;
        int count = 0;
        for (int x = 0; x < g.Width; x++)
        {
            int v = g.At(x, row);
            if (v == 0)
                continue;
            sum += v;
            count++;
        }

        return count == 0 ? 0 : sum / count;
    }

    private static byte[] Paste(GlyphBitmap g, int left, int top, int width, int height)
    {
        var canvas = new byte[width * height];
        int ox = g.XOffset - left, oy = g.BaselineTop - top;
        for (int y = 0; y < g.Height; y++)
        {
            int cy = oy + y;
            if (cy < 0 || cy >= height)
                continue;
            for (int x = 0; x < g.Width; x++)
            {
                int cx = ox + x;
                if (cx >= 0 && cx < width)
                    canvas[(cy * width) + cx] = g.At(x, y);
            }
        }

        return canvas;
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
