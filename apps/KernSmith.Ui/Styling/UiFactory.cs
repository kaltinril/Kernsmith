using global::Gum.DataTypes;
using global::Gum.Forms.Controls;
using KernSmith.Ui.Layout;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum.GueDeriving;

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
    /// Creates a grid row with label | color swatch | hex input for color selection.
    /// </summary>
    public static void AddColorRow(
        GraphicsDevice graphicsDevice,
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, string initialHex,
        Action<string> onColorChanged)
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
        grid.AddChild(lbl, row: 0, column: 0);

        var swatchContainer = new ContainerRuntime();
        swatchContainer.Width = 24;
        swatchContainer.Height = 24;
        swatchContainer.HasEvents = true;
        grid.AddChild(swatchContainer, row: 0, column: 1);

        var swatch = new ColoredRectangleRuntime();
        swatch.Width = 0;
        swatch.WidthUnits = DimensionUnitType.RelativeToParent;
        swatch.Height = 0;
        swatch.HeightUnits = DimensionUnitType.RelativeToParent;
        swatch.Color = new Color(defaultR, defaultG, defaultB);
        swatchContainer.Children.Add(swatch);

        var swatchBorder = new ColoredRectangleRuntime();
        swatchBorder.Width = 0;
        swatchBorder.WidthUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Height = 0;
        swatchBorder.HeightUnits = DimensionUnitType.RelativeToParent;
        swatchBorder.Color = Theme.PanelBorder;
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
                swatch.Color = new Color(r, g, b);
                onColorChanged($"#{r:X2}{g:X2}{b:X2}");
            }
        };
        grid.AddChild(hexBox, row: 0, column: 2);

        swatchContainer.Click += (_, _) =>
        {
            var currentColor = swatch.Color;
            ColorPickerDialog.Show(graphicsDevice, currentColor, newColor =>
            {
                var hex = $"#{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
                swatch.Color = newColor;
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
