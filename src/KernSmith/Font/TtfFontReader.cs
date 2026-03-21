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
        var availableCodepoints = parser.CmapTable
            .Where(kvp => kvp.Value != 0)
            .Select(kvp => kvp.Key)
            .OrderBy(cp => cp)
            .ToList();

        // Merge kerning pairs: start with kern, overlay GPOS (GPOS takes precedence)
        var mergedPairs = new Dictionary<(int left, int right), int>();

        foreach (var pair in parser.KernPairs)
        {
            if (glyphToCodepoint.TryGetValue(pair.LeftCodepoint, out var leftCp) &&
                glyphToCodepoint.TryGetValue(pair.RightCodepoint, out var rightCp))
            {
                mergedPairs[(leftCp, rightCp)] = pair.XAdvanceAdjustment;
            }
        }

        foreach (var pair in parser.GposPairs)
        {
            if (glyphToCodepoint.TryGetValue(pair.LeftCodepoint, out var leftCp) &&
                glyphToCodepoint.TryGetValue(pair.RightCodepoint, out var rightCp))
            {
                mergedPairs[(leftCp, rightCp)] = pair.XAdvanceAdjustment;
            }
        }

        var kerningPairs = mergedPairs
            .Select(kvp => new KerningPair(kvp.Key.left, kvp.Key.right, kvp.Value))
            .ToList();

        // Determine style flags
        var subfamily = parser.Names?.FontSubfamily ?? "";
        var isBold = (parser.Os2?.WeightClass >= 700) ||
                     subfamily.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        var isItalic = subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                       subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

        var numGlyphs = parser.CmapTable.Values.Distinct().Count();

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
            Os2 = parser.Os2,
            Head = parser.Head,
            Hhea = parser.Hhea,
            Names = parser.Names,
            VariationAxes = parser.VariationAxes,
            NamedInstances = parser.NamedInstances,
            HasColorGlyphs = parser.HasColorGlyphs,
        };
    }
}
