namespace KernSmith.Ui.Services;

/// <summary>
/// The font file types the desktop app opens, shared by the file dialogs and drag-and-drop.
/// </summary>
public static class FontFileTypes
{
    private static readonly string[] Extensions = ["ttf", "otf", "ttc", "woff", "woff2"];

    /// <summary>Comma-separated extension list for <c>NativeFileDialog.AddFilter</c>.</summary>
    public static string DialogFilter { get; } = string.Join(",", Extensions);

    /// <summary>Returns true if <paramref name="path"/> has a font file extension (case-insensitive).</summary>
    public static bool IsFontFile(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return Array.Exists(Extensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }
}
