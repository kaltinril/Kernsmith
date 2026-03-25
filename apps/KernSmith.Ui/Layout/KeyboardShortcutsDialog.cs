using Gum.Forms;
using Gum.Forms.Controls;
using Gum.DataTypes;
using Microsoft.Xna.Framework;
using MonoGameGum.GueDeriving;
using KernSmith.Ui.Styling;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Modal dialog listing all keyboard shortcuts and mouse controls.
/// Accessible via Help > Keyboard Shortcuts.
/// </summary>
public static class KeyboardShortcutsDialog
{
    private static readonly (string Shortcut, string Description)[] Shortcuts =
    [
        ("Ctrl+O", "Browse for font"),
        ("Ctrl+S", "Save project"),
        ("Ctrl+G", "Generate"),
        ("Ctrl+Shift+S", "Export / Save As"),
        ("Ctrl++", "UI scale up"),
        ("Ctrl+-", "UI scale down"),
        ("Ctrl+0", "Reset UI scale"),
        ("Scroll Wheel", "Zoom (over atlas)"),
        ("Middle Drag", "Pan preview"),
    ];

    public static void Show()
    {
        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
        window.Width = 380;
        window.Height = 340;
        window.ResizeMode = ResizeMode.NoResize;
        FrameworkElement.ModalRoot.Children.Add(window.Visual);

        // Title in title bar
        var windowVisual = window.Visual as Gum.Forms.DefaultVisuals.V3.WindowVisual;
        if (windowVisual?.TitleBarInstance != null)
        {
            var titleLabel = new Label();
            titleLabel.Text = "Keyboard Shortcuts";
            titleLabel.X = 8;
            titleLabel.Y = 2;
            windowVisual.TitleBarInstance.AddChild(titleLabel);
        }

        // Opaque background behind content so atlas doesn't bleed through
        var bg = new ColoredRectangleRuntime();
        bg.X = 0;
        bg.Y = 0;
        bg.Width = 0;
        bg.WidthUnits = DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = DimensionUnitType.RelativeToParent;
        bg.Color = Theme.Panel;
        window.Visual.Children.Insert(0, bg);

        var grid = new Grid();
        grid.Y = 32;
        grid.X = 16;
        grid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        grid.Visual.Width = -32;
        grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        grid.Visual.Height = 0;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < Shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keyLabel = new Label();
            keyLabel.Text = Shortcuts[i].Shortcut;
            grid.AddChild(keyLabel, row: i, column: 0);

            var descLabel = new Label();
            descLabel.Text = Shortcuts[i].Description;
            grid.AddChild(descLabel, row: i, column: 2);
        }

        window.AddChild(grid);

        var okButton = new Button();
        okButton.Text = "OK";
        okButton.Anchor(Gum.Wireframe.Anchor.Bottom);
        okButton.Y = -10;
        okButton.Width = 80;
        window.AddChild(okButton.Visual);
        okButton.Click += (_, _) => window.RemoveFromRoot();
    }
}
