using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Left-side panel containing engine preset buttons, font file browser, system font dropdown,
/// TTC face selector, font metadata display, font size input, generate button, and
/// auto-regenerate toggle.
/// </summary>
public class FontConfigPanel : Panel
{
    private readonly MainViewModel _mainViewModel;
    private readonly FontConfigViewModel _fontConfig;
    private readonly AtlasConfigViewModel _atlasConfig;

    public FontConfigPanel(MainViewModel mainViewModel, FontConfigViewModel fontConfig, AtlasConfigViewModel atlasConfig)
    {
        _mainViewModel = mainViewModel;
        _fontConfig = fontConfig;
        _atlasConfig = atlasConfig;

        BuildContent();
    }

    private void BuildContent()
    {
        // Inner container with padding from panel edges
        var inner = new ContainerRuntime();
        inner.WidthUnits = DimensionUnitType.RelativeToParent;
        inner.HeightUnits = DimensionUnitType.RelativeToParent;
        inner.Width = -16; // 8px padding each side
        inner.Height = 0;
        inner.X = 8;
        inner.Y = 4;
        inner.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.StackSpacing = 6;
        this.AddChild(inner);
        var stack = inner;

        // --- ENGINE PRESET section ---
        AddSectionHeader(stack, "ENGINE PRESET");

        var presetRow = new StackPanel();
        presetRow.Orientation = Orientation.Horizontal;
        presetRow.Spacing = 2;
        stack.Children.Add(presetRow.Visual);

        var presetDescLabel = new Label();
        presetDescLabel.Text = "";
        presetDescLabel.IsVisible = false;

        // Collect buttons so we can update their labels on click
        var presetButtons = new List<(Button btn, EnginePreset preset)>();

        foreach (var preset in EnginePresets.All)
        {
            if (preset == EnginePresets.Custom) continue;

            var btn = new Button();
            btn.Text = preset.ShortName;
            btn.Width = 40;
            presetButtons.Add((btn, preset));

            TooltipService.SetTooltip(btn, preset.Description);
            var capturedPreset = preset;
            btn.Click += (_, _) =>
            {
                _atlasConfig.ApplyPreset(capturedPreset);
                presetDescLabel.Text = capturedPreset.Description;
                presetDescLabel.IsVisible = true;

                // Expand selected, abbreviate others, disable selected for visual distinction
                foreach (var (b, p) in presetButtons)
                {
                    var isSelected = p == capturedPreset;
                    b.Text = isSelected ? p.Name : p.ShortName;
                    b.Width = isSelected ? 80 : 40;
                    b.IsEnabled = !isSelected;
                }
            };
            presetRow.AddChild(btn);
        }

        stack.Children.Add(presetDescLabel.Visual);

        // Default to XNA preset
        _atlasConfig.ApplyPreset(EnginePresets.MonoGame);
        presetDescLabel.Text = EnginePresets.MonoGame.Description;
        presetDescLabel.IsVisible = true;
        foreach (var (b, p) in presetButtons)
        {
            var isSelected = p == EnginePresets.MonoGame;
            b.Text = isSelected ? p.Name : p.ShortName;
            b.Width = isSelected ? 80 : 40;
            b.IsEnabled = !isSelected;
        }

        AddDivider(stack);

        // --- FONT SOURCE section ---
        AddSectionHeader(stack, "FONT FILE");

        var browseBtn = new Button();
        browseBtn.Text = "Browse for Font...";
        browseBtn.Width = 260;
        browseBtn.Click += (_, _) =>
        {
            var dialog = new FileBrowserDialog();
            dialog.Show(path => _mainViewModel.LoadFontFromPath(path));
        };
        stack.Children.Add(browseBtn.Visual);
        TooltipService.SetTooltip(browseBtn, "Browse for a font file");

        var sourceLabel = new Label();
        sourceLabel.Text = "";
        sourceLabel.IsVisible = false;
        stack.Children.Add(sourceLabel.Visual);

        // Only show source label for file-loaded fonts (system font is visible in dropdown)
        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.FontSourceKind) ||
                e.PropertyName == nameof(FontConfigViewModel.FontSourceDescription))
            {
                var isFile = _fontConfig.FontSourceKind == Models.FontSourceKind.File;
                sourceLabel.IsVisible = isFile;
                if (isFile)
                    sourceLabel.Text = _fontConfig.FontSourceDescription;
            }
        };

        // --- TTC Face Selection (hidden unless a .ttc font collection is loaded) ---
        var faceSelectionRow = new StackPanel();
        faceSelectionRow.Orientation = Orientation.Horizontal;
        faceSelectionRow.Spacing = 4;
        faceSelectionRow.IsVisible = false;
        stack.Children.Add(faceSelectionRow.Visual);

        var faceLabel = new Label();
        faceLabel.Text = "Face:";
        faceLabel.Width = 50;
        faceSelectionRow.AddChild(faceLabel);

        var faceCombo = new ComboBox();
        faceCombo.Width = 80;
        faceSelectionRow.AddChild(faceCombo);

        var faceCountLabel = new Label();
        faceCountLabel.Text = "";
        faceSelectionRow.AddChild(faceCountLabel);

        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.IsFontCollection))
            {
                faceSelectionRow.IsVisible = _fontConfig.IsFontCollection;
                if (_fontConfig.IsFontCollection)
                {
                    faceCombo.Items.Clear();
                    for (int i = 0; i < _fontConfig.FaceCount; i++)
                        faceCombo.Items.Add($"Face {i}");
                    faceCombo.SelectedIndex = 0;
                    faceCountLabel.Text = $"of {_fontConfig.FaceCount}";
                }
            }
        };

        faceCombo.SelectionChanged += (_, _) =>
        {
            if (faceCombo.SelectedIndex >= 0 && _fontConfig.IsFontCollection)
            {
                _fontConfig.ReloadWithFaceIndex(faceCombo.SelectedIndex);
            }
        };

        AddDivider(stack);

        var familyLabel = new Label();
        familyLabel.Text = "System Font:";
        stack.Children.Add(familyLabel.Visual);
        TooltipService.SetTooltip(familyLabel, "Choose an installed system font");

        var familyCombo = new ComboBox();
        familyCombo.Width = 260;
        stack.Children.Add(familyCombo.Visual);

        // Wire system font combo
        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.SystemFonts) && _fontConfig.SystemFonts != null)
            {
                familyCombo.Items.Clear();
                foreach (var group in _fontConfig.SystemFonts)
                    familyCombo.Items.Add(group.FamilyName);
            }
        };

        familyCombo.SelectionChanged += (_, _) =>
        {
            if (familyCombo.SelectedIndex >= 0 && _fontConfig.SystemFonts != null)
            {
                var group = _fontConfig.SystemFonts[familyCombo.SelectedIndex];
                _fontConfig.SelectedFontFamily = group.FamilyName;
                _fontConfig.CurrentFontGroup = group;
                _fontConfig.LoadedAsBold = false;
                _fontConfig.LoadedAsItalic = false;

                // Auto-load Regular, falling back to first available
                if (group.Styles.Count > 0)
                {
                    var font = group.Styles.FirstOrDefault(s =>
                        s.StyleName.Equals("Regular", StringComparison.OrdinalIgnoreCase))
                        ?? group.Styles[0];
                    _fontConfig.SelectedSystemFont = font;
                    try { _fontConfig.LoadFromSystem(font); }
                    catch (Exception) { }
                }
            }
        };

        // --- Glyph count (only useful non-redundant info) ---
        var glyphRow = new StackPanel();
        glyphRow.Orientation = Orientation.Horizontal;
        glyphRow.Spacing = 4;
        stack.Children.Add(glyphRow.Visual);

        var glyphLbl = new Label();
        glyphLbl.Text = "Glyphs in font:";
        glyphRow.AddChild(glyphLbl);

        var glyphCount = new Label();
        glyphCount.Text = "0";
        glyphCount.SetBinding(nameof(Label.Text), nameof(FontConfigViewModel.NumGlyphs));
        glyphCount.Visual.BindingContext = _fontConfig;
        glyphRow.AddChild(glyphCount);

        // --- Conditional font info: color glyphs ---
        var colorGlyphLabel = new Label();
        colorGlyphLabel.Text = "Has color glyphs";
        colorGlyphLabel.IsVisible = false;
        stack.Children.Add(colorGlyphLabel.Visual);

        // --- Conditional font info: variable font axes ---
        var axesLabel = new Label();
        axesLabel.Text = "";
        axesLabel.IsVisible = false;
        stack.Children.Add(axesLabel.Visual);

        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.HasColorGlyphs))
                colorGlyphLabel.IsVisible = _fontConfig.HasColorGlyphs;

            if (e.PropertyName is nameof(FontConfigViewModel.HasVariationAxes)
                or nameof(FontConfigViewModel.VariationAxesSummary))
            {
                axesLabel.IsVisible = _fontConfig.HasVariationAxes;
                if (_fontConfig.HasVariationAxes)
                    axesLabel.Text = $"Variable axes: {_fontConfig.VariationAxesSummary}";
            }
        };

        AddDivider(stack);

        // --- SIZE section ---
        AddSectionHeader(stack, "SIZE");

        var sizeRow = new StackPanel();
        sizeRow.Orientation = Orientation.Horizontal;
        sizeRow.Spacing = 4;
        stack.Children.Add(sizeRow.Visual);

        var sizeLabel = new Label();
        sizeLabel.Text = "Font Size:";
        sizeRow.AddChild(sizeLabel);
        TooltipService.SetTooltip(sizeLabel, "Font size in points (4-500)");

        var sizeTextBox = new TextBox();
        sizeTextBox.Width = 42;
        sizeTextBox.Text = "32";
        sizeTextBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(sizeTextBox.Text, out var size))
                _fontConfig.FontSize = Math.Clamp(size, 4, 500);
        };
        sizeRow.AddChild(sizeTextBox);

        var ptLabel = new Label();
        ptLabel.Text = "pt";
        sizeRow.AddChild(ptLabel);

        AddDivider(stack);

        // --- GENERATE button (primary action, visually distinct) ---
        var generateBtnSpacer = new ColoredRectangleRuntime();
        generateBtnSpacer.Width = 0;
        generateBtnSpacer.WidthUnits = DimensionUnitType.RelativeToParent;
        generateBtnSpacer.Height = 4;
        generateBtnSpacer.Color = Microsoft.Xna.Framework.Color.Transparent;
        stack.Children.Add(generateBtnSpacer);

        var generateBtn = new Button();
        generateBtn.Text = "Generate";
        generateBtn.Width = 260;
        generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        generateBtn.Click += async (_, _) => await _mainViewModel.GenerateAsync();
        stack.Children.Add(generateBtn.Visual);
        TooltipService.SetTooltip(generateBtn, "Generate bitmap font from current settings");

        // Enable/disable Generate button based on font loaded state
        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.IsFontLoaded))
                generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        };

        // Auto-regenerate toggle
        var autoRegenCb = new CheckBox();
        autoRegenCb.Text = "Auto-regenerate";
        autoRegenCb.Width = 260;
        autoRegenCb.Checked += (_, _) => _mainViewModel.AutoRegenerate = true;
        autoRegenCb.Unchecked += (_, _) => _mainViewModel.AutoRegenerate = false;
        stack.Children.Add(autoRegenCb.Visual);
        TooltipService.SetTooltip(autoRegenCb, "Auto-regenerate on settings change");

    }

    private static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        var container = new ContainerRuntime();
        container.Width = 0;
        container.WidthUnits = DimensionUnitType.RelativeToParent;
        container.Height = 22;
        container.HeightUnits = DimensionUnitType.Absolute;
        parent.Children.Add(container);

        var bg = new ColoredRectangleRuntime();
        bg.Width = 0;
        bg.WidthUnits = DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = DimensionUnitType.RelativeToParent;
        bg.Color = new Microsoft.Xna.Framework.Color(50, 50, 55);
        container.Children.Add(bg);

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
