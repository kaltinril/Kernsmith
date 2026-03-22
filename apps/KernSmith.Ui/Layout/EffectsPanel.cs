using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Styling;
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

        var boldCheck = new CheckBox();
        boldCheck.Text = "Bold";
        boldCheck.Width = 200;
        boldCheck.Checked += (_, _) => _effects.Bold = true;
        boldCheck.Unchecked += (_, _) => _effects.Bold = false;
        stack.Children.Add(boldCheck.Visual);

        var italicCheck = new CheckBox();
        italicCheck.Text = "Italic";
        italicCheck.Width = 200;
        italicCheck.Checked += (_, _) => _effects.Italic = true;
        italicCheck.Unchecked += (_, _) => _effects.Italic = false;
        stack.Children.Add(italicCheck.Visual);

        var aaCheck = new CheckBox();
        aaCheck.Text = "Anti-Alias";
        aaCheck.Width = 200;
        aaCheck.IsChecked = true;
        aaCheck.Checked += (_, _) => _effects.AntiAlias = true;
        aaCheck.Unchecked += (_, _) => _effects.AntiAlias = false;
        stack.Children.Add(aaCheck.Visual);

        var hintCheck = new CheckBox();
        hintCheck.Text = "Hinting";
        hintCheck.Width = 200;
        hintCheck.IsChecked = true;
        hintCheck.Checked += (_, _) => _effects.Hinting = true;
        hintCheck.Unchecked += (_, _) => _effects.Hinting = false;
        stack.Children.Add(hintCheck.Visual);

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

            // Outline color RGB row
            AddColorRow(contentPanel, "Color:",
                _effects.OutlineColorR, _effects.OutlineColorG, _effects.OutlineColorB,
                v => _effects.OutlineColorR = v,
                v => _effects.OutlineColorG = v,
                v => _effects.OutlineColorB = v);
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

            // Shadow color RGB row
            AddColorRow(contentPanel, "Color:",
                _effects.ShadowColorR, _effects.ShadowColorG, _effects.ShadowColorB,
                v => _effects.ShadowColorR = v,
                v => _effects.ShadowColorG = v,
                v => _effects.ShadowColorB = v);

            // Shadow opacity slider
            AddSliderRow(contentPanel, "Opacity:", 0, 100, 100,
                val => _effects.ShadowOpacity = val);
        }, enableChanged: enabled => _effects.ShadowEnabled = enabled);

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
        }, enableChanged: enabled => _effects.GradientEnabled = enabled);

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
        }, enableChanged: _ => { });

        AddDivider(stack);

        // --- ADVANCED section ---
        AddSectionHeader(stack, "ADVANCED");

        var sdfCheck = new CheckBox();
        sdfCheck.Text = "SDF";
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

        var fallbackTextBox = new TextBox();
        fallbackTextBox.Width = 60;
        fallbackTextBox.Height = 28;
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

        var rBox = new TextBox();
        rBox.Width = 40;
        rBox.Height = 28;
        rBox.Text = defaultR.ToString();
        rBox.TextChanged += (_, _) =>
        {
            if (byte.TryParse(rBox.Text, out var val))
                onRChanged(val);
        };
        row.AddChild(rBox);

        var gBox = new TextBox();
        gBox.Width = 40;
        gBox.Height = 28;
        gBox.Text = defaultG.ToString();
        gBox.TextChanged += (_, _) =>
        {
            if (byte.TryParse(gBox.Text, out var val))
                onGChanged(val);
        };
        row.AddChild(gBox);

        var bBox = new TextBox();
        bBox.Width = 40;
        bBox.Height = 28;
        bBox.Text = defaultB.ToString();
        bBox.TextChanged += (_, _) =>
        {
            if (byte.TryParse(bBox.Text, out var val))
                onBChanged(val);
        };
        row.AddChild(bBox);
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
