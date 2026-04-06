using global::Gum.Wireframe;
using Microsoft.Xna.Framework;
using MonoGameGum;
using MonoGameGum.GueDeriving;
using KernSmith.Ui.Styling;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Global tooltip service. Controls register via <see cref="SetTooltip"/> and the tooltip
/// appears after a 500ms hover delay, following the cursor and clamped to window bounds.
/// Must be initialized once and updated each frame.
/// </summary>
public static class TooltipService
{
    private static ContainerRuntime? _tooltipContainer;
    private static TextRuntime? _tooltipText;
    private static bool _isVisible;
    private static DateTime _hoverStartTime;
    private static bool _waitingToShow;
    private static string _pendingText = "";
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Creates the tooltip container and text runtime, and adds them to the GUM popup root.
    /// </summary>
    public static void Initialize()
    {
        _tooltipContainer = new ContainerRuntime();
        _tooltipContainer.Visible = false;
        _tooltipContainer.HasEvents = false;
        _tooltipContainer.WidthUnits = global::Gum.DataTypes.DimensionUnitType.RelativeToChildren;
        _tooltipContainer.HeightUnits = global::Gum.DataTypes.DimensionUnitType.RelativeToChildren;
        _tooltipContainer.Width = 12; // padding
        _tooltipContainer.Height = 8;

        var bg = new ColoredRectangleRuntime();
        bg.Width = 0;
        bg.WidthUnits = global::Gum.DataTypes.DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = global::Gum.DataTypes.DimensionUnitType.RelativeToParent;
        bg.Color = new Color(20, 20, 20);
        _tooltipContainer.Children.Add(bg);

        _tooltipText = new TextRuntime();
        _tooltipText.X = 6;
        _tooltipText.Y = 4;
        _tooltipText.Color = Theme.Text;
        _tooltipContainer.Children.Add(_tooltipText);

        global::Gum.Forms.Controls.FrameworkElement.PopupRoot.Children.Add(_tooltipContainer);
    }

    /// <summary>
    /// Registers a tooltip for the given control. Shows on hover after delay, hides on roll-off.
    /// </summary>
    public static void SetTooltip(global::Gum.Forms.Controls.FrameworkElement control, string text)
    {
        control.Visual.RollOn += (_, _) =>
        {
            _pendingText = text;
            _hoverStartTime = DateTime.UtcNow;
            _waitingToShow = true;
        };

        control.Visual.RollOff += (_, _) =>
        {
            _waitingToShow = false;
            Hide();
        };
    }

    /// <summary>
    /// Call every frame from KernSmithGame.Update() to handle delay and positioning.
    /// </summary>
    public static void Update()
    {
        if (_tooltipContainer == null) return;

        if (_waitingToShow && !_isVisible)
        {
            if (DateTime.UtcNow - _hoverStartTime >= ShowDelay)
            {
                Show(_pendingText);
                _waitingToShow = false;
            }
        }

        if (_isVisible)
        {
            var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
            var tipWidth = _tooltipContainer.GetAbsoluteWidth();
            var tipHeight = _tooltipContainer.GetAbsoluteHeight();
            var screenWidth = GraphicalUiElement.CanvasWidth;
            var screenHeight = GraphicalUiElement.CanvasHeight;

            float x = mouseState.X + 12;
            float y = mouseState.Y + 16;

            // Clamp to stay within window bounds
            if (x + tipWidth > screenWidth)
                x = mouseState.X - tipWidth - 4;
            if (y + tipHeight > screenHeight)
                y = mouseState.Y - tipHeight - 4;

            _tooltipContainer.X = Math.Max(0, x);
            _tooltipContainer.Y = Math.Max(0, y);
        }
    }

    private static void Show(string text)
    {
        if (_tooltipContainer == null || _tooltipText == null) return;
        _tooltipText.Text = text;
        _tooltipContainer.Visible = true;
        _isVisible = true;
    }

    private static void Hide()
    {
        if (_tooltipContainer == null) return;
        _tooltipContainer.Visible = false;
        _isVisible = false;
    }
}
