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
        };

        // Separator
        AddSeparator(stack);

        // Atlas dimensions
        var dimsLabel = new Label();
        dimsLabel.Text = "";
        dimsLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.AtlasDimensions));
        dimsLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(dimsLabel);

        // Separator
        AddSeparator(stack);

        // Glyph count
        var glyphLabel = new Label();
        glyphLabel.Text = "";
        _statusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusBarViewModel.GlyphCount))
                glyphLabel.Text = _statusBar.GlyphCount > 0 ? $"{_statusBar.GlyphCount} glyphs" : "";
        };
        stack.AddChild(glyphLabel);

        // Separator
        AddSeparator(stack);

        // Glyph info (rendered vs requested, failed codepoints)
        var glyphInfoLabel = new Label();
        glyphInfoLabel.Text = "";
        glyphInfoLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GlyphInfoText));
        glyphInfoLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(glyphInfoLabel);

        // Separator
        AddSeparator(stack);

        // Generation time
        var timeLabel = new Label();
        timeLabel.Text = "";
        timeLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GenerationTime));
        timeLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(timeLabel);
    }

    private static void AddSeparator(StackPanel parent)
    {
        var sep = new Label();
        sep.Text = "|";
        var sepText = FindTextRuntime(sep.Visual);
        if (sepText != null) sepText.Color = Theme.TextMuted;
        parent.AddChild(sep);
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
