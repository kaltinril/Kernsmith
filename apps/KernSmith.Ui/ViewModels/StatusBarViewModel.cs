using Gum.Mvvm;

namespace KernSmith.Ui.ViewModels;

public class StatusBarViewModel : ViewModel
{
    public string StatusText { get => Get<string>(); set => Set(value); }
    public string AtlasDimensions { get => Get<string>(); set => Set(value); }
    public int GlyphCount { get => Get<int>(); set => Set(value); }
    public string GenerationTime { get => Get<string>(); set => Set(value); }
    public bool IsGenerating { get => Get<bool>(); set => Set(value); }

    public StatusBarViewModel()
    {
        StatusText = "Ready";
        AtlasDimensions = "";
        GenerationTime = "";
    }

    public void SetIdle() { StatusText = "Ready"; IsGenerating = false; }

    public void SetGenerating() { StatusText = "Generating..."; IsGenerating = true; }

    public void SetComplete(int pageCount, int scaleW, int scaleH, int glyphCount, TimeSpan elapsed)
    {
        StatusText = $"Generation complete ({pageCount} page(s))";
        AtlasDimensions = $"{scaleW}x{scaleH}";
        GlyphCount = glyphCount;
        GenerationTime = $"{elapsed.TotalSeconds:F2}s";
        IsGenerating = false;
    }

    public void SetError(string message)
    {
        StatusText = $"Error: {message}";
        IsGenerating = false;
    }
}
