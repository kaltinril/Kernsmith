using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

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
        // Inner container — no scroll viewer needed; all content fits
        var inner = new ContainerRuntime();
        inner.Dock(Gum.Wireframe.Dock.Fill);
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
            btn.Height = 24;
            presetButtons.Add((btn, preset));

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

        AddDivider(stack);

        // --- FONT SOURCE section ---
        AddSectionHeader(stack, "FONT FILE");

        var browseBtn = new Button();
        browseBtn.Text = "Browse for Font...";
        browseBtn.Width = 230;
        browseBtn.Height = 28;
        browseBtn.Click += (_, _) =>
        {
            var dialog = new FileBrowserDialog();
            dialog.Show(path => _mainViewModel.LoadFontFromPath(path));
        };
        stack.Children.Add(browseBtn.Visual);

        var sourceLabel = new Label();
        sourceLabel.Text = "No font loaded";
        sourceLabel.SetBinding(nameof(Label.Text), nameof(FontConfigViewModel.FontSourceDescription));
        sourceLabel.Visual.BindingContext = _fontConfig;
        stack.Children.Add(sourceLabel.Visual);

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

        var familyCombo = new ComboBox();
        familyCombo.Width = 230;
        stack.Children.Add(familyCombo.Visual);

        var styleLabel = new Label();
        styleLabel.Text = "Style:";
        stack.Children.Add(styleLabel.Visual);

        var styleCombo = new ComboBox();
        styleCombo.Width = 230;
        stack.Children.Add(styleCombo.Visual);

        // Wire system font combos
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
                styleCombo.Items.Clear();
                foreach (var style in group.Styles)
                    styleCombo.Items.Add(style.StyleName);

                // Auto-select first style (typically "Regular") and trigger font load
                if (styleCombo.Items.Count > 0)
                    styleCombo.SelectedIndex = 0;
            }
        };

        styleCombo.SelectionChanged += (_, _) =>
        {
            if (styleCombo.SelectedIndex >= 0 && _fontConfig.SystemFonts != null
                && familyCombo.SelectedIndex >= 0)
            {
                var group = _fontConfig.SystemFonts[familyCombo.SelectedIndex];
                var font = group.Styles[styleCombo.SelectedIndex];
                _fontConfig.SelectedSystemFont = font;
                try
                {
                    _fontConfig.LoadFromSystem(font);
                }
                catch (Exception)
                {
                    // Will be handled by status bar
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

        var sizeTextBox = new TextBox();
        sizeTextBox.Width = 80;
        sizeTextBox.Height = 28;
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

        // --- CHARACTER SET section ---
        AddSectionHeader(stack, "CHARACTER SET");

        // RadioButtons so all options are visible at once
        var charSetGroup = new StackPanel();
        charSetGroup.Spacing = 2;
        stack.Children.Add(charSetGroup.Visual);

        TextBox? customTextBox = null;

        var presets = new[] { "ASCII (95)", "Extended ASCII (224)", "Latin (559)", "Custom" };
        for (int i = 0; i < presets.Length; i++)
        {
            var rb = new RadioButton();
            rb.Text = presets[i];
            rb.Width = 230;
            if (i == 0) rb.IsChecked = true;
            var presetIndex = i; // capture for closure
            rb.Checked += (_, _) =>
            {
                _fontConfig.SelectedPreset = (CharacterSetPreset)presetIndex;
                if (customTextBox != null)
                    customTextBox.IsVisible = _fontConfig.IsCustomMode;
            };
            charSetGroup.AddChild(rb);
        }

        customTextBox = new TextBox();
        customTextBox.Width = 230;
        customTextBox.Height = 60;
        customTextBox.IsVisible = false;
        customTextBox.Placeholder = "Type characters to include...";
        customTextBox.TextWrapping = Gum.Forms.TextWrapping.Wrap;
        customTextBox.AcceptsReturn = true;
        customTextBox.SetBinding(nameof(TextBox.Text), nameof(FontConfigViewModel.CustomCharacters));
        customTextBox.Visual.BindingContext = _fontConfig;
        stack.Children.Add(customTextBox.Visual);

        var charCountLabel = new Label();
        charCountLabel.Text = "Selected: 95 characters";
        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.CharacterCount))
                charCountLabel.Text = $"Selected: {_fontConfig.CharacterCount} characters";
        };
        stack.Children.Add(charCountLabel.Visual);

        AddDivider(stack);

        // --- GENERATE button (primary action, visually distinct) ---
        var generateBtnSpacer = new ColoredRectangleRuntime();
        generateBtnSpacer.Width = 0;
        generateBtnSpacer.WidthUnits = DimensionUnitType.RelativeToParent;
        generateBtnSpacer.Height = 4;
        generateBtnSpacer.Color = Microsoft.Xna.Framework.Color.Transparent;
        stack.Children.Add(generateBtnSpacer);

        var generateBtn = new Button();
        generateBtn.Text = ">> Generate <<";
        generateBtn.Width = 230;
        generateBtn.Height = 44;
        generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        generateBtn.Click += async (_, _) => await _mainViewModel.GenerateAsync();
        stack.Children.Add(generateBtn.Visual);

        // Enable/disable Generate button based on font loaded state
        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.IsFontLoaded))
                generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        };

        // Auto-regenerate toggle
        var autoRegenCb = new CheckBox();
        autoRegenCb.Text = "Auto-regenerate";
        autoRegenCb.Width = 230;
        autoRegenCb.Checked += (_, _) => _mainViewModel.AutoRegenerate = true;
        autoRegenCb.Unchecked += (_, _) => _mainViewModel.AutoRegenerate = false;
        stack.Children.Add(autoRegenCb.Visual);
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
