using System.Text.Json;

namespace KernSmith.Ui.Services;

/// <summary>
/// Persisted application state: window size, recent file paths, and last used directories.
/// Serialized to JSON in the user's AppData folder.
/// </summary>
public class SessionState
{
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string? LastFontPath { get; set; }
    public string? LastOutputDir { get; set; }
    public string? LastProjectPath { get; set; }
    public List<string> RecentFonts { get; set; } = new();
}

/// <summary>
/// Loads and saves <see cref="SessionState"/> to a JSON file in AppData/KernSmith/.
/// Best-effort persistence: silently ignores read/write failures.
/// </summary>
public class SessionService
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KernSmith", "session.json");

    public SessionState State { get; private set; } = new();

    /// <summary>
    /// Loads session state from disk. Falls back to defaults if the file is missing or corrupt.
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(SessionPath))
            {
                var json = File.ReadAllText(SessionPath);
                State = JsonSerializer.Deserialize<SessionState>(json) ?? new();
            }
        }
        catch { State = new(); }
    }

    /// <summary>
    /// Writes the current session state to disk. Silently ignores write failures.
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SessionPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SessionPath, json);
        }
        catch { /* Best-effort persistence */ }
    }

    /// <summary>
    /// Adds a font path to the recent fonts list (most recent first, max 10 entries).
    /// </summary>
    public void AddRecentFont(string path)
    {
        State.RecentFonts.Remove(path);
        State.RecentFonts.Insert(0, path);
        if (State.RecentFonts.Count > 10)
            State.RecentFonts.RemoveRange(10, State.RecentFonts.Count - 10);
    }
}
