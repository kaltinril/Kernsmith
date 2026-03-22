namespace KernSmith.Ui.Services;

/// <summary>
/// Simple file path service for MVP. Phase 61 will add drag-and-drop via Window.FileDrop.
/// For now, callers provide paths directly (e.g., from a TextBox or system font selection).
/// </summary>
public class FileDialogService
{
    /// <summary>
    /// Returns the last file path set via <see cref="SetOpenPath"/>, then clears it.
    /// </summary>
    public string? OpenFontFile()
    {
        var path = _pendingOpenPath;
        _pendingOpenPath = null;
        return path;
    }

    /// <summary>
    /// Returns the last save path set via <see cref="SetSavePath"/>, then clears it.
    /// </summary>
    public string? SaveFile(string defaultName, string filter)
    {
        var path = _pendingSavePath;
        _pendingSavePath = null;
        return path;
    }

    private string? _pendingOpenPath;
    private string? _pendingSavePath;

    public void SetOpenPath(string path) => _pendingOpenPath = path;
    public void SetSavePath(string path) => _pendingSavePath = path;
}
