using Gum.Mvvm;

namespace KernSmith.Ui.ViewModels;

/// <summary>
/// Observable state for the bottom status bar: status message, atlas dimensions,
/// glyph count, generation time, and generating/error states.
/// </summary>
public class StatusBarViewModel : ViewModel
{
    public string StatusText { get => Get<string>(); set => Set(value); }
    public string AtlasDimensions { get => Get<string>(); set => Set(value); }
    public int GlyphCount { get => Get<int>(); set => Set(value); }
    public string GenerationTime { get => Get<string>(); set => Set(value); }
    public bool IsGenerating { get => Get<bool>(); set => Set(value); }
    public string GlyphInfoText { get => Get<string>(); set => Set(value); }

    public StatusBarViewModel()
    {
        StatusText = "Ready";
        AtlasDimensions = "";
        GenerationTime = "";
        GlyphInfoText = "";
    }

    /// <summary>Resets status to "Ready" and clears the generating flag.</summary>
    public void SetIdle() { StatusText = "Ready"; IsGenerating = false; }

    /// <summary>Sets the status bar to "Generating..." with the generating flag active.</summary>
    public void SetGenerating() { StatusText = "Generating..."; IsGenerating = true; }

    /// <summary>
    /// Updates the status bar with generation results: page count, atlas dimensions, glyph count, and elapsed time.
    /// </summary>
    public void SetComplete(int pageCount, int scaleW, int scaleH, int glyphCount, TimeSpan elapsed)
    {
        StatusText = $"Generation complete ({pageCount} page(s))";
        AtlasDimensions = $"{scaleW}x{scaleH}";
        GlyphCount = glyphCount;
        GenerationTime = $"{elapsed.TotalSeconds:F2}s";
        IsGenerating = false;
    }

    /// <summary>
    /// Displays an error message in the status bar and clears the generating flag.
    /// </summary>
    public void SetError(string message)
    {
        StatusText = $"Error: {message}";
        IsGenerating = false;
    }
}
