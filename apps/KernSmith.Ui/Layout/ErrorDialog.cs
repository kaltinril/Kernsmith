using global::Gum.Forms;
using global::Gum.Forms.Controls;

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
        window.Anchor(global::Gum.Wireframe.Anchor.Center);
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
        okBtn.Anchor(global::Gum.Wireframe.Anchor.Bottom);
        okBtn.Y = -10;
        okBtn.Width = 80;
        window.AddChild(okBtn.Visual);
        okBtn.Click += (_, _) => window.RemoveFromRoot();
    }

    /// <summary>
    /// Shows a centered modal confirmation window with the given title and message.
    /// Invokes <paramref name="onConfirm"/> when the user clicks the confirm button,
    /// or <paramref name="onCancel"/> when the user cancels. Either callback runs after
    /// the dialog is dismissed.
    /// </summary>
    public static void Confirm(
        string title,
        string message,
        string confirmText,
        Action onConfirm,
        Action? onCancel = null)
    {
        var window = new Window();
        window.Anchor(global::Gum.Wireframe.Anchor.Center);
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

        var confirmBtn = new Button();
        confirmBtn.Text = confirmText;
        confirmBtn.Anchor(global::Gum.Wireframe.Anchor.BottomLeft);
        confirmBtn.X = 12;
        confirmBtn.Y = -10;
        confirmBtn.Width = 120;
        window.AddChild(confirmBtn.Visual);
        confirmBtn.Click += (_, _) =>
        {
            window.RemoveFromRoot();
            onConfirm();
        };

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.Anchor(global::Gum.Wireframe.Anchor.BottomRight);
        cancelBtn.X = -12;
        cancelBtn.Y = -10;
        cancelBtn.Width = 80;
        window.AddChild(cancelBtn.Visual);
        cancelBtn.Click += (_, _) =>
        {
            window.RemoveFromRoot();
            onCancel?.Invoke();
        };
    }
}
