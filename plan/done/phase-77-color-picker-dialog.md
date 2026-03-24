# Phase 77 — Color Picker Dialog

> **Status**: Complete
> **Created**: 2026-03-22
> **Goal**: Build a reusable color picker dialog that opens when clicking a color swatch.

---

## Design

Clicking any color swatch in the effects panel (outline, shadow, gradient) opens a modal dialog with:

### Input Modes
- **Visual picker** — saturation/lightness square + hue bar (standard HSV picker)
- **Hex** — text input for `#RRGGBB`
- **RGB** — three sliders or numeric inputs (0-255)
- **HSL** — three sliders: Hue (0-360), Saturation (0-100%), Lightness (0-100%)
- **HSV/HSB** — three sliders: Hue (0-360), Saturation (0-100%), Value/Brightness (0-100%)

### UI Layout
- Large color preview swatch (current vs previous color)
- Visual picker area (main interaction)
- Mode tabs or grouped inputs below
- OK / Cancel buttons
- All inputs sync bidirectionally (change hex → updates RGB/HSL/visual, etc.)

### Integration
- `AddColorRow` in EffectsPanel attaches a click handler to the swatch
- Dialog returns the selected color on OK, reverts on Cancel
- Reusable `ColorPickerDialog` class for any future color input needs

## Completed

- [x] Visual HSV picker (SV square + hue bar with click+drag)
- [x] Hex, RGB, HSL, HSV text inputs with bidirectional sync
- [x] Compact Grid layout (inputs beside picker)
- [x] Title bar, non-resizable modal (ResizeMode.NoResize)
- [x] MaxLettersToShow/MaxLength on value textboxes
- [x] Integration with EffectsPanel AddColorRow click handler
- [x] Fixed regeneration using stale color values (debounced Effects PropertyChanged)

## Remaining Work

- [ ] ~~Fix window background transparency — default Gum Window uses semi-transparent nineslice, causing background bleed. Need solid opaque background behind dialog content (similar to KeyboardShortcutsDialog pattern).~~ **Deferred** — cosmetic only, not blocking.

## Key Source Files

| What | Location |
|------|----------|
| Color picker dialog | `apps/KernSmith.Ui/Layout/ColorPickerDialog.cs` |
| Color row helper | `apps/KernSmith.Ui/Layout/EffectsPanel.cs` |
| Dialog base pattern | `apps/KernSmith.Ui/Layout/FontInspectorDialog.cs` |
| Theme colors | `apps/KernSmith.Ui/Styling/Theme.cs` |
