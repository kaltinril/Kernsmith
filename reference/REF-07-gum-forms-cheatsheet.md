> **Note:** This is third-party reference material for the GUM UI framework, used by the KernSmith UI app in `apps/KernSmith.Ui/`.

# GUM Forms Cheat Sheet (Code-Only, MonoGame)

> Source: https://docs.flatredball.com/gum/
> For agents: use WebFetch on the URLs above if you need more detail. NEVER read DLL/XML files.

---

## Setup & Initialization

**NuGet**: `Gum.MonoGame`

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum;
using Gum.Forms;
using Gum.Forms.Controls;
```

```csharp
public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    GumService GumUI => GumService.Default;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        GumUI.Initialize(this, DefaultVisualsVersion.V3);
        // Create your screen/UI here
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        GumUI.Draw();  // REQUIRED — nothing renders without this
        base.Draw(gameTime);
    }
}
```

### Enabling Keyboard Input for TextBox/etc
```csharp
FrameworkElement.KeyboardsForUiControl.Add(GumService.Default.Keyboard);
```

---

## Screen Patterns

### Pattern 1: Screen as ContainerRuntime
```csharp
internal class MyScreen : ContainerRuntime
{
    public MyScreen()
    {
        this.Dock(Gum.Wireframe.Dock.Fill);
        // build UI here, add children with this.AddChild(...)
    }
}

// In Game.Initialize():
var screen = new MyScreen();
screen.AddToRoot();  // extension method
```

### Pattern 2: Screen as FrameworkElement
```csharp
internal class MyScreen : FrameworkElement
{
    public MyScreen() : base(new ContainerRuntime())
    {
        Dock(Gum.Wireframe.Dock.Fill);
        // build UI here
    }
}

// In Game.Initialize():
var screen = new MyScreen();
screen.AddToRoot();
```

---

## Controls Reference

All controls are in `Gum.Forms.Controls` namespace (also aliased as `MonoGameGum.Forms.Controls`).

### Button
```csharp
var button = new Button();
button.Text = "Click Me";
button.Width = 100;
button.Height = 50;
button.Click += (_, _) => { /* handle click */ };
button.IsEnabled = false;  // disables the button
parentPanel.AddChild(button);
```

### Label
```csharp
var label = new Label();
label.Text = "Hello World";
parentPanel.AddChild(label);
```

**Text alignment**: V3 `LabelVisual` IS a `TextRuntime` (no separate text child). Use `SetProperty` to center text vertically within fixed-height labels:
```csharp
label.Visual.HeightUnits = DimensionUnitType.Absolute;
label.Height = 30;
label.Visual.WidthUnits = DimensionUnitType.RelativeToChildren;
label.Visual.SetProperty("VerticalAlignment",
    RenderingLibrary.Graphics.VerticalAlignment.Center);
```
**Note**: `Dock.FillVertically` on a Label in a horizontal StackPanel does NOT reliably center text. Use explicit `Absolute` height + `SetProperty("VerticalAlignment", Center)` instead.

### TextBox
```csharp
var textBox = new TextBox();
textBox.Width = 200;
textBox.Placeholder = "Enter text here...";
textBox.TextChanged += (_, _) => { /* handle change */ };
parentPanel.AddChild(textBox);

// Limit visible characters without restricting input (default null = no limit):
textBox.MaxLettersToShow = 3;
// Limit actual input — typing, pasting, and programmatic text are truncated (default null = no limit).
// Equivalent to WPF's TextBox.MaxLength:
textBox.MaxLength = 3;

// Multi-line:
textBox.TextWrapping = Gum.Forms.TextWrapping.Wrap;
textBox.AcceptsReturn = true;
textBox.Height = 140;
```

### ComboBox
```csharp
var comboBox = new ComboBox();
comboBox.Width = 140;
for (int i = 0; i < 20; i++)
    comboBox.Items.Add($"Item {i}");
comboBox.SelectionChanged += (_, _) =>
{
    var selected = comboBox.SelectedObject;
    var index = comboBox.SelectedIndex;
};
parentPanel.AddChild(comboBox);
```

### ListBox
```csharp
var listBox = new ListBox();
listBox.Width = 200;  // or listBox.Visual.Width = 200;
listBox.Height = 300; // or listBox.Visual.Height = 300;
for (int i = 0; i < 20; i++)
    listBox.Items.Add($"Item {i}");
listBox.SelectionChanged += (_, _) => { };
parentPanel.AddChild(listBox);
```

### CheckBox
```csharp
var checkBox = new CheckBox();
checkBox.Text = "Enable feature";
checkBox.Checked += (_, _) => { };
checkBox.Unchecked += (_, _) => { };
parentPanel.AddChild(checkBox);
```

### RadioButton
```csharp
// Group by putting in same container
var group = new StackPanel();
for (int i = 0; i < 3; i++)
{
    var rb = new RadioButton();
    rb.Text = $"Option {i}";
    group.AddChild(rb);
}
parentPanel.AddChild(group);
```

### ScrollViewer
```csharp
var scrollViewer = new ScrollViewer();
scrollViewer.Width = 200;
scrollViewer.Height = 300;
// scrollViewer.InnerPanel gives access to inner stacking container
parentPanel.AddChild(scrollViewer);

// Add children:
for (int i = 0; i < 15; i++)
{
    var btn = new Button();
    btn.Text = "Button " + i;
    scrollViewer.AddChild(btn);
}
```

### Slider
```csharp
var slider = new Slider();
slider.Width = 200;
slider.Minimum = 0;
slider.Maximum = 100;
slider.TicksFrequency = 1;
slider.IsSnapToTickEnabled = true;
slider.ValueChanged += (_, _) => { };
parentPanel.AddChild(slider);
```

### PasswordBox
```csharp
var passwordBox = new PasswordBox();
passwordBox.Width = 200;
passwordBox.Placeholder = "Enter Password";
parentPanel.AddChild(passwordBox);
```

### Menu & MenuItem
```csharp
var menu = new Menu();

var fileItem = new MenuItem();
fileItem.Header = "File";

var openItem = new MenuItem();
openItem.Header = "Open";
openItem.Clicked += (_, _) => { /* handle */ };
fileItem.Items.Add(openItem);

var saveItem = new MenuItem();
saveItem.Header = "Save";
fileItem.Items.Add(saveItem);

menu.Items.Add(fileItem);

var editItem = new MenuItem();
editItem.Header = "Edit";
menu.Items.Add(editItem);

this.AddChild(menu);  // add to screen/container
```

### Splitter (resizable divider)
```csharp
var container = new StackPanel();

var topPanel = new Button();
topPanel.Width = 200;
topPanel.Height = 200;
container.AddChild(topPanel);

var splitter = new Splitter();
container.AddChild(splitter);
splitter.Dock(Gum.Wireframe.Dock.FillHorizontally);
splitter.Height = 5;

var bottomPanel = new Button();
bottomPanel.Width = 200;
bottomPanel.Height = 200;
container.AddChild(bottomPanel);
```

**Double-click to reset**: `Splitter.Visual` is an `InteractiveGue` which exposes a `DoubleClick` event. Use it to reset adjacent panel widths:
```csharp
if (splitter.Visual is InteractiveGue interactive)
    interactive.DoubleClick += (_, _) => panel.Width = defaultWidth;
```

### UI Scaling / Accessibility Zoom

Scale the entire UI globally via `Camera.Zoom`. Adjust canvas dimensions inversely so layout still fills the window.

```csharp
// Store original height at startup
private int _originalHeight;
private float _uiScale = 1.0f;

protected override void Initialize()
{
    _originalHeight = _graphics.GraphicsDevice.Viewport.Height;
}

// Apply UI scale
private void ApplyUiScale()
{
    var camera = SystemManagers.Default.Renderer.Camera;
    camera.Zoom = _uiScale;
    GumService.Default.CanvasWidth = _graphics.GraphicsDevice.Viewport.Width / _uiScale;
    GumService.Default.CanvasHeight = _graphics.GraphicsDevice.Viewport.Height / _uiScale;
    GumService.Default.Root.UpdateLayout();
}
```

Also works with window resize — combine the resize zoom with UI scale:
```csharp
private void HandleClientSizeChanged(object sender, EventArgs e)
{
    // Reapply UI scale after resize
    ApplyUiScale();
}
```

### Window (popup/dialog)
```csharp
// Modal dialog
var window = new Window();
window.Anchor(Gum.Wireframe.Anchor.Center);
window.Width = 300;
window.Height = 200;
FrameworkElement.ModalRoot.AddChild(window);

// Disable resize (default is ResizeMode.CanResize):
window.ResizeMode = ResizeMode.NoResize;

var label = new Label();
label.Dock(Gum.Wireframe.Dock.Top);
label.Text = "Dialog text";
window.AddChild(label);

var closeBtn = new Button();
closeBtn.Anchor(Gum.Wireframe.Anchor.Bottom);
closeBtn.Text = "Close";
window.AddChild(closeBtn.Visual);
closeBtn.Click += (_, _) => window.RemoveFromRoot();

// Non-modal:
// FrameworkElement.PopupRoot.AddChild(window);

// No built-in Title property. To set title text:
// var titleBar = window.GetFrameworkElement("TitleBarInstance");
// then add a Label to it.

// Disable movement by disabling TitleBarInstance.
// Resize handles: BorderTopLeftInstance, BorderTopRightInstance,
//   BorderBottomLeftInstance, BorderBottomRightInstance,
//   BorderTopInstance, BorderBottomInstance,
//   BorderLeftInstance, BorderRightInstance
// Disable any by: window.GetFrameworkElement("BorderXxxInstance").IsEnabled = false;
```

### Image
```csharp
var image = new Image();
// set texture via image.Visual properties
parentPanel.AddChild(image);
```

---

## Layout Containers

### Panel
```csharp
var panel = new Panel();
// Default: sizes to children (RelativeToChildren)
// Set explicit size for top-down layout:
panel.Width = 200;
panel.WidthUnits = Gum.DataTypes.DimensionUnitType.Absolute;
panel.Height = 200;
panel.HeightUnits = Gum.DataTypes.DimensionUnitType.Absolute;
panel.AddChild(someControl);
```

### StackPanel
```csharp
var stack = new StackPanel();
stack.Spacing = 4;  // gap between children

// Vertical (default):
stack.Orientation = Orientation.Vertical;

// Horizontal:
stack.Orientation = Orientation.Horizontal;

stack.AddChild(control1);
stack.AddChild(control2);
```

### Grid

Native grid container that arranges children in rows and columns. Similar to WPF/XAML Grid. **Experimental** — API may change.

> Docs: https://docs.flatredball.com/gum/code/controls/grid

```csharp
var grid = new Grid();
// Grid fills its parent by default (RelativeToParent for BOTH width and height)

// Define rows and columns
grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Absolute) });
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

// Place children at specific cells (zero-based row, column)
var lbl = new Label();
lbl.Text = "Name:";
grid.AddChild(lbl, row: 0, column: 0);

var val = new TextBox();
val.Width = 120;
grid.AddChild(val, row: 0, column: 1);

parent.Children.Add(grid.Visual);
```

**GridLength sizing modes**:
- **Star** (default) — distributes available space proportionally (e.g., 1-star + 2-star in 300px = 100px + 200px)
- **Absolute** — fixed pixel size (`new GridLength(60, GridUnitType.Absolute)` or `new GridLength(60)`)
- **Auto** — sizes to fit the widest/tallest content in that row/column (`GridLength.Auto`)

**RowDefinition/ColumnDefinition** also support `MinHeight`/`MaxHeight` and `MinWidth`/`MaxWidth`.

**Key rules**:
- Always use `grid.AddChild(child, row, column)` — calling `AddChild` without row/column throws `NotSupportedException`
- Do NOT use `grid.Visual.Children.Add()` directly — bypasses the cell system
- Calling `AddChild` twice on the same child moves it to the new cell
- Out-of-range row/column indices clamp to the last valid index
- Use `grid.RemoveChild(child)` to remove

**Critical: Height inside stacked containers**. Grid defaults to `HeightUnits = RelativeToParent` (fill parent). When placing a Grid inside a `TopToBottomStack`, you MUST override this or grids will overlap/collapse:
```csharp
grid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
grid.Visual.Height = 0;  // auto-size to row content
```

**Children don't auto-fill cells**. Grid places children in cells but controls keep their own default width. To make a control fill its Star column, set it explicitly:
```csharp
slider.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
slider.Visual.Width = 0;  // fills the grid cell
```
> **Performance warning**: RelativeToParent sliders inside Grid cause severe lag — Grid fires `RefreshLayout` on every slider value change. Use fixed `slider.Width = 100` until this is resolved in Gum.

**Common patterns** (from `EffectsPanel`):

Slider row — `[label | slider | value]`:
```csharp
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });        // label
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // slider (Star)
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });           // value label
```

Color row — `[label | swatch | hexbox]`:
```csharp
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // label
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // swatch (24x24)
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // hex textbox (Width=80)
```

### Grid Alternative: Column StackPanels

When you need grid-like alignment but want more manual control, use vertical `TopToBottomStack` columns inside a horizontal `LeftToRightStack` container.

**Structure**:
```
[Horizontal ContainerRuntime - LeftToRightStack]
  +-- [Vertical ContainerRuntime - TopToBottomStack] (column 0)
  +-- [Vertical ContainerRuntime - TopToBottomStack] (column 1)
  +-- ...
```

```csharp
var grid = new ContainerRuntime();
grid.HeightUnits = DimensionUnitType.RelativeToChildren;
grid.Height = 0;
grid.WidthUnits = DimensionUnitType.RelativeToChildren;
grid.Width = 0;
grid.ChildrenLayout = ChildrenLayout.LeftToRightStack;
grid.StackSpacing = 4;
parent.Children.Add(grid);

// Label column
var labels = new ContainerRuntime();
labels.HeightUnits = DimensionUnitType.RelativeToChildren;
labels.Height = 0;
labels.WidthUnits = DimensionUnitType.RelativeToChildren;
labels.Width = 0;
labels.ChildrenLayout = ChildrenLayout.TopToBottomStack;
labels.StackSpacing = 4;
grid.Children.Add(labels);

// Value column
var values = new ContainerRuntime();
values.HeightUnits = DimensionUnitType.RelativeToChildren;
values.Height = 0;
values.WidthUnits = DimensionUnitType.RelativeToChildren;
values.Width = 0;
values.ChildrenLayout = ChildrenLayout.TopToBottomStack;
values.StackSpacing = 4;
grid.Children.Add(values);

// Row 1
var lbl1 = new TextRuntime { Text = "Name:", Width = 60 };
labels.Children.Add(lbl1);
var val1 = new TextBox { Width = 120 };
values.Children.Add(val1.Visual);

// Row 2
var lbl2 = new TextRuntime { Text = "Email:", Width = 60 };
labels.Children.Add(lbl2);
var val2 = new TextBox { Width = 120 };
values.Children.Add(val2.Visual);
```

**Key rules**:
- All vertical columns must use the **same `StackSpacing`** so rows align
- Each column's elements must have **consistent heights** (or all use auto-height)
- Use `RelativeToChildren` with `Height = 0` / `Width = 0` on both the outer container and each column
- For empty cells, use a `ContainerRuntime` spacer with explicit height matching the other cells in that row
- Set fixed `Width` on elements within a column for consistent column widths

**When to use**: Prefer the native `Grid` control for simple tabular layouts. Use column StackPanels when you need custom per-column scroll behavior, dynamic column insertion, or the cross/diamond directional layout pattern (see Lessons Learned).

---

## Layout System

### Anchor (positions without changing size)
```csharp
element.Anchor(Gum.Wireframe.Anchor.Center);        // center in parent
element.Anchor(Gum.Wireframe.Anchor.Top);            // top center
element.Anchor(Gum.Wireframe.Anchor.Bottom);         // bottom center
element.Anchor(Gum.Wireframe.Anchor.TopLeft);        // top-left corner
element.Anchor(Gum.Wireframe.Anchor.BottomRight);    // bottom-right
element.Anchor(Gum.Wireframe.Anchor.CenterHorizontally);  // center X only
element.Anchor(Gum.Wireframe.Anchor.CenterVertically);    // center Y only
```

### Dock (positions AND sizes)
```csharp
element.Dock(Gum.Wireframe.Dock.Fill);               // fill parent completely
element.Dock(Gum.Wireframe.Dock.Top);                // fill width, pin to top
element.Dock(Gum.Wireframe.Dock.Bottom);             // fill width, pin to bottom
element.Dock(Gum.Wireframe.Dock.Left);               // fill height, pin to left
element.Dock(Gum.Wireframe.Dock.Right);              // fill height, pin to right
element.Dock(Gum.Wireframe.Dock.FillHorizontally);   // fill width only
element.Dock(Gum.Wireframe.Dock.FillVertically);     // fill height only
element.Dock(Gum.Wireframe.Dock.SizeToChildren);     // size to fit children
```

### Width/Height Units (DimensionUnitType)
```csharp
using Gum.DataTypes;

element.WidthUnits = DimensionUnitType.Absolute;           // pixels (default)
element.WidthUnits = DimensionUnitType.PercentageOfParent;  // 0-100% of parent
element.WidthUnits = DimensionUnitType.RelativeToParent;    // 0 = same as parent, negative = smaller
element.WidthUnits = DimensionUnitType.RelativeToChildren;  // size to fit children + padding
element.WidthUnits = DimensionUnitType.Ratio;               // proportional sharing with siblings

// Same for HeightUnits
```

### X/Y Units (GeneralUnitType)
```csharp
using Gum.Converters;

element.XUnits = GeneralUnitType.PixelsFromSmall;   // from left edge (default)
element.XUnits = GeneralUnitType.PixelsFromLarge;   // from right edge
element.XUnits = GeneralUnitType.PixelsFromMiddle;  // from center
element.XUnits = GeneralUnitType.Percentage;         // percentage of parent

// Same for YUnits (top=small, bottom=large)
```

### X/Y Origin (alignment within element)
```csharp
using RenderingLibrary.Graphics;

element.XOrigin = HorizontalAlignment.Left;    // default
element.XOrigin = HorizontalAlignment.Center;
element.XOrigin = HorizontalAlignment.Right;

element.YOrigin = VerticalAlignment.Top;       // default
element.YOrigin = VerticalAlignment.Center;
element.YOrigin = VerticalAlignment.Bottom;
```

### ChildrenLayout (on ContainerRuntime / Visual)
```csharp
container.ChildrenLayout = Gum.Managers.ChildrenLayout.Regular;           // manual positioning
container.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;  // vertical stack
container.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;  // horizontal stack
container.StackSpacing = 4;   // gap between stacked children
container.WrapsChildren = true; // wrap to next line when full
```

### Margins (via position/size offsets after Dock)
```csharp
// Gum has no Margin/Padding properties. Create margins with offsets:
button.Dock(Gum.Wireframe.Dock.Top);
button.Y = 8;              // top margin (positive = down from top)
button.Width = -16;         // shrink width by 16px total (8px each side)

// Or use an inner Panel:
var innerPanel = new Panel();
innerPanel.Dock(Gum.Wireframe.Dock.Fill);
innerPanel.Width = -16;     // 8px margin on each side
innerPanel.Height = -16;
outerPanel.AddChild(innerPanel);
```

---

## Visual Property Access

Every Forms control has a `.Visual` property (GraphicalUiElement) for advanced layout:

```csharp
// Convenience properties (equivalent):
button.Width = 100;          // same as button.Visual.Width = 100;
button.X = 50;               // same as button.Visual.X = 50;

// Advanced properties only on Visual:
button.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
button.Visual.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
button.Visual.StackSpacing = 4;
button.Visual.WrapsChildren = true;
```

---

## Data Binding

### ViewModel base class
```csharp
using Gum.Mvvm;

public class MyViewModel : ViewModel
{
    public string Name { get => Get<string>(); set => Set(value); }
    public int Count { get => Get<int>(); set => Set(value); }
    public bool IsEnabled { get => Get<bool>(); set => Set(value); }

    // Computed property:
    [DependsOn(nameof(Count))]
    public string CountDisplay => $"Count: {Count}";
}
```

### Binding controls to ViewModel
```csharp
// Set BindingContext on parent — children inherit it
parentPanel.Visual.BindingContext = viewModel;

// Or set on individual control:
label.Visual.BindingContext = viewModel;

// Bind a property:
label.SetBinding(nameof(Label.Text), nameof(MyViewModel.Name));
textBox.SetBinding(nameof(TextBox.Text), nameof(MyViewModel.Name));
```

---

## Adding to Visual Tree

```csharp
// Add Forms control to Forms container:
panel.AddChild(button);
stackPanel.AddChild(label);

// Add to GUM root (top-level):
myScreen.AddToRoot();       // extension method

// Remove from root:
myScreen.RemoveFromRoot();  // extension method

// Add raw runtime to container:
container.Children.Add(spriteRuntime);

// Modal/Popup roots:
FrameworkElement.ModalRoot.AddChild(window);
FrameworkElement.PopupRoot.AddChild(window);
```

---

## Creating Custom Controls

Inherit from `Panel`, `StackPanel`, `Window`, or `FrameworkElement`:

```csharp
public class MyCustomControl : Panel
{
    public MyCustomControl()
    {
        // Panel default: sizes to children
        this.Width = 24;   // extra margin
        this.Height = 24;

        var background = new NineSliceRuntime();
        this.AddChild(background);
        background.Dock(Gum.Wireframe.Dock.Fill);
        background.Texture = Styling.ActiveStyle.SpriteSheet;
        background.ApplyState(Styling.ActiveStyle.NineSlice.Panel);

        var innerPanel = new StackPanel();
        innerPanel.Spacing = 10;
        innerPanel.Anchor(Gum.Wireframe.Anchor.Center);
        this.AddChild(innerPanel);

        var label = new Label();
        label.Text = "Hello";
        innerPanel.AddChild(label);

        var textBox = new TextBox();
        textBox.Width = 0;
        textBox.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        innerPanel.AddChild(textBox);
    }
}

// Usage:
var dialog = new MyCustomControl();
dialog.AddToRoot();
dialog.Anchor(Gum.Wireframe.Anchor.Center);
```

---

## Styling (V3)

The V3 styling system uses `Gum.Forms.DefaultVisuals.V3.Styling`. Create a new instance, customize colors, set as `ActiveStyle` BEFORE creating controls.

```csharp
using Gum.Forms.DefaultVisuals.V3;

// In Initialize(), AFTER GumService.Default.Initialize() but BEFORE creating any controls:

// Create a new style using the existing sprite sheet
var darkStyle = new Styling(Styling.ActiveStyle.SpriteSheet, useDefaults: true);
darkStyle.Colors.Primary = new Color(0, 120, 212);     // accent/button color
darkStyle.Colors.InputBackground = new Color(45, 45, 48);
darkStyle.Colors.TextPrimary = new Color(204, 204, 204);
darkStyle.Colors.TextMuted = new Color(136, 136, 136);
darkStyle.Colors.Accent = new Color(0, 120, 212);
darkStyle.Colors.SurfaceVariant = new Color(60, 60, 60);
darkStyle.Colors.DarkGray = new Color(37, 37, 38);
darkStyle.Colors.Gray = new Color(60, 60, 60);
darkStyle.Colors.LightGray = new Color(136, 136, 136);
darkStyle.Colors.Black = new Color(30, 30, 30);
darkStyle.Colors.White = new Color(204, 204, 204);
Styling.ActiveStyle = darkStyle;

// Now create controls — they inherit the dark style
var button = new Button(); // uses darkStyle.Colors.Primary, etc.
```

### Available Colors properties
```csharp
Colors.Black           // background-level dark
Colors.DarkGray        // panel backgrounds
Colors.Gray            // borders, muted elements
Colors.LightGray       // secondary text
Colors.White           // primary text, icons

Colors.Primary         // buttons, active states, accent elements
Colors.Success         // success indicators
Colors.Warning         // warnings, focus indicators
Colors.Danger          // error states
Colors.Accent          // highlights, selections
Colors.InputBackground // TextBox, ComboBox, ListBox backgrounds
Colors.SurfaceVariant  // ScrollBar tracks
Colors.IconDefault     // icon tint color
Colors.TextPrimary     // primary text color
Colors.TextMuted       // placeholder, muted text

// Shading percentages (used by V3 controls for hover/press states)
Colors.PercentDarken         // default: -15f
Colors.PercentLighten        // default: 15f
Colors.PercentGreyScaleDarken    // default: -35f
Colors.PercentGreyScaleLighten   // default: 20f
Colors.PercentGreyScaleSuperDarken // default: -50f
```

### Per-control styling (override for specific controls)
```csharp
// Save current style, apply custom, create controls, restore
var savedStyle = Styling.ActiveStyle;
var customStyle = new Styling(Styling.ActiveStyle.SpriteSheet, true);
customStyle.Colors.Primary = Color.Red;
Styling.ActiveStyle = customStyle;

var redButton = new Button(); // uses red primary
Styling.ActiveStyle = savedStyle; // restore for subsequent controls
```

### Using NineSlice backgrounds from the style
```csharp
var bg = new NineSliceRuntime();
bg.Texture = Styling.ActiveStyle.SpriteSheet;
bg.ApplyState(Styling.ActiveStyle.NineSlice.Panel);    // panel background
bg.ApplyState(Styling.ActiveStyle.NineSlice.Bordered);  // bordered rectangle
bg.ApplyState(Styling.ActiveStyle.NineSlice.Solid);     // solid fill
bg.Dock(Gum.Wireframe.Dock.Fill);
```

### Using Icons from the style
```csharp
var icon = new SpriteRuntime();
icon.Texture = Styling.ActiveStyle.SpriteSheet;
icon.ApplyState(Styling.ActiveStyle.Icons.Gear);    // gear icon
icon.ApplyState(Styling.ActiveStyle.Icons.Check);   // checkmark
icon.ApplyState(Styling.ActiveStyle.Icons.Close);   // X close
// ... many more (Arrow1, Heart, Star, Trash, Warning, etc.)
```

---

## Raw Runtime Types (non-Forms)

For direct visual elements without Forms interaction logic:

| Type | Purpose |
|------|---------|
| `ContainerRuntime` | Layout container (no visuals) |
| `TextRuntime` | Text display |
| `ColoredRectangleRuntime` | Solid color rectangle |
| `SpriteRuntime` | Texture/image display |
| `NineSliceRuntime` | Scalable bordered rectangle |
| `RectangleRuntime` | Line-drawn rectangle |
| `PolygonRuntime` | Polygon shape |

```csharp
using MonoGameGum.GueDeriving;

var sprite = new SpriteRuntime();
sprite.SourceFileName = "myimage.png";
sprite.Width = 100;
sprite.Height = 100;
container.Children.Add(sprite);  // raw runtimes use Children.Add

var text = new TextRuntime();
text.Text = "Hello";
container.Children.Add(text);
```

---

## Common Three-Column Layout Pattern

From the official FrameworkElementExampleScreen sample:

```csharp
internal class MyScreen : ContainerRuntime
{
    public MyScreen()
    {
        this.Dock(Gum.Wireframe.Dock.Fill);

        // Menu at top
        var menu = new Menu();
        var fileItem = new MenuItem();
        fileItem.Header = "File";
        menu.Items.Add(fileItem);
        this.AddChild(menu);

        // Column 1
        var col1 = new StackPanel();
        col1.Spacing = 4;
        col1.Y = 40;  // below menu
        this.AddChild(col1);
        // add controls to col1...

        // Column 2
        var col2 = new StackPanel();
        col2.Y = 40;
        col2.X = 260;
        col2.Spacing = 4;
        this.AddChild(col2);
        // add controls to col2...

        // Column 3
        var col3 = new StackPanel();
        col3.Y = 40;
        col3.X = 520;
        col3.Spacing = 4;
        this.AddChild(col3);
        // add controls to col3...
    }
}
```

---

## Key Gotchas

1. **Must call `GumService.Default.Draw()`** in your `Draw()` method or nothing renders
2. **`AddChild()`** for Forms controls, **`Children.Add()`** for raw runtimes
3. **Panel default sizing is `RelativeToChildren`** — set explicit `Absolute` size for top-down layouts
4. **Circular dependencies**: if parent sizes to children AND children size to parent, you get 0-width elements
5. **`Dock()` changes both position AND size**; `Anchor()` changes only position
6. **No Margin/Padding properties** — use position offsets and negative size values after Dock
7. **`button.Click`** is on the Button directly (not `button.Visual.Click`)
8. **`MenuItem.Clicked`** for menu item events (not `Click`)
9. **`Window`** is in `Gum.Forms` namespace (not `Gum.Forms.Controls`)

---

## Lessons Learned (KernSmith UI Development)

### Don't force heights on controls
V3 controls (Button, TextBox, CheckBox, RadioButton, ComboBox, Label) size to their content by default. Setting explicit Height makes buttons 2-3x taller than needed. **Only set Height on**: ScrollViewer, ListBox, Window dialogs, status bars, spacer rectangles, and the small int boxes in compact layouts.

```csharp
// BAD — forces oversized button
var btn = new Button();
btn.Height = 28;  // unnecessary, makes it too tall

// GOOD — let GUM size it
var btn = new Button();
btn.Text = "Click Me";
// no Height set — sizes to content
```

### Use RelativeToParent for widths inside containers
Controls inside a panel should use `RelativeToParent` widths, not hardcoded pixel values. Hardcoded widths break when the panel resizes.

```csharp
// Acceptable for known fixed-width panels:
btn.Width = 230;

// Better for responsive layouts:
btn.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
btn.Width = -16;  // fill parent minus 16px margin
```

### PropertyChanged for re-generation: toggle false→true
GUM's `Get/Set` pattern only fires `PropertyChanged` when the value actually changes. If `HasResult` is already `true` and you set it to `true` again, nothing fires. To force an update on repeated generation:
```csharp
HasResult = false;
HasResult = true;  // now PropertyChanged fires
```

### Cross/diamond layout for directional inputs (padding, etc.)
Use 3 vertical StackPanels in a horizontal StackPanel:
```
Column 1 (vertical): [empty] [Left]  [empty]
Column 2 (vertical): [Up]    [label] [Down]
Column 3 (vertical): [empty] [Right] [empty]
```
The empty spacers need explicit Height matching the TextBox height to align properly.

### Atlas sizes should be ComboBox dropdowns, not TextBoxes
Nobody types "2047" — use power-of-two values: 128, 256, 512, 1024, 2048, 4096, 8192.

### Section headers with background bars
Wrap a `TextRuntime` in a `ContainerRuntime` with a `ColoredRectangleRuntime` background for visual grouping:
```csharp
var container = new ContainerRuntime();
container.Width = 0;
container.WidthUnits = DimensionUnitType.RelativeToParent;
container.Height = 22;

var bg = new ColoredRectangleRuntime();
bg.Dock(Gum.Wireframe.Dock.Fill);
bg.Color = new Color(50, 50, 55);
container.Children.Add(bg);

var header = new TextRuntime();
header.Text = "SECTION NAME";
header.Color = accentColor;
header.X = 6; header.Y = 2;
container.Children.Add(header);
```

### Collapsible sections with background
Wrap content in a `ContainerRuntime` with a subtle background color, toggle `Visible` on the wrapper (not individual controls):
```csharp
var wrapper = new ContainerRuntime();
wrapper.HeightUnits = DimensionUnitType.RelativeToChildren;
wrapper.Height = 8; // padding

var bg = new ColoredRectangleRuntime();
bg.Dock(Gum.Wireframe.Dock.Fill);
bg.Color = new Color(40, 40, 44);
wrapper.Children.Add(bg);

var content = new StackPanel();
content.Visual.X = 4; content.Visual.Y = 4;
wrapper.Children.Add(content.Visual);

// Toggle: wrapper.Visible = true/false;
```

### Engine preset buttons — abbreviate and expand on selection
Use short labels (2-3 chars) that expand to full name when clicked:
```csharp
btn.Text = isSelected ? preset.Name : preset.ShortName;
btn.Width = isSelected ? 80 : 40;
btn.IsEnabled = !isSelected;  // disabled state = visual distinction
```

### Styling must be set BEFORE creating controls
`Styling.ActiveStyle` only affects controls created AFTER it's set. Always configure styling right after `GumService.Default.Initialize()`, before any UI construction.

### Status bar: hide empty separators
When using `|` separators between status fields, hide them when adjacent labels are empty to avoid "Ready | | | |".

### Don't read DLL/XML files for API info
Use WebFetch on documentation URLs or read the GUM source at `c:\git\gum\` if available locally. Never grep NuGet package DLLs or XML files.
