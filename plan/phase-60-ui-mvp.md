# Phase 60 — UI MVP: Core Font Generation Interface

> **Status**: In Progress
> **Created**: 2026-03-21
> **Goal**: Build a minimum viable desktop application that wraps the KernSmith NuGet library with a MonoGame + GUM UI interface, enabling users to load a font, configure basic settings, generate a bitmap font, preview the atlas, and save output to disk.

---

## Architectural Decisions

### Framework: MonoGame + GUM UI + MonoGame.Extended

| Decision | Rationale |
|----------|-----------|
| **MonoGame (DesktopGL)** | Cross-platform .NET game framework with GPU-accelerated rendering. Native Texture2D display for atlas preview. Single codebase for Windows, macOS, Linux. |
| **GUM UI (code-only)** | Lightweight retained-mode UI framework with built-in Forms controls (Button, TextBox, ComboBox, etc.). No XAML, no editor — everything is code-only. Built-in ViewModel base class with `Get`/`Set` pattern and `SetBinding()`. |
| **MonoGame.Extended** | Utility library for MonoGame — screen management, input handling, and other conveniences. |
| **NativeFileDialogSharp** | Cross-platform native OS file dialogs (open/save). Avoids reimplementing file pickers in GUM. |
| **Single-project architecture** | One `apps/KernSmith.Ui/` project. GUM does not require view/viewmodel separation into separate assemblies — ViewModels live alongside the Game class in organized folders. No XAML, no view resolution, no external MVVM framework. |
| **Direct library reference** | The UI project references `src/KernSmith/` directly. It does NOT shell out to the CLI. All generation happens in-process via `BmFont.Builder()`. |

**Cross-Platform & Future Web Path:**
MonoGame DesktopGL targets Windows, macOS (including Apple Silicon), and Linux from a single project via SDL2 + OpenGL. No additional framework is needed for desktop cross-platform support. For a future web version, KNI (an API-compatible MonoGame fork) provides Blazor WebGL support — switching requires only NuGet reference changes, not code changes. GUM has `Gum.KNI` and MonoGame.Extended has `KNI.Extended` with identical APIs. The web rasterization challenge (FreeTypeSharp doesn't work in WASM) is tracked in Phase 30. The desktop UI is built on MonoGame DesktopGL with confidence that it does not lock out a future KNI web migration.

### Project Structure

```
apps/
  KernSmith.Ui/
    KernSmith.Ui.csproj
    Program.cs
    KernSmithGame.cs                     # MonoGame Game subclass (Initialize, Update, Draw)
    Layout/
      MainLayout.cs                      # Root layout: menu bar, three-panel splitter, status bar
      FontConfigPanel.cs                 # Left panel: font loading, size, character set
      PreviewPanel.cs                    # Center panel: atlas image display via SpriteRuntime
      EffectsPanel.cs                    # Right panel: placeholder for future effects config
      MenuBar.cs                         # Top menu bar (File, Edit, View, Tools, Help)
      StatusBar.cs                       # Bottom status bar
    ViewModels/
      MainViewModel.cs                   # Root ViewModel, orchestrates child VMs
      FontConfigViewModel.cs             # Font loading, size, character set config
      PreviewViewModel.cs                # Atlas page display state
      StatusBarViewModel.cs              # Status bar text and progress state
    Models/
      PreviewPage.cs                     # Data class for a single preview page
      GenerationRequest.cs               # Request record for GenerationService
      SystemFontGroup.cs                 # Grouped system font family
      FontSourceKind.cs                  # Enum: None, File, System
      CharacterSetPreset.cs              # Enum: Ascii, ExtendedAscii, Latin, Custom
    Services/
      GenerationService.cs              # Wraps BmFont.Builder() on background thread
      FontDiscoveryService.cs           # Wraps DefaultSystemFontProvider
      FileDialogService.cs              # Wraps NativeFileDialogSharp for open/save
    Styling/
      Theme.cs                          # Dark theme colors and style definitions
    Content/
      Content.mgcb                      # MonoGame Content Pipeline (empty — we load fonts via KernSmith, not content pipeline)
    Assets/
      kernsmith-icon.ico
```

### Separation of Concerns

- **`KernSmithGame`** is the MonoGame `Game` subclass. It owns the GUM UI initialization, update loop, and draw loop. It creates `MainLayout` and `MainViewModel` and wires them together.
- **`Layout/` classes** build the GUM UI hierarchy (containers, controls, bindings). They are thin layout shells — no business logic. Each panel class creates GUM controls and binds them to ViewModel properties via `SetBinding()`.
- **`ViewModels/`** contain all state and logic. They extend GUM's `ViewModel` base class with `Get<T>()`/`Set(value)` pattern. They have zero dependency on MonoGame or GUM visual types.
- **`Services/`** handle external concerns: file dialogs, font discovery, generation pipeline.

---

## Wave 1: Project Scaffolding

**Goal**: Bootable MonoGame application with GUM UI initialized, empty window with dark background, and all NuGet dependencies resolved.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 1.1 | Create `KernSmith.Ui.csproj` | `<OutputType>WinExe</OutputType>`, `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`. PackageRefs: `MonoGame.Framework.DesktopGL`, `Gum.MonoGame`, `MonoGame.Extended`, `NativeFileDialogSharp`. ProjectRef: `..\..\src\KernSmith\KernSmith.csproj`. RootNamespace: `KernSmith.Ui`. | S |
| 1.2 | Add project to `KernSmith.sln` | Under solution folder `apps/`. Ensure `dotnet build` at solution root picks it up. | S |
| 1.3 | Create `Program.cs` | Standard MonoGame entry point: `using var game = new KernSmithGame(); game.Run();` | S |
| 1.4 | Create `KernSmithGame.cs` | Inherits `Microsoft.Xna.Framework.Game`. Constructor: set `Window.Title = "KernSmith"`, set `IsMouseVisible = true`, set preferred back buffer to 1280x720, set `Window.AllowUserResizing = true`. In `Initialize()`: call `GumUI.Initialize(this, DefaultVisualsVersion.V3)`. In `Update()`: call `GumUI.Update(gameTime)`. In `Draw()`: clear to dark background color (`new Color(30, 30, 30)`), call `GumUI.Draw()`. | S |
| 1.5 | Create `Theme.cs` | Static class defining dark theme colors as `Microsoft.Xna.Framework.Color` constants: `Background` (#1E1E1E), `Panel` (#252526), `PanelBorder` (#3C3C3C), `Text` (#CCCCCC), `TextMuted` (#888888), `Accent` (#0078D4), `AccentHover` (#1A8AD4), `Error` (#F44747), `Success` (#4EC9B0). Used by layout classes when styling controls. | S |
| 1.6 | Create stub `MainLayout.cs` | Creates a root `ContainerRuntime` that fills the entire screen (`WidthUnits = RelativeToContainer`, `HeightUnits = RelativeToContainer`, `Width = 0`, `Height = 0` with 100% relative). Adds a single `Label` with text "KernSmith" centered on screen. Adds the root container to `GumUI`'s root via `ContainerRuntime.Children.Add()`. | S |
| 1.7 | Wire layout in `KernSmithGame.Initialize()` | After `GumUI.Initialize()`, instantiate `MainLayout` and call its `Build()` method. Store reference for later update/teardown. | S |
| 1.8 | Create `Content.mgcb` | Empty MonoGame content project. The app loads fonts via KernSmith's API, not the content pipeline. Include only the app icon if needed. | S |
| 1.9 | Verify build and launch | `dotnet build apps/KernSmith.Ui/KernSmith.Ui.csproj` succeeds. `dotnet run --project apps/KernSmith.Ui` opens a dark window titled "KernSmith" with centered label text. Window is resizable. | S |

### NuGet Dependency Table

| Package | Version | Purpose | Referenced By |
|---------|---------|---------|---------------|
| `MonoGame.Framework.DesktopGL` | latest stable | Core game framework (DesktopGL for cross-platform) | KernSmith.Ui |
| `Gum.MonoGame` | latest stable | Code-only UI framework with Forms controls | KernSmith.Ui |
| `MonoGame.Extended` | latest stable | MonoGame utilities (screen management, etc.) | KernSmith.Ui |
| `NativeFileDialogSharp` | latest stable | Cross-platform native file open/save dialogs | KernSmith.Ui |

### Key Code Patterns

**GUM initialization** (in `KernSmithGame`):
```csharp
using Gum.Wireframe;
using MonoGameGum;
using MonoGameGum.Forms;

namespace KernSmith.Ui;

public class KernSmithGame : Game
{
    private GraphicsDeviceManager _graphics;
    private MainLayout? _mainLayout;

    public KernSmithGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 720;
        Window.Title = "KernSmith";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        GumUI.Initialize(this, DefaultVisualsVersion.V3);

        _mainLayout = new MainLayout();
        _mainLayout.Build();

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
```

**GUM ViewModel pattern** (all ViewModels):
```csharp
using Gum.Mvvm;

namespace KernSmith.Ui.ViewModels;

public class MainViewModel : ViewModel
{
    public FontConfigViewModel FontConfig { get => Get<FontConfigViewModel>(); set => Set(value); }
    public PreviewViewModel Preview { get => Get<PreviewViewModel>(); set => Set(value); }
    public StatusBarViewModel StatusBar { get => Get<StatusBarViewModel>(); set => Set(value); }
}
```

**GUM control creation and binding** (layout classes):
```csharp
var label = new Label();
label.Text = "Font Size:";
label.X = 10;
label.Y = 100;
parentContainer.AddChild(label.Visual);

var textBox = new TextBox();
textBox.X = 100;
textBox.Y = 100;
textBox.Width = 80;
textBox.SetBinding(nameof(TextBox.Text), nameof(FontConfigViewModel.FontSize));
textBox.Visual.BindingContext = viewModel;
parentContainer.AddChild(textBox.Visual);
```

---

## Wave 2: Main Window Layout (Three-Panel Design)

**Goal**: Three resizable panels (left, center, right) with a menu bar and status bar, using GUM StackPanels and Splitters.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 2.1 | Design `MainLayout.Build()` | Root `ContainerRuntime` fills window. Top: menu bar `StackPanel` (horizontal, docked top, fixed height 30px). Bottom: status bar `ContainerRuntime` (fixed height 24px, docked bottom). Center: horizontal `ContainerRuntime` hosting three child panels with `Splitter` controls between them. | M |
| 2.2 | Create `MenuBar.cs` | Horizontal `StackPanel` with `Button` controls styled as menu items: **File**, **Edit**, **View**, **Tools**, **Help**. Each button shows a dropdown-style list (using `ListBox` positioned absolutely below the button) on click. Menu items listed in the menu command table below. Use GUM `Button.Click` events that delegate to `MainViewModel` methods. **Note:** Consider using GUM's built-in `MenuItem` control as an alternative to the custom Button+ListBox approach. | M |
| 2.3 | Create `FontConfigPanel.cs` | Left panel stub. `ContainerRuntime` with fixed initial width 280px, background color `Theme.Panel`. Contains a single `Label` with text "Font Configuration" as header. Panel is the left child of the center splitter layout. Will be populated in Wave 3. | S |
| 2.4 | Create `PreviewPanel.cs` | Center panel stub. `ContainerRuntime` that fills remaining width (percentage-based sizing). Background color `Theme.Background`. Contains centered `Label` with text "No atlas generated" and muted text color. Will show atlas `SpriteRuntime` in Wave 4. | S |
| 2.5 | Create `EffectsPanel.cs` | Right panel stub. `ContainerRuntime` with fixed initial width 240px, background color `Theme.Panel`. Contains `Label` with text "Effects (coming soon)". Placeholder for future phases; not wired in Phase 60. | S |
| 2.6 | Add `Splitter` between panels | Two `Splitter` controls: one between left and center panels, one between center and right panels. GUM `Splitter` allows users to drag and resize the adjacent panels. Configure minimum widths (left: 200px, center: 300px, right: 180px). | M |
| 2.7 | Wire menu bar actions | **File > Open Font...**: calls `MainViewModel.OpenFont()`. **File > Save As...**: calls `MainViewModel.SaveAs()`. **File > Exit**: calls `MainViewModel.Exit()` (which calls `Game.Exit()`). **View > Reset Layout**: resets splitter positions to defaults. **Help > About**: shows a centered GUM `Window` (modal dialog) with app name, version, and an OK button. All other menu items are stubs (disabled or no-op). | M |
| 2.8 | Create `StatusBar.cs` | Horizontal `ContainerRuntime` at the bottom of the window (24px tall, dark border top). Four `Label` controls arranged horizontally: status text (left, fills remaining), atlas dimensions (right, fixed width), glyph count (right, fixed width), generation time (right, fixed width). Separated by `|` divider labels. | S |
| 2.9 | Create `StatusBarViewModel.cs` | Properties: `string StatusText` (default "Ready"), `string AtlasDimensions` (default ""), `int GlyphCount` (default 0), `string GenerationTime` (default ""), `bool IsGenerating` (default false). All using GUM `Get<T>()`/`Set()` pattern. | S |
| 2.10 | Bind status bar to ViewModel | `StatusBar` labels use `SetBinding()` to bind `Text` to `StatusBarViewModel` properties. `BindingContext` set to `StatusBarViewModel` instance. | S |
| 2.11 | Handle window resize | Subscribe to `Window.ClientSizeChanged` event. On resize, update GUM root container dimensions so the layout reflows correctly. GUM's percentage-based sizing should handle most of this automatically. | S |
| 2.12 | Responsive layout testing | Verify layout at 960x640, 1920x1080, and 1280x720. Panels should resize with splitters. Minimum window size enforced at 960x640 (set via MonoGame window properties or clamp in resize handler). | S |

### Layout Diagram

```
+------------------------------------------------------------------+
| File  Edit  View  Tools  Help                          (Menu Bar) |
+----------+---+----------------------------+---+------------------+
|          | | |                            | | |                  |
|  Font    |S| |                            |S| |  Effects         |
|  Config  |p| |     Preview / Atlas        |p| |  (placeholder)   |
|  Panel   |l| |     (center, fills)        |l| |                  |
|  (280px) |i| |                            |i| |  (240px)         |
|          |t| |                            |t| |                  |
|          | | |                            | | |                  |
+----------+---+----------------------------+---+------------------+
| Ready                        | 512x512 | 95 glyphs | 0.42s     |
+------------------------------------------------------------------+
```

### Menu Command Bindings

| Menu Item | ViewModel Method | Implemented In |
|-----------|-----------------|----------------|
| File > Open Font... | `MainViewModel.OpenFont()` | Wave 3 |
| File > Save As... | `MainViewModel.SaveAs()` | Wave 4 |
| File > Exit | `MainViewModel.Exit()` | Wave 2 (calls `Game.Exit()`) |
| View > Reset Layout | `MainViewModel.ResetLayout()` | Wave 2 (resets splitter positions) |
| Help > About | `MainViewModel.ShowAbout()` | Wave 2 (shows GUM `Window` dialog) |

### StatusBarViewModel

```csharp
using Gum.Mvvm;

namespace KernSmith.Ui.ViewModels;

public class StatusBarViewModel : ViewModel
{
    public string StatusText { get => Get<string>(); set => Set(value); }
    public string AtlasDimensions { get => Get<string>(); set => Set(value); }
    public int GlyphCount { get => Get<int>(); set => Set(value); }
    public string GenerationTime { get => Get<string>(); set => Set(value); }
    public bool IsGenerating { get => Get<bool>(); set => Set(value); }

    public StatusBarViewModel()
    {
        StatusText = "Ready";
        AtlasDimensions = "";
        GenerationTime = "";
    }

    public void SetIdle() { StatusText = "Ready"; IsGenerating = false; }

    public void SetGenerating() { StatusText = "Generating..."; IsGenerating = true; }

    public void SetComplete(int pageCount, int scaleW, int scaleH, int glyphCount, TimeSpan elapsed)
    {
        StatusText = $"Generation complete ({pageCount} page(s))";
        AtlasDimensions = $"{scaleW}x{scaleH}";
        GlyphCount = glyphCount;
        GenerationTime = $"{elapsed.TotalSeconds:F2}s";
        IsGenerating = false;
    }

    public void SetError(string message)
    {
        StatusText = $"Error: {message}";
        IsGenerating = false;
    }
}
```

### About Dialog

```csharp
// In MainViewModel or MainLayout — shown as a centered GUM Window
var aboutWindow = new Window();
aboutWindow.Width = 300;
aboutWindow.Height = 180;
// Position centered on screen
aboutWindow.X = (screenWidth - 300) / 2;
aboutWindow.Y = (screenHeight - 180) / 2;

var stack = new StackPanel();
stack.Orientation = Orientation.Vertical;

var title = new Label();
title.Text = "KernSmith";
// ... font size, bold styling

var version = new Label();
version.Text = "Version 1.0.0";

var desc = new Label();
desc.Text = "Bitmap Font Generator";

var okButton = new Button();
okButton.Text = "OK";
okButton.Click += (s, e) => aboutWindow.Visible = false;

stack.AddChild(title.Visual);
stack.AddChild(version.Visual);
stack.AddChild(desc.Visual);
stack.AddChild(okButton.Visual);
aboutWindow.AddChild(stack.Visual);
```

---

## Wave 3: Font Loading

**Goal**: Load fonts from file or system, display font metadata, and configure font size.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 3.1 | Create `FileDialogService` | Wraps `NativeFileDialogSharp`. Methods: `string? OpenFontFile()` — calls `Dialog.FileOpen("ttf,otf,woff,ttc")`, returns selected path or null. `string? SaveFile(string defaultName, string filter)` — calls `Dialog.FileSave(filter)`. No interface needed — this is a concrete service (no unit-test mocking required for MVP). | S |
| 3.2 | Create `FontDiscoveryService` | Wraps `DefaultSystemFontProvider.GetInstalledFonts()`. Returns `IReadOnlyList<SystemFontGroup>` grouped by family name. Method: `IReadOnlyList<SystemFontGroup> GetSystemFonts()` where `SystemFontGroup` has `FamilyName` and `Styles` (list of `SystemFontInfo`). Caches on first call. | M |
| 3.3 | Create `FontConfigViewModel` | Full ViewModel for the left panel. Extends GUM `ViewModel`. See property table below. | M |
| 3.4 | Implement `FontConfigViewModel.LoadFromFile(string path)` | Reads the font file to `byte[]`. Creates a temporary `TtfFontReader`, calls `ReadFont(fontData, faceIndex: 0)` to get `FontInfo`. Populates metadata properties. Stores `byte[]` for later generation. Sets `IsFontLoaded = true`. **Note:** `TtfFontReader` is currently internal. Depends on Phase 55 to either make it public or provide a `BmFont.ReadFontInfo()` API. | M |
| 3.5 | Implement `FontConfigViewModel.LoadFromSystem(SystemFontInfo systemFont)` | Reads `File.ReadAllBytes(systemFont.FilePath)`. Calls `ReadFont()` same as file path. Sets `FontSourceDescription` to `$"{familyName} (System)"`. Sets `FontSourceKind = FontSourceKind.System`. | S |
| 3.6 | Implement `MainViewModel.OpenFont()` | Calls `FileDialogService.OpenFontFile()`. On success, calls `FontConfig.LoadFromFile(path)`. Updates `StatusBar.StatusText` with loaded font info. | S |
| 3.7 | Build `FontConfigPanel.cs` layout | GUM layout using `StackPanel` (vertical) with sections. See control layout below. All controls created in code and bound to `FontConfigViewModel` via `SetBinding()`. | M |
| 3.8 | System font ComboBox | Two `ComboBox` controls: one for font family (populated from `FontDiscoveryService.GetSystemFonts()` family names), one for style (populated when family is selected). On style selection, calls `FontConfig.LoadFromSystem()`. | M |
| 3.9 | Font size TextBox with validation | `TextBox` bound to `FontConfigViewModel.FontSize` (as string, parsed to int). Validate on text changed: clamp to range 4-500. Display current value with "pt" label next to the TextBox. | S |
| 3.10 | Error handling for invalid fonts | Wrap `ReadFont()` in try/catch for `FontParsingException`. Display error in status bar via `StatusBar.SetError()`. Optionally show error text in the font config panel (red `Label`). | S |
| 3.11 | Load system font list on startup | In `KernSmithGame.Initialize()` (or on first panel show), call `FontDiscoveryService.GetSystemFonts()` on a background thread. Populate `FontConfigViewModel.SystemFonts` when complete. Show "Loading..." in ComboBox while loading. | S |

### FontConfigViewModel Properties

```csharp
using Gum.Mvvm;

namespace KernSmith.Ui.ViewModels;

public class FontConfigViewModel : ViewModel
{
    // --- Font source ---
    public string? FontFilePath { get => Get<string?>(); set => Set(value); }
    public byte[]? FontData { get => Get<byte[]?>(); set => Set(value); }
    public string FontSourceDescription { get => Get<string>(); set => Set(value); }
    public FontSourceKind FontSourceKind { get => Get<FontSourceKind>(); set => Set(value); }

    // --- Font metadata (populated after load) ---
    public string FamilyName { get => Get<string>(); set => Set(value); }
    public string StyleName { get => Get<string>(); set => Set(value); }
    public int NumGlyphs { get => Get<int>(); set => Set(value); }
    public bool HasColorGlyphs { get => Get<bool>(); set => Set(value); }
    public bool HasVariationAxes { get => Get<bool>(); set => Set(value); }
    public string VariationAxesSummary { get => Get<string>(); set => Set(value); }
    public bool IsFontLoaded { get => Get<bool>(); set => Set(value); }

    // --- Generation settings ---
    public int FontSize { get => Get<int>(); set => Set(value); }

    // --- System font list ---
    public IReadOnlyList<SystemFontGroup>? SystemFonts { get => Get<IReadOnlyList<SystemFontGroup>?>(); set => Set(value); }
    public string? SelectedFontFamily { get => Get<string?>(); set => Set(value); }
    public SystemFontInfo? SelectedSystemFont { get => Get<SystemFontInfo?>(); set => Set(value); }

    public FontConfigViewModel()
    {
        FontSourceDescription = "No font loaded";
        FamilyName = "";
        StyleName = "";
        VariationAxesSummary = "";
        FontSize = 32;
    }
}
```

### `FontSourceKind` Enum

```csharp
namespace KernSmith.Ui.Models;

public enum FontSourceKind
{
    None,
    File,
    System
}
```

### Font Config Panel Layout

```
+------------------------------------------+
| FONT SOURCE                              |
| [Open File...]                           |
| Currently: Roboto-Regular.ttf            |
|                                          |
| -- OR --                                 |
|                                          |
| System Font: [ComboBox: Arial         ]  |
|      Style:  [ComboBox: Regular       ]  |
+------------------------------------------+
| FONT INFO                                |
| Family:     Roboto                       |
| Style:      Regular                      |
| Glyphs:     1,294                        |
| Color:      No                           |
| Variable:   No                           |
+------------------------------------------+
| SIZE                                     |
| Font Size:  [ 32 ] pt                    |
+------------------------------------------+
```

### GUM Controls Used in FontConfigPanel

| UI Element | GUM Control | Binding Target |
|------------|-------------|----------------|
| "Open File..." button | `Button` | `Click` event -> `MainViewModel.OpenFont()` |
| Font source description | `Label` | `SetBinding("Text", "FontSourceDescription")` |
| System font family picker | `ComboBox` | Items populated from `SystemFonts`, selection -> `SelectedFontFamily` |
| System font style picker | `ComboBox` | Items filtered by selected family, selection -> `SelectedSystemFont` |
| Font info labels | `Label` (multiple) | `SetBinding("Text", "FamilyName")`, etc. |
| Font size input | `TextBox` | `SetBinding("Text", "FontSize")` with int parse |

### Font Loading Flow (Sequence)

```
User clicks "Open File..."
  -> Button.Click event fires
  -> MainViewModel.OpenFont()
  -> FileDialogService.OpenFontFile()
  -> NativeFileDialogSharp.Dialog.FileOpen("ttf,otf,woff,ttc")
  -> User picks "Roboto-Regular.ttf"
  -> FontConfigViewModel.LoadFromFile("C:/Fonts/Roboto-Regular.ttf")
    -> File.ReadAllBytes() -> byte[] fontData
    -> new TtfFontReader().ReadFont(fontData, faceIndex: 0) -> FontInfo
    // NOTE: TtfFontReader is internal. Depends on Phase 55 to expose a public API.
    // Actual signature: ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0)
    // byte[] implicitly converts to ReadOnlySpan<byte>, so this call works.
    -> Set FamilyName = fontInfo.FamilyName           // "Roboto"
    -> Set StyleName = fontInfo.StyleName              // "Regular"
    -> Set NumGlyphs = fontInfo.NumGlyphs              // 1294
    -> Set HasColorGlyphs = fontInfo.HasColorGlyphs    // false
    -> Set HasVariationAxes = fontInfo.VariationAxes != null
    -> Set FontData = fontData                          // stored for generation
    -> Set FontFilePath = path
    -> Set FontSourceDescription = "Roboto-Regular.ttf"
    -> Set FontSourceKind = FontSourceKind.File
    -> Set IsFontLoaded = true
  -> StatusBar.StatusText = "Loaded Roboto Regular (1,294 glyphs)"
```

### System Font Loading Flow

```
App startup (background thread):
  -> FontDiscoveryService.GetSystemFonts()
  -> DefaultSystemFontProvider().GetInstalledFonts()
  -> Returns IReadOnlyList<SystemFontInfo> with FamilyName, StyleName, FilePath, FaceIndex
  -> Group by FamilyName -> List<SystemFontGroup>
  -> FontConfigViewModel.SystemFonts = grouped list

User selects "Arial" from family ComboBox:
  -> FontConfigViewModel.SelectedFontFamily = "Arial"
  -> Style ComboBox populated with styles for "Arial"

User selects "Regular" from style ComboBox:
  -> FontConfigViewModel.SelectedSystemFont = SystemFontInfo { FamilyName="Arial", ... }
  -> FontConfigViewModel.LoadFromSystem(selectedSystemFont)
    -> File.ReadAllBytes(selectedSystemFont.FilePath) -> byte[] fontData
    -> ReadFont(fontData, selectedSystemFont.FaceIndex) -> FontInfo
    -> Populate metadata properties (same as file load)
    -> FontSourceKind = FontSourceKind.System
    -> FontSourceDescription = "Arial (System)"
```

---

## Wave 4: Basic Generation and Output

**Goal**: Wire the Generate button to `BmFont.Builder()`, display the atlas as a `Texture2D` in the preview panel via GUM `SpriteRuntime`, and enable Save As.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 4.1 | Create `GenerationService` | Wraps the `BmFont.Builder()` fluent API. Method: `Task<BmFontResult> GenerateAsync(GenerationRequest request)`. Runs `BmFontBuilder.Build()` on a background thread via `Task.Run()`. Returns `BmFontResult`. | M |
| 4.2 | Create `GenerationRequest` record | Record with properties: `byte[] FontData`, `string? FontFilePath`, `string? SystemFontFamily`, `FontSourceKind SourceKind`, `int FontSize`, `CharacterSet Characters`. Passed from ViewModel to `GenerationService`. | S |
| 4.3 | Create `PreviewViewModel` | Properties: `IReadOnlyList<PreviewPage> Pages`, `int SelectedPageIndex`, `PreviewPage? SelectedPage`, `bool HasResult`. `PreviewPage` holds `byte[] PngData` and dimensions. The layout class converts PNG bytes to `Texture2D` for display. | M |
| 4.4 | Implement `MainViewModel.GenerateAsync()` | Sets `StatusBar.SetGenerating()`, builds `GenerationRequest` from `FontConfig` properties, calls `GenerationService.GenerateAsync()`, updates `Preview` and `StatusBar` on completion. Catches `BmFontException` and `InvalidOperationException`, calls `StatusBar.SetError()`. Uses `Stopwatch` for timing. | M |
| 4.5 | Wire Generate button enable/disable | Generate button enabled when `FontConfig.IsFontLoaded && !StatusBar.IsGenerating`. Check these conditions before executing. Disable button visually by setting `IsEnabled` on the GUM `Button`. | S |
| 4.6 | Build `PreviewPanel.cs` layout | `ScrollViewer` containing a `SpriteRuntime` that displays the atlas `Texture2D`. When no result: show centered `Label` with "No atlas generated". When result: create `Texture2D` from PNG bytes via `Texture2D.FromStream()`, assign to `SpriteRuntime.Texture`. If multiple pages, add page selector buttons at the top. | M |
| 4.7 | Implement PNG to Texture2D conversion | Helper method: `Texture2D LoadTexture(GraphicsDevice device, byte[] pngData)` — wraps `Texture2D.FromStream(device, new MemoryStream(pngData))`. Called in the layout class (not ViewModel) since `Texture2D` is a MonoGame type. | S |
| 4.8 | Implement page navigation | If `PreviewViewModel.Pages.Count > 1`, show `Button` controls at the top of the preview panel: "< Page 1 of 3 >". Clicking left/right changes `SelectedPageIndex` and updates the displayed `Texture2D`. | S |
| 4.9 | Implement Save As | `MainViewModel.SaveAs()`: calls `FileDialogService.SaveFile("myfont", "fnt")`. Calls `BmFontResult.ToFile(outputPath)`. Updates status bar: "Saved to C:\output\myfont.fnt". | S |
| 4.10 | Wire Save As enable/disable | Save As enabled only when `PreviewViewModel.HasResult` is true. Disable the File > Save As menu item otherwise. | S |
| 4.11 | Add "Generate" button to `FontConfigPanel` | Large `Button` at the bottom of the font config panel. Styled with accent color (`Theme.Accent` background, white text). Disabled when font not loaded or generation in progress. Text changes to "Generating..." during generation. | S |
| 4.12 | Generation timing | Use `Stopwatch` in `GenerateAsync()` to measure wall-clock time. Pass elapsed to `StatusBar.SetComplete()`. | S |

### GenerationService Implementation

```csharp
namespace KernSmith.Ui.Services;

public class GenerationService
{
    public async Task<BmFontResult> GenerateAsync(GenerationRequest request)
    {
        return await Task.Run(() =>
        {
            var builder = BmFont.Builder()
                .WithSize(request.FontSize)
                .WithCharacters(request.Characters);

            // Set font source
            switch (request.SourceKind)
            {
                case FontSourceKind.File when request.FontFilePath != null:
                    builder.WithFont(request.FontFilePath);
                    break;
                case FontSourceKind.System when request.SystemFontFamily != null:
                    builder.WithSystemFont(request.SystemFontFamily);
                    break;
                default:
                    builder.WithFont(request.FontData!);
                    break;
            }

            return builder.Build();
        });
    }
}
```

### GenerateAsync Implementation

```csharp
public async Task GenerateAsync()
{
    if (!FontConfig.IsFontLoaded || StatusBar.IsGenerating)
        return;

    StatusBar.SetGenerating();

    var sw = Stopwatch.StartNew();

    try
    {
        var request = new GenerationRequest
        {
            FontData = FontConfig.FontData,
            FontFilePath = FontConfig.FontFilePath,
            SystemFontFamily = FontConfig.FontSourceKind == FontSourceKind.System
                ? FontConfig.FontSourceDescription?.Replace(" (System)", "")
                : null,
            SourceKind = FontConfig.FontSourceKind,
            FontSize = FontConfig.FontSize,
            Characters = FontConfig.GetCharacterSet()
        };

        _lastResult = await _generationService.GenerateAsync(request);

        sw.Stop();

        // Update preview
        Preview.LoadResult(_lastResult);

        // Update status bar from BmFontModel
        var common = _lastResult.Model.Common;
        StatusBar.SetComplete(
            pageCount: common.Pages,
            scaleW: common.ScaleW,
            scaleH: common.ScaleH,
            glyphCount: _lastResult.Model.Characters.Count,
            elapsed: sw.Elapsed);
    }
    catch (Exception ex) when (ex is BmFontException or InvalidOperationException)
    {
        sw.Stop();
        StatusBar.SetError(ex.Message);
    }
    catch (Exception ex)
    {
        sw.Stop();
        StatusBar.SetError($"Unexpected error: {ex.Message}");
    }
}
```

### PreviewViewModel.LoadResult Implementation

```csharp
public void LoadResult(BmFontResult result)
{
    var pages = new List<PreviewPage>();

    for (int i = 0; i < result.Pages.Count; i++)
    {
        var pngBytes = result.GetPngData(i);
        var page = result.Pages[i];

        pages.Add(new PreviewPage
        {
            PageIndex = i,
            PngData = pngBytes,
            Width = page.Width,
            Height = page.Height,
            Label = $"Page {i} ({page.Width}x{page.Height})"
        });
    }

    Pages = pages;
    SelectedPageIndex = 0;
    HasResult = true;
}
```

### PreviewPage Data Class

```csharp
namespace KernSmith.Ui.Models;

public class PreviewPage
{
    public int PageIndex { get; init; }
    public byte[] PngData { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public string Label { get; init; } = "";
}
```

### Atlas Display in PreviewPanel (GUM + MonoGame)

```csharp
// In PreviewPanel.cs — when atlas is generated, convert PNG to Texture2D and display

private SpriteRuntime _atlasSprite;
private Label _placeholder;

public void ShowAtlas(GraphicsDevice device, PreviewPage page)
{
    _placeholder.Visible = false;

    // Dispose previous texture to prevent memory leak
    _atlasSprite.Texture?.Dispose();

    using var stream = new MemoryStream(page.PngData);
    var texture = Texture2D.FromStream(device, stream);

    _atlasSprite.Texture = texture;
    _atlasSprite.Width = texture.Width;
    _atlasSprite.Height = texture.Height;
    _atlasSprite.Visible = true;
}

public void ShowPlaceholder()
{
    _atlasSprite.Visible = false;
    _placeholder.Visible = true;
}
```

### Save As Flow

```
User clicks File > Save As...
  -> MainViewModel.SaveAs()
  -> FileDialogService.SaveFile("myfont", "fnt")
  -> NativeFileDialogSharp.Dialog.FileSave("fnt")
  -> User picks "C:\output\myfont"
  -> _lastResult.ToFile("C:\output\myfont")
    -> Writes myfont.fnt (text format)
    -> Writes myfont_0.png (atlas page 0)
    -> Writes myfont_1.png (if multi-page)
    -> Writes myfont.bmfc (config file)
  -> StatusBar.StatusText = "Saved to C:\output\myfont.fnt"
```

---

## Wave 5: Minimal Character Set Selection

**Goal**: Let users pick a character set preset or enter custom characters.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 5.1 | Add character set properties to `FontConfigViewModel` | `CharacterSetPreset SelectedPreset`, `string CustomCharacters` (text input for custom mode), `int CharacterCount` (computed), `bool IsCustomMode` (true when preset is Custom). All using GUM `Get`/`Set` pattern. | S |
| 5.2 | Create `CharacterSetPreset` enum | Values: `Ascii`, `ExtendedAscii`, `Latin`, `Custom`. | S |
| 5.3 | Implement `GetCharacterSet()` method on `FontConfigViewModel` | Maps preset enum to `CharacterSet.Ascii`, `CharacterSet.ExtendedAscii`, `CharacterSet.Latin`. For `Custom`, uses `CharacterSet.FromChars(CustomCharacters)`. Returns the resolved `CharacterSet`. | S |
| 5.4 | Compute character count | When preset changes or custom text changes, recalculate: `CharacterCount = GetCharacterSet().Count`. Update is triggered by property setters calling a shared `UpdateCharacterCount()` method. | S |
| 5.5 | Add character set UI to `FontConfigPanel.cs` | Below the font size section. `ComboBox` for preset selection (items: "ASCII (95)", "Extended ASCII (224)", "Latin (559)", "Custom"). `TextBox` for custom characters (visible only when preset is Custom, via `Visible` property). `Label` showing character count: "Selected: 95 characters". | S |
| 5.6 | Wire ComboBox selection to preset | `ComboBox.SelectionChanged` event handler maps selected index to `CharacterSetPreset` enum value. Sets `FontConfigViewModel.SelectedPreset`. Shows/hides custom `TextBox` based on selection. | S |
| 5.7 | Wire custom TextBox to ViewModel | `TextBox.SetBinding("Text", "CustomCharacters")` on `FontConfigViewModel`. On text changed, calls `UpdateCharacterCount()`. | S |
| 5.8 | Wire character set into generation | `MainViewModel.GenerateAsync()` reads `FontConfig.GetCharacterSet()` and passes it in `GenerationRequest.Characters`. | S |
| 5.9 | Update status bar with character count | After generation, `StatusBar.GlyphCount` shows actual rendered glyph count from `result.Model.Characters.Count` (which may differ from requested count due to missing glyphs). | S |

### CharacterSetPreset Enum

```csharp
namespace KernSmith.Ui.Models;

public enum CharacterSetPreset
{
    Ascii,          // CharacterSet.Ascii -- 95 characters
    ExtendedAscii,  // CharacterSet.ExtendedAscii -- 224 characters
    Latin,          // CharacterSet.Latin -- 559 characters
    Custom          // CharacterSet.FromChars(user input)
}
```

### Character Set UI Layout (within FontConfigPanel)

```
+------------------------------------------+
| CHARACTER SET                            |
| Preset: [ComboBox: ASCII (95)         ]  |
|                                          |
| (when Custom is selected:)               |
| Characters:                              |
| [TextBox: ABCDEFGabcdefg0123456789]      |
|                                          |
| Selected: 95 characters                  |
+------------------------------------------+
```

### GetCharacterSet() Implementation

```csharp
public CharacterSet GetCharacterSet()
{
    return SelectedPreset switch
    {
        CharacterSetPreset.Ascii => CharacterSet.Ascii,
        CharacterSetPreset.ExtendedAscii => CharacterSet.ExtendedAscii,
        CharacterSetPreset.Latin => CharacterSet.Latin,
        CharacterSetPreset.Custom when !string.IsNullOrEmpty(CustomCharacters)
            => CharacterSet.FromChars(CustomCharacters),
        _ => CharacterSet.Ascii  // fallback
    };
}
```

### Character Count Update Logic

```csharp
// In FontConfigViewModel property setters:

public CharacterSetPreset SelectedPreset
{
    get => Get<CharacterSetPreset>();
    set
    {
        Set(value);
        IsCustomMode = value == CharacterSetPreset.Custom;
        UpdateCharacterCount();
    }
}

public string CustomCharacters
{
    get => Get<string>();
    set
    {
        Set(value);
        if (SelectedPreset == CharacterSetPreset.Custom)
            UpdateCharacterCount();
    }
}

private void UpdateCharacterCount()
{
    CharacterCount = GetCharacterSet().Count;
}
```

---

## Files Created in This Phase

| File | Purpose |
|------|---------|
| `apps/KernSmith.Ui/KernSmith.Ui.csproj` | MonoGame + GUM UI desktop app project file |
| `apps/KernSmith.Ui/Program.cs` | Application entry point (`new KernSmithGame().Run()`) |
| `apps/KernSmith.Ui/KernSmithGame.cs` | MonoGame `Game` subclass (Initialize, Update, Draw with GUM) |
| `apps/KernSmith.Ui/Layout/MainLayout.cs` | Root layout: menu bar, three-panel splitter, status bar |
| `apps/KernSmith.Ui/Layout/MenuBar.cs` | Top menu bar with File, Edit, View, Tools, Help |
| `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` | Left panel: font loading, size, character set controls |
| `apps/KernSmith.Ui/Layout/PreviewPanel.cs` | Center panel: atlas display via SpriteRuntime + Texture2D |
| `apps/KernSmith.Ui/Layout/EffectsPanel.cs` | Right panel: placeholder for future effects config |
| `apps/KernSmith.Ui/Layout/StatusBar.cs` | Bottom status bar with status text, dimensions, counts |
| `apps/KernSmith.Ui/ViewModels/MainViewModel.cs` | Root ViewModel, orchestrates child VMs and commands |
| `apps/KernSmith.Ui/ViewModels/FontConfigViewModel.cs` | Font loading, size, character set config |
| `apps/KernSmith.Ui/ViewModels/PreviewViewModel.cs` | Atlas page display state |
| `apps/KernSmith.Ui/ViewModels/StatusBarViewModel.cs` | Status bar text and progress state |
| `apps/KernSmith.Ui/Models/PreviewPage.cs` | Data class for a single preview page |
| `apps/KernSmith.Ui/Models/GenerationRequest.cs` | Request record for GenerationService |
| `apps/KernSmith.Ui/Models/SystemFontGroup.cs` | Grouped system font family |
| `apps/KernSmith.Ui/Models/FontSourceKind.cs` | Enum: None, File, System |
| `apps/KernSmith.Ui/Models/CharacterSetPreset.cs` | Enum: Ascii, ExtendedAscii, Latin, Custom |
| `apps/KernSmith.Ui/Services/GenerationService.cs` | Wraps `BmFont.Builder()` on background thread |
| `apps/KernSmith.Ui/Services/FontDiscoveryService.cs` | Wraps `DefaultSystemFontProvider` |
| `apps/KernSmith.Ui/Services/FileDialogService.cs` | Wraps `NativeFileDialogSharp` for open/save dialogs |
| `apps/KernSmith.Ui/Styling/Theme.cs` | Dark theme color constants |
| `apps/KernSmith.Ui/Content/Content.mgcb` | MonoGame content project (empty) |
| `apps/KernSmith.Ui/Assets/kernsmith-icon.ico` | Application icon |

---

## NuGet Dependencies (Final)

| Package | Version | Project | Purpose |
|---------|---------|---------|---------|
| `MonoGame.Framework.DesktopGL` | latest stable | KernSmith.Ui | Core game framework (cross-platform via DesktopGL) |
| `Gum.MonoGame` | latest stable | KernSmith.Ui | Code-only UI framework with Forms controls |
| `MonoGame.Extended` | latest stable | KernSmith.Ui | MonoGame utilities (screen management, input) |
| `NativeFileDialogSharp` | latest stable | KernSmith.Ui | Cross-platform native file open/save dialogs |

---

## KernSmith Core Library API Surface Used

This section documents every core library type, method, and property the UI will call. This serves as a compatibility contract -- if any of these change, the UI must be updated.

### Entry Point

| Call | Purpose |
|------|---------|
| `BmFont.Builder()` | Creates `BmFontBuilder` instance |

### BmFontBuilder Methods

| Method | Used For |
|--------|----------|
| `.WithFont(byte[] fontData)` | Load font from in-memory bytes |
| `.WithFont(string fontPath)` | Load font from file path |
| `.WithSystemFont(string familyName)` | Load system font by family name |
| `.WithSize(int size)` | Set font size in points |
| `.WithCharacters(CharacterSet characters)` | Set character set |
| `.Build()` | Execute pipeline, return `BmFontResult` |

### BmFontResult Properties and Methods

| Member | Used For |
|--------|----------|
| `.Model` | Access `BmFontModel` (font descriptor) |
| `.Pages` | Access `IReadOnlyList<AtlasPage>` |
| `.GetPngData(int pageIndex)` | Encode single atlas page as PNG bytes for preview |
| `.ToFile(string outputPath)` | Write .fnt + .png + .bmfc to disk |
| `.Model.Common.ScaleW` | Atlas width for status bar |
| `.Model.Common.ScaleH` | Atlas height for status bar |
| `.Model.Common.Pages` | Page count for status bar |
| `.Model.Characters.Count` | Glyph count for status bar |

### Font Parsing (for metadata display)

| Call | Purpose |
|------|---------|
| `new TtfFontReader().ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0)` | Parse font metadata before generation (`byte[]` implicitly converts to `ReadOnlySpan<byte>`). **Note:** `TtfFontReader` is internal; depends on Phase 55. |
| `FontInfo.FamilyName` | Display family name |
| `FontInfo.StyleName` | Display style name |
| `FontInfo.NumGlyphs` | Display glyph count |
| `FontInfo.HasColorGlyphs` | Show color font indicator |
| `FontInfo.VariationAxes` | Show variable font axes |

### System Font Discovery

| Call | Purpose |
|------|---------|
| `new DefaultSystemFontProvider().GetInstalledFonts()` | List installed system fonts |
| `SystemFontInfo.FamilyName` | Group and display family names |
| `SystemFontInfo.StyleName` | Display style variants |
| `SystemFontInfo.FilePath` | Load font bytes for generation |
| `SystemFontInfo.FaceIndex` | Pass to `ReadFont()` for TTC collections |

### Character Sets

| Call | Purpose |
|------|---------|
| `CharacterSet.Ascii` | ASCII preset (95 chars) |
| `CharacterSet.ExtendedAscii` | Extended ASCII preset (224 chars) |
| `CharacterSet.Latin` | Latin preset (559 chars) |
| `CharacterSet.FromChars(string)` | Custom character input |
| `CharacterSet.Count` | Display character count |

### Exception Types

| Exception | When |
|-----------|------|
| `BmFontException` | Base exception for all library errors |
| `FontParsingException` | Invalid or corrupt font file |
| `RasterizationException` | FreeType rasterization failure |
| `AtlasPackingException` | Glyphs do not fit in max texture size |

---

## Success Criteria

| Criterion | Verification |
|-----------|-------------|
| User can launch the app | `dotnet run --project apps/KernSmith.Ui` opens a MonoGame window titled "KernSmith" with dark background |
| User can load a TTF/OTF/WOFF file via native file dialog | Click "Open File...", native OS dialog appears, select font, see metadata in left panel |
| User can pick a system font from dropdowns | Select family from ComboBox, then style, see metadata appear |
| Font metadata is displayed after loading | Family name, style, glyph count, color/variable indicators shown in left panel |
| User can set font size | Type size in TextBox, value is stored for generation |
| User can pick a character set preset | Select "Extended ASCII" from dropdown, see "224 characters" |
| User can enter custom characters | Select "Custom", type characters, see count update |
| User can click Generate and see the atlas | Click Generate, see atlas texture rendered in center panel via SpriteRuntime |
| Status bar shows generation info | After Generate: page count, atlas dimensions, glyph count, time |
| User can save output to disk | File > Save As, native save dialog, verify .fnt + .png files written |
| Works on Windows | Primary development and testing platform |
| Launches on macOS and Linux | MonoGame DesktopGL cross-platform -- verify basic launch |
| Dark theme by default | Window uses dark background and panel colors from Theme.cs |
| Panels are resizable | GUM Splitters can be dragged to resize left/center/right |

---

## Known Core Library Gaps to Document

This section is for observations made during UI integration. Items listed here are NOT fixed in this phase -- they are logged for the core library team. See **Phase 55 (Core Library Prerequisites)** for the tracking document covering all library prerequisite work.

| # | Observation | Impact | Workaround |
|---|-------------|--------|------------|
| G1 | `TtfFontReader` is internal -- UI must use `new TtfFontReader()` directly, which works but is not part of the public API contract | Low -- works today, could break if internals change | Could add `BmFont.ReadFontInfo(byte[])` public static method |
| G2 | `DefaultSystemFontProvider` is public but not documented as a user-facing API | Low -- works today | Could add `BmFont.GetSystemFonts()` convenience method |
| G3 | No async generation API -- `BmFontBuilder.Build()` is synchronous | Medium -- UI wraps in `Task.Run()` which works but is not ideal | Could add `BuildAsync()` with `CancellationToken` support |
| G4 | No progress reporting during generation | Medium -- UI shows indeterminate progress (button text change) | Could add `IProgress<GenerationProgress>` parameter to `Build()` |
| G5 | `BmFontResult.ToFile()` does not return the list of files written | Low -- UI constructs the expected paths manually for status message | Could return `IReadOnlyList<string>` of written file paths |

---

## Estimated Effort Summary

| Wave | Description | Estimated Effort | Dependencies |
|------|-------------|-----------------|--------------|
| Wave 1 | Project scaffolding | Small (1-2 sessions) | None |
| Wave 2 | Main window layout | Medium (2-3 sessions) | Wave 1 |
| Wave 3 | Font loading | Medium (2-3 sessions) | Wave 2 |
| Wave 4 | Generation and output | Medium (2-3 sessions) | Wave 3 |
| Wave 5 | Character set selection | Small (1 session) | Wave 3 |
| **Total** | | **8-12 sessions** | |

Waves 4 and 5 can be developed in parallel after Wave 3 is complete.

---

## Out of Scope (Future Phases)

The following features are explicitly NOT in the Phase 60 MVP. They are listed here to set expectations and prevent scope creep.

| Feature | Future Phase |
|---------|-------------|
| Effects configuration (outline, shadow, gradient) | Phase 62 |
| Advanced settings (padding, spacing, packing algorithm, SDF, etc.) | Phase 63 |
| Variable font axis sliders | Phase 66 |
| Color font / palette selection | Phase 66 |
| Real-time preview (re-generate on setting change) | Phase 64 |
| Atlas zoom and pan controls | Phase 64 |
| Glyph inspector (click glyph to see metrics) | Phase 64 |
| Multiple font merge | Phase 66 |
| Project save/load (.bmfc import/export) | Phase 65 |
| Undo/redo | Phase 67 |
| Keyboard shortcuts | Phase 67 |
| Theme switching (light/dark) | Phase 67 |
| Batch generation UI | Phase 66 |
| Auto-update / version check | Phase 68 |
| Drag-and-drop font loading | Phase 61 |
