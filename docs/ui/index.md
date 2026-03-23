# UI Application

KernSmith.Ui is a cross-platform bitmap font generation UI built with MonoGame and GUM. It provides a visual interface for configuring fonts, effects, atlas settings, and previewing generated bitmap fonts in real time.

## Build and Run

```bash
dotnet build apps/KernSmith.Ui
dotnet run --project apps/KernSmith.Ui
```

## Architecture

The application follows the MVVM pattern using GUM's built-in `ViewModel` base class.

| Layer | Folder | Role |
|-------|--------|------|
| **ViewModels** | `ViewModels/` | Observable state and commands |
| **Layout** | `Layout/` | GUM UI panels and dialogs bound to ViewModels |
| **Services** | `Services/` | Font discovery, generation, project save/load, session persistence |
| **Models** | `Models/` | Data transfer objects for generation requests, presets, preview pages |
| **Styling** | `Styling/` | Dark IDE-inspired color theme |

## Key Classes

- **KernSmithGame** -- MonoGame `Game` subclass. Initializes GUM, creates services, handles keyboard shortcuts, drag-and-drop, UI scaling, and the game loop.
- **MainViewModel** -- Central orchestrator. Owns all child ViewModels (`FontConfigViewModel`, `AtlasConfigViewModel`, `EffectsViewModel`, `PreviewViewModel`, `CharacterGridViewModel`, `StatusBarViewModel`), wires auto-regeneration, and manages project save/load.
- **MainLayout** -- Root GUM container. Three-column layout (font config, preview, effects) with splitters, menu bar, and status bar.
- **GenerationService** -- Translates a `GenerationRequest` into a <xref:KernSmith.BmFont>.Builder() call chain and runs generation on a background thread.

## Dependencies

- **MonoGame.Framework.DesktopGL** -- rendering, input, windowing
- **Gum.MonoGame** -- UI framework (panels, controls, MVVM bindings, forms)
- **KernSmith** (project reference) -- core bitmap font generation library
