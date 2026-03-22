using Gum.DataTypes;
using Gum.Forms.Controls;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class StatusBar : Panel
{
    private readonly StatusBarViewModel _statusBar;

    public StatusBar(StatusBarViewModel statusBar)
    {
        _statusBar = statusBar;

        BuildContent();
    }

    private void BuildContent()
    {
        // Background to visually separate from content above
        var bg = new ColoredRectangleRuntime();
        bg.Color = Theme.Panel;
        this.AddChild(bg);
        bg.Dock(Gum.Wireframe.Dock.Fill);

        // Top border line
        var border = new ColoredRectangleRuntime();
        border.Color = Theme.PanelBorder;
        border.Height = 1;
        border.Dock(Gum.Wireframe.Dock.Top);
        this.AddChild(border);

        var stack = new StackPanel();
        stack.Orientation = Orientation.Horizontal;
        stack.Spacing = 12;
        stack.Y = 4;
        stack.X = 8;
        stack.Height = 20;
        stack.HeightUnits = DimensionUnitType.Absolute;
        this.AddChild(stack);

        // Status text
        var statusLabel = new Label();
        statusLabel.Text = "Ready";
        statusLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.StatusText));
        statusLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(statusLabel);

        // Error state visual feedback: change status label text color on error
        var statusTextInstance = FindTextRuntime(statusLabel.Visual);

        // Separator + Atlas dimensions
        var sep1 = CreateSeparator();
        stack.AddChild(sep1);

        var dimsLabel = new Label();
        dimsLabel.Text = "";
        dimsLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.AtlasDimensions));
        dimsLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(dimsLabel);

        // Separator + Glyph count
        var sep2 = CreateSeparator();
        stack.AddChild(sep2);

        var glyphLabel = new Label();
        glyphLabel.Text = "";
        stack.AddChild(glyphLabel);

        // Separator + Glyph info
        var sep3 = CreateSeparator();
        stack.AddChild(sep3);

        var glyphInfoLabel = new Label();
        glyphInfoLabel.Text = "";
        glyphInfoLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GlyphInfoText));
        glyphInfoLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(glyphInfoLabel);

        // Separator + Generation time
        var sep4 = CreateSeparator();
        stack.AddChild(sep4);

        var timeLabel = new Label();
        timeLabel.Text = "";
        timeLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GenerationTime));
        timeLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(timeLabel);

        // Update separator visibility and error coloring when properties change
        _statusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusBarViewModel.StatusText) && statusTextInstance != null)
            {
                var text = _statusBar.StatusText ?? "";
                if (text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                    statusTextInstance.Color = Theme.Error;
                else
                    statusTextInstance.Color = Theme.Text;
            }

            if (e.PropertyName == nameof(StatusBarViewModel.GlyphCount))
                glyphLabel.Text = _statusBar.GlyphCount > 0 ? $"{_statusBar.GlyphCount} glyphs" : "";

            // Update separator visibility: hide when adjacent label is empty
            UpdateSeparatorVisibility(sep1, dimsLabel);
            UpdateSeparatorVisibility(sep2, glyphLabel);
            UpdateSeparatorVisibility(sep3, glyphInfoLabel);
            UpdateSeparatorVisibility(sep4, timeLabel);
        };

        // Initial separator state (all hidden since labels start empty)
        UpdateSeparatorVisibility(sep1, dimsLabel);
        UpdateSeparatorVisibility(sep2, glyphLabel);
        UpdateSeparatorVisibility(sep3, glyphInfoLabel);
        UpdateSeparatorVisibility(sep4, timeLabel);
    }

    private static void UpdateSeparatorVisibility(Label separator, Label adjacentLabel)
    {
        separator.IsVisible = !string.IsNullOrEmpty(adjacentLabel.Text);
    }

    private static Label CreateSeparator()
    {
        var sep = new Label();
        sep.Text = "|";
        var sepText = FindTextRuntime(sep.Visual);
        if (sepText != null) sepText.Color = Theme.TextMuted;
        return sep;
    }

    private static TextRuntime? FindTextRuntime(Gum.Wireframe.GraphicalUiElement element)
    {
        // GUM Labels contain a child named "TextInstance" which is a TextRuntime
        foreach (var child in element.Children)
        {
            if (child is TextRuntime textRuntime)
                return textRuntime;
        }
        // Also check if the element itself wraps text
        var textChild = element.GetChildByNameRecursively("TextInstance");
        return textChild as TextRuntime;
    }
}
