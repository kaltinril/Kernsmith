# Phase 65 — Project Management & File Operations

> **Status**: Complete
> **Completed**: 2026-03-22. .bmfc save/load via BmfcConfigWriter/Reader. Session persistence (window size, recent fonts, last paths). SaveDialog for export. Undo/redo removed — unnecessary for a settings-based tool. Custom menu bar replaced with GUM Menu/MenuItem. Context menus and toolbar deferred.
> **Created**: 2026-03-21
> **Goal**: Build complete project management including save/load configurations, import/export workflows, recent files, undo/redo, and integration with the .bmfc config format. All UI built with GUM (code-only) + MonoGame + MonoGame.Extended.

---

## Overview

This phase covers all file I/O, project state management, menu system construction, and configuration persistence for the KernSmith UI application. The UI acts as a visual wrapper around the KernSmith library APIs, and project management is the bridge between transient UI state and persistent on-disk configuration.

### Framework Stack

| Component | Technology |
|-----------|-----------|
| **Game framework** | MonoGame (DesktopGL) |
| **UI toolkit** | GUM UI (code-only, no .gumx files) |
| **Extended utilities** | MonoGame.Extended (input, camera, etc.) |
| **File dialogs** | NativeFileDialogSharp (cross-platform native open/save/folder dialogs) |
| **MVVM binding** | GUM's ViewModel with `Get<T>`/`Set<T>` + `SetBinding()` |
| **Settings persistence** | JSON file via `System.Text.Json` in app data directory |

### KernSmith Library APIs Used

| API | Purpose |
|-----|---------|
| `BmfcConfig` + `BmfcConfigWriter` | Build config from UI state and serialize to .bmfc string (no generation required) |
| `BmFontResult.ToBmfc()` | Export settings from an existing generation result as .bmfc config string (alternative when a result exists) |
| `BmFont.FromConfig(string bmfcPath)` | Generate bitmap font from .bmfc file path |
| `BmFont.FromConfig(BmfcConfig config)` | Generate bitmap font from parsed config object |
| `BmFont.Load(string fntPath)` | Load existing .fnt + associated textures |
| `BmFontResult.ToFile(path, OutputFormat)` | Write .fnt + textures to disk |
| `BmFontResult.FntText` / `.FntXml` / `.FntBinary` | In-memory descriptor content |
| `BmFontResult.GetPngData()` / `.GetTgaData()` / `.GetDdsData()` | Texture page data |

### Key Design Decisions

1. **Project file format is .bmfc** — The same BMFont-compatible config format the library already supports. No custom format.
2. **Undo/redo is ViewModel-level** — Operations are tracked as property change deltas on ViewModels, not library-level state.
3. **Recent files are user-scoped** — Stored in per-user app settings JSON, never in the project file.
4. **Dirty tracking drives save prompts** — Every undoable change marks the project dirty; save clears the flag.
5. **Export is separate from Save** — Save writes .bmfc (configuration); Export writes .fnt + textures (output).
6. **No XAML** — All UI is built in C# code using GUM's code-only API. No `.axaml`, `.gumx`, or markup files.
7. **File dialogs via NativeFileDialogSharp** — Cross-platform native OS dialogs, not custom GUM panels.

### GUM UI Patterns Used Throughout

GUM is a code-only UI toolkit for MonoGame. Key patterns:

```csharp
// Creating containers
var panel = new ContainerRuntime();
panel.Width = 300;
panel.Height = 40;
panel.WidthUnits = DimensionUnitType.Absolute;

// Text elements
var label = new TextRuntime();
label.Text = "File";
label.FontSize = 14;

// Buttons
var button = new Button();
button.Text = "Save";
button.Click += (s, e) => OnSaveClicked();

// ViewModel binding
viewModel.SetBinding(
    nameof(ProjectViewModel.WindowTitle),
    () => titleLabel.Text = viewModel.WindowTitle
);
```

---

## Wave 1 — Menu System Implementation

Build a custom menu bar using GUM containers, text elements, and button-like interactive components. GUM does not provide built-in MenuBar or Toolbar controls, so these are built from primitives.

### Architecture

- `MenuBar.cs` — Custom GUM component: horizontal `ContainerRuntime` with top-level menu buttons
- `MenuDropdown.cs` — Dropdown panel that appears on click, containing `MenuItem` instances
- `MenuItem.cs` — Single menu item: label + shortcut text + optional check/submenu indicator
- `MenuBarViewModel.cs` — ViewModel with command methods and can-execute state for all menu items
- Each command delegates to a service: `IProjectService`, `IExportService`, `IRecentFilesService`, `IUndoRedoService`
- Keyboard shortcuts handled via MonoGame `KeyboardState` polling in `Update()` with modifier key detection
- Commands use `Action` delegates bound from the ViewModel; can-execute state drives `MenuItem.IsEnabled`

### Custom Menu System Components

The menu system is built from these GUM primitives:

```csharp
// MenuBar: horizontal container anchored to top of screen
public class MenuBar : ContainerRuntime
{
    // Contains MenuBarItem instances ("File", "Edit", "View", etc.)
    // Each MenuBarItem is a ContainerRuntime with hover/active states
    // Click opens a MenuDropdown below the item
}

// MenuDropdown: vertical container with menu items
public class MenuDropdown : ContainerRuntime
{
    // Semi-transparent background rectangle
    // Contains MenuItem instances stacked vertically
    // Auto-sizes to fit content
    // Closes when clicking outside or pressing Escape
}

// MenuItem: single row in a dropdown
public class MenuItem : ContainerRuntime
{
    // Layout: [CheckMark?] [Label] [ShortcutText] [SubmenuArrow?]
    // Hover highlight via background color change
    // Click invokes Action delegate
    // IsEnabled grays out text and ignores clicks
    // IsSeparator renders as a horizontal line instead
}
```

### File Menu

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 1.1 | Create `MenuBar` GUM component | Custom `ContainerRuntime` subclass. Horizontal layout with menu bar items ("File", "Edit", "View", "Tools", "Help"). Fixed height (28px), full window width. Background color matches app theme. Each top-level item is a `ContainerRuntime` with `TextRuntime` label, hover highlight, and click handler that toggles its `MenuDropdown`. Only one dropdown open at a time. Mouse-over other top-level items while one is open switches the active dropdown. | M |
| 1.2 | Create `MenuDropdown` GUM component | Vertical `ContainerRuntime` that appears below a menu bar item. Contains `MenuItem` instances. Auto-sizes width to fit longest item. Shadow/border effect via layered rectangles. Closes on: click outside, Escape key, clicking a non-submenu item. Supports nested submenus (dropdown appears to the right of the parent item). Z-order managed via GUM's render order. | M |
| 1.3 | Create `MenuItem` GUM component | Single row in a `MenuDropdown`. Layout: optional checkmark icon (for toggles), label text, right-aligned shortcut key text (e.g., "Ctrl+S"), optional submenu arrow. States: Normal, Hovered (highlight background), Disabled (grayed text, no click response), Checked (shows checkmark). Separator variant: thin horizontal line, no interaction. Click invokes an `Action` delegate. | M |
| 1.4 | Create `MenuBarViewModel.cs` | ViewModel with `Action` properties and `bool` can-execute flags for every menu item. Inject `IProjectService`, `IExportService`, `IUndoRedoService`, `IRecentFilesService`. Properties like `CanSave` (true when dirty), `CanUndo` (true when undo stack non-empty), `CanGenerate` (true when font loaded). Uses GUM ViewModel `Get<T>`/`Set<T>` pattern for observable properties. | M |
| 1.5 | Create `InputManager` for keyboard shortcuts | MonoGame `Update()`-based input handler. Tracks current and previous `KeyboardState`. Detects modifier combos (Ctrl+S, Ctrl+Shift+O, etc.). Maps `Keys` combos to `Action` delegates registered from the ViewModel. Handles key repeat suppression (only fires on initial press, not held). Registered shortcut table checked each frame. | M |
| 1.6 | Implement **New** command (Ctrl+N) | Prompt to save if dirty (custom GUM modal dialog, see task 1.20). Reset all ViewModels to default state. Clear loaded font. Set project path to null. Clear undo history. Title bar shows "Untitled -- KernSmith". | S |
| 1.7 | Implement **Open Font** command (Ctrl+O) | Call `NativeFileDialogSharp.Dialog.FileOpen("ttf,otf,woff,woff2,ttc")`. If result is OK, load font bytes, pass to rasterizer. Update `FontConfigViewModel` with font metadata (family, style, glyph count). Set default project name from font family. Does NOT load a project -- just sets the font source. Remember last directory via `ISettingsService`. | S |
| 1.8 | Implement **Open Project** command (Ctrl+Shift+O) | Call `NativeFileDialogSharp.Dialog.FileOpen("bmfc")`. Delegates to `IProjectService.OpenAsync(path)`. See Wave 2 for full implementation. | S |
| 1.9 | Implement **Open Recent** submenu | `MenuItem` with submenu arrow. Submenu `MenuDropdown` populated dynamically from `IRecentFilesService.GetRecentFiles()`. Each item shows filename. Separator, then "Clear Recent Files" item at bottom. Items launch Open Font or Open Project depending on file extension. Rebuilt when dropdown opens. | M |
| 1.10 | Implement **Save Project** command (Ctrl+S) | If project has a path, overwrite it. If no path (new/untitled), behave like Save As. Delegates to `IProjectService.SaveAsync(path)`. Clears dirty flag. Updates title bar. | S |
| 1.11 | Implement **Save Project As** command (Ctrl+Shift+S) | Call `NativeFileDialogSharp.Dialog.FileSave("bmfc")`. Saves to chosen path. Updates current project path. Adds to recent files. | S |
| 1.12 | Implement **Export Font** command (Ctrl+E) | Trigger generation + file write. See Wave 3 for full export workflow. | S |
| 1.13 | Implement **Export Font As** command (Ctrl+Shift+E) | Always prompts for new export path and format settings via export dialog. | S |
| 1.14 | Implement **Close** command (Ctrl+W) | Prompt to save if dirty. Reset to empty/new state. Equivalent to New but does not create a fresh project -- just clears everything. | S |
| 1.15 | Implement **Exit** command (Alt+F4) | Prompt to save if dirty. Call `Game.Exit()`. Also hook into MonoGame's `Game.Exiting` event to intercept window close button and show save prompt before allowing exit. | S |

### Edit Menu

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 1.16 | Implement **Undo** command (Ctrl+Z) | Pop from undo stack, apply reverse operation, push to redo stack. Menu item text shows description: "Undo: Change font size to 48". Disabled when undo stack is empty. See Wave 6 for undo system design. | S |
| 1.17 | Implement **Redo** command (Ctrl+Y) | Pop from redo stack, apply forward operation, push to undo stack. Menu item text shows description: "Redo: Change font size to 32". Disabled when redo stack is empty. | S |
| 1.18 | Implement **Select All Characters** command (Ctrl+A) | Select all characters in the character grid. Delegates to `CharacterSetViewModel.SelectAll()`. | S |
| 1.19 | Implement **Deselect All** command (Ctrl+Shift+A) | Clear character selection. Delegates to `CharacterSetViewModel.DeselectAll()`. | S |
| 1.20 | Create `ModalDialog` GUM component | Reusable GUM component for modal dialogs. Full-screen semi-transparent overlay (`ColoredRectangleRuntime`) that blocks input to controls behind it. Centered content panel with title bar, body content area, and button row. Buttons are configurable (OK, OK/Cancel, Save/Don't Save/Cancel). Result returned via callback `Action<DialogResult>`. Used by save guard, preferences, about, etc. | M |
| 1.21 | Implement **Preferences** command | Open a `ModalDialog` with preferences content. Settings panel built from GUM controls: `TextBox` for default font size, dropdown (`ListBox` or custom) for default output format, toggle (`CheckBox` or custom) for auto-generate on change, theme selector (light/dark), recent files limit. Preferences stored in user-scoped settings JSON file via `ISettingsService`. | M |

### View Menu

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 1.22 | Implement **Toggle Font Config Panel** | Show/hide the left-side font configuration panel. Checkmark on `MenuItem` when visible. Persisted in session state. Bound to `IsConfigPanelVisible` on `MainViewModel`. Toggling adjusts layout by changing panel width to 0 or restoring saved width. | S |
| 1.23 | Implement **Toggle Preview Panel** | Show/hide the bottom/right preview/atlas panel. Same pattern as config panel. | S |
| 1.24 | Implement **Toggle Effects Panel** | Show/hide the effects configuration panel. Same pattern. | S |
| 1.25 | Implement **Zoom In** command (Ctrl+Plus) | Increase atlas preview zoom by 25% (1x -> 1.25x -> 1.5x -> 2x -> 3x -> 4x -> 6x -> 8x). Capped at 8x. Zoom applied via MonoGame.Extended `OrthographicCamera` or direct `Matrix` transform on preview rendering. | S |
| 1.26 | Implement **Zoom Out** command (Ctrl+Minus) | Decrease atlas preview zoom by inverse steps. Minimum 0.125x (12.5%). | S |
| 1.27 | Implement **Fit to Window** command (Ctrl+0) | Calculate zoom level that fits the full atlas texture within the preview panel bounds. Account for panel padding. | S |
| 1.28 | Implement **Actual Size** command (Ctrl+1) | Set zoom to 1x (1 texture pixel = 1 screen pixel). Scroll to center of atlas. | S |
| 1.29 | Implement **Toggle Overlays** submenu | Submenu with checkable `MenuItem` instances: Show Glyph Bounds, Show Glyph Origins, Show Kerning Pairs, Show Grid, Show Padding. Each bound to a bool on `PreviewViewModel`. Overlays render as semi-transparent colored shapes via MonoGame.Extended `ShapeRenderer` or `SpriteBatch` primitives on the atlas preview. | M |

### Tools Menu

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 1.30 | Implement **Generate Font** command (F5) | Trigger font generation with current settings. Uses `BmFont.Generate()` or `BmFont.FromConfig()`. Updates preview with result. Shows progress indicator (animated GUM element) during generation. Runs generation on `Task.Run()` background thread, marshals result back. Disabled if no font is loaded. | S |
| 1.31 | Implement **Batch Generate** command | Open a `ModalDialog` for batch generation. File list panel: "Add Files" button calls `NativeFileDialogSharp.Dialog.FileOpenMultiple("bmfc")`. Shows list of selected .bmfc files with remove buttons. Output directory selector via `NativeFileDialogSharp.Dialog.FolderPicker()`. Generate button processes all sequentially with a progress bar (GUM `ContainerRuntime` with width animated proportionally). Results summary when complete. | L |
| 1.32 | Implement **Font Inspector** command | Open a `ModalDialog` showing raw font metadata: tables present, glyph count, kerning pair count, OS/2 metrics, name records, supported Unicode ranges. Uses `FontReader` data. Scrollable text content built with `TextRuntime` elements. Read-only informational view. | M |
| 1.33 | Implement **Validate Configuration** command | Run validation checks on current settings without generating. Check: font file exists, selected characters exist in font, atlas size sufficient, effect parameters valid. Show results in a `ModalDialog` with a list of warnings/errors (colored text: red for errors, yellow for warnings, green for "all clear"). | M |

### Help Menu

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 1.34 | Implement **About KernSmith** dialog | `ModalDialog` showing: app icon (rendered as `SpriteRuntime`), "KernSmith" name, version number (from assembly), copyright, license, credits (FreeTypeSharp, StbImageWriteSharp, MonoGame, GUM). OK button to close. | S |
| 1.35 | Implement **Documentation** command | Open default browser to documentation URL. Use `Process.Start` with `UseShellExecute = true`. URL configurable. | S |
| 1.36 | Implement **Check for Updates** command | HTTP GET to a version endpoint or GitHub releases API. Compare current version to latest. Show `ModalDialog`: "You are up to date" or "Version X.Y.Z is available" with download link. Handle offline gracefully with error message. | M |

### BMFont Menu Mapping Notes

BMFont's original menu structure differs from KernSmith's. Key mappings:

| BMFont Menu Item | KernSmith Equivalent | Notes |
|------------------|---------------------|-------|
| Options > Font settings (F) | Font Config panel (always visible) | BMFont uses a dialog; KernSmith uses a persistent side panel |
| Options > Export options (T) | Export dialog (Ctrl+E / Ctrl+Shift+E) | Output format, texture format, naming pattern |
| Options > Visualize (V) | View menu > Toggle Overlays | Glyph bounds, grid, kerning pair visualization |
| Options > Save bitmap font as | File > Export Font (Ctrl+E) | "Save bitmap font as" in BMFont maps to KernSmith's export, not save |
| Edit > Open Image Manager | *Deferred* | Image Manager (custom glyph images per character) is a future feature; not in scope for this phase |

### Keyboard Shortcut Summary

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New Project |
| Ctrl+O | Open Font |
| Ctrl+Shift+O | Open Project (.bmfc) |
| Ctrl+S | Save Project |
| Ctrl+Shift+S | Save Project As |
| Ctrl+E | Export Font |
| Ctrl+Shift+E | Export Font As |
| F6 | Quick Export |
| Ctrl+W | Close |
| Alt+F4 | Exit |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+A | Select All Characters |
| Ctrl+Shift+A | Deselect All |
| F5 | Generate Font |
| Ctrl+Plus | Zoom In |
| Ctrl+Minus | Zoom Out |
| Ctrl+0 | Fit to Window |
| Ctrl+1 | Actual Size |

### Keyboard Shortcut Implementation

MonoGame does not have a built-in shortcut/hotkey system. Shortcuts are implemented in the `InputManager`:

```csharp
public class InputManager
{
    private KeyboardState _current, _previous;
    private readonly List<(Keys[] modifiers, Keys key, Action action, Func<bool> canExecute)> _shortcuts = new();

    public void RegisterShortcut(Keys[] modifiers, Keys key, Action action, Func<bool>? canExecute = null)
    {
        _shortcuts.Add((modifiers, key, action, canExecute ?? (() => true)));
    }

    public void Update(GameTime gameTime)
    {
        _previous = _current;
        _current = Keyboard.GetState();

        foreach (var (modifiers, key, action, canExecute) in _shortcuts)
        {
            if (IsNewKeyPress(key) && AllModifiersHeld(modifiers) && canExecute())
                action();
        }
    }

    private bool IsNewKeyPress(Keys key) =>
        _current.IsKeyDown(key) && !_previous.IsKeyDown(key);

    private bool AllModifiersHeld(Keys[] modifiers) =>
        modifiers.All(m => _current.IsKeyDown(m));
}
```

---

## Wave 2 — Project Save/Load (.bmfc Format)

Implement the `IProjectService` that manages the project lifecycle: new, open, save, dirty tracking, and title bar state.

### Project State Model

The project state is the serializable snapshot of all UI settings. It maps bidirectionally to `BmfcConfig`:

```
ProjectState
  +-- FontSourcePath (string -- absolute or relative path to .ttf/.otf)
  +-- FontSourceType (enum: FilePath, SystemFont)
  +-- SystemFontName (string -- when FontSourceType == SystemFont)
  +-- FontSize (int)
  +-- CharacterSet (int[] -- selected codepoints)
  +-- CharacterRanges (UnicodeRange[] -- selected ranges)
  +-- AtlasWidth (int)
  +-- AtlasHeight (int)
  +-- AtlasPadding (int)
  +-- AtlasSpacing (int)
  +-- PackingAlgorithm (enum)
  +-- OutputFormat (enum: Text, Xml, Binary)
  +-- TextureFormat (enum: Png, Tga, Dds)
  +-- SuperSampleLevel (int)
  +-- Effects (EffectConfig[] -- ordered list of active effects)
  |     +-- EffectType (enum: Outline, Shadow, Gradient, Glow, ...)
  |     +-- Enabled (bool)
  |     +-- Parameters (Dictionary<string, object> -- effect-specific)
  +-- SdfEnabled (bool)
  +-- SdfSpread (int)
  +-- ChannelPacking (ChannelPackingConfig)
  +-- AntiAlias (bool)
  +-- Hinting (enum)
  +-- ExportSettings (ExportConfig)
        +-- LastExportPath (string)
        +-- FileNamingPattern (string -- e.g., "{family}_{size}")
        +-- LastUsedFormat (OutputFormat)
```

### Tasks

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 2.1 | Create `IProjectService` interface | Methods: `NewAsync()`, `OpenAsync(string path)`, `SaveAsync()`, `SaveAsAsync(string path)`, `CloseAsync()`. Properties: `CurrentProjectPath`, `IsDirty`, `ProjectState`. Events: `ProjectChanged`, `DirtyStateChanged`. | S |
| 2.2 | Create `ProjectService` implementation | Implements `IProjectService`. Holds current `ProjectState`. Manages dirty flag. Coordinates with ViewModels to extract/apply state. Registered as singleton in DI container. | M |
| 2.3 | Implement `ProjectState` model class | Data class holding all serializable project settings. Maps to/from `BmfcConfig`. Includes `ToConfig()` and `FromConfig(BmfcConfig)` conversion methods. Must capture every setting the UI exposes. | M |
| 2.4 | Implement `SaveAsync()` -- serialize to .bmfc | Extract current settings from all ViewModels into `ProjectState`. Convert to `BmfcConfig` via `ProjectState.ToConfig()`. Serialize using `BmfcConfigWriter` to produce .bmfc text. Write to disk at `CurrentProjectPath`. Clear dirty flag. **Important**: This path does NOT require a prior `BmFont.Generate()` call -- the UI must be able to save project config at any time. If a `BmFontResult` exists from a previous generation, `BmFontResult.ToBmfc()` may be used as an alternative, but it is not the primary path. See Phase 55 for ToBmfc round-trip concerns. | M |
| 2.5 | Implement font path relativization | When saving .bmfc, convert absolute font path to relative (relative to .bmfc file location) for portability. When loading, resolve relative path back to absolute using .bmfc file's directory as base. Fall back to absolute path if relative resolution fails. | S |
| 2.6 | Implement `OpenAsync()` -- deserialize from .bmfc | Read .bmfc file. Parse into `BmfcConfig` using library parser. Convert to `ProjectState`. Validate font file exists at resolved path. Apply state to all ViewModels. Set `CurrentProjectPath`. Clear dirty flag. Add to recent files. | M |
| 2.7 | Implement font-not-found recovery dialog | When opening a .bmfc whose font path no longer resolves: show `ModalDialog` with message "Font file not found at [path]." Three buttons: Browse (calls `NativeFileDialogSharp.Dialog.FileOpen("ttf,otf,woff,woff2,ttc")` to locate it), Skip (open project without font -- read-only config), Cancel. If Browse succeeds, update the project state with new path and mark dirty. | M |
| 2.8 | Implement ViewModel state extraction | `IProjectService` calls each ViewModel's `ExportState()` method to build `ProjectState`. Each ViewModel (`FontConfigViewModel`, `EffectsViewModel`, `AtlasConfigViewModel`, `CharacterSetViewModel`, `OutputConfigViewModel`) implements `IStateful<T>` with `ExportState()` and `ImportState(T)` methods. | M |
| 2.9 | Implement ViewModel state application | `IProjectService` calls each ViewModel's `ImportState(T)` method to apply loaded `ProjectState`. Must suppress change notifications during bulk import to avoid triggering regeneration and undo recording. Use an `IsImporting` flag that ViewModels check before raising change events. | M |
| 2.10 | Implement dirty state tracking | Subscribe to all ViewModel property changes (via GUM ViewModel `PropertyChanged` events). Any change after the last save sets `IsDirty = true`. Saving sets `IsDirty = false`. Opening a project sets `IsDirty = false`. New project sets `IsDirty = false`. Track via `DirtyStateChanged` event. | S |
| 2.11 | Implement title bar binding | Title bar format: `"{FontFamily} {Size}pt -- {ProjectFileName} -- KernSmith"`. When dirty: `"{FontFamily} {Size}pt -- {ProjectFileName}* -- KernSmith"` (asterisk). When untitled: `"Untitled -- KernSmith"`. Set via `Game.Window.Title` property, updated whenever `ProjectState` or `IsDirty` changes. | S |
| 2.12 | Implement save-before-close guard | Before New, Open, Close, Exit: check `IsDirty`. If dirty, show `ModalDialog`: "Save changes to {ProjectFileName}?" with three buttons: Save (save then proceed), Don't Save (proceed without saving), Cancel (abort the operation). Return `bool` indicating whether to proceed. | M |
| 2.13 | Implement `NewAsync()` | Trigger save guard. Reset all ViewModels to default values via `ImportState(ProjectState.Default)`. Clear `CurrentProjectPath`. Clear undo/redo stacks. Clear dirty flag. | S |
| 2.14 | Write round-trip tests | Unit tests: create `ProjectState` with all fields set, save to .bmfc string, parse back, compare field-by-field. Test with effects, character ranges, relative paths, system fonts. Verify no data loss in the round trip. Flag any fields that do not survive the round trip as known gaps. | M |

### .bmfc Format Notes

The .bmfc format is a BMFont-compatible configuration file. Key sections:

```ini
# BMFont configuration
font=Roboto-Regular.ttf
size=48
smooth=1
aa=1
padding=2,2,2,2
spacing=1,1
outline=3

# Character ranges
chars=32-126,160-255,8192-8303

# Effects (KernSmith extensions)
effect.shadow.enabled=true
effect.shadow.offsetX=2
effect.shadow.offsetY=2
effect.shadow.blur=3
effect.shadow.color=#00000080

# Atlas
atlas.width=1024
atlas.height=1024
atlas.packing=shelf

# Output
output.format=text
output.texture=png
```

The library's `BmfcConfig` parser handles the standard BMFont keys. KernSmith-specific extensions (effects, atlas settings) use dotted key prefixes. Any keys the parser does not recognize are preserved as-is for forward compatibility.

---

## Wave 3 — Export Workflows

Export generates the final .fnt descriptor + texture files and writes them to disk. Export is distinct from Save: Save writes the project configuration (.bmfc), Export writes the generated output.

### Export Pipeline

```
UI Settings -> FontGeneratorOptions -> BmFont.Generate() -> BmFontResult
  -> BmFontResult.ToFile(path, format)      // .fnt + textures to disk
  -> BmFontResult.FntText / FntXml          // descriptor content
  -> BmFontResult.GetPngData(pageIndex)     // individual texture pages
```

### Tasks

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 3.1 | Create `IExportService` interface | Methods: `ExportAsync(ExportOptions)`, `QuickExportAsync()`, `ExportPageAsync(int pageIndex, string path, TextureFormat)`, `ExportDescriptorAsync(string path, OutputFormat)`, `CopyDescriptorToClipboard(OutputFormat)`, `CopyPageToClipboard(int pageIndex)`. Properties: `LastExportPath`, `LastExportFormat`, `CanQuickExport`. | S |
| 3.2 | Create `ExportService` implementation | Implements `IExportService`. Depends on `IProjectService` (for current settings) and calls `BmFont.Generate()` then `BmFontResult.ToFile()`. Manages last-used export path and format. | M |
| 3.3 | Create `ExportOptions` model | Properties: `OutputDirectory` (string), `OutputFormat` (Text/Xml/Binary), `TextureFormat` (Png/Tga/Dds), `FileNamePattern` (string -- `"{family}_{size}"` by default), `OverwriteExisting` (bool), `OpenFolderAfterExport` (bool). | S |
| 3.4 | Create export dialog (GUM `ModalDialog`) | Modal dialog shown on Export / Export As. Built entirely from GUM code-only controls. Layout: output directory row (`TextRuntime` label + `TextBox` path + `Button` "Browse" that calls `NativeFileDialogSharp.Dialog.FolderPicker()`), file name pattern row (`TextBox` with live preview `TextRuntime` showing resulting filenames), output format selector (custom dropdown or `ListBox`: Text .fnt / XML .fnt / Binary .fnt), texture format selector (PNG / TGA / DDS), overwrite toggle (custom checkbox), open-folder-after toggle. OK and Cancel buttons. | M |
| 3.5 | Implement file naming pattern | Pattern tokens: `{family}` (font family name), `{style}` (Regular, Bold, etc.), `{size}` (font size), `{index}` (texture page index, zero-based). Default: `"{family}_{size}"` produces `Roboto_48.fnt` and `Roboto_48_0.png`. Preview `TextRuntime` in dialog updates live as user edits pattern. | S |
| 3.6 | Implement export execution with progress | Show animated progress indicator (GUM `ContainerRuntime` with animated width or spinner graphic) during generation. Run `BmFont.Generate()` on background thread via `Task.Run()`. Marshal result back to main thread using a completion flag checked in `Update()`. Call `BmFontResult.ToFile()` with chosen path and format. Handle exceptions (font not found, disk full, permission denied) with error `ModalDialog`. | M |
| 3.7 | Implement export summary dialog | After successful export, show `ModalDialog`: "Export Complete" with details -- files written (list with sizes rendered as `TextRuntime` lines), total size, output directory. "Open Folder" button to open the output directory via `Process.Start`. "Close" button. | S |
| 3.8 | Implement **Quick Export** (F6) | Re-export to `LastExportPath` with `LastExportFormat`. No dialogs. Show brief notification toast "Exported to {path}" for 3 seconds (see Wave 7). Disabled if no previous export path exists (falls back to Export As). Note: Ctrl+Shift+E is reserved for Export Font As. | S |
| 3.9 | Implement **Export Individual Texture Page** | Context menu (see Wave 7) on atlas page in preview: "Save Page As..." Calls `NativeFileDialogSharp.Dialog.FileSave("png;tga;dds")`. Writes `BmFontResult.GetPngData(pageIndex)` (or TGA/DDS variant) to chosen path. | S |
| 3.10 | Implement **Export Descriptor Only** | Menu item: "Save .fnt Only..." Calls `NativeFileDialogSharp.Dialog.FileSave("fnt")`. Writes `BmFontResult.FntText` (or XML/Binary) without any texture files. Useful for updating descriptor after external texture edits. | S |
| 3.11 | Implement **Copy to Clipboard** -- descriptor | Edit menu or context menu: "Copy .fnt to Clipboard". Copies `BmFontResult.FntText` (or `.FntXml`) as plain text to system clipboard via `SDL2` clipboard API (MonoGame uses SDL2 on DesktopGL) or `TextCopy` NuGet package. Only text/XML formats -- binary is not clipboard-friendly. | S |
| 3.12 | Implement **Copy to Clipboard** -- texture | Context menu on atlas page: "Copy Page to Clipboard". Copies the atlas page bitmap to the system clipboard as an image. Implementation via platform-specific clipboard APIs or `TextCopy`/`ClipboardService` for image data. May require native interop on each platform. | M |
| 3.13 | Implement overwrite confirmation | When exporting to a path where files already exist: show `ModalDialog` listing the files that will be overwritten. "Overwrite" / "Choose Different Path" / "Cancel" buttons. Skipped if `OverwriteExisting` is true in export options. | S |
| 3.14 | Track export path in project | After a successful export, store `LastExportPath` and `LastExportFormat` in the `ProjectState`. These are saved with the .bmfc file so re-opening a project remembers where it was last exported. | S |

---

## Wave 4 — Import Workflows

Import allows loading existing bitmap font output (.fnt + textures) for inspection, format conversion, or re-export. This is distinct from Open Project (which loads a .bmfc configuration).

### Tasks

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 4.1 | Create `IImportService` interface | Methods: `ImportFntAsync(string fntPath)`, `ImportBmfcAsync(string bmfcPath)`. Returns `ImportResult` with loaded data and any warnings. | S |
| 4.2 | Implement `ImportFntAsync()` | Call `BmFont.Load(fntPath)`. This reads the .fnt descriptor and loads associated texture page files from the same directory. Returns a `BmFontResult`-like object with descriptor data and texture bitmaps. | M |
| 4.3 | Display imported .fnt in preview | After import, show the loaded textures in the atlas preview panel as MonoGame `Texture2D` objects. Show the parsed descriptor info (character count, page count, line height, base) in an info overlay built with GUM `TextRuntime` elements. Read-only -- the user is viewing existing output, not editing a project. | M |
| 4.4 | Implement format conversion workflow | After importing a .fnt, enable "Export As" to write in a different format. Conversions supported: Text .fnt to XML .fnt, Text .fnt to Binary .fnt, PNG textures to TGA, PNG to DDS, etc. Uses the in-memory descriptor/texture data -- no regeneration needed. | M |
| 4.5 | Handle missing texture files on import | When `BmFont.Load()` finds the .fnt but a referenced texture page file is missing: show warning `ModalDialog` listing missing pages. Allow partial import (show descriptor data + available pages). Mark missing pages with placeholder "Page not found" text rendered via `TextRuntime` in preview. | S |
| 4.6 | Implement import via drag-and-drop | Accept .ttf, .otf, .woff, .bmfc, and .fnt files dropped onto the MonoGame window. Use MonoGame's `Window.FileDrop` event (available on DesktopGL/SDL2). Detect file type by extension. Route to appropriate handler: font files to Open Font, .bmfc to Open Project, .fnt to Import. Show visual drop target overlay (semi-transparent GUM `ColoredRectangleRuntime` with "Drop file here" `TextRuntime`). | M |
| 4.7 | Distinguish import mode vs project mode | When a .fnt is imported (not a project), the UI enters a "view-only" or "imported" mode. The left panel shows parsed info but settings are not editable (GUM controls have `IsEnabled = false` or input handlers disabled). Export As is available for format conversion. Title bar shows "Imported: {filename} -- KernSmith" via `Game.Window.Title`. | S |
| 4.8 | Implement import character list from file | "Import Characters" menu item. Calls `NativeFileDialogSharp.Dialog.FileOpen("txt")`. Reads all unique codepoints from the text file. Adds them to the character set selection. Merges with existing selection (does not replace). Shows count of characters added via notification toast. This may already be covered by Phase 61; if so, this task integrates it into the menu system. | S |
| 4.9 | Support importing multiple font files | When `NativeFileDialogSharp.Dialog.FileOpenMultiple("ttf,otf,woff,woff2,ttc")` returns multiple files, prompt via `ModalDialog`: "Multiple fonts selected. Open first font only?" (Future: tabs or multi-font merge). For now, open the first and show info about skipped files. | S |
| 4.10 | Add file association hints | On first run (or via Preferences), offer to associate .bmfc and .fnt file extensions with KernSmith. On Windows, this writes registry entries. On macOS/Linux, this creates .desktop file entries or plist. Implement as platform-specific with a common `IFileAssociationService` interface. Detect platform via `RuntimeInformation.IsOSPlatform()`. | M |

---

## Wave 5 — Recent Files & Session State

Persist user-level state across application sessions: recently opened files, window layout, and last-used directories.

### Storage Location

- **Windows**: `%APPDATA%/KernSmith/settings.json`
- **macOS**: `~/Library/Application Support/KernSmith/settings.json`
- **Linux**: `~/.config/KernSmith/settings.json`

The settings file is JSON. It is never checked into version control. It is user-scoped, not project-scoped.

### Settings Schema

```json
{
  "recentFiles": [
    {
      "path": "C:/Fonts/Projects/game-ui.bmfc",
      "type": "project",
      "lastOpened": "2026-03-21T14:30:00Z",
      "pinned": false
    },
    {
      "path": "C:/Fonts/Roboto-Regular.ttf",
      "type": "font",
      "lastOpened": "2026-03-20T09:15:00Z",
      "pinned": true
    }
  ],
  "window": {
    "x": 100,
    "y": 50,
    "width": 1400,
    "height": 900,
    "maximized": false,
    "configPanelVisible": true,
    "previewPanelVisible": true,
    "effectsPanelVisible": true,
    "configPanelWidth": 320,
    "previewPanelHeight": 400
  },
  "lastDirectories": {
    "openFont": "C:/Fonts",
    "openProject": "C:/Fonts/Projects",
    "export": "C:/Fonts/Output",
    "importCharacters": "C:/Fonts/CharSets"
  },
  "preferences": {
    "defaultFontSize": 32,
    "defaultOutputFormat": "text",
    "defaultTextureFormat": "png",
    "autoGenerateOnChange": false,
    "theme": "dark",
    "textureBackgroundColor": "#1a1a1a",
    "recentFilesLimit": 15,
    "showExportSummary": true,
    "openFolderAfterExport": false
  }
}
```

### Tasks

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 5.1 | Create `ISettingsService` interface | Methods: `Load()`, `Save()`, `GetSetting<T>(string key)`, `SetSetting<T>(string key, T value)`. Properties: `AppSettings` (the full settings object). Auto-saves on change (debounced to avoid thrashing). | S |
| 5.2 | Create `SettingsService` implementation | Reads/writes JSON settings file. Resolves path via `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` on Windows, equivalent on other platforms. Creates directory and default file on first run. Handles corrupted file gracefully (log warning, reset to defaults). Uses `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true }` for human-readable output. Debounce save: 500ms after last change using a timer. | M |
| 5.3 | Create `AppSettings` model class | Strongly-typed model matching the JSON schema above. Properties for `RecentFiles`, `WindowState`, `LastDirectories`, `Preferences`. Default values for all fields via property initializers. `[JsonPropertyName]` attributes for consistent key naming. | S |
| 5.4 | Create `IRecentFilesService` interface | Methods: `AddFile(string path, RecentFileType type)`, `RemoveFile(string path)`, `ClearAll()`, `PinFile(string path)`, `UnpinFile(string path)`, `GetRecentFiles()` (returns ordered list -- pinned first, then by last-opened descending). Property: `MaxFiles` (default 15). | S |
| 5.5 | Create `RecentFilesService` implementation | Backed by `ISettingsService`. Deduplicates by normalized path. Trims list to `MaxFiles` (pinned files do not count toward limit). Validates files still exist on retrieval -- removes stale entries silently. Fires `RecentFilesChanged` event for menu rebuilding. | M |
| 5.6 | Bind recent files to menu | `MenuBarViewModel` subscribes to `IRecentFilesService.RecentFilesChanged`. Rebuilds the Open Recent submenu `MenuDropdown` dynamically when the File menu opens. Each `MenuItem` shows filename. Separator before "Clear Recent Files" item. Pinned items shown at top with a pin indicator character in the label. | M |
| 5.7 | Implement window state persistence | On `Game.Exiting`: save window position, size, and panel visibility/sizes to settings via `ISettingsService`. MonoGame DesktopGL exposes `Window.Position` and `Window.ClientBounds` for reading, and `Graphics.PreferredBackBufferWidth/Height` for setting. On startup: restore from settings. Handle multi-monitor edge cases -- if saved position is off-screen, reset to center of primary monitor using `GraphicsAdapter.DefaultAdapter.CurrentDisplayMode`. | M |
| 5.8 | Implement panel layout persistence | Save panel widths/heights when user drags splitter handles (custom GUM drag handles at panel borders). Restore on startup by applying saved dimensions to panel `ContainerRuntime` elements. Each panel's visibility (shown/hidden) is also persisted. | S |
| 5.9 | Implement last-directory tracking | When calling `NativeFileDialogSharp.Dialog.FileOpen()` or `.FileSave()`, pass the last-used directory for that dialog type as the `defaultPath` parameter. After a file is selected, extract the directory and update via `ISettingsService`. Stored in `LastDirectories` in settings. | S |
| 5.10 | Implement preferences dialog | `ModalDialog` with preferences content panel. Built from GUM code-only controls: `TextBox` for default font size, custom dropdown for default output format, custom checkbox toggle for auto-generate on change, theme selector buttons (Light/Dark), `TextBox` for recent files limit. OK/Cancel buttons. Changes applied to `ISettingsService` on OK. Cancel discards changes. | M |
| 5.11 | Implement theme switching | Support light and dark color schemes. Theme selection stored in preferences. Define `ThemeColors` static class with named colors (`Background`, `Panel`, `Text`, `Accent`, `MenuHighlight`, etc.) that switch based on current theme. All GUM components reference `ThemeColors` properties. Apply on startup and on preference change by updating all color references and forcing a visual refresh. | M |
| 5.12 | Handle settings migration | When adding new settings in future versions, handle missing keys gracefully -- use defaults for keys not present in the file. Include a `"version": 1` field in the JSON. On load, if version is older, migrate forward (add new keys with defaults, never remove). `System.Text.Json` naturally ignores unknown properties with default options. | S |

---

## Wave 6 — Undo/Redo System

Implement a general-purpose undo/redo system that tracks all user-initiated setting changes and allows reverting them.

### Architecture

The undo system uses the **Command pattern**. Each undoable change is captured as an `UndoOperation` containing:

1. A human-readable description (e.g., "Change font size to 48")
2. A forward action (redo -- apply the change)
3. A reverse action (undo -- revert the change)

Operations are stored in two stacks: `UndoStack` and `RedoStack`. Performing a new action clears the redo stack (standard behavior -- new actions after an undo discard the redo future).

### Grouping & Throttling

Rapid-fire changes (slider dragging, typing in a text box) should not create one undo operation per intermediate value. Instead, changes to the same property within a short time window (300ms) are merged into a single operation that captures the first "before" value and the last "after" value.

Example: dragging the font size slider from 32 through 33, 34, 35, ... 48 over 2 seconds produces a single undo operation "Change font size from 32 to 48", not 16 separate operations.

### Tasks

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 6.1 | Create `IUndoRedoService` interface | Methods: `Execute(UndoOperation)`, `Undo()`, `Redo()`, `Clear()`, `BeginGroup(string description)`, `EndGroup()`. Properties: `CanUndo` (bool), `CanRedo` (bool), `UndoDescription` (string), `RedoDescription` (string), `UndoStackDepth` (int), `IsRecording` (bool). Events: `StateChanged`. | S |
| 6.2 | Create `UndoOperation` record | Properties: `Description` (string), `Undo` (Action), `Redo` (Action), `Timestamp` (DateTime), `PropertyName` (string -- for merge grouping), `TargetId` (string -- identifies which ViewModel/object). Immutable record type. | S |
| 6.3 | Create `UndoRedoService` implementation | Maintains `Stack<UndoOperation>` for undo and redo. `Execute()` pushes to undo stack and clears redo stack. `Undo()` pops from undo, calls `Undo` action, pushes to redo. `Redo()` pops from redo, calls `Redo` action, pushes to undo. Max stack depth: 100 (configurable). When exceeding max, drop oldest by converting to list, removing index 0, converting back. Raises `StateChanged` after every operation so UI can update menu item labels and enabled states. | M |
| 6.4 | Implement change throttling/merging | When `Execute()` is called, check if the top of the undo stack has the same `PropertyName` and `TargetId` and was created within 300ms. If so, merge: keep the original undo action (old "before" value) but replace the redo action (new "after" value). Update timestamp. This collapses slider drags into single operations. | M |
| 6.5 | Implement operation grouping | `BeginGroup(description)` starts collecting operations into a temporary list. `EndGroup()` wraps all collected operations into a single compound `UndoOperation` whose undo/redo executes all child operations in order (undo in reverse). Use case: loading a preset that changes 10 properties at once = one undo step. Nested groups are supported (inner group completes into outer group). | M |
| 6.6 | Create `UndoablePropertyMixin` or base class | Helper that ViewModels use to make property setters undoable. Usage: `this.SetUndoable(ref _fontSize, value, "font size")` instead of a plain setter. This method creates an `UndoOperation` capturing old and new values, calls `IUndoRedoService.Execute()`, and raises property changed via GUM ViewModel's notification system. Skips recording if `IUndoRedoService.IsRecording` is false. | M |
| 6.7 | Wire `FontConfigViewModel` to undo | Make font size, line height, baseline, character spacing, padding, hinting mode, anti-alias, super-sample level undoable. Each property setter uses `SetUndoable()`. | M |
| 6.8 | Wire `EffectsViewModel` to undo | Make effect enable/disable, outline width, outline color, shadow offset X/Y, shadow blur, shadow color, gradient colors/stops, glow radius, glow color undoable. Adding/removing an effect is a single undoable operation. Reordering effects is undoable. | M |
| 6.9 | Wire `AtlasConfigViewModel` to undo | Make atlas width, height, padding, spacing, packing algorithm undoable. | S |
| 6.10 | Wire `CharacterSetViewModel` to undo | Make character selection changes undoable. "Select range" is one operation (undo deselects the range). "Deselect All" is one operation (undo restores previous selection snapshot). Individual character toggle is one operation. | M |
| 6.11 | Wire `OutputConfigViewModel` to undo | Make output format (text/xml/binary), texture format (png/tga/dds) changes undoable. | S |
| 6.12 | Suppress undo during project load | When `IProjectService.OpenAsync()` applies state to ViewModels, set `IUndoRedoService.IsRecording = false`. All property changes during import are NOT recorded in undo history. After import completes, set `IsRecording = true` and call `Clear()` to reset both stacks. | S |
| 6.13 | Bind undo/redo to UI | Undo and Redo `MenuItem` instances show descriptions via dynamic label text: "Undo: Change font size to 48" from `UndoDescription`. `MenuItem.IsEnabled` bound to `CanUndo`/`CanRedo`. Toolbar buttons same. Status bar optionally shows undo stack depth. All updated when `IUndoRedoService.StateChanged` fires. | S |
| 6.14 | Write undo/redo unit tests | Test: single undo/redo, multiple undo, undo then new action clears redo, merge throttling (two changes within 300ms to same property = one operation), group operations, max stack depth trim, suppress during import, description formatting. | M |

---

## Wave 7 — Toolbar & Context Menus

Add toolbar and context menus for quick access to common operations. These are custom GUM components (GUM does not provide built-in Toolbar) that wrap the commands already implemented in previous waves.

### Toolbar Design

The toolbar sits below the menu bar. It is a horizontal `ContainerRuntime` containing icon buttons (24x24 or 32x32) with tooltip text. Buttons are visually grouped with vertical separator lines (`ColoredRectangleRuntime` 1px wide):

```
[Open Font] [Save] [Export] | [Generate] | [Undo] [Redo] | [Zoom -] [100%] [Zoom +] [Fit]
```

### Custom Toolbar Components

```csharp
// ToolbarButton: icon button with hover state and tooltip
public class ToolbarButton : ContainerRuntime
{
    // Fixed size (32x32 or 36x36 with padding)
    // Contains a SpriteRuntime for the icon
    // Hover: background color change
    // Disabled: icon rendered at 50% opacity
    // Tooltip: TextRuntime that appears below on hover after 500ms delay
    // Click invokes Action delegate
}

// ToolbarSeparator: thin vertical line
public class ToolbarSeparator : ColoredRectangleRuntime
{
    // Width = 1, Height = toolbar height - padding
    // Color = theme separator color
}
```

### Tasks

| # | Task | Detail | Estimate |
|---|------|--------|----------|
| 7.1 | Create `ToolbarPanel` GUM component | Horizontal `ContainerRuntime` with `ToolbarButton` and `ToolbarSeparator` instances. Fixed height (36px), full window width. Background color matches app theme. Uses `ChildrenLayout = ChildrenLayout.LeftToRight` for automatic horizontal stacking. Positioned below the `MenuBar`. | M |
| 7.2 | Create toolbar icon set | Design or source 24x24 icons for: Open (folder), Save (disk), Export (arrow-out), Generate (play triangle), Undo (curved left arrow), Redo (curved right arrow), Zoom In (magnifier +), Zoom Out (magnifier -), Fit (expand arrows). Icons stored as MonoGame `Texture2D` loaded from embedded content or PNG files. For theme support, icons can be tinted via `SpriteBatch` color parameter (white icons tinted to theme foreground color). | M |
| 7.3 | Bind toolbar to existing commands | Each `ToolbarButton.Click` invokes the same `Action` delegate as the corresponding `MenuItem` on `MenuBarViewModel`. Shared command methods ensure toolbar and menu are always in sync. `ToolbarButton.IsEnabled` reads the same can-execute flags. | S |
| 7.4 | Add zoom level indicator | Between zoom buttons, place a `TextRuntime` showing current zoom percentage: "100%", "200%", "50%". Clicking the text opens a small `MenuDropdown` with preset zoom levels: 25%, 50%, 100%, 200%, 400%, Fit. Each item calls the corresponding zoom action. | S |
| 7.5 | Create `ContextMenu` GUM component | Reusable component similar to `MenuDropdown` but positioned at the mouse cursor location. Appears on right-click (detected via MonoGame `Mouse.GetState()` with right button press tracking). Contains `MenuItem` instances. Closes on click outside, Escape, or item selection. Z-order rendered last so it appears above all other UI. Position clamped to window bounds so it does not go off-screen. | M |
| 7.6 | Create atlas preview context menu | Right-click on the atlas preview panel shows `ContextMenu`. Items: Save Page As (submenu: PNG, TGA, DDS -- each calls `NativeFileDialogSharp.Dialog.FileSave()`), Copy Page to Clipboard, separator, Zoom In, Zoom Out, Fit to Window, Actual Size, separator, Show Glyph Bounds (toggle), Show Grid (toggle). All bound to existing commands. | S |
| 7.7 | Create character grid context menu | Right-click on the character selection grid shows `ContextMenu`. Items: Select Unicode Block (submenu of blocks), Deselect All, Select All, separator, Character Info (shows codepoint, name, category for clicked character in a tooltip or small overlay), Copy Character (to system clipboard), separator, Import Characters from File (calls `NativeFileDialogSharp.Dialog.FileOpen("txt")`). | S |
| 7.8 | Create effects panel context menu | Right-click on an effect in the effects list shows `ContextMenu`. Items: Reset to Defaults (resets effect parameters), Duplicate Effect, Remove Effect, separator, Move Up, Move Down, separator, Copy Settings (copies effect config as text to clipboard), Paste Settings (reads clipboard, parses effect config). | S |
| 7.9 | Implement toolbar visibility toggle | View menu item "Show Toolbar" (toggle, checked by default). Persisted in session state via `ISettingsService`. When hidden, `ToolbarPanel` height set to 0 and content panels shift up, providing more space for panels. | S |
| 7.10 | Implement status bar | Bottom `ContainerRuntime` (24px height, full width) showing: current font family + size (`TextRuntime`, left-aligned), character count, atlas page count, atlas dimensions (center area), last generation time in ms (right-aligned). Updated after each generation by subscribing to `PreviewViewModel` property changes. Background color from theme. | M |
| 7.11 | Implement notification toasts | Non-modal notification system for brief messages: "Project saved", "Export complete", "Font loaded". Toast is a `ContainerRuntime` with background color, text, and close button. Slides in from bottom-right corner using position animation in `Update()`. Auto-dismiss after 3 seconds. Click to dismiss early. Stack up to 3 concurrent toasts with vertical offset. Used by Quick Export and other operations that should not block the user. Managed by `INotificationService`. | M |

### Tooltip Implementation

GUM does not have built-in tooltips. Custom tooltip system:

```csharp
public class TooltipManager
{
    private TextRuntime _tooltipElement;
    private ContainerRuntime _tooltipContainer;
    private float _hoverTimer;
    private const float ShowDelay = 0.5f; // seconds

    public void Update(GameTime gameTime, Point mousePosition)
    {
        var hoveredControl = FindHoveredControl(mousePosition);
        if (hoveredControl?.TooltipText != null)
        {
            _hoverTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_hoverTimer >= ShowDelay)
            {
                _tooltipElement.Text = hoveredControl.TooltipText;
                _tooltipContainer.X = mousePosition.X + 12;
                _tooltipContainer.Y = mousePosition.Y + 16;
                _tooltipContainer.Visible = true;
            }
        }
        else
        {
            _hoverTimer = 0;
            _tooltipContainer.Visible = false;
        }
    }
}
```

---

## NativeFileDialogSharp Usage Patterns

All file dialogs use `NativeFileDialogSharp` for native OS open/save/folder dialogs. This avoids building custom file browsers in GUM.

```csharp
// Open single file
var result = Dialog.FileOpen("ttf,otf,woff,woff2,ttc", defaultPath);
if (result.IsOk)
{
    string filePath = result.Path;
    // process file...
}

// Open multiple files
var result = Dialog.FileOpenMultiple("bmfc", defaultPath);
if (result.IsOk)
{
    foreach (string path in result.Paths)
    {
        // process each file...
    }
}

// Save file
var result = Dialog.FileSave("bmfc", defaultPath);
if (result.IsOk)
{
    string savePath = result.Path;
    // save to path...
}

// Pick folder
var result = Dialog.FolderPicker(defaultPath);
if (result.IsOk)
{
    string folderPath = result.Path;
    // use folder...
}
```

Key notes:
- Filter strings use comma-separated extensions without dots: `"ttf,otf,woff"`
- `defaultPath` parameter remembers last-used directory (from `ISettingsService.LastDirectories`)
- Dialogs are blocking calls -- they freeze the game loop while open (acceptable for file dialogs)
- Returns `DialogResult` with `.IsOk`, `.IsCancelled`, `.IsError` status checks

---

## Dependencies & Ordering

```
Wave 1 (Menu System)
  +-- Wave 2 (Save/Load) -- menus call project service
  +-- Wave 3 (Export) -- menus call export service
  +-- Wave 4 (Import) -- menus call import service
  +-- Wave 5 (Recent Files) -- menu populates from service
  +-- Wave 6 (Undo/Redo) -- Edit menu calls undo service
Wave 7 (Toolbar) -- depends on all commands from Waves 1-6 existing
```

Waves 2-6 can be developed in parallel after Wave 1 establishes the menu structure and command interfaces. Wave 7 is a thin UI layer and should come last.

---

## Files to Create

| File | Namespace | Purpose |
|------|-----------|---------|
| `MenuBar.cs` | `KernSmith.Ui.Components` | Custom GUM menu bar component |
| `MenuDropdown.cs` | `KernSmith.Ui.Components` | Dropdown panel for menu items |
| `MenuItem.cs` | `KernSmith.Ui.Components` | Single menu item component |
| `MenuBarViewModel.cs` | `KernSmith.Ui.ViewModels` | Menu commands and can-execute state |
| `InputManager.cs` | `KernSmith.Ui.Input` | Keyboard shortcut handler |
| `ToolbarPanel.cs` | `KernSmith.Ui.Components` | Custom GUM toolbar component |
| `ToolbarButton.cs` | `KernSmith.Ui.Components` | Icon button for toolbar |
| `ToolbarSeparator.cs` | `KernSmith.Ui.Components` | Vertical separator for toolbar |
| `ContextMenu.cs` | `KernSmith.Ui.Components` | Right-click context menu component |
| `ModalDialog.cs` | `KernSmith.Ui.Components` | Reusable modal dialog with overlay |
| `TooltipManager.cs` | `KernSmith.Ui.Components` | Hover tooltip system |
| `NotificationToast.cs` | `KernSmith.Ui.Components` | Slide-in toast notification |
| `IProjectService.cs` | `KernSmith.Ui.Services` | Project lifecycle interface |
| `ProjectService.cs` | `KernSmith.Ui.Services` | Project lifecycle implementation |
| `ProjectState.cs` | `KernSmith.Ui.Models` | Serializable project state |
| `IExportService.cs` | `KernSmith.Ui.Services` | Export interface |
| `ExportService.cs` | `KernSmith.Ui.Services` | Export implementation |
| `ExportOptions.cs` | `KernSmith.Ui.Models` | Export configuration |
| `IImportService.cs` | `KernSmith.Ui.Services` | Import interface |
| `ImportService.cs` | `KernSmith.Ui.Services` | Import implementation |
| `ISettingsService.cs` | `KernSmith.Ui.Services` | User settings interface |
| `SettingsService.cs` | `KernSmith.Ui.Services` | User settings implementation |
| `AppSettings.cs` | `KernSmith.Ui.Models` | Settings model |
| `IRecentFilesService.cs` | `KernSmith.Ui.Services` | Recent files interface |
| `RecentFilesService.cs` | `KernSmith.Ui.Services` | Recent files implementation |
| `IUndoRedoService.cs` | `KernSmith.Ui.Services` | Undo/redo interface |
| `UndoRedoService.cs` | `KernSmith.Ui.Services` | Undo/redo implementation |
| `UndoOperation.cs` | `KernSmith.Ui.Models` | Undo operation record |
| `IStateful.cs` | `KernSmith.Ui.ViewModels` | State export/import interface |
| `INotificationService.cs` | `KernSmith.Ui.Services` | Toast notification interface |
| `NotificationService.cs` | `KernSmith.Ui.Services` | Toast notification implementation |
| `ThemeColors.cs` | `KernSmith.Ui.Themes` | Named color definitions for light/dark themes |
| `IFileAssociationService.cs` | `KernSmith.Ui.Services` | File extension association interface |

---

## NuGet Dependencies (UI Project)

| Package | Purpose |
|---------|---------|
| `MonoGame.Framework.DesktopGL` | Game framework, windowing, input, rendering |
| `MonoGame.Extended` | Camera, input helpers, shape rendering |
| `Gum.MonoGame` | GUM UI toolkit (code-only) |
| `NativeFileDialogSharp` | Cross-platform native file open/save/folder dialogs |
| `System.Text.Json` | Settings serialization (included in .NET runtime) |

---

## Core Library Notes (Document, Don't Fix)

These are observations about the KernSmith core library that affect the UI implementation. They should be noted here and addressed in separate library phases if needed.

- **BmfcConfig completeness**: Verify that `BmfcConfig` captures ALL settings that the UI exposes. If the UI adds new settings (e.g., SDF spread, channel packing), the .bmfc format and parser may need extension. Phase 55 tracks known gaps in BmfcConfig coverage.
- **ToBmfc() round-trip fidelity**: Test that saving settings via `ToBmfc()` and loading via `FromConfig()` produces identical `FontGeneratorOptions`. Any fields that do not survive round-trip must be documented as known gaps. See **Phase 55** for detailed analysis of .bmfc round-trip gaps and BmfcConfigWriter completeness.
- **BmFont.Load() state reconstruction**: `BmFont.Load()` reads an existing .fnt + textures, but it may not reconstruct enough state to populate all UI controls (e.g., it cannot know the original effects, packing algorithm, or super-sample level). The import workflow should only expose what `Load()` actually returns.
- **BmfcConfig vs FontGeneratorOptions gaps**: If `BmfcConfig` has properties that `FontGeneratorOptions` does not (or vice versa), document the mapping gaps. The UI needs to know which settings are preserved through the .bmfc format and which are transient.
- **Effect serialization format**: Verify that all effect parameters (gradient stops, shadow color with alpha, outline join style) have string representations in .bmfc format. Complex types may need custom serializers.
- **Texture format in .bmfc**: The standard .bmfc format may not have a field for texture format (PNG vs TGA vs DDS). This may need a KernSmith extension key.

---

## Success Criteria

| Criterion | Verification |
|-----------|-------------|
| Full project save/load cycle | Save .bmfc with all settings, close app, reopen .bmfc, verify all settings match original |
| Export produces correct output | Export generates valid .fnt + texture files readable by BMFont-compatible tools |
| Format conversion works | Import .fnt (text) + PNG, export as .fnt (XML) + TGA, verify output valid |
| Undo/redo for all settings | Change every setting type, undo each, verify state reverted, redo, verify restored |
| Slider drag produces single undo | Drag font size slider, release, undo once, verify original value restored |
| Dirty state tracking | Change setting, verify asterisk in title, save, verify asterisk removed |
| Save guard prevents data loss | Change setting, try to close, verify save prompt appears, cancel prevents close |
| Recent files persist | Open 3 fonts, close app, reopen, verify all 3 in recent files menu |
| Window state persists | Resize window, hide a panel, close app, reopen, verify layout matches |
| Keyboard shortcuts work | Test every shortcut in the shortcut table, verify correct action fires |
| Menu items enable/disable correctly | No font loaded: Generate disabled. No undo history: Undo disabled. Not dirty: no save prompt. |
| Export summary accurate | Export, check summary dialog file list against actual files on disk |
| Missing font recovery | Edit .bmfc to point to nonexistent font, open, verify recovery dialog appears |
| Batch generate | Create 3 .bmfc files, batch generate all, verify 3 sets of output files |
| NativeFileDialogSharp dialogs work | Open/Save/FolderPicker dialogs display correctly on Windows, macOS, Linux |
| Custom GUM menus behave correctly | Dropdown opens/closes, keyboard navigation works, submenus appear, disabled items are grayed |
| Context menus appear at cursor | Right-click atlas/character grid/effects panel, verify context menu at mouse position |
| Toasts auto-dismiss | Trigger Quick Export, verify toast appears and auto-dismisses after 3 seconds |

---

## Estimated Effort

| Wave | Tasks | Estimate |
|------|-------|----------|
| Wave 1 -- Menu System | 36 | Very Large (custom menu system from scratch) |
| Wave 2 -- Project Save/Load | 14 | Large |
| Wave 3 -- Export Workflows | 14 | Medium-Large |
| Wave 4 -- Import Workflows | 10 | Medium |
| Wave 5 -- Recent Files & Session State | 12 | Medium |
| Wave 6 -- Undo/Redo System | 14 | Large |
| Wave 7 -- Toolbar & Context Menus | 11 | Medium-Large (custom toolbar + context menus) |
| **Total** | **111** | **Very Large** |

This phase is one of the largest in the UI track. Wave 1 is larger than the Avalonia equivalent because GUM does not provide built-in MenuBar, Toolbar, or ContextMenu controls -- these must be built from primitives. Wave 7 is also larger for the same reason. Consider splitting into sub-phases (65a: Menu + Save/Load, 65b: Export/Import, 65c: Undo/Redo + Polish) if the scope is too large for a single implementation pass.
