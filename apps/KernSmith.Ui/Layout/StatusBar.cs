using Gum.Forms.Controls;
using KernSmith.Ui.ViewModels;

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
        var stack = new StackPanel();
        stack.Orientation = Orientation.Horizontal;
        stack.Spacing = 16;
        stack.Dock(Gum.Wireframe.Dock.Fill);
        this.AddChild(stack);

        // Status text (left side)
        var statusLabel = new Label();
        statusLabel.Text = "Ready";
        statusLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.StatusText));
        statusLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(statusLabel);

        // Atlas dimensions
        var dimsLabel = new Label();
        dimsLabel.Text = "";
        dimsLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.AtlasDimensions));
        dimsLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(dimsLabel);

        // Glyph count
        var glyphLabel = new Label();
        glyphLabel.Text = "";
        _statusBar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatusBarViewModel.GlyphCount))
                glyphLabel.Text = _statusBar.GlyphCount > 0 ? $"{_statusBar.GlyphCount} glyphs" : "";
        };
        stack.AddChild(glyphLabel);

        // Generation time
        var timeLabel = new Label();
        timeLabel.Text = "";
        timeLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GenerationTime));
        timeLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(timeLabel);
    }
}
