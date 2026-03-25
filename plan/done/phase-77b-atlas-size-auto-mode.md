# Phase 77B — Force Size Checkbox & Remove Engine Presets

> **Status**: Complete
> **Created**: 2026-03-23
> **Updated**: 2026-03-24
> **Goal**: Replace engine presets with a "Force Size" checkbox. Default is autofit (no size UI). When checked, Width/Height dropdowns appear.

---

## Problem

1. Engine presets (Unity, Godot, MonoGame, Unreal, Phaser) set MaxWidth/MaxHeight values, but `AutofitTexture=true` in every preset causes the estimator to ignore those values and pick the smallest size that fits.
2. When a user manually sets atlas width/height, they expect that exact size — not an auto-calculated smaller one.
3. The presets provide minimal differentiated value (padding/spacing differences are trivial, descriptor format is a separate concern, and the size values are misleading).

## Design

### Force Size Checkbox

Replace engine presets + `AutofitTexture` boolean with a single **"Force Size"** checkbox:

- **Unchecked** (default): Autofit — the engine picks the smallest atlas that fits all glyphs. Width/Height dropdowns are hidden. This is the current `AutofitTexture=true` behavior.
- **Checked**: User sets explicit Width and Height via dropdowns. The atlas uses exactly that size. If glyphs don't fit, they spill to multiple pages (existing packer behavior).

This is simpler than the original Auto/Manual enum plan — no new enum, just inverting the existing `AutofitTexture` boolean with better UX.

### UI Changes

**Remove from FontConfigPanel:**
- The entire engine preset button row (MonoGame, Unity, Godot, Unreal, Phaser buttons)
- Preset description label
- `EnginePreset` model and `EnginePresets.All` static list
- `SelectedPresetName` tracking in `AtlasConfigViewModel`
- `ApplyPreset()` method

**Modify in FontConfigPanel atlas config area:**
- Replace "Autofit Texture" checkbox with **"Force Size"** checkbox (inverted logic)
- Width/Height ComboBox dropdowns: **only visible when Force Size is checked**
- Power of Two checkbox: visible always (applies to both autofit and forced)

**Keep as-is:**
- Padding (Up/Right/Down/Left)
- Spacing (H/V)
- Include Kerning checkbox
- Packing Algorithm dropdown

### ViewModel Changes

**AtlasConfigViewModel:**
- Remove `SelectedPresetName` property
- Remove `ApplyPreset()` method
- Keep `AutofitTexture` property (existing, works as-is)
- Keep `MaxWidth`, `MaxHeight`, `PowerOfTwo` properties
- No new enum or mode property needed

### Core Library Changes

None. `FontGeneratorOptions.AutofitTexture`, `MaxTextureWidth`, `MaxTextureHeight` remain unchanged.

### Data Flow

```
UI (Force Size unchecked) → AutofitTexture=true,  MaxWidth=4096, MaxHeight=4096
UI (Force Size checked)   → AutofitTexture=false, MaxWidth=<user>, MaxHeight=<user>
                          ↓
GenerationRequest         → same fields (no changes needed)
                          ↓
BmFontBuilder             → .WithMaxTextureSize(w, h).WithAutofitTexture(auto).WithPowerOfTwo(pot)
                          ↓
BmFont.GenerateCore()     → existing sizing logic handles both paths
```

### Project Save/Load

**ProjectService mapping:**
- Force Size unchecked: save `AutofitTexture=true`, persist 4096/4096 as ceiling
- Force Size checked: save `AutofitTexture=false`, persist user's width/height
- Load: if `AutofitTexture=true` → Force Size unchecked; if `false` → checked with saved width/height
- Backward compat: existing projects already have `AutofitTexture` field — no migration needed

## Implementation Steps

### Step 1: Remove Engine Presets
- Delete `EnginePreset.cs` file
- Remove preset button row and description label from `FontConfigPanel.cs`
- Remove `SelectedPresetName` from `AtlasConfigViewModel`
- Remove `ApplyPreset()` method from `AtlasConfigViewModel`

### Step 2: Replace Autofit Checkbox with Force Size
- Rename/replace "Autofit Texture" checkbox with "Force Size" checkbox (inverted: Force Size checked = AutofitTexture false)
- Wire checkbox: checked → `_atlasConfig.AutofitTexture = false`, unchecked → `_atlasConfig.AutofitTexture = true`

### Step 3: Conditional Width/Height Visibility
- Width/Height ComboBox row: set `Visible = false` by default (autofit is default)
- When Force Size checked → show Width/Height row
- When Force Size unchecked → hide Width/Height row, set MaxWidth/MaxHeight to 4096

### Step 4: Update MainViewModel GenerationRequest
- When `AutofitTexture = true`: pass 4096/4096 as MaxWidth/MaxHeight ceiling
- When `AutofitTexture = false`: pass the user's selected MaxWidth/MaxHeight
- (This may already be correct — verify)

## Key Source Files

| What | Location |
|------|----------|
| Engine preset model (DELETE) | `apps/KernSmith.Ui/Models/EnginePreset.cs` |
| Preset buttons in UI (REMOVE) | `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` |
| Atlas config view model (MODIFY) | `apps/KernSmith.Ui/ViewModels/AtlasConfigViewModel.cs` |
| Main view model (VERIFY) | `apps/KernSmith.Ui/ViewModels/MainViewModel.cs` |
| Project service (VERIFY) | `apps/KernSmith.Ui/Services/ProjectService.cs` |
| Core options (NO CHANGE) | `src/KernSmith/Config/FontGeneratorOptions.cs` |

## Scope

- **UI-only change** — no core library API changes, no breaking changes for CLI or programmatic users
- **Risk**: Low — removes code, simplifies UI, existing atlas logic handles both paths
- **Files changed**: ~3 UI files
- **Files deleted**: 1 (`EnginePreset.cs`)
