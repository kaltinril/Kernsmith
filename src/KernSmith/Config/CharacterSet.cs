namespace KernSmith;

/// <summary>
/// Represents a set of Unicode codepoints to include in the generated font.
/// </summary>
public class CharacterSet
{
    private readonly HashSet<int> _codepoints;

    private CharacterSet(IEnumerable<int> codepoints)
    {
        _codepoints = new HashSet<int>(codepoints);
    }

    // --- Predefined sets ---

    /// <summary>
    /// Printable ASCII characters: U+0020..U+007E (95 characters).
    /// </summary>
    public static CharacterSet Ascii { get; } = FromRanges((0x0020, 0x007E));

    /// <summary>
    /// Extended ASCII characters: U+0020..U+00FF (224 characters).
    /// </summary>
    public static CharacterSet ExtendedAscii { get; } = FromRanges((0x0020, 0x00FF));

    /// <summary>
    /// Latin characters: ASCII + Latin Extended-A (U+0100..U+017F) + Latin Extended-B (U+0180..U+024F).
    /// </summary>
    public static CharacterSet Latin { get; } = FromRanges(
        (0x0020, 0x007E),
        (0x0100, 0x017F),
        (0x0180, 0x024F));

    // --- Factory methods ---

    /// <summary>
    /// Creates a character set from one or more inclusive codepoint ranges.
    /// </summary>
    public static CharacterSet FromRanges(params (int start, int end)[] ranges)
    {
        var codepoints = new HashSet<int>();
        foreach (var (start, end) in ranges)
        {
            for (int cp = start; cp <= end; cp++)
            {
                codepoints.Add(cp);
            }
        }
        return new CharacterSet(codepoints);
    }

    /// <summary>
    /// Creates a character set from the unique codepoints in a string.
    /// </summary>
    public static CharacterSet FromChars(string characters)
    {
        var codepoints = new HashSet<int>();
        for (int i = 0; i < characters.Length; i++)
        {
            int cp;
            if (char.IsHighSurrogate(characters[i]) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
            {
                cp = char.ConvertToUtf32(characters[i], characters[i + 1]);
                i++;
            }
            else
            {
                cp = characters[i];
            }
            codepoints.Add(cp);
        }
        return new CharacterSet(codepoints);
    }

    /// <summary>
    /// Creates a character set from an explicit list of codepoints.
    /// </summary>
    public static CharacterSet FromChars(IEnumerable<int> codepoints)
    {
        return new CharacterSet(codepoints);
    }

    // --- Combination ---

    /// <summary>
    /// Merges multiple character sets into one.
    /// </summary>
    public static CharacterSet Union(params CharacterSet[] sets)
    {
        var combined = new HashSet<int>();
        foreach (var set in sets)
        {
            combined.UnionWith(set._codepoints);
        }
        return new CharacterSet(combined);
    }

    // --- Instance members ---

    /// <summary>
    /// Returns the codepoints in this set, sorted in ascending order.
    /// </summary>
    public IEnumerable<int> GetCodepoints()
    {
        var sorted = new List<int>(_codepoints);
        sorted.Sort();
        return sorted;
    }

    /// <summary>
    /// Gets the number of codepoints in this set.
    /// </summary>
    public int Count => _codepoints.Count;

    /// <summary>
    /// Returns the internal codepoint set for use as a filter hint during parsing.
    /// </summary>
    internal HashSet<int> GetCodepointsHashSet() => _codepoints;

    /// <summary>
    /// Returns only the codepoints that exist in both this set and the available codepoints list (intersection).
    /// This filters the character set to only glyphs the font actually has.
    /// </summary>
    public IEnumerable<int> Resolve(IReadOnlyList<int> availableCodepoints)
    {
        var available = new HashSet<int>(availableCodepoints);
        var intersection = new List<int>();
        foreach (int cp in _codepoints)
        {
            if (available.Contains(cp))
            {
                intersection.Add(cp);
            }
        }
        intersection.Sort();
        return intersection;
    }
}
