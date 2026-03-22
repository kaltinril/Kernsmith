using Gum.Mvvm;
using KernSmith.Output;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

public class PreviewViewModel : ViewModel
{
    public IReadOnlyList<PreviewPage> Pages { get => Get<IReadOnlyList<PreviewPage>>(); set => Set(value); }
    public int SelectedPageIndex { get => Get<int>(); set => Set(value); }
    public PreviewPage? SelectedPage { get => Get<PreviewPage?>(); set => Set(value); }
    public bool HasResult { get => Get<bool>(); set => Set(value); }

    public PreviewViewModel()
    {
        Pages = Array.Empty<PreviewPage>();
    }

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
        HasResult = true;
    }

    public void NavigatePage(int delta)
    {
        if (Pages.Count == 0) return;
        var newIndex = Math.Clamp(SelectedPageIndex + delta, 0, Pages.Count - 1);
        SelectedPageIndex = newIndex;
        SelectedPage = Pages[newIndex];
    }

    public void Clear()
    {
        Pages = Array.Empty<PreviewPage>();
        SelectedPageIndex = 0;
        SelectedPage = null;
        HasResult = false;
    }
}
