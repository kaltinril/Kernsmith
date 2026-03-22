using System.Text.Json;

namespace KernSmith.Ui.Services;

public class SessionState
{
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string? LastFontPath { get; set; }
    public string? LastOutputDir { get; set; }
    public string? LastProjectPath { get; set; }
    public List<string> RecentFonts { get; set; } = new();
}

public class SessionService
{
    private static readonly string SessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KernSmith", "session.json");

    public SessionState State { get; private set; } = new();

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

    public void AddRecentFont(string path)
    {
        State.RecentFonts.Remove(path);
        State.RecentFonts.Insert(0, path);
        if (State.RecentFonts.Count > 10)
            State.RecentFonts.RemoveRange(10, State.RecentFonts.Count - 10);
    }
}
