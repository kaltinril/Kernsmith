namespace KernSmith.Ui.Models;

/// <summary>
/// Indicates how the current font was loaded into the UI.
/// </summary>
public enum FontSourceKind
{
    /// <summary>No font has been loaded.</summary>
    None,
    /// <summary>Font was loaded from a file path (TTF/OTF/WOFF/TTC).</summary>
    File,
    /// <summary>Font was selected from the system font list.</summary>
    System
}
