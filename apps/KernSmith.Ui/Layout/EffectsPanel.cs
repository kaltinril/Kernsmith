using global::Gum.DataTypes;
using global::Gum.Forms.Controls;
using Gum.Themes.Editor;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework.Graphics;
using Gum.GueDeriving;

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
        var (_, inner) = UiFactory.CreateScrollablePanel(this);

        var stack = inner;

        // --- FONT STYLE section (always active) ---
        BuildFontStyleSection(stack);

        // --- EFFECTS section (outline, shadow, gradient, channels) ---
        BuildEffectsSection(stack);

        // --- ADVANCED section (SDF, Color Font, Variable Font) ---
        BuildAdvancedSection(stack);

        // --- FALLBACK CHARACTER section ---
        BuildFallbackSection(stack);

        // --- VARIABLE FONT section (dynamic, appears when axes are present) ---
        BuildVariableFontSection(stack);

    }

    private void BuildFontStyleSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        var expander = UiFactory.CreateExpander("Font Style");
        stack.Children.Add(expander.Visual);

        // Two-column, three-row layout:
        // Row 1: Bold | Synthetic
        // Row 2: Italic | Synthetic
        // Row 3: Hinting
        // (the anti-alias mode is a combo in the grid below)
        var styleRow = new ContainerRuntime();
        styleRow.WidthUnits = DimensionUnitType.RelativeToParent;
        styleRow.Width = 0;
        styleRow.HeightUnits = DimensionUnitType.RelativeToChildren;
        styleRow.Height = 0;
        styleRow.ChildrenLayout = global::Gum.Managers.ChildrenLayout.LeftToRightStack;
        styleRow.StackSpacing = Theme.SectionSpacing;
        expander.AddContent(styleRow);

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
        TooltipService.SetTooltip(hintCheck, "Font hinting for sharper small sizes. The Native backend hints only in Light anti-alias mode (its own vertical auto-hinter).");

        // Right column: Synthetic, Synthetic
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
            if (updatingSyntheticChecks) return;
            _effects.Bold = true;
            UpdateSynBoldState();
        };
        boldCheck.Unchecked += (_, _) =>
        {
            if (updatingSyntheticChecks) return;
            _effects.Bold = false;
            _effects.ForceSyntheticBold = false;
            UpdateSynBoldState();
        };

        // Italic checkbox
        italicCheck.Checked += (_, _) =>
        {
            if (updatingSyntheticChecks) return;
            _effects.Italic = true;
            UpdateSynItalicState();
        };
        italicCheck.Unchecked += (_, _) =>
        {
            if (updatingSyntheticChecks) return;
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
            if (updatingSyntheticChecks) return;
            _effects.ForceSyntheticBold = true;
            if (boldCheck.IsChecked != true)
            {
                updatingSyntheticChecks = true;
                boldCheck.IsChecked = true;
                updatingSyntheticChecks = false;
                _effects.Bold = true;
            }
        };
        synBoldCheck.Unchecked += (_, _) =>
        {
            if (updatingSyntheticChecks) return;
            _effects.ForceSyntheticBold = false;
        };

        // Synthetic italic: when checked, auto-check Italic (synthetic implies italic)
        synItalicCheck.Checked += (_, _) =>
        {
            if (updatingSyntheticChecks) return;
            _effects.ForceSyntheticItalic = true;
            if (italicCheck.IsChecked != true)
            {
                updatingSyntheticChecks = true;
                italicCheck.IsChecked = true;
                updatingSyntheticChecks = false;
                _effects.Italic = true;
            }
        };
        synItalicCheck.Unchecked += (_, _) =>
        {
            if (updatingSyntheticChecks) return;
            _effects.ForceSyntheticItalic = false;
        };

        // Super sampling
        var ssGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(ssGrid);

        var ssLevels = new[] { 1, 2, 4 };
        var ssCombo = new ComboBox();
        ssCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        ssCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        ssCombo.Visual.Width = 0;
        foreach (var level in ssLevels) ssCombo.Items!.Add($"{level}x");
        ssCombo.SelectedIndex = 0;
        ssCombo.SelectionChanged += (_, _) =>
        {
            if (ssCombo.SelectedIndex >= 0)
                _effects.SuperSampleLevel = ssLevels[ssCombo.SelectedIndex];
        };
        ssGrid.AddRow("Super Sample:", ssCombo);
        TooltipService.SetTooltip(ssCombo, "Render at higher resolution then downscale for smoother edges. Higher values improve quality but increase generation time.");

        // Anti-alias mode: only the modes the selected backend advertises are offered.
        var aaCombo = new ComboBox();
        aaCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        aaCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        aaCombo.Visual.Width = 0;
        var aaModes = new List<AntiAliasMode>();
        var updatingAaCombo = false;
        void RefreshAaCombo()
        {
            var modes = _effects.BackendAntiAliasModes;
            if (!modes.Contains(_effects.AntiAlias))
            {
                // Same policy as the distance-field combo: an unsupported choice falls back when
                // the backend changes. Setting it re-enters this method through PropertyChanged.
                _effects.AntiAlias = modes.Contains(AntiAliasMode.Grayscale) ? AntiAliasMode.Grayscale : modes[0];
                return;
            }

            updatingAaCombo = true;
            aaModes.Clear();
            aaModes.AddRange(modes);
            aaCombo.Items!.Clear();
            foreach (var mode in aaModes)
                aaCombo.Items.Add(mode == AntiAliasMode.Lcd ? "LCD" : mode.ToString());
            aaCombo.SelectedIndex = aaModes.IndexOf(_effects.AntiAlias);
            updatingAaCombo = false;
        }
        aaCombo.SelectionChanged += (_, _) =>
        {
            if (updatingAaCombo) return;
            var idx = aaCombo.SelectedIndex;
            if (idx >= 0 && idx < aaModes.Count)
                _effects.AntiAlias = aaModes[idx];
        };
        ssGrid.AddRow("Anti-Alias:", aaCombo);
        TooltipService.SetTooltip(aaCombo, "None: aliased 1-bit edges. Grayscale: smooth edges (default). Light: grayscale with light hinting (Native: its own vertical auto-hinter, needs Hinting on). LCD: subpixel RGB coverage (Native writes RGBA glyphs). Only the selected rasterizer's modes are listed.");
        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EffectsViewModel.BackendAntiAliasModes) or nameof(EffectsViewModel.AntiAlias))
                RefreshAaCombo();
        };
        RefreshAaCombo();

        // LCD filter and subpixel order: only used in LCD mode. Native honors them; the other
        // backends ignore them (there is no capability flag), which the tooltips say.
        var lcdFilterCombo = CreateEnumCombo(
            [LcdFilter.Default, LcdFilter.Light, LcdFilter.Smooth, LcdFilter.None],
            v => v.ToString(), () => _effects.LcdFilter, v => _effects.LcdFilter = v,
            nameof(EffectsViewModel.LcdFilter));
        ssGrid.AddRow("LCD Filter:", lcdFilterCombo);
        TooltipService.SetTooltip(lcdFilterCombo, "LCD mode only. Low-pass filter across the subpixels: Default (Native: Light), Light, Smooth (fewer color fringes, softer), None (sharpest, strongest fringes). Honored by Native; other backends ignore it.");

        var lcdOrderCombo = CreateEnumCombo(
            [LcdSubpixelOrder.Rgb, LcdSubpixelOrder.Bgr, LcdSubpixelOrder.VRgb, LcdSubpixelOrder.VBgr],
            v => v switch
            {
                LcdSubpixelOrder.VRgb => "V-RGB",
                LcdSubpixelOrder.VBgr => "V-BGR",
                _ => v.ToString().ToUpperInvariant(),
            },
            () => _effects.LcdSubpixelOrder, v => _effects.LcdSubpixelOrder = v,
            nameof(EffectsViewModel.LcdSubpixelOrder));
        ssGrid.AddRow("LCD Order:", lcdOrderCombo);
        TooltipService.SetTooltip(lcdOrderCombo, "LCD mode only. The display's subpixel layout: RGB/BGR run left to right, V-RGB/V-BGR top to bottom. Honored by Native; other backends ignore it.");

        void RefreshLcdEnabled()
        {
            var lcd = _effects.AntiAlias == AntiAliasMode.Lcd;
            lcdFilterCombo.IsEnabled = lcd;
            lcdOrderCombo.IsEnabled = lcd;
        }

        // Synthetic width and italic angle: only a backend that supports them gets a non-default
        // value (MainViewModel.EffectiveWidthPercent/EffectiveItalicAngle), so on other backends
        // the box is disabled and shows the value actually used.
        var widthBox = CreateNumberBox(48,
            () => _effects.BackendSupportsWidthScale ? _effects.WidthPercent : 100,
            text => int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v is >= 10 and <= 1000 ? v : null,
            v => _effects.WidthPercent = (int)v);
        ssGrid.AddRow("Width %:", widthBox.Box);
        TooltipService.SetTooltip(widthBox.Box, "Synthetic condensed/expanded width, 10-1000 (100 = normal). Scales glyphs, advances and kerning. Native only; other backends use 100.");

        var angleBox = CreateNumberBox(48,
            () => _effects.BackendSupportsItalicAngle ? _effects.ItalicAngle : 12f,
            text => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                && float.IsFinite(v) && Math.Abs(v) <= 60f ? v : null,
            v => _effects.ItalicAngle = (float)v);
        ssGrid.AddRow("Italic Angle:", angleBox.Box);
        TooltipService.SetTooltip(angleBox.Box, "Slant of synthetic italic in degrees, -60 to 60 (12 = default; positive leans right). Used only when synthetic italic is applied (Italic on a non-italic face, or Synthetic). Native only; other backends use 12.");

        void RefreshShapeBoxes()
        {
            widthBox.Refresh();
            widthBox.Box.IsEnabled = _effects.BackendSupportsWidthScale;
            angleBox.Refresh();
            angleBox.Box.IsEnabled = _effects.BackendSupportsItalicAngle && _effects.Italic;
        }

        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EffectsViewModel.AntiAlias))
                RefreshLcdEnabled();
            if (e.PropertyName is nameof(EffectsViewModel.WidthPercent)
                or nameof(EffectsViewModel.ItalicAngle)
                or nameof(EffectsViewModel.Italic)
                or nameof(EffectsViewModel.BackendSupportsWidthScale)
                or nameof(EffectsViewModel.BackendSupportsItalicAngle))
                RefreshShapeBoxes();
        };
        RefreshLcdEnabled();
        RefreshShapeBoxes();
    }

    /// <summary>
    /// Creates a full-width combo listing <paramref name="values"/>, writing the selection through
    /// <paramref name="set"/> and following the ViewModel when <paramref name="propertyName"/> changes.
    /// </summary>
    private ComboBox CreateEnumCombo<T>(T[] values, Func<T, string> label, Func<T> get, Action<T> set, string propertyName)
    {
        var combo = new ComboBox();
        combo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        combo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        combo.Visual.Width = 0;
        foreach (var value in values) combo.Items!.Add(label(value));
        combo.SelectedIndex = Array.IndexOf(values, get());
        var updating = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedIndex >= 0)
                set(values[combo.SelectedIndex]);
        };
        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != propertyName) return;
            updating = true;
            try { combo.SelectedIndex = Array.IndexOf(values, get()); }
            finally { updating = false; }
        };
        return combo;
    }

    /// <summary>
    /// A small numeric text box: typing a value <paramref name="parse"/> accepts calls
    /// <paramref name="set"/> (only while the box is enabled); <see cref="NumberBox.Refresh"/>
    /// shows <paramref name="shown"/> unless the text already parses to it, so typing is not
    /// interrupted.
    /// </summary>
    private static NumberBox CreateNumberBox(float width, Func<double> shown, Func<string, double?> parse, Action<double> set)
    {
        var box = new TextBox();
        box.Width = width;
        var numberBox = new NumberBox(box, shown, parse);
        box.TextChanged += (_, _) =>
        {
            if (numberBox.Updating || !box.IsEnabled) return;
            if (parse(box.Text ?? "") is { } value)
                set(value);
        };
        return numberBox;
    }

    private sealed class NumberBox(TextBox box, Func<double> shown, Func<string, double?> parse)
    {
        public TextBox Box { get; } = box;
        public bool Updating { get; private set; }

        public void Refresh()
        {
            var value = shown();
            if (parse(Box.Text ?? "") == value) return;
            Updating = true;
            try { Box.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            finally { Updating = false; }
        }
    }

    private void BuildEffectsSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        var expander = UiFactory.CreateExpander("Effects");
        stack.Children.Add(expander.Visual);

        // --- Fill color (base glyph color; default white = no-op) ---
        var fillColorContent = new ContainerRuntime();
        fillColorContent.WidthUnits = DimensionUnitType.RelativeToParent;
        fillColorContent.Width = 0;
        fillColorContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        fillColorContent.Height = 0;
        fillColorContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        fillColorContent.StackSpacing = 4;
        expander.AddContent(fillColorContent);

        UiFactory.AddColorRow(_graphicsDevice, fillColorContent, "Fill:",
            _effects.FillColor, hex => _effects.FillColor = hex,
            "Base glyph color before outline/shadow/gradient. Default white.");

        // --- Outline ---
        var outlineGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(outlineGrid);

        var outlineCheck = new CheckBox();
        outlineCheck.Text = "";
        outlineGrid.AddRow("Outline:", outlineCheck);
        TooltipService.SetTooltip(outlineCheck, "Add an outline border around each glyph");

        var outlineContent = new ContainerRuntime();
        outlineContent.WidthUnits = DimensionUnitType.RelativeToParent;
        outlineContent.Width = 0;
        outlineContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        outlineContent.Height = 0;
        outlineContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        outlineContent.StackSpacing = 4;
        outlineContent.Visible = false;
        expander.AddContent(outlineContent);

        outlineCheck.Checked += (_, _) => { outlineContent.Visible = true; _effects.OutlineEnabled = true; };
        outlineCheck.Unchecked += (_, _) => { outlineContent.Visible = false; _effects.OutlineEnabled = false; };

        UiFactory.AddSliderRow(outlineContent, "Width:", 1, 10, 1,
            val => _effects.OutlineWidth = val);
        UiFactory.AddColorRow(_graphicsDevice, outlineContent, "Color:",
            _effects.OutlineColor, hex => _effects.OutlineColor = hex);

        var shapeGrid = new Grid();
        shapeGrid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        shapeGrid.Visual.Width = 0;
        shapeGrid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        shapeGrid.Visual.Height = 0;
        shapeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shapeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelWidth) });
        shapeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outlineContent.Children.Add(shapeGrid.Visual);

        var shapeLabel = new Label();
        shapeLabel.Text = "Shape:";
        shapeGrid.AddChild(shapeLabel, row: 0, column: 0);

        var outlineShapes = new[] { OutlineShape.Round, OutlineShape.Square, OutlineShape.Plus };
        var shapeCombo = new ComboBox();
        shapeCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        shapeCombo.Width = 100;
        foreach (var shape in outlineShapes) shapeCombo.Items!.Add(shape.ToString());
        shapeCombo.SelectedIndex = Array.IndexOf(outlineShapes, _effects.OutlineShape);
        var updatingShapeFromVm = false;
        shapeCombo.SelectionChanged += (_, _) =>
        {
            if (!updatingShapeFromVm && shapeCombo.SelectedIndex >= 0)
                _effects.OutlineShape = outlineShapes[shapeCombo.SelectedIndex];
        };
        shapeGrid.AddChild(shapeCombo, row: 0, column: 1);
        TooltipService.SetTooltip(shapeCombo, "Outline corner shape. Round is smooth; Square fills diagonals; Plus is diamond-shaped. Most visible with anti-aliasing off.");

        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(EffectsViewModel.OutlineShape)) return;
            updatingShapeFromVm = true;
            try { shapeCombo.SelectedIndex = Array.IndexOf(outlineShapes, _effects.OutlineShape); }
            finally { updatingShapeFromVm = false; }
        };

        // Outline mode: Vector strokes the glyph outline before rasterizing. Only a backend that
        // strokes outlines (Native) gets it, and never with a distance field (core rejects that;
        // see MainViewModel.EffectiveOutlineMode), so otherwise the combo is disabled on Effect.
        var modeGrid = new Grid();
        modeGrid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        modeGrid.Visual.Width = 0;
        modeGrid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        modeGrid.Visual.Height = 0;
        modeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelWidth) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outlineContent.Children.Add(modeGrid.Visual);

        var modeLabel = new Label();
        modeLabel.Text = "Mode:";
        modeGrid.AddChild(modeLabel, row: 0, column: 0);

        var outlineModes = new[] { OutlineMode.Effect, OutlineMode.Vector };
        var modeCombo = new ComboBox();
        modeCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        modeCombo.Width = 100;
        foreach (var mode in outlineModes) modeCombo.Items!.Add(mode.ToString());
        var updatingModeFromVm = false;
        void RefreshModeCombo()
        {
            var vectorAllowed = _effects.BackendSupportsOutlineStroke && !_effects.SdfEnabled;
            updatingModeFromVm = true;
            try
            {
                modeCombo.SelectedIndex = Array.IndexOf(outlineModes, vectorAllowed ? _effects.OutlineMode : OutlineMode.Effect);
                modeCombo.IsEnabled = vectorAllowed;
            }
            finally { updatingModeFromVm = false; }
        }
        modeCombo.SelectionChanged += (_, _) =>
        {
            if (!updatingModeFromVm && modeCombo.SelectedIndex >= 0)
                _effects.OutlineMode = outlineModes[modeCombo.SelectedIndex];
        };
        modeGrid.AddChild(modeCombo, row: 0, column: 1);
        TooltipService.SetTooltip(modeCombo, "Effect: the bitmap outline (every backend). Vector: strokes the glyph's vector outline before rasterizing, for a crisp edge at any width (Native only; not with a distance field; color glyphs keep the effect).");

        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EffectsViewModel.OutlineMode)
                or nameof(EffectsViewModel.BackendSupportsOutlineStroke)
                or nameof(EffectsViewModel.SdfMode))
                RefreshModeCombo();
        };
        RefreshModeCombo();

        // --- Shadow ---
        var shadowGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(shadowGrid);

        var shadowCheck = new CheckBox();
        shadowCheck.Text = "";
        shadowGrid.AddRow("Shadow:", shadowCheck);
        TooltipService.SetTooltip(shadowCheck, "Add a drop shadow behind each glyph");

        var shadowContent = new ContainerRuntime();
        shadowContent.WidthUnits = DimensionUnitType.RelativeToParent;
        shadowContent.Width = 0;
        shadowContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        shadowContent.Height = 0;
        shadowContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        shadowContent.StackSpacing = 4;
        shadowContent.Visible = false;
        expander.AddContent(shadowContent);

        shadowCheck.Checked += (_, _) => { shadowContent.Visible = true; _effects.ShadowEnabled = true; };
        shadowCheck.Unchecked += (_, _) => { shadowContent.Visible = false; _effects.ShadowEnabled = false; };

        UiFactory.AddSliderRow(shadowContent, "Offset X:", -10, 10, 2,
            val => _effects.ShadowOffsetX = val);
        UiFactory.AddSliderRow(shadowContent, "Offset Y:", -10, 10, 2,
            val => _effects.ShadowOffsetY = val);
        UiFactory.AddSliderRow(shadowContent, "Blur:", 0, 10, 0,
            val => _effects.ShadowBlur = val);
        UiFactory.AddColorRow(_graphicsDevice, shadowContent, "Color:",
            _effects.ShadowColor, hex => _effects.ShadowColor = hex);
        UiFactory.AddSliderRow(shadowContent, "Opacity:", 0, 100, 100,
            val => _effects.ShadowOpacity = val);
        var shadowKernelSlider = UiFactory.AddSliderRow(shadowContent, "Blur Kernel:", 0, 10, 0,
            val => _effects.ShadowBlurKernelSize = val);
        TooltipService.SetTooltip(shadowKernelSlider, "Extra blur kernel size on top of Blur; 0 = none, higher = softer.");
        var shadowPassesSlider = UiFactory.AddSliderRow(shadowContent, "Blur Passes:", 1, 10, 1,
            val => _effects.ShadowBlurPasses = val);
        TooltipService.SetTooltip(shadowPassesSlider, "How many times the blur is applied; more passes = softer, wider shadow.");

        var shadowChecksGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        shadowContent.Children.Add(shadowChecksGrid);

        var hardShadowCheck = new CheckBox();
        hardShadowCheck.Text = "";
        hardShadowCheck.IsChecked = _effects.HardShadow;
        TooltipService.SetTooltip(hardShadowCheck, "Use a crisp silhouette instead of soft antialiased edges");
        hardShadowCheck.Checked += (_, _) => _effects.HardShadow = true;
        hardShadowCheck.Unchecked += (_, _) => _effects.HardShadow = false;
        shadowChecksGrid.AddRow("Hard Shadow:", hardShadowCheck);

        // --- Gradient ---
        var gradientGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(gradientGrid);

        var gradientCheck = new CheckBox();
        gradientCheck.Text = "";
        gradientGrid.AddRow("Gradient:", gradientCheck);
        TooltipService.SetTooltip(gradientCheck, "Apply a color gradient across each glyph");

        var gradientContent = new ContainerRuntime();
        gradientContent.WidthUnits = DimensionUnitType.RelativeToParent;
        gradientContent.Width = 0;
        gradientContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        gradientContent.Height = 0;
        gradientContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        gradientContent.StackSpacing = 4;
        gradientContent.Visible = false;
        expander.AddContent(gradientContent);

        gradientCheck.Checked += (_, _) => { gradientContent.Visible = true; _effects.GradientEnabled = true; };
        gradientCheck.Unchecked += (_, _) => { gradientContent.Visible = false; _effects.GradientEnabled = false; };

        UiFactory.AddColorRow(_graphicsDevice, gradientContent, "Start:",
            _effects.GradientStartColor, hex => _effects.GradientStartColor = hex);
        UiFactory.AddColorRow(_graphicsDevice, gradientContent, "End:",
            _effects.GradientEndColor, hex => _effects.GradientEndColor = hex);
        UiFactory.AddSliderRow(gradientContent, "Angle:", 0, 360, 90,
            val => _effects.GradientAngle = val);
        var gradientOffsetBox = UiFactory.AddFloatBoxRow(gradientContent, "Offset:",
            _effects, nameof(EffectsViewModel.GradientOffset),
            () => _effects.GradientOffset, val => _effects.GradientOffset = val);
        TooltipService.SetTooltip(gradientOffsetBox, "Shift the gradient along its axis. 0 = centered.");
        var gradientScaleBox = UiFactory.AddFloatBoxRow(gradientContent, "Scale:",
            _effects, nameof(EffectsViewModel.GradientScale),
            () => _effects.GradientScale, val => _effects.GradientScale = val);
        TooltipService.SetTooltip(gradientScaleBox, "Stretch (>1) or compress (<1) the gradient. 1 = normal.");

        var gradientChecksGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        gradientContent.Children.Add(gradientChecksGrid);

        var gradientCyclicCheck = new CheckBox();
        gradientCyclicCheck.Text = "";
        gradientCyclicCheck.IsChecked = _effects.GradientCyclic;
        gradientCyclicCheck.Checked += (_, _) => _effects.GradientCyclic = true;
        gradientCyclicCheck.Unchecked += (_, _) => _effects.GradientCyclic = false;
        gradientChecksGrid.AddRow("Cyclic:", gradientCyclicCheck);
        TooltipService.SetTooltip(gradientCyclicCheck, "Repeat the gradient instead of clamping at the ends.");

        // --- Channels ---
        var channelsGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(channelsGrid);

        var channelsCheck = new CheckBox();
        channelsCheck.Text = "";
        channelsGrid.AddRow("Channels:", channelsCheck);
        TooltipService.SetTooltip(channelsCheck, "Pack glyph data into specific RGBA channels");

        var channelsContent = new ContainerRuntime();
        channelsContent.WidthUnits = DimensionUnitType.RelativeToParent;
        channelsContent.Width = 0;
        channelsContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        channelsContent.Height = 0;
        channelsContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        channelsContent.StackSpacing = 4;
        channelsContent.Visible = false;
        expander.AddContent(channelsContent);

        channelsCheck.Checked += (_, _) => { channelsContent.Visible = true; };
        channelsCheck.Unchecked += (_, _) => { channelsContent.Visible = false; };

        var channelsChecksGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        channelsContent.Children.Add(channelsChecksGrid);

        var packingCheck = new CheckBox();
        packingCheck.Text = "";
        packingCheck.Checked += (_, _) => _effects.ChannelPackingEnabled = true;
        packingCheck.Unchecked += (_, _) => _effects.ChannelPackingEnabled = false;
        channelsChecksGrid.AddRow("Channel Packing:", packingCheck);
    }

    private void BuildAdvancedSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        var expander = UiFactory.CreateExpander("Advanced");
        stack.Children.Add(expander.Visual);

        var advancedGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(advancedGrid);

        // Distance field mode: SDF needs SupportsSdf, MSDF/MTSDF need SupportsMsdf (Native only).
        var sdfCombo = new ComboBox();
        sdfCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        sdfCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        sdfCombo.Visual.Width = 0;
        var sdfModes = new List<SdfMode>();
        var updatingSdfCombo = false;
        void RefreshSdfCombo()
        {
            var modes = new List<SdfMode> { SdfMode.None };
            if (_effects.BackendSupportsSdf) modes.Add(SdfMode.Sdf);
            if (_effects.BackendSupportsMsdf) { modes.Add(SdfMode.Msdf); modes.Add(SdfMode.Mtsdf); }
            if (!modes.Contains(_effects.SdfMode))
            {
                // Re-enters this method through PropertyChanged.
                _effects.SdfMode = SdfMode.None;
                return;
            }

            updatingSdfCombo = true;
            sdfModes.Clear();
            sdfModes.AddRange(modes);
            sdfCombo.Items!.Clear();
            foreach (var mode in sdfModes)
                sdfCombo.Items.Add(mode == SdfMode.None ? "None" : mode.ToString().ToUpperInvariant());
            sdfCombo.SelectedIndex = sdfModes.IndexOf(_effects.SdfMode);
            sdfCombo.IsEnabled = sdfModes.Count > 1;
            updatingSdfCombo = false;
        }
        sdfCombo.SelectionChanged += (_, _) =>
        {
            if (updatingSdfCombo) return;
            var idx = sdfCombo.SelectedIndex;
            if (idx >= 0 && idx < sdfModes.Count)
                _effects.SdfMode = sdfModes[idx];
        };
        advancedGrid.AddRow("Distance Field:", sdfCombo);
        TooltipService.SetTooltip(sdfCombo, "Distance field output for resolution-independent scaling. SDF: single channel (FreeType, StbTrueType, Native). MSDF: RGB multi-channel with sharp corners; MTSDF: MSDF plus a true SDF in alpha (Native only). Multi-channel atlases are RGBA; the Sample tab decodes them.");

        // SDF incompatibility warning (covers super-sample, outline, shadow, gradient)
        var sdfWarning = new TextRuntime();
        sdfWarning.Text = "";
        sdfWarning.Color = Theme.Warning;
        sdfWarning.Visible = false;
        expander.AddContent(sdfWarning);

        var sdfSpreadContent = new ContainerRuntime();
        sdfSpreadContent.WidthUnits = DimensionUnitType.RelativeToParent;
        sdfSpreadContent.Width = 0;
        sdfSpreadContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        sdfSpreadContent.Height = 0;
        sdfSpreadContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        sdfSpreadContent.StackSpacing = 4;
        expander.AddContent(sdfSpreadContent);

        var sdfSpreadSlider = UiFactory.AddFloatSliderRow(sdfSpreadContent, "SDF Spread:", 2f, 32f, 8f, 1,
            val => _effects.SdfSpread = val);
        TooltipService.SetTooltip(sdfSpreadSlider, "How far the distance field reaches from each edge (pixels). 8 = default; larger = thicker SDF range.");

        var sdfScaleSlider = UiFactory.AddSliderRow(sdfSpreadContent, "SDF Scale:", 1, 4, _effects.SdfScale,
            val => _effects.SdfScale = val);
        TooltipService.SetTooltip(sdfScaleSlider, "Distance-field supersampling: renders the field at 1-4x size for finer precision, then scales it down. 1 = default. Any backend with SDF; also saved to .hiero.");
        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EffectsViewModel.SdfScale) && (int)sdfScaleSlider.Value != _effects.SdfScale)
                sdfScaleSlider.Value = _effects.SdfScale;
        };

        var colorCheck = new CheckBox();
        colorCheck.Text = "";
        colorCheck.IsEnabled = _effects.HasColorGlyphs && _effects.BackendSupportsColorFonts;
        colorCheck.Checked += (_, _) => _effects.ColorFontEnabled = true;
        colorCheck.Unchecked += (_, _) => _effects.ColorFontEnabled = false;
        advancedGrid.AddRow("Color Font:", colorCheck);
        TooltipService.SetTooltip(colorCheck, "Render color glyphs. Needs a backend with color support and a font with color tables: FreeType renders COLR/CPAL and CBDT/CBLC (emoji); Native renders COLR v0/v1, CBDT/CBLC and sbix.");

        var paletteBox = new TextBox();
        paletteBox.Width = 36;
        paletteBox.Text = _effects.ColorPaletteIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        paletteBox.IsEnabled = _effects.ColorFontEnabled;
        paletteBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(paletteBox.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var val))
                _effects.ColorPaletteIndex = val;
        };
        advancedGrid.AddRow("Palette:", paletteBox);
        TooltipService.SetTooltip(paletteBox, "CPAL palette index for color glyphs (0 = the font's default). An index the font lacks fails generation with an error in the status bar.");

        // Color font + Gradient mutual exclusion feedback
        var colorGradientWarning = new TextRuntime();
        colorGradientWarning.Text = "";
        colorGradientWarning.Color = Theme.Warning;
        colorGradientWarning.Visible = false;
        expander.AddContent(colorGradientWarning);

        // Rendering quality: gamma correction (default 1.8 = no-op vs current output)
        var gammaContent = new ContainerRuntime();
        gammaContent.WidthUnits = DimensionUnitType.RelativeToParent;
        gammaContent.Width = 0;
        gammaContent.HeightUnits = DimensionUnitType.RelativeToChildren;
        gammaContent.Height = 0;
        gammaContent.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        gammaContent.StackSpacing = 4;
        expander.AddContent(gammaContent);

        var gammaSlider = UiFactory.AddFloatSliderRow(gammaContent, "Gamma:", 1.0f, 3.0f, 1.8f, 10,
            val => _effects.Gamma = val);
        TooltipService.SetTooltip(gammaSlider, "Lighten/darken glyph edges; 1.8 = default, higher = darker.");

        // Track whether we are programmatically updating checkboxes to avoid recursive loops
        bool updatingColorCheck = false;

        // Wire up validation, SDF auto-disable, color/gradient mutual exclusion, and backend capability gating
        _effects.PropertyChanged += (_, e) =>
        {
            // --- SDF compatibility warning (informational only, does NOT auto-disable) ---
            if (e.PropertyName is nameof(EffectsViewModel.SdfMode)
                or nameof(EffectsViewModel.SuperSampleLevel)
                or nameof(EffectsViewModel.OutlineEnabled)
                or nameof(EffectsViewModel.ShadowEnabled)
                or nameof(EffectsViewModel.GradientEnabled)
                or nameof(EffectsViewModel.ChannelPackingEnabled)
                or nameof(EffectsViewModel.ColorFontEnabled)
                or nameof(EffectsViewModel.FillColor))
            {
                if (_effects.SdfMode is SdfMode.Msdf or SdfMode.Mtsdf)
                {
                    // Core rejects these with MSDF/MTSDF (RGB holds distances, not color).
                    var conflicts = new List<string>();
                    if (_effects.SuperSampleLevel > 1) conflicts.Add("super sampling");
                    if (_effects.OutlineEnabled) conflicts.Add("outline");
                    if (_effects.ShadowEnabled) conflicts.Add("shadow");
                    if (_effects.GradientEnabled) conflicts.Add("gradient");
                    if (_effects.ChannelPackingEnabled) conflicts.Add("channel packing");
                    if (_effects.ColorFontEnabled) conflicts.Add("color font");
                    if (!string.Equals(_effects.FillColor, "#FFFFFF", StringComparison.OrdinalIgnoreCase)) conflicts.Add("fill color");

                    sdfWarning.Text = conflicts.Count > 0
                        ? $"{_effects.SdfMode.ToString().ToUpperInvariant()} cannot be combined with {string.Join(", ", conflicts)}"
                        : "";
                    sdfWarning.Visible = conflicts.Count > 0;
                }
                else if (_effects.SdfEnabled)
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

            if (e.PropertyName is nameof(EffectsViewModel.ColorFontEnabled))
                paletteBox.IsEnabled = _effects.ColorFontEnabled;

            if (e.PropertyName is nameof(EffectsViewModel.ColorPaletteIndex)
                && paletteBox.Text != _effects.ColorPaletteIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
                paletteBox.Text = _effects.ColorPaletteIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Offer only the distance-field modes the backend supports
            if (e.PropertyName is nameof(EffectsViewModel.BackendSupportsSdf)
                or nameof(EffectsViewModel.BackendSupportsMsdf)
                or nameof(EffectsViewModel.SdfMode))
                RefreshSdfCombo();
        };
        RefreshSdfCombo();
    }

    private void BuildFallbackSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        var expander = UiFactory.CreateExpander("Fallback Character");
        stack.Children.Add(expander.Visual);

        var grid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };

        var fallbackTextBox = new TextBox();
        fallbackTextBox.Width = 60;
        fallbackTextBox.Text = _effects.FallbackCharacter;
        fallbackTextBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(fallbackTextBox.Text))
                _effects.FallbackCharacter = fallbackTextBox.Text;
        };
        TooltipService.SetTooltip(fallbackTextBox, "Replacement for missing glyphs");

        grid.AddRow("Char", fallbackTextBox);
        expander.AddContent(grid);
    }

    private void BuildVariableFontSection(global::Gum.Wireframe.GraphicalUiElement stack)
    {
        var expander = UiFactory.CreateExpander("Variable Font", isExpanded: false);
        expander.Visual.Visible = false;
        stack.Children.Add(expander.Visual);
        TooltipService.SetTooltip(expander, "Variable font axis controls. Requires a backend with variable-font support (FreeType or Native) and a variable font (with fvar table).");

        // Container for dynamically rebuilt axis sliders
        var varFontContainer = new ContainerRuntime();
        varFontContainer.WidthUnits = DimensionUnitType.RelativeToParent;
        varFontContainer.Width = 0;
        varFontContainer.HeightUnits = DimensionUnitType.RelativeToChildren;
        varFontContainer.Height = 0;
        varFontContainer.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        varFontContainer.StackSpacing = 4;
        expander.AddContent(varFontContainer);

        var varFontUnsupportedWarning = new TextRuntime();
        varFontUnsupportedWarning.Text = "Rasterizer does not support variable fonts";
        varFontUnsupportedWarning.Color = Theme.Warning;
        varFontUnsupportedWarning.Visible = false;
        expander.AddContent(varFontUnsupportedWarning);

        _effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EffectsViewModel.HasVariationAxes)
                or nameof(EffectsViewModel.VariationAxesList)
                or nameof(EffectsViewModel.BackendSupportsVariableFonts))
            {
                var hasAxes = _effects.HasVariationAxes;
                var backendSupports = _effects.BackendSupportsVariableFonts;
                expander.Visual.Visible = hasAxes;
                expander.IsExpanded = hasAxes;
                varFontUnsupportedWarning.Visible = hasAxes && !backendSupports;

                // Rebuild axis sliders
                varFontContainer.Children.Clear();

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
                        varFontContainer.Children.Add(axisGrid.Visual);

                        var axisLabel = new Label();
                        axisLabel.Text = axis.Name ?? axis.Tag;
                        axisGrid.AddChild(axisLabel, row: 0, column: 0);

                        var axisSlider = new Slider();
                        axisSlider.Minimum = axis.MinValue;
                        axisSlider.Maximum = axis.MaxValue;
                        var initialValue = _effects.VariationAxisValues.TryGetValue(axis.Tag, out var loadedValue)
                            ? loadedValue
                            : axis.DefaultValue;
                        axisSlider.Value = initialValue;
                        axisSlider.Width = 100;
                        axisSlider.IsEnabled = backendSupports;
                        // Use tick frequency of 1 for integer-like axes, finer for float axes
                        axisSlider.TicksFrequency = (axis.MaxValue - axis.MinValue) > 100 ? 1 : 0.1;
                        axisSlider.IsSnapToTickEnabled = (axis.MaxValue - axis.MinValue) > 100;
                        axisGrid.AddChild(axisSlider, row: 0, column: 1);

                        var axisValue = new Label();
                        axisValue.Text = initialValue.ToString("F0");
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
