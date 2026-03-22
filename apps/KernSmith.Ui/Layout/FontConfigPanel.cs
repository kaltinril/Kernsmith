using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Models;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class FontConfigPanel : Panel
{
    private readonly MainViewModel _mainViewModel;
    private readonly FontConfigViewModel _fontConfig;

    public FontConfigPanel(MainViewModel mainViewModel, FontConfigViewModel fontConfig)
    {
        _mainViewModel = mainViewModel;
        _fontConfig = fontConfig;

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

        // --- FONT SOURCE section ---
        AddSectionHeader(stack, "FONT FILE");

        var browseBtn = new Button();
        browseBtn.Text = "Browse for Font...";
        browseBtn.Width = 260;
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

        AddDivider(stack);

        var familyLabel = new Label();
        familyLabel.Text = "System Font:";
        stack.Children.Add(familyLabel.Visual);

        var familyCombo = new ComboBox();
        familyCombo.Width = 260;
        stack.Children.Add(familyCombo.Visual);

        var styleLabel = new Label();
        styleLabel.Text = "Style:";
        stack.Children.Add(styleLabel.Visual);

        var styleCombo = new ComboBox();
        styleCombo.Width = 260;
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
            rb.Width = 260;
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
        customTextBox.Width = 260;
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

        // --- GENERATE button ---
        var generateBtn = new Button();
        generateBtn.Text = "Generate";
        generateBtn.Width = 260;
        generateBtn.Height = 40;
        generateBtn.Click += async (_, _) => await _mainViewModel.GenerateAsync();
        stack.Children.Add(generateBtn.Visual);
    }

    private static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        var label = new Label();
        label.Text = text;
        parent.Children.Add(label.Visual);
    }

    private static void AddDivider(Gum.Wireframe.GraphicalUiElement parent)
    {
        var divider = new ColoredRectangleRuntime();
        divider.Width = 0;
        divider.WidthUnits = Gum.DataTypes.DimensionUnitType.RelativeToParent;
        divider.Height = 1;
        divider.Color = new Microsoft.Xna.Framework.Color(60, 60, 60);
        parent.Children.Add(divider);
    }
}
