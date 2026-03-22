using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class PreviewPanel : Panel
{
    private readonly PreviewViewModel _preview;
    private readonly CharacterGridViewModel _characterGrid;
    private readonly GraphicsDevice _graphicsDevice;
    private SpriteRuntime? _atlasSprite;
    private SpriteRuntime? _checkerSprite;
    private Label? _placeholder;
    private Label? _pageLabel;
    private Label? _atlasInfoLabel;
    private Label? _glyphInfoLabel;
    private Label? _zoomValueLabel;
    private StackPanel? _navRow;
    private StackPanel? _toolbarRow;
    private Texture2D? _checkerTexture;

    // Tab switching
    private Panel? _previewContent;
    private CharacterSelectionPanel? _charactersContent;
    private Button? _previewTabBtn;
    private Button? _charactersTabBtn;
    private bool _showingPreview = true;

    // Sample text
    private TextBox? _sampleTextBox;

    public PreviewPanel(PreviewViewModel preview, CharacterGridViewModel characterGrid, GraphicsDevice graphicsDevice)
    {
        _preview = preview;
        _characterGrid = characterGrid;
        _graphicsDevice = graphicsDevice;

        _checkerTexture = CreateCheckerTexture();

        BuildContent();
    }

    private Texture2D CreateCheckerTexture()
    {
        // Create a checkered transparency background pattern (16x16 tiles)
        const int tileSize = 8;
        const int texSize = tileSize * 2;
        var texture = new Texture2D(_graphicsDevice, texSize, texSize);
        var pixels = new Color[texSize * texSize];

        var light = new Color(200, 200, 200);
        var dark = new Color(160, 160, 160);

        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                bool isLight = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                pixels[y * texSize + x] = isLight ? light : dark;
            }
        }

        texture.SetData(pixels);
        return texture;
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

        // --- Toolbar row: zoom slider + atlas info ---
        _toolbarRow = new StackPanel();
        _toolbarRow.Orientation = Orientation.Horizontal;
        _toolbarRow.Spacing = 8;
        _toolbarRow.Dock(Gum.Wireframe.Dock.Top);
        _toolbarRow.Height = 28;
        _toolbarRow.IsVisible = false;
        _previewContent.AddChild(_toolbarRow);

        var zoomLabel = new Label();
        zoomLabel.Text = "Zoom:";
        _toolbarRow.AddChild(zoomLabel);

        var zoomSlider = new Slider();
        zoomSlider.Minimum = 25;
        zoomSlider.Maximum = 400;
        zoomSlider.Value = 100;
        zoomSlider.Width = 120;
        zoomSlider.TicksFrequency = 25;
        zoomSlider.IsSnapToTickEnabled = true;
        _toolbarRow.AddChild(zoomSlider);

        _zoomValueLabel = new Label();
        _zoomValueLabel.Text = "100%";
        _toolbarRow.AddChild(_zoomValueLabel);

        zoomSlider.ValueChanged += (_, _) =>
        {
            var pct = (int)zoomSlider.Value;
            _preview.ZoomLevel = pct / 100f;
            if (_zoomValueLabel != null)
                _zoomValueLabel.Text = $"{pct}%";
            ApplyZoom();
        };

        // Separator
        var sep = new Label();
        sep.Text = "|";
        _toolbarRow.AddChild(sep);

        // Atlas info overlay
        _atlasInfoLabel = new Label();
        _atlasInfoLabel.Text = "";
        _toolbarRow.AddChild(_atlasInfoLabel);

        // --- Page navigation row ---
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

        // Per-page info label
        _glyphInfoLabel = new Label();
        _glyphInfoLabel.Text = "";
        _navRow.AddChild(_glyphInfoLabel);

        // Placeholder text centered
        _placeholder = new Label();
        _placeholder.Text = "No atlas generated";
        _placeholder.Anchor(Gum.Wireframe.Anchor.Center);
        _previewContent.AddChild(_placeholder);

        // Checkered transparency background (hidden initially)
        _checkerSprite = new SpriteRuntime();
        _checkerSprite.Visible = false;
        _checkerSprite.X = 10;
        _checkerSprite.Y = 60;
        if (_checkerTexture != null)
            _checkerSprite.Texture = _checkerTexture;
        _previewContent.AddChild(_checkerSprite);

        // Atlas sprite (hidden initially)
        _atlasSprite = new SpriteRuntime();
        _atlasSprite.Visible = false;
        _atlasSprite.X = 10;
        _atlasSprite.Y = 60;
        _previewContent.AddChild(_atlasSprite);

        // --- Sample Text section ---
        var sampleRow = new StackPanel();
        sampleRow.Orientation = Orientation.Vertical;
        sampleRow.Spacing = 4;
        sampleRow.Dock(Gum.Wireframe.Dock.Bottom);
        sampleRow.Height = 60;
        _previewContent.AddChild(sampleRow);

        var sampleHeader = new Label();
        sampleHeader.Text = "Sample Text:";
        sampleRow.AddChild(sampleHeader);

        _sampleTextBox = new TextBox();
        _sampleTextBox.Width = 0;
        _sampleTextBox.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _sampleTextBox.Height = 28;
        _sampleTextBox.Text = _preview.SampleText;
        _sampleTextBox.Placeholder = "Type sample text...";
        _sampleTextBox.TextChanged += (_, _) =>
        {
            _preview.SampleText = _sampleTextBox.Text;
        };
        sampleRow.AddChild(_sampleTextBox);

        // Listen for result changes
        _preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewViewModel.HasResult))
            {
                if (_preview.HasResult)
                {
                    UpdateDisplay();
                    _navRow.IsVisible = _preview.Pages.Count > 1;
                    if (_toolbarRow != null)
                        _toolbarRow.IsVisible = true;
                }
                else
                {
                    ShowPlaceholder();
                    _navRow.IsVisible = false;
                    if (_toolbarRow != null)
                        _toolbarRow.IsVisible = false;
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
        {
            var page = _preview.SelectedPage;
            _pageLabel.Text = $"Page {_preview.SelectedPageIndex + 1} of {_preview.Pages.Count}  ({page.Width}x{page.Height})";
        }

        if (_atlasInfoLabel != null && _preview.SelectedPage != null)
        {
            var page = _preview.SelectedPage;
            _atlasInfoLabel.Text = $"Atlas: {page.Width}x{page.Height}  |  {_preview.Pages.Count} page(s)";
        }

        if (_glyphInfoLabel != null)
            _glyphInfoLabel.Text = _preview.GlyphInfoText;
    }

    private void ShowAtlas(PreviewPage page)
    {
        if (_placeholder != null)
            _placeholder.IsVisible = false;

        using var stream = new MemoryStream(page.PngData);
        var texture = Texture2D.FromStream(_graphicsDevice, stream);
        var zoom = _preview.ZoomLevel;
        var scaledWidth = (int)(texture.Width * zoom);
        var scaledHeight = (int)(texture.Height * zoom);

        // Show checkered background behind atlas
        if (_checkerSprite != null && _checkerTexture != null)
        {
            _checkerSprite.Texture = _checkerTexture;
            _checkerSprite.Width = scaledWidth;
            _checkerSprite.Height = scaledHeight;
            _checkerSprite.Visible = true;
        }

        if (_atlasSprite != null)
        {
            _atlasSprite.Texture = texture;
            _atlasSprite.Width = scaledWidth;
            _atlasSprite.Height = scaledHeight;
            _atlasSprite.Visible = true;
        }
    }

    private void ApplyZoom()
    {
        if (_atlasSprite?.Texture == null) return;

        var zoom = _preview.ZoomLevel;
        var texture = _atlasSprite.Texture;
        var scaledWidth = (int)(texture.Width * zoom);
        var scaledHeight = (int)(texture.Height * zoom);

        _atlasSprite.Width = scaledWidth;
        _atlasSprite.Height = scaledHeight;

        if (_checkerSprite != null)
        {
            _checkerSprite.Width = scaledWidth;
            _checkerSprite.Height = scaledHeight;
        }
    }

    private void ShowPlaceholder()
    {
        if (_atlasSprite != null)
            _atlasSprite.Visible = false;
        if (_checkerSprite != null)
            _checkerSprite.Visible = false;
        if (_placeholder != null)
            _placeholder.IsVisible = true;
    }
}
