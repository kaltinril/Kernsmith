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

        // Build reverse cmap: glyphIndex -> lowest codepoint
        var glyphToCodepoint = new Dictionary<int, int>();
        foreach (var (codepoint, glyphIndex) in parser.CmapTable)
        {
            if (glyphIndex == 0)
                continue;

            if (!glyphToCodepoint.TryGetValue(glyphIndex, out var existing) || codepoint < existing)
                glyphToCodepoint[glyphIndex] = codepoint;
        }

        // Available codepoints: all codepoints that map to a non-zero glyph index, sorted
        var availableCodepoints = new List<int>(parser.CmapTable.Count);
        foreach (var (codepoint, glyphIndex) in parser.CmapTable)
        {
            if (glyphIndex != 0)
                availableCodepoints.Add(codepoint);
        }
        availableCodepoints.Sort(); // keys are unique, so this is the stable order too

        // Merge kerning pairs: start with kern, overlay GPOS (GPOS takes precedence). A pair
        // keeps the position of its first insertion (Dictionary enumeration order with no
        // removals) and the value of its last.
        var mergedPairs = new Dictionary<long, int>(parser.KernPairs.Count + parser.GposPairs.Count);
        MergeKerningPairs(parser.KernPairs, glyphToCodepoint, mergedPairs);
        MergeKerningPairs(parser.GposPairs, glyphToCodepoint, mergedPairs);

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
            FamilyName = parser.Names?.FontFamily ?? "Unknown",
            StyleName = parser.Names?.FontSubfamily ?? "Regular",
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
    /// Adds each pair whose glyphs both map to a codepoint, keyed by the codepoint pair packed
    /// into a <see cref="long"/> (left in the high half), overwriting an earlier value.
    /// </summary>
    private static void MergeKerningPairs(
        IReadOnlyList<KerningPair> pairs, Dictionary<int, int> glyphToCodepoint, Dictionary<long, int> merged)
    {
        foreach (var pair in pairs)
        {
            if (glyphToCodepoint.TryGetValue(pair.LeftCodepoint, out var leftCp) &&
                glyphToCodepoint.TryGetValue(pair.RightCodepoint, out var rightCp))
            {
                merged[((long)leftCp << 32) | (uint)rightCp] = pair.XAdvanceAdjustment;
            }
        }
    }
}
