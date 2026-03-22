using Microsoft.Xna.Framework;

namespace KernSmith.Ui.Styling;

/// <summary>
/// Dark IDE-inspired color palette used throughout the UI for consistent styling.
/// </summary>
public static class Theme
{
    /// <summary>Window and deep background color (#1E1E1E).</summary>
    public static readonly Color Background = new(30, 30, 30);
    /// <summary>Panel fill color (#252526).</summary>
    public static readonly Color Panel = new(37, 37, 38);
    /// <summary>Panel border and divider color (#3C3C3C).</summary>
    public static readonly Color PanelBorder = new(60, 60, 60);
    /// <summary>Primary text color (#CCCCCC).</summary>
    public static readonly Color Text = new(204, 204, 204);
    /// <summary>Muted/secondary text color (#888888).</summary>
    public static readonly Color TextMuted = new(136, 136, 136);
    /// <summary>Accent color for buttons, section headers, and selection highlights (#0078D4).</summary>
    public static readonly Color Accent = new(0, 120, 212);
    /// <summary>Accent hover state (#1A8AD4).</summary>
    public static readonly Color AccentHover = new(26, 138, 212);
    /// <summary>Error text and indicator color (#F44747).</summary>
    public static readonly Color Error = new(244, 71, 71);
    /// <summary>Warning text color (#FFC832).</summary>
    public static readonly Color Warning = new(255, 200, 50);
    /// <summary>Success indicator color (#4EC9B0).</summary>
    public static readonly Color Success = new(78, 201, 176);

    /// <summary>Light checkerboard tile color for the atlas transparency background.</summary>
    public static Color CheckerLight = new(60, 60, 60);
    /// <summary>Dark checkerboard tile color for the atlas transparency background.</summary>
    public static Color CheckerDark = new(45, 45, 45);
}
