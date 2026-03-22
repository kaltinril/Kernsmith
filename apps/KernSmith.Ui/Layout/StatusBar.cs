using Gum.DataTypes;
using Gum.Forms.Controls;
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
        bg.Color = new Microsoft.Xna.Framework.Color(37, 37, 38);
        this.AddChild(bg);
        bg.Dock(Gum.Wireframe.Dock.Fill);

        // Top border line
        var border = new ColoredRectangleRuntime();
        border.Color = new Microsoft.Xna.Framework.Color(60, 60, 60);
        border.Height = 1;
        border.Dock(Gum.Wireframe.Dock.Top);
        this.AddChild(border);

        var stack = new StackPanel();
        stack.Orientation = Orientation.Horizontal;
        stack.Spacing = 20;
        stack.Y = 4;
        stack.X = 8;
        this.AddChild(stack);

        // Status text
        var statusLabel = new Label();
        statusLabel.Text = "Ready";
        statusLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.StatusText));
        statusLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(statusLabel);

        // Separator
        var sep1 = new Label();
        sep1.Text = "|";
        stack.AddChild(sep1);

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

        // Glyph info (rendered vs requested, failed codepoints)
        var glyphInfoLabel = new Label();
        glyphInfoLabel.Text = "";
        glyphInfoLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GlyphInfoText));
        glyphInfoLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(glyphInfoLabel);

        // Generation time
        var timeLabel = new Label();
        timeLabel.Text = "";
        timeLabel.SetBinding(nameof(Label.Text), nameof(StatusBarViewModel.GenerationTime));
        timeLabel.Visual.BindingContext = _statusBar;
        stack.AddChild(timeLabel);
    }
}
