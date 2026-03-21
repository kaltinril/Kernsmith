namespace KernSmith.Font.Models;

/// <summary>
/// Describes an installed system font discovered by scanning font directories.
/// </summary>
public sealed class SystemFontInfo
{
    /// <summary>
    /// The font family name (e.g., "Arial", "Times New Roman").
    /// </summary>
    public required string FamilyName { get; init; }

    /// <summary>
    /// The font style/subfamily name (e.g., "Regular", "Bold", "Italic").
    /// </summary>
    public required string StyleName { get; init; }

    /// <summary>
    /// The full path to the font file on disk.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The face index within the font file (relevant for TTC collections).
    /// </summary>
    public int FaceIndex { get; init; }
}
