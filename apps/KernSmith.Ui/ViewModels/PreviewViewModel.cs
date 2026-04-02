using Gum.Mvvm;
using KernSmith.Output;
using KernSmith.Output.Model;
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

    // BMFont model for sample text rendering
    public BmFontModel? Model { get => Get<BmFontModel?>(); set => Set(value); }

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
            var page = result.Pages[i];

            pages.Add(new PreviewPage
            {
                PageIndex = i,
                PixelData = page.PixelData,
                Width = page.Width,
                Height = page.Height,
                Label = $"Page {i} ({page.Width}x{page.Height})",
                IsRgba = page.Format == PixelFormat.Rgba32
            });
        }

        Pages = pages;
        Model = result.Model;
        SelectedPageIndex = 0;
        SelectedPage = pages.Count > 0 ? pages[0] : null;

        // Glyph info
        RenderedGlyphCount = result.Model.Characters.Count;
        FailedCodepointCount = result.FailedCodepoints.Count;
        RequestedGlyphCount = RenderedGlyphCount + FailedCodepointCount;

        GlyphInfoText = FailedCodepointCount > 0
            ? $"Rendered {RenderedGlyphCount}/{RequestedGlyphCount} glyphs ({FailedCodepointCount} failed)"
            : $"Rendered {RenderedGlyphCount} glyphs";

        // Atlas summary with dimensions and kerning pair count
        var kpCount = result.Model.KerningPairs?.Count ?? 0;
        var firstPage = result.Model.Pages.Count > 0 ? result.Model.Pages[0] : null;
        var sizeText = firstPage != null ? $"{result.Model.Common.ScaleW}x{result.Model.Common.ScaleH}" : "";
        var pageText = result.Model.Pages.Count > 1 ? $" ({result.Model.Pages.Count} pages)" : "";
        AtlasSummary = $"{sizeText}{pageText} | {result.Model.Characters.Count} glyphs | Line height: {result.Model.Common.LineHeight} | Base: {result.Model.Common.Base} | {kpCount} kerning pairs";

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
        Model = null;
        SelectedPageIndex = 0;
        SelectedPage = null;
        HasResult = false;
        GlyphInfoText = "";
        AtlasSummary = "";
    }
}
