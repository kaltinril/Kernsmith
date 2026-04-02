using global::Gum.DataTypes;
using global::Gum.Forms;
using global::Gum.Forms.Controls;
using global::Gum.Wireframe;
using KernSmith.Ui.Styling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Modal color picker dialog with hex, RGB, HSL, and HSV input modes.
/// All inputs sync bidirectionally. Includes visual HSV picker (SV square + hue bar)
/// and "New" and "Previous" color preview swatches.
/// </summary>
public class ColorPickerDialog
{
    private const int SvSize = 160;
    private const int HueBarWidth = 20;
    private const int HueBarHeight = 160;

    private bool _suppressSync;

    private byte _r, _g, _b;
    private float _currentHue;
    private float _currentSat;
    private float _currentVal;
    private readonly Color _previousColor;
    private readonly GraphicsDevice _graphicsDevice;

    private TextBox _hexBox = null!;
    private TextBox _rBox = null!, _gBox = null!, _bBox = null!;
    private TextBox _hslHBox = null!, _hslSBox = null!, _hslLBox = null!;
    private TextBox _hsvHBox = null!, _hsvSBox = null!, _hsvVBox = null!;
    private ColoredRectangleRuntime _newSwatch = null!;

    private Texture2D? _svTexture;
    private Texture2D? _hueBarTexture;
    private SpriteRuntime? _svSprite;
    private SpriteRuntime? _hueBarSprite;

    // SV crosshair indicator (4 thin rectangles forming a + shape)
    private ColoredRectangleRuntime? _svCrosshairH;
    private ColoredRectangleRuntime? _svCrosshairV;
    private ColoredRectangleRuntime? _svCrosshairHBorder;
    private ColoredRectangleRuntime? _svCrosshairVBorder;

    // Hue bar indicator
    private ColoredRectangleRuntime? _hueIndicator;
    private ColoredRectangleRuntime? _hueIndicatorBorder;

    private bool _draggingSv;
    private bool _draggingHue;

    private ColorPickerDialog(GraphicsDevice graphicsDevice, Color currentColor)
    {
        _graphicsDevice = graphicsDevice;
        _previousColor = currentColor;
        _r = currentColor.R;
        _g = currentColor.G;
        _b = currentColor.B;

        var (h, s, v) = RgbToHsv(_r, _g, _b);
        _currentHue = h;
        _currentSat = s;
        _currentVal = v;
    }

    /// <summary>
    /// Opens the color picker dialog. Calls <paramref name="onColorSelected"/> with the chosen color on OK.
    /// Does nothing on Cancel.
    /// </summary>
    public static void Show(GraphicsDevice graphicsDevice, Color currentColor, Action<Color> onColorSelected)
    {
        var dialog = new ColorPickerDialog(graphicsDevice, currentColor);
        dialog.Build(onColorSelected);
    }

    private void Build(Action<Color> onColorSelected)
    {
        _window = new Window();
        _window.Anchor(global::Gum.Wireframe.Anchor.Center);
        _window.Width = 370;
        _window.Height = 236;
        _window.ResizeMode = ResizeMode.NoResize;
        FrameworkElement.ModalRoot.Children.Add(_window.Visual);

        // Title
        var windowVisual = _window.Visual as global::Gum.Forms.DefaultVisuals.V3.WindowVisual;
        if (windowVisual?.TitleBarInstance != null)
        {
            var titleLabel = new Label();
            titleLabel.Text = "Color Picker";
            titleLabel.X = 8;
            titleLabel.Y = 2;
            windowVisual.TitleBarInstance.AddChild(titleLabel);
        }

        var outerStack = new ContainerRuntime();
        outerStack.WidthUnits = DimensionUnitType.RelativeToParent;
        outerStack.HeightUnits = DimensionUnitType.RelativeToChildren;
        outerStack.Width = -16;
        outerStack.Height = 0;
        outerStack.X = 8;
        outerStack.Y = 32;
        outerStack.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        outerStack.StackSpacing = 4;
        _window.AddChild(outerStack);

        // --- Row 1: New [swatch] Previous [swatch] Hex: [#hexbox] ---
        BuildTopRow(outerStack);

        // --- Row 2: [SV square] [Hue bar] [Right panel: inputs + buttons] ---
        BuildMainRow(outerStack, onColorSelected);

        // Initial sync to populate all fields from the current color
        SyncFromRgb();
    }

    private void BuildTopRow(ContainerRuntime parent)
    {
        var row = new ContainerRuntime();
        row.WidthUnits = DimensionUnitType.RelativeToParent;
        row.Width = 0;
        row.HeightUnits = DimensionUnitType.RelativeToChildren;
        row.Height = 0;
        row.ChildrenLayout = global::Gum.Managers.ChildrenLayout.LeftToRightStack;
        row.StackSpacing = 2;
        parent.Children.Add(row);

        // "New" label + swatch
        var newLabel = new TextRuntime();
        newLabel.Text = "New";
        newLabel.Width = 26;
        newLabel.Color = Theme.TextMuted;
        row.Children.Add(newLabel);

        AddBorderedSwatch(row, 24, 20, out _newSwatch);
        _newSwatch.Color = new Color(_r, _g, _b);

        // Small spacer
        var spacer1 = new ContainerRuntime();
        spacer1.Width = 4;
        spacer1.Height = 1;
        row.Children.Add(spacer1);

        // "Previous" label + swatch
        var prevLabel = new TextRuntime();
        prevLabel.Text = "Prev";
        prevLabel.Width = 28;
        prevLabel.Color = Theme.TextMuted;
        row.Children.Add(prevLabel);

        AddBorderedSwatch(row, 24, 20, out var prevSwatch);
        prevSwatch.Color = _previousColor;

        // Spacer before hex
        var spacer2 = new ContainerRuntime();
        spacer2.Width = 8;
        spacer2.Height = 1;
        row.Children.Add(spacer2);

        // Hex label + text box
        var hexLabel = new TextRuntime();
        hexLabel.Text = "Hex:";
        hexLabel.Width = 28;
        hexLabel.Color = Theme.Text;
        row.Children.Add(hexLabel);

        _hexBox = new TextBox();
        _hexBox.Width = 84;
        _hexBox.Text = $"#{_r:X2}{_g:X2}{_b:X2}";
        _hexBox.TextChanged += OnHexChanged;
        row.Children.Add(_hexBox.Visual);
    }

    private void BuildMainRow(ContainerRuntime parent, Action<Color> onColorSelected)
    {
        var mainRow = new ContainerRuntime();
        mainRow.WidthUnits = DimensionUnitType.RelativeToParent;
        mainRow.Width = 0;
        mainRow.HeightUnits = DimensionUnitType.Absolute;
        mainRow.Height = SvSize;
        mainRow.ChildrenLayout = global::Gum.Managers.ChildrenLayout.LeftToRightStack;
        mainRow.StackSpacing = 4;
        parent.Children.Add(mainRow);

        // --- SV Square ---
        BuildSvSquare(mainRow);

        // --- Hue Bar ---
        BuildHueBar(mainRow);

        // --- Right panel: inputs + buttons ---
        BuildRightPanel(mainRow, onColorSelected);
    }

    private void BuildRightPanel(ContainerRuntime parent, Action<Color> onColorSelected)
    {
        var rightPanel = new ContainerRuntime();
        rightPanel.WidthUnits = DimensionUnitType.RelativeToChildren;
        rightPanel.Width = 0;
        rightPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        rightPanel.Height = 0;
        rightPanel.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        rightPanel.StackSpacing = 4;
        parent.Children.Add(rightPanel);

        // --- Precompute initial values ---
        var (hslH, hslS, hslL) = RgbToHsl(_r, _g, _b);
        var (hsvH, hsvS, hsvV) = RgbToHsv(_r, _g, _b);

        // --- Grid: 3 rows (RGB/HSL/HSV) x 4 columns (label + 3 values) ---
        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rightPanel.Children.Add(grid.Visual);

        // Row 0: RGB
        AddGridLabel(grid, "RGB:", 0, 0);
        _rBox = AddGridTextBox(grid, _r.ToString(), 0, 1);
        _gBox = AddGridTextBox(grid, _g.ToString(), 0, 2);
        _bBox = AddGridTextBox(grid, _b.ToString(), 0, 3);

        // Row 1: HSL
        AddGridLabel(grid, "HSL:", 1, 0);
        _hslHBox = AddGridTextBox(grid, ((int)hslH).ToString(), 1, 1);
        _hslSBox = AddGridTextBox(grid, ((int)hslS).ToString(), 1, 2);
        _hslLBox = AddGridTextBox(grid, ((int)hslL).ToString(), 1, 3);

        // Row 2: HSV
        AddGridLabel(grid, "HSV:", 2, 0);
        _hsvHBox = AddGridTextBox(grid, ((int)hsvH).ToString(), 2, 1);
        _hsvSBox = AddGridTextBox(grid, ((int)hsvS).ToString(), 2, 2);
        _hsvVBox = AddGridTextBox(grid, ((int)hsvV).ToString(), 2, 3);

        // --- OK / Cancel buttons ---
        var buttonRow = new ContainerRuntime();
        buttonRow.WidthUnits = DimensionUnitType.RelativeToChildren;
        buttonRow.Width = 0;
        buttonRow.HeightUnits = DimensionUnitType.RelativeToChildren;
        buttonRow.Height = 0;
        buttonRow.ChildrenLayout = global::Gum.Managers.ChildrenLayout.LeftToRightStack;
        buttonRow.StackSpacing = 4;
        rightPanel.Children.Add(buttonRow);

        var okBtn = new Button();
        okBtn.Text = "OK";
        okBtn.Width = 70;
        okBtn.Click += (_, _) =>
        {
            onColorSelected(new Color(_r, _g, _b));
            CloseDialog();
        };
        buttonRow.Children.Add(okBtn.Visual);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.Width = 70;
        cancelBtn.Click += (_, _) => CloseDialog();
        buttonRow.Children.Add(cancelBtn.Visual);

        // --- Wire up TextChanged handlers ---
        _rBox.TextChanged += (_, _) => OnRgbChanged();
        _gBox.TextChanged += (_, _) => OnRgbChanged();
        _bBox.TextChanged += (_, _) => OnRgbChanged();

        _hslHBox.TextChanged += (_, _) => OnHslChanged();
        _hslSBox.TextChanged += (_, _) => OnHslChanged();
        _hslLBox.TextChanged += (_, _) => OnHslChanged();

        _hsvHBox.TextChanged += (_, _) => OnHsvChanged();
        _hsvSBox.TextChanged += (_, _) => OnHsvChanged();
        _hsvVBox.TextChanged += (_, _) => OnHsvChanged();
    }

    private Window? _window;

    private void CloseDialog()
    {
        DisposeTextures();
        _window?.RemoveFromRoot();
    }

    private static void AddGridLabel(Grid grid, string text, int row, int column)
    {
        var lbl = new Label();
        lbl.Text = text;
        grid.AddChild(lbl, row, column);
    }

    private static TextBox AddGridTextBox(Grid grid, string text, int row, int column)
    {
        var box = new TextBox();
        box.Width = 40;
        box.MaxLettersToShow = 3;
        box.MaxLength = 3;
        box.Placeholder = "";
        box.Text = text;
        grid.AddChild(box, row, column);
        return box;
    }

    private static void AddBorderedSwatch(ContainerRuntime parent, int width, int height, out ColoredRectangleRuntime innerSwatch)
    {
        var container = new ContainerRuntime();
        container.Width = width;
        container.Height = height;
        parent.Children.Add(container);

        var border = new ColoredRectangleRuntime();
        border.Width = 0;
        border.WidthUnits = DimensionUnitType.RelativeToParent;
        border.Height = 0;
        border.HeightUnits = DimensionUnitType.RelativeToParent;
        border.Color = Theme.PanelBorder;
        container.Children.Add(border);

        innerSwatch = new ColoredRectangleRuntime();
        innerSwatch.X = 1;
        innerSwatch.Y = 1;
        innerSwatch.Width = -2;
        innerSwatch.WidthUnits = DimensionUnitType.RelativeToParent;
        innerSwatch.Height = -2;
        innerSwatch.HeightUnits = DimensionUnitType.RelativeToParent;
        container.Children.Add(innerSwatch);
    }

    private void BuildSvSquare(ContainerRuntime parent)
    {
        var svContainer = new ContainerRuntime();
        svContainer.Width = SvSize;
        svContainer.Height = SvSize;
        svContainer.ClipsChildren = true;
        parent.Children.Add(svContainer);

        // Border
        var svBorder = new ColoredRectangleRuntime();
        svBorder.Width = 0;
        svBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        svBorder.Height = 0;
        svBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        svBorder.Color = Theme.PanelBorder;
        svContainer.Children.Add(svBorder);

        // SV texture sprite
        _svSprite = new SpriteRuntime();
        _svSprite.X = 1;
        _svSprite.Y = 1;
        _svSprite.WidthUnits = DimensionUnitType.Absolute;
        _svSprite.HeightUnits = DimensionUnitType.Absolute;
        _svSprite.Width = SvSize - 2;
        _svSprite.Height = SvSize - 2;
        _svSprite.TextureAddress = global::Gum.Managers.TextureAddress.EntireTexture;
        svContainer.Children.Add(_svSprite);

        RegenerateSvTexture();

        // Crosshair indicator: dark border lines behind white lines
        _svCrosshairHBorder = new ColoredRectangleRuntime();
        _svCrosshairHBorder.Width = 9;
        _svCrosshairHBorder.Height = 3;
        _svCrosshairHBorder.Color = new Color(0, 0, 0);
        svContainer.Children.Add(_svCrosshairHBorder);

        _svCrosshairVBorder = new ColoredRectangleRuntime();
        _svCrosshairVBorder.Width = 3;
        _svCrosshairVBorder.Height = 9;
        _svCrosshairVBorder.Color = new Color(0, 0, 0);
        svContainer.Children.Add(_svCrosshairVBorder);

        _svCrosshairH = new ColoredRectangleRuntime();
        _svCrosshairH.Width = 7;
        _svCrosshairH.Height = 1;
        _svCrosshairH.Color = Color.White;
        svContainer.Children.Add(_svCrosshairH);

        _svCrosshairV = new ColoredRectangleRuntime();
        _svCrosshairV.Width = 1;
        _svCrosshairV.Height = 7;
        _svCrosshairV.Color = Color.White;
        svContainer.Children.Add(_svCrosshairV);

        UpdateSvIndicator();

        // Mouse interaction
        if (svContainer is InteractiveGue svInteractive)
        {
            svInteractive.Push += OnSvPush;
            svInteractive.RollOver += OnSvRollOver;
            svInteractive.LosePush += OnSvLosePush;
        }
    }

    private void BuildHueBar(ContainerRuntime parent)
    {
        var hueContainer = new ContainerRuntime();
        hueContainer.Width = HueBarWidth;
        hueContainer.Height = HueBarHeight;
        hueContainer.ClipsChildren = true;
        parent.Children.Add(hueContainer);

        // Border
        var hueBorder = new ColoredRectangleRuntime();
        hueBorder.Width = 0;
        hueBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        hueBorder.Height = 0;
        hueBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        hueBorder.Color = Theme.PanelBorder;
        hueContainer.Children.Add(hueBorder);

        // Hue bar texture sprite
        _hueBarSprite = new SpriteRuntime();
        _hueBarSprite.X = 1;
        _hueBarSprite.Y = 1;
        _hueBarSprite.WidthUnits = DimensionUnitType.Absolute;
        _hueBarSprite.HeightUnits = DimensionUnitType.Absolute;
        _hueBarSprite.Width = HueBarWidth - 2;
        _hueBarSprite.Height = HueBarHeight - 2;
        _hueBarSprite.TextureAddress = global::Gum.Managers.TextureAddress.EntireTexture;
        hueContainer.Children.Add(_hueBarSprite);

        GenerateHueBarTexture();

        // Hue indicator: horizontal line with dark border
        _hueIndicatorBorder = new ColoredRectangleRuntime();
        _hueIndicatorBorder.Width = HueBarWidth;
        _hueIndicatorBorder.Height = 5;
        _hueIndicatorBorder.Color = new Color(0, 0, 0);
        hueContainer.Children.Add(_hueIndicatorBorder);

        _hueIndicator = new ColoredRectangleRuntime();
        _hueIndicator.Width = HueBarWidth - 2;
        _hueIndicator.Height = 3;
        _hueIndicator.Color = Color.White;
        hueContainer.Children.Add(_hueIndicator);

        UpdateHueIndicator();

        // Mouse interaction
        if (hueContainer is InteractiveGue hueInteractive)
        {
            hueInteractive.Push += OnHuePush;
            hueInteractive.RollOver += OnHueRollOver;
            hueInteractive.LosePush += OnHueLosePush;
        }
    }

    // --- Mouse event handlers ---

    private void OnSvPush(object? sender, EventArgs e)
    {
        _draggingSv = true;
        PickSvColorFromMouse();
    }

    private void OnSvRollOver(object? sender, EventArgs e)
    {
        if (!_draggingSv) return;
        PickSvColorFromMouse();
    }

    private void OnSvLosePush(object? sender, EventArgs e)
    {
        _draggingSv = false;
    }

    private void OnHuePush(object? sender, EventArgs e)
    {
        _draggingHue = true;
        PickHueFromMouse();
    }

    private void OnHueRollOver(object? sender, EventArgs e)
    {
        if (!_draggingHue) return;
        PickHueFromMouse();
    }

    private void OnHueLosePush(object? sender, EventArgs e)
    {
        _draggingHue = false;
    }

    private void PickSvColorFromMouse()
    {
        if (_svSprite == null) return;

        var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
        float relX = mouseState.X - _svSprite.AbsoluteLeft;
        float relY = mouseState.Y - _svSprite.AbsoluteTop;

        float spriteWidth = SvSize - 2;
        float spriteHeight = SvSize - 2;

        relX = Math.Clamp(relX, 0, spriteWidth - 1);
        relY = Math.Clamp(relY, 0, spriteHeight - 1);

        _currentSat = relX / (spriteWidth - 1) * 100f;
        _currentVal = (1f - relY / (spriteHeight - 1)) * 100f;

        var (r, g, b) = HsvToRgb(_currentHue, _currentSat, _currentVal);
        _r = r; _g = g; _b = b;

        UpdateSvIndicator();
        SyncFromRgb(syncHsv: false);

        _suppressSync = true;
        try
        {
            _hsvHBox.Text = ((int)_currentHue).ToString();
            _hsvSBox.Text = ((int)_currentSat).ToString();
            _hsvVBox.Text = ((int)_currentVal).ToString();
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private void PickHueFromMouse()
    {
        if (_hueBarSprite == null) return;

        var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
        float relY = mouseState.Y - _hueBarSprite.AbsoluteTop;

        float spriteHeight = HueBarHeight - 2;

        relY = Math.Clamp(relY, 0, spriteHeight - 1);

        _currentHue = relY / (spriteHeight - 1) * 360f;

        RegenerateSvTexture();
        UpdateHueIndicator();

        var (r, g, b) = HsvToRgb(_currentHue, _currentSat, _currentVal);
        _r = r; _g = g; _b = b;

        SyncFromRgb(syncHsv: false);

        _suppressSync = true;
        try
        {
            _hsvHBox.Text = ((int)_currentHue).ToString();
            _hsvSBox.Text = ((int)_currentSat).ToString();
            _hsvVBox.Text = ((int)_currentVal).ToString();
        }
        finally
        {
            _suppressSync = false;
        }
    }

    // --- Texture generation ---

    private void RegenerateSvTexture()
    {
        int w = SvSize - 2;
        int h = SvSize - 2;
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float val = (1f - (float)y / (h - 1)) * 100f;
            for (int x = 0; x < w; x++)
            {
                float sat = (float)x / (w - 1) * 100f;
                var (r, g, b) = HsvToRgb(_currentHue, sat, val);
                pixels[y * w + x] = new Color(r, g, b);
            }
        }

        if (_svTexture == null)
            _svTexture = new Texture2D(_graphicsDevice, w, h);

        _svTexture.SetData(pixels);

        if (_svSprite != null)
            _svSprite.Texture = _svTexture;
    }

    private void GenerateHueBarTexture()
    {
        int w = HueBarWidth - 2;
        int h = HueBarHeight - 2;
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float hue = (float)y / (h - 1) * 360f;
            var (r, g, b) = HsvToRgb(hue, 100, 100);
            var color = new Color(r, g, b);
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = color;
        }

        _hueBarTexture = new Texture2D(_graphicsDevice, w, h);
        _hueBarTexture.SetData(pixels);

        if (_hueBarSprite != null)
            _hueBarSprite.Texture = _hueBarTexture;
    }

    // --- Indicator positioning ---

    private void UpdateSvIndicator()
    {
        if (_svCrosshairH == null || _svSprite == null) return;

        float spriteWidth = SvSize - 2;
        float spriteHeight = SvSize - 2;

        float cx = 1 + _currentSat / 100f * (spriteWidth - 1);
        float cy = 1 + (1f - _currentVal / 100f) * (spriteHeight - 1);

        _svCrosshairHBorder!.X = cx - 4;
        _svCrosshairHBorder.Y = cy - 1;
        _svCrosshairVBorder!.X = cx - 1;
        _svCrosshairVBorder.Y = cy - 4;

        _svCrosshairH.X = cx - 3;
        _svCrosshairH.Y = cy;
        _svCrosshairV!.X = cx;
        _svCrosshairV.Y = cy - 3;
    }

    private void UpdateHueIndicator()
    {
        if (_hueIndicator == null) return;

        float spriteHeight = HueBarHeight - 2;
        float iy = 1 + _currentHue / 360f * (spriteHeight - 1);

        _hueIndicatorBorder!.X = 0;
        _hueIndicatorBorder.Y = iy - 2;

        _hueIndicator.X = 1;
        _hueIndicator.Y = iy - 1;
    }

    private void DisposeTextures()
    {
        if (_svSprite != null)
            _svSprite.Texture = null;
        if (_hueBarSprite != null)
            _hueBarSprite.Texture = null;

        _svTexture?.Dispose();
        _svTexture = null;
        _hueBarTexture?.Dispose();
        _hueBarTexture = null;
    }

    // --- Text input change handlers ---

    private void OnHexChanged(object? sender, EventArgs e)
    {
        if (_suppressSync) return;
        var hex = _hexBox.Text?.Trim() ?? "";
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 6 &&
            byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            _r = r; _g = g; _b = b;
            SyncFromRgb(syncHex: false);
        }
    }

    private void OnRgbChanged()
    {
        if (_suppressSync) return;
        if (byte.TryParse(_rBox.Text, out var r) &&
            byte.TryParse(_gBox.Text, out var g) &&
            byte.TryParse(_bBox.Text, out var b))
        {
            _r = r; _g = g; _b = b;
            SyncFromRgb(syncRgb: false);
        }
    }

    private void OnHslChanged()
    {
        if (_suppressSync) return;
        if (int.TryParse(_hslHBox.Text, out var hv) &&
            int.TryParse(_hslSBox.Text, out var sv) &&
            int.TryParse(_hslLBox.Text, out var lv) &&
            hv is >= 0 and <= 360 && sv is >= 0 and <= 100 && lv is >= 0 and <= 100)
        {
            var (r, g, b) = HslToRgb(hv, sv, lv);
            _r = r; _g = g; _b = b;
            SyncFromRgb(syncHsl: false);
        }
    }

    private void OnHsvChanged()
    {
        if (_suppressSync) return;
        if (int.TryParse(_hsvHBox.Text, out var hv) &&
            int.TryParse(_hsvSBox.Text, out var sv) &&
            int.TryParse(_hsvVBox.Text, out var vv) &&
            hv is >= 0 and <= 360 && sv is >= 0 and <= 100 && vv is >= 0 and <= 100)
        {
            var (r, g, b) = HsvToRgb(hv, sv, vv);
            _r = r; _g = g; _b = b;
            SyncFromRgb(syncHsv: false);
        }
    }

    /// <summary>
    /// Syncs all input fields and preview swatch from the current _r, _g, _b values.
    /// Pass false for any mode that triggered the change to avoid redundant updates.
    /// </summary>
    private void SyncFromRgb(bool syncHex = true, bool syncRgb = true, bool syncHsl = true, bool syncHsv = true)
    {
        _suppressSync = true;
        try
        {
            _newSwatch.Color = new Color(_r, _g, _b);

            if (syncHex)
                _hexBox.Text = $"#{_r:X2}{_g:X2}{_b:X2}";

            if (syncRgb)
            {
                _rBox.Text = _r.ToString();
                _gBox.Text = _g.ToString();
                _bBox.Text = _b.ToString();
            }

            if (syncHsl)
            {
                var (h, s, l) = RgbToHsl(_r, _g, _b);
                _hslHBox.Text = ((int)h).ToString();
                _hslSBox.Text = ((int)s).ToString();
                _hslLBox.Text = ((int)l).ToString();
            }

            if (syncHsv)
            {
                var (hv, sv, vv) = RgbToHsv(_r, _g, _b);
                _currentHue = hv;
                _currentSat = sv;
                _currentVal = vv;
                _hsvHBox.Text = ((int)hv).ToString();
                _hsvSBox.Text = ((int)sv).ToString();
                _hsvVBox.Text = ((int)vv).ToString();

                RegenerateSvTexture();
                UpdateSvIndicator();
                UpdateHueIndicator();
            }
        }
        finally
        {
            _suppressSync = false;
        }
    }

    // --- Color conversion helpers ---

    /// <summary>Converts RGB (0-255) to HSL. Returns H (0-360), S (0-100), L (0-100).</summary>
    private static (float H, float S, float L) RgbToHsl(byte r, byte g, byte b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;
        float l = (max + min) / 2f;

        if (delta == 0)
            return (0, 0, l * 100f);

        float s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);
        float h;

        if (max == rf)
            h = ((gf - bf) / delta) % 6f;
        else if (max == gf)
            h = (bf - rf) / delta + 2f;
        else
            h = (rf - gf) / delta + 4f;

        h *= 60f;
        if (h < 0) h += 360f;

        return (h, s * 100f, l * 100f);
    }

    /// <summary>Converts HSL to RGB. H (0-360), S (0-100), L (0-100) -> RGB bytes.</summary>
    private static (byte R, byte G, byte B) HslToRgb(float h, float s, float l)
    {
        s /= 100f;
        l /= 100f;

        if (s == 0)
        {
            var v = (byte)MathF.Round(l * 255f);
            return (v, v, v);
        }

        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float hNorm = h / 360f;

        float rf = HueToRgb(p, q, hNorm + 1f / 3f);
        float gf = HueToRgb(p, q, hNorm);
        float bf = HueToRgb(p, q, hNorm - 1f / 3f);

        return (
            (byte)MathF.Round(rf * 255f),
            (byte)MathF.Round(gf * 255f),
            (byte)MathF.Round(bf * 255f));
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }

    /// <summary>Converts RGB (0-255) to HSV. Returns H (0-360), S (0-100), V (0-100).</summary>
    private static (float H, float S, float V) RgbToHsv(byte r, byte g, byte b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;

        float h;
        if (delta == 0)
            h = 0;
        else if (max == rf)
            h = 60f * (((gf - bf) / delta) % 6f);
        else if (max == gf)
            h = 60f * ((bf - rf) / delta + 2f);
        else
            h = 60f * ((rf - gf) / delta + 4f);

        if (h < 0) h += 360f;

        float s = max == 0 ? 0 : delta / max;

        return (h, s * 100f, max * 100f);
    }

    /// <summary>Converts HSV to RGB. H (0-360), S (0-100), V (0-100) -> RGB bytes.</summary>
    private static (byte R, byte G, byte B) HsvToRgb(float h, float s, float v)
    {
        s /= 100f;
        v /= 100f;

        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;

        float rf, gf, bf;
        if (h < 60) { rf = c; gf = x; bf = 0; }
        else if (h < 120) { rf = x; gf = c; bf = 0; }
        else if (h < 180) { rf = 0; gf = c; bf = x; }
        else if (h < 240) { rf = 0; gf = x; bf = c; }
        else if (h < 300) { rf = x; gf = 0; bf = c; }
        else { rf = c; gf = 0; bf = x; }

        return (
            (byte)MathF.Round((rf + m) * 255f),
            (byte)MathF.Round((gf + m) * 255f),
            (byte)MathF.Round((bf + m) * 255f));
    }
}
