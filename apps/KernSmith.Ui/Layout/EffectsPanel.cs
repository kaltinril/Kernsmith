using global::Gum.DataTypes;
using global::Gum.Forms.Controls;
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
        var (scrollViewer, inner) = UiFactory.CreateScrollablePanel(this);

        var stack = inner;

        // --- FONT STYLE section (always active) ---
        BuildFontStyleSection(stack);

        // --- OUTLINE section ---
        UiFactory.AddCollapsibleSection(stack, "OUTLINE", BuildOutlineContent,
            enableChanged: enabled => _effects.OutlineEnabled = enabled,
            tooltip: "Add an outline border around each glyph");

        // --- SHADOW section ---
        UiFactory.AddCollapsibleSection(stack, "SHADOW", BuildShadowContent,
            enableChanged: enabled => _effects.ShadowEnabled = enabled,
            tooltip: "Add a drop shadow behind each glyph");

        // --- GRADIENT section ---
        UiFactory.AddCollapsibleSection(stack, "GRADIENT", BuildGradientContent,
            enableChanged: enabled => _effects.GradientEnabled = enabled,
            tooltip: "Apply a color gradient across each glyph");

        // --- CHANNELS section ---
        UiFactory.AddCollapsibleSection(stack, "CHANNELS", BuildChannelsContent,
            enableChanged: _ => { },
            tooltip: "Pack glyph data into specific RGBA channels");

        // --- ADVANCED section (SDF, Color Font, Variable Font) ---
        BuildAdvancedSection(stack);

        // --- FALLBACK CHARACTER section ---
        BuildFallbackSection(stack);

        // --- VARIABLE FONT section (dynamic, appears when axes are present) ---
        BuildVariableFontSection(stack);

    }

    private void BuildFontStyleSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        UiFactory.AddCollapsibleHeader(stack, "FONT STYLE", content =>
        {
            // Two-column, three-row layout:
            // Row 1: Bold | Synthetic
            // Row 2: Italic | Synthetic
            // Row 3: Hinting | Anti-Alias
            var styleRow = new ContainerRuntime();
            styleRow.WidthUnits = DimensionUnitType.RelativeToParent;
            styleRow.Width = 0;
            styleRow.HeightUnits = DimensionUnitType.RelativeToChildren;
            styleRow.Height = 0;
            styleRow.ChildrenLayout = global::Gum.Managers.ChildrenLayout.LeftToRightStack;
            styleRow.StackSpacing = Theme.SectionSpacing;
            content.Children.Add(styleRow);

            // Left column: Bold, Italic, Hinting
            var leftCol = new ContainerRuntime();
            leftCol.WidthUnits = DimensionUnitType.Ratio;
            leftCol.Width = 1;
            leftCol.HeightUnits = DimensionUnitType.RelativeToChildren;
            leftCol.Height = 0;
            leftCol.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
            leftCol.StackSpacing = 8;
            styleRow.Children.Add(leftCol);

            var boldCheck = new CheckBox();
            boldCheck.Text = "Bold";
            leftCol.Children.Add(boldCheck.Visual);
            TooltipService.SetTooltip(boldCheck, "Use the native bold face if available, otherwise apply synthetic emboldening");

            var italicCheck = new CheckBox();
            italicCheck.Text = "Italic";
            leftCol.Children.Add(italicCheck.Visual);
            TooltipService.SetTooltip(italicCheck, "Use the native italic face if available, otherwise apply synthetic oblique");

            var hintCheck = new CheckBox();
            hintCheck.Text = "Hinting";
            hintCheck.IsChecked = true;
            hintCheck.Checked += (_, _) => _effects.Hinting = true;
            hintCheck.Unchecked += (_, _) => _effects.Hinting = false;
            leftCol.Children.Add(hintCheck.Visual);
            TooltipService.SetTooltip(hintCheck, "Font hinting for sharper small sizes");

            // Right column: Synthetic, Synthetic, Anti-Alias
            var rightCol = new ContainerRuntime();
            rightCol.WidthUnits = DimensionUnitType.Ratio;
            rightCol.Width = 1;
            rightCol.HeightUnits = DimensionUnitType.RelativeToChildren;
            rightCol.Height = 0;
            rightCol.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
            rightCol.StackSpacing = 8;
            styleRow.Children.Add(rightCol);

            var synBoldCheck = new CheckBox();
            synBoldCheck.Text = "Synthetic";
            synBoldCheck.IsEnabled = false;
            rightCol.Children.Add(synBoldCheck.Visual);
            TooltipService.SetTooltip(synBoldCheck, "Force synthetic bold, skip native face lookup");

            var synItalicCheck = new CheckBox();
            synItalicCheck.Text = "Synthetic";
            synItalicCheck.IsEnabled = false;
            rightCol.Children.Add(synItalicCheck.Visual);
            TooltipService.SetTooltip(synItalicCheck, "Force synthetic italic, skip native face lookup");

            var aaCheck = new CheckBox();
            aaCheck.Text = "Anti-Alias";
            aaCheck.IsChecked = true;
            aaCheck.Checked += (_, _) => _effects.AntiAlias = true;
            aaCheck.Unchecked += (_, _) => _effects.AntiAlias = false;
            rightCol.Children.Add(aaCheck.Visual);
            TooltipService.SetTooltip(aaCheck, "Smooth glyph edges with anti-aliasing");

            // Guard flag to prevent recursive property change loops
            var updatingSyntheticChecks = false;

            void UpdateSynBoldState()
            {
                if (!_effects.Bold)
                {
                    // Bold unchecked: disable and uncheck synthetic
                    synBoldCheck.IsEnabled = false;
                    if (synBoldCheck.IsChecked == true)
                    {
                        updatingSyntheticChecks = true;
                        synBoldCheck.IsChecked = false;
                        updatingSyntheticChecks = false;
                        _effects.ForceSyntheticBold = false;
                    }
                    TooltipService.SetTooltip(synBoldCheck, "Check Bold first to enable synthetic");
                }
                else if (!_effects.FontHasBoldVariant)
                {
                    // Font has no bold variant: bold IS synthetic, show as checked + disabled
                    synBoldCheck.IsEnabled = false;
                    if (synBoldCheck.IsChecked != true)
                    {
                        updatingSyntheticChecks = true;
                        synBoldCheck.IsChecked = true;
                        updatingSyntheticChecks = false;
                    }
                    _effects.ForceSyntheticBold = true;
                    TooltipService.SetTooltip(synBoldCheck, "This font has no native bold face — bold is always synthetic");
                }
                else if (_effects.BackendIsGdi)
                {
                    // GDI + font has bold: can't do synthetic, disable
                    synBoldCheck.IsEnabled = false;
                    if (synBoldCheck.IsChecked == true)
                    {
                        updatingSyntheticChecks = true;
                        synBoldCheck.IsChecked = false;
                        updatingSyntheticChecks = false;
                        _effects.ForceSyntheticBold = false;
                    }
                    TooltipService.SetTooltip(synBoldCheck, "GDI cannot apply synthetic bold when a native bold face exists. Use FreeType or DirectWrite.");
                }
                else
                {
                    // FreeType/DW + font has bold: user can choose
                    synBoldCheck.IsEnabled = true;
                    TooltipService.SetTooltip(synBoldCheck, "Force synthetic bold, skip native face lookup");
                }
            }

            void UpdateSynItalicState()
            {
                if (!_effects.Italic)
                {
                    synItalicCheck.IsEnabled = false;
                    if (synItalicCheck.IsChecked == true)
                    {
                        updatingSyntheticChecks = true;
                        synItalicCheck.IsChecked = false;
                        updatingSyntheticChecks = false;
                        _effects.ForceSyntheticItalic = false;
                    }
                    TooltipService.SetTooltip(synItalicCheck, "Check Italic first to enable synthetic");
                }
                else if (!_effects.FontHasItalicVariant)
                {
                    // Font has no italic variant: italic IS synthetic, show as checked + disabled
                    synItalicCheck.IsEnabled = false;
                    if (synItalicCheck.IsChecked != true)
                    {
                        updatingSyntheticChecks = true;
                        synItalicCheck.IsChecked = true;
                        updatingSyntheticChecks = false;
                    }
                    _effects.ForceSyntheticItalic = true;
                    TooltipService.SetTooltip(synItalicCheck, "This font has no native italic face — italic is always synthetic");
                }
                else
                {
                    // Font has italic variant: user can choose
                    synItalicCheck.IsEnabled = true;
                    TooltipService.SetTooltip(synItalicCheck, "Force synthetic italic, skip native face lookup");
                }
            }

            // Bold checkbox
            boldCheck.Checked += (_, _) =>
            {
                _effects.Bold = true;
                UpdateSynBoldState();
            };
            boldCheck.Unchecked += (_, _) =>
            {
                _effects.Bold = false;
                _effects.ForceSyntheticBold = false;
                UpdateSynBoldState();
            };

            // Italic checkbox
            italicCheck.Checked += (_, _) =>
            {
                _effects.Italic = true;
                UpdateSynItalicState();
            };
            italicCheck.Unchecked += (_, _) =>
            {
                _effects.Italic = false;
                _effects.ForceSyntheticItalic = false;
                UpdateSynItalicState();
            };

            // React to backend or font family changes
            _effects.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(EffectsViewModel.BackendIsGdi)
                    or nameof(EffectsViewModel.FontHasBoldVariant)
                    or nameof(EffectsViewModel.FontHasItalicVariant))
                {
                    UpdateSynBoldState();
                    UpdateSynItalicState();
                }
            };

            // Synthetic bold: when checked, auto-check Bold (synthetic implies bold)
            synBoldCheck.Checked += (_, _) =>
            {
                _effects.ForceSyntheticBold = true;
                if (boldCheck.IsChecked != true)
                {
                    updatingSyntheticChecks = true;
                    boldCheck.IsChecked = true;
                    updatingSyntheticChecks = false;
                    _effects.Bold = true;
                }
            };
            synBoldCheck.Unchecked += (_, _) => _effects.ForceSyntheticBold = false;

            // Synthetic italic: when checked, auto-check Italic (synthetic implies italic)
            synItalicCheck.Checked += (_, _) =>
            {
                _effects.ForceSyntheticItalic = true;
                if (italicCheck.IsChecked != true)
                {
                    updatingSyntheticChecks = true;
                    italicCheck.IsChecked = true;
                    updatingSyntheticChecks = false;
                    _effects.Italic = true;
                }
            };
            synItalicCheck.Unchecked += (_, _) => _effects.ForceSyntheticItalic = false;

            // Super sampling — label and radio buttons on one row
            var ssRow = new StackPanel();
            ssRow.Orientation = Orientation.Horizontal;
            ssRow.Spacing = Theme.ControlSpacing;
            content.Children.Add(ssRow.Visual);

            var ssLabel = new Label();
            ssLabel.Text = "Super Sample:";
            ssRow.AddChild(ssLabel);
            TooltipService.SetTooltip(ssLabel, "Render at higher resolution then downscale for smoother edges. Available with all backends. Higher values improve quality but increase generation time.");

            foreach (var level in new[] { 1, 2, 4 })
            {
                var rb = new RadioButton();
                rb.Text = $"{level}x";
                rb.Width = 50;
                if (level == 1) rb.IsChecked = true;
                var capturedLevel = level;
                rb.Checked += (_, _) => _effects.SuperSampleLevel = capturedLevel;
                ssRow.AddChild(rb);
            }
        });
    }

    private void BuildOutlineContent(global::Gum.Wireframe.GraphicalUiElement contentPanel)
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
        UiFactory.AddColorRow(_graphicsDevice, contentPanel, "Color:",
            _effects.OutlineColor, hex => _effects.OutlineColor = hex);
    }

    private void BuildShadowContent(global::Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        UiFactory.AddSliderRow(contentPanel, "Offset X:", -10, 10, 2,
            val => _effects.ShadowOffsetX = val);
        UiFactory.AddSliderRow(contentPanel, "Offset Y:", -10, 10, 2,
            val => _effects.ShadowOffsetY = val);
        UiFactory.AddSliderRow(contentPanel, "Blur:", 0, 10, 0,
            val => _effects.ShadowBlur = val);

        // Shadow color row
        UiFactory.AddColorRow(_graphicsDevice, contentPanel, "Color:",
            _effects.ShadowColor, hex => _effects.ShadowColor = hex);

        // Shadow opacity slider
        UiFactory.AddSliderRow(contentPanel, "Opacity:", 0, 100, 100,
            val => _effects.ShadowOpacity = val);

        var hardShadowCheck = new CheckBox();
        hardShadowCheck.Text = "Hard Shadow";
        hardShadowCheck.Width = 180;
        hardShadowCheck.IsChecked = _effects.HardShadow;
        TooltipService.SetTooltip(hardShadowCheck, "Use a crisp silhouette instead of soft antialiased edges");
        hardShadowCheck.Checked += (_, _) => _effects.HardShadow = true;
        hardShadowCheck.Unchecked += (_, _) => _effects.HardShadow = false;
        contentPanel.Children.Add(hardShadowCheck.Visual);
    }

    private void BuildGradientContent(global::Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        UiFactory.AddColorRow(_graphicsDevice, contentPanel, "Start:",
            _effects.GradientStartColor, hex => _effects.GradientStartColor = hex);

        UiFactory.AddColorRow(_graphicsDevice, contentPanel, "End:",
            _effects.GradientEndColor, hex => _effects.GradientEndColor = hex);

        UiFactory.AddSliderRow(contentPanel, "Angle:", 0, 360, 90,
            val => _effects.GradientAngle = val);
    }

    private void BuildChannelsContent(global::Gum.Wireframe.GraphicalUiElement contentPanel)
    {
        var packingCheck = new CheckBox();
        packingCheck.Text = "Channel Packing";
        packingCheck.Width = 180;
        packingCheck.Checked += (_, _) => _effects.ChannelPackingEnabled = true;
        packingCheck.Unchecked += (_, _) => _effects.ChannelPackingEnabled = false;
        contentPanel.Children.Add(packingCheck.Visual);
    }

    private void BuildAdvancedSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        UiFactory.AddCollapsibleHeader(stack, "ADVANCED", content =>
        {
            var sdfCheck = new CheckBox();
            sdfCheck.Text = "SDF";
            sdfCheck.Width = 220;
            sdfCheck.IsEnabled = _effects.BackendSupportsSdf;
            sdfCheck.Checked += (_, _) => _effects.SdfEnabled = true;
            sdfCheck.Unchecked += (_, _) => _effects.SdfEnabled = false;
            content.Children.Add(sdfCheck.Visual);
            TooltipService.SetTooltip(sdfCheck, "Signed Distance Field rendering for resolution-independent scaling. Only supported by the FreeType backend.");

            // SDF incompatibility warning (covers super-sample, outline, shadow, gradient)
            var sdfWarning = new TextRuntime();
            sdfWarning.Text = "";
            sdfWarning.Color = Theme.Warning;
            sdfWarning.Visible = false;
            content.Children.Add(sdfWarning);

            var colorCheck = new CheckBox();
            colorCheck.Text = "Color Font";
            colorCheck.Width = 220;
            colorCheck.IsEnabled = _effects.HasColorGlyphs && _effects.BackendSupportsColorFonts;
            colorCheck.Checked += (_, _) => _effects.ColorFontEnabled = true;
            colorCheck.Unchecked += (_, _) => _effects.ColorFontEnabled = false;
            content.Children.Add(colorCheck.Visual);
            TooltipService.SetTooltip(colorCheck, "Render color glyphs (emoji). Requires DirectWrite backend and a font with color tables (COLR/CPAL or CBDT/CBLC).");

            // Color font + Gradient mutual exclusion feedback
            var colorGradientWarning = new TextRuntime();
            colorGradientWarning.Text = "";
            colorGradientWarning.Color = Theme.Warning;
            colorGradientWarning.Visible = false;
            content.Children.Add(colorGradientWarning);

            // Track whether we are programmatically updating checkboxes to avoid recursive loops
            bool updatingSdfCheck = false;
            bool updatingColorCheck = false;

            // Wire up validation, SDF auto-disable, color/gradient mutual exclusion, and backend capability gating
            _effects.PropertyChanged += (_, e) =>
            {
                // --- SDF compatibility warning (informational only, does NOT auto-disable) ---
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
                            sdfWarning.Text = $"SDF + {string.Join(", ", incompatible)} \u2014 effects applied to SDF bitmap";
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

                // Enable/disable color font checkbox based on font color tables and backend capability
                if (e.PropertyName is nameof(EffectsViewModel.HasColorGlyphs)
                    or nameof(EffectsViewModel.BackendSupportsColorFonts))
                    colorCheck.IsEnabled = _effects.HasColorGlyphs && _effects.BackendSupportsColorFonts;

                // Enable/disable SDF checkbox based on backend capability
                if (e.PropertyName is nameof(EffectsViewModel.BackendSupportsSdf))
                {
                    sdfCheck.IsEnabled = _effects.BackendSupportsSdf;
                    if (!_effects.BackendSupportsSdf && _effects.SdfEnabled)
                    {
                        _effects.SdfEnabled = false;
                        if (!updatingSdfCheck)
                        {
                            updatingSdfCheck = true;
                            sdfCheck.IsChecked = false;
                            updatingSdfCheck = false;
                        }
                    }
                }
            };
        });
    }

    private void BuildFallbackSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        UiFactory.AddCollapsibleHeader(stack, "FALLBACK CHARACTER", content =>
        {
            var fallbackRow = new StackPanel();
            fallbackRow.Orientation = Orientation.Horizontal;
            fallbackRow.Spacing = Theme.ControlSpacing;
            content.Children.Add(fallbackRow.Visual);

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
        });
    }

    private void BuildVariableFontSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        var varFontHeader = new Label();
        varFontHeader.Text = "VARIABLE FONT";
        varFontHeader.IsVisible = false;
        stack.Children.Add(varFontHeader.Visual);
        TooltipService.SetTooltip(varFontHeader, "Variable font axis controls. Requires DirectWrite backend and a variable font (with fvar table).");

        var varFontContainer = new StackPanel();
        varFontContainer.Spacing = 4;
        varFontContainer.IsVisible = false;
        stack.Children.Add(varFontContainer.Visual);

        var varFontUnsupportedWarning = new TextRuntime();
        varFontUnsupportedWarning.Text = "Rasterizer does not support variable fonts";
        varFontUnsupportedWarning.Color = Theme.Warning;
        varFontUnsupportedWarning.Visible = false;
        stack.Children.Add(varFontUnsupportedWarning);

        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EffectsViewModel.HasVariationAxes)
                or nameof(EffectsViewModel.BackendSupportsVariableFonts))
            {
                var hasAxes = _effects.HasVariationAxes;
                var backendSupports = _effects.BackendSupportsVariableFonts;
                varFontHeader.IsVisible = hasAxes;
                varFontContainer.IsVisible = hasAxes;
                varFontUnsupportedWarning.Visible = hasAxes && !backendSupports;

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
                        axisSlider.IsEnabled = backendSupports;
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

}
