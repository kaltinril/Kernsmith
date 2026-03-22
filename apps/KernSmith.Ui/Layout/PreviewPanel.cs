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
    private readonly CharacterGridViewModel _characterGrid;
    private readonly GraphicsDevice _graphicsDevice;
    private SpriteRuntime? _atlasSprite;
    private Label? _placeholder;
    private Label? _pageLabel;
    private StackPanel? _navRow;

    // Tab switching
    private Panel? _previewContent;
    private CharacterSelectionPanel? _charactersContent;
    private Button? _previewTabBtn;
    private Button? _charactersTabBtn;
    private bool _showingPreview = true;

    public PreviewPanel(PreviewViewModel preview, CharacterGridViewModel characterGrid, GraphicsDevice graphicsDevice)
    {
        _preview = preview;
        _characterGrid = characterGrid;
        _graphicsDevice = graphicsDevice;

        BuildContent();
    }

    private void BuildContent()
    {
        // Tab bar at top
        var tabBar = new StackPanel();
        tabBar.Orientation = Orientation.Horizontal;
        tabBar.Spacing = 4;
        tabBar.Dock(Gum.Wireframe.Dock.Top);
        tabBar.Height = 28;
        this.AddChild(tabBar);

        _previewTabBtn = new Button();
        _previewTabBtn.Text = "Preview";
        _previewTabBtn.Width = 90;
        _previewTabBtn.Height = 26;
        _previewTabBtn.IsEnabled = false; // active tab shown as disabled
        _previewTabBtn.Click += (_, _) => ShowTab(preview: true);
        tabBar.AddChild(_previewTabBtn);

        _charactersTabBtn = new Button();
        _charactersTabBtn.Text = "Characters";
        _charactersTabBtn.Width = 90;
        _charactersTabBtn.Height = 26;
        _charactersTabBtn.Click += (_, _) => ShowTab(preview: false);
        tabBar.AddChild(_charactersTabBtn);

        // Preview content area
        _previewContent = new Panel();
        _previewContent.Y = 30;
        _previewContent.WidthUnits = DimensionUnitType.RelativeToParent;
        _previewContent.Width = 0;
        _previewContent.HeightUnits = DimensionUnitType.RelativeToParent;
        _previewContent.Height = -30;
        this.AddChild(_previewContent);

        BuildPreviewContent();

        // Characters content area
        _charactersContent = new CharacterSelectionPanel(_characterGrid);
        _charactersContent.Y = 30;
        _charactersContent.WidthUnits = DimensionUnitType.RelativeToParent;
        _charactersContent.Width = 0;
        _charactersContent.HeightUnits = DimensionUnitType.RelativeToParent;
        _charactersContent.Height = -30;
        _charactersContent.IsVisible = false;
        this.AddChild(_charactersContent);
    }

    private void BuildPreviewContent()
    {
        if (_previewContent == null) return;

        // Placeholder text centered
        _placeholder = new Label();
        _placeholder.Text = "No atlas generated";
        _placeholder.Anchor(Gum.Wireframe.Anchor.Center);
        _previewContent.AddChild(_placeholder);

        // Atlas sprite (hidden initially)
        _atlasSprite = new SpriteRuntime();
        _atlasSprite.Visible = false;
        _atlasSprite.X = 10;
        _atlasSprite.Y = 30;
        _previewContent.AddChild(_atlasSprite);

        // Page navigation row
        _navRow = new StackPanel();
        _navRow.Orientation = Orientation.Horizontal;
        _navRow.Spacing = 4;
        _navRow.Dock(Gum.Wireframe.Dock.Top);
        _navRow.Height = 28;
        _navRow.IsVisible = false;
        _previewContent.AddChild(_navRow);

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

    private void ShowTab(bool preview)
    {
        _showingPreview = preview;

        if (_previewContent != null)
            _previewContent.IsVisible = preview;
        if (_charactersContent != null)
            _charactersContent.IsVisible = !preview;

        // Toggle button enabled state to indicate active tab
        if (_previewTabBtn != null)
            _previewTabBtn.IsEnabled = !preview;
        if (_charactersTabBtn != null)
            _charactersTabBtn.IsEnabled = preview;
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
