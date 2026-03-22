using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class EffectsPanel : Panel
{
    private readonly EffectsViewModel _effects;

    public EffectsPanel(EffectsViewModel effects)
    {
        _effects = effects;
        BuildContent();
    }

    private void BuildContent()
    {
        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Dock(Gum.Wireframe.Dock.Fill);
        this.AddChild(scrollViewer);

        var stack = scrollViewer.InnerPanel;
        stack.StackSpacing = 4;

        // --- FONT STYLE section (always active) ---
        AddSectionHeader(stack, "FONT STYLE");

        // Bold + Italic row
        var styleRow = new StackPanel();
        styleRow.Orientation = Orientation.Horizontal;
        styleRow.Spacing = 12;
        stack.Children.Add(styleRow.Visual);

        var boldCheck = new CheckBox();
        boldCheck.Text = "Bold";
        boldCheck.Width = 80;
        boldCheck.Checked += (_, _) => _effects.Bold = true;
        boldCheck.Unchecked += (_, _) => _effects.Bold = false;
        styleRow.AddChild(boldCheck);

        var italicCheck = new CheckBox();
        italicCheck.Text = "Italic";
        italicCheck.Width = 80;
        italicCheck.Checked += (_, _) => _effects.Italic = true;
        italicCheck.Unchecked += (_, _) => _effects.Italic = false;
        styleRow.AddChild(italicCheck);

        // Anti-alias + Hinting row
        var aaRow = new StackPanel();
        aaRow.Orientation = Orientation.Horizontal;
        aaRow.Spacing = 12;
        stack.Children.Add(aaRow.Visual);

        var aaCheck = new CheckBox();
        aaCheck.Text = "Anti-Alias";
        aaCheck.Width = 100;
        aaCheck.IsChecked = true;
        aaCheck.Checked += (_, _) => _effects.AntiAlias = true;
        aaCheck.Unchecked += (_, _) => _effects.AntiAlias = false;
        aaRow.AddChild(aaCheck);

        var hintCheck = new CheckBox();
        hintCheck.Text = "Hinting";
        hintCheck.Width = 80;
        hintCheck.IsChecked = true;
        hintCheck.Checked += (_, _) => _effects.Hinting = true;
        hintCheck.Unchecked += (_, _) => _effects.Hinting = false;
        aaRow.AddChild(hintCheck);

        // Super sampling
        var ssLabel = new Label();
        ssLabel.Text = "Super Sample:";
        stack.Children.Add(ssLabel.Visual);

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
        }, enableChanged: enabled => _effects.OutlineEnabled = enabled);

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
        }, enableChanged: enabled => _effects.ShadowEnabled = enabled);

        AddDivider(stack);

        // --- ADVANCED section ---
        AddSectionHeader(stack, "ADVANCED");

        var sdfCheck = new CheckBox();
        sdfCheck.Text = "SDF (Signed Distance Field)";
        sdfCheck.Width = 220;
        sdfCheck.Checked += (_, _) => _effects.SdfEnabled = true;
        sdfCheck.Unchecked += (_, _) => _effects.SdfEnabled = false;
        stack.Children.Add(sdfCheck.Visual);

        var colorCheck = new CheckBox();
        colorCheck.Text = "Color Font";
        colorCheck.Width = 220;
        colorCheck.Checked += (_, _) => _effects.ColorFontEnabled = true;
        colorCheck.Unchecked += (_, _) => _effects.ColorFontEnabled = false;
        stack.Children.Add(colorCheck.Visual);
    }

    private static void AddCollapsibleSection(
        Gum.Wireframe.GraphicalUiElement parent,
        string title,
        Action<Gum.Wireframe.GraphicalUiElement> buildContent,
        Action<bool> enableChanged)
    {
        var enableCheck = new CheckBox();
        enableCheck.Text = title;
        enableCheck.Width = 220;
        parent.Children.Add(enableCheck.Visual);

        // Content container (initially hidden)
        var content = new StackPanel();
        content.Spacing = 4;
        content.Visual.X = 16; // indent
        content.IsVisible = false;
        parent.Children.Add(content.Visual);

        enableCheck.Checked += (_, _) =>
        {
            content.IsVisible = true;
            enableChanged(true);
        };
        enableCheck.Unchecked += (_, _) =>
        {
            content.IsVisible = false;
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
        divider.WidthUnits = DimensionUnitType.RelativeToParent;
        divider.Height = 1;
        divider.Color = new Microsoft.Xna.Framework.Color(60, 60, 60);
        parent.Children.Add(divider);
    }
}
