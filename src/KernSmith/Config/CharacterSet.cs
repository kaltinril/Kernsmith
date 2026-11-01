namespace KernSmith;

/// <summary>
/// Defines which Unicode characters to include in the generated bitmap font.
/// </summary>
public class CharacterSet
{
    private readonly HashSet<int> _codepoints;

    /// <summary>The last Unicode codepoint.</summary>
    private const int MaxCodepoint = 0x10FFFF;

    private CharacterSet(IEnumerable<int> codepoints)
    {
        _codepoints = new HashSet<int>(codepoints);
    }

    /// <summary>Printable ASCII characters (space through tilde, 95 chars).</summary>
    public static CharacterSet Ascii { get; } = FromRanges((0x0020, 0x007E));

    /// <summary>Extended ASCII characters including accented Latin (224 chars).</summary>
    public static CharacterSet ExtendedAscii { get; } = FromRanges((0x0020, 0x00FF));

    /// <summary>ASCII plus Latin Extended-A and Extended-B blocks.</summary>
    public static CharacterSet Latin { get; } = FromRanges(
        (0x0020, 0x007E),
        (0x0100, 0x017F),
        (0x0180, 0x024F));

    /// <summary>
    /// Creates a character set from one or more (start, end) character code ranges.
    /// For example, <c>(0x0020, 0x007E)</c> covers printable ASCII.
    /// </summary>
    /// <param name="ranges">One or more (start, end) ranges, both ends included. Each is clamped
    /// to the codepoint space, U+0000 to U+10FFFF.</param>
    public static CharacterSet FromRanges(params (int start, int end)[] ranges)
    {
        var codepoints = new HashSet<int>();
        foreach (var (start, end) in ranges)
        {
            // Clamped, so a range read from an untrusted config (chars=0-2147483647) cannot
            // overflow the loop counter or grow the set without bound.
            for (int cp = Math.Max(start, 0), last = Math.Min(end, MaxCodepoint); cp <= last; cp++)
            {
                codepoints.Add(cp);
            }
        }
        return new CharacterSet(codepoints);
    }

    /// <summary>
    /// Creates a character set from the unique characters in a string.
    /// For example, <c>FromChars("ABCabc123")</c> includes exactly those 9 characters.
    /// </summary>
    /// <param name="characters">The characters to include.</param>
    public static CharacterSet FromChars(string characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        return new CharacterSet(EnumerateCodepoints(characters));
    }

    /// <summary>
    /// Enumerates the Unicode codepoints of a string in order, decoding surrogate pairs
    /// to single codepoints. Duplicates are preserved — callers dedupe as needed.
    /// Shared with <see cref="BmFontIncrementalSession"/>.
    /// </summary>
    internal static IEnumerable<int> EnumerateCodepoints(string characters)
    {
        for (int i = 0; i < characters.Length; i++)
        {
            if (char.IsHighSurrogate(characters[i]) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
            {
                yield return char.ConvertToUtf32(characters[i], characters[i + 1]);
                i++;
            }
            else
            {
                yield return characters[i];
            }
        }
    }

    /// <summary>Creates a character set from an explicit list of Unicode character codes.</summary>
    /// <param name="codepoints">The character codes to include.</param>
    public static CharacterSet FromChars(IEnumerable<int> codepoints)
    {
        ArgumentNullException.ThrowIfNull(codepoints);
        return new CharacterSet(codepoints);
    }

    /// <summary>Merges multiple character sets into one.</summary>
    /// <param name="sets">The character sets to combine.</param>
    public static CharacterSet Union(params CharacterSet[] sets)
    {
        var combined = new HashSet<int>();
        foreach (var set in sets)
        {
            combined.UnionWith(set._codepoints);
        }
        return new CharacterSet(combined);
    }

    /// <summary>Returns all character codes in this set, sorted ascending.</summary>
    public IEnumerable<int> GetCodepoints()
    {
        var sorted = new List<int>(_codepoints);
        sorted.Sort();
        return sorted;
    }

    /// <summary>Number of characters in this set.</summary>
    public int Count => _codepoints.Count;

    /// <summary>Returns the internal set for use as a filter hint during parsing.</summary>
    internal HashSet<int> GetCodepointsHashSet() => _codepoints;

    /// <summary>
    /// Filters this set down to only characters the font actually contains.
    /// </summary>
    /// <param name="availableCodepoints">Characters available in the font.</param>
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
