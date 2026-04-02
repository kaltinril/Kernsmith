using global::Gum.DataTypes;
using global::Gum.Managers;
using global::Gum.Forms.Controls;
using KernSmith.Output.Model;
using KernSmith.Ui.Models;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Center panel with Preview/Characters tab switching. The preview tab displays the generated
/// atlas texture with zoom slider, page navigation, glyph info, and scroll-wheel zoom plus
/// middle-click pan. The characters tab hosts <see cref="CharacterSelectionPanel"/>.
/// </summary>
public class PreviewPanel : Panel
{
    private readonly PreviewViewModel _preview;
    private readonly CharacterGridViewModel _characterGrid;
    private readonly GraphicsDevice _graphicsDevice;
    private SpriteRuntime? _atlasSprite;
    private SpriteRuntime? _checkerSprite;
    private Texture2D? _currentAtlasTexture;
    private Label? _placeholder;
    private Label? _pageLabel;
    private Label? _glyphInfoLabel;
    private Label? _zoomValueLabel;
    private Label? _atlasSummaryLabel;
    private Label? _failedWarningLabel;
    private StackPanel? _navRow;
    private StackPanel? _toolbarRow;
    private ContainerRuntime? _atlasContentArea;
    private Slider? _zoomSlider;
    private Texture2D? _checkerTexture;

    // Tab switching
    private Panel? _previewContent;
    private CharacterSelectionPanel? _charactersContent;
    private Panel? _sampleTextContent;
    private Button? _previewTabBtn;
    private Button? _charactersTabBtn;
    private Button? _sampleTextTabBtn;
    private enum ActiveTab { Preview, Characters, SampleText }
    private ActiveTab _activeTab = ActiveTab.Preview;

    // Sample text rendering
    private ContainerRuntime? _sampleTextContainer;
    private TextBox? _sampleTextBox;
    private List<Texture2D>? _atlasPageTextures;
    private Slider? _sampleZoomSlider;
    private Label? _sampleZoomValueLabel;
    private float _sampleZoomLevel = 1.0f;

    // Texture caches — avoid re-decoding PNGs on every page switch / tab switch
    private readonly Dictionary<int, Texture2D> _previewTextureCache = new();
    private readonly Dictionary<int, Texture2D> _sampleTextureCache = new();

    // Sample text pan state
    private float _samplePanOffsetX, _samplePanOffsetY;
    private float _baseSampleX = 8, _baseSampleY = 72;
    private int _samplePanStartX, _samplePanStartY;
    private bool _isSamplePanning;

    // UI scale controls
    private Label? _uiScaleLabel;
    public event Action? UiScaleUpRequested;
    public event Action? UiScaleDownRequested;
    public event Action? UiScaleResetRequested;

    // Layout constants
    private const float TabBarHeight = 30;
    private const float ToolbarY = 32;
    private const float ToolbarHeight = 28;
    private const float AtlasContentY = 32;

    // Pan/zoom input state
    private int _previousScrollValue;
    private bool _isPanning;
    private int _panStartX, _panStartY;
    private float _panOffsetX, _panOffsetY;
    private float _baseSpriteX = 10, _baseSpriteY = 0;
    private bool _hasUserZoom;

    public PreviewPanel(PreviewViewModel preview, CharacterGridViewModel characterGrid, GraphicsDevice graphicsDevice)
    {
        _preview = preview;
        _characterGrid = characterGrid;
        _graphicsDevice = graphicsDevice;

        BuildContent();
    }

    private Texture2D CreateCheckerTexture(int width, int height)
    {
        const int tileSize = 8;
        var texture = new Texture2D(_graphicsDevice, width, height);
        var pixels = new Color[width * height];

        var light = Theme.CheckerLight;
        var dark = Theme.CheckerDark;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isLight = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                pixels[y * width + x] = isLight ? light : dark;
            }
        }

        texture.SetData(pixels);
        return texture;
    }

    private void BuildContent()
    {
        // Tab bar at Y=0, height=30
        var tabBar = new StackPanel();
        tabBar.Orientation = Orientation.Horizontal;
        tabBar.Spacing = 4;
        tabBar.X = 4;
        tabBar.Y = 0;
        tabBar.Height = TabBarHeight;
        tabBar.WidthUnits = DimensionUnitType.RelativeToParent;
        tabBar.Width = -8;
        this.AddChild(tabBar);

        _previewTabBtn = new Button();
        _previewTabBtn.Text = "Preview";
        _previewTabBtn.Width = 90;
        _previewTabBtn.Click += (_, _) => SwitchTab(ActiveTab.Preview);
        tabBar.AddChild(_previewTabBtn);

        _charactersTabBtn = new Button();
        _charactersTabBtn.Text = "Characters";
        _charactersTabBtn.Width = 90;
        _charactersTabBtn.Click += (_, _) => SwitchTab(ActiveTab.Characters);
        tabBar.AddChild(_charactersTabBtn);

        _sampleTextTabBtn = new Button();
        _sampleTextTabBtn.Text = "Sample";
        _sampleTextTabBtn.Width = 70;
        _sampleTextTabBtn.Click += (_, _) => SwitchTab(ActiveTab.SampleText);
        tabBar.AddChild(_sampleTextTabBtn);

        // Style initial active tab
        StyleTabButtons(_activeTab);

        // UI scale controls — right-aligned in tab bar
        var scaleSpacer = new ContainerRuntime();
        scaleSpacer.WidthUnits = DimensionUnitType.Ratio;
        scaleSpacer.Width = 1;
        scaleSpacer.Height = TabBarHeight;
        tabBar.AddChild(scaleSpacer);

        var scaleDownBtn = new Button();
        scaleDownBtn.Text = "-";
        scaleDownBtn.Width = 28;
        scaleDownBtn.Click += (_, _) => UiScaleDownRequested?.Invoke();
        tabBar.AddChild(scaleDownBtn);

        _uiScaleLabel = new Label();
        _uiScaleLabel.Text = "100%";
        _uiScaleLabel.Visual.WidthUnits = DimensionUnitType.RelativeToChildren;
        _uiScaleLabel.Visual.HeightUnits = DimensionUnitType.Absolute;
        _uiScaleLabel.Height = TabBarHeight;
        _uiScaleLabel.Visual.SetProperty("VerticalAlignment",
            RenderingLibrary.Graphics.VerticalAlignment.Center);
        tabBar.AddChild(_uiScaleLabel);

        var scaleUpBtn = new Button();
        scaleUpBtn.Text = "+";
        scaleUpBtn.Width = 28;
        scaleUpBtn.Click += (_, _) => UiScaleUpRequested?.Invoke();
        tabBar.AddChild(scaleUpBtn);

        var scaleResetBtn = new Button();
        scaleResetBtn.Text = "Reset";
        scaleResetBtn.Width = 50;
        scaleResetBtn.Click += (_, _) => UiScaleResetRequested?.Invoke();
        tabBar.AddChild(scaleResetBtn);

        // Preview content area starts below tab bar — clips children so atlas doesn't overflow
        _previewContent = new Panel();
        _previewContent.Y = TabBarHeight;
        _previewContent.WidthUnits = DimensionUnitType.RelativeToParent;
        _previewContent.Width = 0;
        _previewContent.HeightUnits = DimensionUnitType.RelativeToParent;
        _previewContent.Height = -TabBarHeight;
        _previewContent.Visual.ClipsChildren = true;
        this.AddChild(_previewContent);

        BuildPreviewContent();

        // Characters content area (same position, toggled via visibility)
        _charactersContent = new CharacterSelectionPanel(_characterGrid);
        _charactersContent.Y = TabBarHeight;
        _charactersContent.WidthUnits = DimensionUnitType.RelativeToParent;
        _charactersContent.Width = 0;
        _charactersContent.HeightUnits = DimensionUnitType.RelativeToParent;
        _charactersContent.Height = -TabBarHeight;
        _charactersContent.IsVisible = false;
        this.AddChild(_charactersContent);

        // Sample text content area (same position, toggled via visibility)
        _sampleTextContent = new Panel();
        _sampleTextContent.Y = TabBarHeight;
        _sampleTextContent.WidthUnits = DimensionUnitType.RelativeToParent;
        _sampleTextContent.Width = 0;
        _sampleTextContent.HeightUnits = DimensionUnitType.RelativeToParent;
        _sampleTextContent.Height = -TabBarHeight;
        _sampleTextContent.Visual.ClipsChildren = true;
        _sampleTextContent.IsVisible = false;
        this.AddChild(_sampleTextContent);

        BuildSampleTextContent();
    }

    private void BuildPreviewContent()
    {
        if (_previewContent == null) return;

        // --- Toolbar row at Y=2: zoom slider + atlas info + summary ---
        _toolbarRow = new StackPanel();
        _toolbarRow.Orientation = Orientation.Horizontal;
        _toolbarRow.Spacing = 8;
        _toolbarRow.X = 4;
        _toolbarRow.Y = 2;
        _toolbarRow.Height = ToolbarHeight;
        _toolbarRow.WidthUnits = DimensionUnitType.RelativeToParent;
        _toolbarRow.Width = -8;
        _toolbarRow.IsVisible = false;
        _previewContent.AddChild(_toolbarRow);

        var zoomLabel = new Label();
        zoomLabel.Text = "Zoom:";
        _toolbarRow.AddChild(zoomLabel);

        _zoomSlider = new Slider();
        _zoomSlider.Minimum = 25;
        _zoomSlider.Maximum = 1000;
        _zoomSlider.Value = 100;
        _zoomSlider.Width = 120;
        _zoomSlider.TicksFrequency = 25;
        _zoomSlider.IsSnapToTickEnabled = true;
        _toolbarRow.AddChild(_zoomSlider);

        _zoomValueLabel = new Label();
        _zoomValueLabel.Text = "100%";
        _toolbarRow.AddChild(_zoomValueLabel);

        _zoomSlider.ValueChanged += (_, _) =>
        {
            var pct = (int)_zoomSlider.Value;
            _preview.ZoomLevel = pct / 100f;
            if (_zoomValueLabel != null)
                _zoomValueLabel.Text = $"{pct}%";
            _hasUserZoom = true;
            ApplyZoom();
        };

        // Separator
        var sep = new Label();
        sep.Text = "|";
        _toolbarRow.AddChild(sep);

        // Atlas summary in toolbar (compact info)
        _atlasSummaryLabel = new Label();
        _atlasSummaryLabel.Text = "";
        _toolbarRow.AddChild(_atlasSummaryLabel);

        // --- Page navigation row (below toolbar) ---
        _navRow = new StackPanel();
        _navRow.Orientation = Orientation.Horizontal;
        _navRow.Spacing = Theme.SectionSpacing;
        _navRow.X = 4;
        _navRow.Y = ToolbarHeight + 4;
        _navRow.Height = 24;
        _navRow.IsVisible = false;
        _previewContent.AddChild(_navRow);

        _pageLabel = new Label();
        _pageLabel.Text = "";
        _navRow.AddChild(_pageLabel);

        var prevBtn = new Button();
        prevBtn.Text = "<";
        prevBtn.Width = 30;
        prevBtn.Click += (_, _) =>
        {
            _preview.NavigatePage(-1);
            UpdateDisplay();
        };
        _navRow.AddChild(prevBtn);

        var nextBtn = new Button();
        nextBtn.Text = ">";
        nextBtn.Width = 30;
        nextBtn.Click += (_, _) =>
        {
            _preview.NavigatePage(1);
            UpdateDisplay();
        };
        _navRow.AddChild(nextBtn);

        // Per-page glyph info + failed warning combined
        _glyphInfoLabel = new Label();
        _glyphInfoLabel.Text = "";
        _navRow.AddChild(_glyphInfoLabel);

        _failedWarningLabel = new Label();
        _failedWarningLabel.Text = "";
        _failedWarningLabel.IsVisible = false;
        _navRow.AddChild(_failedWarningLabel);

        // Placeholder text centered
        _placeholder = new Label();
        _placeholder.Text = "Drop a font file here or use Browse to get started";
        _placeholder.Anchor(global::Gum.Wireframe.Anchor.Center);
        _previewContent.AddChild(_placeholder);

        // Clipped container for atlas sprites — prevents them from overlapping the toolbar/nav
        _atlasContentArea = new ContainerRuntime();
        _atlasContentArea.Y = AtlasContentY;
        _atlasContentArea.WidthUnits = DimensionUnitType.RelativeToParent;
        _atlasContentArea.Width = 0;
        _atlasContentArea.HeightUnits = DimensionUnitType.RelativeToParent;
        _atlasContentArea.Height = -AtlasContentY;
        _atlasContentArea.ClipsChildren = true;
        _previewContent.AddChild(_atlasContentArea);

        // Checkered transparency background (hidden initially)
        _checkerSprite = new SpriteRuntime();
        _checkerSprite.Visible = false;
        _checkerSprite.X = 10;
        _checkerSprite.Y = 0;
        _checkerSprite.WidthUnits = DimensionUnitType.Absolute;
        _checkerSprite.HeightUnits = DimensionUnitType.Absolute;
        _checkerSprite.TextureAddress = global::Gum.Managers.TextureAddress.EntireTexture;
        _atlasContentArea.Children.Add(_checkerSprite);

        // Atlas sprite (hidden initially)
        _atlasSprite = new SpriteRuntime();
        _atlasSprite.Visible = false;
        _atlasSprite.X = 10;
        _atlasSprite.Y = 0;
        _atlasSprite.WidthUnits = DimensionUnitType.Absolute;
        _atlasSprite.HeightUnits = DimensionUnitType.Absolute;
        _atlasSprite.TextureAddress = global::Gum.Managers.TextureAddress.EntireTexture;
        _atlasContentArea.Children.Add(_atlasSprite);

        // Listen for result changes
        _preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewViewModel.HasResult))
            {
                // Invalidate all texture caches on new generation
                ClearAllTextureCaches();

                if (_preview.HasResult)
                {
                    UpdateDisplay();
                    _navRow.IsVisible = _preview.Pages.Count > 1;
                    if (_toolbarRow != null)
                        _toolbarRow.IsVisible = true;

                    // Refresh sample text if that tab is active
                    if (_activeTab == ActiveTab.SampleText)
                        RenderSampleText();
                }
                else
                {
                    ShowPlaceholder();
                    _navRow.IsVisible = false;
                    if (_toolbarRow != null)
                        _toolbarRow.IsVisible = false;
                    if (_sampleTextContainer != null)
                        _sampleTextContainer.Visible = false;
                }
            }
        };
    }

    private void SwitchTab(ActiveTab tab)
    {
        _activeTab = tab;

        if (_previewContent != null)
            _previewContent.IsVisible = tab == ActiveTab.Preview;
        if (_charactersContent != null)
            _charactersContent.IsVisible = tab == ActiveTab.Characters;
        if (_sampleTextContent != null)
            _sampleTextContent.IsVisible = tab == ActiveTab.SampleText;

        StyleTabButtons(tab);

        // Refresh sample text rendering when switching to that tab
        if (tab == ActiveTab.SampleText && _preview.HasResult)
            RenderSampleText();
    }

    private void StyleTabButtons(ActiveTab activeTab)
    {
        StyleTab(_previewTabBtn, activeTab == ActiveTab.Preview);
        StyleTab(_charactersTabBtn, activeTab == ActiveTab.Characters);
        StyleTab(_sampleTextTabBtn, activeTab == ActiveTab.SampleText);
    }

    private static void StyleTab(Button? btn, bool isActive)
    {
        if (btn == null) return;
        var buttonVisual = (global::Gum.Forms.DefaultVisuals.V3.ButtonVisual)btn.Visual;

        var activeColor = Theme.Accent;
        var activeHover = Theme.AccentHover;
        var activePushed = new Color(0, 90, 170);
        var inactiveColor = Theme.PanelBorder;
        var inactiveHover = new Color(75, 75, 80);
        var inactivePushed = new Color(50, 50, 55);

        var enabled = buttonVisual.States.Enabled;
        enabled.Clear();
        enabled.Apply = () =>
        {
            buttonVisual.Background.Color = isActive ? activeColor : inactiveColor;
        };

        var highlighted = buttonVisual.States.Highlighted;
        highlighted.Clear();
        highlighted.Apply = () =>
        {
            buttonVisual.Background.Color = isActive ? activeHover : inactiveHover;
        };

        var pushed = buttonVisual.States.Pushed;
        pushed.Clear();
        pushed.Apply = () =>
        {
            buttonVisual.Background.Color = isActive ? activePushed : inactivePushed;
        };

        btn.UpdateState();
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

        if (_glyphInfoLabel != null)
            _glyphInfoLabel.Text = _preview.GlyphInfoText;

        // Atlas summary in toolbar
        if (_atlasSummaryLabel != null)
            _atlasSummaryLabel.Text = _preview.AtlasSummary;

        // Failed codepoints warning (shown in nav row)
        if (_failedWarningLabel != null)
        {
            if (_preview.FailedCodepointCount > 0)
            {
                _failedWarningLabel.Text = $"| {_preview.FailedCodepointCount} failed";
                _failedWarningLabel.IsVisible = true;
            }
            else
            {
                _failedWarningLabel.Text = "";
                _failedWarningLabel.IsVisible = false;
            }
        }
    }

    private void ShowAtlas(PreviewPage page)
    {
        if (_placeholder != null)
            _placeholder.IsVisible = false;

        // Use cached texture if available; otherwise create from raw pixel data
        if (!_previewTextureCache.TryGetValue(page.PageIndex, out var texture))
        {
            texture = CreateTextureFromPixels(page);
            _previewTextureCache[page.PageIndex] = texture;
        }

        _currentAtlasTexture = texture;

        // Auto-fit on first display only; preserve user's zoom on regenerate
        if (!_hasUserZoom)
            AutoFitZoom(texture.Width, texture.Height);

        var zoom = _preview.ZoomLevel;
        var scaledWidth = (int)Math.Round(texture.Width * zoom);
        var scaledHeight = (int)Math.Round(texture.Height * zoom);

        // Show checkered background behind atlas (sized to match); only recreate when dimensions change
        if (_checkerSprite != null)
        {
            if (_checkerTexture == null || _checkerTexture.Width != texture.Width || _checkerTexture.Height != texture.Height)
            {
                _checkerTexture?.Dispose();
                _checkerTexture = CreateCheckerTexture(texture.Width, texture.Height);
            }
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

    private void AutoFitZoom(int atlasWidth, int atlasHeight)
    {
        if (_previewContent == null || atlasWidth <= 0 || atlasHeight <= 0) return;

        // Available space: panel width minus margins, panel height minus toolbar/nav/sample areas
        var availableWidth = Math.Max(1f, _previewContent.Visual.GetAbsoluteWidth() - 20);
        var availableHeight = Math.Max(1f, _previewContent.Visual.GetAbsoluteHeight() - AtlasContentY - 10);

        var fitZoom = Math.Min(availableWidth / atlasWidth, availableHeight / atlasHeight);
        // Clamp to slider range and don't exceed 100% (no upscaling by default)
        fitZoom = Math.Clamp(fitZoom, 0.25f, 1.0f);

        _preview.ZoomLevel = fitZoom;

        var pct = (int)(fitZoom * 100);
        if (_zoomSlider != null)
            _zoomSlider.Value = pct;
        if (_zoomValueLabel != null)
            _zoomValueLabel.Text = $"{pct}%";
    }

    private void ApplyZoom()
    {
        if (_atlasSprite?.Texture == null) return;

        var zoom = _preview.ZoomLevel;
        var texture = _atlasSprite.Texture;
        var scaledWidth = (int)Math.Round(texture.Width * zoom);
        var scaledHeight = (int)Math.Round(texture.Height * zoom);

        _atlasSprite.Width = scaledWidth;
        _atlasSprite.Height = scaledHeight;

        if (_checkerSprite != null)
        {
            _checkerSprite.Width = scaledWidth;
            _checkerSprite.Height = scaledHeight;
        }

    }

    private void ApplySampleZoom()
    {
        if (_preview.HasResult)
            RenderSampleText();
    }

    private void ShowPlaceholder()
    {
        if (_atlasSprite != null)
            _atlasSprite.Visible = false;
        if (_checkerSprite != null)
            _checkerSprite.Visible = false;
        if (_placeholder != null)
            _placeholder.IsVisible = true;
        if (_atlasSummaryLabel != null)
            _atlasSummaryLabel.Text = "";
        if (_failedWarningLabel != null)
            _failedWarningLabel.IsVisible = false;
    }

    private void BuildSampleTextContent()
    {
        if (_sampleTextContent == null) return;

        // Input row at top
        var inputRow = new StackPanel();
        inputRow.Orientation = Orientation.Horizontal;
        inputRow.Spacing = 8;
        inputRow.X = 8;
        inputRow.Y = 8;
        inputRow.Height = 28;
        inputRow.WidthUnits = DimensionUnitType.RelativeToParent;
        inputRow.Width = -16;
        _sampleTextContent.AddChild(inputRow);

        var label = new Label();
        label.Text = "Text:";
        inputRow.AddChild(label);

        _sampleTextBox = new TextBox();
        _sampleTextBox.Text = _preview.SampleText;
        _sampleTextBox.Width = 400;
        _sampleTextBox.TextChanged += (_, _) =>
        {
            _preview.SampleText = _sampleTextBox.Text;
            if (_preview.HasResult)
                RenderSampleText();
        };
        inputRow.AddChild(_sampleTextBox);

        // Zoom toolbar row
        var sampleToolbar = new StackPanel();
        sampleToolbar.Orientation = Orientation.Horizontal;
        sampleToolbar.Spacing = 8;
        sampleToolbar.X = 8;
        sampleToolbar.Y = 36;
        sampleToolbar.Height = 28;
        sampleToolbar.WidthUnits = DimensionUnitType.RelativeToParent;
        sampleToolbar.Width = -16;
        _sampleTextContent.AddChild(sampleToolbar);

        var sampleZoomLabel = new Label();
        sampleZoomLabel.Text = "Zoom:";
        sampleToolbar.AddChild(sampleZoomLabel);

        _sampleZoomSlider = new Slider();
        _sampleZoomSlider.Minimum = 25;
        _sampleZoomSlider.Maximum = 1000;
        _sampleZoomSlider.Value = 100;
        _sampleZoomSlider.Width = 120;
        _sampleZoomSlider.TicksFrequency = 25;
        _sampleZoomSlider.IsSnapToTickEnabled = true;
        sampleToolbar.AddChild(_sampleZoomSlider);

        _sampleZoomValueLabel = new Label();
        _sampleZoomValueLabel.Text = "100%";
        sampleToolbar.AddChild(_sampleZoomValueLabel);

        _sampleZoomSlider.ValueChanged += (_, _) =>
        {
            var scale = (int)_sampleZoomSlider.Value / 100f;
            _sampleZoomLevel = scale;
            if (_sampleZoomValueLabel != null)
                _sampleZoomValueLabel.Text = $"{(int)_sampleZoomSlider.Value}%";
            ApplySampleZoom();
        };

        // Placeholder when no result is available
        var samplePlaceholder = new Label();
        samplePlaceholder.Text = "Generate a bitmap font first to preview sample text";
        samplePlaceholder.Anchor(global::Gum.Wireframe.Anchor.Center);
        _sampleTextContent.AddChild(samplePlaceholder);

        // Container for per-glyph sprites
        _sampleTextContainer = new ContainerRuntime();
        _sampleTextContainer.Visible = false;
        _sampleTextContainer.X = 8;
        _sampleTextContainer.Y = 72;
        _sampleTextContainer.WidthUnits = DimensionUnitType.RelativeToChildren;
        _sampleTextContainer.HeightUnits = DimensionUnitType.RelativeToChildren;
        _sampleTextContent.Visual.Children.Add(_sampleTextContainer);

        // Hide placeholder when result is available
        _preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PreviewViewModel.HasResult))
                samplePlaceholder.IsVisible = !_preview.HasResult;
        };
    }

    private void RenderSampleText()
    {
        var model = _preview.Model;
        if (model == null || _sampleTextContainer == null) return;

        // Clear previous glyph sprites
        _sampleTextContainer.Children.Clear();

        var text = _preview.SampleText ?? "";
        if (string.IsNullOrEmpty(text))
        {
            _sampleTextContainer.Visible = false;
            return;
        }

        // Build character lookup from BmFontModel
        var charMap = new Dictionary<int, CharEntry>();
        foreach (var ch in model.Characters)
            charMap[ch.Id] = ch;

        // Build kerning lookup
        var kerningMap = new Dictionary<(int, int), int>();
        if (model.KerningPairs != null)
        {
            foreach (var kp in model.KerningPairs)
                kerningMap[(kp.First, kp.Second)] = kp.Amount;
        }

        // Load atlas page textures if not yet loaded — reuse preview cache where possible
        if (_atlasPageTextures == null || _atlasPageTextures.Count != _preview.Pages.Count)
        {
            DisposeAtlasPageTextures();
            _atlasPageTextures = new List<Texture2D>();
            foreach (var page in _preview.Pages)
            {
                if (page.IsRgba)
                {
                    // RGBA pages can share the preview cache texture directly
                    if (!_previewTextureCache.TryGetValue(page.PageIndex, out var tex))
                    {
                        tex = CreateTextureFromPixels(page);
                        _previewTextureCache[page.PageIndex] = tex;
                    }
                    _atlasPageTextures.Add(tex);
                }
                else
                {
                    // Grayscale atlas: pixel luminance IS the alpha channel.
                    // Remap to (255,255,255,L) so glyph pixels are white with
                    // correct alpha and background is transparent.
                    // Cached so conversion only happens once per generation.
                    if (!_sampleTextureCache.TryGetValue(page.PageIndex, out var tex))
                    {
                        tex = new Texture2D(_graphicsDevice, page.Width, page.Height);
                        var pixels = new Color[page.Width * page.Height];
                        for (int i = 0; i < page.PixelData.Length; i++)
                        {
                            var alpha = page.PixelData[i];
                            pixels[i] = new Color((byte)255, (byte)255, (byte)255, alpha);
                        }
                        tex.SetData(pixels);
                        _sampleTextureCache[page.PageIndex] = tex;
                    }
                    _atlasPageTextures.Add(tex);
                }
            }
        }

        if (_atlasPageTextures.Count == 0) return;

        var zoom = _sampleZoomLevel;
        var lineHeight = model.Common.LineHeight;
        var padLeft = model.Info.Padding.Left;
        var padTop = model.Info.Padding.Up;
        var lines = text.Split('\n');

        int cursorY = 0;
        int maxRight = 0;
        int maxBottom = 0;

        foreach (var line in lines)
        {
            int cursorX = 0;
            int prevId = -1;

            foreach (var c in line)
            {
                int id = c;
                if (!charMap.TryGetValue(id, out var entry))
                    continue;

                if (prevId >= 0 && kerningMap.TryGetValue((prevId, id), out var kern))
                    cursorX += kern;

                if (entry.Page >= 0 && entry.Page < _atlasPageTextures.Count && entry.Width > 0 && entry.Height > 0)
                {
                    var glyph = new SpriteRuntime();
                    glyph.Texture = _atlasPageTextures[entry.Page];
                    glyph.TextureLeft = entry.X + padLeft;
                    glyph.TextureTop = entry.Y + padTop;
                    glyph.TextureWidth = entry.Width;
                    glyph.TextureHeight = entry.Height;
                    glyph.TextureAddress = TextureAddress.Custom;
                    glyph.X = (cursorX + entry.XOffset) * zoom;
                    glyph.Y = (cursorY + entry.YOffset) * zoom;
                    glyph.WidthUnits = DimensionUnitType.Absolute;
                    glyph.HeightUnits = DimensionUnitType.Absolute;
                    glyph.Width = entry.Width * zoom;
                    glyph.Height = entry.Height * zoom;
                    _sampleTextContainer.Children.Add(glyph);

                    maxRight = Math.Max(maxRight, (int)((cursorX + entry.XOffset + entry.Width) * zoom));
                    maxBottom = Math.Max(maxBottom, (int)((cursorY + entry.YOffset + entry.Height) * zoom));
                }

                cursorX += entry.XAdvance;
                prevId = id;
            }

            cursorY += lineHeight;
        }

        // Set container size explicitly so nothing gets clipped
        _sampleTextContainer.WidthUnits = DimensionUnitType.Absolute;
        _sampleTextContainer.HeightUnits = DimensionUnitType.Absolute;
        _sampleTextContainer.Width = maxRight;
        _sampleTextContainer.Height = maxBottom;
        _sampleTextContainer.Visible = true;
    }

    private void DisposeAtlasPageTextures()
    {
        // The _atlasPageTextures list references textures owned by the two caches,
        // so just null the list — actual disposal happens via the caches below.
        _atlasPageTextures = null;
    }

    private void ClearAllTextureCaches()
    {
        // Unset sprite textures before disposing to avoid referencing disposed GPU resources
        if (_atlasSprite != null)
            _atlasSprite.Texture = null;
        _currentAtlasTexture = null;

        _atlasPageTextures = null;

        foreach (var tex in _previewTextureCache.Values)
            tex.Dispose();
        _previewTextureCache.Clear();

        foreach (var tex in _sampleTextureCache.Values)
            tex.Dispose();
        _sampleTextureCache.Clear();
    }

    /// <summary>Creates a Texture2D directly from raw pixel data, avoiding PNG encode/decode.
    /// RGBA data is premultiplied to match MonoGame's expected format for alpha blending.</summary>
    private Texture2D CreateTextureFromPixels(PreviewPage page)
    {
        var tex = new Texture2D(_graphicsDevice, page.Width, page.Height);
        if (page.IsRgba)
        {
            // Premultiply alpha — MonoGame's default BlendState.AlphaBlend expects
            // premultiplied textures. Without this, semi-transparent pixels (shadows,
            // anti-aliased edges) render incorrectly.
            var data = new byte[page.PixelData.Length];
            for (int i = 0; i < data.Length; i += 4)
            {
                var a = page.PixelData[i + 3];
                if (a == 255)
                {
                    data[i] = page.PixelData[i];
                    data[i + 1] = page.PixelData[i + 1];
                    data[i + 2] = page.PixelData[i + 2];
                    data[i + 3] = 255;
                }
                else if (a == 0)
                {
                    // Leave as zero (transparent black)
                }
                else
                {
                    data[i] = (byte)(page.PixelData[i] * a / 255);
                    data[i + 1] = (byte)(page.PixelData[i + 1] * a / 255);
                    data[i + 2] = (byte)(page.PixelData[i + 2] * a / 255);
                    data[i + 3] = a;
                }
            }
            tex.SetData(data);
        }
        else
        {
            // Grayscale: expand 1-byte-per-pixel to RGBA for the GPU
            var rgba = new byte[page.Width * page.Height * 4];
            for (int i = 0; i < page.PixelData.Length; i++)
            {
                var v = page.PixelData[i];
                var j = i * 4;
                rgba[j] = v;
                rgba[j + 1] = v;
                rgba[j + 2] = v;
                rgba[j + 3] = 255;
            }
            tex.SetData(rgba);
        }
        return tex;
    }

    /// <summary>Increases zoom by 25%, clamped to the active tab's slider range.</summary>
    public void ZoomIn()
    {
        var slider = _activeTab == ActiveTab.SampleText ? _sampleZoomSlider : _zoomSlider;
        if (slider != null)
            slider.Value = Math.Clamp(slider.Value + 25, slider.Minimum, slider.Maximum);
    }

    /// <summary>Decreases zoom by 25%, clamped to the active tab's slider range.</summary>
    public void ZoomOut()
    {
        var slider = _activeTab == ActiveTab.SampleText ? _sampleZoomSlider : _zoomSlider;
        if (slider != null)
            slider.Value = Math.Clamp(slider.Value - 25, slider.Minimum, slider.Maximum);
    }

    /// <summary>Updates the UI scale label in the tab bar to reflect the current scale percentage.</summary>
    public void UpdateUiScaleDisplay(float scale)
    {
        if (_uiScaleLabel != null)
            _uiScaleLabel.Text = $"{(int)(scale * 100)}%";
    }

    /// <summary>
    /// Call each frame from KernSmithGame.Update() for mouse zoom/pan input.
    /// </summary>
    public void UpdateInput()
    {
        if (_activeTab != ActiveTab.Preview && _activeTab != ActiveTab.SampleText) return;
        if (!_preview.HasResult) return;

        var mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();

        // Determine which slider to use based on active tab
        var activeSlider = _activeTab == ActiveTab.Preview ? _zoomSlider : _sampleZoomSlider;

        // --- Scroll wheel zoom ---
        var scrollDelta = mouse.ScrollWheelValue - _previousScrollValue;
        _previousScrollValue = mouse.ScrollWheelValue;

        var panelLeft = this.Visual.AbsoluteLeft;
        var panelTop = this.Visual.AbsoluteTop;
        var panelRight = panelLeft + this.Visual.GetAbsoluteWidth();
        var panelBottom = panelTop + this.Visual.GetAbsoluteHeight();

        if (scrollDelta != 0 && activeSlider != null)
        {

            if (mouse.X >= panelLeft && mouse.X <= panelRight &&
                mouse.Y >= panelTop && mouse.Y <= panelBottom)
            {
                var oldZoom = _activeTab == ActiveTab.Preview ? _preview.ZoomLevel : _sampleZoomLevel;

                var step = scrollDelta > 0 ? 25 : -25;
                var newVal = Math.Clamp(activeSlider.Value + step, activeSlider.Minimum, activeSlider.Maximum);
                activeSlider.Value = newVal;

                // Zoom toward cursor: adjust pan offset so the point under the cursor stays fixed
                if (_activeTab == ActiveTab.Preview && _atlasSprite != null && _atlasContentArea != null)
                {
                    var newZoom = _preview.ZoomLevel;
                    var spriteScreenX = _baseSpriteX + _panOffsetX;
                    var spriteScreenY = _baseSpriteY + _panOffsetY;

                    // Mouse position relative to the sprite's top-left within the content area
                    var areaLeft = _atlasContentArea.AbsoluteLeft;
                    var areaTop = _atlasContentArea.AbsoluteTop;
                    var relX = mouse.X - areaLeft - spriteScreenX;
                    var relY = mouse.Y - areaTop - spriteScreenY;

                    // Scale the relative offset by the zoom ratio
                    var ratio = newZoom / oldZoom;
                    _panOffsetX -= relX * (ratio - 1);
                    _panOffsetY -= relY * (ratio - 1);

                    _atlasSprite.X = (float)Math.Round(_baseSpriteX + _panOffsetX);
                    _atlasSprite.Y = (float)Math.Round(_baseSpriteY + _panOffsetY);
                    if (_checkerSprite != null)
                    {
                        _checkerSprite.X = _atlasSprite.X;
                        _checkerSprite.Y = _atlasSprite.Y;
                    }
                }
                else if (_activeTab == ActiveTab.SampleText && _sampleTextContainer != null)
                {
                    var newZoom = _sampleZoomLevel;
                    var containerScreenX = _sampleTextContainer.AbsoluteLeft - panelLeft;
                    var containerScreenY = _sampleTextContainer.AbsoluteTop - panelTop;

                    var relX = mouse.X - panelLeft - containerScreenX;
                    var relY = mouse.Y - panelTop - containerScreenY;

                    var ratio = newZoom / oldZoom;
                    _samplePanOffsetX -= relX * (ratio - 1);
                    _samplePanOffsetY -= relY * (ratio - 1);

                    _sampleTextContainer.X = (float)Math.Round(_baseSampleX + _samplePanOffsetX);
                    _sampleTextContainer.Y = (float)Math.Round(_baseSampleY + _samplePanOffsetY);
                }
            }
        }

        // --- Middle-click pan (atlas preview) ---
        bool mouseInPanel = mouse.X >= panelLeft && mouse.X <= panelRight &&
                            mouse.Y >= panelTop && mouse.Y <= panelBottom;

        if (_activeTab == ActiveTab.Preview)
        {
            if (mouse.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                if (!_isPanning)
                {
                    if (mouseInPanel)
                    {
                        _isPanning = true;
                        _panStartX = mouse.X;
                        _panStartY = mouse.Y;
                    }
                }
                else
                {
                    var dx = mouse.X - _panStartX;
                    var dy = mouse.Y - _panStartY;
                    _panStartX = mouse.X;
                    _panStartY = mouse.Y;
                    _panOffsetX += dx;
                    _panOffsetY += dy;

                    if (_atlasSprite != null)
                    {
                        _atlasSprite.X = (float)Math.Round(_baseSpriteX + _panOffsetX);
                        _atlasSprite.Y = (float)Math.Round(_baseSpriteY + _panOffsetY);
                    }
                    if (_checkerSprite != null)
                    {
                        _checkerSprite.X = (float)Math.Round(_baseSpriteX + _panOffsetX);
                        _checkerSprite.Y = (float)Math.Round(_baseSpriteY + _panOffsetY);
                    }
                }
            }
            else
            {
                _isPanning = false;
            }
        }
        else if (_activeTab == ActiveTab.SampleText)
        {
            if (mouse.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed)
            {
                if (!_isSamplePanning)
                {
                    if (mouseInPanel)
                    {
                        _isSamplePanning = true;
                        _samplePanStartX = mouse.X;
                        _samplePanStartY = mouse.Y;
                    }
                }
                else
                {
                    var dx = mouse.X - _samplePanStartX;
                    var dy = mouse.Y - _samplePanStartY;
                    _samplePanStartX = mouse.X;
                    _samplePanStartY = mouse.Y;
                    _samplePanOffsetX += dx;
                    _samplePanOffsetY += dy;

                    if (_sampleTextContainer != null)
                    {
                        _sampleTextContainer.X = _baseSampleX + _samplePanOffsetX;
                        _sampleTextContainer.Y = _baseSampleY + _samplePanOffsetY;
                    }
                }
            }
            else
            {
                _isSamplePanning = false;
            }
        }
    }
}
