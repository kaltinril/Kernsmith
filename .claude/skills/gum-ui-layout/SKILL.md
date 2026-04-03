---
name: gum-ui-layout
description: "Gum UI layout patterns, gotchas, and target aesthetic for the KernSmith desktop app (apps/KernSmith.Ui/). Use this skill whenever working on KernSmith UI layout code, styling, panel construction, or control placement. Also trigger when the user mentions Gum controls, section headers, collapsible sections, ScrollViewer customization, or the egui-inspired redesign. If you're about to create or modify any file in apps/KernSmith.Ui/Layout/ or apps/KernSmith.Ui/Styling/, read this skill first."
---

# Gum UI Layout for KernSmith

This skill captures hard-won knowledge about building UI with Gum Forms V3 in the KernSmith desktop app. It covers layout patterns, common pitfalls, and the target visual aesthetic.

## Project Structure

- `apps/KernSmith.Ui/Layout/` — Panel classes (FontConfigPanel, EffectsPanel, etc.)
- `apps/KernSmith.Ui/Styling/Theme.cs` — Color palette and layout constants
- `apps/KernSmith.Ui/Styling/UiFactory.cs` — Shared UI factory methods
- `apps/KernSmith.Ui/KernSmithGame.cs` — App init, Gum setup, dark theme colors

## Target Aesthetic: egui-Inspired

The UI is moving toward an egui-inspired look. The core principles:

### Every Row is Label:Control
```
  Source:      [Browse for Font...]
  Font:        [dropdown           v]
  Glyphs:      0
  Size:        [32    ] pt
  Rasterizer:  [FreeType           v]
```
All settings use a consistent two-column layout: fixed-width label on the left, control filling remaining space on the right. This creates visual alignment and rhythm.

### Headers Are Just Text
Section headers (FONT FILE, SIZE, ATLAS, etc.) should be plain text with a chevron indicator — no colored background bars, no accent-colored text. Use spacing to separate sections, not decorative containers.

### No Decorative Chrome
- No section header background rectangles
- No distinct `contentWrapper` background colors
- No visible layering between panel, content, and sections
- Sections separated by spacing alone

### Color Hierarchy
- **Accent blue** (#0078D4) is reserved for interactive elements only: buttons, active tabs, checked checkboxes
- **Section header text** uses `Theme.SectionHeaderText` (plain light gray), not `Theme.Accent`
- **Body text** uses `Theme.Text` (200, 200, 200)
- The Gum dark style colors are set in `KernSmithGame.Initialize()`

## Gum Layout Fundamentals

### Container Types and When to Use Them
- **StackPanel** — Use for simple rows of controls. `Orientation.Horizontal` for label:control rows.
- **ContainerRuntime with TopToBottomStack** — Use for vertical stacking with `StackSpacing`. More flexible than StackPanel for mixed content.
- **Grid** — Use sparingly. Grid with `Star` columns doesn't work reliably inside StackPanel parents. Prefer StackPanel with fixed-width labels instead.
- **ScrollViewer** — For scrollable content. Always strip the default border (see below).

### Width/Height Units
```csharp
// Fill parent width
element.WidthUnits = DimensionUnitType.RelativeToParent;
element.Width = 0; // 0 = same as parent, negative = smaller, positive = larger

// Size to children
element.HeightUnits = DimensionUnitType.RelativeToChildren;
element.Height = 0; // 0 = exact fit, positive = extra padding

// Fill remaining space in a stack
element.HeightUnits = DimensionUnitType.Ratio;
element.Height = 1; // Shares remaining space proportionally

// Fixed size
element.HeightUnits = DimensionUnitType.Absolute;
element.Height = 60;
```

### The Two-Column Row Pattern
This is the standard pattern for label:control pairs:
```csharp
var row = new StackPanel();
row.Orientation = Orientation.Horizontal;
row.Spacing = Theme.ControlSpacing;
parent.Children.Add(row.Visual);

var label = new Label();
label.Text = "Size:";
label.Width = Theme.LabelWidth; // 70px
row.AddChild(label);

var textBox = new TextBox();
textBox.Width = 42;
row.AddChild(textBox);
```

For combos that should fill available space, put the label above the combo instead (Grid Star columns are unreliable):
```csharp
var label = new Label();
label.Text = "Rasterizer:";
parent.Children.Add(label.Visual);

var combo = new ComboBox();
combo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
combo.Visual.Width = 0;
parent.Children.Add(combo.Visual);
```

### Spacing Constants
- `Theme.ControlSpacing` (4px) — Between related controls in a row
- `Theme.SectionSpacing` (8px) — Between sections or groups
- `Theme.PanelPadding` (8px) — Panel edge padding
- `Theme.LabelWidth` (70px) — Standard label column width

Always use the Theme constants, never hardcode spacing values.

## Critical Gotchas

### 1. States Override Direct Property Sets
**This is the #1 gotcha.** If you set a property on a Gum visual element and it gets "reset," the state system is overriding your value.

**Wrong** (will be reset):
```csharp
scrollVisual.ClipContainerInstance.X = 0;
scrollVisual.ClipContainerInstance.Width = 0;
```

**Right** (inject into states):
```csharp
var marginVars = new (string Name, object Value)[]
{
    ("ClipContainerInstance.X", 0f),
    ("ClipContainerInstance.Y", 0f),
    ("ClipContainerInstance.Width", 0f),
    ("ClipContainerInstance.Height", 0f),
};
foreach (var state in new[] { visual.States.Enabled, visual.States.Focused })
{
    foreach (var (name, value) in marginVars)
    {
        state.Variables.Add(new Gum.DataTypes.Variables.VariableSave
        {
            Name = name,
            Value = value
        });
    }
}
```

The working implementation is in `FontConfigPanel.StripScrollViewerMargins()`.

### 2. ScrollViewer Border Removal
V3 ScrollViewers have a NineSlice background with visible borders AND a 2px ClipContainerInstance margin. To make them borderless:

```csharp
if (scrollViewer.Visual is global::Gum.Forms.DefaultVisuals.V3.ScrollViewerVisual sv)
{
    // Switch from bordered to solid NineSlice, match panel color
    sv.Background.ApplyState(
        global::Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle.NineSlice.Solid);
    sv.BackgroundColor = Theme.Panel;
    // Remove the 2px clip margins via state injection
    FontConfigPanel.StripScrollViewerMargins(sv);
}
```

Key details:
- The background is a **NineSlice**, not a ColoredRectangle. `GetGraphicalUiElementByName("Background")` finds the wrong element (recursive search hits nested backgrounds first).
- Cast `scrollViewer.Visual` to `ScrollViewerVisual` to access typed properties like `.Background`, `.ClipContainerInstance`.
- The cast **works** — V3 styling creates `ScrollViewerVisual` instances.

### 3. Dock.Fill Does NOT Respect Siblings
In a TopToBottomStack, `Dock.Fill` doesn't subtract sibling heights. Use `Ratio` height instead:
```csharp
// Scroll area fills remaining space, bottom bar is fixed
scrollArea.HeightUnits = DimensionUnitType.Ratio;
scrollArea.Height = 1;
bottomBar.HeightUnits = DimensionUnitType.Absolute;
bottomBar.Height = 60;
```

### 4. Namespace Collision: KernSmith.Gum vs Gum
Adding `KernSmith.MonoGameGum` as a project reference introduces the `KernSmith.Gum` namespace, which collides with `Gum.Wireframe`, `Gum.Forms`, etc. Use `global::` prefix:
```csharp
using global::Gum.DataTypes;
using global::Gum.Forms.Controls;
// In method signatures:
public static void DoThing(global::Gum.Wireframe.GraphicalUiElement parent)
```

### 5. Bottom Bar Sizing
For fixed bottom bars (like Generate button), use `RelativeToChildren` height instead of hardcoded absolute — avoids content overflow:
```csharp
bottomBar.HeightUnits = DimensionUnitType.RelativeToChildren;
bottomBar.Height = 0;
```
Don't add extra `Y` offset on children inside a `TopToBottomStack` — the stack spacing already handles separation.

## Dynamic Font Generation

KernSmith uses its own font creator for Gum's text rendering:
```csharp
// In KernSmithGame.Initialize(), after GumService.Default.Initialize():
CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmith.Gum.KernSmithFontCreator(GraphicsDevice);
```

To change the default font size globally:
```csharp
MonoGameGum.GueDeriving.TextRuntime.DefaultFontSize = 14;
```
**Note:** Changing font size without also scaling control chrome (buttons, checkboxes) looks unbalanced. The V3 control visuals have baked-in dimensions from the sprite sheet.

## Layout Debugging

Gum has a layout export feature for diagnostics:
```csharp
using GumRuntime;
GumService.Default.Root.ExportLayoutJson("layout-dump.json");
```
This outputs the full UI tree as JSON with absolute pixel positions. Useful for diagnosing overlap, margin, and sizing issues. The JSON is large (~600KB) — use node.js or similar to filter to the area of interest.
