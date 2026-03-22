using Gum.Forms;
using Gum.Forms.Controls;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Modal error dialog that displays a title and message with an OK button to dismiss.
/// </summary>
public static class ErrorDialog
{
    /// <summary>
    /// Shows a centered modal window with the given title and error message.
    /// </summary>
    public static void Show(string title, string message)
    {
        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
        window.Width = 400;
        window.Height = 200;
        FrameworkElement.ModalRoot.Children.Add(window.Visual);

        var stack = new StackPanel();
        stack.Spacing = 8;
        stack.Y = 28;
        stack.X = 12;
        window.AddChild(stack);

        var titleLabel = new Label();
        titleLabel.Text = title;
        stack.AddChild(titleLabel);

        var msgLabel = new Label();
        msgLabel.Text = message;
        stack.AddChild(msgLabel);

        var okBtn = new Button();
        okBtn.Text = "OK";
        okBtn.Anchor(Gum.Wireframe.Anchor.Bottom);
        okBtn.Y = -10;
        okBtn.Width = 80;
        window.AddChild(okBtn.Visual);
        okBtn.Click += (_, _) => window.RemoveFromRoot();
    }
}
