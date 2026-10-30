using System.ComponentModel;
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
/// Helpers that bind a control to a view-model property name the control's visual after that
/// property, so tests and Gum layout exports can find it.
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
    /// Creates a grid row with label | slider | value display for integer ranges, bound to a
    /// view-model property: it shows <paramref name="getValue"/> now and whenever
    /// <paramref name="source"/> raises <paramref name="propertyName"/> (e.g. after Load Project),
    /// and writes the user's changes to <paramref name="setValue"/>. Showing a value never writes
    /// it back, and a value the slider already shows (mid-drag) is left alone.
    /// </summary>
    public static Slider AddSliderRow(
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, int min, int max,
        INotifyPropertyChanged source, string propertyName,
        Func<int> getValue, Action<int> setValue)
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
        slider.Visual.Name = propertyName;
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Width = 100;
        slider.TicksFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        grid.AddChild(slider, row: 0, column: 1);

        var valueLabel = new Label();
        grid.AddChild(valueLabel, row: 0, column: 2);

        var showing = false;
        void Show()
        {
            var value = getValue();
            valueLabel.Text = value.ToString();
            if ((int)slider.Value == value) return;
            showing = true;
            try { slider.Value = value; }
            finally { showing = false; }
        }

        slider.ValueChanged += (_, _) =>
        {
            if (showing) return;
            var val = (int)slider.Value;
            valueLabel.Text = val.ToString();
            setValue(val);
        };
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName) Show();
        };
        Show();

        return slider;
    }

    /// <summary>
    /// Creates a grid row with label | slider | live value label for a bounded float range.
    /// The underlying Gum slider is integer-snapped; values are mapped to floats by dividing the
    /// raw slider value by <paramref name="steps"/> (e.g. min=1, max=3, steps=10 yields 1.0..3.0
    /// in 0.1 increments). Use this for discoverable, bounded float params (Gamma, SdfSpread).
    /// Bound to a view-model property like <see cref="AddSliderRow"/>; the label shows the
    /// property's exact value even where the slider can only show the nearest step.
    /// </summary>
    public static Slider AddFloatSliderRow(
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label, float min, float max, int steps,
        INotifyPropertyChanged source, string propertyName,
        Func<float> getValue, Action<float> setValue)
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
        slider.Visual.Name = propertyName;
        slider.Minimum = min * steps;
        slider.Maximum = max * steps;
        slider.Width = 100;
        slider.TicksFrequency = 1;
        slider.IsSnapToTickEnabled = true;
        grid.AddChild(slider, row: 0, column: 1);

        var valueLabel = new Label();
        grid.AddChild(valueLabel, row: 0, column: 2);

        var showing = false;
        void Show()
        {
            var value = getValue();
            valueLabel.Text = value.ToString("0.0#");
            var tick = Math.Round(value * steps);
            if (Math.Round(slider.Value) == tick) return;
            showing = true;
            try { slider.Value = tick; }
            finally { showing = false; }
        }

        slider.ValueChanged += (_, _) =>
        {
            if (showing) return;
            var val = (float)(slider.Value / steps);
            valueLabel.Text = val.ToString("0.0#");
            setValue(val);
        };
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName) Show();
        };
        Show();

        return slider;
    }

    /// <summary>
    /// Creates a label : float-text-box row for a precise/unbounded float value.
    /// Mirrors the two-column rhythm of <see cref="AddSliderRow"/> using a fixed-width label.
    /// See <see cref="CreateSmallFloatBox"/> for the binding parameters.
    /// </summary>
    public static TextBox AddFloatBoxRow(
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label,
        INotifyPropertyChanged source, string propertyName,
        Func<float> getValue, Action<float> setValue)
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

        var box = CreateSmallFloatBox(source, propertyName, getValue, setValue);
        grid.AddChild(box, row: 0, column: 1);

        return box;
    }

    /// <summary>
    /// Creates a small text box for a precise/unbounded float value, parsing on edit.
    /// Use this for params that benefit from exact entry (advance adjust, gradient offset/scale).
    /// The box shows <paramref name="getValue"/> and writes parsed edits to <paramref name="setValue"/>.
    /// </summary>
    /// <param name="source">The view model that owns the value.</param>
    /// <param name="propertyName">The property <paramref name="source"/> raises when the value changes.</param>
    /// <param name="getValue">Reads the value.</param>
    /// <param name="setValue">Writes a parsed edit.</param>
    public static TextBox CreateSmallFloatBox(
        INotifyPropertyChanged source, string propertyName,
        Func<float> getValue, Action<float> setValue)
    {
        var box = new TextBox();
        box.Visual.Name = propertyName;
        box.Width = 56;
        box.Height = 24;
        box.Text = getValue().ToString("0.0###");
        box.TextChanged += (_, _) =>
        {
            if (float.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out var val))
                setValue(val);
        };
        // Show values set from code (e.g. a loaded project). Text that already parses to the value
        // is left alone, so "0.50" or "1." being typed is not rewritten under the cursor.
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != propertyName) return;
            if (float.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture, out var shown) && shown == getValue())
                return;
            box.Text = getValue().ToString("0.0###");
        };
        return box;
    }

    /// <summary>
    /// Binds <paramref name="checkBox"/> to a bool view-model property: it shows
    /// <paramref name="getValue"/> now and whenever <paramref name="source"/> raises
    /// <paramref name="propertyName"/>, and writes the user's clicks to <paramref name="setValue"/>.
    /// Showing a value does not write it back. Other <c>Checked</c>/<c>Unchecked</c> handlers still
    /// run, so a panel can hang UI-only reactions (such as showing a section) on them.
    /// </summary>
    public static void BindCheckBox(CheckBox checkBox,
        INotifyPropertyChanged source, string propertyName,
        Func<bool> getValue, Action<bool> setValue)
    {
        checkBox.Visual.Name = propertyName;
        var showing = false;
        void Show()
        {
            if (checkBox.IsChecked == getValue()) return;
            showing = true;
            try { checkBox.IsChecked = getValue(); }
            finally { showing = false; }
        }

        checkBox.Checked += (_, _) => { if (!showing) setValue(true); };
        checkBox.Unchecked += (_, _) => { if (!showing) setValue(false); };
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName) Show();
        };
        Show();
    }

    /// <summary>
    /// Creates a grid row with label | color swatch | hex input for color selection, bound to a
    /// <c>#RRGGBB</c> view-model property: it shows <paramref name="getHex"/> now and whenever
    /// <paramref name="source"/> raises <paramref name="propertyName"/>, and writes complete colors
    /// typed or picked by the user to <paramref name="setHex"/>. Text that already means the
    /// shown color, or is still being typed, is left alone.
    /// </summary>
    /// <returns>The hex text box.</returns>
    public static TextBox AddColorRow(
        GraphicsDevice graphicsDevice,
        global::Gum.Wireframe.GraphicalUiElement parent,
        string label,
        INotifyPropertyChanged source, string propertyName,
        Func<string> getHex, Action<string> setHex,
        string? tooltip = null)
    {
        var (defaultR, defaultG, defaultB) = EffectsViewModel.ParseHex(getHex());

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
        hexBox.Visual.Name = propertyName;
        hexBox.Width = 80;
        hexBox.Text = $"#{defaultR:X2}{defaultG:X2}{defaultB:X2}";
        TooltipService.SetTooltip(hexBox, "Hex color (e.g., #FF0000)");
        var suppressHexSync = false;

        bool TryParseBox(out Color color)
        {
            var hex = hexBox.Text?.Trim() ?? "";
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 6 &&
                byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                color = new Color(r, g, b);
                return true;
            }
            color = default;
            return false;
        }

        hexBox.TextChanged += (_, _) =>
        {
            if (suppressHexSync || !TryParseBox(out var color)) return;
            swatch.FillColor = color;
            setHex($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        };
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != propertyName) return;
            var (r, g, b) = EffectsViewModel.ParseHex(getHex());
            var color = new Color(r, g, b);
            swatch.FillColor = color;
            if (TryParseBox(out var shown) && shown == color) return;
            suppressHexSync = true;
            try { hexBox.Text = $"#{r:X2}{g:X2}{b:X2}"; }
            finally { suppressHexSync = false; }
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
                setHex(hex);
            });
        };

        return hexBox;
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
