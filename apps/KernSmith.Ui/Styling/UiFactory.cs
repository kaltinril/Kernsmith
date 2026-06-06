using global::Gum.DataTypes;
using global::Gum.Forms.Controls;
using KernSmith.Ui.Layout;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Gum.GueDeriving;

namespace KernSmith.Ui.Styling;

/// <summary>
/// Shared UI factory methods for creating consistent controls across all panels.
/// </summary>
public static class UiFactory
{
    /// <summary>
    /// Creates an Expander with the standard content indent applied.
    /// </summary>
    public static Expander CreateExpander(string header, bool isExpanded = true)
    {
        var expander = new Expander();
        expander.Header = header;
        expander.IsExpanded = isExpanded;

        var contentContainer = expander.Visual.GetGraphicalUiElementByName("ContentContainer");
        if (contentContainer != null)
        {
            contentContainer.X = Theme.ExpanderContentIndent;
            contentContainer.Width = -Theme.ExpanderContentIndent;
        }

        return expander;
    }

    /// <summary>
    /// Creates a plain text section header (no background).
    /// </summary>
    public static void AddSectionHeader(global::Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        var header = new TextRuntime();
        header.Text = text;
        header.Color = Theme.SectionHeaderText;
        parent.Children.Add(header);
    }

    /// <summary>
    /// Creates a grid row with label | slider | value display for integer ranges.
    /// </summary>
    public static Slider AddSliderRow(
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, int min, int max, int defaultVal,
        Action<int> onChanged)
    {
        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        parent.Children.Add(grid.Visual);

        var lbl = new Label();
        lbl.Text = label;
        grid.AddChild(lbl, row: 0, column: 0);

        var slider = new Slider();
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = defaultVal;
        slider.Width = 100;
        slider.TicksFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        grid.AddChild(slider, row: 0, column: 1);

        var valueLabel = new Label();
        valueLabel.Text = defaultVal.ToString();
        grid.AddChild(valueLabel, row: 0, column: 2);

        slider.ValueChanged += (_, _) =>
        {
            var val = (int)slider.Value;
            valueLabel.Text = val.ToString();
            onChanged(val);
        };

        return slider;
    }

    /// <summary>
    /// Creates a grid row with label | slider | live value label for a bounded float range.
    /// The underlying Gum slider is integer-snapped; values are mapped to floats by dividing the
    /// raw slider value by <paramref name="steps"/> (e.g. min=1, max=3, steps=10 yields 1.0..3.0
    /// in 0.1 increments). Use this for discoverable, bounded float params (Gamma, SdfSpread).
    /// </summary>
    public static Slider AddFloatSliderRow(
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, float min, float max, float defaultVal, int steps,
        Action<float> onChanged)
    {
        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        parent.Children.Add(grid.Visual);

        var lbl = new Label();
        lbl.Text = label;
        grid.AddChild(lbl, row: 0, column: 0);

        var slider = new Slider();
        slider.Minimum = min * steps;
        slider.Maximum = max * steps;
        slider.Value = defaultVal * steps;
        slider.Width = 100;
        slider.TicksFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        grid.AddChild(slider, row: 0, column: 1);

        var valueLabel = new Label();
        valueLabel.Text = defaultVal.ToString("0.0#");
        grid.AddChild(valueLabel, row: 0, column: 2);

        slider.ValueChanged += (_, _) =>
        {
            var val = (float)(slider.Value / steps);
            valueLabel.Text = val.ToString("0.0#");
            onChanged(val);
        };

        return slider;
    }

    /// <summary>
    /// Creates a label : float-text-box row for a precise/unbounded float value.
    /// Mirrors the two-column rhythm of <see cref="AddSliderRow"/> using a fixed-width label.
    /// </summary>
    public static TextBox AddFloatBoxRow(
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, float initialValue,
        Action<float> onChanged)
    {
        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        parent.Children.Add(grid.Visual);

        var lbl = new Label();
        lbl.Text = label;
        grid.AddChild(lbl, row: 0, column: 0);

        var box = CreateSmallFloatBox(initialValue, onChanged);
        grid.AddChild(box, row: 0, column: 1);

        return box;
    }

    /// <summary>
    /// Creates a small text box for a precise/unbounded float value, parsing on edit.
    /// Use this for params that benefit from exact entry (advance adjust, gradient offset/scale).
    /// </summary>
    public static TextBox CreateSmallFloatBox(float initialValue, Action<float> onChanged)
    {
        var box = new TextBox();
        box.Width = 56;
        box.Height = 24;
        box.Text = initialValue.ToString("0.0###");
        box.TextChanged += (_, _) =>
        {
            if (float.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out var val))
                onChanged(val);
        };
        return box;
    }

    /// <summary>
    /// Creates a grid row with label | color swatch | hex input for color selection.
    /// </summary>
    public static void AddColorRow(
        GraphicsDevice graphicsDevice,
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, string initialHex,
        Action<string> onColorChanged,
        string? tooltip = null)
    {
        var (defaultR, defaultG, defaultB) = EffectsViewModel.ParseHex(initialHex);

        var grid = new Grid();
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = 0;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Theme.LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        parent.Children.Add(grid.Visual);

        var lbl = new Label();
        lbl.Text = label;
        if (tooltip != null) TooltipService.SetTooltip(lbl, tooltip);
        grid.AddChild(lbl, row: 0, column: 0);

        var swatchContainer = new ContainerRuntime();
        swatchContainer.Width = 24;
        swatchContainer.Height = 24;
        swatchContainer.HasEvents = true;
        grid.AddChild(swatchContainer, row: 0, column: 1);

        var swatch = new RectangleRuntime();
        swatch.IsFilled = true;
        swatch.Width = 0;
        swatch.WidthUnits = DimensionUnitType.RelativeToParent;
        swatch.Height = 0;
        swatch.HeightUnits = DimensionUnitType.RelativeToParent;
        swatch.FillColor = new Color(defaultR, defaultG, defaultB);
        swatchContainer.Children.Add(swatch);

        var swatchBorder = new RectangleRuntime();
        swatchBorder.IsFilled = true;
        swatchBorder.Width = 0;
        swatchBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Height = 0;
        swatchBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.FillColor = Theme.PanelBorder;
        swatchContainer.Children.Insert(0, swatchBorder);

        var hexBox = new TextBox();
        hexBox.Width = 80;
        hexBox.Text = $"#{defaultR:X2}{defaultG:X2}{defaultB:X2}";
        TooltipService.SetTooltip(hexBox, "Hex color (e.g., #FF0000)");
        var suppressHexSync = false;

        hexBox.TextChanged += (_, _) =>
        {
            if (suppressHexSync) return;
            var hex = hexBox.Text?.Trim() ?? "";
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                swatch.FillColor = new Color(r, g, b);
                onColorChanged($"#{r:X2}{g:X2}{b:X2}");
            }
        };
        grid.AddChild(hexBox, row: 0, column: 2);

        swatchContainer.Click += (_, _) =>
        {
            var currentColor = swatch.FillColor;
            ColorPickerDialog.Show(graphicsDevice, currentColor, newColor =>
            {
                var hex = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
                swatch.FillColor = newColor;
                suppressHexSync = true;
                hexBox.Text = hex;
                suppressHexSync = false;
                onColorChanged(hex);
            });
        };
    }

    /// <summary>
    /// Creates a ScrollViewer with an inner stacked container, applying standard panel padding.
    /// Returns both so callers can add children to the inner container.
    /// </summary>
    public static (ScrollViewer scrollViewer, ContainerRuntime inner) CreateScrollablePanel(Panel panel)
    {
        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Dock(global::Gum.Wireframe.Dock.Fill);

        // Make ScrollViewer borderless and remove clip container margins
        if (scrollViewer.Visual is global::Gum.Forms.DefaultVisuals.V3.ScrollViewerVisual scrollVisual)
        {
            scrollVisual.Background.ApplyState(global::Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle.NineSlice.Solid);
            scrollVisual.BackgroundColor = Theme.Panel;
            FontConfigPanel.StripScrollViewerMargins(scrollVisual);
        }

        panel.AddChild(scrollViewer);

        var inner = new ContainerRuntime();
        inner.WidthUnits = DimensionUnitType.RelativeToParent;
        inner.HeightUnits = DimensionUnitType.RelativeToChildren;
        inner.Width = 0;
        inner.Height = 0;
        inner.X = 0;
        inner.Y = 0;
        inner.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.StackSpacing = Theme.SectionSpacing;
        scrollViewer.InnerPanel.Children.Add(inner);

        return (scrollViewer, inner);
    }
}
