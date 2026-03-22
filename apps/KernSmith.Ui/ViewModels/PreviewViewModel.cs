using Gum.Mvvm;
using KernSmith.Output;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

/// <summary>
/// Holds the generated atlas preview state: pages, selected page, zoom level, glyph counts,
/// and atlas summary text. Updated by <see cref="LoadResult"/> after each generation.
/// </summary>
public class PreviewViewModel : ViewModel
{
    public IReadOnlyList<PreviewPage> Pages { get => Get<IReadOnlyList<PreviewPage>>(); set => Set(value); }
    public int SelectedPageIndex { get => Get<int>(); set => Set(value); }
    public PreviewPage? SelectedPage { get => Get<PreviewPage?>(); set => Set(value); }
    public bool HasResult { get => Get<bool>(); set => Set(value); }

    // Zoom
    public float ZoomLevel { get => Get<float>(); set => Set(value); }

    // Sample text
    public string SampleText { get => Get<string>(); set => Set(value); }

    // Glyph generation info
    public int RenderedGlyphCount { get => Get<int>(); set => Set(value); }
    public int RequestedGlyphCount { get => Get<int>(); set => Set(value); }
    public int FailedCodepointCount { get => Get<int>(); set => Set(value); }
    public string GlyphInfoText { get => Get<string>(); set => Set(value); }

    // Atlas summary
    public string AtlasSummary { get => Get<string>(); set => Set(value); }

    public PreviewViewModel()
    {
        Pages = Array.Empty<PreviewPage>();
        ZoomLevel = 1.0f;
        SampleText = "Hello World";
        GlyphInfoText = "";
        AtlasSummary = "";
    }

    /// <summary>
    /// Populates pages, glyph counts, and atlas summary from a completed generation result.
    /// </summary>
    public void LoadResult(BmFontResult result)
    {
        var pages = new List<PreviewPage>();

        for (int i = 0; i < result.Pages.Count; i++)
        {
            var pngBytes = result.GetPngData(i);
            var page = result.Pages[i];

            pages.Add(new PreviewPage
            {
                PageIndex = i,
                PngData = pngBytes,
                Width = page.Width,
                Height = page.Height,
                Label = $"Page {i} ({page.Width}x{page.Height})"
            });
        }

        Pages = pages;
        SelectedPageIndex = 0;
        SelectedPage = pages.Count > 0 ? pages[0] : null;

        // Glyph info
        RenderedGlyphCount = result.Model.Characters.Count;
        FailedCodepointCount = result.FailedCodepoints.Count;
        RequestedGlyphCount = RenderedGlyphCount + FailedCodepointCount;

        GlyphInfoText = FailedCodepointCount > 0
            ? $"Rendered {RenderedGlyphCount}/{RequestedGlyphCount} glyphs ({FailedCodepointCount} failed)"
            : $"Rendered {RenderedGlyphCount} glyphs";

        // Atlas summary with kerning pair count
        var kpCount = result.Model.KerningPairs?.Count ?? 0;
        AtlasSummary = $"{result.Model.Characters.Count} glyphs | Line height: {result.Model.Common.LineHeight} | Base: {result.Model.Common.Base} | {kpCount} kerning pairs";

        // Force PropertyChanged even if already true (for subsequent generations)
        HasResult = false;
        HasResult = true;
    }

    /// <summary>
    /// Moves the selected page index by <paramref name="delta"/>, clamped to valid range.
    /// </summary>
    public void NavigatePage(int delta)
    {
        if (Pages.Count == 0) return;
        var newIndex = Math.Clamp(SelectedPageIndex + delta, 0, Pages.Count - 1);
        SelectedPageIndex = newIndex;
        SelectedPage = Pages[newIndex];
    }

    /// <summary>
    /// Resets the preview to its initial empty state.
    /// </summary>
    public void Clear()
    {
        Pages = Array.Empty<PreviewPage>();
        SelectedPageIndex = 0;
        SelectedPage = null;
        HasResult = false;
        GlyphInfoText = "";
        AtlasSummary = "";
    }
}
