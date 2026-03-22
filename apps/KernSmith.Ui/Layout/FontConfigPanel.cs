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
        AddSectionHeader(stack, "FONT SOURCE");

        var openFileBtn = new Button();
        openFileBtn.Text = "Open File...";
        openFileBtn.Width = 260;
        openFileBtn.Height = 30;
        openFileBtn.Click += (_, _) => _mainViewModel.OpenFont();
        stack.Children.Add(openFileBtn.Visual);

        var sourceLabel = new Label();
        sourceLabel.Text = "No font loaded";
        sourceLabel.SetBinding(nameof(Label.Text), nameof(FontConfigViewModel.FontSourceDescription));
        sourceLabel.Visual.BindingContext = _fontConfig;
        stack.Children.Add(sourceLabel.Visual);

        // --- System font section ---
        AddSectionHeader(stack, "-- OR --");

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

        // --- FONT INFO section ---
        AddSectionHeader(stack, "FONT INFO");
        AddInfoRow(stack, "Family:", nameof(FontConfigViewModel.FamilyName));
        AddInfoRow(stack, "Style:", nameof(FontConfigViewModel.StyleName));
        AddInfoRow(stack, "Glyphs:", nameof(FontConfigViewModel.NumGlyphs));

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

        // --- CHARACTER SET section ---
        AddSectionHeader(stack, "CHARACTER SET");

        TextBox? customTextBox = null;

        var presetCombo = new ComboBox();
        presetCombo.Width = 260;
        presetCombo.Items.Add("ASCII (95)");
        presetCombo.Items.Add("Extended ASCII (224)");
        presetCombo.Items.Add("Latin (559)");
        presetCombo.Items.Add("Custom");
        presetCombo.SelectedIndex = 0;
        presetCombo.SelectionChanged += (_, _) =>
        {
            _fontConfig.SelectedPreset = (CharacterSetPreset)presetCombo.SelectedIndex;
            if (customTextBox != null)
                customTextBox.IsVisible = _fontConfig.IsCustomMode;
        };
        stack.Children.Add(presetCombo.Visual);

        customTextBox = new TextBox();
        customTextBox.Width = 260;
        customTextBox.Height = 28;
        customTextBox.IsVisible = false;
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

    private void AddInfoRow(Gum.Wireframe.GraphicalUiElement parent, string labelText, string bindingProperty)
    {
        var row = new StackPanel();
        row.Orientation = Orientation.Horizontal;
        row.Spacing = 4;
        parent.Children.Add(row.Visual);

        var lbl = new Label();
        lbl.Text = labelText;
        lbl.Width = 80;
        row.AddChild(lbl);

        var valueLabel = new Label();
        valueLabel.Text = "";
        valueLabel.SetBinding(nameof(Label.Text), bindingProperty);
        valueLabel.Visual.BindingContext = _fontConfig;
        row.AddChild(valueLabel);
    }
}
