# Phase 61 — Font Loading & Character Selection

> **Status**: Complete
> **Completed**: 2026-03-22. NativeFileDialogSharp and MonoGame.Extended removed — replaced with GUM-based file browser and standard MonoGame APIs. Character grid uses RadioButton presets + Unicode block CheckBoxes + text input instead of BMFont-style clickable cell grid. Preset management deferred as unnecessary.
> **Created**: 2026-03-21
> **Goal**: Build comprehensive font loading and character selection capabilities inspired by BMFont's character grid and Hiero's text-based input, using GUM UI + MonoGame + MonoGame.Extended.

---

## Overview

This phase implements the font browser, font information display, and character selection subsystem for the KernSmith UI application. Character selection is the single most important user-facing feature of a bitmap font generator — it determines which glyphs appear in the output atlas. The design draws from two proven tools:

- **BMFont** — Interactive Unicode grid where every codepoint is visible and clickable, organized by Unicode block. The main window IS the character grid with a Unicode block sidebar.
- **Hiero (libGDX)** — Text-based input where users paste sample text and the tool extracts unique characters.

KernSmith's UI combines both approaches and adds preset management, drag-and-drop font loading, and a system font browser backed by `ISystemFontProvider`.

### Framework Stack

| Technology | Role |
|------------|------|
| **MonoGame (DesktopGL)** | Game loop, SpriteBatch rendering, Texture2D, input handling |
| **GUM UI (code-only)** | Layout engine, controls, ViewModel binding via `SetBinding()` |
| **MonoGame.Extended** | Camera, input helpers, sprite utilities |
| **NativeFileDialogSharp** | Native file open/save dialogs (cross-platform) |

### GUM Control Reference

This phase uses the following GUM controls and primitives:

| GUM Control | Usage in Phase 61 |
|-------------|-------------------|
| `Button` | Toolbar actions, Apply, Clear, Import, Save Preset |
| `CheckBox` | Unicode block toggles, Show All Blocks toggle |
| `ComboBox` | Preset selector, sample text dropdown |
| `Label` | Section headers, key-value labels, summary text |
| `ListBox` | System font browser, named instances, preset management |
| `RadioButton` | Add vs. Replace mode toggle |
| `ScrollViewer` | Font info panel scroll, text input scroll, block sidebar scroll |
| `Slider` | Grid zoom, variable font axis preview |
| `StackPanel` | Vertical/horizontal layout containers |
| `TextBox` | Font search, text input area, codepoint jump input, block filter |
| `Window` | TTC face selector dialog, jump-to-codepoint dialog, preset management dialog |
| `Splitter` | Resizable boundary between grid and sidebar |
| **Primitives** | |
| `ContainerRuntime` | Grid cell container, panel roots, tooltip container |
| `ColoredRectangleRuntime` | Grid cell backgrounds, selection rectangle overlay, badge backgrounds |
| `SpriteRuntime` | Font preview image, glyph thumbnail display |
| `TextRuntime` | Grid cell characters, codepoint hex labels, info panel values |
| `NineSliceRuntime` | Cell borders, panel borders, focus indicators |

### Library Types Referenced

This phase builds UI on top of existing KernSmith library types:

| Type | Namespace | Role |
|------|-----------|------|
| `ISystemFontProvider` | `KernSmith.Font` | Enumerates installed system fonts |
| `SystemFontInfo` | `KernSmith.Font.Models` | Family, style, file path, face index |
| `FontInfo` | `KernSmith.Font.Models` | Full parsed font metadata |
| `CharacterSet` | `KernSmith` | Defines which codepoints to include |
| `VariationAxis` | `KernSmith.Font.Tables` | Variable font axis definition |
| `NamedInstance` | `KernSmith.Font.Tables` | Variable font preset style |
| `AtlasSizeEstimator` | `KernSmith.Atlas` | Estimates texture dimensions from glyph count |
| `FontGeneratorOptions` | `KernSmith` | Options that receive the final `CharacterSet` |

### MVVM Pattern (GUM ViewModel)

All ViewModels in this phase inherit from GUM's `ViewModel` base class and use `Get<T>()` / `Set<T>()` for property change notification. Views bind via `SetBinding()`.

```csharp
using Gum.Mvvm;

public class CharacterGridViewModel : ViewModel
{
    public int SelectedCount
    {
        get => Get<int>();
        set => Set(value);
    }

    public bool HasFont
    {
        get => Get<bool>();
        set => Set(value);
    }
}
```

View binding:

```csharp
var label = new Label();
label.SetBinding(
    nameof(Label.Text),
    nameof(viewModel.SelectedCount));
label.BindingContext = viewModel;
```

---

## Wave 1: Enhanced Font Loading

### Objective

Give users three ways to load a font: browse system fonts, open a file, or drag-and-drop. Display complete font metadata once loaded.

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 1.1 | System font enumeration service | Create `SystemFontBrowserViewModel : ViewModel` that calls `ISystemFontProvider.GetInstalledFonts()` on a `Task.Run` background thread. Cache the result for the session lifetime. Expose `IReadOnlyList<SystemFontInfo>` grouped by `FamilyName`. Use `Get`/`Set` properties for `FontFamilies`, `IsLoading`, `SearchFilter`. | M |
| 1.2 | System font browser panel | GUM `ListBox` in a left sidebar `ContainerRuntime`. Each item shows the family name as a `TextRuntime` with child items for styles (Regular, Bold, Italic, etc.). Double-click or press Enter to load the selected font. The `ListBox` uses GUM's built-in virtualization for the potentially large font list. Wrap in a `ScrollViewer` for overflow. | L |
| 1.3 | Font search/filter box | GUM `TextBox` above the system font list with placeholder text "Search fonts...". Bind `Text` property to `SystemFontBrowserViewModel.SearchFilter`. Filter the font list in real-time as the user types (case-insensitive substring match against `FamilyName`). Add a `Button` with "X" label to clear the filter. Handle `Ctrl+F` via MonoGame `KeyboardState` to focus the search box. | S |
| 1.4 | Font file open dialog | File > Open Font triggers `NativeFileDialogSharp.Dialog.FileOpen()` with filter `ttf,otf,woff,ttc`. Loads the selected file bytes via `File.ReadAllBytesAsync` and passes to font reader. If the file is a `.ttc` collection, proceed to task 1.5 before loading. Register `Ctrl+O` in the MonoGame input handler. | S |
| 1.5 | TTC face index selector | When a `.ttc` file is opened, show a GUM `Window` (modal) containing a `ListBox` of all faces by index and name (read `FamilyName` + `StyleName` for each face index). User selects which face to load. Confirm with OK `Button`, cancel with Cancel `Button` or Escape key. Store the selected `FaceIndex` on the view model. | M |
| 1.6 | Drag-and-drop font loading | Register `MonoGame.Game.Window.FileDrop` event handler. Accept `.ttf`, `.otf`, `.woff`, `.ttc` files. On drop, read the file bytes and load as if the user had used File > Open. If multiple files are dropped, load only the first and show a toast notification: "Loaded {filename}. Drop one file at a time." For `.ttc`, trigger the face selector dialog (task 1.5). | M |
| 1.7 | Recent fonts list | File > Recent Fonts submenu using GUM `StackPanel` with `Button` items. Store as a JSON array in `%APPDATA%/KernSmith/recent-fonts.json` (or platform equivalent via `Environment.SpecialFolder.ApplicationData`). Each entry stores: `FilePath`, `FamilyName`, `StyleName`, `FaceIndex`, `LastUsed` (UTC timestamp). Limit to 10 entries. Clicking an entry loads that font. If the file no longer exists, show a toast and remove from the list. Use `System.Text.Json` for serialization. | M |
| 1.8 | Font loading progress indicator | Show a `ColoredRectangleRuntime` overlay with 50% alpha black background and a `TextRuntime` "Loading font..." centered on the main area while `IFontReader.Read()` runs on a background thread. Disable font-dependent UI controls (set `IsEnabled = false` on relevant GUM controls) until loading completes. On failure, show an error `Window` dialog with the exception message and reset to the no-font state. | S |
| 1.9 | Font load event propagation | When a font is successfully loaded, update `FontWorkspaceViewModel.CurrentFont` property (which fires `PropertyChanged`). All bound views (character grid, info panel, generation pipeline) react via `SetBinding`. Clear previous character selection state if the font family changes. Preserve character selection if only the style changes within the same family. | M |
| 1.10 | System font browser toggle | View > System Font Browser menu item toggles visibility of the font browser `ContainerRuntime` (set `Visible = true/false`). Handle `Ctrl+Shift+F` via MonoGame input. Default state: hidden. Panel appears as a docked sidebar on the left side. Remember visibility state across sessions via user settings JSON file. | S |

### Keyboard Shortcuts (Wave 1)

| Shortcut | Action | Implementation |
|----------|--------|----------------|
| `Ctrl+O` | Open font file dialog | MonoGame `KeyboardState` check in `Update()` |
| `Ctrl+F` | Focus font search box (when browser visible) | MonoGame input handler |
| `Ctrl+Shift+F` | Toggle system font browser panel | MonoGame input handler |
| `Enter` | Load selected font in browser | GUM `ListBox` selection event |
| `Escape` | Close TTC face selector dialog | GUM `Window` key handler |

### Toast Notification System

Since GUM has no built-in toast/notification control, build a lightweight one:

- `ToastRuntime` — custom composite: `ContainerRuntime` + `NineSliceRuntime` background + `TextRuntime` message
- Positioned at bottom-center of the screen
- Fades in over 200ms, stays for 3 seconds, fades out over 300ms (animate `Alpha` in `Update()`)
- Queue multiple toasts; show one at a time
- Color-coded: info (blue), warning (yellow), error (red)

---

## Wave 2: Font Information Panel

### Objective

Display all parsed `FontInfo` metadata in a structured, readable panel so users understand what font they are working with before configuring generation.

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 2.1 | Font info panel layout | Create a `ContainerRuntime` panel (right side or tab-switched area) with a `TextRuntime` header showing `FontInfo.FamilyName` + `FontInfo.StyleName` in large font. Below that, a `ScrollViewer` containing a `StackPanel` with grouped property sections. Each section is a collapsible group built from a `Button` header (click to toggle) + `ContainerRuntime` body that shows/hides. | M |
| 2.2 | Primary metrics section | Always-visible `StackPanel` section showing: `FamilyName`, `StyleName`, `NumGlyphs` (formatted with thousands separator), `UnitsPerEm`, `Ascender`, `Descender`, `LineGap`, computed `LineHeight`. Display as a two-column layout using `StackPanel` rows, each with a `Label` on the left and a `Label` with the value on the right. Bind all values from `FontInfoViewModel`. | S |
| 2.3 | Style flags row | Horizontal `StackPanel` of badge-style indicators built from `ColoredRectangleRuntime` (rounded corners via `NineSliceRuntime`) + `TextRuntime` label: "Bold" (visible if `IsBold`), "Italic" (visible if `IsItalic`), "Monospace" (visible if `IsFixedPitch`), "Color" (visible if `HasColorGlyphs`). Each badge has an accent color (blue for Bold, italic for Italic, green for Monospace, orange for Color). Hidden badges set `Visible = false`. | S |
| 2.4 | Variable font axes section | Conditional section visible only when `FontInfo.VariationAxes` is non-null and non-empty. Show a vertical `StackPanel` with rows: Axis Name (or Tag if Name is null), Tag, Min, Default, Max. Each axis row also shows a GUM `Slider` control bound to the axis range so users can preview axis values. Slider range set via `Minimum`/`Maximum` properties. A `Label` beside the slider shows the current numeric value. Axis application is informational only for now. | M |
| 2.5 | Named instances list | Conditional section visible only when `FontInfo.NamedInstances` is non-null. Show a GUM `ListBox` of named instance strings (e.g., "Thin", "Light", "Regular", "Bold", "Black"). Selecting one updates the axis sliders in 2.4 to reflect that instance's coordinates. Display-only — applying variation requires future work. | S |
| 2.6 | OS/2 metrics section | Collapsible section labeled "OS/2 Table" (collapsed by default). Shows `Os2Metrics` properties if `FontInfo.Os2` is non-null: `WeightClass`, `WidthClass`, `TypoAscender`, `TypoDescender`, `TypoLineGap`, `WinAscent`, `WinDescent`, `XHeight`, `CapHeight`, `Panose`, `FirstCharIndex`, `LastCharIndex`. Two-column key-value layout using `StackPanel` rows with `Label` pairs. **Note:** Additional OS/2 fields (XAvgCharWidth, SubscriptSize, SuperscriptSize, StrikeoutSize/Position) are tracked as core library prerequisites in Phase 55. | S |
| 2.7 | Head table section | Collapsible section labeled "Head Table" (collapsed by default). Shows `HeadTable` properties if `FontInfo.Head` is non-null: `UnitsPerEm`, `Created`, `Modified`, `XMin`, `YMin`, `XMax`, `YMax` (font bounding box), `IndexToLocFormat`. `Created` and `Modified` are `long` raw timestamps — convert to human-readable dates for display. **Note:** Additional head table fields (MacStyle, LowestRecPPEM) are tracked as core library prerequisites in Phase 55. | S |
| 2.8 | Hhea table section | Collapsible section labeled "Horizontal Header" (collapsed by default). Shows `HheaTable` properties if `FontInfo.Hhea` is non-null: `Ascender`, `Descender`, `LineGap`, `AdvanceWidthMax`, `NumberOfHMetrics`. **Note:** Additional hhea fields (MinLeftSideBearing, MinRightSideBearing, XMaxExtent) are tracked as core library prerequisites in Phase 55. | S |
| 2.9 | Name table section | Collapsible section labeled "Name Table" (collapsed by default). Shows `NameInfo` properties if `FontInfo.Names` is non-null: `FontFamily`, `FontSubfamily`, `FullName`, `PostScriptName`, `Copyright`, `Trademark`. Long strings are truncated with "..." and show full value in a hover tooltip (`ContainerRuntime` popup positioned near the mouse). **Note:** Additional name table fields (UniqueId, Version, Manufacturer, Designer, Description, License, LicenseUrl) are tracked as core library prerequisites in Phase 55. | M |
| 2.10 | Font preview text | At the top of the info panel, below the family/style header, show a GUM `TextBox` with default text "The quick brown fox jumps over the lazy dog". The text is rasterized using `IRasterizer` at 24px into a `Texture2D`, displayed via a `SpriteRuntime` below the text box. User can edit the preview text. Re-renders on text change with a 300ms debounce (tracked in `Update()` with a timer). | L |
| 2.11 | Copy font info to clipboard | `Button` labeled "Copy Info" at the top of the info panel. Uses `SDL2.SDL.SDL_SetClipboardText()` (MonoGame's underlying SDL layer) to copy a formatted plain-text summary of all visible font metadata. Format: one property per line, e.g., `Family: Roboto\nStyle: Regular\nGlyphs: 1,294\n...`. | S |
| 2.12 | Empty state | When no font is loaded, the info panel shows centered `TextRuntime` messages: "No font loaded" (large) and "Open a font file or select from the system font browser" (smaller, muted color). Include an "Open Font..." `Button` that triggers the same action as `Ctrl+O`. | S |

### Collapsible Section Pattern (GUM)

Since GUM has no built-in `Expander` control, build a reusable composite:

```csharp
public class CollapsibleSection
{
    public ContainerRuntime Root { get; }       // Outer container
    public Button Header { get; }               // Click to toggle
    public ContainerRuntime Content { get; }    // Body, toggled visible/hidden
    public bool IsExpanded { get; set; }

    public CollapsibleSection(string title, bool initiallyExpanded = true)
    {
        Root = new ContainerRuntime { ChildrenLayout = ChildrenLayout.TopToBottomStack };
        Header = new Button { Text = (initiallyExpanded ? "v " : "> ") + title };
        Content = new ContainerRuntime
        {
            ChildrenLayout = ChildrenLayout.TopToBottomStack,
            Visible = initiallyExpanded
        };
        IsExpanded = initiallyExpanded;
        Header.Click += (_, _) => Toggle();
        Root.Children.Add(Header);
        Root.Children.Add(Content);
    }

    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        Content.Visible = IsExpanded;
        Header.Text = (IsExpanded ? "v " : "> ") + Header.Text[2..];
    }
}
```

---

## Wave 3: Interactive Character Grid (BMFont-Inspired)

### Objective

Build a scrollable, zoomable grid of all Unicode codepoints available in the loaded font. Users click cells to select/deselect characters for inclusion in the generated bitmap font. This is a fully custom GUM component built from primitives — no built-in grid control exists in GUM.

### Architecture: Custom Grid Component

The character grid is the most complex UI element in the application. It is built entirely from GUM primitives using manual layout and virtualized rendering:

```
CharacterGridRuntime (ContainerRuntime)
  +-- RowHeaderColumn (ContainerRuntime, fixed width)
  |     +-- TextRuntime per visible row ("000", "001", ..., "FFF")
  +-- GridViewport (ContainerRuntime, clips children)
  |     +-- GridCanvas (ContainerRuntime, absolute positioning)
  |           +-- [CellRuntime] per visible cell (virtualized pool)
  |           +-- SelectionRectangle (ColoredRectangleRuntime, overlay)
  +-- ColumnHeaderRow (ContainerRuntime, fixed height)
        +-- TextRuntime per column ("0", "1", ..., "F")

CellRuntime (ContainerRuntime, fixed size)
  +-- Background (ColoredRectangleRuntime)
  +-- CharLabel (TextRuntime, centered)
  +-- HexLabel (TextRuntime, bottom, 8px)
  +-- FocusBorder (NineSliceRuntime, hidden by default)
```

Virtualization strategy:
- Maintain a pool of `CellRuntime` instances equal to `(viewportRows + 2) * 16` (16 columns for hex grid)
- On scroll, recycle off-screen cells by updating their position, text, and visual state
- Track scroll position as a codepoint offset; convert to pixel offset for positioning
- Use MonoGame's `Update()` loop to check scroll velocity and recycle cells each frame

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 3.1 | Character grid data model | Create `CharacterGridViewModel : ViewModel` with properties: `HashSet<int> SelectedCodepoints` (via `Get`/`Set`), `HashSet<int> AvailableCodepoints` (from `FontInfo.AvailableCodepoints`). `GridCell` record: `int Codepoint`, `string DisplayChar`, `string UnicodeName`, `bool IsAvailable`, `bool IsSelected`. The grid covers U+0000 through U+FFFF (Basic Multilingual Plane) by default. Pre-compute `AvailableCodepoints` into a `BitArray` at font load time for O(1) lookup. | M |
| 3.2 | Virtualized grid rendering | Build the `CharacterGridRuntime` custom component as described above. Each cell is a fixed-size `ContainerRuntime` (default 32x32 pixels). Use object pooling: create enough `CellRuntime` instances to fill the viewport + 2 rows of buffer. On scroll (handled via MonoGame `MouseState.ScrollWheelValue` delta), shift cell positions and recycle. Only cells in the visible viewport are rendered. Target: 60fps scrolling through 65,536 cells with no jank. | XL |
| 3.3 | Cell visual states | Each `CellRuntime` renders with distinct visual states by updating `ColoredRectangleRuntime` colors: **Selected** — blue background (`#2196F3`, RGB 33/150/243) with white `TextRuntime`; **Available** — white background with dark `TextRuntime`; **Unavailable** — light gray background (`#E0E0E0`, RGB 224/224/224) with medium gray text at 50% alpha; **Hovered** — light blue `NineSliceRuntime` border (`#90CAF9`), 2px. The character is rendered as a `TextRuntime` centered in the cell. Below the character, show the hex codepoint in an 8px `TextRuntime` (e.g., "41" for U+0041). | M |
| 3.4 | Single-click selection toggle | Handle MonoGame `MouseState` left-click in `Update()`. Hit-test against cell positions to determine which codepoint was clicked. If available, toggle its selection state in `SelectedCodepoints`. Update the `CellRuntime` visual state. Unavailable cells ignore clicks. Fire a `SelectionChanged` event (C# event or `ViewModel` property change) that the summary panel and generation pipeline observe. | S |
| 3.5 | Shift+click range selection | Track the last-clicked cell codepoint. When `Shift+Click` occurs (check `KeyboardState.IsKeyDown(Keys.LeftShift)`), select all available cells between the last-clicked codepoint and the current codepoint (inclusive). If the last-clicked cell was being deselected, deselect the range instead. Range follows reading order (left-to-right, top-to-bottom = ascending codepoint order). | M |
| 3.6 | Ctrl+click multi-select | `Ctrl+Click` (check `KeyboardState.IsKeyDown(Keys.LeftControl)`) toggles a single cell without affecting other selections. Standard click (no modifier) also toggles single cells. Both behaviors are identical for individual cells but Ctrl distinguishes intent when combined with other gestures. | S |
| 3.7 | Drag-to-select rectangle | On mouse down + drag, draw a translucent blue `ColoredRectangleRuntime` selection rectangle overlay (alpha 0.3, blue tint). Track drag start/end positions in `Update()` via `MouseState`. On mouse up, toggle selection for all available cells within the rectangle bounds (hit-test by comparing cell positions to rectangle bounds). Hold `Ctrl` while dragging to add to existing selection; without `Ctrl`, replace selection with the dragged region. | L |
| 3.8 | Hover tooltip | When hovering over a cell for 500ms (track hover time in `Update()`), show a custom tooltip built from `ContainerRuntime` + `NineSliceRuntime` background + `TextRuntime` lines: Unicode codepoint (`U+0041`), character name ("LATIN CAPITAL LETTER A"), decimal value (65), UTF-8 byte sequence (`0x41`), and category (e.g., "Letter, uppercase"). Position the tooltip near the mouse cursor. Hide when the mouse moves to a different cell. | M |
| 3.9 | Grid zoom control | A GUM `Slider` in the grid toolbar controlling cell size from 16px to 64px (default 32px). `Label` shows current size. Zoom in/out also available via `Ctrl+MouseWheel` (check `KeyboardState` + `MouseState.ScrollWheelValue` delta). On zoom change, resize all pooled `CellRuntime` instances and recalculate the grid layout. Maintain scroll position by keeping the center of the viewport at the same codepoint after zoom. | M |
| 3.10 | Grid row/column headers | Left margin `ContainerRuntime` shows row labels as `TextRuntime` instances with the upper codepoint prefix (e.g., "004" for U+0040-U+004F). Top margin shows column headers "0"-"F" as `TextRuntime`. This mirrors BMFont's hex-grid layout where each row is 16 cells and the row label is the high nibbles. Headers scroll in sync with the grid content (row headers scroll vertically, column headers are fixed). | M |
| 3.11 | Keyboard navigation | Handle `KeyboardState` in `Update()`: Arrow keys move a focus `NineSliceRuntime` rectangle between cells. `Space` toggles selection of the focused cell. `Shift+Arrow` extends selection range. `Home` jumps to first available character. `End` jumps to last available character. `PageUp`/`PageDown` scroll by one viewport height. `Ctrl+A` selects all available characters. Track `focusedCodepoint` as an integer. | M |
| 3.12 | Jump-to-codepoint dialog | Toolbar `Button` "Go to..." or `Ctrl+G`. Opens a GUM `Window` (modal) with a `TextBox` for codepoint input (hex format: "0041" or "U+0041", or paste a single character). On OK, scroll the grid to center that codepoint and briefly highlight the cell with a pulse animation (lerp the `NineSliceRuntime` border alpha from 1.0 to 0.0 over 1 second in `Update()`). Input validation: show a red `Label` error message for invalid codepoints. | M |
| 3.13 | Search by character name | Toolbar `TextBox` with placeholder "Search by name...". As user types (debounced 300ms via timer in `Update()`), search the Unicode name database. Show results in a `ListBox` dropdown (positioned below the search box) with up to 20 matching entries. Clicking a result scrolls the grid to that codepoint and selects it. Example: typing "arrow" shows "RIGHTWARDS ARROW (U+2192)", "LEFTWARDS ARROW (U+2190)", etc. | L |
| 3.14 | Grid performance optimization | Pre-compute cell availability at font load time into a `BitArray(65536)`. Cache `CellRuntime` pool — recycle rather than create/destroy. Limit re-render to dirty cells when selection changes (track dirty set per frame). Use `ColoredRectangleRuntime` color updates rather than recreating primitives. Profile to ensure < 16ms frame time during scroll on a mid-range machine. Target: zero GC allocations during scroll. | M |
| 3.15 | Grid context menu | Right-click on a cell or selection shows a custom popup menu built from `ContainerRuntime` + `Button` items: "Select Unicode Block" (selects all available chars in this cell's block), "Deselect Unicode Block", "Copy Character" (SDL clipboard), "Copy Codepoint (U+XXXX)", "Add Range..." (opens a dialog to enter start-end range). Popup is positioned at mouse cursor and dismissed on click-away or Escape. | S |

### Keyboard Shortcuts (Wave 3)

| Shortcut | Action | Implementation |
|----------|--------|----------------|
| `Ctrl+G` | Jump to codepoint dialog | MonoGame `KeyboardState` |
| `Ctrl+A` | Select all available characters | MonoGame `KeyboardState` |
| `Ctrl+MouseWheel` | Zoom grid in/out | MonoGame `KeyboardState` + `MouseState` |
| `Space` | Toggle selection of focused cell | MonoGame `KeyboardState` |
| `Shift+Click` | Range selection | MonoGame `KeyboardState` + `MouseState` |
| `Ctrl+Click` | Toggle single cell (additive) | MonoGame `KeyboardState` + `MouseState` |
| `Arrow Keys` | Move focus in grid | MonoGame `KeyboardState` |
| `Shift+Arrow` | Extend selection | MonoGame `KeyboardState` |
| `Home` / `End` | Jump to first/last available character | MonoGame `KeyboardState` |
| `PageUp` / `PageDown` | Scroll grid by one viewport | MonoGame `KeyboardState` |

### Grid Cell Rendering Detail

Each `CellRuntime` updates in a `ConfigureCell(int codepoint)` method:

```csharp
public void ConfigureCell(int codepoint, bool isAvailable, bool isSelected, int cellSize)
{
    // Position is set by the grid layout manager
    Width = cellSize;
    Height = cellSize;

    // Character display
    CharLabel.Text = char.ConvertFromUtf32(codepoint);
    HexLabel.Text = codepoint.ToString("X4")[^2..]; // last 2 hex digits

    // Visual state
    if (isSelected)
    {
        Background.Color = new Color(33, 150, 243);   // #2196F3
        CharLabel.Color = Color.White;
    }
    else if (isAvailable)
    {
        Background.Color = Color.White;
        CharLabel.Color = new Color(33, 33, 33);
    }
    else
    {
        Background.Color = new Color(224, 224, 224);   // #E0E0E0
        CharLabel.Color = new Color(158, 158, 158);
        CharLabel.Alpha = 128;                          // 50%
    }
}
```

---

## Wave 4: Unicode Block Sidebar

### Objective

Provide a list of Unicode blocks alongside the character grid, allowing bulk selection by block and easy navigation. The sidebar mirrors BMFont's right-side panel with checkboxes per Unicode block.

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 4.1 | Unicode block data source | Bundle a data file (`unicode-blocks.json` or embedded resource) listing all Unicode blocks: name, start codepoint, end codepoint. Source from Unicode 15.1 Blocks.txt. Parse at startup into `IReadOnlyList<UnicodeBlock>` where `UnicodeBlock` is a record with `string Name`, `int Start`, `int End`. | S |
| 4.2 | Block sidebar panel layout | `ContainerRuntime` panel to the right of the character grid, separated by a GUM `Splitter` for resizable width. Contains a `ScrollViewer` wrapping a `StackPanel` with vertical layout. Each item is a custom row built from: `CheckBox` + `TextRuntime` block name + `TextRuntime` glyph count badge (e.g., "42/128"). Only blocks with at least one available glyph in the loaded font are shown by default. A `CheckBox` toggle "Show all blocks" at the top reveals blocks with zero available glyphs (grayed out, checkbox disabled). | M |
| 4.3 | Block checkbox selection | Checking a block's `CheckBox` selects all available codepoints in that block range by adding them to `CharacterGridViewModel.SelectedCodepoints`. Unchecking deselects them. `CheckBox` shows indeterminate state (set via `IsChecked = null` if GUM supports, otherwise use a custom three-state visual: unchecked, checked, partial via `ColoredRectangleRuntime` fill) when some but not all available codepoints in the block are selected. State is derived from `SelectedCodepoints` intersection with the block's available codepoints. | M |
| 4.4 | Block click-to-scroll | Clicking the block name `TextRuntime` (not the `CheckBox`) scrolls the character grid to the start of that block by updating the grid's scroll offset. Highlight the block's range in the grid with a subtle `ColoredRectangleRuntime` overlay (light yellow, alpha 0.15) for 2 seconds (fade out via timer in `Update()`). | S |
| 4.5 | Block search/filter | GUM `TextBox` at the top of the sidebar with placeholder "Filter blocks...". Bind to `BlockSidebarViewModel.FilterText`. Filter the block list by name (case-insensitive substring). Example: typing "latin" shows "Basic Latin", "Latin-1 Supplement", "Latin Extended-A", "Latin Extended-B", "Latin Extended Additional". Rebuild the `StackPanel` children on filter change (debounced 200ms). | S |
| 4.6 | Block glyph count calculation | For each Unicode block, compute in the view model: `TotalCodepoints` (end - start + 1), `AvailableCodepoints` (intersection with `FontInfo.AvailableCodepoints`), `SelectedCodepoints` (intersection with current selection). Display as "Selected/Available" in the badge `TextRuntime`. Update counts reactively when selection changes by recalculating on `SelectionChanged` event. | S |
| 4.7 | Quick-select block groups | Below the filter `TextBox`, provide a horizontal `StackPanel` of `Button` controls for common block groups: "All Latin" (selects Basic Latin, Latin-1, Latin Extended-A/B, Latin Extended Additional), "All Cyrillic", "All Greek", "All CJK" (CJK Unified Ideographs + CJK Compatibility), "Symbols" (Mathematical Operators, Miscellaneous Symbols, Arrows, Box Drawing, Block Elements, Geometric Shapes). Each button is a toggle — first click selects, second click deselects. Track toggle state with `ColoredRectangleRuntime` background color. | M |
| 4.8 | Block sidebar visibility toggle | View > Unicode Blocks menu item toggles `Visible` on the sidebar `ContainerRuntime`. Handle `Ctrl+B` via MonoGame `KeyboardState`. Default state: visible when character grid is active. Persist visibility in user settings JSON. | S |
| 4.9 | Sync grid scroll to sidebar | As the user scrolls the character grid, determine the Unicode block at the top-left corner of the visible viewport. Highlight the corresponding block row in the sidebar with a `ColoredRectangleRuntime` left accent bar (4px wide, blue `#2196F3`). Scroll the sidebar `ScrollViewer` to keep the highlighted block visible. Distinct from the click-to-scroll highlight (4.4). | M |
| 4.10 | Block statistics summary | At the bottom of the sidebar, a `ContainerRuntime` with `TextRuntime` showing: "X blocks with glyphs | Y blocks selected" where X is blocks that have available glyphs and Y is blocks that have at least one selected glyph. Bind to `BlockSidebarViewModel` computed properties. | S |

### Keyboard Shortcuts (Wave 4)

| Shortcut | Action | Implementation |
|----------|--------|----------------|
| `Ctrl+B` | Toggle Unicode block sidebar | MonoGame `KeyboardState` |

---

## Wave 5: Text-Based Character Selection (Hiero-Inspired)

### Objective

Allow users to select characters by pasting or typing text, or by importing a text file. The system extracts unique codepoints and adds them to the selection. This mirrors Hiero's text-based character input approach.

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 5.1 | Text input tab/panel | A tab in the character selection area (alongside the grid tab, switched via `Button` tab bar). Contains a large GUM `TextBox` with `AcceptsReturn = true` (multiline). Placeholder text: "Paste or type text here to select those characters...". Minimum height 200px. Wrap the `TextBox` in a `ScrollViewer` for vertical scrolling. | S |
| 5.2 | Character extraction engine | When text changes (debounced 500ms via timer in `Update()`), extract all unique codepoints using the same surrogate-pair-aware logic as `CharacterSet.FromChars(string)`. Display results below the text area in a `Label`: "Found X unique characters". Internally store as `HashSet<int>`. Filter out control characters (U+0000-U+001F, U+007F-U+009F) except common whitespace if desired. | S |
| 5.3 | Add vs. Replace mode toggle | Two GUM `RadioButton` controls above the text area: "Add to selection" (default, `IsChecked = true`) and "Replace selection". Bind to `TextInputViewModel.IsReplaceMode` property. In Add mode, extracted characters are unioned with the current selection. In Replace mode, extracted characters become the entire selection. Mode applies when the user clicks the "Apply" button. | S |
| 5.4 | Apply button with confirmation | GUM `Button` labeled "Apply" below the text area. In Replace mode, show a confirmation `Window` dialog: "This will replace your current selection of N characters with M characters from the text input. Continue?" with Yes/No `Button` controls. In Add mode, apply immediately without confirmation. After apply, show a toast: "Added X new characters (Y already selected, Z not available in font)". | S |
| 5.5 | Availability highlighting in text | Below the `TextBox`, show a `ContainerRuntime` with the input text re-rendered character by character. Characters not available in the loaded font are rendered via `TextRuntime` with red background (`ColoredRectangleRuntime` behind each char). Show a warning `Label`: "W characters not available in this font". Include a `Button` "Show missing" that opens a popup `ListBox` of the specific missing characters with their codepoints. | L |
| 5.6 | Load characters from text file | `Button` "Import from File..." below the text area. Uses `NativeFileDialogSharp.Dialog.FileOpen()` with filter `txt,csv`. Reads the file as UTF-8 via `File.ReadAllTextAsync`, populates the `TextBox` with its contents, and triggers extraction. File size limit: 1MB (show error `Window` for larger files). Show a toast: "Loaded {filename} ({length} characters)". | S |
| 5.7 | Load characters from codepoint list | `Button` "Import Codepoint List..." uses `NativeFileDialogSharp.Dialog.FileOpen()` for `.txt` files. Each line contains a codepoint in one of these formats: `U+0041`, `0x0041`, `0041` (hex), or `65` (decimal). Lines starting with `#` are comments. Blank lines are ignored. Parse and add to selection. Show count of parsed codepoints and any parse errors (with line numbers) in a results `Label`. | M |
| 5.8 | Character extraction preview | Below the text area, show a horizontal `ScrollViewer` containing a `StackPanel` (horizontal layout) of extracted characters rendered as small cells (24px `ContainerRuntime` + `ColoredRectangleRuntime` background + `TextRuntime` character). Characters available in the font show normally; unavailable characters show with a red `ColoredRectangleRuntime` overlay. Clicking a character cell removes it from the extraction set. | M |
| 5.9 | Common text samples dropdown | A GUM `ComboBox` labeled "Insert Sample Text..." with options: "English pangram" (The quick brown fox...), "Digits and punctuation" (0123456789!@#$%...), "Lorem ipsum", "European diacritics" (curated French, German, Spanish, Portuguese, Polish accented characters), "Currency symbols" (dollar, euro, pound, yen, etc.), "Math operators" (plus, minus, multiply, divide, equals, etc.). Selecting an option appends the text to the `TextBox`. | S |
| 5.10 | Clear text input | `Button` labeled "Clear" that empties the `TextBox.Text` and resets the extraction preview. No confirmation needed since the text is transient input, not the committed selection. | S |

---

## Wave 6: Character Set Presets & Management

### Objective

Provide built-in presets for common character sets and let users save/load their own custom presets.

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 6.1 | Preset data model | Create `CharacterSetPreset` class: `string Name`, `string? Description`, `IReadOnlyList<(int Start, int End)> Ranges`, `IReadOnlyList<int>? ExplicitCodepoints`, `bool IsBuiltIn`, `DateTime? Created`. Presets can define selection via ranges, explicit codepoints, or both (union). This is a plain C# model, not a GUM ViewModel. | S |
| 6.2 | Built-in preset definitions | Define built-in presets as static instances mapping to KernSmith library `CharacterSet` equivalents: **ASCII** (`CharacterSet.Ascii`, U+0020-U+007E), **Extended ASCII** (`CharacterSet.ExtendedAscii`, U+0020-U+00FF), **Latin** (`CharacterSet.Latin`, ASCII + Latin Extended-A U+0100-U+017F + Latin Extended-B U+0180-U+024F), **Digits Only** (U+0030-U+0039), **Basic Latin + Supplement** (U+0020-U+00FF), **Latin Complete** (ASCII + all Latin Extended blocks + Latin Extended Additional U+1E00-U+1EFF), **Cyrillic** (U+0400-U+04FF), **Greek** (U+0370-U+03FF), **CJK Common** (U+4E00-U+9FFF), **Symbols & Punctuation** (General Punctuation U+2000-U+206F + Misc Symbols U+2600-U+26FF + Dingbats U+2700-U+27BF), **Currency** (U+0024 + U+00A2-U+00A5 + U+20A0-U+20CF), **Arrows** (U+2190-U+21FF), **Box Drawing** (U+2500-U+257F + Block Elements U+2580-U+259F), **Mathematical** (U+2200-U+22FF). | M |
| 6.3 | Preset selector UI | A GUM `ComboBox` in the character selection toolbar labeled "Presets". Lists all built-in presets first (grouped under a `Label` header "Built-in"), then user presets (grouped under "Custom"), separated by a visual divider (`ColoredRectangleRuntime`, 1px height, gray). Selecting a preset shows its description in a `Label` below the combo box. Bind items from `PresetManagerViewModel`. | M |
| 6.4 | Apply preset action | When a preset is selected from the `ComboBox`, convert its ranges and explicit codepoints into a `HashSet<int>`. Show a `Window` dialog with two `Button` options: "Add to selection" and "Replace selection". If replacing, show a confirmation message in the dialog. After applying, show a toast with count. Update the grid and sidebar to reflect the new selection via `SelectionChanged` event. | S |
| 6.5 | Save current selection as preset | `Button` "Save as Preset..." opens a GUM `Window` dialog with: `TextBox` for Name (required, validated non-empty), `TextBox` for Description (optional, multiline). On save (`Button` "Save"), serialize the current `SelectedCodepoints` as ranges where possible (consecutive codepoints collapsed into ranges) and individual codepoints for isolated selections. Save to `%APPDATA%/KernSmith/presets/` as individual JSON files named `{sanitized-name}.json`. | M |
| 6.6 | Custom preset JSON format | Define the JSON schema: `{ "name": "...", "description": "...", "version": 1, "ranges": [[start, end], ...], "codepoints": [cp, ...], "created": "ISO8601" }`. Use `System.Text.Json` for serialization with `JsonSerializerOptions { WriteIndented = true }`. On load, validate the schema version and show an error `Window` for unsupported versions. | S |
| 6.7 | Export preset to file | Right-click a custom preset in the `ComboBox` list > popup menu "Export..." uses `NativeFileDialogSharp.Dialog.FileSave()` to save the preset JSON. Default filename is the preset name. File extension: `.kspreset` (KernSmith preset). Filter: `kspreset`. | S |
| 6.8 | Import preset from file | `Button` "Import Preset..." or File > Import Preset. Uses `NativeFileDialogSharp.Dialog.FileOpen()` for `.kspreset` and `.json` files. Parse the file, validate, and add to the custom presets list. If a preset with the same name already exists, show a `Window` dialog: "A preset named '{name}' already exists." with three `Button` options: "Overwrite", "Rename", "Cancel". | S |
| 6.9 | Preset management dialog | Accessible via `Button` "Manage Presets..." in the toolbar. Opens a GUM `Window` containing a `ListBox` of all custom presets. Each item shows: Name (`TextRuntime`), Character Count (`TextRuntime`), Created Date (`TextRuntime`). Action `Button` row per item: Rename, Delete (with confirmation `Window`), Export, Duplicate. Built-in presets are listed with a "Built-in" badge and have Rename/Delete disabled. | M |
| 6.10 | Custom Unicode range input | `Button` "Custom Range..." opens a GUM `Window` with a multiline `TextBox` where the user enters one or more ranges as `start-end` pairs (hex format, e.g., "0020-007E"). One range per line. Validate that start <= end and both are valid Unicode codepoints (U+0000 to U+10FFFF). Show count in a `Label`: "X codepoints in Y ranges". Validation errors in a red `Label` with line numbers. Apply as add-to or replace selection via `RadioButton` toggle. | M |
| 6.11 | Preset preview | When hovering over a preset in the `ComboBox` dropdown (tracked via mouse position in `Update()`), show a popup `ContainerRuntime` with: character count (`TextRuntime`), Unicode blocks covered (`TextRuntime` list), and a horizontal strip of the first 30 characters rendered as small `TextRuntime` cells in a `StackPanel`. | S |

---

## Wave 7: Selection Summary & Validation

### Objective

Provide an always-visible summary of the current character selection with warnings, quick actions, and atlas size estimation.

### Tasks

| # | Task | Details | Effort |
|---|------|---------|--------|
| 7.1 | Selection summary bar | A horizontal `ContainerRuntime` bar at the bottom of the character selection area. Always visible when a font is loaded. Contains `Label` controls showing: total selected count as a large number, selected Unicode block count, and available-but-unselected count. Example: "1,247 characters selected | 8 Unicode blocks | 3,041 available". Bind all values from `SelectionSummaryViewModel`. | S |
| 7.2 | Atlas size estimation | Display estimated atlas dimensions in a `Label` next to the selection count. Call `AtlasSizeEstimator.Estimate()` with the current font size and selected character count. Show as "Est. atlas: 1024x512". Update on selection change (debounced 500ms via timer in `Update()`). **Note:** `AtlasSizeEstimator` is currently `internal`. This depends on Phase 55 to make it public or provide a wrapper API (see G.1). | M |
| 7.3 | Large selection warning | When selected count exceeds a threshold (configurable, default 5,000), show a yellow warning banner — `ContainerRuntime` with yellow `ColoredRectangleRuntime` background and `TextRuntime`: "Large character set: {count} characters selected. Generation may take several seconds and produce multiple atlas pages." Include a dismiss `Button` (X). Dismissible per session (set `Visible = false`, track in view model). | S |
| 7.4 | Missing glyphs warning | If the selection includes codepoints not available in the font (possible if a preset or text import added them), show an orange warning banner: "{count} selected characters are not available in this font and will be skipped." Include a `Button` "Remove unavailable" that prunes the selection by intersecting with `FontInfo.AvailableCodepoints`. | S |
| 7.5 | Empty selection warning | If selection count is zero, show the summary bar in a warning state (red-tinted `ColoredRectangleRuntime` background): "No characters selected. Add characters using the grid, text input, or presets." Set `IsEnabled = false` on the Generate `Button` in the main toolbar. | S |
| 7.6 | Select All action | Toolbar `Button` "Select All" and `Ctrl+A` shortcut (when grid is focused). Selects all codepoints in `FontInfo.AvailableCodepoints`. Show a confirmation `Window` if this would select more than 10,000 characters: "Select all {count} available characters? This may result in a large atlas." with Yes/No `Button` controls. | S |
| 7.7 | Deselect All action | Toolbar `Button` "Deselect All" and `Ctrl+Shift+A` shortcut (via MonoGame `KeyboardState`). Clears `SelectedCodepoints`. Show a confirmation `Window`: "Clear all {count} selected characters?" with Yes/No. | S |
| 7.8 | Invert Selection action | Toolbar `Button` "Invert". For every available codepoint: if selected, deselect it; if not selected, select it. No confirmation needed. Useful for "select everything except these characters" workflows. | S |
| 7.9 | Match Font action | Distinct from "Select All" — ensures the selection matches exactly `FontInfo.AvailableCodepoints` (removing any stale codepoints from previous fonts that are no longer available). Useful after switching fonts. `Button` label: "Match Font" with tooltip (hover popup): "Select exactly the characters available in this font". | S |
| 7.10 | Selection diff on font change | When the user loads a new font, compare the current selection against the new font's available codepoints. Show a `Window` dialog: "Font changed. {kept} of your selected characters are available in {new font}. {lost} characters are not available." Three `Button` options: "Keep compatible" (keep the intersection), "Clear selection" (start fresh), "Keep all" (keep the full selection, unavailable chars will be warned about). | M |
| 7.11 | Export selection summary | `Button` "Copy Summary" on the summary bar. Uses `SDL2.SDL.SDL_SetClipboardText()` to copy: "KernSmith Character Selection\nFont: {family} {style}\nSelected: {count}\nBlocks: {block list}\nCodepoint ranges: {collapsed ranges}". Useful for documentation or sharing configuration. | S |
| 7.12 | Selection integration with pipeline | When the user clicks Generate (from Phase 60), construct a `CharacterSet` from `SelectedCodepoints` using `CharacterSet.FromChars(IEnumerable<int>)` and assign it to `FontGeneratorOptions.Characters`. This is the bridge between the UI selection state and the library's generation pipeline. Verify that the `CharacterSet` round-trips correctly (selected count in == count out after `.Resolve()`). Call `BmFontBuilder.WithCharacters(characterSet)` to pass the selection downstream. | M |

### Keyboard Shortcuts (Wave 7)

| Shortcut | Action | Implementation |
|----------|--------|----------------|
| `Ctrl+A` | Select all available (when grid focused) | MonoGame `KeyboardState` |
| `Ctrl+Shift+A` | Deselect all | MonoGame `KeyboardState` |

---

## Unicode Character Name Database

The character grid tooltips (3.8) and character name search (3.13) require a Unicode character name database. This is a cross-cutting concern for Waves 3-5.

### Approach

| # | Task | Details | Effort |
|---|------|---------|--------|
| U.1 | Bundle UnicodeData subset | Extract character names from Unicode 15.1 `UnicodeData.txt` for the BMP (U+0000-U+FFFF). Store as a compressed embedded resource in the MonoGame `Content` pipeline or as a raw embedded resource. Each entry: codepoint (int), name (string), general category (string). Total: ~35,000 entries, approximately 1MB uncompressed, ~300KB compressed. | M |
| U.2 | Name lookup service | Create `UnicodeNameLookup` class with methods: `string? GetName(int codepoint)`, `string? GetCategory(int codepoint)`, `IEnumerable<(int Codepoint, string Name)> Search(string query, int maxResults)`. Load the database lazily on first access. Use a dictionary for O(1) name lookup and a pre-sorted list for prefix/substring search. | M |
| U.3 | Block lookup integration | `UnicodeNameLookup.GetBlock(int codepoint)` returns the `UnicodeBlock` containing the given codepoint. Uses binary search over the sorted block list from Wave 4's data source. | S |

---

## Custom GUM Component Summary

This phase requires several custom components built from GUM primitives since GUM's control library does not include them:

| Component | Built From | Used In |
|-----------|-----------|---------|
| `CharacterGridRuntime` | `ContainerRuntime`, `ColoredRectangleRuntime`, `TextRuntime`, `NineSliceRuntime` | Wave 3: Main character grid |
| `CellRuntime` | `ContainerRuntime`, `ColoredRectangleRuntime`, `TextRuntime`, `NineSliceRuntime` | Wave 3: Individual grid cells (pooled) |
| `CollapsibleSection` | `ContainerRuntime`, `Button` | Wave 2: Font info panel sections |
| `ToastRuntime` | `ContainerRuntime`, `NineSliceRuntime`, `TextRuntime` | Wave 1: Notifications |
| `PopupMenu` | `ContainerRuntime`, `ColoredRectangleRuntime`, `Button` | Wave 3: Context menu, Wave 6: Preset context actions |
| `TabBar` | `ContainerRuntime`, `Button`, `ColoredRectangleRuntime` | Wave 5: Grid/Text tab switching |
| `ThreeStateCheckBox` | `ContainerRuntime`, `ColoredRectangleRuntime`, `CheckBox` | Wave 4: Block partial selection |
| `TooltipPopup` | `ContainerRuntime`, `NineSliceRuntime`, `TextRuntime` | Wave 3: Cell hover info |
| `CharacterStrip` | `ScrollViewer`, `StackPanel`, `ContainerRuntime`, `TextRuntime` | Wave 5: Extraction preview, Wave 6: Preset preview |

---

## Library API Gaps to Document

During implementation, document (but do not fix in this phase) any gaps discovered in the core KernSmith library that hinder UI integration. **Phase 55 is the tracking document for core library prerequisites** — all gaps identified here should be cross-referenced there. Potential issues:

| # | Potential Gap | Impact | Workaround |
|---|---------------|--------|------------|
| G.1 | `AtlasSizeEstimator` is `internal` | Cannot call from UI project for size preview | Tracked in Phase 55. Use `InternalsVisibleTo` or create a public wrapper method on `BmFont` |
| G.2 | `CharacterSet` has no `Intersect` method | Need to compute intersection with font availability from UI side | Use `Resolve()` which already does this, or add `Intersect(CharacterSet other)` |
| G.3 | `CharacterSet` has no `Except` method | Cannot remove specific codepoints from a set | Build new set from filtered codepoints using `FromChars(IEnumerable<int>)` |
| G.4 | `CharacterSet` has no `Contains(int)` method | Need to check if a codepoint is in the set from UI | Use `GetCodepoints()` and build a `HashSet<int>` |
| G.5 | No public API to render a single glyph preview | Font info panel preview (2.10) and grid cell rendering need single-glyph bitmaps as `Texture2D` | Use `IRasterizer` directly, which is public, then convert `byte[]` to `Texture2D` via `Texture2D.SetData()` |
| G.6 | System font enumeration may be slow on first call | UI freezes if called on the main thread | Already planned to call on background thread (task 1.1) |
| G.7 | No `Texture2D` conversion helper | Need to convert rasterized glyph `byte[]` to MonoGame `Texture2D` for display | Create a utility: `Texture2D.SetData<byte>(pixelData)` with appropriate format |

---

## Cross-Phase Dependencies

| Dependency | Phase | Description |
|------------|-------|-------------|
| Phase 60 | UI Application Shell | Provides the main MonoGame game loop, GUM initialization, menu bar, toolbar, and panel layout that this phase populates |
| Phase 62+ | Effects & Generation Settings | The character selection from this phase feeds into the generation options configured in future phases |
| Core Library | Current | `ISystemFontProvider`, `FontInfo`, `CharacterSet`, `IFontReader`, `AtlasSizeEstimator` must be stable |

---

## Non-Functional Requirements

- **Performance**: Character grid must scroll at 60fps with 65,536 cells via object pooling and virtualized rendering. Selection changes must update the summary bar within 50ms. Font loading must run on a background thread — never block the MonoGame `Update()`/`Draw()` loop.
- **Memory**: Unicode name database should use < 5MB RAM. Grid `CellRuntime` pool capped at `(viewportRows + 2) * 16` instances. Glyph `Texture2D` thumbnails cached with LRU eviction at 2,000 entries.
- **Input**: All interactive elements must be keyboard-navigable via MonoGame `KeyboardState` handling. Input priority: GUM controls consume input first; unhandled input falls through to grid/custom components.
- **Rendering**: All GUM controls rendered via GUM's built-in renderer integrated with MonoGame's `SpriteBatch`. Custom grid cells use GUM primitives (not raw `SpriteBatch` calls) to stay within the GUM layout system.
- **Persistence**: Character selection is session-scoped (not persisted to disk automatically). Custom presets are persisted to disk as JSON. Recent fonts list is persisted. Panel visibility states are persisted. All persistence uses `%APPDATA%/KernSmith/` directory.
- **Threading**: MonoGame is single-threaded for rendering. Background tasks (font loading, file I/O) must marshal results back to the main thread. Use `ConcurrentQueue<Action>` drained in `Update()` for thread-safe UI updates.

---

## Success Criteria

1. User can browse and search system fonts with real-time filtering in a GUM `ListBox`.
2. User can load fonts via drag-and-drop (MonoGame `FileDrop`), `NativeFileDialogSharp` file dialog, or system font browser with TTC face selection.
3. Font information panel displays all `FontInfo` metadata including variable font axes (GUM `Slider`) and OpenType table details in collapsible sections.
4. Interactive character grid renders all BMP codepoints with virtualized cell pooling at 60fps using GUM primitives.
5. User can select characters via click, shift-click, ctrl-click, and drag gestures in the grid (MonoGame input handling).
6. Unicode block sidebar with GUM `CheckBox` controls enables bulk selection and grid navigation by block.
7. Text-based input via GUM `TextBox` extracts unique characters from pasted text or imported files.
8. Built-in and custom presets via GUM `ComboBox` allow one-click character set configuration.
9. Selection summary bar shows count, estimated atlas size, and actionable warnings.
10. Character selection integrates with `FontGeneratorOptions.Characters` via `CharacterSet.FromChars()` and `BmFontBuilder.WithCharacters()` for generation pipeline handoff.
11. All keyboard shortcuts function as documented via MonoGame `KeyboardState`.
12. Font loading and character selection state survives font style changes within the same family.
13. File dialogs use `NativeFileDialogSharp` — no Avalonia `StorageProvider` or XAML anywhere in the codebase.
