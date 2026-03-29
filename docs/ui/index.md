# UI Guide

KernSmith.Ui is a visual bitmap font generator built with MonoGame and Gum. It provides a three-column interface for configuring fonts, applying effects, previewing atlas textures, and exporting BMFont-compatible output.

## Build and Run

```bash
dotnet build apps/KernSmith.Ui
dotnet run --project apps/KernSmith.Ui
```

## Layout Overview

The interface uses a three-column layout with a menu bar at the top and a status bar at the bottom. All panels are resizable via draggable splitters.

| Area | Position | Purpose |
|------|----------|---------|
| Menu Bar | Top | File, View, Help menus |
| Font Config | Left panel | Font loading, size, atlas settings, output format |
| Preview | Center panel | Atlas preview, character selection, sample text (tabbed) |
| Effects | Right panel | Bold, italic, outline, shadow, gradient, SDF, variable axes |
| Status Bar | Bottom | Generation status, atlas dimensions, glyph count, timing |

## Loading a Font

There are several ways to load a font:

- **Browse**: Click "Browse for Font..." in the left panel and select a TTF, OTF, WOFF, or TTC file
- **Drag and drop**: Drag a font file directly onto the application window
- **System fonts**: Use the "System Font" dropdown to pick an installed font (availability depends on rasterizer backend)
- **Recent fonts**: File > Recent Fonts shows the last 10 loaded fonts
- **Keyboard**: Ctrl+O opens the file browser

When a TrueType Collection (.ttc) file is loaded, a face selector appears to choose which font in the collection to use.

After loading, the left panel shows font metadata: family name, style, glyph count, and badges for color font or variable font support.

## Selecting Characters

Switch to the **Characters** tab in the center panel to choose which glyphs to include.

### Presets

- **ASCII**: Standard printable ASCII (32-126, 95 characters)
- **Extended ASCII**: ASCII plus Latin-1 Supplement (32-255, 224 characters)
- **Latin**: Extended Latin blocks (500+ characters)
- **Custom**: User-defined selection

### Adding characters from text

Paste or type text into the "Add From Text" box and click "Add". All unique characters from the text are added to the selection. This automatically switches the preset to Custom.

### Unicode blocks

Toggle individual Unicode blocks using the checkbox list: Basic Latin, Greek, Cyrillic, Arabic, Thai, CJK, Emoji, Box Drawing, and more. Each block shows its character count.

## Configuring Effects

The right panel contains collapsible sections for each effect type.

### Font Style

- **Bold / Italic**: If a matching variant font file exists, it loads that file. Otherwise applies synthetic bold or italic.
- **Anti-Alias**: Smooth glyph edges (enabled by default)
- **Hinting**: Sharper rendering at small sizes (enabled by default)
- **Super Sample**: Render at 2x or 4x resolution then downsample for smoother results. Higher values are slower.

### Outline

Adds a colored border around each glyph.

- **Width**: 1-10 pixels
- **Color**: Hex color picker (#RRGGBB)

### Shadow

Adds a drop shadow behind each glyph.

- **Offset X/Y**: -10 to +10 pixels
- **Blur**: 0-10 pixels
- **Color**: Hex color picker
- **Opacity**: 0-100%
- **Hard Shadow**: Checkbox for binarized (hard edge) vs. soft shadow

### Gradient

Applies a vertical color gradient across each glyph.

- **Start / End Color**: Hex color pickers
- **Angle**: 0-360 degrees

### Channel Packing

Maps glyph data into individual RGBA channels for compressed output. Useful for game engines that support channel-packed fonts.

### SDF (Signed Distance Field)

Generates signed distance field output for resolution-independent rendering. Availability depends on the selected rasterizer backend.

### Variable Font Axes

When a variable font is loaded, sliders appear for each variation axis (e.g., Weight, Width). Adjust these to control the font's appearance without loading a separate font file.

### Fallback Character

A single character used when the renderer encounters a missing glyph (default: "?").

## Atlas Configuration

Located in the left panel under a collapsible "Atlas" section.

- **Auto-fit vs Force Size**: By default the atlas is auto-sized to fit all glyphs. Check "Force Size" to set specific dimensions.
- **Width / Height**: Choose from 128 to 8192 pixels
- **Packing Algorithm**: MaxRects or Skyline
- **Padding**: Pixels around each glyph (prevents texture bleeding)
- **Spacing**: Pixels between adjacent glyphs
- **Power of Two**: Constrain atlas dimensions to powers of 2 (required by some game engines)
- **Include Kerning**: Include kerning pair data in the output

## Output Format

Choose the BMFont descriptor format in the left panel:

- **Text**: Human-readable BMFont text format (default)
- **XML**: BMFont XML format
- **Binary**: BMFont binary format

## Generating a Font

1. Load a font file
2. Select characters (or use a preset)
3. Optionally adjust size, effects, and atlas settings
4. Click **Generate** or press **Ctrl+G**
5. Watch the status bar for progress: "Generating..." > "Generation complete (N pages)"

### Auto-Regenerate

Enable the "Auto-Regenerate" checkbox in the left panel to automatically regenerate the preview whenever you change a setting. Changes are debounced (300ms delay) to avoid excessive regeneration while typing.

## Previewing Results

### Preview Tab

The default center tab shows the generated atlas texture on a checkered transparency background.

- **Zoom**: Use the zoom slider (25%-1000%) or scroll wheel
- **Pan**: Middle-mouse drag
- **Page navigation**: If the atlas has multiple pages, use Previous/Next buttons
- **Atlas summary**: Shows dimensions, page count, glyph count, line height, and kerning pairs

The first generation auto-fits the zoom level. Subsequent regenerations preserve your zoom and pan position.

### Sample Text Tab

Switch to the Sample Text tab to see how the generated font renders actual text. Type any text in the input field and see it rendered in real-time using the generated bitmap font.

## Exporting

Once you have generated a font you are happy with:

1. **File > Export As...** (or Ctrl+Shift+S)
2. Choose a location and base filename
3. The export produces:
   - `basename.fnt` -- BMFont descriptor file (in the format selected under Output Format)
   - `basename_0.png`, `basename_1.png`, etc. -- Atlas page textures

## Saving and Loading Projects

### Save Project

File > Save Project (Ctrl+S) saves all current settings to a `.bmfc` file: font path, size, effects, atlas config, character selection, and output format. The window title shows an asterisk (*) when there are unsaved changes.

### Load Project

File > Load Project opens a `.bmfc` file and restores all settings. You can also drag a `.bmfc` file onto the window. If the original font file has moved, you will need to re-browse for it.

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+O | Open font file |
| Ctrl+S | Save project |
| Ctrl+Shift+S | Export As |
| Ctrl+G | Generate bitmap font |
| Ctrl++ | Increase UI scale |
| Ctrl+- | Decrease UI scale |
| Ctrl+0 | Reset UI scale to 100% |
| Scroll wheel | Zoom preview (when over atlas or sample text) |
| Middle mouse drag | Pan preview |

## Rasterizer Backends

The Font Config panel includes a rasterizer backend dropdown. Different backends have different capabilities:

| Backend | Platform | Color Fonts | Variable Fonts | SDF |
|---------|----------|-------------|----------------|-----|
| FreeType | All | No | No | Yes |
| DirectWrite | Windows | Yes | Yes | No |
| GDI | Windows | No | No | No |

Install the corresponding NuGet package to enable a backend. They auto-register via module initializer -- no code changes needed.

## Tips

- **Large character sets** (CJK, emoji) may produce multiple atlas pages. Use page navigation in the Preview tab to review them all.
- **Failed glyphs** are reported in the status bar and preview toolbar. Some codepoints may not have glyphs in the loaded font.
- **Super sampling at 4x** produces the smoothest results but is significantly slower. Use 1x for quick iteration, then bump up for final export.
- **Channel packing** is an advanced feature for game engines that read glyph data from individual texture channels. Leave it off unless your renderer expects it.
- **Force atlas size** when your game engine has specific texture size requirements (e.g., mobile GPUs may require power-of-two textures under 2048).
