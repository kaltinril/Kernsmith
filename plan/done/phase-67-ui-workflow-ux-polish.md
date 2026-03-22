# Phase 67 — Workflow & UX Polish

> **Status**: Complete
> **Completed**: 2026-03-22. Keyboard shortcuts (Ctrl+O/S/G/Shift+S/+/-/0), engine presets (XNA/Unity/Godot/Unreal/Phaser), pre-generation validation, dynamic window title with dirty tracking, UI scaling via Camera.Zoom, tooltip system (custom built — GUM has none). Command palette and light/dark theme switching deferred as unnecessary.
> **Created**: 2026-03-21
> **Goal**: Refine the user experience with guided workflows, smart defaults, contextual help, and intuitive interaction patterns that make font generation approachable for beginners while powerful for experts.

---

## Design Principles

1. **Guide, don't gatekeep.** The workflow indicator shows the typical path but never locks panels or forces ordering. Every control is reachable at all times.
2. **Zero-config should work.** A user who loads a font and clicks "Generate" with all defaults should get a usable result.
3. **Progressive disclosure.** Show simple controls by default; advanced options are one click away, never hidden behind multiple layers.
4. **Errors are actionable.** Every error message includes what went wrong, why it matters, and what to do about it.
5. **Keyboard-first is optional.** Full mouse/touch operation, but power users can drive everything from the keyboard.

---

## Framework Notes

This phase targets **MonoGame (DesktopGL) + GUM UI (code-only) + MonoGame.Extended**. There is no XAML, no Avalonia, no WPF. All UI is built from GUM Forms controls and GUM primitives (`ContainerRuntime`, `TextRuntime`, `NineSliceRuntime`, `SpriteRuntime`, `ColoredRectangleRuntime`). Theming uses GUM's `ActiveStyles` system. Keyboard input uses MonoGame's `Keyboard.GetState()` / `KeyboardState` in `Game.Update()`.

### Key GUM/MonoGame Patterns Used in This Phase

| Pattern | Implementation |
|---------|----------------|
| Custom tooltip | Floating `ContainerRuntime` with `TextRuntime` children, positioned relative to mouse via `FormsUtilities.Cursor` |
| Custom command palette | Full-screen semi-transparent `ColoredRectangleRuntime` overlay + centered `ContainerRuntime` with `TextBox` and `ListBox` |
| Overlay / modal backdrop | `ColoredRectangleRuntime` stretched to screen size with alpha < 255 |
| Animated transitions | Manual interpolation in `Game.Update()` using delta time on runtime properties (`X`, `Y`, `Width`, `Height`, `Alpha`) |
| Theme switching | Swap `ActiveStyles` list on all runtimes; GUM re-evaluates style-dependent properties automatically |
| Keyboard shortcuts | `Keyboard.GetState()` in `Game.Update()`, tracking previous state for edge detection (pressed this frame) |
| Drag-and-drop | MonoGame DesktopGL `Window.FileDrop` event for OS-level file drops |
| Status bar | Bottom-docked `ContainerRuntime` with `TextRuntime` for message text and `SpriteRuntime` for icon |
| Breadcrumb / sidebar | `ContainerRuntime` with `ChildLayout = ChildLayout.TopToBottomStack` containing step items |

---

## Terminology

| Term | Meaning |
|------|---------|
| Workflow Indicator | The sidebar/breadcrumb showing the six canonical steps |
| Step | One of the six workflow stages (Load, Preview, Characters, Effects, Atlas, Export) |
| Preset | A named bundle of settings targeting a specific game engine or use case |
| Drop Zone | A region of the UI that accepts drag-and-drop files |
| Pre-flight | Validation that runs before generation starts |
| Command Palette | A searchable popup for executing any action by name |
| Quick-fix | A one-click button attached to a warning/error that applies the suggested resolution |
| ActiveStyles | GUM's mechanism for applying named style sets to runtimes at runtime |
| Runtime | A GUM visual element (ContainerRuntime, TextRuntime, ColoredRectangleRuntime, etc.) |

---

## Wave 1 — Guided Workflow (Non-Blocking)

### Overview

A persistent, unobtrusive workflow indicator that shows where the user is in the typical font generation process. It serves as both a progress tracker and a navigation device. It is never modal and never prevents access to any panel. Built entirely from GUM primitives — there is no built-in breadcrumb or stepper control in GUM.

### Workflow Steps Definition

| Step | Label | Icon Texture Region | Panel Target | Completion Condition |
|------|-------|---------------------|--------------|---------------------|
| 1 | Load Font | `icon_file_font` | Font Source panel | A font file is loaded and parsed without error |
| 2 | Preview | `icon_eye` | Preview panel | Preview panel has been viewed at least once after loading |
| 3 | Characters | `icon_grid` | Character Selection panel | At least one character or character set is selected |
| 4 | Effects | `icon_brush` | Effects panel | Effects panel has been viewed (even if no effects are enabled) |
| 5 | Atlas | `icon_texture` | Atlas Settings panel | Atlas settings panel has been viewed AND settings are valid (no validation errors) |
| 6 | Export | `icon_download` | Export panel / action | Generation has completed successfully at least once |

### Step State Machine

Each step has one of four states:

| State | Visual | Meaning |
|-------|--------|---------|
| `NotStarted` | Gray icon + gray label | User has not interacted with this step |
| `InProgress` | Amber icon + amber label, subtle pulse animation | User is currently working in this step's panel |
| `Complete` | Green checkmark icon + green label | Completion condition is met |
| `Error` | Red exclamation icon + red label | Step has a blocking issue (e.g., font failed to load) |

State transitions:

- `NotStarted` -> `InProgress`: User opens the associated panel
- `InProgress` -> `Complete`: Completion condition is satisfied
- `Complete` -> `InProgress`: User returns to the panel and modifies something that invalidates completion (e.g., clears character selection)
- Any -> `Error`: A blocking validation error is detected for this step
- `Error` -> `InProgress`: User returns to fix the issue

### GUM Implementation Details

The workflow indicator is a `ContainerRuntime` with `ChildLayout = ChildLayout.TopToBottomStack` (sidebar) or `ChildLayout = ChildLayout.LeftToRightStack` (breadcrumb). Each step is a child `ContainerRuntime` containing:

```
ContainerRuntime (step item, ChildLayout = LeftToRightStack)
  SpriteRuntime (icon, 24x24, texture region from sprite sheet)
  TextRuntime (label, e.g., "Load Font")
  TextRuntime (status indicator, e.g., checkmark character or empty)
```

State-based styling uses ActiveStyles:

- `"workflow-step-not-started"` — sets icon color to gray, label color to gray
- `"workflow-step-in-progress"` — sets icon color to amber, label color to amber
- `"workflow-step-complete"` — sets icon color to green, label color to green
- `"workflow-step-error"` — sets icon color to red, label color to red

Pulse animation for `InProgress` is driven in `Game.Update()` by oscillating the icon's `Alpha` property between 180 and 255 using a sine wave over 1.5 seconds.

Chevron separators in breadcrumb mode are `TextRuntime` elements displaying ">" between step items.

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 1.1 | Define `WorkflowStep` enum and `WorkflowStepState` enum | Six steps, four states as defined above. Plain C# enums in the UI project. | Low |
| 1.2 | Create `WorkflowTracker` model | Tracks current state of each step, exposes `IReadOnlyList<WorkflowStepInfo>` (record with `Step`, `State`, `Label`). Subscribes to panel navigation events and model changes to auto-update states. No GUM dependency — pure logic. | Medium |
| 1.3 | Build `WorkflowIndicatorRuntime` custom GUM container | Vertical sidebar variant: `ContainerRuntime` with `ChildLayout.TopToBottomStack`, containing step item runtimes. Each step item is a `ContainerRuntime` with `SpriteRuntime` (icon) + `TextRuntime` (label). Expose `SetStepState(step, state)` method that swaps ActiveStyles on the step's children. | Medium |
| 1.4 | Build `WorkflowBreadcrumbRuntime` custom GUM container | Horizontal breadcrumb variant: `ContainerRuntime` with `ChildLayout.LeftToRightStack`. Steps separated by `TextRuntime` chevrons. Same `SetStepState` API as sidebar variant. | Medium |
| 1.5 | Implement sidebar layout option | Workflow indicator docked to the left side of the main window. Collapsed mode: 48px wide (icons only, labels hidden via `Visible = false`). Expanded mode: 180px wide (icons + labels). Toggle button at top uses `SpriteRuntime` with collapse/expand icon. | Medium |
| 1.6 | Wire step click navigation | Attach `Click` event handler to each step item's `ContainerRuntime`. On click, raise a `StepClicked` event that the main screen handles by activating the associated panel. Use GUM's `FormsUtilities.Cursor` for hit testing. | Medium |
| 1.7 | Implement state-based ActiveStyles | Define style categories: `"workflow-not-started"`, `"workflow-in-progress"`, `"workflow-complete"`, `"workflow-error"`. Each style sets `Red`, `Green`, `Blue` on `TextRuntime` and `SpriteRuntime` children. Apply by adding/removing style names from each runtime's `ActiveStyles` list. | Low |
| 1.8 | Create "Getting Started" overlay | Full-screen `ColoredRectangleRuntime` (black, alpha 160) with centered `ContainerRuntime` containing `TextRuntime` elements describing the six steps. Two `Button` controls at the bottom: "Got it" (dismiss) and "Don't show again" (dismiss + set preference). Fade-in/fade-out by interpolating alpha in `Update()`. | Medium |
| 1.9 | Persist "Don't show again" preference | Store in user settings file (`%AppData%/KernSmith/settings.json` on Windows, `~/.config/kernsmith/settings.json` on Linux/macOS). Key: `"showGettingStarted": false`. Use `System.Text.Json` for serialization. | Low |
| 1.10 | Add View menu toggle | View > Workflow Indicator (checked by default). Unchecking sets `WorkflowIndicatorRuntime.Visible = false`. Persisted in settings. Uses the custom menu bar from Phase 65. | Low |
| 1.11 | Implement layout preference persistence | Remember whether user chose sidebar vs. breadcrumb, collapsed vs. expanded. Store in settings alongside other layout state. | Low |
| 1.12 | Animate step transitions | When a step transitions from `InProgress` to `Complete`, play a brief scale animation on the checkmark icon: scale from 0 to 1.2 to 1.0 over 300ms using manual interpolation in `Update()`. Track animation state per step with elapsed time. | Low |
| 1.13 | Add step tooltip on hover | When mouse hovers over a step item for 500ms, show a custom floating `ContainerRuntime` tooltip (see Wave 3 tooltip system). Content: "Step 3: Select Characters — Choose which characters to include in your bitmap font. Status: Complete". | Low |
| 1.14 | Keyboard navigation for indicator | When the workflow indicator has logical focus, arrow keys (Up/Down for sidebar, Left/Right for breadcrumb) move a visual focus ring (`ColoredRectangleRuntime` outline) between steps. Enter activates the focused step. Tracked via index in `Update()` when indicator is focused. | Low |

### Interaction Patterns

**First launch flow:**
1. App opens with empty state. All steps are `NotStarted`.
2. "Getting Started" overlay fades in (alpha 0 to target over 200ms) over the main content area.
3. User reads the overview and clicks "Got it" or "Don't show again".
4. Overlay fades out (200ms alpha interpolation to 0, then `Visible = false`).
5. Workflow indicator on the left shows all steps gray.

**Typical usage flow:**
1. User drags a .ttf file onto the window. Step 1 transitions to `Complete` (green).
2. Preview panel auto-activates. Step 2 transitions to `InProgress` (amber, pulse begins).
3. User views the preview, then clicks "Characters" in the indicator. Step 2 -> `Complete`, Step 3 -> `InProgress`.
4. User selects "ASCII" character set. Step 3 -> `Complete`.
5. User skips Effects (optional). Step 4 remains `NotStarted` — this is fine.
6. User clicks "Generate". Pre-flight runs. If it passes, Step 6 -> `Complete`.

**Expert user flow:**
1. Expert hides the workflow indicator via View menu.
2. Works freely across panels in any order.
3. Workflow state still tracked internally (for pre-flight validation) but not displayed.

---

## Wave 2 — Smart Defaults & Auto-Configuration

### Overview

Engine presets eliminate the need for users to research format requirements for their target platform. Selecting a preset applies a bundle of recommended settings. All settings remain editable after preset application. A "Custom" pseudo-preset means no auto-configuration was applied.

### Preset Definitions

| Preset | Format | Image Format | SDF | Padding | Channel Packing | Super Sampling | Notes |
|--------|--------|-------------|-----|---------|-----------------|----------------|-------|
| Unity (TextMeshPro SDF) | JSON | PNG | Enabled, radius 8 | 9 | Disabled | 1x | TMP expects SDF in alpha channel, RGB white |
| Unity (Legacy) | Text | PNG | Disabled | 2 | Disabled | 1x | Standard Unity bitmap font import |
| Godot 4 | Text | PNG | Disabled | 2 | Disabled | 1x | Godot uses standard BMFont text format |
| Unreal Engine | Text | PNG | Disabled | 2 | Disabled | 1x | UE4/5 bitmap font import |
| MonoGame / FNA | XML | PNG | Disabled | 2 | Disabled | 1x | XNA-compatible BMFont XML |
| Phaser | XML | PNG | Disabled | 2 | Disabled | 1x | Phaser.BitmapFont expects XML |
| LibGDX | Text | PNG | Disabled | 2 | Disabled | 1x | LibGDX uses Hiero-compatible format |
| Cocos2d | Text | PNG | Disabled | 2 | Disabled | 1x | Cocos2d plist-compatible |
| SDF (Generic) | Text | PNG | Enabled, radius 6 | 7 | Disabled | 1x | Generic SDF for any engine |
| MSDF (Generic) | Text | PNG | MSDF, radius 4 | 5 | Disabled | 1x | Multi-channel SDF |
| Channel-Packed (4 fonts) | Binary | PNG | Disabled | 1 | RGBA (4 fonts) | 1x | Pack 4 single-channel fonts into RGBA |
| Custom | — | — | — | — | — | — | No auto-configuration; manual settings |

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 2.1 | Define `EnginePreset` enum | One value per preset defined above, plus `Custom`. | Low |
| 2.2 | Create `PresetDefinition` data class | Properties: `Name`, `Description`, `OutputFormat`, `ImageFormat`, `SdfEnabled`, `SdfRadius`, `Padding`, `ChannelPacking`, `SuperSampling`, `AdditionalNotes`. Immutable record type. | Low |
| 2.3 | Build `PresetRegistry` static class | `GetPreset(EnginePreset) -> PresetDefinition`. Returns the preset configuration. `GetAll() -> IReadOnlyList<PresetDefinition>`. | Low |
| 2.4 | Create preset dropdown using GUM `ComboBox` | GUM Forms `ComboBox` in the toolbar area. Populate `Items` with preset names. On `SelectionChanged`, call `ApplyPreset`. Style the ComboBox items to show preset name text. | Medium |
| 2.5 | Implement `ApplyPreset` logic | Applies all settings from the `PresetDefinition` to the current `FontGeneratorOptions`. Tracks which settings were changed by the preset vs. manually overridden using a `HashSet<string>` of property names. | Medium |
| 2.6 | Add preset override detection | When a user manually changes a setting that was set by a preset, update the ComboBox display text to "Custom (modified from Unity TMP)". Show a small amber dot `ColoredRectangleRuntime` (6x6, rounded via NineSlice) next to the modified setting's label. | Medium |
| 2.7 | Implement "Reset to Defaults" GUM `Button` | Resets all settings to application defaults (not preset defaults). Show a confirmation using a custom dialog overlay: `ColoredRectangleRuntime` backdrop + centered `ContainerRuntime` with message text and two `Button` controls: "Reset" (red-styled) and "Cancel". | Low |
| 2.8 | Implement "Reapply Preset" GUM `Button` | Visible (via `Visible` property) only when a preset has been modified. Re-applies the original preset values, discarding manual overrides. | Low |
| 2.9 | Smart padding auto-suggestion | When outline width changes from 0 to N, show suggestion via an inline `TextRuntime` hint below the padding control: "Recommended padding: 5 (based on outline width)". Clicking the hint text applies the value. Color the hint text with `AccentPrimary` theme token. | Medium |
| 2.10 | Smart texture size suggestion | Calculate estimated atlas size from: `(charCount * (fontSize + padding*2)^2) / packingEfficiency`. Use 0.65 as default packing efficiency estimate. Suggest the next power-of-two that fits. Show as inline `TextRuntime` hint below texture size setting. | Medium |
| 2.11 | Auto-fit toggle enhancement | When "Auto-fit" `CheckBox` is checked, show informational `TextRuntime`: "Estimated atlas size: 1024x512 (auto)". When unchecked, show as suggestion: "Suggested minimum: 1024x512". | Low |
| 2.12 | Preset description tooltip | Hovering over the preset ComboBox shows a custom tooltip (floating `ContainerRuntime`, see Wave 3) with the preset description, a summary of changed settings, and target engine notes. | Low |
| 2.13 | Remember last used preset | Store the last selected preset in user settings JSON. On next launch, pre-select it in the ComboBox (but don't auto-apply — show "Last used: Unity TMP" as placeholder text via a `TextRuntime` overlay on the ComboBox). | Low |
| 2.14 | Preset import/export | Allow saving current settings as a named custom preset (JSON file). Allow importing custom presets. Store in `%AppData%/KernSmith/presets/`. Use `System.Text.Json` serialization. File dialog via MonoGame platform interop or `System.Windows.Forms.OpenFileDialog` on Windows. | Medium |

### Smart Padding Algorithm

```
function suggestPadding(outlineWidth, shadowEnabled, shadowOffsetX, shadowOffsetY, shadowBlur, sdfEnabled, sdfRadius):
    basePadding = 1  // minimum for bilinear filtering

    if outlineWidth > 0:
        basePadding = max(basePadding, outlineWidth + 2)

    if shadowEnabled:
        shadowExtent = max(abs(shadowOffsetX), abs(shadowOffsetY)) + shadowBlur
        basePadding = max(basePadding, shadowExtent + 2)

    if sdfEnabled:
        basePadding = max(basePadding, sdfRadius + 1)

    return basePadding
```

### Smart Texture Size Algorithm

```
function suggestTextureSize(charCount, fontSize, padding, packingEfficiency = 0.65):
    glyphSize = fontSize + (padding * 2)
    totalArea = charCount * glyphSize * glyphSize
    adjustedArea = totalArea / packingEfficiency
    side = ceil(sqrt(adjustedArea))
    // Round up to next power of two
    width = nextPowerOfTwo(side)
    height = nextPowerOfTwo(side)
    // Try half-height if it fits
    if width * (height / 2) >= adjustedArea:
        height = height / 2
    return (width, height)
```

---

## Wave 3 — Contextual Help & Tooltips

### Overview

Every user-facing setting, control, and output message should be self-documenting. Users should never need to leave the application to understand what a setting does. Help ranges from lightweight tooltips to inline hints to detailed error messages with actionable suggestions.

GUM has **no built-in tooltip control**. This wave builds a custom tooltip system from GUM primitives: a floating `ContainerRuntime` with `TextRuntime` children that tracks the mouse cursor position and appears after a configurable hover delay.

### Custom Tooltip Architecture

```
TooltipRuntime : ContainerRuntime
  |-- NineSliceRuntime (background, uses tooltip-bg nine-slice texture)
  |-- ContainerRuntime (content, ChildLayout = TopToBottomStack, padding 8px)
  |     |-- TextRuntime (title, bold via BitmapFont, AccentPrimary color)
  |     |-- ColoredRectangleRuntime (separator line, 1px height, BorderDefault color)
  |     |-- TextRuntime (description, TextPrimary color)
  |     |-- TextRuntime (tip, TextSecondary color, italic font variant)
```

The `TooltipManager` is a singleton that:
1. Tracks which runtime the cursor is over via `FormsUtilities.Cursor` position + hit testing
2. Starts a 500ms hover timer when the cursor enters a runtime that has registered tooltip content
3. On timer expiry, creates/updates the `TooltipRuntime` and positions it near the cursor
4. Keeps the tooltip on-screen by clamping to viewport bounds (`GraphicsDevice.Viewport`)
5. Hides the tooltip immediately when the cursor leaves the source runtime
6. The `TooltipRuntime` is added to a top-level overlay container so it renders above all other UI

### Tooltip Content Schema

Each tooltip follows a consistent structure:

```
[Setting Name]
--------------
What: One-sentence description of what the setting controls.
When: When you might want to change it from the default.
Default: The default value.
Range: Valid range (for numeric settings).
Tip: A practical recommendation.
```

### Setting Tooltip Catalog

| Setting | What | When | Default | Tip |
|---------|------|------|---------|-----|
| Font Size | The size in pixels at which glyphs are rasterized. | Increase for sharper text at larger display sizes. | 32 | For SDF fonts, 42-64px gives good quality at all display sizes. |
| Padding | Extra transparent pixels around each glyph in the atlas. | Increase when using outline or shadow effects to prevent clipping. | 1 | Set to at least outline width + 2 when using outlines. |
| Spacing X | Horizontal spacing added between glyphs in the atlas. | Increase if glyphs visually bleed into neighbors during rendering. | 0 | Usually 0 is fine; increase to 1 if you see artifacts. |
| Spacing Y | Vertical spacing added between glyphs in the atlas. | Same as Spacing X but vertical. | 0 | Usually 0 is fine. |
| Outline Width | Width of the outline effect in pixels. | Enable to add a border around each glyph for readability over varied backgrounds. | 0 (disabled) | Values of 1-3 work well for most font sizes. Remember to increase padding. |
| Outline Color | Color of the outline stroke. | Change for colored outlines or to match your game's art style. | Black (#000000) | Use a contrasting color to the body for maximum readability. |
| Shadow Offset X | Horizontal offset of the drop shadow in pixels. | Adjust for shadow direction. Positive values move right. | 0 | Small values (1-3) create subtle shadows. |
| Shadow Offset Y | Vertical offset of the drop shadow in pixels. | Adjust for shadow direction. Positive values move down. | 0 | Match X offset for a 45-degree shadow. |
| Shadow Blur | Gaussian blur radius applied to the shadow. | Increase for softer, more diffused shadows. | 0 | Values of 2-4 give natural-looking shadows. |
| SDF Enabled | Generate a Signed Distance Field instead of a standard bitmap. | Enable for fonts that need to look sharp at multiple sizes in your game. | Disabled | Required for TextMeshPro in Unity. |
| SDF Radius | The distance field spread in pixels. | Higher values allow smoother outlines/effects in the shader but use more atlas space. | 6 | 4-8 is typical. Higher values need more padding. |
| Super Sampling | Render glyphs at a multiple of the target size, then downsample for antialiasing. | Enable for smoother edges, especially at small font sizes. | 1x (disabled) | 2x is a good balance. 4x is high quality but slow and memory-intensive. |
| Texture Max Width | Maximum width of each atlas page in pixels. | Constrain to your target platform's maximum texture size. | 2048 | Most modern platforms support at least 2048. Mobile may need 1024. |
| Texture Max Height | Maximum height of each atlas page in pixels. | Same as max width. | 2048 | Keep width and height as powers of two for GPU compatibility. |
| Auto-fit | Automatically shrink the atlas to the smallest size that fits all glyphs. | Enable to minimize texture memory usage. | Enabled | Disable if you need a specific texture size for your engine. |
| Output Format | The .fnt file format: Text, XML, JSON, or Binary. | Choose based on your game engine's BMFont parser. | Text | Text is the most widely supported. XML for engines that prefer it. |
| Image Format | Atlas image format: PNG, TGA, or DDS. | Choose based on your engine's texture import pipeline. | PNG | PNG is lossless and universally supported. TGA for legacy pipelines. |

### Status Bar Message Templates

| Trigger | Message | Duration |
|---------|---------|----------|
| Font loaded successfully | "Loaded {fontName} — {glyphCount} glyphs available" | 5 seconds |
| Character set selected | "Selected {count} characters ({setName})" | 3 seconds |
| Effect enabled | "Tip: Increase padding to at least {suggested} when using {effectName}" | 8 seconds |
| Generation started | "Generating bitmap font..." | Until complete |
| Generation succeeded | "Generated {pageCount} atlas page(s) in {time}ms — {charCount} characters" | 10 seconds |
| Generation failed | "Generation failed: {reason}. {suggestion}" | Until dismissed |
| File saved | "Saved to {path}" | 5 seconds |
| Idle (no font loaded) | "Drop a font file here or use File > Open to get started" | Persistent |
| Idle (font loaded, no chars) | "Select characters to include — try the ASCII preset to start" | Persistent |

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 3.1 | Define `TooltipContent` model | Record with `SettingName`, `Description`, `WhenToChange`, `DefaultValue`, `ValidRange`, `Tip`. Plain C# record, no GUM dependency. | Low |
| 3.2 | Create `TooltipRegistry` | Static dictionary mapping setting identifier strings to `TooltipContent`. Populated from code constants. All entries from the catalog above. | Medium |
| 3.3 | Build `TooltipRuntime` custom GUM container | A `ContainerRuntime` subclass containing a `NineSliceRuntime` background, a title `TextRuntime` (bold font), a separator `ColoredRectangleRuntime` (1px), a description `TextRuntime`, and a tip `TextRuntime` (secondary color). Expose `SetContent(TooltipContent)` method. Max width 320px; text wraps via `TextRuntime.Width` + word wrap. | Medium |
| 3.4 | Build `TooltipManager` singleton | Manages a single shared `TooltipRuntime` instance added to the top-level overlay layer. Tracks hover state: which runtime is the cursor over, how long has it hovered. After 500ms hover delay, positions the tooltip near the cursor (offset 16px right, 16px down), clamped to viewport. Hides on cursor exit. Call `TooltipManager.Update(gameTime)` from `Game.Update()`. | Medium |
| 3.5 | Create `TooltipManager.Register(runtime, tooltipContent)` API | Associates a GUM runtime with tooltip content. The manager uses cursor position + runtime bounds to determine hover. Registrations stored in a `Dictionary<IRenderableIpso, TooltipContent>`. | Low |
| 3.6 | Build `InfoIconRuntime` custom GUM element | A small (16x16) `SpriteRuntime` showing a circled "i" icon from the sprite sheet. On hover, triggers `TooltipManager` to show the associated tooltip. On click (for touch), toggles tooltip visibility. Register for tooltip via `TooltipManager.Register()`. | Medium |
| 3.7 | Attach `InfoIconRuntime` to all settings controls | Place an `InfoIconRuntime` next to every setting label in the Effects, Atlas, and Export panels. Each is registered with its `TooltipContent` from the registry. Positioned using GUM layout (right-aligned in the label container). | Medium |
| 3.8 | Build `StatusBarRuntime` custom GUM container | Docked to bottom of main window (`ContainerRuntime` with `YOrigin = Bottom`, `YUnits = PixelsFromLarge`, `Height = 28`). Contains: `SpriteRuntime` (status icon, 16x16), `TextRuntime` (message text), optional `Button` (action button, right-aligned). Background is a `ColoredRectangleRuntime` using `BackgroundTertiary` theme color. | Medium |
| 3.9 | Create `StatusBarService` singleton | Methods: `ShowInfo(message, duration)`, `ShowWarning(message, duration)`, `ShowError(message, suggestion, action)`, `ShowSuccess(message, duration)`, `Clear()`. Manages a queue of messages; displays the most recent. Auto-dismiss uses elapsed time tracking in `Update(gameTime)`. Swaps icon texture region and message text on the `StatusBarRuntime`. | Medium |
| 3.10 | Wire status bar to font loading | Show "Loaded {fontName}" on successful load (swap icon to success, green tint). Show error message with suggestion on failed load (e.g., "Could not load font: unsupported format. Try a .ttf or .otf file." with error icon, red tint). | Low |
| 3.11 | Wire status bar to generation | Show progress during generation. Show success with stats on completion. Show actionable error on failure with quick-fix button. | Low |
| 3.12 | Wire status bar to idle tips | When no font is loaded and app has been idle for 5 seconds (tracked via elapsed time), show the idle tip. When font is loaded but no characters selected and idle for 5 seconds, show the character tip. | Low |
| 3.13 | Build `ErrorMessagePanelRuntime` custom GUM container | Displayed inline (not modal) when generation fails. `ContainerRuntime` with: `SpriteRuntime` (error icon), `TextRuntime` (error title, bold), `TextRuntime` (detailed description), `TextRuntime` (suggested fix, secondary color), optional `Button` (quick-fix). Close button (`Button` with "X" text) in top-right corner. Background uses `ErrorColor` at 10% alpha. | Medium |
| 3.14 | Define error message templates | Create a catalog of all known error conditions as `ErrorTemplate` records: `ErrorCode`, `Title`, `Description`, `Suggestion`, `QuickFixAction` (nullable `Action`). See error catalog table below. | Medium |
| 3.15 | Implement warning indicators on settings | Small amber triangle `SpriteRuntime` (16x16) next to settings that may cause issues. Appears dynamically by setting `Visible = true` based on current configuration. Register with `TooltipManager` for hover explanation. | Medium |
| 3.16 | Warning: large char set + small texture | When `charCount > estimatedCapacity(textureSize)`, show warning triangle next to texture size setting. Tooltip: "Current texture size may be too small for {charCount} characters at size {fontSize}. Estimated minimum: {suggested}." | Low |
| 3.17 | Warning: effects + zero padding | When any effect is enabled and padding is 0, show warning triangle next to padding setting. Tooltip: "Padding of 0 may cause {effectName} to be clipped. Recommended: {suggested}." | Low |
| 3.18 | Warning: high super-sampling + large atlas | When super-sampling >= 4x and estimated atlas >= 2048, show warning triangle next to super-sampling setting. Tooltip: "4x super-sampling with a large atlas will be slow and memory-intensive. Consider 2x for faster generation." | Low |

### Error Message Catalog

| Error Code | Title | Description | Suggestion | Quick-Fix |
|------------|-------|-------------|------------|-----------|
| `ERR_NO_FONT` | No font loaded | Cannot generate without a font file. | Load a font file first using File > Open or drag-and-drop. | Open file dialog |
| `ERR_NO_CHARS` | No characters selected | Cannot generate a bitmap font with no characters. | Select at least one character set or add individual characters. | Select ASCII |
| `ERR_ATLAS_TOO_SMALL` | Atlas too small | All glyphs do not fit in a {width}x{height} atlas. | Increase maximum texture size or reduce character count. | Set to {suggested} |
| `ERR_FONT_PARSE` | Font parsing error | The font file could not be parsed: {detail}. | Ensure the file is a valid .ttf, .otf, or .woff font. | None |
| `ERR_FREETYPE` | Rasterization error | FreeType failed to render glyph U+{codepoint}: {detail}. | This glyph may be missing from the font. Try excluding it. | Exclude glyph |
| `ERR_OUT_OF_MEMORY` | Out of memory | Not enough memory to generate the atlas at current settings. | Reduce texture size, character count, or super-sampling level. | Set super-sampling to 1x |
| `ERR_WRITE_FAILED` | File write error | Could not write to {path}: {detail}. | Check that the directory exists and you have write permission. | Choose different path |
| `ERR_INVALID_RANGE` | Invalid character range | The character range {start}-{end} is invalid. | Ensure start <= end and both are valid Unicode codepoints. | None |

---

## Wave 4 — Drag-and-Drop

### Overview

Drag-and-drop is a primary interaction pattern. MonoGame DesktopGL supports OS-level file drops via the `Window.FileDrop` event. This provides the file paths of dropped files. Unlike Avalonia's per-control DragDrop, MonoGame's `FileDrop` fires at the window level — the app must determine the drop target by checking the cursor position against UI element bounds at the time of the drop.

### MonoGame FileDrop Architecture

```csharp
// In Game.Initialize() or similar:
Window.FileDrop += OnFileDrop;

private void OnFileDrop(object? sender, FileDropEventArgs e)
{
    string[] filePaths = e.Files;
    Point cursorPos = Mouse.GetState().Position;

    // Determine which drop zone the cursor is over
    var dropZone = DropZoneManager.GetZoneAtPosition(cursorPos);
    if (dropZone != null)
    {
        dropZone.HandleDrop(filePaths);
    }
}
```

The `DropZoneManager` maintains a list of registered drop zones, each defined by a GUM runtime's screen bounds and accepted file extensions. Hit testing uses the runtime's `GetAbsoluteX/Y/Width/Height` methods.

### Drop Zone Definitions

| Drop Zone | GUM Runtime | Accepted Types | Action | Feedback |
|-----------|-------------|---------------|--------|----------|
| Main window (background) | Root container | `.ttf`, `.otf`, `.woff`, `.woff2` | Load as font source | "Drop to load font" overlay |
| Main window (background) | Root container | `.bmfc` | Open as project file | "Drop to open project" overlay |
| Main window (background) | Root container | `.fnt` | Open in BMFont reader/inspector | "Drop to inspect .fnt file" overlay |
| Character panel | Character panel container | `.txt` | Read file contents, add all characters found | "Drop to import characters from text file" overlay |
| Custom glyph area | Custom glyph container | `.png`, `.bmp`, `.tga` | Add as custom glyph image | "Drop to add custom glyph" overlay |
| Preset area | Preset panel container | `.json` (preset) | Import as custom engine preset | "Drop to import preset" overlay |

### Drop Overlay Visual

Since MonoGame's `FileDrop` event fires instantly on drop (no drag-enter/drag-over preview), the drop overlay serves as **post-drop feedback** rather than a live preview. When files are dropped:

1. Validate file extension against the drop zone's accepted types
2. If valid: briefly flash a success overlay (green-tinted `ColoredRectangleRuntime`, 300ms fade-out) with message text
3. If invalid: briefly flash an error overlay (red-tinted `ColoredRectangleRuntime`, 500ms) with "Unsupported file type" message
4. Process the file

For platforms that support drag-enter events (Windows via P/Invoke `IDropTarget`), an optional enhanced mode can show a live overlay during drag. This is a stretch goal documented in task 4.13.

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 4.1 | Create `DropZone` model class | Properties: `Id` (string), `AcceptedExtensions` (HashSet<string>), `DropHandler` (Action<string[]>), `OverlayMessage` (string), `SourceRuntime` (IRenderableIpso, for bounds). Method: `Accepts(string filePath) -> bool` checks extension. | Low |
| 4.2 | Build `DropZoneManager` singleton | Maintains `List<DropZone>`. Methods: `Register(DropZone)`, `Unregister(string id)`, `GetZoneAtPosition(Point cursorPos) -> DropZone?` (hit tests cursor against runtime bounds, returns most specific/smallest zone). | Medium |
| 4.3 | Wire `Window.FileDrop` event | In `Game.Initialize()`, subscribe to `Window.FileDrop`. On drop, get cursor position from `Mouse.GetState()`, call `DropZoneManager.GetZoneAtPosition()`, validate file extensions, call handler or show error feedback. | Medium |
| 4.4 | Build `DropFeedbackOverlay` runtime | A `ContainerRuntime` containing `ColoredRectangleRuntime` (background tint) + `SpriteRuntime` (icon) + `TextRuntime` (message). Shown briefly on drop, fades out via alpha interpolation in `Update()`. Two variants: success (green tint) and error (red tint). Added to top-level overlay layer. | Medium |
| 4.5 | Register main window font drop zone | Register root container as a drop zone accepting `.ttf`, `.otf`, `.woff`, `.woff2`. Handler calls the existing font loading pipeline. Show success/error feedback via `DropFeedbackOverlay`. | Low |
| 4.6 | Register main window project drop zone | Register root container as a drop zone accepting `.bmfc`. Handler calls project open logic. If current project has unsaved changes, show save prompt dialog first. Lower priority than font drop zone (checked second). | Medium |
| 4.7 | Register main window .fnt inspection drop | Register root container accepting `.fnt`. Handler parses with `BmFontReader` and opens the inspection/preview panel. Lower priority than font and project zones. | Medium |
| 4.8 | Register character panel text file drop | Register character panel container accepting `.txt`. Handler reads file as UTF-8, extracts unique characters, adds to current selection. Status bar shows "Added {count} characters from {filename}". | Low |
| 4.9 | Register custom glyph image drop | Register custom glyph container accepting `.png`, `.bmp`, `.tga`. Handler prompts for codepoint assignment via a small dialog overlay (TextBox for codepoint + OK/Cancel buttons). Load and preview the image in the custom glyph list. | Medium |
| 4.10 | Register preset import drop | Register preset area accepting `.json`. Validate JSON schema matches `PresetDefinition`. On success, add to custom presets list and show success feedback. | Low |
| 4.11 | Multi-file drop support | When multiple font files are dropped, load the first one. Show status bar message: "Loaded {first}. Multiple font merging is available in the Font Sources panel." Ignore additional files. | Low |
| 4.12 | Drop zone visual styling | Define consistent visual constants for drop feedback overlays: background alpha = 80, border thickness = 2 (rendered as `ColoredRectangleRuntime` frame), icon size = 48px, text uses 16px font. Success uses `SuccessColor`, error uses `ErrorColor`. | Low |
| 4.13 | (Stretch) Live drag-over preview on Windows | Use P/Invoke to implement `IDropTarget` COM interface on the MonoGame window's HWND. On `DragEnter`/`DragOver`, show a live drop overlay with dashed border. On `DragLeave`, hide it. This is Windows-only and optional. Document the approach for Linux/macOS equivalents. | High |
| 4.14 | Accessibility for drop zones | Ensure every drop zone has a keyboard alternative. Every drop zone must have an adjacent `Button` or menu item that opens a file picker for the same action (e.g., "Browse..." button next to font source, "Import..." button next to character panel). | Low |

### Interaction Patterns

**Font drag-and-drop flow:**
1. User drags a `.ttf` file from their file manager and drops it on the KernSmith window.
2. `Window.FileDrop` event fires with the file path.
3. `DropZoneManager` finds the main window font drop zone (cursor is over root container).
4. Extension `.ttf` matches accepted types.
5. Success feedback overlay flashes briefly (green tint, "Loading font..." text, 300ms).
6. Font loading pipeline runs.
7. On success: status bar shows "Loaded Roboto Regular — 1,294 glyphs available". Step 1 -> Complete.
8. On failure: error message panel appears inline with actionable message.

**Invalid file drop flow:**
1. User drops a `.jpg` file on the main window.
2. `DropZoneManager` finds the main window drop zone but `.jpg` is not in accepted extensions.
3. Error feedback overlay flashes briefly (red tint, "Unsupported file type. Expected: .ttf, .otf, .woff" text, 500ms).
4. No action taken.

---

## Wave 5 — Validation & Pre-flight Checks

### Overview

Validation happens at three stages: real-time (as settings change), pre-generation (when the user clicks "Generate"), and post-generation (after output is produced). Real-time validation provides immediate feedback via warning indicators. Pre-generation validation is a blocking check that prevents wasted time. Post-generation validation reports quality and completeness.

### Validation Rule Definitions

| Rule ID | Stage | Severity | Condition | Message | Quick-Fix |
|---------|-------|----------|-----------|---------|-----------|
| `VAL_NO_FONT` | Pre-gen | Error | No font loaded | No font loaded. Load a font file to generate. | Open file dialog |
| `VAL_NO_CHARS` | Pre-gen | Error | Character set empty | No characters selected. Select at least one character set. | Select ASCII |
| `VAL_ATLAS_OVERFLOW` | Pre-gen | Error | Estimated area > max texture area | Estimated glyph area exceeds maximum atlas size. | Suggest larger size |
| `VAL_PADDING_LOW` | Real-time | Warning | Effect active && padding < suggestedPadding | Padding may be too low for {effect}. Recommended: {suggested}. | Set padding |
| `VAL_SUPERSAMPLE_SLOW` | Real-time | Warning | superSampling >= 4 && estimatedArea > 2048^2 | High super-sampling with large atlas will be slow. | Set to 2x |
| `VAL_SDF_NO_PADDING` | Real-time | Warning | SDF enabled && padding < sdfRadius | SDF radius exceeds padding. Distance field will be clipped. | Set padding = radius + 1 |
| `VAL_TEXTURE_NOT_POT` | Real-time | Info | width or height not power of two | Texture size is not a power of two. Some platforms require power-of-two textures. | Round to nearest POT |
| `VAL_LARGE_CHARSET` | Real-time | Info | charCount > 5000 | Large character set ({count} chars). Generation may take several seconds. | None |
| `VAL_MISSING_GLYPHS` | Post-gen | Warning | Some codepoints had no glyph in the font | {count} characters had no glyph in the font and were skipped: {list}. | Remove from selection |
| `VAL_PACKING_WASTE` | Post-gen | Info | packing efficiency < 50% | Atlas packing efficiency is {pct}%. Consider reducing texture size or enabling auto-fit. | Enable auto-fit |
| `VAL_NO_KERNING` | Post-gen | Info | Kerning requested but no pairs found | No kerning pairs found in the font. The .fnt file will have no kerning data. | None |
| `VAL_MULTI_PAGE` | Post-gen | Info | Output has > 1 atlas page | Output spans {count} atlas pages. Some engines only support single-page fonts. | Increase texture size |

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 5.1 | Define `ValidationRule` base class | Properties: `RuleId`, `Stage` (enum: RealTime, PreGeneration, PostGeneration), `Severity` (enum: Error, Warning, Info), `Check(context) -> ValidationResult?`. Abstract base class. | Low |
| 5.2 | Define `ValidationResult` record | Properties: `RuleId`, `Severity`, `Message` (formatted string), `QuickFixAction` (nullable `Action`), `QuickFixLabel` (nullable string), `RelatedSettingId` (nullable, for navigation). | Low |
| 5.3 | Implement `ValidationEngine` | Maintains a list of `ValidationRule` instances. Methods: `RunRealTime(context) -> List<ValidationResult>`, `RunPreGeneration(context) -> List<ValidationResult>`, `RunPostGeneration(context, result) -> List<ValidationResult>`. Pure logic, no GUM dependency. | Medium |
| 5.4 | Implement all real-time rules | `VAL_PADDING_LOW`, `VAL_SUPERSAMPLE_SLOW`, `VAL_SDF_NO_PADDING`, `VAL_TEXTURE_NOT_POT`, `VAL_LARGE_CHARSET`. Called when any setting changes. Results update warning indicators (Wave 3). | Medium |
| 5.5 | Implement all pre-generation rules | `VAL_NO_FONT`, `VAL_NO_CHARS`, `VAL_ATLAS_OVERFLOW`. Called when user clicks Generate. If any Error-severity results, block generation and show results. | Medium |
| 5.6 | Implement all post-generation rules | `VAL_MISSING_GLYPHS`, `VAL_PACKING_WASTE`, `VAL_NO_KERNING`, `VAL_MULTI_PAGE`. Called after generation completes. Results shown in validation panel. | Medium |
| 5.7 | Build `ValidationPanelRuntime` custom GUM container | Dockable panel (a `ContainerRuntime` with `ChildLayout.TopToBottomStack`) showing a scrollable list of validation results. Each row is a `ContainerRuntime` containing: `SpriteRuntime` (severity icon — red circle, amber triangle, blue "i"), `TextRuntime` (message), optional `Button` (quick-fix). Click a row to navigate to the related setting. Use GUM `ScrollViewer` (or `ContainerRuntime` with `ClipsChildren = true` + manual scroll offset) for long lists. | Medium |
| 5.8 | Implement validation result navigation | Clicking a validation result that has `RelatedSettingId` navigates to that setting's panel (activates the tab/panel containing it). Brief highlight: set the setting label's `TextRuntime` color to `WarningColor` for 1 second, then interpolate back to normal over 500ms. Tracked via animation state in `Update()`. | Medium |
| 5.9 | Implement quick-fix execution | Quick-fix buttons execute their associated `Action` (e.g., set padding to suggested value). After execution, re-run real-time validation to update the list. Show brief status bar confirmation: "Padding set to 5". | Low |
| 5.10 | Auto-dismiss resolved results | When a real-time validation result is resolved (e.g., user increases padding), remove it from the validation panel. Animate removal by interpolating the row's `Height` from current to 0 over 200ms, then remove the runtime. | Low |
| 5.11 | Pre-generation blocking inline errors | When pre-generation validation finds errors: show an inline `ContainerRuntime` error summary above the Generate button. List each error with its quick-fix button. Generate button's `IsEnabled = false` until all errors are resolved. Re-run validation when settings change to auto-clear. | Medium |
| 5.12 | Pre-generation warning confirmation | When pre-generation validation finds only warnings (no errors): show a confirmation dialog overlay (`ColoredRectangleRuntime` backdrop + centered container). Message: "There are {count} warnings. Generate anyway?" Two `Button` controls: "Generate Anyway" and "Review Warnings". | Low |
| 5.13 | Post-generation summary notification | After generation, if there are post-gen validation results, show a non-blocking status bar message: "{count} items to review" with an action button that activates the validation panel. | Low |
| 5.14 | Validation badge on panel tab | The Validation panel's tab header (if using tabbed layout) shows a badge — a small `ContainerRuntime` circle with `TextRuntime` count text overlaid on the tab. Background color matches highest severity: `ErrorColor` (errors), `WarningColor` (warnings), `InfoColor` (info only). | Low |
| 5.15 | Persist validation preferences | Allow users to suppress specific info-level rules via a context menu or settings. Store suppressions in settings JSON as `"suppressedValidationRules": ["VAL_TEXTURE_NOT_POT"]`. Warning and error rules cannot be suppressed. | Low |

### Pre-flight Interaction Pattern

```
User clicks "Generate"
    |
    v
Run pre-generation validation
    |
    +-- Errors found? -----> Show inline error list above Generate button
    |                         Generate button stays disabled
    |                         User fixes errors (or uses quick-fixes)
    |                         Errors auto-clear as resolved
    |                         User clicks Generate again
    |
    +-- Warnings only? ----> Show confirmation dialog overlay
    |                         "Generate Anyway" -> proceed
    |                         "Review Warnings" -> open validation panel
    |
    +-- Clean? ------------> Proceed to generation
                              Show progress in status bar
                              On complete, run post-generation validation
                              If post-gen results: show badge on validation tab
```

---

## Wave 6 — Keyboard Shortcuts & Power User Features

### Overview

Power users should be able to drive the entire application from the keyboard. This includes a command palette for discoverability, comprehensive keyboard shortcuts for common actions, and keyboard navigation through all panels and controls.

### MonoGame Keyboard Input Architecture

MonoGame provides `Keyboard.GetState()` which returns the current state of all keys. To detect key presses (not holds), the shortcut system tracks both current and previous `KeyboardState` and fires shortcuts on the frame a key transitions from released to pressed.

```csharp
// In Game.Update():
var currentKeyboard = Keyboard.GetState();

foreach (var shortcut in ShortcutRegistry.GetAll())
{
    if (shortcut.IsPressed(currentKeyboard, _previousKeyboard))
    {
        if (!IsTextInputFocused() || shortcut.IsGlobal)
        {
            shortcut.Execute();
        }
    }
}

_previousKeyboard = currentKeyboard;
```

Modifier detection:
- `Ctrl`: `currentKeyboard.IsKeyDown(Keys.LeftControl) || currentKeyboard.IsKeyDown(Keys.RightControl)`
- `Shift`: `currentKeyboard.IsKeyDown(Keys.LeftShift) || currentKeyboard.IsKeyDown(Keys.RightShift)`
- `Alt`: `currentKeyboard.IsKeyDown(Keys.LeftAlt) || currentKeyboard.IsKeyDown(Keys.RightAlt)`

Edge detection (pressed this frame, not last frame):
- `currentKeyboard.IsKeyDown(key) && !previousKeyboard.IsKeyDown(key)`

### Keyboard Shortcut Map

| Shortcut | Action | Context |
|----------|--------|---------|
| `Ctrl+O` | Open font file | Global |
| `Ctrl+Shift+O` | Open project file (.bmfc) | Global |
| `Ctrl+S` | Save project | Global |
| `Ctrl+Shift+S` | Save project as | Global |
| `Ctrl+E` | Export / Generate | Global |
| `Ctrl+Shift+E` | Export with options dialog | Global |
| `Ctrl+Z` | Undo | Global |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo | Global |
| `Ctrl+Shift+P` | Open command palette | Global |
| `Ctrl+,` | Open settings / preferences | Global |
| `F5` | Generate (same as Ctrl+E) | Global |
| `F6` | Toggle preview panel | Global |
| `F7` | Toggle validation panel | Global |
| `Ctrl+1` | Navigate to Font Source panel | Global |
| `Ctrl+2` | Navigate to Preview panel | Global |
| `Ctrl+3` | Navigate to Characters panel | Global |
| `Ctrl+4` | Navigate to Effects panel | Global |
| `Ctrl+5` | Navigate to Atlas Settings panel | Global |
| `Ctrl+6` | Navigate to Export panel | Global |
| ~~`Ctrl+0`~~ | ~~Navigate to Validation panel~~ | ~~Global~~ | _Removed — redundant with `F7` (Toggle validation panel). `Ctrl+0` is reserved for "Reset preview zoom" in Preview context._ |
| `Ctrl+A` | Select all characters | Characters panel |
| `Ctrl+Shift+A` | Deselect all characters | Characters panel |
| `Ctrl+F` | Search/filter characters | Characters panel |
| `Ctrl+Plus` | Increase preview font size | Preview panel |
| `Ctrl+Minus` | Decrease preview font size | Preview panel |
| `Ctrl+0` (in preview) | Reset preview zoom | Preview panel |
| `Ctrl+Shift+D` | Toggle dark/light theme | Global |
| `Escape` | Close overlay / dismiss dialog / close command palette | Global |
| `Ctrl+W` | Close current tab/panel | Global |
| `F1` | Open help / keyboard shortcuts reference | Global |

### Command Palette Commands

| Command | Aliases | Action |
|---------|---------|--------|
| Open Font File | load font, import font | Open file dialog for font |
| Generate Bitmap Font | export, build, render | Run generation |
| Select ASCII Characters | ascii | Select the ASCII character set |
| Select Latin Extended | latin | Select Latin Extended character set |
| Select All Characters | all chars | Select every available glyph |
| Clear Character Selection | deselect, clear chars | Remove all character selections |
| Set Font Size... | size, pt | Show input for font size |
| Toggle SDF | sdf on, sdf off | Toggle SDF mode |
| Set SDF Radius... | sdf radius, spread | Show input for SDF radius |
| Toggle Outline | outline on, outline off | Toggle outline effect |
| Set Outline Width... | outline size, border | Show input for outline width |
| Toggle Shadow | shadow on, shadow off | Toggle shadow effect |
| Set Padding... | padding, pad | Show input for padding value |
| Apply Preset: Unity TMP | unity tmp, textmeshpro | Apply Unity TMP preset |
| Apply Preset: Godot | godot | Apply Godot preset |
| Apply Preset: MonoGame | monogame, fna, xna | Apply MonoGame preset |
| Reset All Settings | defaults, reset | Reset to default settings |
| Toggle Dark Theme | dark mode, light mode, theme | Switch theme |
| Show Keyboard Shortcuts | keys, hotkeys, shortcuts | Open shortcut reference |
| Toggle Workflow Indicator | workflow, sidebar, breadcrumb | Show/hide workflow indicator |
| Open Validation Panel | validate, warnings, errors | Open the validation panel |
| Zoom In Preview | zoom in, bigger | Increase preview magnification |
| Zoom Out Preview | zoom out, smaller | Decrease preview magnification |

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 6.1 | Define `KeyboardShortcut` model | Properties: `Key` (MonoGame `Keys` enum), `Modifiers` (flags: Ctrl, Shift, Alt), `ActionId` (string), `DisplayLabel` (e.g., "Ctrl+O"), `Description`, `Context` (Global or panel-specific), `Execute` (Action). Method: `IsPressed(KeyboardState current, KeyboardState previous) -> bool` using edge detection and modifier checks. | Low |
| 6.2 | Create `ShortcutRegistry` | Registers all shortcuts from the map above. Provides lookup by key combination and by action ID. Methods: `Register(KeyboardShortcut)`, `GetByKeys(Keys key, Modifiers mods) -> KeyboardShortcut?`, `GetByActionId(string id) -> KeyboardShortcut?`, `GetAll() -> IReadOnlyList<KeyboardShortcut>`. Detects conflicts (two actions with same key combo) at registration time. | Medium |
| 6.3 | Build `ShortcutManager` service | Called from `Game.Update()` with current and previous `KeyboardState`. Iterates all registered shortcuts, checks `IsPressed()`, respects context (only fire panel-specific shortcuts when that panel is active). Maintains a `_previousKeyboard` field. Checks `IsTextInputFocused()` to avoid stealing keys from GUM `TextBox` controls. | Medium |
| 6.4 | Implement `IsTextInputFocused()` check | Query GUM's `FormsUtilities` or the current `FocusManager` to determine if a `TextBox` or similar text-input control has focus. When text input is focused, suppress non-global shortcuts. Global shortcuts (Ctrl+Shift+P, Escape) always fire. Ctrl+A fires text-select in TextBox context, not character-select. | Medium |
| 6.5 | Build `CommandPaletteRuntime` custom GUM overlay | Full-screen `ColoredRectangleRuntime` backdrop (black, alpha 25). Centered `ContainerRuntime` (500px wide, max 400px tall) with: GUM `TextBox` at top (search input, auto-focused), `ListBox` below (filtered command list). `NineSliceRuntime` background for the palette container. Escape key or backdrop click closes. | High |
| 6.6 | Implement fuzzy matching for command palette | Match against command name and all aliases. Score by: exact prefix match (100) > word start match (75) > substring match (50) > fuzzy character match (25). Sort results by score descending. Highlight matched characters in `ListBox` items by using different `TextRuntime` segments with `AccentPrimary` color for matched chars and `TextPrimary` for unmatched. | Medium |
| 6.7 | Implement command palette parameter input | Commands ending in "..." (e.g., "Set Font Size...") replace the command list with a single `TextBox` showing the prompt label ("Font Size:") and pre-filled current value. Enter applies the value (with validation), Escape returns to command list. Use a `TextRuntime` label + `TextBox` input in the palette container. | Medium |
| 6.8 | Build keyboard shortcuts reference panel | Accessed via F1 or Help menu. A modal overlay with a scrollable `ContainerRuntime` (TopToBottomStack) listing all shortcuts. Each row: `TextRuntime` (shortcut label, e.g., "Ctrl+O", monospace font, `AccentPrimary` color) + `TextRuntime` (description). Grouped by context ("Global", "Preview", "Characters") with section header `TextRuntime` elements. Searchable via `TextBox` at top. | Medium |
| 6.9 | Implement panel navigation shortcuts | `Ctrl+1` through `Ctrl+6` activate the corresponding panel. If panels are in a tab container, set the active tab index. If in a dock layout, bring the panel's container to front and make visible. Validation panel is toggled via `F7` (no `Ctrl+0` mapping — see shortcut table). | Low |
| 6.15 | Populate `CommandRegistry` with all commands and wire action delegates | Register every command from the Command Palette Commands table into the `CommandRegistry` at startup. Each registration wires the command's `Execute` delegate to the corresponding application action (e.g., "Open Font File" calls the file dialog service, "Toggle SDF" toggles `FontGeneratorOptions.EnableSdf`). Commands with parameters ("Set Font Size...") wire a delegate that opens the parameter input flow. This ensures the command palette and shortcuts both resolve to the same action handlers. | Medium |
| 6.10 | Implement focus management | Define a logical focus order for controls in each panel. When Tab is pressed (detected via `KeyboardState`), advance focus to the next GUM Forms control. Show a focus ring: a `ColoredRectangleRuntime` outline (2px, `AccentPrimary` color) around the focused control. Hide focus ring on mouse click (track input method). | Medium |
| 6.11 | Implement Ctrl+A / Ctrl+Shift+A in Characters panel | Select all / deselect all characters. Only active when Characters panel is the active panel and no `TextBox` has focus. Show count in status bar: "Selected {count} characters" or "Cleared character selection". | Low |
| 6.12 | Implement preview zoom shortcuts | `Ctrl+Plus` increases preview font size by 4px (or zoom level by 25%). `Ctrl+Minus` decreases. `Ctrl+0` resets to default. Only active when Preview panel is active. Show current zoom/size in status bar. Note: MonoGame `Keys.OemPlus` and `Keys.OemMinus` for these keys. | Low |
| 6.13 | Implement shortcut customization (future-prep) | Design the `ShortcutRegistry` to support user overrides loaded from settings JSON. Schema: `"shortcuts": { "open-font": "Ctrl+O" }`. Do not implement the UI for customization in this phase, but ensure the data model supports loading custom shortcuts. | Low |
| 6.14 | Menu item shortcut labels | All custom menu items (from Phase 65 custom menu bar) show the shortcut text right-aligned in the menu item `TextRuntime`. Pull display labels from `ShortcutRegistry` so they stay in sync. Right-align using a second `TextRuntime` with `XOrigin = Right`, `XUnits = PixelsFromLarge`. | Low |

### Command Palette Interaction Pattern

```
1. User presses Ctrl+Shift+P
   - Detected via edge detection in ShortcutManager.Update()
2. Command palette overlay appears (full-screen backdrop + centered palette container)
   - Backdrop: ColoredRectangleRuntime, black at alpha 25
   - Palette: ContainerRuntime, 500px wide, NineSliceRuntime background
   - TextBox is auto-focused (GUM FocusManager)
   - ListBox shows all commands (scrollable)
3. User starts typing "sdf"
   - On TextBox.TextChanged, run fuzzy match against all commands
   - ListBox items filtered and re-sorted by score
   - Matches: "Toggle SDF", "Set SDF Radius..."
   - Matched characters highlighted via AccentPrimary-colored TextRuntime segments
4. User presses Down Arrow to select "Set SDF Radius..."
   - ListBox.SelectedIndex increments
   - Selected item highlighted with AccentPrimary background
5. User presses Enter
   - Command list replaced by parameter input: TextRuntime "SDF Radius:" + TextBox "[6]"
   - Current value pre-filled and selected
6. User types "8" and presses Enter
   - Value validated (must be positive integer)
   - SDF radius set to 8
   - Palette closes (Visible = false, backdrop Visible = false)
   - Status bar shows "SDF radius set to 8"
7. If user presses Escape at any point, palette closes with no action
```

---

## Wave 7 — Theme & Appearance

### Overview

The application supports light and dark themes using GUM's `ActiveStyles` system. All custom controls define style-dependent properties (colors, fonts) via named styles. Switching themes replaces the active style name on all runtimes. There is no XAML, no FluentTheme — theming is purely code-driven through GUM's style mechanism.

### GUM ActiveStyles Theming Architecture

GUM's `ActiveStyles` system works by allowing each runtime to have a list of active style names. When a style name is active, any properties defined for that style override the runtime's default values. Theme switching is accomplished by:

1. Defining two style sets in code: `"theme-light"` and `"theme-dark"`
2. All runtimes that have theme-dependent properties include both style definitions
3. On theme switch, iterate all runtimes and swap `"theme-light"` for `"theme-dark"` (or vice versa) in their `ActiveStyles` list
4. GUM automatically re-evaluates and applies the new style's property values

```csharp
// Example: defining theme styles on a TextRuntime
var label = new TextRuntime();
label.SetProperty("Red", 26);   // default (light theme text)
label.SetProperty("Green", 26);
label.SetProperty("Blue", 26);

// Define dark theme style override
var darkStyle = new StateSave { Name = "theme-dark" };
darkStyle.Variables.Add(new VariableSave { Name = "Red", Value = 224 });
darkStyle.Variables.Add(new VariableSave { Name = "Green", Value = 224 });
darkStyle.Variables.Add(new VariableSave { Name = "Blue", Value = 224 });
label.Component.States.Add(darkStyle);

// To switch to dark theme:
label.ActiveStyles.Add("theme-dark");
// To switch back to light:
label.ActiveStyles.Remove("theme-dark");
```

For efficiency, the `ThemeManager` maintains a registry of all theme-aware runtimes. When the theme changes, it iterates the registry and updates `ActiveStyles` on each runtime.

### Theme Color Tokens

| Token | Light Value (RGB) | Dark Value (RGB) | Usage |
|-------|-------------------|------------------|-------|
| `BackgroundPrimary` | `255, 255, 255` | `30, 30, 30` | Main window background |
| `BackgroundSecondary` | `245, 245, 245` | `37, 37, 38` | Panel backgrounds |
| `BackgroundTertiary` | `232, 232, 232` | `51, 51, 51` | Toolbar, status bar |
| `TextPrimary` | `26, 26, 26` | `224, 224, 224` | Primary text |
| `TextSecondary` | `102, 102, 102` | `160, 160, 160` | Secondary/hint text |
| `AccentPrimary` | `0, 120, 212` | `76, 194, 255` | Buttons, links, focus rings |
| `AccentHover` | `0, 108, 190` | `96, 207, 255` | Button hover state |
| `BorderDefault` | `208, 208, 208` | `64, 64, 64` | Control borders |
| `BorderFocused` | `0, 120, 212` | `76, 194, 255` | Focused control borders |
| `SuccessColor` | `16, 124, 16` | `108, 203, 95` | Success icons, complete steps |
| `WarningColor` | `255, 140, 0` | `255, 185, 0` | Warning icons, in-progress steps |
| `ErrorColor` | `209, 52, 56` | `255, 107, 107` | Error icons, error steps |
| `InfoColor` | `0, 120, 212` | `76, 194, 255` | Info icons |
| `DropZoneBackground` | `0, 120, 212` at alpha 20 | `76, 194, 255` at alpha 20 | Active drop zone fill |
| `DropZoneBorder` | `0, 120, 212` | `76, 194, 255` | Active drop zone border |
| `OverlayBackground` | `0, 0, 0` at alpha 102 | `0, 0, 0` at alpha 153 | Modal/overlay backdrop |
| `PaletteBackground` | `255, 255, 255` | `45, 45, 45` | Command palette background |
| `HighlightMatch` | `255, 215, 0` at alpha 76 | `255, 215, 0` at alpha 51 | Fuzzy match highlight in command palette |

### GUM Forms Control Theming

GUM Forms controls (`Button`, `TextBox`, `ComboBox`, `CheckBox`, `ListBox`, `ScrollViewer`, etc.) use their own internal styling system. To theme them:

1. **Default GUM Forms styles**: GUM provides `FrameworkElement.DefaultFormsComponents` which defines the visual tree for each Forms control type. Override these at startup to use theme-aware colors.
2. **Runtime style updates**: When theme switches, iterate all Forms control instances and update their visual runtimes' `ActiveStyles`.
3. **Custom controls**: Controls built from primitives (tooltip, command palette, workflow indicator) are fully theme-aware via the `ThemeManager` registry.

### Tasks

| # | Task | Description | Complexity |
|---|------|-------------|------------|
| 7.1 | Create `ThemeDefinition` class | Immutable class holding all color token values as `Color` properties (MonoGame `Microsoft.Xna.Framework.Color`). Two static instances: `ThemeDefinition.Light` and `ThemeDefinition.Dark`. | Low |
| 7.2 | Create `ThemeManager` singleton | Properties: `CurrentTheme` (Light/Dark enum), `CurrentDefinition` (ThemeDefinition). Methods: `SetTheme(theme)`, `ToggleTheme()`, `Register(runtime, applyThemeAction)`, `Unregister(runtime)`, `UnregisterAll()`. `Unregister(runtime)` removes a single runtime from the registry — required when dialogs are closed or panels are destroyed, otherwise stale references cause memory leaks or null-reference errors on theme switch. `SetTheme` iterates all registered runtimes and calls their `applyThemeAction` with the new `ThemeDefinition`. Fires `ThemeChanged` event. | Medium |
| 7.3 | Implement `ActiveStyles`-based theme application | For each runtime registered with `ThemeManager`, the `applyThemeAction` swaps `"theme-light"` and `"theme-dark"` in the runtime's `ActiveStyles` list. Define both style states on all theme-aware runtimes at creation time. Alternative approach for simpler cases: directly set `Red`, `Green`, `Blue`, `Alpha` from `ThemeDefinition` colors. | Medium |
| 7.4 | Theme all GUM Forms controls | Override `FrameworkElement.DefaultFormsComponents` at app startup to use theme-aware visual trees. Register all Forms control visual runtimes with `ThemeManager`. When theme changes, update button backgrounds, text colors, border colors on all standard controls (Button, TextBox, ComboBox, CheckBox, ListBox, ScrollViewer, Slider). | Medium |
| 7.5 | Implement system theme detection | On startup, detect OS theme preference. Windows: read registry key `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`. macOS: `defaults read -g AppleInterfaceStyle`. Linux: check `GTK_THEME` env var or `gsettings get org.gnome.desktop.interface color-scheme`. When `UseSystemTheme` is true, match OS. | Medium |
| 7.6 | Add theme toggle to View menu | View > Theme > Light / Dark / System (three menu items, current one has a checkmark indicator). Uses the custom menu bar from Phase 65. Persisted in user settings JSON. | Low |
| 7.7 | Wire Ctrl+Shift+D shortcut to theme toggle | Cycles: Light -> Dark -> System -> Light. Status bar shows "Theme: Dark" on change. Registered in `ShortcutRegistry` with action ID `"toggle-theme"`. Note: `Ctrl+D` was avoided because it conflicts with text editing conventions (duplicate line / select word). | Low |
| 7.8 | Theme the workflow indicator | Ensure step state colors reference `ThemeDefinition` tokens (`SuccessColor`, `WarningColor`, `ErrorColor`) rather than hardcoded RGB values. Register all workflow indicator runtimes with `ThemeManager`. Test both themes. | Low |
| 7.9 | Theme the command palette | Palette background (`PaletteBackground`), input field, result list, hover highlight (`AccentPrimary` at alpha 30), and match highlight (`HighlightMatch`) all use theme tokens. Register all palette runtimes with `ThemeManager`. Ensure readability in both themes. | Low |
| 7.10 | Theme the drop zone overlays | Drop feedback overlay background and text use `DropZoneBackground`, `DropZoneBorder`, and `TextPrimary` tokens. Invalid drop uses `ErrorColor`. Register overlay runtimes with `ThemeManager`. | Low |
| 7.11 | Theme the validation panel | Severity icons use `ErrorColor`, `WarningColor`, `InfoColor` from current `ThemeDefinition`. Quick-fix buttons use `AccentPrimary`. Row hover background uses `BackgroundTertiary`. Register all validation panel runtimes. | Low |
| 7.12 | Theme the tooltip system | `TooltipRuntime` background NineSlice uses `PaletteBackground` tint. Title text uses `AccentPrimary`. Description uses `TextPrimary`. Tip uses `TextSecondary`. Separator uses `BorderDefault`. Register with `ThemeManager`. | Low |
| 7.13 | Theme the gradient editor | Gradient editor control (from effects panel) backgrounds, handles, and labels use theme tokens. Gradient preview area always uses a neutral gray checkerboard background (`ColoredRectangleRuntime` with alternating gray tiles) regardless of theme. | Medium |
| 7.14 | Theme the atlas preview overlay | Atlas grid lines (`ColoredRectangleRuntime` elements), glyph highlight rectangles, and info labels use theme-aware colors. Atlas image itself is drawn unmodified via `SpriteBatch`. | Low |
| 7.15 | Implement preview background color picker | In the Preview panel toolbar, a small color swatch `Button` opens a custom color picker overlay (built from GUM primitives: `ColoredRectangleRuntime` for hue bar, saturation/value square, and preview swatch). Presets: checkerboard (transparent), dark gray (51,51,51), white (255,255,255), custom. Independent of app theme. Store in settings. | Medium |
| 7.16 | High contrast mode support | Detect Windows High Contrast mode via registry (`HKCU\Control Panel\Accessibility\HighContrast\Flags`). When active, create a `ThemeDefinition.HighContrast` with system high-contrast colors (read via `SystemColors` or P/Invoke). Ensure minimum 4.5:1 contrast ratio for all text by validating token pairs. | Medium |
| 7.17 | Accent color customization | In Settings panel, a color picker (same component as preview background picker) allows overriding the accent color. Default: `#0078D4`. Updates `AccentPrimary`, `AccentHover`, `BorderFocused` tokens in the current `ThemeDefinition`. Persisted in settings. Re-applies theme on change. | Medium |

### Theme Switching Interaction Pattern

```
1. User presses Ctrl+Shift+D (or uses View > Theme menu)
2. Current theme: Light -> switches to Dark
   - ThemeManager.SetTheme(Dark) called
   - Iterates all registered runtimes, swaps ActiveStyles
   - All controls re-render with new colors on the next frame
   - Preview panel background does NOT change (independent setting)
   - Status bar shows "Theme: Dark" for 3 seconds
3. User presses Ctrl+Shift+D again -> switches to System
   - OS theme detected, applied
   - Status bar shows "Theme: System (Dark)" or "Theme: System (Light)"
4. User presses Ctrl+Shift+D again -> switches to Light
   - Cycle complete
```

---

## Dependencies & Prerequisites

| Dependency | Required By | Notes |
|------------|-------------|-------|
| MonoGame (DesktopGL) | All waves | MonoGame framework must be set up in the `apps/` project |
| GUM UI (code-only, NuGet) | All waves | `Gum.MonoGame` NuGet package for all UI controls and primitives |
| MonoGame.Extended | Waves 1, 4, 6 | Utility library for input helpers, collections, etc. |
| Sprite sheet with icons | Waves 1, 3, 4, 6 | Workflow step icons, info icons, severity icons — packed into a shared texture atlas |
| BitmapFont assets | Waves 1, 3, 6, 7 | Pre-rendered bitmap fonts for UI text (regular, bold, italic, monospace variants) |
| Custom menu bar from Phase 65 | Waves 1, 6, 7 | View menu items, shortcut labels in menus |
| Core library `BmFont.Generate()` | Waves 2, 5 | Preset application and validation need access to generator options |
| `BmFontReader` | Wave 4 | For .fnt file drag-and-drop inspection |
| Settings persistence layer | Waves 1, 2, 6, 7 | JSON-based user settings in platform-appropriate location (`System.Text.Json`) |

---

## Core Library Notes (Document, Don't Fix)

These items are worth noting for potential future core library work. They do not block this phase.

- **Preset configurations**: The `PresetRegistry` lives in the UI layer and applies settings to `FontGeneratorOptions`. If presets become popular, consider adding a `FontGeneratorOptions.ApplyPreset(string presetName)` method to the core library so CLI users can also use presets.
- **Validation helpers**: The `ValidationEngine` is UI-specific, but some validation logic (e.g., "will these glyphs fit in this texture size?") could be useful as a core library utility. Consider extracting `AtlasSizeEstimator.WillFit(charCount, fontSize, padding, maxWidth, maxHeight)`.
- **Generation progress callback**: The validation and status bar features would benefit from a progress callback on `BmFont.Generate()` (e.g., `IProgress<GenerationProgress>`). This would enable accurate progress bars and per-glyph status updates.
- **Error detail enrichment**: The core library currently throws exceptions on failure. For better error messages in the UI, consider returning a `GenerationResult` with structured error information (failed codepoints, packing stats) rather than relying on exception messages.
- **Settings combinations**: Some combinations of settings are technically valid but produce poor results (e.g., SDF with channel packing, 1px font size with 4x super-sampling). A `FontGeneratorOptions.Validate() -> List<ValidationMessage>` method could be added to the core library.
- **Phase 55 gaps**: Phase 55 (core library enhancements) tracks several foundational improvements — progress callbacks, structured error results, and options validation — that would strengthen the UX features in this phase. Coordinate with Phase 55 when implementing the validation engine and status bar progress reporting.

---

## Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| New users can generate their first font within 2 minutes | First-launch workflow guides the user from load to export without external documentation |
| Engine presets eliminate platform guesswork | Selecting a preset auto-configures all format-specific settings correctly |
| Every setting is self-documenting | All settings have info icon tooltips with what/when/default/tip content |
| Drag-and-drop covers all file inputs | .ttf/.otf/.woff, .bmfc, .fnt, .txt, and .png can all be dropped on the window |
| Pre-flight catches mistakes before generation | No font, no characters, and atlas-too-small errors are caught before the user waits for generation |
| Quick-fixes reduce friction | Common warnings have one-click resolutions that apply the suggested setting |
| Power users have full keyboard control | Command palette and shortcuts cover all major actions; no mouse required for core workflow |
| Theme support is complete | Both light and dark themes render all GUM controls correctly; no hardcoded colors in custom controls |
| Preview background is theme-independent | Users can set preview background to any color regardless of app theme |
| Keyboard shortcuts are discoverable | F1 reference and command palette make shortcuts easy to find |
| Custom tooltip system works reliably | Hover delay, positioning, viewport clamping, and theme awareness all function correctly |
| MonoGame keyboard input is robust | Edge detection prevents repeated firing; text input fields are not interrupted by shortcuts |

---

## Estimated Effort

| Wave | Estimated Effort | Key Risk |
|------|-----------------|----------|
| Wave 1 — Guided Workflow | 3-4 days | Building stepper/breadcrumb from GUM primitives; animation system for pulse/transitions |
| Wave 2 — Smart Defaults | 2-3 days | Keeping preset definitions accurate and up-to-date with engine changes |
| Wave 3 — Contextual Help | 4-5 days | Building custom tooltip system from scratch (GUM has no built-in tooltip); hover detection reliability |
| Wave 4 — Drag-and-Drop | 2-3 days | MonoGame FileDrop is window-level only — no drag-over preview without platform interop |
| Wave 5 — Validation | 3-4 days | Avoiding false positives that annoy users; getting quick-fix logic right |
| Wave 6 — Keyboard Shortcuts | 4-5 days | Command palette from GUM primitives; fuzzy matching; text input focus conflict resolution |
| Wave 7 — Theme & Appearance | 3-4 days | Registering all runtimes with ThemeManager; ensuring nothing is missed; GUM Forms control theming |
| **Total** | **21-28 days** | |
