namespace KernSmith.Ui.Models;

/// <summary>
/// Built-in character set presets for quick selection in the character grid.
/// </summary>
public enum CharacterSetPreset
{
    /// <summary>Printable ASCII characters (U+0020..U+007F).</summary>
    Ascii,
    /// <summary>ASCII plus Latin-1 Supplement (U+0020..U+00FF).</summary>
    ExtendedAscii,
    /// <summary>Extended Latin including Latin-A and Latin-B blocks.</summary>
    Latin,
    /// <summary>User-defined set via text input or Unicode block toggles.</summary>
    Custom
}
