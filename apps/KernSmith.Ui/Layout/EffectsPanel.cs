using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Right-side panel containing all glyph effect controls (font style, outline, shadow, gradient,
/// channels, SDF, color font, variable font axes, fallback character) and the atlas/output
/// configuration sections.
/// </summary>
public class EffectsPanel : Panel
{
    private readonly EffectsViewModel _effects;
    private readonly AtlasConfigViewModel _atlasConfig;

    public EffectsPanel(EffectsViewModel effects, AtlasConfigViewModel atlasConfig)
    {
        _effects = effects;
        _atlasConfig = atlasConfig;
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
        AddSectionHeader(stack, "FONT STYLE");

        var boldCheck = new CheckBox();
        boldCheck.Text = "Bold";
        boldCheck.Width = 200;
        boldCheck.Checked += (_, _) => _effects.Bold = true;
        boldCheck.Unchecked += (_, _) => _effects.Bold = false;
        stack.Children.Add(boldCheck.Visual);
        TooltipService.SetTooltip(boldCheck, "Use the bold variant of the font if available, otherwise apply synthetic bold");

        var italicCheck = new CheckBox();
        italicCheck.Text = "Italic";
        italicCheck.Width = 200;
        italicCheck.Checked += (_, _) => _effects.Italic = true;
        italicCheck.Unchecked += (_, _) => _effects.Italic = false;
        stack.Children.Add(italicCheck.Visual);
        TooltipService.SetTooltip(italicCheck, "Use the italic variant if available, otherwise apply synthetic italic");

        var aaCheck = new CheckBox();
        aaCheck.Text = "Anti-Alias";
        aaCheck.Width = 200;
        aaCheck.IsChecked = true;
        aaCheck.Checked += (_, _) => _effects.AntiAlias = true;
        aaCheck.Unchecked += (_, _) => _effects.AntiAlias = false;
        stack.Children.Add(aaCheck.Visual);
        TooltipService.SetTooltip(aaCheck, "Smooth glyph edges using grayscale anti-aliasing");

        var hintCheck = new CheckBox();
        hintCheck.Text = "Hinting";
        hintCheck.Width = 200;
        hintCheck.IsChecked = true;
        hintCheck.Checked += (_, _) => _effects.Hinting = true;
        hintCheck.Unchecked += (_, _) => _effects.Hinting = false;
        stack.Children.Add(hintCheck.Visual);
        TooltipService.SetTooltip(hintCheck, "Apply font hinting for sharper rendering at small sizes");

        // Super sampling
        var ssLabel = new Label();
        ssLabel.Text = "Super Sample:";
        stack.Children.Add(ssLabel.Visual);
        TooltipService.SetTooltip(ssLabel, "Render at higher resolution then downscale for smoother results");

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

        AddDivider(stack);

        // --- OUTLINE section ---
        AddCollapsibleSection(stack, "OUTLINE", contentPanel =>
        {
            var widthRow = new StackPanel();
            widthRow.Orientation = Orientation.Horizontal;
            widthRow.Spacing = 4;
            contentPanel.Children.Add(widthRow.Visual);

            var widthLabel = new Label();
            widthLabel.Text = "Width:";
            widthLabel.Width = 70;
            widthRow.AddChild(widthLabel);

            var widthSlider = new Slider();
            widthSlider.Minimum = 1;
            widthSlider.Maximum = 10;
            widthSlider.Value = 1;
            widthSlider.Width = 100;
            widthSlider.TicksFrequency = 1;
            widthSlider.IsSnapToTickEnabled = true;
            widthRow.AddChild(widthSlider);

            var widthValue = new Label();
            widthValue.Text = "1";
            widthRow.AddChild(widthValue);

            widthSlider.ValueChanged += (_, _) =>
            {
                var val = (int)widthSlider.Value;
                widthValue.Text = val.ToString();
                _effects.OutlineWidth = val;
            };

            // Outline color RGB row
            AddColorRow(contentPanel, "Color:",
                _effects.OutlineColorR, _effects.OutlineColorG, _effects.OutlineColorB,
                v => _effects.OutlineColorR = v,
                v => _effects.OutlineColorG = v,
                v => _effects.OutlineColorB = v);
        }, enableChanged: enabled => _effects.OutlineEnabled = enabled,
            tooltip: "Add an outline border around each glyph");

        AddDivider(stack);

        // --- SHADOW section ---
        AddCollapsibleSection(stack, "SHADOW", contentPanel =>
        {
            AddSliderRow(contentPanel, "Offset X:", -10, 10, 2,
                val => _effects.ShadowOffsetX = val);
            AddSliderRow(contentPanel, "Offset Y:", -10, 10, 2,
                val => _effects.ShadowOffsetY = val);
            AddSliderRow(contentPanel, "Blur:", 0, 10, 0,
                val => _effects.ShadowBlur = val);

            // Shadow color RGB row
            AddColorRow(contentPanel, "Color:",
                _effects.ShadowColorR, _effects.ShadowColorG, _effects.ShadowColorB,
                v => _effects.ShadowColorR = v,
                v => _effects.ShadowColorG = v,
                v => _effects.ShadowColorB = v);

            // Shadow opacity slider
            AddSliderRow(contentPanel, "Opacity:", 0, 100, 100,
                val => _effects.ShadowOpacity = val);
        }, enableChanged: enabled => _effects.ShadowEnabled = enabled,
            tooltip: "Add a drop shadow behind each glyph");

        AddDivider(stack);

        // --- GRADIENT section ---
        AddCollapsibleSection(stack, "GRADIENT", contentPanel =>
        {
            AddColorRow(contentPanel, "Start:",
                _effects.GradientStartR, _effects.GradientStartG, _effects.GradientStartB,
                v => _effects.GradientStartR = v,
                v => _effects.GradientStartG = v,
                v => _effects.GradientStartB = v);

            AddColorRow(contentPanel, "End:",
                _effects.GradientEndR, _effects.GradientEndG, _effects.GradientEndB,
                v => _effects.GradientEndR = v,
                v => _effects.GradientEndG = v,
                v => _effects.GradientEndB = v);

            AddSliderRow(contentPanel, "Angle:", 0, 360, 90,
                val => _effects.GradientAngle = val);
        }, enableChanged: enabled => _effects.GradientEnabled = enabled,
            tooltip: "Apply a color gradient across each glyph");

        AddDivider(stack);

        // --- CHANNELS section ---
        AddCollapsibleSection(stack, "CHANNELS", contentPanel =>
        {
            var packingCheck = new CheckBox();
            packingCheck.Text = "Channel Packing";
            packingCheck.Width = 180;
            packingCheck.Checked += (_, _) => _effects.ChannelPackingEnabled = true;
            packingCheck.Unchecked += (_, _) => _effects.ChannelPackingEnabled = false;
            contentPanel.Children.Add(packingCheck.Visual);
        }, enableChanged: _ => { },
            tooltip: "Pack glyph data into specific RGBA channels");

        AddDivider(stack);

        // --- ADVANCED section ---
        AddSectionHeader(stack, "ADVANCED");

        var sdfCheck = new CheckBox();
        sdfCheck.Text = "SDF";
        sdfCheck.Width = 220;
        sdfCheck.Checked += (_, _) => _effects.SdfEnabled = true;
        sdfCheck.Unchecked += (_, _) => _effects.SdfEnabled = false;
        stack.Children.Add(sdfCheck.Visual);
        TooltipService.SetTooltip(sdfCheck, "Signed Distance Field — scalable font rendering for game engines. Not compatible with outline, shadow, gradient, or super sampling.");

        // SDF incompatibility warning (covers super-sample, outline, shadow, gradient)
        var sdfWarning = new TextRuntime();
        sdfWarning.Text = "";
        sdfWarning.Color = Theme.Warning;
        sdfWarning.Visible = false;
        stack.Children.Add(sdfWarning);

        var colorCheck = new CheckBox();
        colorCheck.Text = "Color Font";
        colorCheck.Width = 220;
        colorCheck.Checked += (_, _) => _effects.ColorFontEnabled = true;
        colorCheck.Unchecked += (_, _) => _effects.ColorFontEnabled = false;
        stack.Children.Add(colorCheck.Visual);
        TooltipService.SetTooltip(colorCheck, "Render color glyphs from fonts with COLR/CPAL tables (e.g., emoji). Has no effect on fonts without color tables.");

        var colorFontHint = new TextRuntime();
        colorFontHint.Text = "Only affects fonts with color tables (e.g. emoji)";
        colorFontHint.Color = Theme.TextMuted;
        stack.Children.Add(colorFontHint);

        // Warning when color font is enabled but the loaded font has no color glyphs
        var colorFontNoTablesWarning = new TextRuntime();
        colorFontNoTablesWarning.Text = "Current font has no color tables \u2014 no visible effect";
        colorFontNoTablesWarning.Color = Theme.Warning;
        colorFontNoTablesWarning.Visible = false;
        stack.Children.Add(colorFontNoTablesWarning);

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

            // Color font no-tables warning
            if (e.PropertyName is nameof(EffectsViewModel.ColorFontEnabled) or nameof(EffectsViewModel.HasColorGlyphs))
                colorFontNoTablesWarning.Visible = _effects.ColorFontEnabled && !_effects.HasColorGlyphs;
        };

        // --- FALLBACK CHARACTER section ---
        AddDivider(stack);
        AddSectionHeader(stack, "FALLBACK CHARACTER");

        var fallbackRow = new StackPanel();
        fallbackRow.Orientation = Orientation.Horizontal;
        fallbackRow.Spacing = 4;
        stack.Children.Add(fallbackRow.Visual);

        var fallbackLabel = new Label();
        fallbackLabel.Text = "Char:";
        fallbackLabel.Width = 70;
        fallbackRow.AddChild(fallbackLabel);
        TooltipService.SetTooltip(fallbackLabel, "Character to display when a requested glyph is missing from the font");

        var fallbackTextBox = new TextBox();
        fallbackTextBox.Width = 60;
        fallbackTextBox.Text = _effects.FallbackCharacter;
        fallbackTextBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(fallbackTextBox.Text))
                _effects.FallbackCharacter = fallbackTextBox.Text;
        };
        fallbackRow.AddChild(fallbackTextBox);

        // --- VARIABLE FONT section (dynamic, appears when axes are present) ---
        AddDivider(stack);

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
                        var axisRow = new StackPanel();
                        axisRow.Orientation = Orientation.Horizontal;
                        axisRow.Spacing = 4;
                        varFontContainer.Visual.Children.Add(axisRow.Visual);

                        var axisLabel = new Label();
                        axisLabel.Text = axis.Name ?? axis.Tag;
                        axisLabel.Width = 70;
                        axisRow.AddChild(axisLabel);

                        var axisSlider = new Slider();
                        axisSlider.Minimum = axis.MinValue;
                        axisSlider.Maximum = axis.MaxValue;
                        axisSlider.Value = axis.DefaultValue;
                        axisSlider.Width = 100;
                        // Use tick frequency of 1 for integer-like axes, finer for float axes
                        axisSlider.TicksFrequency = (axis.MaxValue - axis.MinValue) > 100 ? 1 : 0.1;
                        axisSlider.IsSnapToTickEnabled = (axis.MaxValue - axis.MinValue) > 100;
                        axisRow.AddChild(axisSlider);

                        var axisValue = new Label();
                        axisValue.Text = axis.DefaultValue.ToString("F0");
                        axisRow.AddChild(axisValue);

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

        // ================================================================
        // ATLAS / OUTPUT CONFIG (moved from left panel to reduce scrolling)
        // ================================================================
        AddDivider(stack);

        // --- ATLAS section ---
        AddCollapsibleSection(stack, "ATLAS", contentPanel =>
        {
            bool _updatingFromVm = false;

            var sizes = new[] { 128, 256, 512, 1024, 2048, 4096, 8192 };

            var maxSizeRow = new StackPanel();
            maxSizeRow.Orientation = Orientation.Horizontal;
            maxSizeRow.Spacing = 4;
            contentPanel.Children.Add(maxSizeRow.Visual);

            var sizeLabel = new Label();
            sizeLabel.Text = "Max Size:";
            maxSizeRow.AddChild(sizeLabel);
            TooltipService.SetTooltip(sizeLabel, "Maximum atlas texture dimensions in pixels. When Autofit is enabled, the actual output may be smaller if all glyphs fit in a smaller atlas.");

            var maxWidthCombo = new ComboBox();
            maxWidthCombo.Width = 80;
            foreach (var s in sizes) maxWidthCombo.Items.Add(s.ToString());
            maxWidthCombo.SelectedIndex = Array.IndexOf(sizes, _atlasConfig.MaxWidth);
            if (maxWidthCombo.SelectedIndex < 0) maxWidthCombo.SelectedIndex = 3; // 1024
            maxWidthCombo.SelectionChanged += (_, _) =>
            {
                if (!_updatingFromVm && maxWidthCombo.SelectedIndex >= 0)
                    _atlasConfig.MaxWidth = sizes[maxWidthCombo.SelectedIndex];
            };
            maxSizeRow.AddChild(maxWidthCombo);

            var xLabel = new Label();
            xLabel.Text = "x";
            maxSizeRow.AddChild(xLabel);

            var maxHeightCombo = new ComboBox();
            maxHeightCombo.Width = 80;
            foreach (var s in sizes) maxHeightCombo.Items.Add(s.ToString());
            maxHeightCombo.SelectedIndex = Array.IndexOf(sizes, _atlasConfig.MaxHeight);
            if (maxHeightCombo.SelectedIndex < 0) maxHeightCombo.SelectedIndex = 3;
            maxHeightCombo.SelectionChanged += (_, _) =>
            {
                if (!_updatingFromVm && maxHeightCombo.SelectedIndex >= 0)
                    _atlasConfig.MaxHeight = sizes[maxHeightCombo.SelectedIndex];
            };
            maxSizeRow.AddChild(maxHeightCombo);

            var autofit = new CheckBox();
            autofit.Text = "Autofit Texture";
            autofit.Width = 220;
            autofit.IsChecked = _atlasConfig.AutofitTexture;
            TooltipService.SetTooltip(autofit, "Shrink the atlas to the smallest power-of-two size that fits all glyphs. The output may be much smaller than Max Size (e.g., 256x256 even if max is 1024x1024). Disable this to always use the exact Max Size.");
            autofit.Checked += (_, _) => { if (!_updatingFromVm) _atlasConfig.AutofitTexture = true; };
            autofit.Unchecked += (_, _) => { if (!_updatingFromVm) _atlasConfig.AutofitTexture = false; };
            contentPanel.Children.Add(autofit.Visual);

            var packAlgoLabel = new Label();
            packAlgoLabel.Text = "Packing Algorithm:";
            contentPanel.Children.Add(packAlgoLabel.Visual);
            TooltipService.SetTooltip(packAlgoLabel, "Algorithm used to arrange glyphs in the texture atlas");

            var packAlgoCombo = new ComboBox();
            packAlgoCombo.Width = 200;
            packAlgoCombo.Items.Add("MaxRects");
            packAlgoCombo.Items.Add("Skyline");
            packAlgoCombo.SelectedIndex = _atlasConfig.PackingAlgorithmIndex;
            packAlgoCombo.SelectionChanged += (_, _) =>
            {
                if (!_updatingFromVm && packAlgoCombo.SelectedIndex >= 0)
                    _atlasConfig.PackingAlgorithmIndex = packAlgoCombo.SelectedIndex;
            };
            contentPanel.Children.Add(packAlgoCombo.Visual);

            // --- Padding (cross layout using 3 columns) ---
            AddLabeledDivider(contentPanel, "Padding");

            var padCross = new StackPanel();
            padCross.Orientation = Orientation.Horizontal;
            padCross.Spacing = 2;
            contentPanel.Children.Add(padCross.Visual);

            // Left column: empty, Left input, empty
            var padColLeft = new StackPanel();
            padColLeft.Spacing = 2;
            var padLeftSpacer = new Label(); padLeftSpacer.Text = ""; padLeftSpacer.Height = 24;
            padColLeft.AddChild(padLeftSpacer);
            var padLeftBox = CreateSmallIntBox(_atlasConfig.PaddingLeft, v => { if (!_updatingFromVm) _atlasConfig.PaddingLeft = Math.Clamp(v, 0, 32); });
            padColLeft.AddChild(padLeftBox);
            var padLeftSpacer2 = new Label(); padLeftSpacer2.Text = ""; padLeftSpacer2.Height = 24;
            padColLeft.AddChild(padLeftSpacer2);
            padCross.AddChild(padColLeft);

            // Center column: Up input, "pad" label, Down input
            var padColCenter = new StackPanel();
            padColCenter.Spacing = 2;
            var padUpBox = CreateSmallIntBox(_atlasConfig.PaddingUp, v => { if (!_updatingFromVm) _atlasConfig.PaddingUp = Math.Clamp(v, 0, 32); });
            padColCenter.AddChild(padUpBox);
            var padCenterLabel = new Label(); padCenterLabel.Text = "Aa";
            padColCenter.AddChild(padCenterLabel);
            var padDownBox = CreateSmallIntBox(_atlasConfig.PaddingDown, v => { if (!_updatingFromVm) _atlasConfig.PaddingDown = Math.Clamp(v, 0, 32); });
            padColCenter.AddChild(padDownBox);
            padCross.AddChild(padColCenter);

            // Right column: empty, Right input, empty
            var padColRight = new StackPanel();
            padColRight.Spacing = 2;
            var padRightSpacer = new Label(); padRightSpacer.Text = ""; padRightSpacer.Height = 24;
            padColRight.AddChild(padRightSpacer);
            var padRightBox = CreateSmallIntBox(_atlasConfig.PaddingRight, v => { if (!_updatingFromVm) _atlasConfig.PaddingRight = Math.Clamp(v, 0, 32); });
            padColRight.AddChild(padRightBox);
            var padRightSpacer2 = new Label(); padRightSpacer2.Text = ""; padRightSpacer2.Height = 24;
            padColRight.AddChild(padRightSpacer2);
            padCross.AddChild(padColRight);

            // --- Spacing (simple H x V row) ---
            AddLabeledDivider(contentPanel, "Spacing");

            var spacingRow = new StackPanel();
            spacingRow.Orientation = Orientation.Horizontal;
            spacingRow.Spacing = 4;
            contentPanel.Children.Add(spacingRow.Visual);

            var spcHLabel = new Label(); spcHLabel.Text = "H:";
            spacingRow.AddChild(spcHLabel);
            var spacingHBox = CreateSmallIntBox(_atlasConfig.SpacingH, v => { if (!_updatingFromVm) _atlasConfig.SpacingH = Math.Clamp(v, 0, 32); });
            spacingRow.AddChild(spacingHBox);
            var spcVLabel = new Label(); spcVLabel.Text = "V:";
            spacingRow.AddChild(spcVLabel);
            var spacingVBox = CreateSmallIntBox(_atlasConfig.SpacingV, v => { if (!_updatingFromVm) _atlasConfig.SpacingV = Math.Clamp(v, 0, 32); });
            spacingRow.AddChild(spacingVBox);

            // Sync UI from ViewModel when preset is applied
            _atlasConfig.PropertyChanged += (_, e) =>
            {
                _updatingFromVm = true;
                try
                {
                    switch (e.PropertyName)
                    {
                        case nameof(AtlasConfigViewModel.MaxWidth):
                            var wi = Array.IndexOf(sizes, _atlasConfig.MaxWidth);
                            if (wi >= 0) maxWidthCombo.SelectedIndex = wi;
                            break;
                        case nameof(AtlasConfigViewModel.MaxHeight):
                            var hi = Array.IndexOf(sizes, _atlasConfig.MaxHeight);
                            if (hi >= 0) maxHeightCombo.SelectedIndex = hi;
                            break;
                        case nameof(AtlasConfigViewModel.AutofitTexture): autofit.IsChecked = _atlasConfig.AutofitTexture; break;
                        case nameof(AtlasConfigViewModel.PackingAlgorithmIndex): packAlgoCombo.SelectedIndex = _atlasConfig.PackingAlgorithmIndex; break;
                        case nameof(AtlasConfigViewModel.PaddingUp): padUpBox.Text = _atlasConfig.PaddingUp.ToString(); break;
                        case nameof(AtlasConfigViewModel.PaddingRight): padRightBox.Text = _atlasConfig.PaddingRight.ToString(); break;
                        case nameof(AtlasConfigViewModel.PaddingDown): padDownBox.Text = _atlasConfig.PaddingDown.ToString(); break;
                        case nameof(AtlasConfigViewModel.PaddingLeft): padLeftBox.Text = _atlasConfig.PaddingLeft.ToString(); break;
                        case nameof(AtlasConfigViewModel.SpacingH): spacingHBox.Text = _atlasConfig.SpacingH.ToString(); break;
                        case nameof(AtlasConfigViewModel.SpacingV): spacingVBox.Text = _atlasConfig.SpacingV.ToString(); break;
                    }
                }
                finally { _updatingFromVm = false; }
            };
        }, enableChanged: _ => { }, startExpanded: true);

        AddDivider(stack);

        // --- OUTPUT section ---
        AddCollapsibleSection(stack, "OUTPUT", contentPanel =>
        {
            var formatLabel = new Label();
            formatLabel.Text = "Descriptor Format:";
            contentPanel.Children.Add(formatLabel.Visual);
            TooltipService.SetTooltip(formatLabel, "File format for the .fnt descriptor (Text, XML, or Binary)");

            var formatGroup = new StackPanel();
            formatGroup.Spacing = 2;
            contentPanel.Children.Add(formatGroup.Visual);

            var formatRadios = new List<(RadioButton rb, OutputFormat fmt)>();
            var formats = new[] { ("Text", OutputFormat.Text), ("XML", OutputFormat.Xml), ("Binary", OutputFormat.Binary) };
            foreach (var (name, format) in formats)
            {
                var rb = new RadioButton();
                rb.Text = name;
                rb.Width = 200;
                if (format == _atlasConfig.DescriptorFormat) rb.IsChecked = true;
                var capturedFormat = format;
                rb.Checked += (_, _) => _atlasConfig.DescriptorFormat = capturedFormat;
                formatRadios.Add((rb, format));
                formatGroup.AddChild(rb);
            }

            var kerningCb = new CheckBox();
            kerningCb.Text = "Include Kerning";
            kerningCb.Width = 200;
            kerningCb.IsChecked = _atlasConfig.IncludeKerning;
            TooltipService.SetTooltip(kerningCb, "Include kerning pair data in the output for improved text spacing");
            kerningCb.Checked += (_, _) => _atlasConfig.IncludeKerning = true;
            kerningCb.Unchecked += (_, _) => _atlasConfig.IncludeKerning = false;
            contentPanel.Children.Add(kerningCb.Visual);

            _atlasConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AtlasConfigViewModel.DescriptorFormat))
                    foreach (var (rb, fmt) in formatRadios)
                        rb.IsChecked = fmt == _atlasConfig.DescriptorFormat;
                if (e.PropertyName == nameof(AtlasConfigViewModel.IncludeKerning))
                    kerningCb.IsChecked = _atlasConfig.IncludeKerning;
            };
        }, enableChanged: _ => { }, startExpanded: true);
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
        var row = new StackPanel();
        row.Orientation = Orientation.Horizontal;
        row.Spacing = 4;
        parent.Children.Add(row.Visual);

        var lbl = new Label();
        lbl.Text = label;
        lbl.Width = 70;
        row.AddChild(lbl);

        var slider = new Slider();
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = defaultVal;
        slider.Width = 100;
        slider.TicksFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        row.AddChild(slider);

        var valueLabel = new Label();
        valueLabel.Text = defaultVal.ToString();
        row.AddChild(valueLabel);

        slider.ValueChanged += (_, _) =>
        {
            var val = (int)slider.Value;
            valueLabel.Text = val.ToString();
            onChanged(val);
        };
    }

    private static void AddColorRow(
        Gum.Wireframe.GraphicalUiElement parent,
        string label, byte defaultR, byte defaultG, byte defaultB,
        Action<byte> onRChanged, Action<byte> onGChanged, Action<byte> onBChanged)
    {
        var row = new StackPanel();
        row.Orientation = Orientation.Horizontal;
        row.Spacing = 4;
        parent.Children.Add(row.Visual);

        var lbl = new Label();
        lbl.Text = label;
        lbl.Width = 70;
        row.AddChild(lbl);

        // Color swatch preview
        var swatchContainer = new ContainerRuntime();
        swatchContainer.Width = 24;
        swatchContainer.Height = 24;
        row.Visual.Children.Add(swatchContainer);

        var swatch = new ColoredRectangleRuntime();
        swatch.Width = 0;
        swatch.WidthUnits = DimensionUnitType.RelativeToParent;
        swatch.Height = 0;
        swatch.HeightUnits = DimensionUnitType.RelativeToParent;
        swatch.Color = new Microsoft.Xna.Framework.Color(defaultR, defaultG, defaultB);
        swatchContainer.Children.Add(swatch);

        // Swatch border for visibility against dark backgrounds
        var swatchBorder = new ColoredRectangleRuntime();
        swatchBorder.Width = 0;
        swatchBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Height = 0;
        swatchBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Color = Theme.PanelBorder;
        swatchContainer.Children.Insert(0, swatchBorder);

        // Hex input (e.g., "#FF0000" or "FF0000")
        var hexBox = new TextBox();
        hexBox.Width = 80;
        hexBox.Text = $"#{defaultR:X2}{defaultG:X2}{defaultB:X2}";
        TooltipService.SetTooltip(hexBox, "Enter a hex color value (e.g., #FF0000 or FF0000)");

        byte currentR = defaultR, currentG = defaultG, currentB = defaultB;
        bool updatingFromSlider = false;

        hexBox.TextChanged += (_, _) =>
        {
            if (updatingFromSlider) return;
            var hex = hexBox.Text?.Trim() ?? "";
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                currentR = r; currentG = g; currentB = b;
                onRChanged(r); onGChanged(g); onBChanged(b);
                swatch.Color = new Microsoft.Xna.Framework.Color(r, g, b);
            }
        };
        row.AddChild(hexBox);

        // R/G/B individual component boxes (compact, for fine-tuning)
        var rBox = new TextBox();
        rBox.Width = 34;
        rBox.Text = defaultR.ToString();
        rBox.TextChanged += (_, _) =>
        {
            if (byte.TryParse(rBox.Text, out var val))
            {
                currentR = val;
                onRChanged(val);
                swatch.Color = new Microsoft.Xna.Framework.Color(currentR, currentG, currentB);
                updatingFromSlider = true;
                hexBox.Text = $"#{currentR:X2}{currentG:X2}{currentB:X2}";
                updatingFromSlider = false;
            }
        };
        row.AddChild(rBox);

        var gBox = new TextBox();
        gBox.Width = 34;
        gBox.Text = defaultG.ToString();
        gBox.TextChanged += (_, _) =>
        {
            if (byte.TryParse(gBox.Text, out var val))
            {
                currentG = val;
                onGChanged(val);
                swatch.Color = new Microsoft.Xna.Framework.Color(currentR, currentG, currentB);
                updatingFromSlider = true;
                hexBox.Text = $"#{currentR:X2}{currentG:X2}{currentB:X2}";
                updatingFromSlider = false;
            }
        };
        row.AddChild(gBox);

        var bBox = new TextBox();
        bBox.Width = 34;
        bBox.Text = defaultB.ToString();
        bBox.TextChanged += (_, _) =>
        {
            if (byte.TryParse(bBox.Text, out var val))
            {
                currentB = val;
                onBChanged(val);
                swatch.Color = new Microsoft.Xna.Framework.Color(currentR, currentG, currentB);
                updatingFromSlider = true;
                hexBox.Text = $"#{currentR:X2}{currentG:X2}{currentB:X2}";
                updatingFromSlider = false;
            }
        };
        row.AddChild(bBox);
    }

    private static void AddLabeledIntBox(StackPanel parent, string label, int initialValue, int width, Action<int> onChanged)
    {
        var lbl = new Label();
        lbl.Text = label;
        parent.AddChild(lbl);

        var box = new TextBox();
        box.Width = width;
        box.Text = initialValue.ToString();
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var val))
                onChanged(val);
        };
        parent.AddChild(box);
    }

    private static TextBox AddLabeledIntBoxReturn(Gum.Wireframe.GraphicalUiElement parent, string label, int initialValue, int width, Action<int> onChanged)
    {
        var row = new StackPanel();
        row.Orientation = Orientation.Horizontal;
        row.Spacing = 4;
        parent.Children.Add(row.Visual);

        var lbl = new Label();
        lbl.Text = label;
        row.AddChild(lbl);

        var box = new TextBox();
        box.Width = width;
        box.Text = initialValue.ToString();
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var val))
                onChanged(val);
        };
        row.AddChild(box);
        return box;
    }

    private static TextBox CreateSmallIntBox(int initialValue, Action<int> onChanged)
    {
        var box = new TextBox();
        box.Width = 36;
        box.Height = 24;
        box.Text = initialValue.ToString();
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var val))
                onChanged(val);
        };
        return box;
    }

    private static void AddLabeledDivider(Gum.Wireframe.GraphicalUiElement parent, string label)
    {
        var text = new TextRuntime();
        text.Text = label;
        text.Color = Theme.Accent;
        parent.Children.Add(text);
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
