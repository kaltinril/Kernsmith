using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class PreviewPanel : Panel
{
    private readonly PreviewViewModel _preview;
    private readonly GraphicsDevice _graphicsDevice;
    private SpriteRuntime? _atlasSprite;
    private Label? _placeholder;
    private Label? _pageLabel;
    private StackPanel? _navRow;

    public PreviewPanel(PreviewViewModel preview, GraphicsDevice graphicsDevice)
    {
        _preview = preview;
        _graphicsDevice = graphicsDevice;

        BuildContent();
    }

    private void BuildContent()
    {
        // Placeholder text centered
        _placeholder = new Label();
        _placeholder.Text = "No atlas generated";
        _placeholder.Anchor(Gum.Wireframe.Anchor.Center);
        this.AddChild(_placeholder);

        // Atlas sprite (hidden initially)
        _atlasSprite = new SpriteRuntime();
        _atlasSprite.Visible = false;
        _atlasSprite.X = 10;
        _atlasSprite.Y = 30;
        this.AddChild(_atlasSprite);

        // Page navigation row
        _navRow = new StackPanel();
        _navRow.Orientation = Orientation.Horizontal;
        _navRow.Spacing = 4;
        _navRow.Dock(Gum.Wireframe.Dock.Top);
        _navRow.Height = 28;
        _navRow.IsVisible = false;
        this.AddChild(_navRow);

        _pageLabel = new Label();
        _pageLabel.Text = "";
        _navRow.AddChild(_pageLabel);

        var prevBtn = new Button();
        prevBtn.Text = "<";
        prevBtn.Width = 30;
        prevBtn.Height = 24;
        prevBtn.Click += (_, _) =>
        {
            _preview.NavigatePage(-1);
            UpdateDisplay();
        };
        _navRow.AddChild(prevBtn);

        var nextBtn = new Button();
        nextBtn.Text = ">";
        nextBtn.Width = 30;
        nextBtn.Height = 24;
        nextBtn.Click += (_, _) =>
        {
            _preview.NavigatePage(1);
            UpdateDisplay();
        };
        _navRow.AddChild(nextBtn);

        // Listen for result changes
        _preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewViewModel.HasResult))
            {
                if (_preview.HasResult)
                {
                    UpdateDisplay();
                    _navRow.IsVisible = _preview.Pages.Count > 1;
                }
                else
                {
                    ShowPlaceholder();
                    _navRow.IsVisible = false;
                }
            }
        };
    }

    private void UpdateDisplay()
    {
        if (_preview.SelectedPage == null) return;

        ShowAtlas(_preview.SelectedPage);
        if (_pageLabel != null)
            _pageLabel.Text = $"Page {_preview.SelectedPageIndex + 1} of {_preview.Pages.Count}";
    }

    private void ShowAtlas(PreviewPage page)
    {
        if (_placeholder != null)
            _placeholder.IsVisible = false;

        using var stream = new MemoryStream(page.PngData);
        var texture = Texture2D.FromStream(_graphicsDevice, stream);

        if (_atlasSprite != null)
        {
            _atlasSprite.Texture = texture;
            _atlasSprite.Width = texture.Width;
            _atlasSprite.Height = texture.Height;
            _atlasSprite.Visible = true;
        }
    }

    private void ShowPlaceholder()
    {
        if (_atlasSprite != null)
            _atlasSprite.Visible = false;
        if (_placeholder != null)
            _placeholder.IsVisible = true;
    }
}
