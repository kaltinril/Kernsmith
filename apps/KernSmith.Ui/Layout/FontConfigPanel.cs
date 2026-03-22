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
        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Dock(Gum.Wireframe.Dock.Fill);
        this.AddChild(scrollViewer);

        var stack = scrollViewer.InnerPanel;
        stack.StackSpacing = 6;

        // --- ENGINE PRESET section ---
        AddSectionHeader(stack, "ENGINE PRESET");

        var presetRow = new StackPanel();
        presetRow.Orientation = Orientation.Horizontal;
        presetRow.Spacing = 3;
        presetRow.Visual.WrapsChildren = true;
        stack.Children.Add(presetRow.Visual);

        var presetDescLabel = new Label();
        presetDescLabel.Text = "";
        presetDescLabel.IsVisible = false;

        foreach (var preset in EnginePresets.All)
        {
            if (preset == EnginePresets.Custom) continue; // Custom is the default / no-preset state

            var btn = new Button();
            btn.Text = preset.Name;
            btn.Width = 65;
            btn.Height = 26;
            var capturedPreset = preset;
            btn.Click += (_, _) =>
            {
                _atlasConfig.ApplyPreset(capturedPreset);
                presetDescLabel.Text = capturedPreset.Description;
                presetDescLabel.IsVisible = true;
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

        AddDivider(stack);

        // --- ATLAS section ---
        AddSectionHeader(stack, "ATLAS");

        var maxSizeLabel = new Label();
        maxSizeLabel.Text = "Max Size:";
        stack.Children.Add(maxSizeLabel.Visual);

        var maxSizeRow = new StackPanel();
        maxSizeRow.Orientation = Orientation.Horizontal;
        maxSizeRow.Spacing = 4;
        stack.Children.Add(maxSizeRow.Visual);

        var maxWidthBox = new TextBox();
        maxWidthBox.Width = 80;
        maxWidthBox.Height = 28;
        maxWidthBox.Text = _atlasConfig.MaxWidth.ToString();
        maxWidthBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxWidthBox.Text, out var w))
                _atlasConfig.MaxWidth = Math.Clamp(w, 64, 8192);
        };
        maxSizeRow.AddChild(maxWidthBox);

        var xLabel = new Label();
        xLabel.Text = "x";
        maxSizeRow.AddChild(xLabel);

        var maxHeightBox = new TextBox();
        maxHeightBox.Width = 80;
        maxHeightBox.Height = 28;
        maxHeightBox.Text = _atlasConfig.MaxHeight.ToString();
        maxHeightBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(maxHeightBox.Text, out var h))
                _atlasConfig.MaxHeight = Math.Clamp(h, 64, 8192);
        };
        maxSizeRow.AddChild(maxHeightBox);

        var pot = new CheckBox();
        pot.Text = "Power of Two";
        pot.IsChecked = _atlasConfig.PowerOfTwo;
        pot.Checked += (_, _) => _atlasConfig.PowerOfTwo = true;
        pot.Unchecked += (_, _) => _atlasConfig.PowerOfTwo = false;
        stack.Children.Add(pot.Visual);

        var autofit = new CheckBox();
        autofit.Text = "Autofit Texture";
        autofit.IsChecked = _atlasConfig.AutofitTexture;
        autofit.Checked += (_, _) => _atlasConfig.AutofitTexture = true;
        autofit.Unchecked += (_, _) => _atlasConfig.AutofitTexture = false;
        stack.Children.Add(autofit.Visual);

        var packAlgoLabel = new Label();
        packAlgoLabel.Text = "Packing Algorithm:";
        stack.Children.Add(packAlgoLabel.Visual);

        var packAlgoCombo = new ComboBox();
        packAlgoCombo.Width = 230;
        packAlgoCombo.Items.Add("MaxRects");
        packAlgoCombo.Items.Add("Skyline");
        packAlgoCombo.SelectedIndex = _atlasConfig.PackingAlgorithmIndex;
        packAlgoCombo.SelectionChanged += (_, _) =>
        {
            if (packAlgoCombo.SelectedIndex >= 0)
                _atlasConfig.PackingAlgorithmIndex = packAlgoCombo.SelectedIndex;
        };
        stack.Children.Add(packAlgoCombo.Visual);

        AddDivider(stack);

        // --- PADDING section ---
        AddSectionHeader(stack, "PADDING");

        var padTopRow = new StackPanel();
        padTopRow.Orientation = Orientation.Horizontal;
        padTopRow.Spacing = 4;
        stack.Children.Add(padTopRow.Visual);

        AddLabeledIntBox(padTopRow, "Up:", _atlasConfig.PaddingUp, 45, v => _atlasConfig.PaddingUp = Math.Clamp(v, 0, 32));
        AddLabeledIntBox(padTopRow, "Right:", _atlasConfig.PaddingRight, 45, v => _atlasConfig.PaddingRight = Math.Clamp(v, 0, 32));

        var padBotRow = new StackPanel();
        padBotRow.Orientation = Orientation.Horizontal;
        padBotRow.Spacing = 4;
        stack.Children.Add(padBotRow.Visual);

        AddLabeledIntBox(padBotRow, "Down:", _atlasConfig.PaddingDown, 45, v => _atlasConfig.PaddingDown = Math.Clamp(v, 0, 32));
        AddLabeledIntBox(padBotRow, "Left:", _atlasConfig.PaddingLeft, 45, v => _atlasConfig.PaddingLeft = Math.Clamp(v, 0, 32));

        AddDivider(stack);

        // --- SPACING section ---
        AddSectionHeader(stack, "SPACING");

        var spacingRow = new StackPanel();
        spacingRow.Orientation = Orientation.Horizontal;
        spacingRow.Spacing = 4;
        stack.Children.Add(spacingRow.Visual);

        AddLabeledIntBox(spacingRow, "H:", _atlasConfig.SpacingH, 45, v => _atlasConfig.SpacingH = Math.Clamp(v, 0, 32));
        AddLabeledIntBox(spacingRow, "V:", _atlasConfig.SpacingV, 45, v => _atlasConfig.SpacingV = Math.Clamp(v, 0, 32));

        AddDivider(stack);

        // --- OUTPUT section ---
        AddSectionHeader(stack, "OUTPUT");

        var formatLabel = new Label();
        formatLabel.Text = "Descriptor Format:";
        stack.Children.Add(formatLabel.Visual);

        var formatGroup = new StackPanel();
        formatGroup.Spacing = 2;
        stack.Children.Add(formatGroup.Visual);

        var formats = new[] { ("Text", OutputFormat.Text), ("XML", OutputFormat.Xml), ("Binary", OutputFormat.Binary) };
        foreach (var (name, format) in formats)
        {
            var rb = new RadioButton();
            rb.Text = name;
            rb.Width = 230;
            if (format == _atlasConfig.DescriptorFormat) rb.IsChecked = true;
            var capturedFormat = format;
            rb.Checked += (_, _) => _atlasConfig.DescriptorFormat = capturedFormat;
            formatGroup.AddChild(rb);
        }

        var kerningCb = new CheckBox();
        kerningCb.Text = "Include Kerning";
        kerningCb.IsChecked = _atlasConfig.IncludeKerning;
        kerningCb.Checked += (_, _) => _atlasConfig.IncludeKerning = true;
        kerningCb.Unchecked += (_, _) => _atlasConfig.IncludeKerning = false;
        stack.Children.Add(kerningCb.Visual);
    }

    private static void AddLabeledIntBox(StackPanel parent, string label, int initialValue, int width, Action<int> onChanged)
    {
        var lbl = new Label();
        lbl.Text = label;
        parent.AddChild(lbl);

        var box = new TextBox();
        box.Width = width;
        box.Height = 28;
        box.Text = initialValue.ToString();
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var val))
                onChanged(val);
        };
        parent.AddChild(box);
    }

    private static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        // Top spacer for consistent section separation
        var spacer = new ColoredRectangleRuntime();
        spacer.Width = 0;
        spacer.WidthUnits = DimensionUnitType.RelativeToParent;
        spacer.Height = 4;
        spacer.Color = Microsoft.Xna.Framework.Color.Transparent;
        parent.Children.Add(spacer);

        // Use TextRuntime directly so we can set color for section headers
        var header = new TextRuntime();
        header.Text = text;
        header.Color = Theme.Accent;
        parent.Children.Add(header);
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
