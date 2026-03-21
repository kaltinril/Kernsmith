# Phase 66 — Advanced Features

> **Status**: Planning
> **Created**: 2026-03-21
> **Goal**: Implement advanced font generation features in the UI including variable font support, SDF configuration, batch generation, custom glyphs, and font inspection tools.

---

## Overview

This phase adds the power-user features that differentiate KernSmith from simpler bitmap font tools. Each wave delivers a self-contained feature set that maps directly to existing KernSmith builder API methods. The UI must expose these capabilities without overwhelming casual users — progressive disclosure is the guiding principle.

All features are optional and hidden behind collapsible `StackPanel` sections, secondary panels, or dedicated windows. The default workflow (load font, pick size, generate) remains uncluttered.

### Framework

| Technology | Role |
|---|---|
| **MonoGame (DesktopGL)** | Rendering, input, game loop, `Texture2D` for image handling |
| **GUM UI (code-only)** | Layout and controls — `Button`, `CheckBox`, `ComboBox`, `Label`, `ListBox`, `Slider`, `StackPanel`, `TextBox`, `ScrollViewer`, `Image` |
| **MonoGame.Extended** | Camera, sprite utilities, additional drawing primitives |
| **GUM ViewModel** | MVVM via `Get<T>()` / `Set<T>()` properties with `SetBinding()` for control-to-VM wiring |

### Dependencies

- UI shell, font loading, preview panel, and basic generation must already be functional (prior UI phases).
- KernSmith core library APIs referenced here are already implemented unless noted otherwise. **Phase 55** tracks core library API additions needed by this phase — see individual task notes for gaps.
- Variable font support requires a variable font test file (e.g., a `.ttf` with `fvar` table).
- Image import for custom glyphs uses MonoGame's `Texture2D.FromStream()`.

### API Surface Used

| API Method / Property | Wave |
|---|---|
| `WithVariationAxis(string tag, float value)` | 1 |
| `FontInfo.VariationAxes` | 1 |
| `FontInfo.NamedInstances` | 1 |
| `WithSdf(bool)` | 2 |
| `WithCustomGlyph(int, int, int, byte[], PixelFormat, int?)` | 3 |
| `BmFont.GenerateBatch(IReadOnlyList<BatchJob>, BatchOptions?)` | 4 |
| `BatchJob`, `BatchOptions`, `BatchResult` | 4 |
| `BmFont.Load(fntPath)`, `BmFont.LoadModel(fntData)` | 5 |
| `WithColorFont(bool)`, `WithColorPaletteIndex(int)` | 6 |
| `WithFallbackCharacter(char)` | 7 |
| `WithFaceIndex(int)` | 7 |

---

## Wave 1: Variable Font Support

Variable fonts contain one or more design axes (weight, width, slant, etc.) that allow continuous interpolation between extremes. The UI must detect variable fonts, expose axis controls, and provide named-instance shortcuts.

### GUM Layout

The axes panel is a collapsible `StackPanel` (vertical) nested inside the font info `ScrollViewer`. Each axis row is a horizontal `StackPanel` containing a `Label` (axis name), a smaller monospace-styled `Label` (tag), a `Slider`, a `TextBox` (numeric value), and a `Button` (reset icon). The named-instance selector is a `ComboBox` placed above the axis rows.

```
+----------------------------------------------+
| Variation Axes (3)          [Collapse]        |
|----------------------------------------------|
| Preset: [ComboBox: named instances       v]  |
|----------------------------------------------|
| Weight  wght  [====O===========] [400  ] [R] |
| Width   wdth  [======O=========] [100  ] [R] |
| Slant   slnt  [O===============] [  0  ] [R] |
|----------------------------------------------|
| [Reset All Axes]                              |
+----------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 1.1 | Detect variable font on load | After loading a font, check `FontInfo.VariationAxes`. If non-null and non-empty, the font is variable. Store the axis list and named instances on the ViewModel using `Set<List<VariationAxis>>()`. | `FontInfo.VariationAxes` |
| 1.2 | Show "Variable Font" badge | Add a `Label` in the font info `StackPanel` with distinct foreground color (e.g., teal) and text "Variable Font". Set its `Visible` property via `SetBinding()` to a `bool IsVariableFont` VM property. Hide for non-variable fonts. | `FontInfo.VariationAxes != null` |
| 1.3 | Create "Variation Axes" collapsible panel | Add a vertical `StackPanel` below the font info section. Use a `Button` header showing axis count (e.g., "Variation Axes (3)") that toggles the inner content `StackPanel` visibility. Starts collapsed (`Visible = false` on content) to avoid clutter. Bind visibility to a `bool IsAxesPanelExpanded` VM property. | -- |
| 1.4 | Render axis slider per axis | For each `VariationAxis` in the list, programmatically create a horizontal `StackPanel` row containing: a `Label` for axis name (e.g., "Weight"), a `Label` for axis tag in monospace style (e.g., `wght`), a `Slider` with `Minimum = MinValue`, `Maximum = MaxValue`, `Value` bound to the VM axis value, and a `TextBox` for numeric entry synced to the slider via two-way binding. Initial value is `DefaultValue`. Each row is added to the axes content `StackPanel`. | `VariationAxis.Tag`, `.MinValue`, `.DefaultValue`, `.MaxValue`, `.Name` |
| 1.5 | Axis slider value binding | When a `Slider.Value` changes (via `SetBinding()` on the slider and the VM property), update the builder call to include `WithVariationAxis(tag, value)`. If the value equals the default, omit the call (no-op). Multiple axes produce multiple `WithVariationAxis` calls chained on the builder. The VM exposes a `Dictionary<string, float> AxisValues` property. | `WithVariationAxis(string tag, float value)` |
| 1.6 | Per-axis "Reset to Default" button | Each axis row has a small `Button` (text "R" or reset icon via a glyph character) that sets the `Slider.Value` and `TextBox.Text` back to `DefaultValue`. Bind the `Button.Enabled` state to a comparison: `currentValue != defaultValue`. Use `SetBinding()` to keep this reactive. | `VariationAxis.DefaultValue` |
| 1.7 | "Reset All Axes" button | A `Button` with text "Reset All Axes" at the bottom of the axes `StackPanel`. Click handler iterates all axis entries in the VM and resets each to `DefaultValue`. | -- |
| 1.8 | Named instances ComboBox | Above the axis slider rows, add a `ComboBox` labeled "Preset" (via an adjacent `Label`). Populate its items from `FontInfo.NamedInstances`, displaying each instance's `Name` (e.g., "Bold", "Light Condensed"). Bind `SelectedIndex` to a VM property. | `FontInfo.NamedInstances`, `NamedInstance.Name`, `NamedInstance.Coordinates` |
| 1.9 | Named instances to slider sync | When the `ComboBox.SelectedIndex` changes, read the selected `NamedInstance.Coordinates` dictionary and set each matching axis slider value via the VM. If the font has axes not covered by the instance, leave those at their current values. When the user manually adjusts any slider, reset the `ComboBox.SelectedIndex` to -1 (no selection) to indicate a custom configuration. | -- |
| 1.10 | Axis value validation | Clamp `TextBox` numeric input to `[MinValue, MaxValue]` on focus-lost. If the user types a value outside the range, clamp it and briefly flash the `TextBox` border red (set a color, then reset after 500ms in the `Update` loop). Reject non-numeric input by parsing with `float.TryParse` and reverting to previous value on failure. Handle edge case where `MinValue == MaxValue` by setting `Slider.Enabled = false` and `TextBox.Enabled = false` (read-only). | -- |
| 1.11 | Live preview update on axis change | When any axis value changes in the VM, set a debounce timer (300ms). On timer expiry, re-render the font preview with the new axis configuration on a background `Task`. Show a `Label` with text "Rendering..." overlaid on the preview during re-rasterization (toggle its `Visible` property). Cancel any in-flight render via `CancellationTokenSource`. | -- |
| 1.12 | Axis visualization strip | Below each axis `Slider`, optionally render a horizontal `StackPanel` of 5-7 small `Image` controls. Each `Image` shows a sample glyph (e.g., "A") rendered at an evenly-spaced axis value from min to max. Generate these `Texture2D` thumbnails asynchronously on a background thread using `Task.Run`, then assign each to the `Image` control's texture on the main thread. | -- |
| 1.13 | Persist axis values in project/settings | When saving a project or preset, serialize the `Dictionary<string, float> AxisValues` from the VM. On reload, restore axis values by setting each slider via the VM property. | -- |
| 1.14 | Edge case: font with many axes | If a variable font has more than 6 axes, show the first 6 axis rows in the `StackPanel` and add a `Button` "Show all (N)" that sets a `bool ShowAllAxes` VM property, making the remaining rows visible. Uncommon but possible (e.g., parametric fonts). | -- |

### Edge Cases & Notes

- Some variable fonts have axes with very large ranges (e.g., `opsz` from 6 to 144). The `Slider` should use appropriate step granularity — set `SmallChange` to 1 for ranges > 100, 0.1 for ranges < 10, 0.01 for ranges < 1.
- Axis tags are 4-character strings. The UI should display the human-readable `Name` prominently in a larger `Label` and the tag in a smaller monospace-styled `Label`.
- Re-rasterization on axis change is expensive. The 300ms debounce plus a `CancellationTokenSource` on the previous render task is essential.
- Named instances may have duplicate names in malformed fonts. Display them as-is but deduplicate if identical coordinates.

---

## Wave 2: SDF Configuration

Signed Distance Field (SDF) rendering is a technique used by game engines to render resolution-independent text. The UI must make SDF accessible to users who need it and invisible to those who do not.

### GUM Layout

SDF controls live in a collapsible section within the output settings `ScrollViewer`. The section header has a `CheckBox` for enabling SDF, and the body contains spread, channel, and engine preset controls.

```
+----------------------------------------------+
| [x] SDF Mode                          [?]    |
|----------------------------------------------|
| Spread:    [====O=====] [  8  ]               |
| Channel:   [ComboBox: Alpha Only          v]  |
|----------------------------------------------|
| Quick Setup:                                  |
|  [Unity TMP] [Godot] [Unreal Slate]          |
|----------------------------------------------|
| (i) SDF fonts are typically generated at      |
|     32-64px for runtime scaling.              |
+----------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 2.1 | SDF toggle control | Add a `CheckBox` labeled "SDF Mode" in the output settings section. Bind its `IsChecked` state to a `bool IsSdfEnabled` VM property via `SetBinding()`. When checked, maps to `WithSdf(true)` on the builder. Default: unchecked. | `WithSdf(bool)` |
| 2.2 | SDF explanation tooltip | Add a small `Button` with text "?" next to the SDF `CheckBox`. On click, toggle a `Label` with multi-line text visible below the checkbox: "Signed Distance Field -- encodes glyph distance data instead of pixel coverage. Used for scalable text rendering in game engines like Unity (TextMeshPro), Godot, and Unreal." GUM does not have native tooltips, so use a toggleable info `Label`. | -- |
| 2.3 | SDF spread/radius control | When SDF is enabled (bind `Visible` to `IsSdfEnabled`), show a `Slider` labeled "SDF Spread" (via adjacent `Label`) with `Minimum = 1`, `Maximum = 32`, `Value = 8` (default). Paired with a `TextBox` for numeric entry. Bind to `int SdfSpread` VM property. **Requires Phase 55 API addition** — `WithSdfSpread(int)` does not exist yet. UI control is a placeholder until the API is available. | `WithSdfSpread(int)` — **Phase 55 prerequisite** |
| 2.4 | SDF channel preset selector | When SDF is enabled, show a `ComboBox` labeled "Channel" with items: "Alpha Only (RGB=0, A=SDF)", "Grayscale (R=G=B=A=SDF)", "Multi-channel MSDF" (set `Enabled = false` on this item entry if not supported). Default to "Alpha Only". Bind `SelectedIndex` to a `int SdfChannelPreset` VM property. Map to appropriate channel packing configuration. | Channel packing API |
| 2.5 | SDF preview mode toggle | Add a `CheckBox` labeled "Show SDF Field" in the preview panel. Bind `Visible` to `IsSdfEnabled` (only shown when SDF is on). When checked, render the preview using a gradient visualization — the preview renderer uses a custom shader or grayscale mapping: white at center of glyph, fading to black at the spread boundary, then clipped. Render to a `Texture2D` and display in the preview `Image` control. | -- |
| 2.6 | SDF size recommendation | When SDF is enabled, show a `Label` styled as an info banner (blue/teal text) below the SDF controls: "SDF fonts are typically generated at a larger size (32-64px) than the final render size. The SDF data allows smooth scaling at runtime." If the current font size (read from VM) is below 24px, change the `Label` color to yellow/amber as a warning and append: "Current size may be too small for quality SDF output." | -- |
| 2.7 | Game engine presets panel | When SDF is enabled, show a horizontal `StackPanel` labeled "Quick Setup" containing three `Button` controls: **"Unity TMP"** (sets size=64, spread=8, channel=Alpha), **"Godot"** (sets size=48, spread=6, channel=Grayscale), **"Unreal Slate"** (sets size=64, spread=8, channel=Alpha). Each button's click handler sets the corresponding VM properties. Does not auto-generate. | -- |
| 2.8 | SDF + effects compatibility warning | When `IsSdfEnabled` is true AND any effect (outline, shadow, gradient) is also enabled in the VM, show a `Label` styled as a warning (amber text): "Visual effects (outline, shadow) are baked into the SDF output. For runtime effects, disable baked effects and use your engine's SDF shader instead." Add a `Button` "Dismiss" that hides the warning. | -- |
| 2.9 | SDF + channel packing conflict | When `IsSdfEnabled` is true AND channel packing is also enabled, show a `Label` warning: "SDF mode and channel packing are typically mutually exclusive. SDF data should occupy its own channel. Proceeding may produce unexpected results." Add a `Button` "Disable Packing" that sets the channel packing VM property to false. | -- |
| 2.10 | SDF output format auto-suggestion | When SDF is enabled, auto-set the texture format ComboBox to PNG (lossless, preserves distance data) via the VM. If the user manually selects a lossy format (e.g., DDS with BC1), show a `Label` warning: "Lossy compression can corrupt SDF distance data. Use a lossless format or BC4/BC7 compression." | -- |
| 2.11 | SDF atlas padding auto-adjust | When SDF is enabled, automatically set the atlas glyph padding VM property to at least `spread + 2` pixels to prevent distance field bleeding between adjacent glyphs. Show the adjusted value in the padding `TextBox`. If the user manually sets padding below `spread + 2`, show a `Label` warning: "Padding is below SDF spread. Distance fields may bleed between glyphs." | -- |
| 2.12 | Persist SDF settings | Save `IsSdfEnabled`, `SdfSpread`, `SdfChannelPreset`, and selected engine preset index in project/preset files. Restore on load. | -- |

### Edge Cases & Notes

- SDF generation is significantly slower than normal rasterization. Show a "Generating SDF..." `Label` in the preview during rendering and consider generating a low-res preview first, then the full SDF on explicit "Generate" action.
- MSDF (multi-channel SDF) is a more advanced technique that may not be supported by the core library yet. Show the `ComboBox` item with `Enabled = false` and append "(Coming soon)" to the display text.
- Some users may not understand SDF. The info button and recommendation banner are critical for discoverability without confusion.

---

## Wave 3: Custom Glyph Import

Custom glyphs let users inject their own bitmap images into specific codepoints — commonly used for game UI icons, custom symbols, or logo characters embedded in a font atlas.

### GUM Layout

The custom glyph panel is a collapsible `StackPanel` in the character configuration area. It contains a glyph list displayed in a `ListBox`, and import controls in a secondary panel or inline form.

```
+----------------------------------------------+
| Custom Glyphs (3)              [Collapse]     |
|----------------------------------------------|
| [ListBox]                                     |
|  [img] * U+2605  24x24  RGBA  xAdv:24       |
|  [img] ^ U+E000  32x32  Gray  xAdv:36       |
|  [img] @ U+E001  16x16  RGBA  xAdv:18       |
|----------------------------------------------|
| [Add Custom Glyph] [Edit] [Remove]            |
|----------------------------------------------|
| Import/Export:  [Import JSON] [Export JSON]    |
+----------------------------------------------+

Add/Edit Dialog (inline panel or separate render area):
+----------------------------------------------+
| Codepoint: [TextBox: U+E000]                  |
| Image:     [TextBox: path] [Browse]           |
| Preview:   [Image: 128x128 area]              |
|            24x24 px | 2.3 KB | RGBA           |
| Format:    [ComboBox: Grayscale / RGBA    v]  |
| xAdvance:  [TextBox: 24]                      |
| [Confirm]  [Cancel]                           |
+----------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 3.1 | "Add Custom Glyph" button | Add a `Button` labeled "Add Custom Glyph" in a "Custom Glyphs" collapsible `StackPanel` (below character set configuration). The section header `Button` shows the current count: "Custom Glyphs (3)". Clicking the add button shows the inline import panel (toggles a `StackPanel` visible). | -- |
| 3.2 | Codepoint selection | In the import panel, provide a `TextBox` for codepoint entry. Accept two input modes: (a) type a character directly (e.g., type the star symbol), which auto-resolves to its Unicode codepoint via `char.ConvertToUtf32`; (b) enter a codepoint in U+XXXX or decimal format (detect the "U+" prefix and parse as hex). Validate range U+0000 to U+10FFFF on focus-lost. Show a `Label` warning (amber) if the codepoint conflicts with an existing character in the selected character set. Bind to `int CustomGlyphCodepoint` VM property. | `int codepoint` parameter |
| 3.3 | Image file picker | A `Button` labeled "Browse" next to a read-only `TextBox` showing the file path. On click, use a platform file dialog (via `System.Windows.Forms.OpenFileDialog` or a cross-platform abstraction) filtered to `*.png`. After selection, load the image using `Texture2D.FromStream(GraphicsDevice, fileStream)` to create a `Texture2D`. Read dimensions from the texture. Reject files larger than 512x512 pixels — show a `Label` error: "Image exceeds 512x512 limit." Reject non-PNG files. Store the `Texture2D` reference and file path in the VM. | -- |
| 3.4 | Image preview | Display the loaded `Texture2D` in an `Image` GUM control within the import panel. Size the `Image` to fit within a 128x128 area (scale down if larger, display at actual size if smaller). Below the `Image`, show three `Label` controls: dimensions (e.g., "24x24 px"), file size (e.g., "2.3 KB"), and detected pixel format ("Grayscale" if all RGB channels are identical, otherwise "RGBA"). | -- |
| 3.5 | Pixel format selector | A `ComboBox` labeled "Format" with items: "Grayscale (8-bit)", "RGBA (32-bit)". Auto-detect from the loaded `Texture2D`: extract pixel data via `Texture2D.GetData<Color>()`, check if all pixels have R==G==B and A==255 — if so, suggest Grayscale; if color variation or alpha exists, suggest RGBA. Set the `ComboBox.SelectedIndex` to the suggested value. Allow override. Bind to `int CustomGlyphFormat` VM property. | `PixelFormat format` parameter |
| 3.6 | Image resize/crop tool | Below the preview `Image`, add resize controls: a `TextBox` for target width and a `TextBox` for target height, with a `CheckBox` "Lock Aspect Ratio" (default: checked). When locked, changing width auto-calculates height and vice versa. Add a `ComboBox` for scaling method: "Nearest Neighbor" (for pixel art) or "Bilinear" (for photographic content). Apply the resize by creating a new `RenderTarget2D` at the target size, drawing the source `Texture2D` scaled via `SpriteBatch.Draw`, then reading back pixels. Show final dimensions in a `Label`. | Affects `width`, `height`, `pixelData` |
| 3.7 | xAdvance configuration | A `TextBox` labeled "xAdvance" for horizontal advance after rendering this glyph. Default to the image width (set when image loads). Allow custom values via `int.TryParse`. In the preview `Image`, draw a vertical red line at `xAdvance` pixels from the left edge using `SpriteBatch` line rendering (1px wide rectangle) overlaid on the glyph preview during the MonoGame `Draw` pass. Bind to `int CustomGlyphXAdvance` VM property. | `int? xAdvance` parameter |
| 3.8 | Pixel data extraction | On confirm, extract raw pixel data from the (possibly resized) `Texture2D`. Call `Texture2D.GetData<byte>()` for RGBA data (4 bytes/pixel). For Grayscale, extract RGBA data then take only the R channel (1 byte/pixel). Validate array length matches `width * height * bytesPerPixel`. Store as `byte[] PixelData` in the VM. | `byte[] pixelData` parameter |
| 3.9 | Confirm and add to list | A `Button` labeled "Confirm" validates all inputs (codepoint in range, image loaded, pixel data extracted). Creates a `CustomGlyphEntry` object (codepoint, width, height, pixelData, format, xAdvance, sourceImagePath) and adds it to an `ObservableCollection<CustomGlyphEntry>` on the VM. Hide the import panel. The generation pipeline reads this collection and calls `WithCustomGlyph(...)` for each entry. A `Button` labeled "Cancel" hides the import panel without adding. | `WithCustomGlyph(codepoint, width, height, pixelData, format, xAdvance)` |
| 3.10 | Custom glyphs list display | Show all custom glyphs in a `ListBox` within the custom glyphs `StackPanel`. Each `ListBox` item is a horizontal `StackPanel` containing: an `Image` (small thumbnail of the glyph texture, 24x24), a `Label` showing the character and codepoint (e.g., "* U+2605"), a `Label` showing dimensions (e.g., "24x24"), a `Label` showing pixel format, and a `Label` showing xAdvance value. Bind the `ListBox.Items` to the VM collection. | -- |
| 3.11 | Edit existing custom glyph | An "Edit" `Button` below the `ListBox`. When a `ListBox` item is selected and Edit is clicked, populate the import panel with the existing entry's values (set VM properties for codepoint, reload the `Texture2D` from the stored path, set format and xAdvance). On confirm, replace the entry in the collection at the selected index. On cancel, discard changes. | -- |
| 3.12 | Remove custom glyph | A "Remove" `Button` below the `ListBox`. When clicked with a selection, show a confirmation `Label` with "Remove this glyph?" and two `Button` controls: "Yes" (removes from collection) and "No" (cancels). For multi-select, the `ListBox` can use a selection tracking list in the VM. | -- |
| 3.13 | Duplicate codepoint detection | When confirming an add or edit, check if the codepoint already exists in the custom glyphs collection. If so, show a `Label` warning: "A custom glyph for U+XXXX already exists. Replace it?" with `Button` controls "Replace" and "Cancel". Also warn via a `Label` if the codepoint conflicts with a character in the standard character set (the custom glyph will override it). | -- |
| 3.14 | Custom glyph preview in atlas | Custom glyphs should appear in the main font preview alongside regular glyphs. Render them at their native size (no font-size scaling) — draw the `Texture2D` directly via `SpriteBatch` at its original dimensions. If the custom glyph height is more than 4x the font's line height, show a size mismatch `Label` warning. | -- |
| 3.15 | Import/export custom glyph set | Two `Button` controls at the bottom of the custom glyphs panel: "Export JSON" serializes all `CustomGlyphEntry` objects to a JSON file (codepoint, dimensions, format, xAdvance, base64-encoded pixel data) using `System.Text.Json`. "Import JSON" loads from such a file via a file dialog, deserializes, and merges or replaces entries in the VM collection. Enables sharing custom glyph sets across projects. | -- |

### Edge Cases & Notes

- Custom glyphs bypass the font rasterizer entirely — they are raw bitmaps injected at packing time. Users must ensure the glyph dimensions are appropriate for the font size.
- If the user loads a different font, custom glyphs persist (they are independent of the font). This is intentional and should be documented in the info `Label`.
- Very large custom glyphs (e.g., 256x256) in a small font (e.g., 16px) will dominate the atlas. Show a `Label` warning if the custom glyph is more than 4x the font's line height.
- `Texture2D.FromStream()` requires a `GraphicsDevice` reference. Pass the `Game.GraphicsDevice` into the VM service layer or use a service interface to decouple.

---

## Wave 4: Batch Generation

Batch generation allows users to queue multiple font generation jobs and execute them in parallel. This is essential for game studios that need to generate fonts for multiple languages, sizes, or styles in one operation.

### GUM Layout

Batch generation opens as a dedicated panel (or replaces the main content area). It has a job `ListBox` on the left, configuration controls on the right, and progress indicators at the bottom.

```
+---------------------------+----------------------+
| Jobs                      | Global Settings      |
| [ListBox]                 |                      |
|  [P] Roboto 24px          | Output: [TextBox][B] |
|  [P] Roboto 32px          | Parallel: [Slider] 4 |
|  [R] Arial 16px  [=== ]  | [x] Font Cache       |
|  [D] Mono 12px   [Done]  |                      |
|  [F] Sans 48px   [Err!]  |                      |
|                           |                      |
| [Add Current] [Add Manual]|                      |
| [Clone] [Edit] [Remove]  |                      |
+---------------------------+----------------------+
| Overall: [===========     ] 3/5 | 01:23 elapsed  |
| [Start Batch]  [Cancel]  [Open Output Dir]       |
+--------------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 4.1 | Batch Generation entry point | Add a `Button` labeled "Batch Generate..." in the tools area of the main UI. On click, show the batch generation panel (set a `StackPanel` visible or swap the main content area). The panel has three regions: job list (`ListBox`), global settings (`StackPanel`), and execution controls (bottom `StackPanel`). | -- |
| 4.2 | Job list panel | A `ListBox` on the left side showing queued jobs. Each item is a horizontal `StackPanel` displaying: a `Label` for status icon (text characters: "P" pending, "R" running, "D" done, "F" failed), a `Label` for job name (auto-generated from font name + size, e.g., "Roboto-Regular 24px"), and a `Label` for progress percentage when running. Bind to `ObservableCollection<BatchJobViewModel>` on the VM. | -- |
| 4.3 | "Add Current Settings" button | A `Button` labeled "Add Current" below the `ListBox`. On click, snapshot the current font, size, character set, effects, and all other options from the main VM into a new `BatchJobViewModel`. Add to the collection. The job is a snapshot — subsequent changes to the main UI do not affect it. | Creates `BatchJob` from current `FontGeneratorOptions` |
| 4.4 | "Add Job Manually" inline form | A `Button` labeled "Add Manual" that shows an inline form `StackPanel`: a `TextBox` for font file path with a "Browse" `Button`, a `TextBox` for font size, a `ComboBox` for character set preset (items: "ASCII", "Latin", "Full Unicode"), and a `Button` "Add Job". Creates a `BatchJobViewModel` with these settings. | `BatchJob` with `FontData` or `FontPath` |
| 4.5 | "Add from .bmfc file" import | A `Button` labeled "Import .bmfc" in the job actions area. Opens a file dialog filtered to `*.bmfc`. Parse the BMFont Creator `.bmfc` format and map fields to `FontGeneratorOptions`. Show a `Label` with any unsupported settings that could not be mapped. Add the parsed job to the collection. | -- |
| 4.6 | "Clone Job" action | A `Button` labeled "Clone" below the `ListBox`. When a job is selected, duplicate it as a new `BatchJobViewModel` (deep copy all properties). Set the clone's name to the original + " (copy)". The user can then select and edit it. | -- |
| 4.7 | Edit job settings | A `Button` labeled "Edit" below the `ListBox`. When clicked with a job selected, show an inline editor `StackPanel` (or replace the global settings area) with `TextBox`/`ComboBox`/`Slider` controls for all `FontGeneratorOptions` relevant to the selected job. Changes apply only to the selected job's VM. A `Button` "Done" hides the editor. | -- |
| 4.8 | Remove job(s) | A `Button` labeled "Remove" below the `ListBox`. When clicked with a selection, remove the selected `BatchJobViewModel` from the collection. If more than 3 items are selected (tracked via a selection list in the VM), show a confirmation `Label` with "Remove N jobs?" and "Yes"/"No" `Button` controls. | -- |
| 4.9 | Output directory configuration | In the global settings `StackPanel`: a `Label` "Output Directory", a `TextBox` showing the path (bound to `string OutputDirectory` VM property), and a `Button` "Browse" for directory selection. Default to a "batch-output" subdirectory next to the first job's font file. Create subdirectories per job (named after font + size) to avoid file collisions. | -- |
| 4.10 | Parallelism control | A `Slider` labeled "Max Parallel Jobs" in the global settings area. `Minimum = 1`, `Maximum = Environment.ProcessorCount`, default = `ProcessorCount / 2`. Paired with a `TextBox` for numeric display. Below it, a `Label` with info text: "Higher parallelism uses more CPU and memory." Bind to `int MaxParallelism` VM property. | `BatchOptions.MaxParallelism` |
| 4.11 | Font cache toggle | A `CheckBox` labeled "Enable font cache" in the global settings area. Default: checked. Bind to `bool FontCacheEnabled` VM property. Below it, a `Label` with info text: "Reuse loaded font data across jobs sharing the same font file." Maps to `BatchOptions.FontCache`. | `BatchOptions.FontCache` |
| 4.12 | "Start Batch" execution button | A `Button` labeled "Start Batch" in the bottom execution `StackPanel`. On click: disable all job editing controls (set `Enabled = false` on add/edit/remove buttons), change this button's text to "Cancel", call `BmFont.GenerateBatch(jobs, batchOptions)` on a background `Task`. Bind button state to a `bool IsBatchRunning` VM property. **Note:** `GenerateBatch()` currently has no `CancellationToken` parameter (Phase 55 prerequisite). Cancellation means stopping before submitting the next job to `GenerateBatch`, not cancelling an in-progress job. | `BmFont.GenerateBatch(IReadOnlyList<BatchJob>, BatchOptions?)` |
| 4.13 | Per-job progress reporting | Each `BatchJobViewModel` has properties: `string Status` ("Pending", "Running...", "Completed (1.2s)", "Failed: [error]") and `float Progress` (0.0 to 1.0). **Note:** `GenerateBatch()` has no per-job progress callback (Phase 55 prerequisite). The UI can only track progress by wrapping individual `BmFont.Generate()` calls manually instead of using `GenerateBatch()`, or by tracking job-level completion (0% or 100% per job). Granular sub-job status (e.g., "Rasterizing...", "Packing atlas...") is not available until Phase 55 adds progress reporting. Update status on the main thread (use `SynchronizationContext` or queue updates for the `Update` loop). The `ListBox` item template shows the status `Label` and a progress bar rendered as a filled rectangle via `SpriteBatch` or a styled `Label` with width binding. | **Phase 55 prerequisite** for progress callbacks |
| 4.14 | Overall progress bar | A horizontal `StackPanel` at the bottom containing: a `Label` "Overall:", a progress bar (rendered as two layered rectangles — background and fill, with fill width proportional to `completedJobs / totalJobs`), a `Label` showing "X of Y completed", and a `Label` showing elapsed time (updated each frame from a `Stopwatch`). Bind to `int CompletedJobCount`, `int TotalJobCount`, `TimeSpan ElapsedTime` VM properties. | -- |
| 4.15 | Cancel batch execution | When the batch is running, the button reads "Cancel". On click, set a cancellation flag. Since `GenerateBatch()` currently has no `CancellationToken` parameter (Phase 55 prerequisite), cancellation means stopping before submitting the next job — it does not cancel an in-progress job. Running jobs complete but no new jobs start. Update the button text to "Cancelling..." (set `Enabled = false`). When all running jobs finish, show the results summary and reset the button to "Start Batch". Already-written output files are kept. | Cancellation — **Phase 55 for `CancellationToken` support** |
| 4.16 | Results summary panel | After execution completes (or is cancelled), show a results `StackPanel` below the progress area. Contains `Label` controls for: total jobs, succeeded count, failed count, total elapsed time, total output file size. Failed jobs are listed in a `ListBox` with expandable error details — each item shows job name and exception message. Click a failed job to see the full stack trace in a `TextBox` (read-only, multi-line). | `BatchResult` |
| 4.17 | "Open Output Directory" button | A `Button` labeled "Open Output" in the results area. On click, call `Process.Start(new ProcessStartInfo(outputDirectory) { UseShellExecute = true })` to open the directory in the OS file explorer. Also show per-job "Open" `Button` controls (visible on completed jobs in the `ListBox`) to jump to individual job output subdirectories. | -- |
| 4.18 | Save/load batch configuration | Two `Button` controls in the batch panel header: "Save Batch" serializes the entire job collection and global settings to a `.ksbatch` JSON file via `System.Text.Json`. "Load Batch" opens a file dialog filtered to `*.ksbatch`, deserializes, and populates the VM collection. Enables repeatable builds. | -- |

### Edge Cases & Notes

- If a job references a font file that no longer exists, show the status `Label` as "File not found" (red) and skip it during execution (do not abort the entire batch).
- Memory usage can spike with high parallelism and large fonts. Consider showing an estimated memory `Label` based on job count and font sizes.
- If the output directory does not exist, create it automatically via `Directory.CreateDirectory`. If files already exist, show a `Label` prompt: "Overwrite existing files?" with "Yes"/"No"/"Yes to All" `Button` controls.
- The batch panel can be non-modal — the user can switch back to the main panel while a batch runs, since the batch runs on background tasks and progress updates flow through the VM.

---

## Wave 5: Font Inspector Tool

A dedicated inspection tool for examining `.fnt` files and comparing fonts. Primarily useful for debugging, verifying output, and understanding third-party font assets.

### GUM Layout

The font inspector occupies its own panel (full content area replacement or a secondary window managed by the `Game`). It has a toolbar, metadata display, and tabbed data views.

```
+----------------------------------------------+
| Font Inspector            [Open .fnt] [Close] |
|----------------------------------------------|
| File: Roboto-Regular-24.fnt (text format)     |
|----------------------------------------------|
| [Info] [Common] [Pages] [Chars] [Kern] [Meta] |
|----------------------------------------------|
| Info Block:                                   |
|  Face:     Roboto          Size:   24         |
|  Bold:     No              Italic: No         |
|  Unicode:  Yes             Smooth: Yes        |
|  Padding:  1,1,1,1         Spacing: 1,1       |
|  Outline:  0                                  |
|----------------------------------------------|
| [Export CSV] [Compare...]                     |
+----------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 5.1 | Menu entry and panel | Add a `Button` labeled "Font Inspector" in the tools area. On click, replace the main content area with the inspector panel (or show as a separate renderable area). The inspector is independent of the main generation workflow — it can be used without loading a font for generation. A `Button` "Close" returns to the main view. | -- |
| 5.2 | Load .fnt file | A `Button` labeled "Open .fnt" with file picker (filter: `*.fnt`). Loads the file using `BmFont.Load(fntPath)`, which returns a `BmFontResult`. Access `.Model` on the result to get the `BmFontModel` for inspection. Alternatively, use `BmFont.LoadModel(fntData)` to get the model directly from raw bytes. Display the file path in a `Label` at the top of the inspector. The VM stores the loaded `BmFontModel`. | `BmFont.Load(string fntPath)` returns `BmFontResult` — access `.Model` for `BmFontModel` |
| 5.3 | Load from raw data | Two additional `Button` controls: "Load from Clipboard" reads `System.Windows.Forms.Clipboard.GetText()` (or cross-platform equivalent), converts to `byte[]`, and calls `BmFont.LoadModel(fntData)`. "Load from Text" shows a large `TextBox` (multi-line) where the user can paste .fnt content, with a `Button` "Parse" that processes it. | `BmFont.LoadModel(byte[] fntData)` |
| 5.4 | Tab navigation | A horizontal `StackPanel` of `Button` controls acting as tabs: "Info", "Common", "Pages", "Chars", "Kern", "Meta". Each tab `Button` click sets a `int ActiveInspectorTab` VM property and shows/hides the corresponding content `StackPanel`. Highlight the active tab `Button` with a distinct background color. | -- |
| 5.5 | Info block display | When the "Info" tab is active, show a vertical `StackPanel` of label pairs (two-column layout using horizontal `StackPanel` rows). Fields: Face, Size, Bold, Italic, Charset, Unicode, StretchH, Smooth, SuperSampling, Padding (top/right/bottom/left), Spacing (horizontal/vertical), Outline. Each row: `Label` for field name (fixed width, right-aligned), `Label` for value. | `BmFontModel.Info` |
| 5.6 | Common block display | When the "Common" tab is active, show fields: LineHeight, Base, ScaleW, ScaleH, Pages, Packed, AlphaChannel, RedChannel, GreenChannel, BlueChannel. Non-standard channel configurations (not 0 or 1) are shown with amber-colored `Label` text. | `BmFontModel.Common` |
| 5.7 | Pages display | When the "Pages" tab is active, show a `ListBox` of page entries. Each item: `Label` for Id, `Label` for File. If the image file exists alongside the .fnt file (check via `File.Exists`), load it as a `Texture2D` via `Texture2D.FromStream()` and display a small thumbnail in an `Image` control (64x64 scaled). Click an item to show the full page texture in a larger `Image` control below (scrollable area). If the file is missing, show a `Label` "File not found" placeholder. | `BmFontModel.Pages` |
| 5.8 | Character table | When the "Chars" tab is active, show a `ScrollViewer` containing a vertical `StackPanel` of character rows. Each row is a horizontal `StackPanel` with `Label` controls for: Id (decimal), Unicode character preview, X, Y, Width, Height, XOffset, YOffset, XAdvance, Page, Channel. Add a `TextBox` filter at the top — typing a character or codepoint filters the list. Show total count in a header `Label`. For fonts with 1000+ characters, only render visible rows (virtualize by tracking scroll position and only creating rows within the visible range). | `BmFontModel.Characters` |
| 5.9 | Character detail view | When a character row is clicked (track via `ListBox.SelectedIndex` or custom click detection), show a detail `StackPanel` below the list. Contains: an `Image` showing the glyph cropped from the atlas `Texture2D` (use `SpriteBatch.Draw` with a source rectangle to render the glyph region to a `RenderTarget2D`), all metrics visualized graphically (draw origin point, bounding box, advance width line, and offset arrows via `SpriteBatch` primitives), and `Label` controls for all raw field values. | -- |
| 5.10 | Kerning table | When the "Kern" tab is active, show a `ScrollViewer` with kerning pair rows. Each row: `Label` for First (decimal + char preview), `Label` for Second (decimal + char preview), `Label` for Amount. Header `Label` shows total count. Add a `TextBox` filter to show pairs for a specific character. Virtualize for large pair counts. | `BmFontModel.KerningPairs` |
| 5.11 | Kerning pair visualization | When a kerning pair row is selected, show a detail area with two renders: (a) the two characters side-by-side WITHOUT kerning (glyphs placed at their default xAdvance), and (b) the same two characters WITH the kerning amount applied. Draw both using `SpriteBatch` with the atlas `Texture2D` as source. Overlay a line showing the kerning pixel adjustment (red if negative/tighter, green if positive/looser). | -- |
| 5.12 | Extended metadata display | When the "Meta" tab is active, show fields: GeneratorName, GeneratorVersion, SdfEnabled, SdfSpread, Effects list, OriginalFontFamily, OriginalFontStyle, GenerationTimestamp. If `ExtendedMetadata` is null, show a `Label`: "No extended metadata present." | `BmFontModel.ExtendedMetadata` |
| 5.13 | Statistics summary | A `StackPanel` at the bottom of the inspector (visible on all tabs) showing computed statistics in `Label` controls: total character count, total kerning pairs, atlas utilization percentage (sum of glyph pixel areas / total atlas pixel area), average glyph dimensions, min/max glyph dimensions, total atlas area across all pages. | Computed from `BmFontModel` |
| 5.14 | Export tables as CSV | A `Button` labeled "Export" with a `ComboBox` for format: "Characters CSV", "Kerning Pairs CSV", "All Data JSON". On click, open a save file dialog, then serialize using `System.Text.Json` (JSON) or manual CSV generation with `StringBuilder` (CSV with headers). Write to disk via `File.WriteAllText`. | -- |
| 5.15 | Font comparison mode | A `Button` labeled "Compare..." opens a file dialog to load a second `.fnt` file. Show a two-column layout: left column shows Font A data, right column shows Font B data. Differences are highlighted with amber `Label` text. Below, show a summary `Label`: "Font A has 95 characters, Font B has 128 characters. 33 characters added, 0 removed." List characters present in A but not B (and vice versa) in a `ListBox`. Show kerning pair count delta and atlas size differences. | -- |

### Edge Cases & Notes

- The inspector must handle both text-format and binary-format .fnt files (BMFont supports both). `BmFont.Load` handles this internally; the inspector should show which format was detected via a `Label` next to the file path.
- If atlas texture files referenced by the .fnt are missing, show placeholder `Label` text "File not found" in the pages list. Do not error out.
- Very large .fnt files (10,000+ characters for CJK fonts) require virtualized scrolling — track `ScrollViewer` scroll position and only create/update `StackPanel` rows within the visible range to avoid UI lag.
- Comparison should handle .fnt files from different generators (not just KernSmith) gracefully.

---

## Wave 6: Color Font Support

Color fonts (COLRv0/COLRv1 and CBDT/CBLC) contain multi-colored glyph data, commonly used for emoji. The UI must expose color font options when a color-capable font is loaded.

### GUM Layout

Color font controls are a collapsible section in the rendering settings area, shown only when a color font is detected.

```
+----------------------------------------------+
| [x] Color Rendering                          |
|----------------------------------------------|
| Type: COLR/CPAL          Palettes: 3         |
|----------------------------------------------|
| Palette: [ComboBox: Palette 0 (Default)  v]  |
| Colors:  [##][##][##][##][##][##][##][##]     |
|----------------------------------------------|
| [x] Show color preview                       |
| Background: [ComboBox: Checkerboard      v]  |
|----------------------------------------------|
| (i) Color output requires RGBA32 format.     |
+----------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 6.1 | Detect color font on load | After loading a font, check `FontInfo` for COLR/CPAL or CBDT/CBLC table indicators. Set a `bool IsColorFont` VM property. Show a `Label` badge "Color Font" with distinct color (e.g., a rainbow-like multi-color or magenta) in the font info `StackPanel`. Also show the color font type (COLR, CBDT, SVG) in a second `Label`. Bind badge `Visible` to `IsColorFont`. | `FontInfo` color table detection |
| 6.2 | Color font toggle | Add a `CheckBox` labeled "Color Rendering" in the rendering settings section. Bind `Visible` to `IsColorFont`. Default: checked when a color font is detected. When unchecked, renders glyphs in standard grayscale/alpha mode. Bind to `bool ColorRenderingEnabled` VM property, which maps to `WithColorFont(bool)`. | `WithColorFont(bool)` |
| 6.3 | Color palette detection | Read the CPAL table data from `FontInfo` to determine how many color palettes are available. Show a `Label` "N palettes available" in the color font section. Store `int PaletteCount` on the VM. If no CPAL table exists or only 1 palette, hide the palette selector. **Note:** `FontInfo` currently only has `HasColorGlyphs` (boolean) — no palette count or color data. This task depends on Phase 55 for `FontInfo` expansion with CPAL palette information. | `FontInfo` palette data — **Phase 55 prerequisite** |
| 6.4 | Color palette selector | A `ComboBox` labeled "Palette" with items "Palette 0", "Palette 1", etc. If palette names are available from the CPAL table, show them instead (e.g., "Default", "Dark Mode"). Default to palette 0. Bind `SelectedIndex` to `int SelectedPaletteIndex` VM property, which maps to `WithColorPaletteIndex(int)`. Only visible when color rendering is enabled and `PaletteCount > 1`. **Note:** Depends on Phase 55 `FontInfo` expansion — palette count is not currently available (see task 6.3). | `WithColorPaletteIndex(int)` |
| 6.5 | Palette preview strip | Below the palette `ComboBox`, show a horizontal `StackPanel` of colored rectangles. For each color in the selected palette (up to 32 visible), render a small colored square (16x16) as an `Image` control. Create each square's `Texture2D` programmatically: `new Texture2D(graphicsDevice, 1, 1)` filled with the palette color, then scale via the `Image` control's width/height. If more than 32 colors, add a `Label` showing "+N more". For color hex display, show the value in a `Label` next to each swatch on hover (track mouse position in `Update` and show/hide a tooltip `Label`). **Note:** Depends on Phase 55 `FontInfo` expansion — `FontInfo` currently only has `HasColorGlyphs` (boolean), no palette color data. This task cannot render color swatches until `FontInfo` exposes CPAL palette colors. | **Phase 55 prerequisite** |
| 6.6 | Color glyph preview | When color rendering is enabled, the main font preview renders glyphs in color (RGBA). The preview panel's background must be configurable: add a `ComboBox` labeled "Background" with items "White", "Black", "Checkerboard" (default). For checkerboard, render a classic alpha-check pattern using `SpriteBatch` (alternating gray/white 8x8 rectangles) behind the glyphs. Bind to `int PreviewBackground` VM property. | -- |
| 6.7 | RGBA format auto-selection | When `ColorRenderingEnabled` is set to true, automatically set the texture format VM property to RGBA32. Show a `Label` info banner: "Color font output requires RGBA texture format. Format has been set to RGBA32." If the user manually changes to a non-RGBA format via the format `ComboBox`, show a `Label` warning (amber): "Non-RGBA format may lose color data." | Auto-set pixel format |
| 6.8 | Color font + effects interaction | When `ColorRenderingEnabled` is true AND any effect is enabled, show per-effect warnings via `Label` controls: Outline — "Outline may not follow COLR layer boundaries", Gradient — "Gradient will overwrite color data", Shadow — no warning (shadow composites correctly). Disable the outline and gradient `CheckBox` controls (set `Enabled = false`) with an explanation `Label`, or allow with explicit user confirmation via a "Proceed Anyway" `Button`. | -- |
| 6.9 | Color vs. monochrome comparison | In the preview area, add a `CheckBox` labeled "Compare Monochrome". When checked, render the preview twice side-by-side: left half shows color glyphs, right half shows the same glyphs in grayscale (generated with `ColorRenderingEnabled = false`). Use `SpriteBatch` to draw both `Texture2D` renders into the preview area with a dividing line. | -- |
| 6.10 | Atlas size warning for color fonts | When color rendering is enabled, show a `Label` with estimated texture size comparison: "Color: ~2.4 MB (RGBA32), Monochrome: ~600 KB (Grayscale8)". Calculate from: `pageWidth * pageHeight * bytesPerPixel * pageCount`. Help users understand the 4x size impact. | -- |
| 6.11 | Color font test characters | When a color font is loaded, add a `Button` labeled "Show Color Glyphs Only" in the character set area. On click, filter the character set `ListBox` to show only codepoints that have color glyph data (cross-reference with COLR/CBDT table coverage from `FontInfo`). A second click ("Show All") restores the full character set. Bind to `bool ShowOnlyColorGlyphs` VM property. | -- |
| 6.12 | Persist color font settings | Save `ColorRenderingEnabled` and `SelectedPaletteIndex` in project/preset files. Restore on load. If the saved palette index exceeds the available palettes in a newly-loaded font, fall back to index 0 and show a `Label` notification: "Saved palette index N not available, using palette 0." | -- |

### Edge Cases & Notes

- Not all glyphs in a color font have color data. Missing color data falls back to standard rendering — the UI should not show errors for this.
- COLRv1 (variable color) is newer and may not be fully supported by the rasterizer. Detect and show a `Label` banner "COLRv1 support is limited" if applicable.
- Color emoji fonts tend to produce very large atlas textures. Consider suggesting smaller character subsets for color fonts via the size warning `Label`.
- Some color fonts have palettes for dark mode vs. light mode. Display palette names from CPAL if available.

---

## Wave 7: Fallback Character & Font Collections

Two smaller but important features: configuring a fallback character for missing glyphs, and handling `.ttc` (TrueType Collection) files that contain multiple font faces.

### GUM Layout

```
Fallback Character (in character set panel):
+----------------------------------------------+
| Fallback Character:                           |
|  [TextBox: ?]  [ComboBox: Common presets  v]  |
|  Preview: [Image: glyph preview]              |
+----------------------------------------------+

Font Collection (in font info panel):
+----------------------------------------------+
| Font Collection (4 faces)                     |
| Face: [ComboBox: Arial - Regular          v]  |
|  Family: Arial    Style: Regular              |
|  Face 1 of 4     Glyphs: 3,381               |
+----------------------------------------------+
```

### Tasks

| # | Task | Details | API Mapping |
|---|------|---------|-------------|
| 7.1 | Fallback character input | Add a `TextBox` labeled "Fallback Character" (via adjacent `Label`) in the character set `StackPanel`. Single-character input — validate on focus-lost that the text is exactly 1 character. Bind to a `char? FallbackCharacter` VM property. When set, maps to `WithFallbackCharacter(char)`. Default: empty (no fallback, missing glyphs produce a warning). **Note:** `WithFallbackCharacter(char)` cannot handle characters above U+FFFF (surrogate pairs). The UI must validate that the entered character is within the BMP (U+0000-U+FFFF). For characters above U+FFFF, Phase 55 would need to add `WithFallbackCodepoint(int)`. | `WithFallbackCharacter(char)` — BMP only |
| 7.2 | Fallback character common presets | A `ComboBox` next to the `TextBox` with common fallback choices as items: "? (U+003F)", "[] (U+25A1)", "<> (U+25C7)", "FFFD (U+FFFD)", ".notdef (built-in)". Selecting an item sets the `TextBox` text and the VM property. `SelectedIndex = -1` when the user types a custom character. | -- |
| 7.3 | Fallback glyph preview | When a fallback character is set, render the glyph at the current font size to a small `Texture2D` (using the rasterizer on a background thread) and display in an `Image` control (32x32 area) next to the `TextBox`. If the fallback character itself is not in the font (rasterizer returns empty/missing glyph), show a `Label` warning (red): "The fallback character itself is missing from the font." | -- |
| 7.4 | Missing glyph report | After generation (or in preview), show a `Label` with count: "N characters missing (replaced by fallback)". Add a `Button` "Show Missing" that populates a `ListBox` with the missing codepoints (each item shows the codepoint and character name if available). This helps users verify their character set coverage. Bind to `List<int> MissingCodepoints` VM property, populated by the generation result. | -- |
| 7.5 | Detect .ttc file on load | When a `.ttc` file is loaded, detect that it is a collection (check `FontInfo` metadata for face count > 1 or file extension). Set `bool IsFontCollection` and `int FaceCount` on the VM. Show a `Label` badge "Font Collection" with face count in the font info `StackPanel`. Bind badge `Visible` to `IsFontCollection`. | -- |
| 7.6 | Face selector ComboBox | When `IsFontCollection` is true, show a `ComboBox` labeled "Font Face" (via adjacent `Label`) above the font info section. Populate items from the collection's face metadata, showing each face's family name and style (e.g., "Arial -- Regular", "Arial -- Bold", "Arial -- Italic"). Default to index 0. Bind `SelectedIndex` to `int SelectedFaceIndex` VM property. Bind `Visible` to `IsFontCollection`. | `WithFaceIndex(int)` |
| 7.7 | Face selection to preview update | When `SelectedFaceIndex` changes, reload the font with `WithFaceIndex(selectedIndex)` on a background task. Update the entire UI: font info `Label` controls, preview `Image`, metrics, variation axes (if the selected face is variable), and character set coverage. Show a "Loading face..." `Label` during the reload. | `WithFaceIndex(int)` |
| 7.8 | Face info display | Below the face selector `ComboBox`, show a `StackPanel` with `Label` controls for: family name, style name, face index ("Face N of M" — display as 1-based for user clarity, map to 0-based internally), glyph count, units per em. Update these `Label` values via `SetBinding()` to VM properties when the face selection changes. | `FontInfo` for selected face |
| 7.9 | Face comparison (optional) | A `Button` labeled "Compare Faces" below the face selector. On click, render the same sample text (e.g., "AaBbCc 123") in each face of the collection on separate `RenderTarget2D` targets, then display all as `Image` controls in a vertical `StackPanel` within a `ScrollViewer`. Each row shows: face name `Label` and the rendered preview `Image`. Useful for quickly identifying which face to use. Generate all renders on background tasks. | -- |
| 7.10 | Persist fallback and face settings | Save `FallbackCharacter` and `SelectedFaceIndex` in project/preset files. On reload, restore the settings. If the face index exceeds the face count in a newly-loaded .ttc, fall back to index 0 and show a `Label` notification. | -- |
| 7.11 | Face index for non-.ttc files | When a non-.ttc font is loaded, set `IsFontCollection = false` on the VM. The face selector `ComboBox` hides automatically (bound to `IsFontCollection`). Do not call `WithFaceIndex` (default of 0 is implicit). If a saved project has a face index but the loaded font is not a .ttc, ignore the saved value silently. | -- |
| 7.12 | Fallback character validation | Validate on focus-lost that the `TextBox` contains a single Unicode character within the BMP (U+0000-U+FFFF). **Note:** `WithFallbackCharacter(char)` cannot accept characters above U+FFFF. If a surrogate pair (e.g., emoji) is entered, show a `Label` warning: "Characters above U+FFFF are not supported as fallback. Use a BMP character (U+0000-U+FFFF)." Reject the input and revert to the previous value. Phase 55 may add `WithFallbackCodepoint(int)` for supplementary plane support. If a multi-character string is pasted, take only the first BMP character, update the `TextBox`, and show a brief `Label` notice: "Only the first character was used." | -- |

### Edge Cases & Notes

- The replacement character U+FFFD is a common fallback but may not be present in every font. Always verify the fallback character exists in the loaded font after setting it.
- Some .ttc files contain faces that share glyph data but have different metrics or names. Face switching should be fast because the raw font data is already loaded.
- The face index is zero-based. Display "Face 1 of 4" in the UI but map to `WithFaceIndex(0)` internally.
- If a .ttc is loaded from system fonts, the system font API may expose each face separately. Deduplicate if necessary.

---

## Testing Strategy

### Unit Tests

| Test Area | What to Test |
|---|---|
| Variable font axis binding | Verify `WithVariationAxis` is called with correct tag/value for each slider VM property change |
| Named instance selection | Verify all axis VM values update when an instance ComboBox index is selected |
| SDF toggle mapping | Verify `WithSdf(true/false)` is called based on the CheckBox VM property |
| Custom glyph pixel extraction | Verify `Texture2D.GetData<byte>()` to `byte[]` conversion for both Grayscale8 and RGBA32 |
| Batch job creation | Verify `BatchJob` is created with correct options snapshot from current VM state |
| Font inspector model parsing | Verify `BmFont.Load` / `BmFont.LoadModel` round-trip |
| Fallback character validation | Verify single-char constraint, surrogate pair handling |
| Face index binding | Verify `WithFaceIndex` is called with correct 0-based index from ComboBox SelectedIndex |

### Integration Tests

| Test Area | What to Test |
|---|---|
| Variable font end-to-end | Load a variable font, set axes via VM, generate, verify output reflects axis values |
| SDF generation | Enable SDF via VM, generate, verify output .fnt contains SDF metadata |
| Custom glyph in atlas | Add a custom glyph via `Texture2D.FromStream`, generate, verify it appears in the atlas at the correct codepoint |
| Batch generation | Create 3 jobs in VM, run batch, verify all outputs are written correctly |
| Font inspector round-trip | Generate a font, load the output .fnt in inspector, verify all fields match |
| Color font palette | Load a color font, select palette 1 via VM, generate, verify palette index is used |
| .ttc face selection | Load a .ttc, select face 2 via VM, generate, verify the correct face is rasterized |

### Manual Test Scenarios

| Scenario | Steps |
|---|---|
| Variable font slider interaction | Load a variable font (e.g., Recursive, Inter). Drag weight Slider. Verify preview Image updates after 300ms debounce. Select a named instance from the ComboBox. Verify sliders jump. Click Reset All Axes. |
| SDF with engine preset | Check the SDF CheckBox. Click "Unity TMP" Button. Verify size TextBox, spread Slider, and channel ComboBox change. Generate. Open output in Unity. |
| Custom glyph workflow | Click "Add Custom Glyph". Browse a 32x32 PNG. Enter U+E000 in codepoint TextBox. Set xAdvance to 36. Click Confirm. Verify it appears in the ListBox. Generate. Verify the icon appears in the atlas and .fnt file. |
| Batch of 5 fonts | Open batch panel. Add 5 different fonts at sizes 16, 24, 32, 48, 64 via "Add Current" and "Add Manual". Set parallelism Slider to 2. Click Start Batch. Verify all 5 outputs exist. Cancel mid-batch and verify partial results. |
| Inspector comparison | Generate the same font at two different sizes. Open inspector. Load first .fnt via "Open .fnt". Click "Compare..." and load second .fnt. Verify character count is the same but metrics differ. |

---

## Performance Considerations

| Concern | Mitigation |
|---|---|
| Variable font re-rasterization on slider drag | 300ms debounce timer in `Update` loop + `CancellationTokenSource` for in-flight background `Task`. Consider a low-res fast preview during drag, full render on release. |
| SDF generation time | SDF is 3-10x slower than normal rasterization. Show estimated time via `Label`. Generate on explicit "Generate" `Button` click, not on every settings change. |
| Custom glyph image loading | `Texture2D.FromStream()` is fast for small PNGs. Pixel extraction via `GetData<byte>()` for generation happens on a background `Task`. Preview `Image` updates on main thread. |
| Batch memory usage | With high parallelism, multiple fonts + atlases are in memory simultaneously. Show estimated memory `Label`. Consider sequential fallback if memory is low. |
| Font inspector with large .fnt | CJK fonts may have 20,000+ characters. Virtualize `ScrollViewer` content — only create `StackPanel` rows for visible range (track scroll offset in `Update`). Lazy-populate character detail view on selection. |
| Color font atlas size | RGBA32 atlases are 4x larger than Grayscale8. Show size estimates via `Label`. Suggest character set reduction for color fonts. |
| GUM control creation for axis rows | Creating many GUM controls dynamically (e.g., 14 axis sliders) can be slow. Cache and reuse control instances when switching between fonts. Pool `Texture2D` objects for axis visualization thumbnails. |

---

## Open Questions

| # | Question | Impact |
|---|----------|--------|
| Q1 | ~~Is `WithSdfSpread(int)` exposed on the builder, or is spread controlled internally?~~ | **Resolved:** `WithSdfSpread(int)` does not exist yet. Phase 55 prerequisite. Task 2.3 UI control is a placeholder. |
| Q2 | ~~Does `BmFont.GenerateBatch` provide per-job progress callbacks, or only completion?~~ | **Resolved:** `GenerateBatch()` has no progress callbacks and no `CancellationToken`. Phase 55 prerequisite. UI must wrap individual `BmFont.Generate()` calls for granular progress (tasks 4.13, 4.15). |
| Q3 | ~~Can `FontInfo` enumerate CPAL palette colors, or only the palette count?~~ | **Resolved:** `FontInfo` currently only has `HasColorGlyphs` (boolean) — no palette count or color data. Phase 55 prerequisite for tasks 6.3-6.5. |
| Q4 | Is COLRv1 (variable color) supported by the FreeType rasterizer? | Affects Wave 6 color font rendering scope. |
| Q5 | Should the font inspector be a full content area replacement or a separate `Game` window? | MonoGame DesktopGL supports only one window natively. A full panel swap is simpler; a secondary window requires SDL2 multi-window handling. |
| Q6 | Should batch generation support system font names (not just file paths)? | Affects Wave 4 job creation UI. `BatchJob` has a `SystemFont` property, so the API supports it. |
| Q7 | What is the maximum practical number of custom glyphs before atlas packing degrades? | Affects whether Wave 3 needs a glyph count limit or warning threshold. |
| Q8 | How should `Texture2D` lifetimes be managed for custom glyph previews? | Custom glyph `Texture2D` objects must be disposed when removed from the list and when the app exits. Use a dispose-tracking collection or `IDisposable` on the VM. |

---

## Success Criteria

- [ ] Variable font axes are interactive with GUM `Slider`, `TextBox`, and named instance `ComboBox` shortcuts
- [ ] Axis slider changes produce correct `WithVariationAxis` calls on the builder
- [ ] SDF `CheckBox` toggle and spread `Slider` configuration map correctly to the API
- [ ] SDF engine preset `Button` controls configure all related settings in one click
- [ ] Custom glyphs can be imported from PNG files via `Texture2D.FromStream()` with full control over codepoint, size, format, and advance
- [ ] Custom glyphs appear correctly in the generated atlas and .fnt output
- [ ] Batch generation processes multiple jobs with progress `Label` reporting and cancellation
- [ ] Batch results show per-job status, errors, and output locations in the results `StackPanel`
- [ ] Font inspector loads and displays all .fnt data (info, common, pages, characters, kerning) using GUM `Label` and `ListBox` controls
- [ ] Font comparison highlights differences between two .fnt files
- [ ] Color font rendering produces correct RGBA output with palette `ComboBox` selection
- [ ] Fallback character `TextBox` is applied to missing glyphs during generation
- [ ] Font collection (.ttc) face `ComboBox` selector loads the correct face
- [ ] All settings persist in project/preset files and restore correctly
- [ ] No UI freezes during long operations — all heavy work runs on background `Task` with VM property updates on the main thread
- [ ] All GUM controls use `SetBinding()` for ViewModel wiring (no direct control state manipulation outside initialization)
