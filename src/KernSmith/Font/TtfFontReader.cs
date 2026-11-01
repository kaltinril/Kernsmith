using KernSmith.Font.Models;

namespace KernSmith.Font;

internal class TtfFontReader : IFontReader
{
    /// <summary>
    /// Optional codepoint filter hint. When set, the parser will only include
    /// cmap entries for these codepoints and skip irrelevant kerning pairs.
    /// </summary>
    internal HashSet<int>? RequestedCodepoints { get; set; }

    /// <summary>
    /// When set, the parser uses this shared byte array instead of copying from the span.
    /// </summary>
    internal byte[]? SharedFontBytes { get; set; }

    public FontInfo ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0)
    {
        var parser = SharedFontBytes != null
            ? new TtfParser(SharedFontBytes, faceIndex, RequestedCodepoints)
            : new TtfParser(fontData, faceIndex, RequestedCodepoints);

        // Available codepoints: all codepoints that map to a non-zero glyph index, sorted
        var availableCodepoints = new List<int>(parser.CmapTable.Count);
        foreach (var (codepoint, glyphIndex) in parser.CmapTable)
        {
            if (glyphIndex != 0)
                availableCodepoints.Add(codepoint);
        }
        availableCodepoints.Sort(); // keys are unique, so this is the stable order too

        // Reverse cmap: glyph -> its lowest codepoint, plus (rarely) its other codepoints in
        // ascending order. Kerning is by glyph, so every codepoint of a glyph gets its pairs.
        var glyphToCodepoint = new Dictionary<int, int>();
        Dictionary<int, List<int>>? glyphToMoreCodepoints = null;
        foreach (var codepoint in availableCodepoints)
        {
            var glyphIndex = parser.CmapTable[codepoint];
            if (glyphToCodepoint.TryAdd(glyphIndex, codepoint))
                continue;
            glyphToMoreCodepoints ??= new Dictionary<int, List<int>>();
            if (!glyphToMoreCodepoints.TryGetValue(glyphIndex, out var more))
                glyphToMoreCodepoints[glyphIndex] = more = new List<int>(1);
            more.Add(codepoint);
        }

        // Merge kerning pairs: start with kern, overlay GPOS (GPOS takes precedence). A pair
        // keeps the position of its first insertion (Dictionary enumeration order with no
        // removals) and the value of its last.
        var mergedPairs = new Dictionary<long, int>(parser.KernPairs.Count + parser.GposKerningPairs.Count);
        MergeKerningPairs(parser.KernPairs, glyphToCodepoint, glyphToMoreCodepoints, mergedPairs);
        MergeKerningPairs(parser.GposKerningPairs, glyphToCodepoint, glyphToMoreCodepoints, mergedPairs);

        var kerningPairs = new List<KerningPair>(mergedPairs.Count);
        foreach (var (key, value) in mergedPairs)
            kerningPairs.Add(new KerningPair((int)(key >> 32), (int)key, value));

        // Determine style flags
        var subfamily = parser.Names?.FontSubfamily ?? "";
        var isBold = (parser.Os2?.WeightClass >= 700) ||
                     subfamily.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var isItalic = subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                       subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

        var numGlyphs = new HashSet<int>(parser.CmapTable.Values).Count;

        // Per-codepoint design metrics (advance width + left side bearing), keyed the same
        // way as availableCodepoints. Empty if the font has no hmtx table (or one that failed
        // to parse) — this only affects DesignMetrics, not core metadata or rasterization.
        var designMetrics = new Dictionary<int, GlyphDesignMetrics>();
        if (parser.Hmtx != null)
        {
            foreach (var codepoint in availableCodepoints)
            {
                var glyphIndex = parser.CmapTable[codepoint];
                if ((uint)glyphIndex >= (uint)parser.Hmtx.GlyphCount)
                    continue;

                designMetrics[codepoint] = new GlyphDesignMetrics(
                    parser.Hmtx.GetAdvanceWidth(glyphIndex),
                    parser.Hmtx.GetLeftSideBearing(glyphIndex));
            }
        }

        return new FontInfo
        {
            FamilyName = SanitizeName(parser.Names?.FontFamily) ?? "Unknown",
            StyleName = SanitizeName(parser.Names?.FontSubfamily) ?? "Regular",
            UnitsPerEm = parser.Head!.UnitsPerEm,
            Ascender = parser.Hhea?.Ascender ?? parser.Head!.YMax,
            Descender = parser.Hhea?.Descender ?? parser.Head!.YMin,
            LineGap = parser.Hhea?.LineGap ?? 0,
            IsBold = isBold,
            IsItalic = isItalic,
            IsFixedPitch = false,
            NumGlyphs = numGlyphs,
            AvailableCodepoints = availableCodepoints,
            KerningPairs = kerningPairs,
            DesignMetrics = designMetrics,
            Os2 = parser.Os2,
            Head = parser.Head,
            Hhea = parser.Hhea,
            Names = parser.Names,
            VariationAxes = parser.VariationAxes,
            NamedInstances = parser.NamedInstances,
            HasColorGlyphs = parser.HasColorGlyphs,
        };
    }

    /// <summary>
    /// A name-table string made safe to write into a descriptor: control characters are dropped
    /// and double quotes become single quotes. The name comes from the font, and the text
    /// <c>.fnt</c> format quotes it on one line with no escape mechanism, so a quote or newline
    /// would end the value and start new, attacker-chosen lines (pages, chars); XML rejects
    /// control characters outright.
    /// </summary>
    internal static string? SanitizeName(string? name)
    {
        if (name is null || !name.Any(c => c == '"' || char.IsControl(c)))
            return name;

        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c == '"')
                sb.Append('\'');
            else if (!char.IsControl(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Adds each pair whose glyphs both map to a codepoint, once per (left, right) codepoint
    /// combination (left-major, ascending), keyed by the codepoint pair packed into a
    /// <see cref="long"/> (left in the high half), overwriting an earlier value.
    /// </summary>
    private static void MergeKerningPairs(
        IReadOnlyList<KerningPair> pairs, Dictionary<int, int> glyphToCodepoint,
        Dictionary<int, List<int>>? glyphToMoreCodepoints, Dictionary<long, int> merged)
    {
        foreach (var pair in pairs)
        {
            if (!glyphToCodepoint.TryGetValue(pair.LeftCodepoint, out var leftCp) ||
                !glyphToCodepoint.TryGetValue(pair.RightCodepoint, out var rightCp))
                continue;

            var value = pair.XAdvanceAdjustment;
            List<int>? moreLeft = null, moreRight = null;
            if (glyphToMoreCodepoints != null)
            {
                glyphToMoreCodepoints.TryGetValue(pair.LeftCodepoint, out moreLeft);
                glyphToMoreCodepoints.TryGetValue(pair.RightCodepoint, out moreRight);
            }

            AddRow(merged, leftCp, rightCp, moreRight, value);
            if (moreLeft != null)
            {
                foreach (var cp in moreLeft)
                    AddRow(merged, cp, rightCp, moreRight, value);
            }
        }

        static void AddRow(Dictionary<long, int> merged, int left, int right, List<int>? moreRight, int value)
        {
            merged[((long)left << 32) | (uint)right] = value;
            if (moreRight != null)
            {
                foreach (var cp in moreRight)
                    merged[((long)left << 32) | (uint)cp] = value;
            }
        }
    }
}
