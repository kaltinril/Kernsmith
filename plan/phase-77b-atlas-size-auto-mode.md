# Phase 77B — Atlas Size Auto Mode & Remove Engine Presets

> **Status**: Planning
> **Created**: 2026-03-23
> **Goal**: Replace engine presets with Auto/Manual atlas sizing so the user's explicit size is respected and auto-sizing is the clear default.

---

## Problem

1. Engine presets (Unity, Godot, MonoGame, Unreal, Phaser) set MaxWidth/MaxHeight values, but `AutofitTexture=true` in every preset causes the estimator to ignore those values and pick the smallest size that fits.
2. When a user manually sets atlas width/height, they expect that exact size — not an auto-calculated smaller one.
3. The presets provide minimal differentiated value (padding/spacing differences are trivial, descriptor format is a separate concern, and the size values are misleading).

## Design

### Atlas Size Mode

Replace the current `AutofitTexture` boolean + `MaxWidth`/`MaxHeight` pair with a clearer mental model:

- **Auto** (default): The engine picks the smallest atlas that fits all glyphs. PowerOfTwo toggle still applies. This is the current `AutofitTexture=true` behavior, but without a misleading max size shown.
- **Manual**: The user sets explicit Width and Height. The atlas uses exactly that size. If glyphs don't fit, they spill to multiple pages (existing packer behavior).

### UI Changes

**Remove from FontConfigPanel:**
- The entire engine preset button row (MonoGame, Unity, Godot, Unreal, Phaser buttons)
- `EnginePreset` model and `EnginePresets.All` static list
- `SelectedPresetName` tracking in `AtlasConfigViewModel`
- `ApplyPreset()` method

**Add/modify in atlas config area (likely in EffectsPanel or a new AtlasConfigPanel section):**
- **Size Mode** toggle: Auto / Manual (two radio-style buttons or a dropdown)
- **Width** and **Height** numeric inputs — visible and editable only in Manual mode; hidden or greyed out in Auto mode
- **Power of Two** checkbox — visible in both modes

**Keep as-is:**
- Padding (Up/Right/Down/Left)
- Spacing (H/V)
- Descriptor Format dropdown (Text/Xml/Binary)
- Include Kerning checkbox

### Core Library Changes

**FontGeneratorOptions:**
- Keep `AutofitTexture` as the underlying mechanism (no breaking API change)
- When Auto: `AutofitTexture = true`, `MaxTextureWidth`/`MaxTextureHeight` set to a sensible ceiling (e.g., 4096×4096) so the estimator has room
- When Manual: `AutofitTexture = false`, `MaxTextureWidth`/`MaxTextureHeight` set to the user's exact values

**BmFont.GenerateCore() sizing logic:**
- No changes needed — the existing `AutofitTexture` code path already handles both cases correctly
- When `AutofitTexture = false`, the estimator still runs but is clamped to `MaxWidth`/`MaxHeight`, and the packer uses those dimensions directly

**BmFontBuilder:**
- No API changes needed — `WithMaxTextureSize()`, `WithAutofitTexture()`, `WithPowerOfTwo()` remain

### Data Flow

```
UI (Auto mode)    → AutofitTexture=true,  MaxWidth=4096, MaxHeight=4096
UI (Manual mode)  → AutofitTexture=false, MaxWidth=<user>, MaxHeight=<user>
                  ↓
GenerationRequest → same fields
                  ↓
BmFontBuilder     → .WithMaxTextureSize(w, h).WithAutofitTexture(auto).WithPowerOfTwo(pot)
                  ↓
BmFont.GenerateCore() → existing sizing logic handles both paths
```

### Project Save/Load

**ProjectService mapping:**
- Auto mode: save `AutofitTexture=true`, don't persist width/height (or persist as 4096/4096 sentinel)
- Manual mode: save `AutofitTexture=false`, persist user's width/height
- Load: if `AutofitTexture=true` → Auto mode; if `false` → Manual mode with saved width/height

## Implementation Steps

### Step 1: Remove Engine Presets from UI
- Delete preset button row from `FontConfigPanel.cs`
- Remove `EnginePreset.cs` and `EnginePresets` static class
- Remove `SelectedPresetName` from `AtlasConfigViewModel`
- Remove `ApplyPreset()` method
- Keep descriptor format dropdown (move if needed)

### Step 2: Add Size Mode to AtlasConfigViewModel
- Add `AtlasSizeMode` enum: `Auto`, `Manual`
- Add `SizeMode` property (default: `Auto`)
- Keep `MaxWidth`, `MaxHeight`, `PowerOfTwo` properties
- When `SizeMode` is `Auto`, `MaxWidth`/`MaxHeight` are informational only (show "Auto" in UI)

### Step 3: Update Atlas Config UI
- Add Auto/Manual toggle in the atlas config section
- Show Width/Height inputs only when Manual is selected
- PowerOfTwo checkbox visible in both modes

### Step 4: Update GenerationService
- Map `SizeMode.Auto` → `AutofitTexture=true`, large ceiling for max dimensions
- Map `SizeMode.Manual` → `AutofitTexture=false`, user's exact dimensions

### Step 5: Update ProjectService Save/Load
- Serialize/deserialize the new `SizeMode` field
- Backward compat: if loading old project without `SizeMode`, infer from `AutofitTexture`

### Step 6: Update GenerationRequest
- Add `AtlasSizeMode` field (or keep mapping through `AutofitTexture` + dimensions)

## Key Source Files

| What | Location |
|------|----------|
| Engine preset model (DELETE) | `apps/KernSmith.Ui/Models/EnginePreset.cs` |
| Preset buttons in UI (REMOVE) | `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` |
| Atlas config view model (MODIFY) | `apps/KernSmith.Ui/ViewModels/AtlasConfigViewModel.cs` |
| Generation request (MODIFY) | `apps/KernSmith.Ui/Models/GenerationRequest.cs` |
| Generation service (MODIFY) | `apps/KernSmith.Ui/Services/GenerationService.cs` |
| Project service (MODIFY) | `apps/KernSmith.Ui/Services/ProjectService.cs` |
| Core options (NO CHANGE) | `src/KernSmith/Config/FontGeneratorOptions.cs` |
| Atlas estimator (NO CHANGE) | `src/KernSmith/Atlas/AtlasSizeEstimator.cs` |
| BmFont generator (NO CHANGE) | `src/KernSmith/BmFont.cs` |
| Builder API (NO CHANGE) | `src/KernSmith/BmFontBuilder.cs` |

## Scope

- **UI-only change** — no core library API changes, no breaking changes for CLI or programmatic users
- **Risk**: Low — removes code, simplifies UI, existing atlas logic handles both paths
- **Files changed**: ~6 UI files
- **Files deleted**: 1 (`EnginePreset.cs`)
