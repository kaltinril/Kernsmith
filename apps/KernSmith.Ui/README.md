# KernSmith.Ui

Cross-platform bitmap font generation UI built with MonoGame and GUM. Provides a visual interface for the KernSmith core library, allowing users to configure fonts, effects, atlas settings, and preview generated bitmap fonts in real time.

## Architecture

The application follows the MVVM pattern using GUM's built-in `ViewModel` base class:

| Layer | Folder | Role |
|-------|--------|------|
| **ViewModels** | `ViewModels/` | Observable state and commands — `MainViewModel` orchestrates child VMs (`FontConfigViewModel`, `AtlasConfigViewModel`, `EffectsViewModel`, `PreviewViewModel`, `CharacterGridViewModel`, `StatusBarViewModel`) |
| **Layout** | `Layout/` | GUM UI panels and dialogs — bind to ViewModels via `PropertyChanged` and `SetBinding` |
| **Services** | `Services/` | Non-UI logic — font discovery, bitmap font generation, project save/load, session persistence, file dialogs |
| **Models** | `Models/` | Data transfer objects — `GenerationRequest`, `EnginePreset`, `PreviewPage`, `UnicodeBlock`, enums |
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

## Dependencies

- **MonoGame.Framework.DesktopGL** — rendering, input, windowing
- **Gum.MonoGame** — UI framework (panels, controls, MVVM bindings, forms)
- **KernSmith** (project reference) — core bitmap font generation library
