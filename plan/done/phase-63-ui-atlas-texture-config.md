# Phase 63 — Atlas & Texture Configuration UI

> **Status**: Complete
> **Completed**: 2026-03-22. Atlas size uses power-of-two ComboBoxes instead of free-text. Padding uses cross/diamond layout. Moved from left panel to effects panel to eliminate scrolling. Post-generation metrics shown in status bar and toolbar. BitDepth/Compression deferred (no library support).
> **Created**: 2026-03-21
> **Updated**: 2026-03-21
> **Goal**: Build comprehensive atlas packing and texture output configuration using GUM UI (code-only) on MonoGame (DesktopGL), including size controls, packing algorithm selection, padding/spacing with BMFont-style directional layout, channel configuration, output format selection, presets, and post-generation metrics.

---

## Architectural Context

### Framework Stack

| Layer | Technology | Role |
|-------|-----------|------|
| **Rendering** | MonoGame (DesktopGL) | Window, SpriteBatch, Texture2D, input |
| **UI Framework** | GUM UI (code-only, no GUMX files) | Layout, controls, data binding |
| **UI Utilities** | MonoGame.Extended | Camera, input helpers, shapes |
| **Data Binding** | GUM ViewModel with `Get<T>` / `Set<T>` + `SetBinding()` | Two-way binding, property notification |
| **Library** | KernSmith (direct project reference) | `BmFont.Builder()` pipeline, in-process |

### GUM UI Approach (Code-Only)

All UI is constructed in C# code. There are no XAML files, no GUMX files, no markup. Controls are instantiated, configured, and added to parent containers programmatically. Data binding uses GUM's `ViewModel` base class with `Get<T>`/`Set<T>` properties and `SetBinding()` to connect controls to view model properties.

```csharp
// Example pattern used throughout this phase
var slider = new Slider();
slider.Minimum = 0.80;
slider.Maximum = 0.99;
slider.BindingContext = viewModel;
slider.SetBinding(nameof(slider.Value), nameof(viewModel.PackingEfficiency));
parentStack.Children.Add(slider);
```

### Panel Placement

The atlas/texture configuration panel occupies a section of the right-side configuration area in the three-panel layout from Phase 60. It is a `ScrollViewer` containing a vertical `StackPanel` of collapsible sections. Each section is a container with a header row (Label + optional CheckBox toggle) and a content `StackPanel` that can be shown/hidden.

---

## API Surface Reference

The following KernSmith library APIs are exercised by this phase:

| API Method / Property | Purpose | Default |
|---|---|---|
| `WithMaxTextureSize(int w, int h)` | Maximum atlas page dimensions | 1024 x 1024 |
| `WithPadding(int up, int right, int down, int left)` | Per-glyph cell padding (pixels) | 0, 0, 0, 0 |
| `WithSpacing(int horizontal, int vertical)` | Inter-glyph gutters (pixels) | 1, 1 |
| `WithPackingAlgorithm(PackingAlgorithm)` | MaxRects or Skyline | MaxRects |
| `WithPowerOfTwo(bool)` | Constrain dimensions to power-of-two | true |
| `WithAutofitTexture(bool)` | Shrink atlas to smallest fitting size | false |
| `WithPackingEfficiency(float)` | Target efficiency 0.0 - 1.0 | 0.90 |
| `WithTextureFormat(TextureFormat)` | Png, Tga, Dds | Png |
| `WithKerning(bool)` | Include/exclude kerning pairs | true |
| `WithChannelPacking(bool)` | Enable packing different glyph sets into separate RGBA channels | false |
| `WithChannels(ChannelConfig)` | Per-channel content type assignment (glyph/outline/zero/one per A/R/G/B) | Glyph in all |
| `BmFontResult.Model` | Access CharEntry, KerningEntry, PageEntry | -- |
| `BmFontResult.GetPngData(int page)` | Get PNG bytes for a page | -- |
| `BmFontResult.GetTgaData(int page)` | Get TGA bytes for a page | -- |
| `BmFontResult.GetDdsData(int page)` | Get DDS bytes for a page | -- |
| `BmFontResult.Metrics` | Per-stage timing breakdown | null unless collected |

> **Note — Output format**: There is no `WithOutputFormat()` builder method. Output format (Text/XML/Binary) is set at export time via `BmFontResult.ToFile(path, format)`. The UI stores this setting separately in view model state.
>
> **Note — CollectMetrics**: There is no `WithCollectMetrics()` builder method yet. Set `FontGeneratorOptions.CollectMetrics = true` directly. A builder method is tracked in Phase 55.
>
> **Note — ChannelPacking vs ChannelConfig**: `PackMultipleChannels` maps to `WithChannelPacking(bool)` which enables packing different glyph sets into separate RGBA channels. The per-channel A/R/G/B dropdowns map to `WithChannels(ChannelConfig)` which controls what content type each channel holds. These are different features.

### BMFont Export Options Covered

This phase covers the following BMFont export option categories (matching the BMFont application's Export Options dialog):

- **Layout**: Padding (4-directional with A/B/C/D visual), Spacing (horizontal/vertical), Equalize cell heights, Force offsets to zero, Adaptive padding factor
- **Texture**: Width/Height numeric inputs, Bit depth (8/32) radio buttons
- **Channels**: Pack chars in multiple channels toggle, per-channel (A/R/G/B) Value dropdown (outline/glyph/one/zero) + Invert checkbox
- **Presets**: Custom, White text with alpha, Black text with alpha, White text on black, Outlined text with alpha, Pack text and outline in same channel
- **File format**: Font descriptor (Text/XML/Binary) radio buttons, Textures dropdown (png/dds/tga), Compression dropdown

---

## View Model Design

### AtlasConfigViewModel

```csharp
public class AtlasConfigViewModel : ViewModel
{
    // Wave 1 - Atlas Size
    public int MaxWidth { get => Get<int>(); set => Set(value); }           // 1024
    public int MaxHeight { get => Get<int>(); set => Set(value); }          // 1024
    public bool LinkDimensions { get => Get<bool>(); set => Set(value); }   // false
    public bool PowerOfTwo { get => Get<bool>(); set => Set(value); }       // true
    public bool AutofitTexture { get => Get<bool>(); set => Set(value); }   // false
    public float PackingEfficiency { get => Get<float>(); set => Set(value); } // 0.90f
    public string EstimatedSizeText { get => Get<string>(); set => Set(value); }
    public bool ShowSizeWarning { get => Get<bool>(); set => Set(value); }

    // Wave 2 - Padding & Spacing
    public int PaddingUp { get => Get<int>(); set => Set(value); }
    public int PaddingRight { get => Get<int>(); set => Set(value); }
    public int PaddingDown { get => Get<int>(); set => Set(value); }
    public int PaddingLeft { get => Get<int>(); set => Set(value); }
    public bool UniformPadding { get => Get<bool>(); set => Set(value); }
    public int SpacingH { get => Get<int>(); set => Set(value); }           // 1
    public int SpacingV { get => Get<int>(); set => Set(value); }           // 1
    public bool LinkSpacing { get => Get<bool>(); set => Set(value); }      // true
    public bool EqualizeCellHeights { get => Get<bool>(); set => Set(value); }
    public bool ForceOffsetsToZero { get => Get<bool>(); set => Set(value); }
    public float AdaptivePaddingFactor { get => Get<float>(); set => Set(value); } // 1.0f

    // Wave 3 - Packing Algorithm
    public int SelectedAlgorithm { get => Get<int>(); set => Set(value); }  // 0=MaxRects, 1=Skyline
    public string AlgorithmDescription { get => Get<string>(); set => Set(value); }
    public bool ShowAlgorithmComparison { get => Get<bool>(); set => Set(value); }

    // Wave 4 - Output Format & Channels
    public int TextureFormatIndex { get => Get<int>(); set => Set(value); } // 0=Png, 1=Tga, 2=Dds
    public int DescriptorFormatIndex { get => Get<int>(); set => Set(value); } // 0=Text, 1=Xml, 2=Binary
    public bool IncludeKerning { get => Get<bool>(); set => Set(value); }   // true
    public int KerningPairCount { get => Get<int>(); set => Set(value); }
    public int BitDepth { get => Get<int>(); set => Set(value); }           // 0=32-bit, 1=8-bit
    public bool PackMultipleChannels { get => Get<bool>(); set => Set(value); }
    public int ChannelPresetIndex { get => Get<int>(); set => Set(value); } // 0=Custom, 1..5=presets
    public int ChannelA_Value { get => Get<int>(); set => Set(value); }
    public bool ChannelA_Invert { get => Get<bool>(); set => Set(value); }
    public int ChannelB_Value { get => Get<int>(); set => Set(value); }     // repeat for R, G, B
    // ... (R, G, B channel properties follow same pattern)

    // Wave 5 - Metrics
    public bool CollectMetrics { get => Get<bool>(); set => Set(value); }
    // ... (read-only display properties populated after generation)

    // Wave 6 - Multi-page Navigation
    public int CurrentPage { get => Get<int>(); set => Set(value); }
    public int TotalPages { get => Get<int>(); set => Set(value); }
}
```

### Binding to KernSmith API

The `GenerationService` reads the view model state and translates it to `FontGeneratorOptions`:

```csharp
var options = BmFont.Builder()
    .WithMaxTextureSize(vm.MaxWidth, vm.MaxHeight)
    .WithPadding(vm.PaddingUp, vm.PaddingRight, vm.PaddingDown, vm.PaddingLeft)
    .WithSpacing(vm.SpacingH, vm.SpacingV)
    .WithPackingAlgorithm(vm.SelectedAlgorithm == 0
        ? PackingAlgorithm.MaxRects : PackingAlgorithm.Skyline)
    .WithPowerOfTwo(vm.PowerOfTwo)
    .WithAutofitTexture(vm.AutofitTexture)
    .WithPackingEfficiency(vm.PackingEfficiency)
    .WithTextureFormat(textureFormats[vm.TextureFormatIndex])
    .WithKerning(vm.IncludeKerning)
    .WithChannelPacking(vm.PackMultipleChannels)
    .WithChannels(BuildChannelConfig(vm));

// Output format is not set on the builder -- it is specified at export time:
//   result.ToFile(path, descriptorFormats[vm.DescriptorFormatIndex]);

// CollectMetrics has no builder method yet (Phase 55); set directly on options:
//   options.CollectMetrics = vm.CollectMetrics;
```

---

## Wave 1 — Atlas Size Configuration Panel

### Goal

Provide numeric controls for maximum atlas dimensions, power-of-two constraint, autofit, and packing efficiency. Show a real-time estimated atlas size before the user clicks Generate.

### Tasks

| # | Task | Details | GUM Control | API Mapping |
|---|---|---|---|---|
| 1.1 | **Max texture width dropdown** | ComboBox restricted to powers of 2: 256, 512, 1024, 2048, 4096, 8192. Default: 1024. Label via `Label` control: "Max Width (px)". Placed in a horizontal `StackPanel` alongside height control. Bind `SelectedIndex` to `MaxWidth` via `SetBinding()`. | `ComboBox` | `WithMaxTextureSize(w, h)` -- `w` parameter |
| 1.2 | **Max texture height dropdown** | Identical ComboBox to width, placed adjacent. Default: 1024. Label: "Max Height (px)". Bind `SelectedIndex` to `MaxHeight`. | `ComboBox` | `WithMaxTextureSize(w, h)` -- `h` parameter |
| 1.3 | **Link width/height toggle** | `CheckBox` between the width and height ComboBoxes, labeled "Link". When checked, changing width auto-sets height to the same value and vice versa, enforcing square atlas dimensions. Toggle state persists across sessions via user settings JSON file. View model property change handler synchronizes the paired value. | `CheckBox` | UI-only; both values passed to single `WithMaxTextureSize` call |
| 1.4 | **Power-of-two toggle** | `CheckBox` labeled "Constrain to power-of-two". Default: checked. When checked, the final atlas dimensions are rounded up to the nearest power of two. | `CheckBox` | `WithPowerOfTwo(bool)` |
| 1.5 | **Autofit texture toggle** | `CheckBox` labeled "Auto-shrink to fit". Default: unchecked. When checked, the atlas is cropped to the smallest rectangle that contains all packed glyphs (still respecting power-of-two if enabled). | `CheckBox` | `WithAutofitTexture(bool)` |
| 1.6 | **Packing efficiency slider** | `Slider` with `Minimum=0.80`, `Maximum=0.99`, step 0.01. `Label` to the left: "Packing Efficiency". Second `Label` to the right displaying the current value as a percentage (e.g., "90%"), updated via binding. Default: 0.90. Horizontal `StackPanel` layout: `[Label] [Slider] [Label]`. | `Slider` + `Label` | `WithPackingEfficiency(float)` |
| 1.7 | **Estimated atlas size display** | Read-only `Label` below the size controls showing "Estimated: 1024 x 512 (1 page)" or "Estimated: 2048 x 2048 (3 pages)". Bound to `EstimatedSizeText` property. Updated on every settings change via `AtlasSizeEstimator` using the current character set, font size, padding, and spacing. Monospace `BitmapFont` for alignment. | `Label` | `AtlasSizeEstimator.Estimate(...)` |
| 1.8 | **Size warning indicator** | `Label` with amber/yellow text color, shown when the estimated atlas size exceeds the configured max texture size. Bound to `ShowSizeWarning` property for visibility. Message: "Estimated glyph area exceeds maximum texture size. Multiple pages will be required." Displayed inline below the estimate. | `Label` | UI validation against estimate vs max size |
| 1.9 | **Preset size buttons** | Row of four `Button` controls in a horizontal `StackPanel`: "512", "1024", "2048", "4096". Click handler on each sets both width and height ComboBox selections to that value (regardless of link toggle state). Provides fast access to common square sizes. | `Button` (x4) | Sets both `w` and `h` for `WithMaxTextureSize` |
| 1.10 | **Section container** | All Wave 1 controls wrapped in a collapsible section: a `StackPanel` with a header row containing a `Label` "Atlas Size" and a `Button` to toggle collapse (show/hide the content `StackPanel`). Expanded by default. Collapse state saved to user settings JSON. Internal layout uses nested `StackPanel` containers with consistent 8px margins. | `StackPanel`, `Label`, `Button` | -- |

### Interaction Patterns

- Changing any size-related control triggers a debounced (300ms) re-estimation of atlas size. Debounce implemented in the view model with a `System.Threading.Timer`.
- The estimate uses the currently loaded font, current character set, current font size, and current padding/spacing values. If no font is loaded, the estimate label shows "Load a font to see estimates".
- When the link toggle is checked and the user changes width, the `MaxWidth` property setter detects `LinkDimensions == true` and sets `MaxHeight` to match (and vice versa).
- Preset buttons set both MaxWidth and MaxHeight directly, bypassing the link toggle check.

---

## Wave 2 — Padding & Spacing Controls

### Goal

Expose per-glyph padding (up/right/down/left) in a BMFont-style directional layout with labeled positions (A/B/C/D), inter-glyph spacing (horizontal/vertical), and BMFont-specific options: equalize cell heights, force offsets to zero, and adaptive padding factor.

### Tasks

| # | Task | Details | GUM Control | API Mapping |
|---|---|---|---|---|
| 2.1 | **Padding Up input (A)** | `TextBox` with numeric validation, range 0-20, default 0. `Label` above: "A (Up)". Part of a cross-shaped layout built from nested `StackPanel` containers. The cross layout places Up at top-center, Left at center-left, Right at center-right, Down at bottom-center. Bind to `PaddingUp`. | `TextBox` + `Label` | `WithPadding(up, right, down, left)` -- `up` param |
| 2.2 | **Padding Right input (B)** | `TextBox` with numeric validation, range 0-20, default 0. `Label` to the right: "B (Right)". Placed at center-right of cross layout. Bind to `PaddingRight`. | `TextBox` + `Label` | `WithPadding(...)` -- `right` param |
| 2.3 | **Padding Down input (C)** | `TextBox` with numeric validation, range 0-20, default 0. `Label` below: "C (Down)". Placed at bottom-center of cross layout. Bind to `PaddingDown`. | `TextBox` + `Label` | `WithPadding(...)` -- `down` param |
| 2.4 | **Padding Left input (D)** | `TextBox` with numeric validation, range 0-20, default 0. `Label` to the left: "D (Left)". Placed at center-left of cross layout. Bind to `PaddingLeft`. | `TextBox` + `Label` | `WithPadding(...)` -- `left` param |
| 2.5 | **Uniform padding toggle** | `CheckBox` labeled "Uniform" in the center of the cross layout. When checked, all four padding inputs are synchronized: changing any one sets all four to the same value via the view model property change handler. Default: off. | `CheckBox` | UI-only; all four values passed to `WithPadding` |
| 2.6 | **Visual padding preview** | Custom-drawn rectangle (approximately 120x120 px) rendered via MonoGame `SpriteBatch` primitives (using MonoGame.Extended `ShapeExtensions` for filled rectangles). Shows a simplified glyph cell with colored bands (semi-transparent blue) on each side representing padding with pixel-count labels. Updates on every value change by reading the four padding properties. Positioned to the right of the cross-shaped inputs in a horizontal `StackPanel`. | Custom render area | Visual only -- reads current padding values |
| 2.7 | **Horizontal spacing input** | `TextBox` with numeric validation, range 0-10, default 1. `Label`: "Horizontal". Represents the horizontal gutter (pixels) between adjacent glyph cells. Placed in a "Spacing" sub-section below padding. Bind to `SpacingH`. | `TextBox` + `Label` | `WithSpacing(h, v)` -- `h` param |
| 2.8 | **Vertical spacing input** | `TextBox` with numeric validation, range 0-10, default 1. `Label`: "Vertical". Represents the vertical gutter between rows of glyph cells. Bind to `SpacingV`. | `TextBox` + `Label` | `WithSpacing(h, v)` -- `v` param |
| 2.9 | **Link spacing toggle** | `CheckBox` labeled "Link" between horizontal and vertical inputs. When checked, changing either value sets both via the view model. Default: checked (linked). | `CheckBox` | UI-only; both values passed to `WithSpacing` |
| 2.10 | **Equalize cell heights** | `CheckBox` labeled "Equalize cell heights". When checked, all glyph cells in the atlas are sized to the same height (the tallest glyph). Matches BMFont's export option. Bind to `EqualizeCellHeights`. | `CheckBox` | `WithEqualizeCellHeights(bool)` (if available, otherwise stored for future API) |
| 2.11 | **Force offsets to zero** | `CheckBox` labeled "Force offsets to zero". When checked, glyph x/y offsets in the descriptor are zeroed out. Matches BMFont's export option. Bind to `ForceOffsetsToZero`. | `CheckBox` | `WithForceOffsetsToZero(bool)` (if available) |
| 2.12 | **Adaptive padding factor** | `Slider` with `Minimum=0.0`, `Maximum=2.0`, step 0.1. `Label`: "Adaptive Padding Factor". Default: 1.0. A second `Label` to the right shows the current value. Bind to `AdaptivePaddingFactor`. **Non-functional placeholder -- requires Phase 55 API addition.** | `Slider` + `Label` | Future API mapping (Phase 55) |
| 2.13 | **Section container** | Collapsible section titled "Padding & Spacing". Expanded by default. The padding cross-layout and preview occupy the top half; the spacing inputs occupy the bottom half; BMFont options (equalize, force offsets, adaptive factor) sit at the bottom. Sections separated by spacing in the `StackPanel`. | `StackPanel`, `Label`, `Button` | -- |

### Cross-Shaped Layout Construction (GUM Code-Only)

The cross layout is built using three horizontal `StackPanel` rows inside a vertical `StackPanel`:

```csharp
// Row 1: centered "Up" input
var topRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = Center };
topRow.Children.Add(paddingUpLabel);
topRow.Children.Add(paddingUpTextBox);

// Row 2: Left | Uniform toggle | Right
var middleRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = Center };
middleRow.Children.Add(paddingLeftLabel);
middleRow.Children.Add(paddingLeftTextBox);
middleRow.Children.Add(uniformCheckBox);
middleRow.Children.Add(paddingRightTextBox);
middleRow.Children.Add(paddingRightLabel);

// Row 3: centered "Down" input
var bottomRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = Center };
bottomRow.Children.Add(paddingDownLabel);
bottomRow.Children.Add(paddingDownTextBox);

var crossLayout = new StackPanel();
crossLayout.Children.Add(topRow);
crossLayout.Children.Add(middleRow);
crossLayout.Children.Add(bottomRow);
```

### Interaction Patterns

- The cross-shaped padding layout with A/B/C/D labels matches BMFont's directional padding UI, making the spatial arrangement immediately obvious to users familiar with BMFont.
- The visual padding preview redraws on every value change (no debounce needed for simple rectangle drawing via SpriteBatch).
- Changing any padding or spacing value triggers the atlas size re-estimation from Wave 1 (debounced 300ms).
- When the uniform padding toggle is checked and the user changes any padding value, the view model property setter detects `UniformPadding == true` and sets all four padding properties to the new value.

---

## Wave 3 — Packing Algorithm Selection

### Goal

Let the user choose between MaxRects and Skyline packing algorithms with clear descriptions of each. Optionally show a visual comparison after generation.

### Tasks

| # | Task | Details | GUM Control | API Mapping |
|---|---|---|---|---|
| 3.1 | **Algorithm dropdown** | `ComboBox` with two items: "MaxRects" and "Skyline". Default: MaxRects (index 0). `Label`: "Packing Algorithm". Bind `SelectedIndex` to `SelectedAlgorithm` via `SetBinding()`. | `ComboBox` | `WithPackingAlgorithm(PackingAlgorithm.MaxRects \| .Skyline)` |
| 3.2 | **Algorithm description label** | `Label` below the dropdown, bound to `AlgorithmDescription`. When MaxRects is selected (index 0), the view model sets description to: "Best short-side fit -- evaluates all free rectangles for tightest placement. Higher density, slightly slower for large sets." When Skyline is selected (index 1): "Bottom-left skyline placement -- fast horizon-line algorithm. Better for large character sets (1000+ glyphs)." Styled with muted text color. | `Label` | -- |
| 3.3 | **Algorithm info button** | `Button` labeled "(i)" next to the dropdown. Click handler toggles visibility of a detail `StackPanel` containing a comparison table built from `Label` controls in a grid-like layout: Density (MaxRects: Higher, Skyline: Slightly Lower), Speed (MaxRects: O(n*m), Skyline: O(n log n)), Best For (MaxRects: Small-to-medium sets, Skyline: Large sets). | `Button`, `StackPanel`, `Label` (x8) | -- |
| 3.4 | **Comparison toggle** | `CheckBox` labeled "Show algorithm comparison after generation (slower)". Default: unchecked. When checked, generation runs both algorithms (selected one for output, the other for comparison only) and displays side-by-side efficiency percentages in `Label` controls below. Bind to `ShowAlgorithmComparison`. | `CheckBox` | Runs `WithPackingAlgorithm` with both values |
| 3.5 | **Comparison result labels** | Two `Label` controls side by side, hidden until comparison generation completes: "MaxRects: 94.2%" and "Skyline: 91.8%". Visibility bound to a `ComparisonResultsAvailable` property. | `Label` (x2) | -- |
| 3.6 | **Large set hint** | `Label` with info-blue text color. Visible when the current character set has more than 2000 codepoints and MaxRects is selected. Text: "Large character set detected. Skyline may be faster with similar density." Visibility controlled by view model logic checking character count and algorithm selection. | `Label` | UI validation based on character set count |
| 3.7 | **Algorithm impact on estimate** | Changing the algorithm triggers a re-estimation of atlas size (Wave 1 task 1.7). The estimator accounts for the selected algorithm's typical efficiency characteristics. | -- | `AtlasSizeEstimator` integration |
| 3.8 | **Section container** | Collapsible section titled "Packing Algorithm". Collapsed by default (less frequently changed than size/padding). Collapse toggle via header `Button`. | `StackPanel`, `Label`, `Button` | -- |
| 3.9 | **Persist selection** | The selected algorithm index is saved to user settings JSON and restored on next launch. The view model constructor reads from settings; a property change handler writes back. | -- | User settings persistence |

### Interaction Patterns

- The description label text updates immediately when the dropdown selection changes (view model property change handler updates `AlgorithmDescription`).
- The info panel (task 3.3) is hidden by default and toggles visibility on button click. Click-outside detection is not needed -- a second click on the info button hides it.
- Selecting a different algorithm does not trigger regeneration -- it only affects the next Generate action. The atlas estimate updates immediately.
- The comparison result labels are hidden entirely when no comparison data exists, not just empty -- their container's `Visible` property is set to `false`.

---

## Wave 4 — Output Format & Channel Configuration

### Goal

Allow the user to choose texture image format (PNG/TGA/DDS), font descriptor format (Text/XML/Binary), toggle kerning, configure bit depth, and set up per-channel content assignment with BMFont-style presets.

### Tasks

| # | Task | Details | GUM Control | API Mapping |
|---|---|---|---|---|
| 4.1 | **Texture format radio group** | Three `RadioButton` controls in a horizontal `StackPanel`: "PNG", "TGA", "DDS". Default: PNG selected. `Label` above: "Textures". Each `RadioButton` is in the same group so only one can be selected. Bind selection index to `TextureFormatIndex`. | `RadioButton` (x3), `Label` | `WithTextureFormat(TextureFormat.Png \| .Tga \| .Dds)` |
| 4.2 | **Texture format description** | `Label` below the radio group, text updates based on selection. PNG: "Lossless compression, widely supported. Best for development." TGA: "Uncompressed/RLE. Common in game engines. Larger file size." DDS: "DirectX format with GPU-native compression. Smallest runtime memory." Bound to a computed property in the view model. | `Label` | -- |
| 4.3 | **Compression dropdown** | `ComboBox` labeled "Compression" shown below the texture format radios. Items depend on selected texture format: PNG shows "Default", TGA shows "None/RLE", DDS shows "None/BC1/BC3/BC7". Bind to `CompressionIndex`. Re-populated when texture format changes. **Non-functional placeholder -- requires Phase 55 API addition.** | `ComboBox` + `Label` | Future API (Phase 55) |
| 4.4 | **Descriptor format radio group** | Three `RadioButton` controls: "Text (.fnt)", "XML", "Binary". Default: Text. `Label` above: "Font Descriptor". Bind to `DescriptorFormatIndex`. Output format is set at export time via `BmFontResult.ToFile(path, format)`, not on the builder. | `RadioButton` (x3), `Label` | `BmFontResult.ToFile(path, format)` at export time |
| 4.5 | **Descriptor format description** | `Label` below the radio group. Text: "Human-readable key=value pairs (MonoGame, LibGDX, Cocos2d)" / "Structured XML document for XML-based pipelines" / "Compact binary encoding, fastest parsing, requires binary reader". Updated on radio selection change. | `Label` | -- |
| 4.6 | **Kerning toggle** | `CheckBox` labeled "Include kerning pairs". Default: checked. When unchecked, kerning data is omitted from the output. Bind to `IncludeKerning`. Disabled (grayed) when `KerningPairCount == 0`. | `CheckBox` | `WithKerning(bool)` |
| 4.7 | **Kerning pair count display** | `Label` next to the kerning toggle showing "X pairs available" where X is `FontInfo.KerningPairs.Count`. Shows "--" when no font is loaded. Shows "0 pairs" if the font has no kerning data. Bind to a formatted string property. | `Label` | Reads `FontInfo.KerningPairs.Count` |
| 4.8 | **Bit depth radio group** | Two `RadioButton` controls: "32-bit" and "8-bit". Default: 32-bit. `Label`: "Bit Depth". Matches BMFont's bit depth selector. Bind to `BitDepth`. **Non-functional placeholder -- requires Phase 55 API addition.** | `RadioButton` (x2), `Label` | Future API (Phase 55) |
| 4.9 | **Pack multiple channels toggle** | `CheckBox` labeled "Pack chars in multiple channels". Default: unchecked. When checked, enables the per-channel configuration rows (tasks 4.10-4.13). When unchecked, the channel config rows are hidden. Bind to `PackMultipleChannels`. Note: this maps to `WithChannelPacking(bool)` which is distinct from `WithChannels(ChannelConfig)` used by the per-channel dropdowns. | `CheckBox` | `WithChannelPacking(bool)` |
| 4.10 | **Channel A config row** | Horizontal `StackPanel`: `Label` "A:", `ComboBox` for value (items: "glyph", "outline", "glyph+outline", "zero", "one"), `CheckBox` "Invert". Bind ComboBox to `ChannelA_Value`, CheckBox to `ChannelA_Invert`. Only visible when `PackMultipleChannels` is true. | `Label` + `ComboBox` + `CheckBox` | `ChannelConfig.Alpha` |
| 4.11 | **Channel R config row** | Same layout as 4.10 for the Red channel. `Label` "R:", binds to `ChannelR_Value` and `ChannelR_Invert`. | `Label` + `ComboBox` + `CheckBox` | `ChannelConfig.Red` |
| 4.12 | **Channel G config row** | Same layout for Green channel. `Label` "G:", binds to `ChannelG_Value` and `ChannelG_Invert`. | `Label` + `ComboBox` + `CheckBox` | `ChannelConfig.Green` |
| 4.13 | **Channel B config row** | Same layout for Blue channel. `Label` "B:", binds to `ChannelB_Value` and `ChannelB_Invert`. | `Label` + `ComboBox` + `CheckBox` | `ChannelConfig.Blue` |
| 4.14 | **Channel presets dropdown** | `ComboBox` labeled "Preset" above the channel config rows. Items: "Custom", "White text with alpha", "Black text with alpha", "White text on black", "Outlined text with alpha", "Pack text and outline in same channel". Selecting a preset auto-fills all four channel rows with predefined values. Selecting "Custom" does nothing (allows manual editing). If the user manually changes any channel value after selecting a preset, the dropdown reverts to "Custom". Bind to `ChannelPresetIndex`. | `ComboBox` + `Label` | Presets map to `WithChannels(ChannelConfig)` |
| 4.15 | **Binary format warning** | `Label` with amber text, visible when Binary descriptor format is selected. Text: "Binary format is less commonly supported. Verify your target framework can read BMFont binary descriptors." Visibility bound to `DescriptorFormatIndex == 2`. | `Label` | UI validation |
| 4.16 | **Section container** | Collapsible section titled "Output Format & Channels". Expanded by default. Layout: Texture format at top, compression below it, descriptor format in middle, kerning toggle, bit depth, then channel configuration at bottom. Separated by spacing in `StackPanel`. | `StackPanel`, `Label`, `Button` | -- |

### Channel Preset Values

| Preset | Alpha | Red | Green | Blue |
|--------|-------|-----|-------|------|
| White text with alpha | glyph | one | one | one |
| Black text with alpha | glyph | zero | zero | zero |
| White text on black | one | glyph | glyph | glyph |
| Outlined text with alpha | glyph | outline | outline | outline |
| Pack text and outline | outline | glyph | glyph | glyph |

### Interaction Patterns

- Changing the texture format does not trigger regeneration -- it affects the next Generate action. However, if a result already exists in memory, an inline `Button` "Re-export" appears to quickly re-encode the existing atlas data in the new format without re-rasterizing.
- The kerning toggle becoming disabled (when pair count is 0) uses reduced opacity via GUM's `Alpha` property to visually communicate the disabled state.
- Channel configuration rows smoothly appear/disappear when the `PackMultipleChannels` toggle changes (set `Visible` property on the channel `StackPanel`).
- When a preset is selected, all four channel rows update simultaneously. A flag in the view model tracks whether the current config matches a preset; if any value is manually changed, `ChannelPresetIndex` is set back to 0 ("Custom").
- The compression dropdown re-populates its items when the texture format changes. View model clears the list and adds format-appropriate items.

---

## Wave 5 — Atlas Metrics & Statistics Display

### Goal

After generation completes, display comprehensive metrics about the atlas output in a dedicated collapsible section. Optionally show per-stage pipeline timing.

### Tasks

| # | Task | Details | GUM Control | API Mapping |
|---|---|---|---|---|
| 5.1 | **Metrics section container** | Collapsible section titled "Atlas Metrics". Hidden (`Visible = false`) before first generation. After generation, made visible and auto-expanded. Contains a vertical `StackPanel` of key-value rows, each being a horizontal `StackPanel` with two `Label` controls (key and value). | `StackPanel`, `Label`, `Button` | -- |
| 5.2 | **Page count display** | Row: `Label` "Texture Pages" + `Label` bound to page count. If 1, displays "1". If multiple, shows count plus total combined area, e.g., "3 (total: 2048x6144 px)". | `Label` (x2) | `BmFontResult.Model.Pages.Count` |
| 5.3 | **Page dimensions display** | Row: `Label` "Page Size" + `Label` with dimensions. If all pages are the same size, one line (e.g., "2048 x 1024 px"). If pages differ, shows each page on a separate line using a multi-line `Label` or stacked `Label` controls. | `Label` (x2+) | `BmFontResult.Model.Pages[i]` dimensions |
| 5.4 | **Total glyphs packed** | Row: `Label` "Glyphs Packed" + `Label` showing "215 of 218". Green text color if all packed; amber if some failed. Color set via view model property controlling the label's text color. | `Label` (x2) | `BmFontResult.Model.Chars.Count` vs requested |
| 5.5 | **Failed codepoints list** | Row: `Label` "Failed" + `Label` showing count. If failures exist, a `Button` "Show Details" toggles visibility of a `ScrollViewer` containing a `StackPanel` of `Label` controls listing failed codepoints with Unicode names (e.g., "U+FFFD REPLACEMENT CHARACTER"). If none failed, shows "None" in green. | `Label`, `Button`, `ScrollViewer`, `StackPanel` | `BmFontResult.FailedCodepoints` |
| 5.6 | **Packing efficiency display** | Row: `Label` "Packing Efficiency" + `Label` showing percentage (e.g., "93.2%"). Color-coded: green >= 90%, amber 75-89%, red < 75%. Below the percentage, a simple horizontal bar drawn with MonoGame.Extended `ShapeExtensions.FillRectangle` proportional to the efficiency value. | `Label` (x2) + custom draw | Calculated from atlas data |
| 5.7 | **Line height and base** | Two rows: "Line Height" = "52 px" and "Base" = "41 px". Essential layout values from the common block. | `Label` (x4) | `BmFontResult.Model.Common.LineHeight`, `.Base` |
| 5.8 | **Kerning pairs output count** | Row: "Kerning Pairs" = "1,247" (formatted with thousands separator). If kerning was disabled, shows "Disabled". If font has no kerning, shows "None". | `Label` (x2) | `BmFontResult.Model.KerningPairs.Count` |
| 5.9 | **Pipeline metrics toggle** | `CheckBox` at the top of the metrics section: "Collect detailed timing". Bound to `CollectMetrics`. When checked, sets `FontGeneratorOptions.CollectMetrics = true` for next generation (no builder method exists yet -- tracked in Phase 55). | `CheckBox` | `FontGeneratorOptions.CollectMetrics` (direct property, Phase 55) |
| 5.10 | **Per-stage timing breakdown** | Collapsible sub-section "Pipeline Timing", visible only when metrics were collected. Each stage is a row: `Label` for stage name, `Label` for duration (e.g., "145ms"), and a proportional horizontal bar (filled rectangle via MonoGame.Extended). Stages: FontParsing, CharsetResolution, Rasterization, EffectsCompositing, PostProcessing, SuperSampleDownscale, CellEqualization, AtlasSizeEstimation, AtlasPacking, AtlasEncoding, ModelAssembly, Total. Bars scale relative to the longest stage. | `StackPanel`, `Label` (x24+), custom draw | `BmFontResult.Metrics` |
| 5.11 | **Generation timestamp** | Row: "Generated" = "2026-03-21 14:32:07". Timestamp captured at generation completion time (UI-side `DateTime.Now`). | `Label` (x2) | Captured at generation time |
| 5.12 | **Copy metrics button** | `Button` labeled "Copy Metrics" at the bottom of the section. Click handler builds a formatted plain-text string of all metrics (key: value, one per line) and copies to clipboard via MonoGame/platform clipboard API. After copying, the button text changes to "Copied!" for 1.5 seconds, then reverts. | `Button` | UI clipboard integration |

### Interaction Patterns

- The metrics section becomes visible (and if using a `ScrollViewer`, scrolls into view) after generation completes.
- Metrics are cleared and the section hides when the user loads a different font file (the `FontLoaded` event or property change resets metrics state).
- The timing breakdown bars use the application's accent color and scale proportionally to the longest stage duration.
- The copy button's temporary "Copied!" text is managed by a `System.Threading.Timer` that resets after 1500ms.

---

## Wave 6 — Multi-Page Atlas Navigation

### Goal

When the generated atlas contains multiple texture pages, provide intuitive navigation to view each page individually, along with per-page metadata and a thumbnail overview strip.

### Tasks

| # | Task | Details | GUM Control | API Mapping |
|---|---|---|---|---|
| 6.1 | **Page navigation bar** | Horizontal `StackPanel` at the top of the atlas preview area. Contains: left arrow `Button` ("<"), `Label` showing "Page 1 of 3", right arrow `Button` (">"). Only visible when `TotalPages > 1` (bind container `Visible`). Arrow buttons disabled at boundaries (left disabled on page 0, right disabled on last page) via view model computed properties. | `StackPanel`, `Button` (x2), `Label` | `BmFontResult.Model.Pages.Count` |
| 6.2 | **Page indicator dots** | Horizontal `StackPanel` of small `Button` controls (styled as dots -- small fixed-size buttons) below the page label, one per page. Active page button uses the accent color; others use muted color. Clicking a dot sets `CurrentPage` to that index. Only shown when page count is 2-10. | `StackPanel`, `Button` (dynamic) | -- |
| 6.3 | **Page dropdown for large counts** | When page count exceeds 10, replace the dot indicators with a `ComboBox` listing "Page 1", "Page 2", ..., "Page N". Bind `SelectedIndex` to `CurrentPage`. The `ComboBox` and dot `StackPanel` have mutually exclusive visibility based on page count. | `ComboBox` | -- |
| 6.4 | **Preview panel integration** | The main atlas preview area (from a prior phase) displays the currently selected page's texture as a MonoGame `Texture2D`. When `CurrentPage` changes, the preview loads the new page's texture data via `BmFontResult.GetPngData(pageIndex)` (or TGA/DDS based on format), converts to `Texture2D`, and renders via `SpriteBatch`. Zoom and pan (via MonoGame.Extended camera) are preserved across page changes. | Custom MonoGame render | `BmFontResult.GetPngData(i)` / `GetTgaData` / `GetDdsData` |
| 6.5 | **Per-page info overlay** | `Label` in the corner of the preview area showing "Page 2 -- 2048x1024 -- 87 glyphs". Updated when `CurrentPage` changes. An eye-icon `Button` on the navigation bar toggles this label's `Visible` property. Semi-transparent background achieved via `Alpha` property on a backing rectangle. | `Label`, `Button` | `Model.Pages[i]`, char count on page |
| 6.6 | **Per-page glyph count** | `Label` below the navigation bar: "72 glyphs on this page". Calculated by counting `CharEntry` items whose `Page` field matches `CurrentPage`. Bound to a computed property that recalculates on page change. | `Label` | `BmFontResult.Model.Chars.Where(c => c.Page == i).Count()` |
| 6.7 | **Thumbnail strip** | Horizontal `ScrollViewer` at the bottom of the preview panel containing a horizontal `StackPanel` of thumbnail images. Each thumbnail is an 80x80 `Texture2D` rendered into a small area, with a highlighted border on the active page. Clicking a thumbnail sets `CurrentPage`. Only shown when `TotalPages >= 2`. | `ScrollViewer`, `StackPanel`, custom draw | Downscaled page textures |
| 6.8 | **Thumbnail lazy generation** | Thumbnails are generated lazily on a background thread -- only when the thumbnail strip becomes visible or on first generation. A placeholder (gray rectangle with page number `Label`) is shown until the thumbnail `Texture2D` is ready. Uses `Task.Run` to generate downscaled textures without blocking the UI thread. | -- | -- |
| 6.9 | **Keyboard navigation** | MonoGame keyboard input handling: Left/Right arrow keys change `CurrentPage` when the preview panel has focus. Home key goes to page 0, End key goes to last page. Handled in the `Update` loop with key state tracking to prevent rapid-fire. | -- (MonoGame input) | -- |
| 6.10 | **Single page behavior** | When `TotalPages == 1`, the entire navigation bar, dot indicators, and thumbnail strip are hidden (`Visible = false`). The preview panel shows the single page with no navigation chrome. Avoids "Page 1 of 1" displays. | -- | -- |
| 6.11 | **Export current page button** | `Button` labeled "Export Page" on the navigation bar. Click handler opens a platform save dialog (or writes to a user-specified directory). Saves the currently viewed page's texture bytes to a file. Filename defaults to `fontname_page0.png` (or `.tga`, `.dds` based on format). | `Button` | `BmFontResult.GetPngData(i)` written to file |

### Interaction Patterns

- Page navigation state resets to page 0 when a new generation is triggered (view model resets `CurrentPage` in the generation complete handler).
- The thumbnail strip auto-scrolls to keep the active page's thumbnail visible by adjusting the `ScrollViewer`'s scroll position.
- When zoomed into the preview and the user changes pages, the MonoGame.Extended camera position and zoom level are preserved (only the source `Texture2D` changes). This allows comparing the same region across pages.
- If the atlas shrinks from multiple pages to one page on regeneration, all navigation chrome hides cleanly -- the view model sets `TotalPages = 1`, and all visibility bindings react.
- The export button is disabled during generation (bound to an `IsGenerating` property that prevents file I/O during active pipeline execution).

---

## Core Library Notes

These are observations about the KernSmith library's behavior relevant to this phase. These should be documented but not fixed as part of this phase.

### AtlasSizeEstimator

- Verify whether `AtlasSizeEstimator` can produce pre-generation estimates using only glyph metrics (without full rasterization). If it requires rasterized bitmaps, the Wave 1 estimate feature may need to use approximate calculations based on font metrics (ascent, descent, em-size) and character count instead.
- If the estimator is not yet implemented or is internal-only, a simplified estimation formula should be used: `estimatedArea = glyphCount * (avgGlyphWidth + padH + spacingH) * (lineHeight + padV + spacingV)`, then fit into pages of the configured max size.

### Algorithm and Format Constraints

- There are no known constraints between packing algorithm and texture format -- any algorithm can produce any format. Document this as a non-issue.
- DDS output may have additional constraints on dimensions (must be multiples of 4 for block-compressed formats). If `WithTextureFormat(Dds)` is selected and `WithPowerOfTwo(false)`, verify that the library handles this internally or surface a warning in the UI.

### Performance Characteristics

- **MaxRects** performance is approximately O(n * m) where n is glyph count and m is the number of free rectangles maintained. For character sets under 1000 glyphs, this is effectively instant (< 50ms). For 5000+ glyphs, it may take 200-500ms.
- **Skyline** performance is approximately O(n log n) due to its simpler placement strategy. It is consistently fast regardless of character set size but may produce 5-15% lower packing density than MaxRects for small-to-medium sets.
- For CJK character sets (10,000+ glyphs), Skyline is strongly preferred unless maximum density is critical.

### Core Library API Gaps (Phase 55)

Several UI controls in this phase map to APIs that do not yet exist on the builder or library. These are tracked in Phase 55 for future addition:

- `WithCollectMetrics(bool)` -- currently must set `FontGeneratorOptions.CollectMetrics` directly
- `WithOutputFormat(OutputFormat)` -- output format is set at export time via `BmFontResult.ToFile(path, format)`
- AdaptivePaddingFactor, BitDepth, and Compression controls -- no corresponding library API yet

Until Phase 55 is complete, these controls should be rendered as non-functional placeholders in the UI.

### Channel Configuration

- The `ChannelConfig` type maps directly to BMFont's per-channel settings. Each channel (A, R, G, B) can hold glyph data, outline data, a combination, or a constant (zero/one).
- The invert flag per channel flips the pixel values (0 becomes 255, etc.) before writing to the atlas texture.
- Channel presets are a UI convenience -- they set all four channels at once to common configurations matching BMFont's built-in presets.

---

## Collapsible Section Pattern (GUM Code-Only)

All waves use a common collapsible section pattern. Since GUM does not have a built-in `Expander` control, sections are built from primitives:

```csharp
public static StackPanel CreateCollapsibleSection(string title, bool startExpanded = true)
{
    var section = new StackPanel();
    var content = new StackPanel { Visible = startExpanded };

    var header = new StackPanel { Orientation = Orientation.Horizontal };
    var toggleButton = new Button { Text = startExpanded ? "v" : ">" };
    var titleLabel = new Label { Text = title };
    header.Children.Add(toggleButton);
    header.Children.Add(titleLabel);

    toggleButton.Click += (s, e) =>
    {
        content.Visible = !content.Visible;
        toggleButton.Text = content.Visible ? "v" : ">";
    };

    section.Children.Add(header);
    section.Children.Add(content);
    return section; // caller adds controls to content (section.Children[1])
}
```

This pattern is used for: "Atlas Size" (expanded), "Padding & Spacing" (expanded), "Packing Algorithm" (collapsed), "Output Format & Channels" (expanded), "Atlas Metrics" (hidden until generation), and "Pipeline Timing" (collapsed sub-section).

---

## Settings Persistence

User settings are stored in a JSON file at `%APPDATA%/KernSmith/ui-settings.json` (or platform equivalent). The following settings from this phase are persisted:

| Setting Key | Type | Default | Source |
|---|---|---|---|
| `atlas.maxWidth` | int | 1024 | Wave 1 |
| `atlas.maxHeight` | int | 1024 | Wave 1 |
| `atlas.linkDimensions` | bool | false | Wave 1 |
| `atlas.powerOfTwo` | bool | true | Wave 1 |
| `atlas.autofit` | bool | false | Wave 1 |
| `atlas.packingEfficiency` | float | 0.90 | Wave 1 |
| `atlas.paddingUp/Right/Down/Left` | int | 0 | Wave 2 |
| `atlas.uniformPadding` | bool | false | Wave 2 |
| `atlas.spacingH` | int | 1 | Wave 2 |
| `atlas.spacingV` | int | 1 | Wave 2 |
| `atlas.linkSpacing` | bool | true | Wave 2 |
| `atlas.equalizeCellHeights` | bool | false | Wave 2 |
| `atlas.forceOffsetsToZero` | bool | false | Wave 2 |
| `atlas.adaptivePaddingFactor` | float | 1.0 | Wave 2 |
| `atlas.packingAlgorithm` | int | 0 | Wave 3 |
| `atlas.showAlgorithmComparison` | bool | false | Wave 3 |
| `output.textureFormat` | int | 0 | Wave 4 |
| `output.descriptorFormat` | int | 0 | Wave 4 |
| `output.includeKerning` | bool | true | Wave 4 |
| `output.bitDepth` | int | 0 | Wave 4 |
| `output.packMultipleChannels` | bool | false | Wave 4 |
| `output.channelPreset` | int | 0 | Wave 4 |
| `output.channelA/R/G/B_value` | int | 0 | Wave 4 |
| `output.channelA/R/G/B_invert` | bool | false | Wave 4 |
| `metrics.collectTiming` | bool | false | Wave 5 |
| `sections.atlasSizeExpanded` | bool | true | Wave 1 |
| `sections.paddingExpanded` | bool | true | Wave 2 |
| `sections.algorithmExpanded` | bool | false | Wave 3 |
| `sections.outputExpanded` | bool | true | Wave 4 |

Settings are loaded in the view model constructor and saved on each property change (debounced to avoid excessive I/O).

---

## Success Criteria

| Criterion | Verification |
|---|---|
| All atlas size controls (width, height, power-of-two, autofit, efficiency) are functional and map to `FontGeneratorOptions` | Change each setting, generate, verify the output matches the configured constraints |
| Link toggles correctly synchronize paired inputs | Enable link, change one value, verify the other updates |
| Padding cross-layout with A/B/C/D labels matches BMFont directional style | Visual inspection of layout |
| Padding and spacing values are correctly passed to the generator | Generate with non-default padding/spacing, inspect atlas texture for correct cell sizing |
| BMFont-specific options (equalize cells, force offsets) exposed | Toggle each, verify behavior in output |
| Packing algorithm selection affects generation output | Generate with MaxRects, then Skyline, compare packing layouts |
| Channel preset dropdown auto-fills all four channel rows | Select each preset, verify channel values match the preset table |
| Manual channel edit reverts preset to "Custom" | Select a preset, change one channel value, verify dropdown shows "Custom" |
| Texture format selection produces correct output | Select PNG, generate, verify PNG output; repeat for TGA and DDS |
| Descriptor format selection produces correct output | Select Text/XML/Binary, generate, verify .fnt file format |
| Kerning toggle correctly includes/excludes kerning pairs | Generate with kerning on, verify pairs; toggle off, regenerate, verify 0 pairs |
| Bit depth radio affects output texture | Select 8-bit, generate, verify texture is 8-bit grayscale |
| Metrics panel displays accurate post-generation data | Compare displayed metrics against manually inspected output files |
| Pipeline timing breakdown shows per-stage durations when enabled | Enable metrics, generate, verify timing values are reasonable and sum to total |
| Multi-page navigation works correctly | Generate with enough glyphs for 2+ pages, verify navigation, thumbnails, per-page counts |
| Single-page atlases hide navigation chrome | Generate small font, verify no navigation controls shown |
| All settings persist across application sessions | Set non-default values, close and reopen, verify restoration |
| Estimated atlas size updates reactively | Change font size, character set, padding, observe estimate updating |
| All controls use GUM code-only construction (no XAML, no GUMX) | Code review |
| View model bindings use `SetBinding()` pattern | Code review |

---

## Dependencies

- **Prior phases**: This phase assumes the base MonoGame+GUM UI application shell (Phase 60), font loading panel (Phase 61), and atlas preview rendering exist from earlier phases.
- **KernSmith library**: All `FontGeneratorOptions` methods listed in the API reference must be implemented and functional.
- **AtlasSizeEstimator**: Needed for pre-generation estimates (Wave 1). If not available, a simplified fallback estimator is acceptable.
- **BmFontResult.Metrics** (`PipelineMetrics`): Needed for timing breakdown (Wave 5). If not yet implemented, the timing section can be stubbed with a "Coming soon" label.
- **ChannelConfig**: Needed for channel configuration (Wave 4). The `ChannelContent` enum and `ChannelConfig` type must support the BMFont preset configurations.

---

## Out of Scope

- Texture compression level settings (DDS BC format quality sliders) -- future phase.
- SDF-specific atlas settings (spread, downscale) -- covered by SDF configuration phase.
- Atlas preview rendering implementation -- assumed to exist from prior phase; this phase adds navigation and metrics on top of it.
- Super-sampling configuration -- separate phase.
- Color font / emoji atlas handling -- separate phase.
- GUM theme/skin customization -- uses default GUM styling unless overridden by Phase 60's theme setup.
