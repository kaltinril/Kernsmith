using Gum.Forms;
using Gum.Forms.Controls;

namespace KernSmith.Ui.Layout;

public static class KeyboardShortcutsDialog
{
    private static readonly (string Shortcut, string Description)[] Shortcuts =
    [
        ("Ctrl+O", "Browse for font"),
        ("Ctrl+S", "Save project"),
        ("Ctrl+G", "Generate"),
        ("Ctrl+Shift+S", "Export / Save As"),
    ];

    public static void Show()
    {
        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
        window.Width = 360;
        window.Height = 260;
        FrameworkElement.ModalRoot.Children.Add(window.Visual);

        var stack = new StackPanel();
        stack.Spacing = 6;
        stack.Y = 28;
        stack.X = 12;
        window.AddChild(stack);

        var titleLabel = new Label();
        titleLabel.Text = "Keyboard Shortcuts";
        stack.AddChild(titleLabel);

        foreach (var (shortcut, description) in Shortcuts)
        {
            var row = new Label();
            row.Text = $"{shortcut,-20} {description}";
            stack.AddChild(row);
        }

        var okButton = new Button();
        okButton.Text = "OK";
        okButton.Anchor(Gum.Wireframe.Anchor.Bottom);
        okButton.Y = -10;
        okButton.Width = 80;
        window.AddChild(okButton.Visual);
        okButton.Click += (_, _) => window.RemoveFromRoot();
    }
}
