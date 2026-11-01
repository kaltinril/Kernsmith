# KernSmith.Ui

Cross-platform bitmap font generation UI built with MonoGame and GUM. Provides a visual interface for the KernSmith core library, allowing users to configure fonts, effects, atlas settings, and preview generated bitmap fonts in real time.

## Architecture

The application follows the MVVM pattern using GUM's built-in `ViewModel` base class:

| Layer | Folder | Role |
|-------|--------|------|
| **ViewModels** | `ViewModels/` | Observable state and commands — `MainViewModel` orchestrates child VMs (`FontConfigViewModel`, `AtlasConfigViewModel`, `EffectsViewModel`, `PreviewViewModel`, `CharacterGridViewModel`, `StatusBarViewModel`) |
| **Layout** | `Layout/` | GUM UI panels and dialogs — bind to ViewModels via `PropertyChanged` and `SetBinding` |
| **Services** | `Services/` | Non-UI logic — font discovery, bitmap font generation, project save/load, session persistence, file dialogs |
| **Models** | `Models/` | Data transfer objects — `GenerationRequest`, `PreviewPage`, `UnicodeBlock`, `SystemFontGroup`, enums |
| **Styling** | `Styling/` | `Theme` color constants for the dark IDE-inspired palette |

### Key Classes

- **`KernSmithGame`** — MonoGame `Game` subclass. Initializes GUM, creates services and the root `MainViewModel`, handles keyboard shortcuts, drag-and-drop, UI scaling, and the game loop.
- **`MainViewModel`** — Central orchestrator. Owns all child ViewModels, wires auto-regeneration, manages project save/load, and coordinates font loading with generation.
- **`MainLayout`** — Root GUM container. Builds the three-column layout (font config | preview | effects) with splitters, the menu bar, and the status bar.
- **`GenerationService`** — Translates a `GenerationRequest` into a `BmFont.Builder()` call chain and runs generation on a background thread.

## Build and Run

```bash
dotnet build apps/KernSmith.Ui
dotnet run --project apps/KernSmith.Ui
```

## Features

- Font file loading and system font selection — browse or drag-and-drop TTF, OTF, TTC, WOFF and WOFF2 files, with a face selector for `.ttc` collections
- Font Inspector (Help menu) showing the loaded font's metadata
- Bold, italic and force-synthetic bold/italic, hinting, super sampling and a kerning toggle
- Real-time bitmap font preview, with a sample-text tab and optional auto-regenerate on change
- Full effects configuration (outline with shape selector and Effect/Vector mode, fill color, gradient, shadow, hard shadow)
- Anti-alias mode (None/Grayscale/Light/LCD, with LCD filter and subpixel order), distance field (SDF/MSDF/MTSDF), color palette, variable-font axis controls, synthetic width and italic angle, where the selected backend supports them
- Channel packing and a configurable fallback character (per-channel content routing is preserved from a loaded `.bmfc`, but there is no editor for it yet)
- Advanced effect and rendering options — fill color, two-parameter shadow blur (kernel size + passes), extended gradient (offset, scale, cyclic), gamma correction, SDF spread and scale, and per-glyph horizontal advance adjustment
- Atlas packing and texture settings
- Character set selection with Unicode block browser
- Rasterizer backend selection — choose between FreeType, GDI, DirectWrite, StbTrueType, or the experimental Native backend (referenced from source; not on NuGet yet) from a dropdown. Available backends depend on the current platform. The anti-alias mode and distance-field mode (SDF, plus MSDF/MTSDF on Native) combos list only what the selected backend supports. See [TESTING-NATIVE.md](../../TESTING-NATIVE.md).
- `--ui-rasterizer <backend>` renders the app's own interface text with that backend, through the Gum integration's `KernSmithFontCreator` (e.g. `dotnet run --project apps/KernSmith.Ui -f net10.0-windows -- --ui-rasterizer native`).
- Export As — writes the generated `.fnt` descriptor and its atlas pages
- Project save/load and session persistence — load and save `.bmfc` (BMFont) and `.hiero` (libGDX) project files, including drag-and-drop (`.hiero` saving warns when settings can't be represented)

## Dependencies

- **MonoGame.Framework.DesktopGL** — rendering, input, windowing
- **Gum.MonoGame** — UI framework (panels, controls, MVVM bindings, forms)
- **Gum.Themes.Editor.MonoGame** — editor visual theme for the Gum controls
- **NativeFileDialogNET** — native open/save file dialogs
- **KernSmith** (project reference) — core bitmap font generation library
- **KernSmith.Rasterizers.FreeType**, **KernSmith.Rasterizers.StbTrueType** and the experimental **KernSmith.Rasterizers.Native** (project references) — rasterizer backends
- **KernSmith.Rasterizers.Gdi** and **KernSmith.Rasterizers.DirectWrite.TerraFX** (project references, `net10.0-windows` only) — Windows rasterizer backends
