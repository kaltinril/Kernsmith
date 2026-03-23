# Phase 77 — Color Picker Dialog

> **Status**: Planning
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

## Key Source Files

| What | Location |
|------|----------|
| Color row helper | `apps/KernSmith.Ui/Layout/EffectsPanel.cs` |
| Dialog base pattern | `apps/KernSmith.Ui/Layout/FontInspectorDialog.cs` |
| Theme colors | `apps/KernSmith.Ui/Styling/Theme.cs` |
