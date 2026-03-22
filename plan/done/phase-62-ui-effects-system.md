# Phase 62 — Effects System UI

> **Status**: Complete
> **Completed**: 2026-03-22. Implemented as collapsible CheckBox sections with sliders and RGB TextBox inputs. Custom color picker (spectrum+HSV), ShadowOffsetPad, and GradientDirectionControl deferred — RGB TextBox inputs are sufficient for MVP. Channel per-channel config simplified to packing toggle.
> **Created**: 2026-03-21
> **Goal**: Build the complete effects configuration UI including outline, shadow, gradient with interactive controls, inspired by Hiero's stackable effects and SnowB BMF's gradient editor.

---

## Overview

The effects panel occupies the right side of the three-panel layout established in Phase 60. It is the primary surface for configuring all visual effects that KernSmith applies during bitmap font generation: outline, shadow, gradient, font styling, channel packing, and advanced rendering options.

Every control in this panel maps directly to one or more `BmFontBuilder` API calls. The panel does not trigger generation on its own — it populates a shared view model that the "Generate" action reads when the user initiates a build.

### Framework

- **MonoGame (DesktopGL)** — rendering, input handling, SpriteBatch drawing for custom controls
- **GUM UI (code-only)** — layout, standard controls (Button, CheckBox, ComboBox, Label, Slider, StackPanel, ScrollViewer, TextBox, RadioButton)
- **MonoGame.Extended** — utility types, shape drawing helpers
- **No XAML** — all UI is constructed in C# code using GUM's code-only API
- **No Avalonia** — no Avalonia controls, converters, or bindings

### GUM Control Availability

GUM provides: Button, CheckBox, ComboBox, Label, Slider, StackPanel, ScrollViewer, TextBox, RadioButton.

GUM does **NOT** provide: ColorPicker, TabControl, PropertyGrid, Expander, ToggleSwitch, NumericUpDown. These must be custom-built from GUM primitives and MonoGame rendering.

### Custom Controls Required

| Control | Built From | Purpose |
|---------|-----------|---------|
| `CollapsibleSection` | GUM Button (header) + StackPanel (body) + CheckBox (toggle) | Expandable section with enable/disable. Replaces Avalonia `Expander` + `ToggleSwitch`. |
| `NumericInput` | GUM TextBox + two GUM Buttons (+/-) | Integer/float input with increment/decrement. Replaces Avalonia `NumericUpDown`. |
| `ColorPickerControl` | MonoGame SpriteBatch (spectrum), GUM Sliders (R/G/B/H/S/V), GUM TextBox (hex) | Full color picker. GUM has no native color picker. |
| `GradientDirectionControl` | MonoGame SpriteBatch (circle, line, handle), pointer input | Circular compass for setting gradient angle via click+drag. |
| `ShadowOffsetPad` | MonoGame SpriteBatch (grid, crosshair, dot), pointer input | 2D pad for setting shadow X/Y offset via click+drag. |
| `SliderWithNumeric` | GUM Slider + `NumericInput` | Paired slider and numeric input bound to same value. Common pattern used throughout. |

### Design Principles

1. **Collapsible sections** — Each effect category lives in a `CollapsibleSection` with a `CheckBox` toggle in its header. Disabled sections are visually muted (reduced opacity) and their controls are non-interactive.
2. **Progressive disclosure** — The most common settings (font style, outline, shadow) are at the top. Advanced options (channels, SDF, color fonts) are at the bottom.
3. **Direct API mapping** — Every control writes to a single well-defined property on the effects view model. No intermediate translation layers.
4. **Immediate feedback** — Slider changes, color picks, and toggle flips update the view model instantly. The preview panel (Phase 64) can optionally react to changes for live preview, but generation itself requires an explicit action.
5. **Reset capability** — Each section has a section-level reset, and a global "Reset All Effects" button restores all defaults.

### Visual Stack (Top to Bottom)

```
+--------------------------------------+
| Font Style           [v] [expanded]  |
|   Bold / Italic / AA / Hinting ...   |
+--------------------------------------+
| Outline              [v] [collapsed] |
|   Width / Color ...                  |
+--------------------------------------+
| Shadow               [v] [collapsed] |
|   Offset / Blur / Color / Opacity .. |
+--------------------------------------+
| Gradient             [v] [collapsed] |
|   Colors / Angle / Midpoint ...      |
+--------------------------------------+
| Channels             [v] [collapsed] |
|   Per-channel config / Presets ...    |
+--------------------------------------+
| Advanced Rendering   [v] [collapsed] |
|   SDF / Color Font / Equalize ...    |
+--------------------------------------+
| [ Reset All Effects ]                |
+--------------------------------------+
```

### Panel Structure (GUM + MonoGame)

The effects panel is a GUM `ScrollViewer` containing a vertical `StackPanel` of `CollapsibleSection` controls. Each `CollapsibleSection` has a header row with a `CheckBox` (enable/disable), a `Label` (section name), and an optional "Reset" `Button`. The body contains the section's controls arranged in nested `StackPanel` containers.

```csharp
var effectsScroll = new ScrollViewer();
var effectsStack = new StackPanel { Orientation = Orientation.Vertical };
effectsScroll.InnerPanel.AddChild(effectsStack);

// Each section is a CollapsibleSection with CheckBox toggle in header
effectsStack.AddChild(fontStyleSection);
effectsStack.AddChild(outlineSection);
effectsStack.AddChild(shadowSection);
effectsStack.AddChild(gradientSection);
effectsStack.AddChild(channelsSection);
effectsStack.AddChild(advancedSection);
effectsStack.AddChild(resetAllButton);
```

---

## Wave 1 — Effects Panel Layout and Infrastructure

Establish the panel container, collapsible section controls, toggle infrastructure, the `CollapsibleSection` custom control, the `NumericInput` custom control, and the shared view model that bridges UI controls to `BmFontBuilder` calls.

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 1.1 | Create `EffectsViewModel` | Root view model for the entire effects panel. Contains child view models for each section. Uses GUM's ViewModel base with `Get<T>`/`Set<T>` + `SetBinding()`. All properties initialize to KernSmith defaults. | N/A (ViewModel) | All `BmFontBuilder.With*()` methods |
| 1.2 | Create `CollapsibleSection` custom control | Reusable composite control built from GUM primitives. Header row: a `CheckBox` (enable/disable toggle), a `Label` (section name), and a small `Button` ("Reset"). Body: a `StackPanel` that shows/hides when the header is clicked. When the `CheckBox` is unchecked, body controls are dimmed (alpha=0.4 via `ColoredRectangle` overlay or per-control alpha) and input is disabled. Toggle the body visibility by clicking the section label or a collapse chevron `Label` (text "v" / ">"). | GUM `CheckBox`, `Label`, `Button`, `StackPanel`, `ColoredRectangle` | Per-section enable flags |
| 1.3 | Create `NumericInput` custom control | Reusable control replacing Avalonia `NumericUpDown`. Layout: a `TextBox` flanked by "-" and "+" `Button` controls in a horizontal `StackPanel`. Properties: `Min`, `Max`, `Value`, `Increment`, `DecimalPlaces`. Text input validated on focus loss — rejects non-numeric text, clamps to range. Buttons increment/decrement. Fires `ValueChanged` event. | GUM `TextBox`, `Button` x2, `StackPanel` | Various numeric properties |
| 1.4 | Create `SliderWithNumeric` composite | Reusable pairing of a GUM `Slider` and a `NumericInput`, both bound to the same view model property. Slider fills available width. `NumericInput` has fixed width (70px). Arranged in a horizontal `StackPanel`. | GUM `Slider`, `NumericInput` (custom) | Various slider+numeric properties |
| 1.5 | Create `EffectsPanel` container | Top-level container for the right panel. Contains a GUM `ScrollViewer` with a vertical `StackPanel` holding all section instances. Constructed in code, added to the right column of the three-panel layout from Phase 60. Sets `DataContext`-equivalent binding to `EffectsViewModel`. | GUM `ScrollViewer`, `StackPanel` | N/A |
| 1.6 | Add Font Style section | First `CollapsibleSection` in the stack. Default: expanded and enabled (font style is always relevant). Header text: "Font Style". The enable `CheckBox` is hidden for this section since font style is always active. | `CollapsibleSection` | `WithBold`, `WithItalic`, `WithAntiAlias`, `WithHinting`, `WithSuperSampling`, `WithDpi`, `WithHeightPercent` |
| 1.7 | Add Outline section | Second `CollapsibleSection`. Default: collapsed and disabled. Header text: "Outline". | `CollapsibleSection` | `WithOutline(width, r, g, b)` |
| 1.8 | Add Shadow section | Third `CollapsibleSection`. Default: collapsed and disabled. Header text: "Shadow". | `CollapsibleSection` | `WithShadow(...)` |
| 1.9 | Add Gradient section | Fourth `CollapsibleSection`. Default: collapsed and disabled. Header text: "Gradient". | `CollapsibleSection` | `WithGradient(...)` |
| 1.10 | Add Channels section | Fifth `CollapsibleSection`. Default: collapsed and disabled. Header text: "Channels". | `CollapsibleSection` | `WithChannels(...)`, `WithChannelPacking()` |
| 1.11 | Add Advanced Rendering section | Sixth `CollapsibleSection`. Default: collapsed and disabled. Header text: "Advanced Rendering". | `CollapsibleSection` | `WithSdf`, `WithColorFont`, `WithEqualizeCellHeights`, etc. |
| 1.12 | Add "Reset All Effects" button | GUM `Button` at the bottom of the effects `StackPanel`, outside all collapsible sections. Styled as a flat/outline button. Click handler resets `EffectsViewModel` to all defaults. Shows a confirmation prompt (custom modal built from GUM primitives — `ColoredRectangle` overlay + centered `StackPanel` with Label + Yes/No Buttons) before resetting. | GUM `Button`, custom modal | Resets all options to defaults |
| 1.13 | Implement `ApplyToBuilder()` method | Method on `EffectsViewModel` that takes a `BmFontBuilder` and calls the appropriate `With*()` methods based on current state. Only applies effects where the section is enabled. This is the bridge between UI state and generation. | N/A (ViewModel) | All `BmFontBuilder.With*()` methods |
| 1.14 | Wire panel into main window | Add `EffectsPanel` to the right column of the three-panel layout (established in Phase 60). Bind to the `EffectsViewModel` instance from the main view model using `SetBinding()`. | Layout integration | N/A |
| 1.15 | Add keyboard navigation support | Ensure all collapsible sections and controls are reachable via Tab key. Sections toggle expand/collapse with Enter/Space. Focus order follows visual stack top-to-bottom. GUM's built-in focus management handles standard controls; custom controls must implement `IInputReceiver` for keyboard handling. | GUM focus system, `IInputReceiver` | N/A |

---

## Wave 2 — Font Style Controls

The Font Style section contains the most commonly used rendering settings. It is always enabled (no toggle CheckBox — the section header shows "Font Style" without a disable toggle, since some style setting is always active).

### View Model

```csharp
public class FontStyleViewModel : GumViewModel
{
    public bool Bold { get => Get<bool>(); set => Set(value); }                    // default: false
    public bool Italic { get => Get<bool>(); set => Set(value); }                  // default: false
    public AntiAliasMode AntiAliasMode { get => Get<AntiAliasMode>(); set => Set(value); }  // default: Grayscale
    public bool Hinting { get => Get<bool>(); set => Set(value); }                 // default: true
    public int Dpi { get => Get<int>(); set => Set(value); }                       // default: 72
    public int HeightPercent { get => Get<int>(); set => Set(value); }             // default: 100
    public int SuperSampleLevel { get => Get<int>(); set => Set(value); }          // default: 1
}
```

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 2.1 | Create `FontStyleViewModel` | View model with properties for all font style settings. Default values match KernSmith defaults (Bold=false, Italic=false, AntiAlias=Grayscale, Hinting=true, Dpi=72, HeightPercent=100, SuperSampleLevel=1). Uses GUM's `GumViewModel` base with `Get<T>`/`Set<T>`. | N/A (ViewModel) | Multiple |
| 2.2 | Bold toggle | GUM `CheckBox` labeled "Bold". Bound to `FontStyleViewModel.Bold` via `SetBinding()`. Inline with Italic toggle on the same row using a horizontal `StackPanel`. | GUM `CheckBox` | `WithBold(bool)` |
| 2.3 | Italic toggle | GUM `CheckBox` labeled "Italic". Same horizontal `StackPanel` row as Bold. | GUM `CheckBox` | `WithItalic(bool)` |
| 2.4 | Anti-aliasing mode dropdown | GUM `ComboBox` with items: "None", "Grayscale", "Light". `Label` text: "Anti-Aliasing". Selected index maps to `AntiAliasMode` enum via index-to-enum conversion in the view model. | GUM `ComboBox`, `Label` | `WithAntiAlias(AntiAliasMode)` |
| 2.5 | Hinting toggle | GUM `CheckBox` labeled "Hinting". Default checked. | GUM `CheckBox` | `WithHinting(bool)` |
| 2.6 | DPI numeric input | `NumericInput` (custom, from Wave 1) with Min=36, Max=600, Increment=1, bound to `Dpi`. `Label` text: "DPI". | `NumericInput`, `Label` | `WithDpi(int)` |
| 2.7 | Height percent slider with numeric input | `SliderWithNumeric` (custom, from Wave 1). Slider Min=50, Max=200, tick=10. `NumericInput` Min=50, Max=200. Both bound to `HeightPercent`. `Label` text: "Height %". A small GUM `Button` labeled "100" resets to 100. | `SliderWithNumeric`, `Label`, `Button` | `WithHeightPercent(int)` |
| 2.8 | Super-sampling level selector | Row of GUM `RadioButton` controls for 1x, 2x, 3x, 4x in a horizontal `StackPanel`. `Label` text: "Super-Sampling". Each `RadioButton` bound to `SuperSampleLevel` via checked-state logic in the view model. | GUM `RadioButton` x4, `Label`, `StackPanel` | `WithSuperSampling(int)` |
| 2.9 | Font style section layout | Arrange controls in vertical `StackPanel` with consistent spacing (6px). Each row is a horizontal `StackPanel` with a `Label` (fixed width 100px) and the control. Bold and Italic share a row. Consistent label width via explicit `Width` property on each `Label`. | GUM `StackPanel` (nested), `Label` | N/A |
| 2.10 | Section reset button | Small GUM `Button` labeled "Reset" in the section header. Click handler resets all font style properties to defaults. | GUM `Button` | Resets to defaults |
| 2.11 | Validation rules | DPI rejects values below 36 or above 600 (clamped in `NumericInput`). HeightPercent clamps to 50-200. SuperSampleLevel only allows 1-4. Invalid text input in `NumericInput` reverts to previous valid value on focus loss. | `NumericInput` validation logic | Validation before `With*()` |
| 2.12 | DPI preset buttons | Small row of preset GUM `Button` controls below the DPI input: "72" (screen), "96" (Windows), "144" (Retina). Click sets DPI to the preset value. Styled as compact buttons with reduced padding. | GUM `Button` x3 | `WithDpi(int)` |

---

## Wave 3 — Outline Configuration

The outline section adds a colored border around each glyph. Enabling this section means an outline will be applied during generation. This wave also introduces the `ColorPickerControl`, which is reused by shadow and gradient sections.

### View Model

```csharp
public class OutlineViewModel : GumViewModel
{
    public bool IsEnabled { get => Get<bool>(); set => Set(value); }       // default: false
    public int Width { get => Get<int>(); set => Set(value); }             // default: 1, range 1-20
    public byte ColorR { get => Get<byte>(); set => Set(value); }          // default: 0
    public byte ColorG { get => Get<byte>(); set => Set(value); }          // default: 0
    public byte ColorB { get => Get<byte>(); set => Set(value); }          // default: 0
}
```

### ColorPickerControl Implementation Detail

GUM does not provide a color picker. We build one from MonoGame rendering + GUM controls as a reusable custom control.

**Layout** (vertical `StackPanel`):

```
+-----------------------------------+
| [Color Swatch 32x32]  #FF0000    |  <- swatch + hex TextBox
+-----------------------------------+
| +-----------------------------+   |
| |                             |   |
| |   Color Spectrum            |   |  <- MonoGame SpriteBatch rendered
| |   (SV plane, 160x120)      |   |     Texture2D with saturation (X)
| |                             |   |     and value (Y). Hue from slider.
| |            X                |   |  <- crosshair at current S/V
| +-----------------------------+   |
| [=====Hue Slider (0-360)======]  |  <- GUM Slider, rainbow track
+-----------------------------------+
| R [===slider===] [NumericInput]   |  <- GUM Slider + NumericInput (0-255)
| G [===slider===] [NumericInput]   |
| B [===slider===] [NumericInput]   |
+-----------------------------------+
| [Blk][Wht][Red][Grn][Blu][Yel]   |  <- preset color buttons
+-----------------------------------+
```

**Spectrum rendering**: A `Texture2D` (160x120) regenerated whenever the Hue slider changes. Each pixel's color is calculated from HSV where H = slider value, S = x/width, V = 1 - y/height. The texture is drawn via `SpriteBatch.Draw()` in the control's `Draw()` override. Clicking on the spectrum updates S and V, which are then converted to R/G/B.

**Hue slider track**: The rainbow gradient is a 1x256 `Texture2D` with hue cycling from 0 to 360. Drawn behind the GUM `Slider` track using `SpriteBatch`.

**Hex input**: A GUM `TextBox` accepting 6-character hex (no # prefix shown in box, but displayed as label). On focus loss, parses hex to R/G/B. Invalid hex reverts to previous value.

**Bidirectional sync**: Changing R/G/B sliders updates the spectrum crosshair position and hex text. Changing the spectrum updates R/G/B sliders and hex. Changing hex updates everything. The view model is the single source of truth — all controls bind to it.

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 3.1 | Create `OutlineViewModel` | View model with `IsEnabled`, `Width`, `ColorR/G/B`. Default: disabled, width=1, color=black (0,0,0). | N/A (ViewModel) | `WithOutline(int width, byte r, byte g, byte b)` |
| 3.2 | Enable/disable toggle in header | `CheckBox` in the Outline `CollapsibleSection` header. Bound to `OutlineViewModel.IsEnabled` via `SetBinding()`. When checked, section auto-expands. When unchecked, content dims (alpha overlay). | GUM `CheckBox` | Controls whether `WithOutline()` is called |
| 3.3 | Build `ColorPickerControl` — spectrum texture | Create a `Texture2D` (160x120) rendered via MonoGame `SpriteBatch`. Each pixel colored from HSV where H = current hue, S = x/width, V = 1-y/height. Regenerate texture when hue slider changes. Use `Texture2D.SetData<Color>()` for pixel population. | MonoGame `Texture2D`, `SpriteBatch` | N/A (reusable control) |
| 3.4 | Build `ColorPickerControl` — spectrum interaction | On mouse click/drag over the spectrum area, calculate S and V from pointer position relative to spectrum bounds. Convert HSV to RGB and update the view model's R/G/B properties. Draw a small crosshair (two 9px lines, white with black outline for visibility) at the current S/V position. Use MonoGame input (`Mouse.GetState()`) or GUM's input system. | MonoGame input, `SpriteBatch` line drawing | N/A (reusable control) |
| 3.5 | Build `ColorPickerControl` — hue slider | GUM `Slider` with Min=0, Max=360, positioned below the spectrum. Custom track rendering: draw a 1x360 `Texture2D` rainbow gradient behind the slider track using `SpriteBatch`. When hue changes, regenerate the spectrum texture and update R/G/B from the new HSV. | GUM `Slider`, MonoGame `Texture2D` | N/A (reusable control) |
| 3.6 | Build `ColorPickerControl` — R/G/B sliders | Three rows, each with a `Label` ("R"/"G"/"B"), a GUM `Slider` (Min=0, Max=255), and a `NumericInput` (Min=0, Max=255). All bound to the corresponding R/G/B property. When R/G/B changes, update the spectrum crosshair position by converting RGB to HSV and repositioning. | GUM `Slider` x3, `NumericInput` x3, `Label` x3 | Color bytes |
| 3.7 | Build `ColorPickerControl` — hex input | GUM `TextBox` (max 6 chars) showing current color as hex (e.g., "FF0000"). `Label` prefix "#". On focus loss or Enter key, parse hex to R/G/B. Invalid input reverts. On R/G/B change, update hex text. | GUM `TextBox`, `Label` | Color bytes |
| 3.8 | Build `ColorPickerControl` — color swatch | A 32x32 `ColoredRectangle` (GUM primitive) whose color is bound to the current R/G/B. Positioned next to the hex input as a visual reference. Bordered with a 1px gray outline (another `ColoredRectangle` behind it, 34x34). | GUM `ColoredRectangle` x2 | Visual only |
| 3.9 | Build `ColorPickerControl` — preset buttons | Row of small GUM `Button` controls (24x24 each) for common colors: Black, White, Red, Green, Blue, Yellow. Each button's background is a `ColoredRectangle` filled with the preset color. Clicking sets R/G/B to the preset values. | GUM `Button` x6, `ColoredRectangle` x6 | Color bytes |
| 3.10 | Build `ColorPickerControl` — popup mode | The full picker (spectrum + sliders + hex) is shown in a popup overlay when the swatch is clicked, and hidden when clicking outside. In collapsed state, only the swatch (32x32) and hex text are visible. The popup is a `StackPanel` with all picker controls, positioned absolutely near the swatch. A transparent full-screen `ColoredRectangle` behind the popup catches outside clicks to dismiss. | GUM `StackPanel`, `ColoredRectangle` (overlay) | N/A |
| 3.11 | Width slider | `SliderWithNumeric`. Slider Min=1, Max=20, snap to integer. `NumericInput` Min=1, Max=20, Increment=1. Both bound to `OutlineViewModel.Width`. `Label` text: "Width (px)". | `SliderWithNumeric`, `Label` | `WithOutline(width, ...)` |
| 3.12 | Outline color picker instance | Instance of `ColorPickerControl` bound to `OutlineViewModel.ColorR/G/B`. `Label` text: "Color". Positioned below the width control. | `ColorPickerControl`, `Label` | `WithOutline(..., r, g, b)` |
| 3.13 | Outline preview | A small area (64x64) rendered via MonoGame `SpriteBatch` showing a sample character "A" with the current outline width and color. Drawn using a simple pixel-approximation algorithm (not full KernSmith generation). Background is checkerboard (transparency indicator). Updated when width or color changes, throttled to 15fps max. | MonoGame `SpriteBatch`, `Texture2D` | Visual only |
| 3.14 | Section layout | Vertical `StackPanel` with 6px spacing. Row 0: Width slider+numeric. Row 1: Color picker (collapsed mode — swatch + hex). Row 2: Outline preview. Color picker popup overlays everything when expanded. | GUM `StackPanel` | N/A |
| 3.15 | Section reset | Reset button in header restores Width=1, Color=Black. No confirmation needed for section-level reset. | GUM `Button` | Resets to defaults |

---

## Wave 4 — Shadow Configuration

The shadow section provides a drop shadow behind each glyph with configurable offset, blur, color, and opacity. The standout feature is an interactive 2D offset pad built with MonoGame rendering.

### View Model

```csharp
public class ShadowViewModel : GumViewModel
{
    public bool IsEnabled { get => Get<bool>(); set => Set(value); }       // default: false
    public int OffsetX { get => Get<int>(); set => Set(value); }           // default: 2, range -20 to +20
    public int OffsetY { get => Get<int>(); set => Set(value); }           // default: 2, range -20 to +20
    public int Blur { get => Get<int>(); set => Set(value); }              // default: 0, range 0-20
    public byte ColorR { get => Get<byte>(); set => Set(value); }          // default: 0 (black)
    public byte ColorG { get => Get<byte>(); set => Set(value); }          // default: 0
    public byte ColorB { get => Get<byte>(); set => Set(value); }          // default: 0
    public float Opacity { get => Get<float>(); set => Set(value); }       // default: 1.0, range 0.0-1.0
}
```

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 4.1 | Create `ShadowViewModel` | View model with all shadow properties. Defaults: disabled, offsetX=2, offsetY=2, blur=0, color=black, opacity=1.0. | N/A (ViewModel) | `WithShadow(int offsetX, int offsetY, int blur, (byte R, byte G, byte B)? color, float opacity)` |
| 4.2 | Enable/disable toggle in header | `CheckBox` in the Shadow `CollapsibleSection` header. Bound to `IsEnabled`. Auto-expands section when checked. | GUM `CheckBox` | Controls whether `WithShadow()` is called |
| 4.3 | Offset X slider | `SliderWithNumeric`. Slider Min=-20, Max=+20, snap to integer. `NumericInput` Min=-20, Max=20, Increment=1. `Label` text: "Offset X". | `SliderWithNumeric`, `Label` | `WithShadow(offsetX, ...)` |
| 4.4 | Offset Y slider | `SliderWithNumeric`. Same range as X. `Label` text: "Offset Y". | `SliderWithNumeric`, `Label` | `WithShadow(..., offsetY, ...)` |
| 4.5 | Build `ShadowOffsetPad` — rendering | **Custom control**: A 140x140 area rendered via MonoGame `SpriteBatch`. Background: subtle dark gray fill. Grid lines at 5-unit intervals drawn as thin (1px) lines in a lighter gray. Crosshair lines (horizontal + vertical through center) drawn as dashed lines. A filled circle (radius 6) at the current offset position drawn in an accent color (e.g., cornflower blue). The center of the pad represents (0, 0). Coordinate range: -20 to +20 on each axis. | MonoGame `SpriteBatch`, `Texture2D` (1x1 pixel for line drawing) | `WithShadow(offsetX, offsetY, ...)` |
| 4.6 | Build `ShadowOffsetPad` — interaction | On mouse press within the pad area, capture input and map pixel position to offset coordinates: `offsetX = (mouseX - centerX) / (padWidth / 40)`, similarly for Y. Snap to nearest integer. On mouse drag (while pressed), update offset in real time. On release, finalize. Clamp to -20..+20 range. Update `ShadowViewModel.OffsetX` and `OffsetY` via bindings. | MonoGame input (`Mouse.GetState()`) or GUM input | `WithShadow(offsetX, offsetY, ...)` |
| 4.7 | Build `ShadowOffsetPad` — keyboard support | When the pad is focused (via Tab navigation), arrow keys move the dot by 1 unit. Shift+arrow moves by 5 units. Home key centers to (0,0). Implement via GUM's `IInputReceiver` or MonoGame's `Keyboard.GetState()`. | `IInputReceiver`, MonoGame `Keyboard` | `WithShadow(offsetX, offsetY, ...)` |
| 4.8 | Offset coordinate display | GUM `Label` below the pad showing "Offset: (2, 2)" updated in real time as the dot is dragged. Uses monospace-style formatting for stable width. | GUM `Label` | Visual only |
| 4.9 | Offset pad reset button | Small GUM `Button` labeled "Center" below the coordinate display. Click resets offset to (0, 0). The dot position updates immediately (no animation — MonoGame/GUM does not have a built-in animation system like Avalonia). | GUM `Button` | Resets offsets to 0,0 |
| 4.10 | Blur radius slider | `SliderWithNumeric`. Slider Min=0, Max=20, snap to integer. `NumericInput` Min=0, Max=20. `Label` text: "Blur". | `SliderWithNumeric`, `Label` | `WithShadow(..., blur, ...)` |
| 4.11 | Shadow color picker | Instance of `ColorPickerControl` (from Wave 3) bound to `ShadowViewModel.ColorR/G/B`. `Label` text: "Color". Default preset buttons: Black, Dark Gray (#404040), Navy (#000080), Dark Red (#8B0000). | `ColorPickerControl`, `Label` | `WithShadow(..., color, ...)` |
| 4.12 | Opacity slider | `SliderWithNumeric`. Slider Min=0, Max=100 (representing percentage). `NumericInput` Min=0, Max=100, Increment=5. `Label` text: "Opacity". A GUM `Label` to the right shows value + "%" (e.g., "75%"). The view model stores 0.0-1.0 float; the slider/numeric use 0-100 int with conversion in binding logic (divide by 100). | `SliderWithNumeric`, `Label` x2 | `WithShadow(..., opacity)` |
| 4.13 | Sync sliders with pad | When Offset X or Offset Y sliders change, update the pad dot position. When the pad dot is dragged, update the sliders. The view model is the single source of truth — both controls bind to the same `OffsetX`/`OffsetY` properties, so bidirectional sync is automatic. | Binding via ViewModel | N/A |
| 4.14 | Section layout | Vertical `StackPanel` with 6px spacing. Row 0-1: Offset X/Y sliders+numerics. Row 2: 2D offset pad (centered via padding or wrapper `StackPanel`). Row 3: Coordinate display + center button (horizontal `StackPanel`). Row 4: Blur slider+numeric. Row 5: Color picker. Row 6: Opacity slider+numeric. | GUM `StackPanel` (nested) | N/A |
| 4.15 | Section reset | Restores all shadow properties to defaults (offset 2,2, blur 0, black, opacity 1.0). | GUM `Button` | Resets to defaults |

### 2D Offset Pad Implementation Detail

The 2D offset pad is the signature interactive control for shadow configuration. It replaces the tedious process of adjusting two separate sliders by providing a visual, intuitive drag surface.

```
+---------------------------+
|          |                |
|     .....|.....           |
|          |                |
|  --------+--------       |  <- crosshair at (0,0)
|          |   *            |  <- draggable dot at current offset
|     .....|.....           |
|          |                |
+---------------------------+
        Offset: (3, 2)
       [ Center Reset ]
```

**Coordinate mapping**: The pad's pixel space maps linearly to the offset range (-20 to +20 on each axis). Center pixel = (0, 0). For a 140px pad covering 40 units, each unit is 3.5 pixels.

**Rendering** (in `Draw()` method, called from MonoGame's draw loop):
1. Draw background fill (`SpriteBatch.Draw` with a 1x1 white `Texture2D` scaled and tinted).
2. Draw grid lines at 5-unit intervals (8 lines per axis).
3. Draw center crosshair (two full-width lines through center).
4. Draw the offset dot as a filled circle using `MonoGame.Extended`'s `ShapeExtensions.DrawCircle()` or a small circle `Texture2D`.
5. All coordinates calculated from `OffsetX`/`OffsetY` view model values.

**Interaction**:
- Mouse press within pad bounds: capture, set offset from position
- Mouse drag (while captured): update offset in real time
- Mouse release: finalize, snap to integer
- Arrow keys: move dot by 1 unit
- Shift+Arrow: move dot by 5 units

---

## Wave 5 — Gradient Configuration

The gradient section is the most visually rich effects control. It features an interactive circular angle selector (compass control) built with MonoGame rendering, color pickers for start/end colors, a midpoint slider, and a live gradient preview bar.

### View Model

```csharp
public class GradientViewModel : GumViewModel
{
    public bool IsEnabled { get => Get<bool>(); set => Set(value); }               // default: false
    public byte StartR { get => Get<byte>(); set => Set(value); }                   // default: 255
    public byte StartG { get => Get<byte>(); set => Set(value); }                   // default: 255
    public byte StartB { get => Get<byte>(); set => Set(value); }                   // default: 255
    public byte EndR { get => Get<byte>(); set => Set(value); }                     // default: 0
    public byte EndG { get => Get<byte>(); set => Set(value); }                     // default: 0
    public byte EndB { get => Get<byte>(); set => Set(value); }                     // default: 0
    public float AngleDegrees { get => Get<float>(); set => Set(value); }           // default: 90, range 0-360
    public float Midpoint { get => Get<float>(); set => Set(value); }               // default: 0.5, range 0.01-0.99
    public int SelectedPresetIndex { get => Get<int>(); set => Set(value); }        // -1 = custom
}
```

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 5.1 | Create `GradientViewModel` | View model with start/end colors, angle, midpoint, preset index. Defaults: disabled, start=white (255,255,255), end=black (0,0,0), angle=90 (top-to-bottom), midpoint=0.5, preset=-1 (custom). | N/A (ViewModel) | `WithGradient((byte R, byte G, byte B) startColor, (byte R, byte G, byte B) endColor, float angleDegrees, float midpoint)` |
| 5.2 | Enable/disable toggle in header | `CheckBox` in the Gradient `CollapsibleSection` header. Bound to `IsEnabled`. | GUM `CheckBox` | Controls whether `WithGradient()` is called |
| 5.3 | Start color picker | Instance of `ColorPickerControl` bound to `StartR/G/B`. `Label` text: "Start Color". | `ColorPickerControl`, `Label` | `WithGradient(startColor, ...)` |
| 5.4 | End color picker | Instance of `ColorPickerControl` bound to `EndR/G/B`. `Label` text: "End Color". | `ColorPickerControl`, `Label` | `WithGradient(..., endColor, ...)` |
| 5.5 | Swap colors button | GUM `Button` labeled with swap arrows text ("< >") between start and end color pickers. Click swaps StartR/G/B with EndR/G/B values. | GUM `Button` | Swaps start/end colors |
| 5.6 | Build `GradientDirectionControl` — rendering | **Custom control**: A 140x140 area rendered via MonoGame `SpriteBatch`. Visual: a circle outline (radius 55, 2px stroke, gray) centered in the control. Tick marks at 0/45/90/135/180/225/270/315 degrees — cardinal ticks longer (10px) than diagonal ticks (6px). Cardinal labels ("0", "90", "180", "270") drawn outside the circle using `SpriteFont`. A line from center to circle edge at the current angle (accent color, 2px). A filled circle handle (radius 8, accent color) at the line/circle intersection. Background transparent. Draw using `MonoGame.Extended` shape primitives (`DrawCircle`, `DrawLine`) and `SpriteBatch.DrawString()`. | MonoGame `SpriteBatch`, `MonoGame.Extended` shapes, `SpriteFont` | `WithGradient(..., angleDegrees, ...)` |
| 5.7 | Build `GradientDirectionControl` — pointer interaction | On mouse press within the control bounds, or mouse drag while pressed, calculate angle from center to pointer position using `Math.Atan2(dy, dx)`. Convert to degrees (0-360). Update `AngleDegrees` in the view model in real time. Snap to nearest 1 degree. When Shift is held (checked via `Keyboard.GetState()`), snap to nearest 15-degree increment (0, 15, 30, 45, ...). | MonoGame `Mouse.GetState()`, `Keyboard.GetState()` | `WithGradient(..., angleDegrees, ...)` |
| 5.8 | Build `GradientDirectionControl` — keyboard support | When focused (implement `IInputReceiver`), Left/Right arrow keys adjust angle by 1 degree. Shift+Left/Right adjusts by 15 degrees. Home key sets to 0, End key sets to 180. | `IInputReceiver`, MonoGame `Keyboard` | `WithGradient(..., angleDegrees, ...)` |
| 5.9 | Angle numeric input | `NumericInput` (custom) below the compass. Min=0, Max=360, Increment=1, DecimalPlaces=1. Bound to `AngleDegrees`. **Note:** This `NumericInput` must use a `WrapAround` mode instead of clamping — values > 360 wrap to 0 (e.g., 361 becomes 1), and values < 0 wrap to 360+value (e.g., -1 becomes 359). This differs from the standard `NumericInput` clamp behavior and requires the control to support a `WrapAround` property. | `NumericInput`, `Label` | `WithGradient(..., angleDegrees, ...)` |
| 5.10 | Angle quick-set buttons | Row of small GUM `Button` controls for common angles: "0" (horizontal L-R), "90" (vertical T-B), "180" (horizontal R-L), "270" (vertical B-T), "45" (diagonal). Clicking sets `AngleDegrees` to the value. Compact styling with reduced padding. | GUM `Button` x5 | `WithGradient(..., angleDegrees, ...)` |
| 5.11 | Midpoint slider | `SliderWithNumeric`. Slider Min=1, Max=99 (representing 0.01-0.99 after division by 100). `NumericInput` Min=1, Max=99, Increment=1. `Label` text: "Midpoint". A GUM `Label` to the right shows the decimal value (e.g., "0.50"). Conversion: view model stores float 0.01-0.99, slider uses int 1-99 with divide/multiply by 100 in binding logic. | `SliderWithNumeric`, `Label` x2 | `WithGradient(..., midpoint)` |
| 5.12 | Gradient preview bar | A 200x24 area rendered via MonoGame `SpriteBatch`. Creates a `Texture2D` (200x1) where each pixel is interpolated between StartColor and EndColor based on the current angle and midpoint. The midpoint shifts the interpolation center. For a horizontal preview bar, the pixel at position x has color = lerp(start, end, t) where t accounts for the midpoint bias. The preview simplifies the angle to a 1D bar (always drawn left-to-right regardless of angle — the compass shows the actual direction). Regenerated when any gradient property changes, throttled to 15fps. | MonoGame `Texture2D`, `SpriteBatch` | Visual only |
| 5.13 | Gradient preview bar — midpoint indicator | A small triangle marker (drawn via `MonoGame.Extended` polygon or three `SpriteBatch` lines forming a triangle) below the preview bar showing where the midpoint falls. Position: x = midpoint * barWidth. Moves as `Midpoint` changes. Color: white with dark outline for visibility. | MonoGame `SpriteBatch` or `MonoGame.Extended` shapes | Visual only |
| 5.14 | Gradient presets dropdown | GUM `ComboBox` with predefined gradient configurations. Items: "Custom", "Fire", "Ice", "Gold", "Neon", "Sunset". Selecting a preset populates start color, end color, angle, and midpoint. Presets: **Fire** (red #FF0000 to yellow #FFFF00, angle=270), **Ice** (white #FFFFFF to blue #0066FF, angle=90), **Gold** (gold #FFD700 to brown #8B4513, angle=90), **Neon** (cyan #00FFFF to magenta #FF00FF, angle=0), **Sunset** (orange #FF6600 to purple #6600CC, angle=90). "Custom" is auto-selected when user modifies any value after selecting a preset. | GUM `ComboBox` | `WithGradient(...)` |
| 5.15 | Section layout | Vertical `StackPanel` with 6px spacing. Row 0: Start color picker. Row 1: Swap button (centered). Row 2: End color picker. Row 3: Compass control (centered via padding). Row 4: Angle numeric + quick-set buttons (horizontal `StackPanel`). Row 5: Midpoint slider+numeric. Row 6: Gradient preview bar + midpoint indicator. Row 7: Presets dropdown. | GUM `StackPanel` (nested) | N/A |
| 5.16 | Section reset | Restores all gradient properties to defaults (white-to-black, angle=90, midpoint=0.5, no preset). | GUM `Button` | Resets to defaults |

### Compass Control Implementation Detail

The circular angle compass is the key interactive element of the gradient section. It provides an intuitive "point in a direction" metaphor for setting gradient angle.

```
         270 (up)
          |
          |
180 ------+------ 0 (right)
          |
          |
         90 (down)
```

**Rendering** (in `Draw()` method):
1. Draw a circle outline using `MonoGame.Extended`'s `ShapeExtensions.DrawCircle()` — center of control, radius 55, gray, 2px thickness, 64 sides.
2. Draw tick marks at 0, 45, 90, 135, 180, 225, 270, 315 degrees using `DrawLine()`. Cardinal ticks are 10px long inward from the circle edge, diagonal ticks 6px.
3. Draw cardinal labels ("0", "90", "180", "270") outside the circle using `SpriteBatch.DrawString()` with a `SpriteFont`. Position offset outward from the tick by 12px.
4. Draw a line from center to the circle edge at the current angle using `DrawLine()` — accent color, 2px.
5. Draw a filled circle handle using `DrawCircle()` filled — radius 8, accent color — at the intersection of the line and circle edge.
6. The angle follows KernSmith convention: 0 is right (east), 90 is down (south), matching the `angleDegrees` parameter (0 = left-to-right, 90 = top-to-bottom).

**Coordinate math**:
```csharp
// Convert angle to position for drawing the handle
double radians = AngleDegrees * Math.PI / 180.0;
float handleX = centerX + radius * (float)Math.Cos(radians);
float handleY = centerY + radius * (float)Math.Sin(radians);

// Convert pointer position back to angle
float dx = pointerX - centerX;
float dy = pointerY - centerY;
double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
if (angle < 0) angle += 360;
```

---

## Wave 6 — Channel Configuration

The channel section controls how glyph data is distributed across RGBA channels in the output texture. This is primarily used for advanced rendering techniques like channel packing and SDF.

### View Model

```csharp
public class ChannelViewModel : GumViewModel
{
    public bool IsEnabled { get => Get<bool>(); set => Set(value); }                 // default: false
    public bool ChannelPacking { get => Get<bool>(); set => Set(value); }            // default: false
    public int AlphaContentIndex { get => Get<int>(); set => Set(value); }           // maps to ChannelContent enum
    public int RedContentIndex { get => Get<int>(); set => Set(value); }
    public int GreenContentIndex { get => Get<int>(); set => Set(value); }
    public int BlueContentIndex { get => Get<int>(); set => Set(value); }
    public bool InvertAlpha { get => Get<bool>(); set => Set(value); }               // default: false
    public bool InvertRed { get => Get<bool>(); set => Set(value); }                 // default: false
    public bool InvertGreen { get => Get<bool>(); set => Set(value); }               // default: false
    public bool InvertBlue { get => Get<bool>(); set => Set(value); }                // default: false
    public int SelectedPresetIndex { get => Get<int>(); set => Set(value); }         // -1 = custom
}
```

Note: GUM `ComboBox` works with selected index (int), not enum values directly. The view model stores indices and converts to `ChannelContent` enum values in `ApplyToBuilder()`. **Important:** Populate ComboBox items programmatically from `Enum.GetValues<ChannelContent>()` rather than hardcoding strings. This prevents index-to-enum drift if `ChannelContent` members are added, removed, or reordered.

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 6.1 | Create `ChannelViewModel` | View model with per-channel content index and invert flags. Defaults match `ChannelConfig` defaults: all index 0 (Glyph), no inversion, packing off. | N/A (ViewModel) | `WithChannels(ChannelConfig)`, `WithChannelPacking(bool)` |
| 6.2 | Enable/disable toggle in header | `CheckBox` in the Channels `CollapsibleSection` header. When unchecked, default channel config is used (all Glyph, no inversion, no packing). | GUM `CheckBox` | Controls whether custom `WithChannels()` is called |
| 6.3 | Channel packing toggle | GUM `CheckBox` labeled "Enable Channel Packing". Bound to `ChannelPacking`. | GUM `CheckBox` | `WithChannelPacking(bool)` |
| 6.4 | Channel content table header | A horizontal `StackPanel` row with three `Label` controls: "Channel" (width 60), "Content" (width 120), "Invert" (width 50). Styled with a subtle background `ColoredRectangle` behind the row to visually distinguish the header. | GUM `Label` x3, `StackPanel`, `ColoredRectangle` | N/A |
| 6.5 | Alpha channel row | Horizontal `StackPanel` row with: `Label` "Alpha" (width 60), `ComboBox` (items: Glyph, Outline, GlyphAndOutline, Zero, One; bound to `AlphaContentIndex`), `CheckBox` (bound to `InvertAlpha`). | GUM `Label`, `ComboBox`, `CheckBox` | `WithChannels(alpha: ...)` |
| 6.6 | Red channel row | Same structure as Alpha. `Label` "Red". Bound to `RedContentIndex` and `InvertRed`. Row has a subtle red-tinted `ColoredRectangle` background (RGBA: 255, 0, 0, 15) for visual identification. | GUM `Label`, `ComboBox`, `CheckBox`, `ColoredRectangle` | `WithChannels(..., red: ...)` |
| 6.7 | Green channel row | Same structure. `Label` "Green". Bound to `GreenContentIndex` and `InvertGreen`. Subtle green tint (0, 255, 0, 15). | GUM `Label`, `ComboBox`, `CheckBox`, `ColoredRectangle` | `WithChannels(..., green: ...)` |
| 6.8 | Blue channel row | Same structure. `Label` "Blue". Bound to `BlueContentIndex` and `InvertBlue`. Subtle blue tint (0, 0, 255, 15). | GUM `Label`, `ComboBox`, `CheckBox`, `ColoredRectangle` | `WithChannels(..., blue: ...)` |
| 6.9 | Channel presets dropdown | GUM `ComboBox` labeled "Preset" at the top of the section. Items: "Standard", "Outline in Alpha", "Packed 4-Channel", "SDF", "Custom". Selecting a preset populates all four channel dropdowns, invert flags, and the packing toggle. Presets: **Standard** (all Glyph, no invert, packing off), **Outline in Alpha** (Alpha=Outline, RGB=Glyph, no invert), **Packed 4-Channel** (all Glyph, packing=true), **SDF** (Alpha=Glyph, RGB=Zero, no invert), **Custom** (no changes, manual editing). | GUM `ComboBox`, `Label` | `WithChannels(...)` |
| 6.10 | Preset auto-detection | When user manually changes any channel setting, check if the current configuration matches a known preset. If yes, select that preset in the dropdown. If no, select "Custom". This prevents the UI from showing "Standard" when the config has been modified. Logic lives in the view model's property setters. | Logic in ViewModel | N/A |
| 6.11 | Channel visual preview | A horizontal `StackPanel` of four 30x30 `ColoredRectangle` controls, one per channel (Alpha=gray background, Red=red, Green=green, Blue=blue). Each has a `Label` overlay showing abbreviated content (e.g., "Glyph", "OL", "G+OL", "0", "1"). Inverted channels show "INV" as a second line or suffix. | GUM `ColoredRectangle` x4, `Label` x4, `StackPanel` | Visual only |
| 6.12 | Section layout | Vertical `StackPanel` with 6px spacing. Row 0: Presets dropdown. Row 1: Channel packing toggle. Row 2: Table header. Rows 3-6: Channel rows (A, R, G, B). Row 7: Visual preview (horizontal `StackPanel`). | GUM `StackPanel` (nested) | N/A |
| 6.13 | Outline availability warning | When any channel content index maps to Outline or GlyphAndOutline, but the `OutlineViewModel.IsEnabled` is false, show a `Label` with warning text: "Outline effect is not enabled. Enable it in the Outline section for this channel to contain outline data." Styled with orange/amber text color (RGBA: 255, 165, 0, 255). Visibility controlled by logic in the view model that checks across view models. | GUM `Label` | N/A |
| 6.14 | Section reset | Restores all channels to Glyph (index 0), no inversion, packing off, preset = Standard (index 0). | GUM `Button` | Resets to defaults |

---

## Wave 7 — Advanced Rendering Options

The advanced rendering section contains specialized options that most users will not need for basic bitmap font generation. These are power-user features.

### View Model

```csharp
public class AdvancedRenderingViewModel : GumViewModel
{
    public bool IsEnabled { get => Get<bool>(); set => Set(value); }               // default: false (section toggle)
    public bool Sdf { get => Get<bool>(); set => Set(value); }                      // default: false
    public bool ColorFont { get => Get<bool>(); set => Set(value); }                // default: false
    public int ColorPaletteIndex { get => Get<int>(); set => Set(value); }         // default: 0
    public bool EqualizeCellHeights { get => Get<bool>(); set => Set(value); }      // default: false
    public bool ForceOffsetsToZero { get => Get<bool>(); set => Set(value); }       // default: false
    public bool MatchCharHeight { get => Get<bool>(); set => Set(value); }          // default: false
}
```

### Tasks

| # | Task | Detail | GUM/MonoGame Controls | API Mapping |
|---|------|--------|-----------------------|-------------|
| 7.1 | Create `AdvancedRenderingViewModel` | View model with all advanced rendering flags. All default to false/0. | N/A (ViewModel) | Multiple `With*()` methods |
| 7.2 | Enable/disable toggle in header | `CheckBox` in the Advanced Rendering `CollapsibleSection` header. When unchecked, none of the advanced options are applied — all use default values. | GUM `CheckBox` | Controls whether any advanced `With*()` calls are made |
| 7.3 | SDF toggle | GUM `CheckBox` labeled "Signed Distance Field (SDF)". Bound to `Sdf`. | GUM `CheckBox` | `WithSdf(bool)` |
| 7.4 | SDF info text | GUM `Label` below the SDF toggle, only visible when SDF is checked. Text: "SDF output requires special shaders in your rendering engine. Not compatible with standard bitmap font renderers." Styled with italic font and muted gray color (RGBA: 150, 150, 150, 255). Visibility toggled in the view model's `Sdf` property setter. | GUM `Label` | N/A |
| 7.5 | Color font toggle | GUM `CheckBox` labeled "Color Font (Emoji)". Bound to `ColorFont`. | GUM `CheckBox` | `WithColorFont(bool)` |
| 7.6 | Color palette index | `NumericInput` (custom) labeled "Color Palette". Min=0, Max=99, Increment=1. Enabled only when `ColorFont` is checked — the `NumericInput`'s enabled state is bound to `ColorFont` via view model logic. | `NumericInput`, `Label` | `WithColorPaletteIndex(int)` |
| 7.7 | Equalize cell heights toggle | GUM `CheckBox` labeled "Equalize Cell Heights". Bound to `EqualizeCellHeights`. | GUM `CheckBox` | `WithEqualizeCellHeights(bool)` |
| 7.8 | Force offsets to zero toggle | GUM `CheckBox` labeled "Force Offsets to Zero". Bound to `ForceOffsetsToZero`. | GUM `CheckBox` | `WithForceOffsetsToZero(bool)` |
| 7.9 | Match char height toggle | GUM `CheckBox` labeled "Match Character Height". Bound to `MatchCharHeight`. Visibility controlled by a flag from the main view model indicating custom glyphs are present — hidden when not applicable. | GUM `CheckBox` | `WithMatchCharHeight(bool)` |
| 7.10 | Section layout | Vertical `StackPanel` with 8px spacing. Each option is a row in the stack: checkbox, then optional secondary control below it. SDF and Color Font options are at the top (most commonly used advanced options). Cell height and offset options are at the bottom. | GUM `StackPanel` | N/A |
| 7.11 | Mutual exclusion warnings | When SDF is enabled and outline/shadow/gradient sections are also enabled (checked via cross-view-model references), show a `Label` with warning text: "SDF mode may not produce expected results when combined with outline, shadow, or gradient effects." Styled with amber text (RGBA: 255, 165, 0, 255). Visibility logic in the view model checks sibling section `IsEnabled` flags. | GUM `Label` | N/A |
| 7.12 | Section reset | Restores all advanced options to defaults (all false, palette index 0). | GUM `Button` | Resets to defaults |

---

## Cross-Cutting Concerns

### View Model to Builder Bridge

The `EffectsViewModel.ApplyToBuilder(BmFontBuilder builder)` method is the single point where UI state is translated to API calls. It follows this logic:

```csharp
public void ApplyToBuilder(BmFontBuilder builder)
{
    // Font Style (always applied)
    builder.WithBold(FontStyle.Bold)
           .WithItalic(FontStyle.Italic)
           .WithAntiAlias(FontStyle.AntiAliasMode)
           .WithHinting(FontStyle.Hinting)
           .WithDpi(FontStyle.Dpi)
           .WithHeightPercent(FontStyle.HeightPercent)
           .WithSuperSampling(FontStyle.SuperSampleLevel);

    // Outline (only if enabled)
    if (Outline.IsEnabled)
        builder.WithOutline(Outline.Width, Outline.ColorR, Outline.ColorG, Outline.ColorB);

    // Shadow (only if enabled)
    if (Shadow.IsEnabled)
        builder.WithShadow(Shadow.OffsetX, Shadow.OffsetY, Shadow.Blur,
            (Shadow.ColorR, Shadow.ColorG, Shadow.ColorB), Shadow.Opacity);

    // Gradient (only if enabled)
    if (Gradient.IsEnabled)
        builder.WithGradient(
            (Gradient.StartR, Gradient.StartG, Gradient.StartB),
            (Gradient.EndR, Gradient.EndG, Gradient.EndB),
            Gradient.AngleDegrees, Gradient.Midpoint);

    // Channels (only if enabled)
    if (Channels.IsEnabled)
    {
        var alphaContent = (ChannelContent)Channels.AlphaContentIndex;
        var redContent = (ChannelContent)Channels.RedContentIndex;
        var greenContent = (ChannelContent)Channels.GreenContentIndex;
        var blueContent = (ChannelContent)Channels.BlueContentIndex;

        builder.WithChannelPacking(Channels.ChannelPacking)
               .WithChannels(alphaContent, redContent, greenContent, blueContent,
                   Channels.InvertAlpha, Channels.InvertRed,
                   Channels.InvertGreen, Channels.InvertBlue);
    }

    // Advanced (only if enabled)
    if (Advanced.IsEnabled)
    {
        builder.WithSdf(Advanced.Sdf)
               .WithColorFont(Advanced.ColorFont)
               .WithColorPaletteIndex(Advanced.ColorPaletteIndex)
               .WithEqualizeCellHeights(Advanced.EqualizeCellHeights)
               .WithForceOffsetsToZero(Advanced.ForceOffsetsToZero)
               .WithMatchCharHeight(Advanced.MatchCharHeight);
    }
}
```

### Theming and Styling

- All controls use GUM's default styling with no custom themes, consistent with Phase 60 decisions.
- Custom controls (ColorPicker, Compass, 2D Pad) use MonoGame's `SpriteBatch` for rendering with colors from a centralized `UiColors` static class for consistency.
- Section headers use bold `SpriteFont` rendering and larger text where supported by GUM `Label`.
- Labels use a consistent muted gray color (RGBA: 180, 180, 180, 255) for secondary text.
- `NumericInput` controls use consistent width (70px) across all sections.
- GUM `Slider` controls fill available horizontal space via `WidthUnits = DimensionUnitType.RelativeToContainer`.
- `ColoredRectangle` controls are used for color swatches, backgrounds, and visual accents throughout.

### Accessibility

- All GUM controls should have descriptive `Name` properties for identification.
- Color pickers must also show hex values (never rely on color alone to convey information).
- Keyboard navigation must work for all custom controls (compass, 2D pad) via `IInputReceiver`.
- Focus indicators: custom controls must draw a visible focus ring (e.g., 2px accent-colored border) when focused.
- CheckBox state changes should be reflected in the `Label` text or a status `Label` for users who cannot perceive the visual toggle.

### Serialization / Persistence

- The `EffectsViewModel` should be serializable to/from JSON for saving/loading effect presets.
- Format: a simple JSON object with nested sections matching the view model structure.
- File extension: `.ksfx` (KernSmith Effects).
- This enables "Save Effects Preset" / "Load Effects Preset" functionality in a future phase.
- Use `System.Text.Json` for serialization — no additional dependencies needed.

---

## Dependencies

| Dependency | Phase | Description |
|------------|-------|-------------|
| Three-panel layout | Phase 60 | Effects panel lives in the right column |
| Font loading | Phase 60 | Font must be loaded before effects are meaningful |
| Preview panel | Phase 64 | Preview reflects applied effects after generation |
| Generation action | Phase 60 | "Generate" button reads `EffectsViewModel` state |
| MonoGame.Extended | NuGet | Shape drawing for custom controls (circle, line, polygon) |

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Custom controls (compass, 2D pad, color picker) are complex to implement without a rich UI framework | High dev time | Build as isolated reusable controls with unit-testable coordinate math. Isolate rendering from logic. The `ColorPickerControl` is the most complex — build it first in Wave 3 and reuse in Waves 4/5. |
| No native color picker in GUM | Must build from scratch | The custom `ColorPickerControl` uses MonoGame `Texture2D` for the spectrum and GUM sliders for channels. The popup pattern (swatch click to expand) keeps the UI compact. |
| MonoGame rendering integration with GUM layout | Custom controls may not participate in GUM's layout system | Custom controls reserve space via a GUM `Container` of fixed size, then draw into that space using `SpriteBatch` in the MonoGame draw loop. The GUM container handles positioning; MonoGame handles rendering. |
| Performance of gradient preview and color spectrum updates | Could feel sluggish if textures are regenerated on every property change | Throttle texture regeneration to max 15fps. Use dirty flags — only regenerate when the relevant property actually changes. Spectrum texture is only 160x120 (19,200 pixels), gradient bar is 200x1 — both are trivially fast to regenerate. |
| Scroll overflow with all sections expanded | Panel might be very tall | GUM `ScrollViewer` handles this. Consider collapsing other sections when one expands (accordion mode) as an optional behavior. |
| Channel configuration complexity overwhelms users | Users may not understand channel packing | Presets handle the common cases. Add descriptive label text. The "Standard" preset is the default. |
| GUM Slider does not support float values natively | Midpoint and opacity need float precision | Use integer sliders (0-100) with conversion logic in the view model (divide/multiply by 100). This is documented in the midpoint and opacity task details. |

## Core Library Notes (Document, Do Not Fix)

These are observations about the KernSmith core library API that may affect UI behavior. They are documented here for awareness — no core library changes should be made as part of this phase.

1. **Gradient angle convention**: KernSmith defines 0 degrees as left-to-right and 90 degrees as top-to-bottom. The compass control must match this convention, not mathematical convention (where 90 is counterclockwise from right). Verify the mapping is correct before shipping.
2. **Effects stack order**: The layered rendering system (Phase 10) composites in fixed order: shadow (z=0) -> outline (z=1) -> body/gradient (z=2). The UI does not need to expose ordering controls, but users should understand this is not configurable.
3. **Channel packing + effects interaction**: When channel packing is enabled, effects like outline and shadow interact differently with the channel compositor. The UI should note this in descriptive labels but does not need to prevent the combination.
4. **SDF + effects compatibility**: SDF mode with outline/shadow/gradient may produce unexpected results since SDF operates on distance fields rather than pixel bitmaps. The UI should warn about this combination (addressed in task 7.11).
5. **WithOutline(int) vs WithOutline(int, byte, byte, byte)**: The single-parameter overload does not set a color (defaults to black). The UI always uses the 4-parameter overload since it has a color picker.
6. **Core library prerequisites**: See Phase 55 for any core library changes or API additions that this phase depends on. If a required API is missing or behaves unexpectedly, document it here rather than modifying the core library in this phase.

## Success Criteria

- [ ] All six effect sections render correctly in the right panel with expand/collapse behavior
- [ ] Each section's enable/disable `CheckBox` correctly dims and disables child controls
- [ ] `CollapsibleSection` custom control works reliably (expand/collapse, enable/disable, reset)
- [ ] `NumericInput` custom control validates input, clamps to range, supports +/- buttons
- [ ] `ColorPickerControl` renders spectrum via MonoGame, supports HSV interaction, hex input, and R/G/B sliders
- [ ] Font style controls (bold, italic, AA, hinting, DPI, height%, super-sampling) bind correctly
- [ ] Outline width slider and color picker produce correct `WithOutline()` parameters
- [ ] Shadow 2D offset pad (`ShadowOffsetPad`) allows click+drag to set X/Y offset visually via MonoGame rendering
- [ ] Shadow blur, color, and opacity controls bind correctly
- [ ] Gradient compass control (`GradientDirectionControl`) allows click+drag to set angle (0-360) via MonoGame rendering
- [ ] Gradient preview bar updates when colors, angle, or midpoint change
- [ ] Gradient presets populate all fields correctly
- [ ] Channel per-channel `ComboBox` controls and invert `CheckBox` controls map to `ChannelConfig` correctly
- [ ] Channel presets populate the table and auto-detect when configuration matches a preset
- [ ] Advanced rendering toggles (SDF, color font, equalize, force offsets, match height) bind correctly
- [ ] "Reset All Effects" restores every section to defaults
- [ ] `ApplyToBuilder()` correctly translates UI state to `BmFontBuilder` calls
- [ ] All custom controls (compass, 2D pad, color picker) support keyboard navigation via `IInputReceiver`
- [ ] All controls have descriptive names for identification
- [ ] Panel scrolls correctly when all sections are expanded simultaneously
- [ ] Custom MonoGame-rendered controls integrate cleanly with GUM layout (fixed-size containers)
