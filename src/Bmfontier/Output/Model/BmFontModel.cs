namespace Bmfontier.Output.Model;

/// <summary>
/// In-memory representation of a BMFont descriptor file.
/// </summary>
public sealed class BmFontModel
{
    /// <summary>Font metadata.</summary>
    public required InfoBlock Info { get; init; }

    /// <summary>Common metrics shared across all glyphs.</summary>
    public required CommonBlock Common { get; init; }

    /// <summary>Atlas page filenames.</summary>
    public required IReadOnlyList<PageEntry> Pages { get; init; }

    /// <summary>Per-character glyph entries.</summary>
    public required IReadOnlyList<CharEntry> Characters { get; init; }

    /// <summary>Kerning pair adjustments.</summary>
    public IReadOnlyList<KerningEntry> KerningPairs { get; init; } = Array.Empty<KerningEntry>();

    /// <summary>Extended bmfontier-specific metadata (null when not present).</summary>
    public ExtendedMetadata? Extended { get; init; }
}
