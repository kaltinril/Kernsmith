using Gum.DataTypes;
using Gum.Forms.Controls;
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
    /// Creates a section header with background bar and accent-colored text.
    /// </summary>
    public static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        var container = new ContainerRuntime();
        container.Width = 0;
        container.WidthUnits = DimensionUnitType.RelativeToParent;
        container.Height = Theme.SectionHeaderHeight;
        container.HeightUnits = DimensionUnitType.Absolute;
        parent.Children.Add(container);

        var bg = new ColoredRectangleRuntime();
        bg.Width = 0;
        bg.WidthUnits = DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = DimensionUnitType.RelativeToParent;
        bg.Color = Theme.SectionHeaderBg;
        container.Children.Add(bg);

        var header = new TextRuntime();
        header.Text = text;
        header.Color = Theme.Accent;
        header.X = 6;
        header.Y = 4;
        container.Children.Add(header);
    }

    /// <summary>
    /// Creates a collapsible section with a clickable header (chevron + title) that toggles
    /// content visibility. Used for grouping controls that don't need an enable/disable checkbox.
    /// </summary>
    /// <param name="parent">Parent container to add the header and content to.</param>
    /// <param name="title">Section title text.</param>
    /// <param name="buildContent">Callback that populates the content container.</param>
    /// <param name="startExpanded">Whether the section starts expanded (default: true).</param>
    /// <returns>The content container, in case the caller needs to toggle visibility externally.</returns>
    public static ContainerRuntime AddCollapsibleHeader(
        Gum.Wireframe.GraphicalUiElement parent,
        string title,
        Action<Gum.Wireframe.GraphicalUiElement> buildContent,
        bool startExpanded = true)
    {
        // Header bar (clickable)
        var headerContainer = new ContainerRuntime();
        headerContainer.Width = 0;
        headerContainer.WidthUnits = DimensionUnitType.RelativeToParent;
        headerContainer.Height = Theme.SectionHeaderHeight;
        headerContainer.HeightUnits = DimensionUnitType.Absolute;
        headerContainer.HasEvents = true;
        parent.Children.Add(headerContainer);

        var bg = new ColoredRectangleRuntime();
        bg.Width = 0;
        bg.WidthUnits = DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = DimensionUnitType.RelativeToParent;
        bg.Color = Theme.SectionHeaderBg;
        headerContainer.Children.Add(bg);

        var chevron = new TextRuntime();
        chevron.Text = startExpanded ? "v" : ">";
        chevron.Color = Theme.TextMuted;
        chevron.X = 6;
        chevron.Y = 4;
        headerContainer.Children.Add(chevron);

        var headerText = new TextRuntime();
        headerText.Text = title;
        headerText.Color = Theme.Accent;
        headerText.X = 20;
        headerText.Y = 4;
        headerContainer.Children.Add(headerText);

        // Content container with subtle background and indent
        var contentWrapper = new ContainerRuntime();
        contentWrapper.X = Theme.PanelPadding;
        contentWrapper.Width = -Theme.PanelPadding;
        contentWrapper.WidthUnits = DimensionUnitType.RelativeToParent;
        contentWrapper.HeightUnits = DimensionUnitType.RelativeToChildren;
        contentWrapper.Height = Theme.PanelPadding;
        contentWrapper.Visible = startExpanded;
        parent.Children.Add(contentWrapper);

        var contentBg = new ColoredRectangleRuntime();
        contentBg.Width = 0;
        contentBg.WidthUnits = DimensionUnitType.RelativeToParent;
        contentBg.Height = 0;
        contentBg.HeightUnits = DimensionUnitType.RelativeToParent;
        contentBg.Color = Theme.CollapsibleContentBg;
        contentWrapper.Children.Add(contentBg);

        var content = new StackPanel();
        content.Spacing = Theme.ControlSpacing;
        content.Visual.X = Theme.ControlSpacing;
        content.Visual.Y = Theme.ControlSpacing;
        content.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        content.Visual.Width = -(Theme.ControlSpacing * 2);
        content.IsVisible = true;
        contentWrapper.Children.Add(content.Visual);

        // Toggle on click
        headerContainer.Click += (_, _) =>
        {
            contentWrapper.Visible = !contentWrapper.Visible;
            chevron.Text = contentWrapper.Visible ? "v" : ">";
        };

        buildContent(content.Visual);

        return contentWrapper;
    }

    /// <summary>
    /// Creates a 1px horizontal divider line.
    /// </summary>
    public static void AddDivider(Gum.Wireframe.GraphicalUiElement parent)
    {
        var divider = new ColoredRectangleRuntime();
        divider.Width = 0;
        divider.WidthUnits = DimensionUnitType.RelativeToParent;
        divider.Height = 1;
        divider.Color = Theme.PanelBorder;
        parent.Children.Add(divider);
    }

    /// <summary>
    /// Creates a collapsible section with a checkbox header that toggles content visibility
    /// and fires an enable/disable callback. Used for effect sections (outline, shadow, etc.).
    /// </summary>
    public static void AddCollapsibleSection(
        Gum.Wireframe.GraphicalUiElement parent,
        string title,
        Action<Gum.Wireframe.GraphicalUiElement> buildContent,
        Action<bool> enableChanged,
        bool startExpanded = false,
        string? tooltip = null)
    {
        // Header bar with checkbox
        var headerContainer = new ContainerRuntime();
        headerContainer.Width = 0;
        headerContainer.WidthUnits = DimensionUnitType.RelativeToParent;
        headerContainer.Height = Theme.SectionHeaderHeight;
        headerContainer.HeightUnits = DimensionUnitType.Absolute;
        parent.Children.Add(headerContainer);

        var headerBg = new ColoredRectangleRuntime();
        headerBg.Width = 0;
        headerBg.WidthUnits = DimensionUnitType.RelativeToParent;
        headerBg.Height = 0;
        headerBg.HeightUnits = DimensionUnitType.RelativeToParent;
        headerBg.Color = Theme.SectionHeaderBg;
        headerContainer.Children.Add(headerBg);

        var enableCheck = new CheckBox();
        enableCheck.Text = title;
        enableCheck.Width = 220;
        enableCheck.Visual.X = 6;
        enableCheck.Visual.Y = 2;
        headerContainer.Children.Add(enableCheck.Visual);
        if (tooltip != null)
            TooltipService.SetTooltip(enableCheck, tooltip);

        var contentWrapper = new ContainerRuntime();
        contentWrapper.X = Theme.PanelPadding;
        contentWrapper.Width = -Theme.PanelPadding;
        contentWrapper.WidthUnits = DimensionUnitType.RelativeToParent;
        contentWrapper.HeightUnits = DimensionUnitType.RelativeToChildren;
        contentWrapper.Height = Theme.PanelPadding;
        contentWrapper.Visible = startExpanded;
        parent.Children.Add(contentWrapper);

        var contentBg = new ColoredRectangleRuntime();
        contentBg.Width = 0;
        contentBg.WidthUnits = DimensionUnitType.RelativeToParent;
        contentBg.Height = 0;
        contentBg.HeightUnits = DimensionUnitType.RelativeToParent;
        contentBg.Color = Theme.CollapsibleContentBg;
        contentWrapper.Children.Add(contentBg);

        var content = new StackPanel();
        content.Spacing = Theme.ControlSpacing;
        content.Visual.X = Theme.ControlSpacing;
        content.Visual.Y = Theme.ControlSpacing;
        content.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        content.Visual.Width = -(Theme.ControlSpacing * 2);
        content.IsVisible = true;
        contentWrapper.Children.Add(content.Visual);

        if (startExpanded)
            enableCheck.IsChecked = true;

        enableCheck.Checked += (_, _) =>
        {
            contentWrapper.Visible = true;
            enableChanged(true);
        };
        enableCheck.Unchecked += (_, _) =>
        {
            contentWrapper.Visible = false;
            enableChanged(false);
        };

        buildContent(content.Visual);
    }

    /// <summary>
    /// Creates a grid row with label | slider | value display for integer ranges.
    /// </summary>
    public static Slider AddSliderRow(
        Gum.Wireframe.GraphicalUiElement parent,
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
        Gum.Wireframe.GraphicalUiElement parent,
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
        scrollViewer.Dock(Gum.Wireframe.Dock.Fill);

        // Make ScrollViewer background transparent so the panel background color shows through
        var scrollBg = scrollViewer.Visual.GetGraphicalUiElementByName("Background");
        if (scrollBg is ColoredRectangleRuntime scrollRect)
            scrollRect.Color = Color.Transparent;

        panel.AddChild(scrollViewer);

        var inner = new ContainerRuntime();
        inner.WidthUnits = DimensionUnitType.RelativeToParent;
        inner.HeightUnits = DimensionUnitType.RelativeToChildren;
        inner.Width = -(Theme.PanelPadding * 2);
        inner.Height = 0;
        inner.X = Theme.PanelPadding;
        inner.Y = Theme.ControlSpacing;
        inner.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.StackSpacing = Theme.SectionSpacing;
        scrollViewer.InnerPanel.Children.Add(inner);

        return (scrollViewer, inner);
    }
}
