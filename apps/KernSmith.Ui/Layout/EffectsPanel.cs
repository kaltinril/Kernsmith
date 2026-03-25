using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Right-side panel containing all glyph effect controls (font style, outline, shadow, gradient,
/// channels, SDF, color font, variable font axes, fallback character).
/// </summary>
/// <remarks>
/// TODO: Slider controls use fixed Width=100 instead of RelativeToParent inside Grid cells.
/// Using RelativeToParent (fill cell) looks much better but causes severe lag because Gum's Grid
/// triggers RefreshLayout on every slider value change. Waiting on a Gum performance fix to switch
/// sliders to: slider.Visual.WidthUnits = DimensionUnitType.RelativeToParent; slider.Visual.Width = 0;
/// </remarks>
public class EffectsPanel : Panel
{
    private readonly EffectsViewModel _effects;
    private readonly GraphicsDevice _graphicsDevice;

    public EffectsPanel(EffectsViewModel effects, GraphicsDevice graphicsDevice)
    {
        _effects = effects;
        _graphicsDevice = graphicsDevice;
        BuildContent();
    }

    private void BuildContent()
    {
        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Dock(Gum.Wireframe.Dock.Fill);
        this.AddChild(scrollViewer);

        // Inner container with padding from panel edges
        var inner = new ContainerRuntime();
        inner.WidthUnits = DimensionUnitType.RelativeToParent;
        inner.HeightUnits = DimensionUnitType.RelativeToChildren;
        inner.Width = -16; // 8px padding each side
        inner.Height = 0;
        inner.X = 8;
        inner.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.Y = 4;
        inner.StackSpacing = 6;
        scrollViewer.InnerPanel.Children.Add(inner);

        var stack = inner;

        // --- FONT STYLE section (always active) ---
        BuildFontStyleSection(stack);

        AddDivider(stack);

        // --- OUTLINE section ---
        AddCollapsibleSection(stack, "OUTLINE", BuildOutlineContent,
            enableChanged: enabled => _effects.OutlineEnabled = enabled,
            tooltip: "Add an outline border around each glyph");

        AddDivider(stack);

        // --- SHADOW section ---
        AddCollapsibleSection(stack, "SHADOW", BuildShadowContent,
            enableChanged: enabled => _effects.ShadowEnabled = enabled,
            tooltip: "Add a drop shadow behind each glyph");

        AddDivider(stack);

        // --- GRADIENT section ---
        AddCollapsibleSection(stack, "GRADIENT", BuildGradientContent,
            enableChanged: enabled => _effects.GradientEnabled = enabled,
            tooltip: "Apply a color gradient across each glyph");

        AddDivider(stack);

        // --- CHANNELS section ---
        AddCollapsibleSection(stack, "CHANNELS", BuildChannelsContent,
            enableChanged: _ => { },
            tooltip: "Pack glyph data into specific RGBA channels");

        AddDivider(stack);

        // --- ADVANCED section (SDF, Color Font, Variable Font) ---
        BuildAdvancedSection(stack);

        // --- FALLBACK CHARACTER section ---
        AddDivider(stack);
        BuildFallbackSection(stack);

        // --- VARIABLE FONT section (dynamic, appears when axes are present) ---
        AddDivider(stack);
        BuildVariableFontSection(stack);

    }

    private void BuildFontStyleSection(Gum.Wireframe.GraphicalUiElement stack)
    {
        AddSectionHeader(stack, "FONT STYLE");

        // Horizontal row: Bold/Italic on left, Anti-Alias/Hinting on right
        var styleRow = new ContainerRuntime();
        styleRow.WidthUnits = DimensionUnitType.RelativeToParent;
        styleRow.Width = 0;
        styleRow.HeightUnits = DimensionUnitType.RelativeToChildren;
        styleRow.Height = 0;
        styleRow.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;
        styleRow.StackSpacing = 4;
        stack.Children.Add(styleRow);

        // Left column: Bold, Italic
        var leftCol = new ContainerRuntime();
        leftCol.WidthUnits = DimensionUnitType.Ratio;
        leftCol.Width = 1;
        leftCol.HeightUnits = DimensionUnitType.RelativeToChildren;
        leftCol.Height = 0;
        leftCol.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        leftCol.StackSpacing = 4;
        styleRow.Children.Add(leftCol);

        var boldCheck = new CheckBox();
        boldCheck.Text = "Bold";
        boldCheck.Checked += (_, _) => _effects.Bold = true;
        boldCheck.Unchecked += (_, _) => _effects.Bold = false;
        leftCol.Children.Add(boldCheck.Visual);
        TooltipService.SetTooltip(boldCheck, "Bold variant, or synthetic if unavailable");

        var italicCheck = new CheckBox();
        italicCheck.Text = "Italic";
        italicCheck.Checked += (_, _) => _effects.Italic = true;
        italicCheck.Unchecked += (_, _) => _effects.Italic = false;
        leftCol.Children.Add(italicCheck.Visual);
        TooltipService.SetTooltip(italicCheck, "Italic variant, or synthetic if unavailable");

        // Right column: Anti-Alias, Hinting
        var rightCol = new ContainerRuntime();
        rightCol.WidthUnits = DimensionUnitType.Ratio;
        rightCol.Width = 1;
        rightCol.HeightUnits = DimensionUnitType.RelativeToChildren;
        rightCol.Height = 0;
        rightCol.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        rightCol.StackSpacing = 4;
        styleRow.Children.Add(rightCol);

        var aaCheck = new CheckBox();
        aaCheck.Text = "Anti-Alias";
        aaCheck.IsChecked = true;
        aaCheck.Checked += (_, _) => _effects.AntiAlias = true;
        aaCheck.Unchecked += (_, _) => _effects.AntiAlias = false;
        rightCol.Children.Add(aaCheck.Visual);
        TooltipService.SetTooltip(aaCheck, "Smooth glyph edges with anti-aliasing");

        var hintCheck = new CheckBox();
        hintCheck.Text = "Hinting";
        hintCheck.IsChecked = true;
        hintCheck.Checked += (_, _) => _effects.Hinting = true;
        hintCheck.Unchecked += (_, _) => _effects.Hinting = false;
        rightCol.Children.Add(hintCheck.Visual);
        TooltipService.SetTooltip(hintCheck, "Font hinting for sharper small sizes");

        // Super sampling
        var ssLabel = new Label();
        ssLabel.Text = "Super Sample:";
        stack.Children.Add(ssLabel.Visual);
        TooltipService.SetTooltip(ssLabel, "Supersample then downscale for smoother glyphs");

        var ssGroup = new StackPanel();
        ssGroup.Orientation = Orientation.Horizontal;
        ssGroup.Spacing = 4;
        stack.Children.Add(ssGroup.Visual);

        foreach (var level in new[] { 1, 2, 4 })
        {
            var rb = new RadioButton();
            rb.Text = $"{level}x";
            rb.Width = 50;
            if (level == 1) rb.IsChecked = true;
            var capturedLevel = level;
            rb.Checked += (_, _) => _effects.SuperSampleLevel = capturedLevel;
            ssGroup.AddChild(rb);
        }
    }

    private void BuildOutlineContent(Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        var widthGrid = new Grid();
        widthGrid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        widthGrid.Visual.Width = 0;
        widthGrid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        widthGrid.Visual.Height = 0;
        widthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        widthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        widthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        widthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentPanel.Children.Add(widthGrid.Visual);

        var widthLabel = new Label();
        widthLabel.Text = "Width:";
        widthGrid.AddChild(widthLabel, row: 0, column: 0);

        var widthSlider = new Slider();
        widthSlider.Minimum = 1;
        widthSlider.Maximum = 10;
        widthSlider.Value = 1;
        widthSlider.Width = 100;
        widthSlider.TicksFrequency = 1;
        widthSlider.IsSnapToTickEnabled = true;
        widthGrid.AddChild(widthSlider, row: 0, column: 1);

        var widthValue = new Label();
        widthValue.Text = "1";
        widthGrid.AddChild(widthValue, row: 0, column: 2);

        widthSlider.ValueChanged += (_, _) =>
        {
            var val = (int)widthSlider.Value;
            widthValue.Text = val.ToString();
            _effects.OutlineWidth = val;
        };

        // Outline color row
        AddColorRow(_graphicsDevice, contentPanel, "Color:",
            _effects.OutlineColor, hex => _effects.OutlineColor = hex);
    }

    private void BuildShadowContent(Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        AddSliderRow(contentPanel, "Offset X:", -10, 10, 2,
            val => _effects.ShadowOffsetX = val);
        AddSliderRow(contentPanel, "Offset Y:", -10, 10, 2,
            val => _effects.ShadowOffsetY = val);
        AddSliderRow(contentPanel, "Blur:", 0, 10, 0,
            val => _effects.ShadowBlur = val);

        // Shadow color row
        AddColorRow(_graphicsDevice, contentPanel, "Color:",
            _effects.ShadowColor, hex => _effects.ShadowColor = hex);

        // Shadow opacity slider
        AddSliderRow(contentPanel, "Opacity:", 0, 100, 100,
            val => _effects.ShadowOpacity = val);
    }

    private void BuildGradientContent(Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        AddColorRow(_graphicsDevice, contentPanel, "Start:",
            _effects.GradientStartColor, hex => _effects.GradientStartColor = hex);

        AddColorRow(_graphicsDevice, contentPanel, "End:",
            _effects.GradientEndColor, hex => _effects.GradientEndColor = hex);

        AddSliderRow(contentPanel, "Angle:", 0, 360, 90,
            val => _effects.GradientAngle = val);
    }

    private void BuildChannelsContent(Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        var packingCheck = new CheckBox();
        packingCheck.Text = "Channel Packing";
        packingCheck.Width = 180;
        packingCheck.Checked += (_, _) => _effects.ChannelPackingEnabled = true;
        packingCheck.Unchecked += (_, _) => _effects.ChannelPackingEnabled = false;
        contentPanel.Children.Add(packingCheck.Visual);
    }

    private void BuildAdvancedSection(Gum.Wireframe.GraphicalUiElement stack)
    {
        AddSectionHeader(stack, "ADVANCED");

        var sdfCheck = new CheckBox();
        sdfCheck.Text = "SDF";
        sdfCheck.Width = 220;
        sdfCheck.Checked += (_, _) => _effects.SdfEnabled = true;
        sdfCheck.Unchecked += (_, _) => _effects.SdfEnabled = false;
        stack.Children.Add(sdfCheck.Visual);
        TooltipService.SetTooltip(sdfCheck, "Scalable SDF rendering; incompatible with effects");

        // SDF incompatibility warning (covers super-sample, outline, shadow, gradient)
        var sdfWarning = new TextRuntime();
        sdfWarning.Text = "";
        sdfWarning.Color = Theme.Warning;
        sdfWarning.Visible = false;
        stack.Children.Add(sdfWarning);

        var colorCheck = new CheckBox();
        colorCheck.Text = "Color Font";
        colorCheck.Width = 220;
        colorCheck.IsEnabled = _effects.HasColorGlyphs;
        colorCheck.Checked += (_, _) => _effects.ColorFontEnabled = true;
        colorCheck.Unchecked += (_, _) => _effects.ColorFontEnabled = false;
        stack.Children.Add(colorCheck.Visual);
        TooltipService.SetTooltip(colorCheck, "Render color glyphs (emoji) — requires a font with color tables");

        // Color font + Gradient mutual exclusion feedback
        var colorGradientWarning = new TextRuntime();
        colorGradientWarning.Text = "";
        colorGradientWarning.Color = Theme.Warning;
        colorGradientWarning.Visible = false;
        stack.Children.Add(colorGradientWarning);

        // Track whether we are programmatically updating checkboxes to avoid recursive loops
        bool updatingSdfCheck = false;
        bool updatingColorCheck = false;

        // Wire up validation, SDF auto-disable, and color/gradient mutual exclusion
        _effects.PropertyChanged += (_, e) =>
        {
            // --- Issue #9: SDF auto-disable when incompatible options are active ---
            if (e.PropertyName is nameof(EffectsViewModel.SdfEnabled)
                or nameof(EffectsViewModel.SuperSampleLevel)
                or nameof(EffectsViewModel.OutlineEnabled)
                or nameof(EffectsViewModel.ShadowEnabled)
                or nameof(EffectsViewModel.GradientEnabled))
            {
                if (_effects.SdfEnabled)
                {
                    var incompatible = new List<string>();
                    if (_effects.SuperSampleLevel > 1) incompatible.Add("super sampling");
                    if (_effects.OutlineEnabled) incompatible.Add("outline");
                    if (_effects.ShadowEnabled) incompatible.Add("shadow");
                    if (_effects.GradientEnabled) incompatible.Add("gradient");

                    if (incompatible.Count > 0)
                    {
                        // Auto-disable SDF
                        _effects.SdfEnabled = false;
                        if (!updatingSdfCheck)
                        {
                            updatingSdfCheck = true;
                            sdfCheck.IsChecked = false;
                            updatingSdfCheck = false;
                        }
                        sdfWarning.Text = $"SDF disabled \u2014 not compatible with {string.Join(", ", incompatible)}";
                        sdfWarning.Visible = true;
                    }
                    else
                    {
                        sdfWarning.Visible = false;
                    }
                }
                else
                {
                    sdfWarning.Visible = false;
                }
            }

            // --- Issue #10: Color font + Gradient mutual exclusion ---
            if (e.PropertyName == nameof(EffectsViewModel.ColorFontEnabled) && _effects.ColorFontEnabled && _effects.GradientEnabled)
            {
                _effects.GradientEnabled = false;
                colorGradientWarning.Text = "Gradient disabled \u2014 mutually exclusive with color font";
                colorGradientWarning.Visible = true;
            }
            else if (e.PropertyName == nameof(EffectsViewModel.GradientEnabled) && _effects.GradientEnabled && _effects.ColorFontEnabled)
            {
                _effects.ColorFontEnabled = false;
                if (!updatingColorCheck)
                {
                    updatingColorCheck = true;
                    colorCheck.IsChecked = false;
                    updatingColorCheck = false;
                }
                colorGradientWarning.Text = "Color font disabled \u2014 mutually exclusive with gradient";
                colorGradientWarning.Visible = true;
            }
            else if (e.PropertyName is nameof(EffectsViewModel.ColorFontEnabled) or nameof(EffectsViewModel.GradientEnabled))
            {
                colorGradientWarning.Visible = false;
            }

            // Enable/disable color font checkbox based on whether font has color tables
            if (e.PropertyName is nameof(EffectsViewModel.HasColorGlyphs))
                colorCheck.IsEnabled = _effects.HasColorGlyphs;
        };
    }

    private void BuildFallbackSection(Gum.Wireframe.GraphicalUiElement stack)
    {
        AddSectionHeader(stack, "FALLBACK CHARACTER");

        var fallbackRow = new StackPanel();
        fallbackRow.Orientation = Orientation.Horizontal;
        fallbackRow.Spacing = 4;
        stack.Children.Add(fallbackRow.Visual);

        var fallbackLabel = new Label();
        fallbackLabel.Text = "Char:";
        fallbackLabel.Width = 70;
        fallbackRow.AddChild(fallbackLabel);
        TooltipService.SetTooltip(fallbackLabel, "Replacement for missing glyphs");

        var fallbackTextBox = new TextBox();
        fallbackTextBox.Width = 60;
        fallbackTextBox.Text = _effects.FallbackCharacter;
        fallbackTextBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(fallbackTextBox.Text))
                _effects.FallbackCharacter = fallbackTextBox.Text;
        };
        fallbackRow.AddChild(fallbackTextBox);
    }

    private void BuildVariableFontSection(Gum.Wireframe.GraphicalUiElement stack)
    {
        var varFontHeader = new Label();
        varFontHeader.Text = "VARIABLE FONT";
        varFontHeader.IsVisible = false;
        stack.Children.Add(varFontHeader.Visual);

        var varFontContainer = new StackPanel();
        varFontContainer.Spacing = 4;
        varFontContainer.IsVisible = false;
        stack.Children.Add(varFontContainer.Visual);

        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EffectsViewModel.HasVariationAxes))
            {
                var hasAxes = _effects.HasVariationAxes;
                varFontHeader.IsVisible = hasAxes;
                varFontContainer.IsVisible = hasAxes;

                // Rebuild axis sliders
                varFontContainer.Visual.Children.Clear();

                if (hasAxes && _effects.VariationAxesList is { Count: > 0 })
                {
                    foreach (var axis in _effects.VariationAxesList)
                    {
                        var axisGrid = new Grid();
                        axisGrid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
                        axisGrid.Visual.Width = 0;
                        axisGrid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
                        axisGrid.Visual.Height = 0;
                        axisGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        axisGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                        axisGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        axisGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        varFontContainer.Visual.Children.Add(axisGrid.Visual);

                        var axisLabel = new Label();
                        axisLabel.Text = axis.Name ?? axis.Tag;
                        axisGrid.AddChild(axisLabel, row: 0, column: 0);

                        var axisSlider = new Slider();
                        axisSlider.Minimum = axis.MinValue;
                        axisSlider.Maximum = axis.MaxValue;
                        axisSlider.Value = axis.DefaultValue;
                        axisSlider.Width = 100;
                        // Use tick frequency of 1 for integer-like axes, finer for float axes
                        axisSlider.TicksFrequency = (axis.MaxValue - axis.MinValue) > 100 ? 1 : 0.1;
                        axisSlider.IsSnapToTickEnabled = (axis.MaxValue - axis.MinValue) > 100;
                        axisGrid.AddChild(axisSlider, row: 0, column: 1);

                        var axisValue = new Label();
                        axisValue.Text = axis.DefaultValue.ToString("F0");
                        axisGrid.AddChild(axisValue, row: 0, column: 2);

                        var capturedTag = axis.Tag;
                        axisSlider.ValueChanged += (_, _) =>
                        {
                            var val = (float)axisSlider.Value;
                            axisValue.Text = val.ToString("F0");
                            _effects.VariationAxisValues[capturedTag] = val;
                        };
                    }
                }
            }
        };
    }

    private static void AddCollapsibleSection(
        Gum.Wireframe.GraphicalUiElement parent,
        string title,
        Action<Gum.Wireframe.GraphicalUiElement> buildContent,
        Action<bool> enableChanged,
        bool startExpanded = false,
        string? tooltip = null)
    {
        var enableCheck = new CheckBox();
        enableCheck.Text = title;
        enableCheck.Width = 220;
        parent.Children.Add(enableCheck.Visual);
        if (tooltip != null)
            TooltipService.SetTooltip(enableCheck, tooltip);

        // Content container with subtle background
        var contentWrapper = new ContainerRuntime();
        contentWrapper.X = 8;
        contentWrapper.Width = -8;
        contentWrapper.WidthUnits = DimensionUnitType.RelativeToParent;
        contentWrapper.HeightUnits = DimensionUnitType.RelativeToChildren;
        contentWrapper.Height = 8; // padding
        contentWrapper.Visible = startExpanded;
        parent.Children.Add(contentWrapper);

        var contentBg = new ColoredRectangleRuntime();
        contentBg.Width = 0;
        contentBg.WidthUnits = DimensionUnitType.RelativeToParent;
        contentBg.Height = 0;
        contentBg.HeightUnits = DimensionUnitType.RelativeToParent;
        contentBg.Color = new Microsoft.Xna.Framework.Color(40, 40, 44);
        contentWrapper.Children.Add(contentBg);

        var content = new StackPanel();
        content.Spacing = 4;
        content.Visual.X = 4;
        content.Visual.Y = 4;
        content.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        content.Visual.Width = -8; // 4px padding each side within contentWrapper
        content.IsVisible = true;
        contentWrapper.Children.Add(content.Visual);

        if (startExpanded)
            enableCheck.IsChecked = true;

        enableCheck.Checked += (_, _) =>
        {
            contentWrapper.Visible = true;
            enableChanged(true);
        };
        enableCheck.Unchecked += (_, _) =>
        {
            contentWrapper.Visible = false;
            enableChanged(false);
        };

        buildContent(content.Visual);
    }

    private static void AddSliderRow(
        Gum.Wireframe.GraphicalUiElement parent,
        string label, int min, int max, int defaultVal,
        Action<int> onChanged)
    {
        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        parent.Children.Add(grid.Visual);

        var lbl = new Label();
        lbl.Text = label;
        grid.AddChild(lbl, row: 0, column: 0);

        var slider = new Slider();
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = defaultVal;
        slider.Width = 100;
        slider.TicksFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        grid.AddChild(slider, row: 0, column: 1);

        var valueLabel = new Label();
        valueLabel.Text = defaultVal.ToString();
        grid.AddChild(valueLabel, row: 0, column: 2);

        slider.ValueChanged += (_, _) =>
        {
            var val = (int)slider.Value;
            valueLabel.Text = val.ToString();
            onChanged(val);
        };
    }

    private static void AddColorRow(
        GraphicsDevice graphicsDevice,
        Gum.Wireframe.GraphicalUiElement parent,
        string label, string initialHex,
        Action<string> onColorChanged)
    {
        var (defaultR, defaultG, defaultB) = EffectsViewModel.ParseHex(initialHex);

        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        parent.Children.Add(grid.Visual);

        var lbl = new Label();
        lbl.Text = label;
        grid.AddChild(lbl, row: 0, column: 0);

        // Color swatch preview
        var swatchContainer = new ContainerRuntime();
        swatchContainer.Width = 24;
        swatchContainer.Height = 24;
        swatchContainer.HasEvents = true;
        grid.AddChild(swatchContainer, row: 0, column: 1);

        var swatch = new ColoredRectangleRuntime();
        swatch.Width = 0;
        swatch.WidthUnits = DimensionUnitType.RelativeToParent;
        swatch.Height = 0;
        swatch.HeightUnits = DimensionUnitType.RelativeToParent;
        swatch.Color = new Microsoft.Xna.Framework.Color(defaultR, defaultG, defaultB);
        swatchContainer.Children.Add(swatch);

        var swatchBorder = new ColoredRectangleRuntime();
        swatchBorder.Width = 0;
        swatchBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Height = 0;
        swatchBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Color = Theme.PanelBorder;
        swatchContainer.Children.Insert(0, swatchBorder);

        // Hex input
        var hexBox = new TextBox();
        hexBox.Width = 80;
        hexBox.Text = $"#{defaultR:X2}{defaultG:X2}{defaultB:X2}";
        TooltipService.SetTooltip(hexBox, "Hex color (e.g., #FF0000)");
        var suppressHexSync = false;

        hexBox.TextChanged += (_, _) =>
        {
            if (suppressHexSync) return;
            var hex = hexBox.Text?.Trim() ?? "";
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                swatch.Color = new Microsoft.Xna.Framework.Color(r, g, b);
                onColorChanged($"#{r:X2}{g:X2}{b:X2}");
            }
        };
        grid.AddChild(hexBox, row: 0, column: 2);

        swatchContainer.Click += (_, _) =>
        {
            var currentColor = swatch.Color;
            ColorPickerDialog.Show(graphicsDevice, currentColor, newColor =>
            {
                var hex = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
                swatch.Color = newColor;
                suppressHexSync = true;
                hexBox.Text = hex;
                suppressHexSync = false;
                onColorChanged(hex);
            });
        };
    }

    private static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        // Container with background bar
        var container = new ContainerRuntime();
        container.Width = 0;
        container.WidthUnits = DimensionUnitType.RelativeToParent;
        container.Height = 22;
        container.HeightUnits = DimensionUnitType.Absolute;
        parent.Children.Add(container);

        // Background bar
        var bg = new ColoredRectangleRuntime();
        bg.Width = 0;
        bg.WidthUnits = DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = DimensionUnitType.RelativeToParent;
        bg.Color = new Microsoft.Xna.Framework.Color(50, 50, 55);
        container.Children.Add(bg);

        // Header text
        var header = new TextRuntime();
        header.Text = text;
        header.Color = Theme.Accent;
        header.X = 6;
        header.Y = 2;
        container.Children.Add(header);
    }

    private static void AddDivider(Gum.Wireframe.GraphicalUiElement parent)
    {
        var divider = new ColoredRectangleRuntime();
        divider.Width = 0;
        divider.WidthUnits = DimensionUnitType.RelativeToParent;
        divider.Height = 1;
        divider.Color = Theme.PanelBorder;
        parent.Children.Add(divider);
    }
}
