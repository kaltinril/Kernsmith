# Phase 64 — Live Preview & Visualization

> **Status**: Complete
> **Completed**: 2026-03-22. Checkered transparency background, zoom (slider + scroll wheel + Ctrl+/-), middle-click pan, auto-fit zoom, multi-page nav with per-page dimensions. Glyph inspector hover/click, kerning visualization, side-by-side comparison, and HLSL shaders deferred as over-engineered for a font generation tool.
> **Created**: 2026-03-21
> **Goal**: Build a comprehensive preview system with live atlas rendering, sample text preview, glyph inspector, and kerning visualization — making the invisible visible.

---

## Motivation

Live preview is the single most valued feature in bitmap font tools. BMFont's lack of live preview is consistently its biggest complaint — users must generate, open the output in an external image viewer, tweak settings, regenerate, and repeat. This cycle kills productivity.

Phase 64 turns the center panel of the KernSmith UI into a fully interactive preview surface: atlas textures render in real time, glyphs can be inspected on hover, sample text composites with kerning applied, and side-by-side comparison lets users see the impact of every setting change instantly.

### Design Principles

1. **Immediate feedback** — every setting change should produce visible results within 1-2 seconds for typical character sets.
2. **Pixel accuracy** — the preview must show exactly what the engine will receive. No approximations.
3. **Progressive disclosure** — basic preview works with zero configuration; overlays, inspectors, and comparison modes are opt-in.
4. **Non-blocking** — generation runs on background threads; the UI never freezes.

---

## Rendering Architecture

### Technology Stack

- **MonoGame (DesktopGL)** — game framework providing the render loop, `SpriteBatch`, `Texture2D`, `RenderTarget2D`, input handling, and the `Game.Draw()`/`Game.Update()` lifecycle.
- **GUM UI (code-only)** — layout engine for UI panels, toolbars, and inspector elements. All GUM elements created in code (no Glue editor). `ContainerRuntime`, `TextRuntime`, `SpriteRuntime`, `NineSliceRuntime` for UI chrome.
- **MonoGame.Extended** — shape drawing primitives (`ShapeExtensions.DrawRectangle`, `DrawLine`, `DrawCircle`), used for overlay rendering (bounding boxes, metric lines, grid). Also provides `RectangleF`, `SizeF`, and camera utilities.
- **`Texture2D.SetData<byte>()`** — converts `AtlasPage.PixelData` (byte array) into GPU textures for display. No SkiaSharp, no WriteableBitmap, no Avalonia.

### Data Flow

```
FontGeneratorOptions changed
  --> debounce 500ms
  --> BmFont.Generate() on background thread
  --> BmFontResult returned
  --> Update PreviewState
    --> Atlas pages -> Texture2D instances (one per page)
       AtlasPage.PixelData byte[] -> Texture2D(graphicsDevice, width, height)
       texture.SetData<byte>(pixelData)
    --> BmFontModel.Characters -> glyph lookup dictionary
    --> BmFontModel.KerningPairs -> kerning lookup dictionary
  --> Set _dirty flag (triggers redraw on next Draw cycle)
  --> Game.Draw(GameTime)
    --> SpriteBatch.Begin(transformMatrix: _viewMatrix)
    --> Draw checkered background texture
    --> Draw atlas Texture2D
    --> Draw overlays via MonoGame.Extended shape primitives
    --> SpriteBatch.End()
    --> Draw HUD elements in screen-space (second SpriteBatch.Begin with no transform)
    --> GUM draws UI elements (toolbar, panels, tooltips)
```

### Coordinate System

All preview rendering uses a consistent coordinate model:

- **Atlas space** — pixel coordinates within the atlas texture (0,0 = top-left of atlas).
- **View space** — pixel coordinates on screen after zoom/pan transform.
- **Transform** — a `Matrix` combining `Matrix.CreateScale()` (zoom) and `Matrix.CreateTranslation()` (pan). Passed to `SpriteBatch.Begin(transformMatrix: _viewMatrix)`. All mouse hit-testing applies the inverse matrix to convert screen coordinates back to atlas-space.

```csharp
// Core transform maintained by the preview system
Matrix _viewMatrix = Matrix.Identity;
float _zoom = 1.0f;
Vector2 _pan = Vector2.Zero;

void RebuildViewMatrix()
{
    _viewMatrix = Matrix.CreateScale(_zoom, _zoom, 1f)
                * Matrix.CreateTranslation(_pan.X, _pan.Y, 0f);
}

// Atlas-space point from mouse screen position
Vector2 AtlasPointFromScreen(Vector2 screenPos)
{
    Matrix inverse = Matrix.Invert(_viewMatrix);
    return Vector2.Transform(screenPos, inverse);
}
```

### SpriteBatch Usage Patterns

The preview system uses multiple `SpriteBatch.Begin()`/`End()` passes per frame to separate coordinate spaces:

```csharp
// Pass 1a: Checkerboard background (needs LinearWrap for tiling)
_spriteBatch.Begin(
    samplerState: SamplerState.LinearWrap,
    transformMatrix: _viewMatrix);
DrawCheckerboard();
_spriteBatch.End();

// Pass 1b: Atlas content (needs PointClamp for pixel-perfect zoom)
// NOTE: Checkerboard and atlas MUST be separate Begin/End passes because
// the checkerboard requires SamplerState.LinearWrap for tiling while the
// atlas requires SamplerState.PointClamp for crisp pixel rendering at zoom.
_spriteBatch.Begin(
    samplerState: _zoom > 1f ? SamplerState.PointClamp : SamplerState.LinearClamp,
    transformMatrix: _viewMatrix);
DrawAtlasTexture();
DrawGlyphOverlays();   // MonoGame.Extended shapes in atlas-space
_spriteBatch.End();

// Pass 2: Screen-space HUD (not affected by zoom/pan)
_spriteBatch.Begin();
DrawDimensionsBadge();
DrawPageIndicator();
DrawZoomPercentage();
_spriteBatch.End();

// Pass 3: GUM UI layer
GumService.Draw();
```

Sampler state switches between `SamplerState.PointClamp` (nearest-neighbor, zoom > 1.0) and `SamplerState.LinearClamp` (bilinear, zoom <= 1.0) to match pixel-art rendering conventions.

### Texture2D Creation from Atlas Data

```csharp
Texture2D CreateAtlasTexture(GraphicsDevice device, AtlasPage page)
{
    // AtlasPage.PixelData is RGBA32 byte[] (4 bytes per pixel)
    var texture = new Texture2D(device, page.Width, page.Height,
        mipmap: false, SurfaceFormat.Color);
    texture.SetData<byte>(page.PixelData);
    return texture;
}

// For Grayscale format, expand to RGBA:
byte[] ExpandGrayscaleToRgba(byte[] grayscale, int width, int height)
{
    var rgba = new byte[width * height * 4];
    for (int i = 0; i < grayscale.Length; i++)
    {
        int j = i * 4;
        rgba[j] = 255;           // R
        rgba[j + 1] = 255;       // G
        rgba[j + 2] = 255;       // B
        rgba[j + 3] = grayscale[i]; // A (glyph data as alpha)
    }
    return rgba;
}
```

---

## Wave 1 — Atlas Preview Panel

The foundation: display generated atlas textures with zoom, pan, and transparency visualization.

### Tasks

| # | Task | Details |
|---|------|---------|
| 1.1 | Create `AtlasPreviewPanel` | Panel region within the MonoGame `Game` window managed by GUM layout. Uses a `ContainerRuntime` that defines the drawable region bounds. The `Game.Draw()` method uses `GraphicsDevice.ScissorRectangle` set to the panel bounds to clip atlas rendering. All atlas-space SpriteBatch drawing is constrained to this region. |
| 1.2 | Atlas texture loading | Convert `AtlasPage.PixelData` + `AtlasPage.Format` into `Texture2D`. For `PixelFormat.Rgba32`: create `Texture2D(device, width, height, false, SurfaceFormat.Color)` and call `texture.SetData<byte>(pixelData)`. For `PixelFormat.Grayscale`: expand to RGBA (white pixels with alpha from grayscale value) before `SetData`. Cache `Texture2D` instances — only recreate when `BmFontResult` changes. Dispose previous textures via `texture.Dispose()`. |
| 1.3 | Checkered transparency background | Pre-generate a small checkerboard `Texture2D` (16x16 px, 2x2 tile pattern). Light mode colors: `#CCCCCC` and `#FFFFFF`. Dark mode: `#333333` and `#444444`. Tile size per checker square: 8px. Draw via `SpriteBatch.Draw()` with `SamplerState.LinearWrap` and a destination rectangle covering the full atlas area. The source rectangle is set to `(0, 0, atlasWidth, atlasHeight)` and the wrapping sampler tiles the small texture across the entire area. This is GPU-efficient — a single draw call regardless of atlas size. |
| 1.4 | Basic rendering pipeline | In `Game.Draw()`: set scissor rectangle to panel bounds, `SpriteBatch.Begin(transformMatrix: _viewMatrix, samplerState: currentSampler, rasterizerState: scissorRasterizer)`, draw checkerboard, draw atlas `Texture2D` via `SpriteBatch.Draw(atlasTexture, Vector2.Zero, Color.White)`, `SpriteBatch.End()`. Use `SamplerState.PointClamp` when `_zoom > 1.0f` (nearest-neighbor for pixel accuracy) and `SamplerState.LinearClamp` when `_zoom <= 1.0f` (smooth downscaling). Create a `RasterizerState` with `ScissorTestEnable = true` for clipping. |
| 1.5 | Zoom controls | GUM toolbar buttons and a `TextRuntime` showing the current zoom percentage. Range: 25% to 800%, default "fit to window". Snap levels on double-click: 25%, 50%, 100%, 200%, 400%, 800%. "Fit" button: calculates scale as `Math.Min((panelWidth - 32) / atlasWidth, (panelHeight - 32) / atlasHeight)` and centers the atlas. "1:1" button: sets zoom to 1.0 and centers. Store zoom as `float _zoom` and call `RebuildViewMatrix()` on change. |
| 1.6 | Mouse wheel zoom | In `Game.Update()`, read `Mouse.GetState().ScrollWheelValue` delta. Zoom in/out by 10% per scroll notch. Zoom is centered on cursor position: compute atlas-space point under cursor before zoom, apply new zoom, compute the translation needed to keep that atlas-space point under the cursor. Clamp to 25%-800%. Implementation: `_pan += (mouseAtlasPoint * (_zoom - newZoom))` adjustment, then `RebuildViewMatrix()`. |
| 1.7 | Pan (click + drag) | Middle mouse button or Space+left-click to pan. On `ButtonState.Pressed`, record `_panStart = mousePosition` and `_panStartOffset = _pan`. On mouse move while panning, `_pan = _panStartOffset + (mousePosition - _panStart)`. Update `_viewMatrix` via `RebuildViewMatrix()`. Set a custom grab cursor via `Mouse.SetCursor(MouseCursor.Hand)` during pan (MonoGame 3.8.2+), reset to `MouseCursor.Arrow` on release. |
| 1.8 | Atlas dimensions overlay | Semi-transparent dark badge in the bottom-right corner of the preview panel. Drawn in screen-space (second `SpriteBatch.Begin()` without transform). Use a `SpriteFont` loaded as a content asset or a GUM `TextRuntime`. Text: `"1024 x 512 (Page 1/3)"`. Background: filled rectangle via `MonoGame.Extended ShapeExtensions.FillRectangle()` with `Color(0, 0, 0, 128)` and 4px padding around text. Positioned relative to panel bounds, not atlas-space — does not scale with zoom. |
| 1.9 | Multi-page navigation | When `BmFontResult.Pages.Count > 1`, show GUM `SpriteRuntime` arrow buttons (left/right) and page indicator dots at the bottom of the preview panel. Keyboard shortcuts: `[` previous page, `]` next page. Current page index stored in `_currentPageIndex`. Changing page swaps the active `Texture2D` reference. Arrow buttons are `ContainerRuntime` with click handlers registered via GUM event system. |
| 1.10 | Keyboard shortcuts | Handle via `Keyboard.GetState()` in `Game.Update()` with previous-frame state tracking to detect key presses (not held). `0` — fit to window. `1` — actual size (100%). `OemPlus`/`OemMinus` — zoom in/out. Arrow keys — pan by 50px. `Home` — reset view (fit + page 1). Track `_previousKeyboardState` to detect rising edges: `currentState.IsKeyDown(key) && !_previousKeyboardState.IsKeyDown(key)`. |
| 1.11 | Empty state | When no `BmFontResult` is available, draw a centered message: "Load a font and click Generate to see a preview". Use a GUM `TextRuntime` centered in the panel container, with subdued text color (`Color.Gray`). Hide this element (set `Visible = false`) when a result is available. Optionally include a subtle icon via a small `SpriteRuntime`. |
| 1.12 | Performance: dirty tracking | Only perform atlas rendering work when state changes. Track a `bool _dirty` flag set by property changes (new result, zoom, pan, overlay toggle). In `Game.Draw()`, skip atlas `SpriteBatch` passes when `!_dirty` and re-use the previous frame's `RenderTarget2D` instead. Cache the preview to a `RenderTarget2D` sized to the panel bounds. On dirty, render to the target; on clean frames, blit the cached target. Invalidate on window resize. |

### Keyboard Shortcut Summary (Wave 1)

| Key | Action |
|-----|--------|
| `0` | Fit to window |
| `1` | Actual size (100%) |
| `+` / `=` | Zoom in |
| `-` | Zoom out |
| Arrow keys | Pan 50px |
| `Home` | Reset view |
| `[` / `]` | Previous / next atlas page |
| Middle mouse drag | Pan |
| Scroll wheel | Zoom at cursor |

---

## Wave 2 — Glyph Inspector

Hover and click glyphs on the atlas to see their metrics. Makes the abstract numbers in a .fnt file tangible.

### Tasks

| # | Task | Details |
|---|------|---------|
| 2.1 | Build glyph hit-test index | On new `BmFontResult`, build a spatial lookup from `BmFontModel.Characters`. Each `CharEntry` defines a rectangle `(X, Y, Width, Height)` on a `Page`. Store as `Dictionary<int, List<CharEntry>>` keyed by page index. For hit-testing, iterate the current page's entries and test point-in-rect against the mouse position in atlas-space. For large character sets (>500 glyphs), use a simple grid-based spatial hash (atlas divided into 64px cells) for O(1) lookup. Use `Rectangle.Contains(Point)` from MonoGame for hit testing. |
| 2.2 | Hover detection | In `Game.Update()`, convert mouse position to atlas-space via `AtlasPointFromScreen()`. Query the hit-test index for the current page. If a `CharEntry` is under the cursor, set `_hoveredGlyph`. If no glyph, clear `_hoveredGlyph`. Throttle hit-test computation by skipping frames where the mouse position hasn't changed (compare to `_lastMousePosition`). Set `_dirty = true` when hover state changes. |
| 2.3 | Hover tooltip | When `_hoveredGlyph` is set, draw a tooltip near the cursor in screen-space. Rendered in the HUD SpriteBatch pass (no transform matrix). Use a `SpriteFont` for text and `MonoGame.Extended ShapeExtensions.FillRectangle()` for the background. Contents: character display and codepoint (e.g., `A  U+0041`), position `X: 128, Y: 64`, size `32 x 40`, offsets `XOffset: 1, YOffset: 3`, advance `XAdvance: 30`, page and channel. Background: `Color(32, 32, 32, 240)` filled rectangle with 6px padding. Position offset 16px right and 16px below cursor. Clamp to screen bounds to prevent overflow. Alternatively, implement as a GUM `ContainerRuntime` with child `TextRuntime` elements, repositioned each frame. |
| 2.4 | Hover highlight | Draw a 1px colored border around the hovered glyph's rectangle on the atlas. Drawn in atlas-space SpriteBatch pass so it scales with zoom. Use `MonoGame.Extended ShapeExtensions.DrawRectangle(spriteBatch, rect, Color(68, 136, 255), thickness)` where `thickness = 1f / _zoom` to maintain 1 screen-pixel width regardless of zoom level. |
| 2.5 | Click to select | On left mouse button press in `Game.Update()` (rising edge detection), if a glyph is under the cursor, set `_selectedGlyph`. Selected state persists until another glyph is clicked or Escape is pressed. Selected glyph gets a thicker highlight border (2px screen-equivalent, color `Color(255, 170, 0)`) and a filled semi-transparent overlay via `ShapeExtensions.FillRectangle(spriteBatch, rect, Color(255, 170, 0, 32))`. |
| 2.6 | Glyph detail panel | When a glyph is selected, show a detail panel implemented as a GUM `ContainerRuntime` anchored to the right edge of the preview area. Contents: large rendering of the glyph (extract the glyph's source rectangle from the atlas `Texture2D` and draw it at 4x-8x scale with `SamplerState.PointClamp` into the panel region), full metrics table using `TextRuntime` elements (Id, X, Y, Width, Height, XOffset, YOffset, XAdvance, Page, Channel), Unicode name if available. The panel is a `ContainerRuntime` with `NineSliceRuntime` background, scrollable if content exceeds panel height. |
| 2.7 | Glyph metrics visualization | When a glyph is selected, draw visual markers overlaid on the atlas in atlas-space: baseline (horizontal blue line via `ShapeExtensions.DrawLine`), advance width (vertical green line at XAdvance distance from glyph origin), bearing lines (dashed lines showing XOffset/YOffset). Dashed lines: MonoGame.Extended does not natively support dash patterns, so implement by drawing a series of short line segments with gaps (e.g., 4px drawn, 4px gap, in atlas-space). Lines extend 8px beyond the glyph bounds for visibility. |
| 2.8 | Glyph list sidebar | A scrollable list of all glyphs in the current result, shown in a collapsible GUM `ContainerRuntime` sidebar on the left or right edge. Each row is a `ContainerRuntime` with `TextRuntime` children: character, codepoint, dimensions. Clicking a row selects the glyph and adjusts `_zoom` and `_pan` to center the glyph in the preview area. Searchable via a `TextRuntime` input field at the top (GUM does not have built-in text input, so implement a simple character-by-character input handler using `TextInput` event from `Game.Window`). Virtualize by only creating GUM elements for visible rows, recycling them on scroll. |
| 2.9 | Copy glyph info | Right-click detection in `Game.Update()` (right mouse button rising edge). When right-clicking a selected glyph, show a context menu (GUM `ContainerRuntime` positioned at mouse, with clickable menu items as child containers). Options: "Copy Metrics" (copies the .fnt char line, e.g., `char id=65 x=128 y=64 width=32 height=40 xoffset=1 yoffset=3 xadvance=30 page=0 chnl=15`), "Copy Codepoint" (copies `U+0041`), "Copy Character" (copies `A`). Use `SDL2.SDL.SDL_SetClipboardText()` for clipboard access (MonoGame DesktopGL runs on SDL2). |
| 2.10 | Multi-select | Ctrl+click (detect `Keyboard.GetState().IsKeyDown(Keys.LeftControl)`) to add glyphs to a `HashSet<CharEntry> _selectedGlyphs`. Shift+click to select a range by codepoint order between the last-selected and clicked glyph. All selected glyphs highlighted with the selection overlay. Detail panel shows summary when multiple selected: count, total area, average dimensions. Clear multi-selection on Escape. |

---

## Wave 3 — Sample Text Preview

The killer feature: type text and see it rendered using the generated bitmap font, exactly as a game engine would display it.

### Tasks

| # | Task | Details |
|---|------|---------|
| 3.1 | Sample text input | GUM `ContainerRuntime` at the top of the preview panel containing a text input area. Since GUM code-only does not provide a native text input control, implement a custom text input handler: register `Game.Window.TextInput += OnTextInput` for character input, handle `Keys.Back` for deletion, `Keys.Enter` for newline, and arrow keys for cursor movement. Display the typed text in a `TextRuntime` element with a blinking cursor (toggle cursor visibility every 500ms via `GameTime`). Default content: "The quick brown fox jumps over the lazy dog" (pangram). Text changes set `_textPreviewDirty = true` (debounced 100ms via elapsed time tracking). Store in `PreviewState.SampleText`. |
| 3.2 | Text layout engine | Given sample text and `BmFontModel`, compute glyph positions. Build a `Dictionary<int, CharEntry>` from `BmFontModel.Characters` keyed by `CharEntry.Id` for O(1) lookup. Layout algorithm: start at origin `Vector2.Zero`, for each character look up its `CharEntry`, record position. Advance X by `CharEntry.XAdvance`. On newline character, reset X to 0 and advance Y by `CommonBlock.LineHeight`. Output: `List<(CharEntry entry, Vector2 position)>` — the positioned glyph list used by the renderer. Handle missing characters by skipping or inserting a placeholder. |
| 3.3 | Kerning application | After basic layout, apply kerning adjustments. Build a kerning lookup: `Dictionary<(int first, int second), int>` from `BmFontModel.KerningPairs` (each `KerningEntry` has `First`, `Second`, `Amount`). During layout, for each adjacent character pair, look up the pair and adjust the running X position by `Amount` pixels before placing the second glyph. This shifts all subsequent glyphs on the same line. |
| 3.4 | Text rendering via atlas compositing | Render the laid-out text by compositing glyph regions from the atlas `Texture2D`. For each positioned glyph: source rectangle = `new Rectangle(entry.X, entry.Y, entry.Width, entry.Height)` from the atlas texture for `entry.Page`. Destination position = `glyphPosition + new Vector2(entry.XOffset, entry.YOffset)`. Draw via `SpriteBatch.Draw(atlasTextures[entry.Page], destinationPosition, sourceRect, Color.White)`. This produces pixel-accurate output matching what a game engine would render. Use `SamplerState.PointClamp` to avoid interpolation artifacts. |
| 3.5 | Preview mode toggle | GUM toolbar with mode buttons: "Atlas View" and "Text Preview". In text preview mode, the atlas rendering is replaced by the sample text rendering. Alternatively, split the panel vertically using two GUM `ContainerRuntime` regions: atlas on top (60% height), text preview on bottom (40% height), with a draggable divider (track mouse drag on a thin horizontal `ContainerRuntime` bar). Keyboard shortcut: `Tab` to toggle modes when preview panel is focused. Track `PreviewMode _previewMode` enum. |
| 3.6 | Background color picker | GUM dropdown control (custom: a `ContainerRuntime` that toggles a popup `ContainerRuntime` with color swatches). Presets: White, Black, Dark Gray (`Color(51, 51, 51)`), Checkered (transparency). Each preset is a small colored `NineSliceRuntime` square with click handler. For custom color, implement a simple RGB slider panel using three horizontal bar `ContainerRuntime` controls with drag-to-adjust. Pass the chosen background color to `GraphicsDevice.Clear(backgroundColor)` before the text preview SpriteBatch pass. Store in `PreviewState.BackgroundColor`. |
| 3.7 | Preview scale options | GUM toolbar buttons: "1x", "2x", "3x". At 1x, glyphs render at their atlas pixel size. At 2x, apply `Matrix.CreateScale(2f)` to the text preview SpriteBatch transform — each atlas pixel maps to 2x2 screen pixels. At 3x, scale factor 3. Always use `SamplerState.PointClamp` at higher scales for crisp pixel rendering. This simulates how the font looks at different display densities or when a game engine scales the text. |
| 3.8 | Typography overlay lines | Optional overlay showing typographic reference lines on the text preview, drawn in the same atlas-space SpriteBatch pass. **Baseline**: blue line via `ShapeExtensions.DrawLine(spriteBatch, start, end, Color.Blue, 1f)`. **Ascent line**: green dashed line at `CommonBlock.Base` pixels above baseline. **Descent line**: red dashed line at `CommonBlock.LineHeight - CommonBlock.Base` below baseline. **Line height bracket**: gray vertical bracket showing full `CommonBlock.LineHeight`. Toggle via GUM toolbar button or `L` key. Dashed lines implemented as segmented `DrawLine` calls (4px on, 4px off). |
| 3.9 | Missing character indicator | When a character in the sample text has no matching `CharEntry`, render a fallback indicator. Draw a red-outlined rectangle via `ShapeExtensions.DrawRectangle(spriteBatch, rect, Color.Red, 2f)` sized to the average glyph dimensions, with a "?" character drawn inside using a MonoGame `SpriteFont`. Tooltip on hover (same mechanism as Wave 2): "Character 'X' (U+XXXX) not in generated font". Count of missing characters shown in the HUD overlay. |
| 3.10 | Word wrap | Optional word wrap toggle (GUM checkbox-style button). When enabled, text wraps at the preview panel width (accounting for current zoom). Use a greedy line-breaking algorithm: accumulate `XAdvance` values; when the running X exceeds `panelWidth / _zoom`, break at the last whitespace character — reset X to 0 and advance Y by `CommonBlock.LineHeight`. Re-run layout when panel is resized or zoom changes. |
| 3.11 | Text color tinting | GUM color picker (same pattern as 3.6) for text foreground color. Default: `Color.White` (renders atlas pixels as-is). When a color is selected, pass it as the `color` parameter to `SpriteBatch.Draw()`: `SpriteBatch.Draw(atlas, pos, srcRect, tintColor)`. MonoGame's SpriteBatch multiplies the texture color by the tint color, producing the same effect as a `Modulate` blend. |
| 3.12 | Preset sample texts | GUM dropdown with preset sample texts: "Pangram" ("The quick brown fox..."), "Alphabet" (A-Z, a-z, 0-9), "Lorem Ipsum" (paragraph), "Numbers" (0-9 with punctuation), "Special Characters" (common symbols), "Custom" (user's typed text). Selecting a preset replaces `PreviewState.SampleText` and triggers re-layout. Implement as a popup `ContainerRuntime` list with `TextRuntime` items, click handlers set the sample text. |

---

## Wave 4 — Kerning Pair Visualization

Kerning is invisible by definition — this wave makes it visible.

### Tasks

| # | Task | Details |
|---|------|---------|
| 4.1 | Kerning pairs data grid | Scrollable list panel (GUM `ContainerRuntime` with virtualized rows) showing all `KerningEntry` records from `BmFontModel.KerningPairs`. Columns: First (character + codepoint), Second (character + codepoint), Amount (signed integer, pixels), Visual (inline mini-preview). Implement virtualization by tracking scroll offset and only creating/updating GUM `ContainerRuntime` row elements for visible rows (row pool pattern). Sortable: click column header `TextRuntime` to sort; default sort by absolute amount descending. Track `SortColumn` and `SortDirection` state. |
| 4.2 | Kerning pair filter | GUM text input field above the data grid (same custom text input pattern as 3.1). Type a character to filter pairs containing that character (as either first or second). Filter by amount range via GUM toggle buttons: "Negative", "Positive", "> 1px", "> 2px". Filter chips are `ContainerRuntime` elements with toggle state (highlighted background when active). Filtering re-evaluates the visible row list and resets scroll to top. |
| 4.3 | Inline kerning preview | For each visible row in the kerning pairs grid, render a small (48px tall) inline preview showing the two characters with and without kerning. Render each preview pair into a `RenderTarget2D(device, previewWidth, 48)`: `SpriteBatch.Begin()` in the render target, draw the two glyphs composited from the atlas texture (same technique as Wave 3), `SpriteBatch.End()`. Display the render target texture in the row via `SpriteBatch.Draw()`. Cache these `RenderTarget2D` previews — only regenerate when the result changes. Pool render targets to avoid GPU memory churn. |
| 4.4 | Kerning pair detail view | Click a row in the kerning grid to open a larger detail view in a GUM `ContainerRuntime` panel. The two characters rendered at 4x-8x zoom with `SamplerState.PointClamp`. Draw a translucent overlay showing the kerning adjustment as a colored region: use `ShapeExtensions.FillRectangle()` — negative kerning (pulled together) in `Color(0, 200, 0, 64)` (green), positive kerning (pushed apart) in `Color(200, 0, 0, 64)` (red). Display the amount in pixels and as a percentage of the average XAdvance via `TextRuntime`. |
| 4.5 | Kerning overlay on sample text | In the text preview (Wave 3), optional overlay mode that visualizes where kerning is applied. During text rendering, for each character pair where kerning was applied, draw a vertical colored line between the characters: `ShapeExtensions.DrawLine(spriteBatch, top, bottom, color, 1f)` — green for negative/tightening, red for positive/loosening. Draw the pixel amount above the line using `SpriteFont`. Toggle via GUM toolbar button: "Show Kerning" or `K` key. |
| 4.6 | Kerning distribution chart | Simple histogram rendered directly with MonoGame SpriteBatch and MonoGame.Extended shapes. X-axis: amount in pixels (negative to positive). Y-axis: count of pairs. Each bar is a `ShapeExtensions.FillRectangle()` call. Axis lines via `DrawLine()`. Labels via `SpriteFont.DrawString()`. Render into a GUM panel region. No charting library dependency. Helps users identify if kerning data is reasonable or if there are outlier values. |
| 4.7 | Kerning pair comparison | Select two character pairs and see them rendered side-by-side. Two small GUM text input fields for typing pairs (e.g., "AV" and "AW"). Render each pair into a `RenderTarget2D` at 4x zoom, display side-by-side in the panel. Pairs can also be selected from the grid by Ctrl+clicking two rows. Useful for comparing similar pairs to verify consistency. |
| 4.8 | Export kerning report | GUM button "Export CSV". On click, open a native save file dialog via SDL2 (or use a hardcoded export path with a simple GUM file name input). Generate CSV content: columns `First`, `FirstCodepoint`, `Second`, `SecondCodepoint`, `Amount`. Write via `System.IO.File.WriteAllText()`. For native file dialog, use `tinyfd` (tiny file dialogs) P/Invoke or a simple console path prompt. |
| 4.9 | Kerning statistics summary | Summary card as a GUM `ContainerRuntime` at the top of the kerning panel with `TextRuntime` children. Display: total pairs count, min/max/average amount, percentage of character combinations that have kerning, most-kerned character (appears in the most pairs). Computed once per `BmFontResult` change and cached. Quick health check for the kerning data. |
| 4.10 | Navigate from kerning to atlas | Double-click (detect two left-clicks within 300ms on the same row) a kerning pair's first or second character to jump to that glyph on the atlas view. Set `_previewMode = PreviewMode.Atlas`, look up the `CharEntry` for the character, set `_currentPageIndex` to the glyph's page, compute `_zoom` and `_pan` to center the glyph in the panel with 4x zoom, call `RebuildViewMatrix()`, and set `_selectedGlyph`. |

---

## Wave 5 — Side-by-Side Comparison

Compare different generation settings without losing your previous result.

### Tasks

| # | Task | Details |
|---|------|---------|
| 5.1 | Comparison mode toggle | GUM toolbar button "Compare" that restructures the preview panel into two side-by-side GUM `ContainerRuntime` regions, each with its own `RenderTarget2D` and atlas `Texture2D`. Left pane: "Before" (retains the last result). Right pane: "After" (shows the current/new result). Each pane has its own SpriteBatch pass rendering into its own `RenderTarget2D`, which is then drawn to screen in the pane's region. Exit comparison mode by clicking the button again or pressing `Escape`. |
| 5.2 | Result snapshot mechanism | "Snapshot" button that saves the current `BmFontResult` as the comparison baseline. Store the result reference and its `Texture2D` instances in `PreviewState.ComparisonBaseline` and `PreviewState.ComparisonTextures`. The snapshot includes the `FontGeneratorOptions` that produced it (for settings diff). Snapshot textures are independent copies — if the main result's textures are disposed on regeneration, the snapshot textures remain valid. |
| 5.3 | Synchronized zoom and pan | In comparison mode, zoom and pan are synchronized by default. Store a shared `_comparisonViewMatrix`. When zooming or panning in either pane (detect which pane the mouse is in by comparing mouse X to the divider), apply the same `_zoom`, `_pan`, and `RebuildViewMatrix()` to both panes. GUM toggle button "Sync" to enable/disable. When disabled, each pane maintains independent `_zoom`/`_pan`. |
| 5.4 | Settings diff display | GUM `ContainerRuntime` panel between the two comparison panes showing property-by-property differences. Compare the two `FontGeneratorOptions` objects via reflection or manual property comparison. Display changed properties as `TextRuntime` elements: e.g., "Size: 32 -> 48", "Outline: 0 -> 2". Highlight changes with a yellow background `NineSliceRuntime`. Only show changed properties. |
| 5.5 | Pixel diff mode | GUM toggle button for pixel diff visualization. Compute the per-pixel difference between the two atlas `Texture2D` instances: extract pixel data via `texture.GetData<byte>()` for both, compute absolute difference per channel, amplify by 4x for visibility. Create a diff `Texture2D` and call `SetData<byte>()` with the diff result. Display modes: (a) **Difference** — show the diff texture. (b) **Split** — draw left half from "before" texture, right half from "after" texture, with a draggable vertical divider line. Implement split by using two `SpriteBatch.Draw()` calls with source rectangles clipped to left/right halves. (c) **Blink** — alternate between the two textures every 500ms (track elapsed time via `GameTime.TotalGameTime`). Diff computation runs on `Task.Run()` and updates the diff texture when complete. |
| 5.6 | Atlas size comparison | GUM overlay (`TextRuntime` elements) showing atlas dimensions for both results: "Before: 1024x512 (1 page) -> After: 512x512 (1 page)". Size reduction text in `Color.Green`, increase in `Color.Red`. Show pixel waste percentage (unused atlas area / total atlas area) for both, computed from glyph coverage. |
| 5.7 | Glyph count comparison | GUM `TextRuntime` summary: "Before: 95 glyphs -> After: 191 glyphs (+96)". Compute diff by comparing `CharEntry.Id` sets between the two results. List added/removed characters in a scrollable GUM panel if the user clicks "Details". |
| 5.8 | Sample text comparison | In text preview mode during comparison, render the sample text in both panes using their respective `BmFontResult` data. Same text, same scale. Each pane composites glyphs from its own atlas textures. Users directly see the visual impact of setting changes on rendered text. |
| 5.9 | Swap panes | GUM button "Swap" (or `X` key in comparison mode). Swap `PreviewState.ComparisonBaseline` and `PreviewState.CurrentResult` references, along with their `Texture2D` arrays. Simple reference swap — no texture recreation needed. |
| 5.10 | Quick A/B presets | GUM dropdown with comparison presets: "With/Without Anti-Aliasing", "With/Without Outline", "Size Comparison (current vs 2x)". Selecting a preset clones the current `FontGeneratorOptions`, modifies the relevant property, runs `BmFont.Generate()` on a background thread, and enters comparison mode with the new result as "After". Saves the user from manually changing settings. |

---

## Wave 6 — Preview Overlays & Visualization Modes

Optional visual aids that help users understand atlas layout, packing efficiency, and glyph structure.

### Tasks

| # | Task | Details |
|---|------|---------|
| 6.1 | Overlay toggle toolbar | A GUM `ContainerRuntime` toolbar (or dropdown via popup `ContainerRuntime`) with toggle buttons for each overlay type. Each button is a `ContainerRuntime` with `TextRuntime` label that toggles highlighted/unhighlighted on click. Overlays are drawn in atlas-space on top of the atlas texture in the SpriteBatch pass, after drawing the atlas but before `SpriteBatch.End()`. Store overlay state in `PreviewState.ActiveOverlays` (flags enum `[Flags] enum OverlayType` or `HashSet<OverlayType>`). |
| 6.2 | Glyph bounding boxes overlay | Draw a 1px rectangle around every glyph on the current atlas page. For each `CharEntry` on the current page: `ShapeExtensions.DrawRectangle(spriteBatch, new RectangleF(entry.X, entry.Y, entry.Width, entry.Height), Color(0, 255, 255, 128), 1f / _zoom)`. Drawn in atlas-space so they scale with zoom. Useful for seeing packing density and identifying wasted space between glyphs. For 1000+ glyphs, this is ~1000 draw calls per frame — acceptable for modern GPUs, but monitor frame time. |
| 6.3 | Padding visualization | Highlight the padding area around each glyph. If uniform padding is configured via `FontGeneratorOptions.Padding`, draw an inner border inside each glyph rect of that padding width. Use `ShapeExtensions.FillRectangle()` with `Color(255, 0, 0, 48)` for the padding region. For each glyph, draw four thin filled rectangles (top, right, bottom, left padding strips) inside the `CharEntry` bounds. |
| 6.4 | Grid lines overlay | Draw horizontal and vertical grid lines across the atlas at configurable intervals. Default: lines every 64 pixels. Use `ShapeExtensions.DrawLine(spriteBatch, start, end, Color(128, 128, 128, 64), 1f / _zoom)`. For a 2048x2048 atlas at 64px intervals: 32 horizontal + 32 vertical = 64 lines. Draw coordinate labels at edges every 128px or 256px using `SpriteFont.DrawString()`. Grid interval configurable via a GUM numeric input in the overlay settings panel. |
| 6.5 | Channel isolation | Four GUM toggle buttons: R, G, B, A. When a channel is isolated, render the atlas with a custom pixel shader (MonoGame `Effect`) that zeros non-selected channels and maps the selected channel to grayscale. The shader is a simple HLSL effect: `float4 result = float4(tex.r * maskR, tex.g * maskG, tex.b * maskB, 1.0)` where mask values are 0 or 1. For alpha isolation, show alpha as white-on-black: `float4(tex.a, tex.a, tex.a, 1.0)`. Load the effect via `Content.Load<Effect>()` or compile at runtime. Pass the effect to `SpriteBatch.Begin(effect: channelEffect)`. Essential for inspecting channel-packed fonts. |
| 6.6 | Glyph metrics overlay (all glyphs) | Extension of Wave 2's per-glyph metrics to all glyphs simultaneously. For every `CharEntry` on the current page: draw advance width tick (short vertical line at `entry.X + entry.XAdvance` relative to entry origin, via `DrawLine`), origin crosshair (2px cross at `entry.X - entry.XOffset, entry.Y - entry.YOffset`). Use thin lines: `thickness = 0.5f / _zoom` to stay sub-pixel at high zoom. Toggle independently from individual glyph inspection. May produce visual clutter at low zoom — only render when `_zoom >= 2.0f`. |
| 6.7 | Packing efficiency heatmap | Divide the atlas into a grid (e.g., 32x32px cells). For each cell, calculate what percentage is occupied by glyph rectangles vs. empty space (intersect `CharEntry` rects with the cell rect). Map to a color gradient: green (`Color(0, 200, 0, 80)` for >80% utilization), yellow (`Color(200, 200, 0, 80)` for 40-80%), red (`Color(200, 0, 0, 80)` for <40%). Draw each cell as `ShapeExtensions.FillRectangle()` with the mapped color. Compute the heatmap data once per `BmFontResult` change and cache as a `Color[]` array. For a 2048x2048 atlas at 32px cells: 64x64 = 4096 rectangles per frame. |
| 6.8 | Dark / light background toggle | GUM toggle button (or `D` key) to switch the checkerboard background between light mode and dark mode colors. Update the pre-generated checkerboard `Texture2D` with new tile colors. Alternatively, maintain two checkerboard textures and swap which one is used. Persist preference across sessions via a simple JSON settings file. |
| 6.9 | Zoom percentage indicator | GUM `TextRuntime` in the toolbar showing current zoom level as text (e.g., "150%"). Updated in `Game.Update()` whenever `_zoom` changes. Clicking the readout could open a simple GUM text input to type an exact percentage (parse with `float.TryParse`, clamp to 25-800 range, apply). Updates in real-time during mouse wheel zoom. |
| 6.10 | Minimap | When the atlas is zoomed in beyond the viewport, render a minimap in the bottom-left corner. Create a `RenderTarget2D(device, 150, 100)`. Render the full atlas into this target at a reduced scale: `SpriteBatch.Begin()` with `Matrix.CreateScale(150f / atlasWidth)`, draw atlas texture, `SpriteBatch.End()`. Draw a white rectangle outline on the minimap showing the current viewport (computed from `_viewMatrix` inverse applied to the panel corners). Draw the minimap texture in screen-space HUD pass. Click and drag on the minimap to reposition `_pan`: map click position within minimap to atlas-space coordinates and update `_pan` accordingly. |
| 6.11 | Ruler guides | Pixel rulers along the top and left edges of the preview panel, drawn in screen-space but with labels computed from atlas-space. Implement as a dedicated SpriteBatch pass clipped via `ScissorRectangle` to 20px strips at the top and left of the panel. Use `DrawLine` for tick marks: small ticks every 16 atlas-pixels, medium every 64, large every 256 with `SpriteFont` coordinate labels. Translate tick positions from atlas-space to screen-space via `_viewMatrix` to ensure they scroll with the atlas. |
| 6.12 | SDF distance visualization | When the generated font uses SDF mode, offer a special overlay that color-maps the signed distance field. Implement as a custom MonoGame `Effect` (HLSL pixel shader) that maps distance values to a color gradient: negative distances (inside glyph) in warm colors (red/yellow), zero crossing (glyph edge) in white, positive distances (outside) in cool colors (blue/cyan). The shader samples the atlas texture's alpha channel (which contains the SDF distance) and maps it through a color ramp. Apply the effect via `SpriteBatch.Begin(effect: sdfVisualizeEffect)`. Include the .fx shader file in the content pipeline. |

---

## Wave 7 — Live / Auto-Regeneration

Close the feedback loop: change a setting, see the result immediately.

### Tasks

| # | Task | Details |
|---|------|---------|
| 7.1 | Auto-regenerate toggle | GUM toolbar toggle button: "Auto" with a refresh icon (`SpriteRuntime`). When enabled, any change to `FontGeneratorOptions` properties triggers a regeneration. Default: enabled for small character sets (<500 glyphs), disabled for large sets. State stored in `PreviewState.AutoRegenerate`. Toggle button shows highlighted (e.g., green tint) when active. |
| 7.2 | Change debouncing | Track `_lastSettingChangeTime` as a `TimeSpan` from `GameTime.TotalGameTime`. In `Game.Update()`, check if `_pendingRegeneration` is true and `elapsed - _lastSettingChangeTime > debounceInterval` (default 500ms). If so, trigger regeneration and clear the pending flag. If another setting change arrives during the wait, update `_lastSettingChangeTime` to reset the timer. Debounce interval configurable (200ms-2000ms) via a GUM slider in settings. This prevents regenerating on every keystroke when typing "48" (which would otherwise generate at "4" then "48"). |
| 7.3 | Background generation | Run `BmFont.Generate()` on a `Task.Run()` background thread. The MonoGame `Game.Update()` loop continues running at 60fps during generation. Use `CancellationTokenSource` to discard stale results when a new request arrives — note that `BmFont.Generate()` does not accept a `CancellationToken` (see Phase 55 for the planned API addition), so cancellation here means "discard the result when it completes," not true mid-generation abort. The background task checks the token after `Generate()` returns and skips marshaling if cancelled. The background task sets a `_generationComplete` flag and stores the result in a thread-safe field. In `Game.Update()`, check the flag and marshal the result to the main thread: create `Texture2D` instances (must happen on the main thread with access to `GraphicsDevice`), update `PreviewState`, and set `_dirty = true`. |
| 7.4 | Generation progress indicator | While generation is in progress, show visual feedback: (a) Thin progress bar at the top of the preview panel — a GUM `ContainerRuntime` with `NineSliceRuntime` fill that animates width. For indeterminate mode, animate a sliding highlight using `GameTime`. (b) "Generating..." text overlay — GUM `TextRuntime` in the corner with a spinning indicator (cycle through `\|/-` characters every 100ms via `GameTime`). (c) If `PipelineMetrics` is enabled, show stage labels. Indicator hidden when generation completes (set `Visible = false`). |
| 7.5 | Generation timing display | After generation completes, update a GUM `TextRuntime` in the status bar: "Generated in 1.23s (95 glyphs, 512x256)". If `PipelineMetrics` is available, show breakdown on hover (implement hover detection on the `TextRuntime` bounds and display a tooltip `ContainerRuntime`): "Parse: 12ms, Rasterize: 890ms, Pack: 45ms, Encode: 283ms". Helps users understand which settings impact generation time. |
| 7.6 | Smart regeneration gating | Classify setting changes by type to determine debounce behavior. For numeric inputs (font size, outline width): only trigger regeneration on focus-lost or Enter key, not on each digit typed. Detect this by tracking whether the text input is "active" (has focus). For toggle buttons (checkboxes): regenerate immediately on click. For dropdowns: regenerate immediately on selection change. For text inputs (character set): use longer debounce (1000ms). Implement via a `SettingChangeType` enum (`Immediate`, `Debounced`, `OnCommit`) associated with each setting control. |
| 7.7 | Regeneration queue | If a regeneration is already in progress (tracked via `_isGenerating` flag) and a new request arrives (after debounce), cancel the current `CancellationTokenSource`, store the latest `FontGeneratorOptions` as `_queuedOptions`, and set `_hasQueuedRequest = true`. "Cancel" here means the token is signalled so the background task discards its result upon completion — `BmFont.Generate()` itself runs to completion because it has no `CancellationToken` parameter (see Phase 55 for the planned API addition). When the current generation completes (or its result is discarded), check `_hasQueuedRequest` and start the queued generation. Maximum queue depth: 1 (only the latest request matters). State machine: `Idle -> Generating -> (Queued) -> Generating -> Idle`. |
| 7.8 | Large character set warning | When the character set exceeds 1000 glyphs and auto-regenerate is enabled, show a one-time GUM dialog (modal `ContainerRuntime` overlay with semi-transparent background): "Large character set detected. Auto-regeneration may be slow. Consider disabling auto-regeneration." Two buttons: "Disable Auto" (turns off auto-regen) and "Keep Enabled" (dismisses). Track `_largeSetWarningShown` to avoid repeat warnings. Persist the dismissal in the settings JSON file. |
| 7.9 | Performance budget indicator | Colored dot (GUM `SpriteRuntime` with a small circular texture) next to the auto-regenerate toggle. Color indicates estimated generation speed based on current settings: Green (`Color.LimeGreen`, <1s estimated), Yellow (`Color.Yellow`, 1-3s), Red (`Color.Red`, >3s). Heuristic: `estimatedMs = glyphCount * (baseMsPerGlyph) * effectMultiplier` where `baseMsPerGlyph` is calibrated from `PipelineMetrics` history, and `effectMultiplier` is 1.5x for outline, 1.3x for shadow. Updated whenever settings change. |
| 7.10 | Manual generate button | GUM toolbar button "Generate" — always visible regardless of auto-regenerate state. Clicking triggers immediate generation (bypasses debounce, sets `_pendingRegeneration = false` and starts generation directly). Keyboard shortcut: `F5` (detected in `Game.Update()` via rising edge). When auto-regen is disabled, this is the only way to regenerate. Button shows a subtle pulse animation (cycle `NineSliceRuntime` tint between normal and slightly brighter over 300ms) to indicate it's clickable when settings have changed since last generation. |
| 7.11 | Result diff notification | When a new result replaces the old one, briefly flash a colored border around the preview panel. Implement by drawing a `ShapeExtensions.DrawRectangle()` border in the HUD pass with an alpha that fades from 255 to 0 over 300ms (track `_flashTimer` via `GameTime`). Color: `Color.LimeGreen` for success, `Color.Orange` if the new result has more `FailedCodepoints` than the previous. |
| 7.12 | Preserve view state across regeneration | When a new `BmFontResult` arrives, preserve `_zoom`, `_pan`, `_currentPageIndex`, and `_selectedGlyph` (if the glyph's `CharEntry.Id` still exists in the new result — look it up in the new `Dictionary<int, CharEntry>`). Don't call `RebuildViewMatrix()` unless the user explicitly resets. Only auto-reset view when atlas dimensions change dramatically (>50% size change in either dimension): in that case, trigger "fit to window". |

---

## Performance Considerations

### Rendering Performance

| Concern | Mitigation |
|---------|------------|
| Large atlas textures (4096x4096) | `Texture2D` is GPU-resident after `SetData()`. SpriteBatch draws the full texture in a single draw call — no CPU-side pixel iteration. Avoid calling `GetData()` per-frame. Keep the `Texture2D` alive and only recreate on new `BmFontResult`. |
| Many overlay elements (1000+ glyph rects) | MonoGame.Extended `ShapeExtensions` creates geometry per call. For 1000+ overlays, batch into a single `VertexBuffer` of line primitives and draw with `GraphicsDevice.DrawPrimitives()`. Alternatively, render overlays to a `RenderTarget2D` and cache — only re-render when overlay settings or result change. |
| Sample text rendering with many glyphs | Pre-render sample text into an offscreen `RenderTarget2D`. Set render target, draw all glyph quads, restore default render target. Cache the render target and only re-render when text, result, or layout changes — not on zoom/pan. Draw the cached `RenderTarget2D` texture in the main pass. |
| Mouse hover hit-testing at 60fps | Spatial hash grid for O(1) glyph lookup. Avoid iterating all `CharEntry` records on every `Update()`. Skip hit-test when mouse hasn't moved (`_lastMousePosition == currentMousePosition`). |
| Comparison mode (two atlas textures) | Each pane renders into its own `RenderTarget2D`. Diff computation runs on `Task.Run()` — the pixel diff `Texture2D` is created on the main thread after computation completes. Cache diff until either result changes. |
| SpriteBatch draw call count | Group draws by texture to minimize texture swaps. Draw all glyphs from the same atlas page in one `SpriteBatch.Begin()`/`End()` block. MonoGame batches consecutive draws of the same texture into a single GPU draw call automatically. |

### Memory Management

| Concern | Mitigation |
|---------|------------|
| `Texture2D` and `RenderTarget2D` lifecycle | Implement `IDisposable` on the preview state class. Dispose previous `Texture2D` instances when new result arrives (call `texture.Dispose()`). Dispose `RenderTarget2D` caches on resize and on mode exit. Use `GraphicsDevice.DeviceReset` event to recreate render targets after device loss. |
| Comparison mode doubles texture memory | Only keep one comparison baseline set of textures in memory. Dispose them when exiting comparison mode. Warn (via HUD notification) if total texture memory exceeds 100MB (estimate: `width * height * 4` bytes per page). |
| Cached preview textures (inline kerning, sample text) | Use a pool of `RenderTarget2D` instances with a maximum count (e.g., 32). Recycle the oldest when the pool is full. Dispose all on `BmFontResult` change. |
| Content pipeline assets (SpriteFont, Effects) | Load once in `Game.LoadContent()`, disposed automatically by `ContentManager.Unload()` in `Game.UnloadContent()`. |

### Responsiveness Targets

| Interaction | Target Latency |
|-------------|---------------|
| Zoom / pan | < 16ms (60fps — MonoGame fixed update loop) |
| Glyph hover tooltip | < 16ms (computed in `Update()`) |
| Overlay toggle | < 16ms (flag change, redraw on next `Draw()`) |
| Sample text re-render (< 200 glyphs) | < 50ms (re-render cached `RenderTarget2D`) |
| Full regeneration (95 ASCII glyphs, 32px) | < 500ms |
| Full regeneration (1000 glyphs, 48px, outline) | < 5s (background thread, UI stays at 60fps) |

---

## State Architecture

### Core State

```
PreviewState
  |-- BmFontResult? CurrentResult
  |-- BmFontResult? ComparisonBaseline
  |-- Texture2D[] AtlasTextures          // one per page, from CurrentResult
  |-- Texture2D[] ComparisonTextures     // one per page, from ComparisonBaseline
  |-- string SampleText
  |-- PreviewMode Mode                   // Atlas / Text / Comparison
  |-- float Zoom
  |-- Vector2 Pan
  |-- Matrix ViewMatrix
  |-- int CurrentPageIndex
  |-- CharEntry? HoveredGlyph
  |-- CharEntry? SelectedGlyph
  |-- HashSet<CharEntry> SelectedGlyphs  // multi-select
  |-- OverlayType ActiveOverlays         // [Flags] enum
  |-- Color BackgroundColor
  |-- int PreviewScale                   // 1, 2, 3
  |-- bool AutoRegenerate
  |-- bool IsGenerating
  |-- bool IsDirty
  |-- string? GenerationStatus
  |-- TimeSpan? LastGenerationTime
  |-- byte[]? FontData                   // in-memory font bytes (mutually exclusive with FontPath/SystemFontFamily)
  |-- string? FontPath                   // path to .ttf/.otf/.woff file
  |-- string? SystemFontFamily           // system font family name
  |-- Dictionary<int, CharEntry> CharLookup
  |-- Dictionary<(int, int), int> KerningLookup
  |-- Dictionary<int, List<CharEntry>> PageGlyphIndex
```

### State Management

- All preview state lives in `PreviewState`, a plain C# class (no `INotifyPropertyChanged` — MonoGame uses a polling model in `Update()`).
- Property setters on `PreviewState` set `IsDirty = true` when render-affecting properties change.
- `Game.Update()` checks for input changes (mouse, keyboard), updates `PreviewState`, and manages debounce timers.
- `Game.Draw()` checks `IsDirty` — if true, re-renders to the cached `RenderTarget2D` and clears the flag. If false, blits the cached render target.
- Generation is triggered by `PreviewState.RequestGeneration()`, which manages debouncing, cancellation, and `Task.Run()`.
- Result arrival: the background task stores the `BmFontResult` in a thread-safe field. `Game.Update()` detects completion, creates `Texture2D` instances on the main thread (requires `GraphicsDevice`), rebuilds lookup dictionaries, and sets `IsDirty = true`.

### GUM UI Integration

- GUM is initialized in `Game.Initialize()` via `GumService.Initialize(graphicsDevice)` (code-only mode, no .gumx project file).
- All UI elements (toolbars, panels, buttons, text) are created as `ContainerRuntime`, `TextRuntime`, `SpriteRuntime`, and `NineSliceRuntime` instances in code.
- GUM layout handles automatic sizing, anchoring, and stacking for the UI chrome.
- GUM draws its layer via `GumService.Draw()` after all SpriteBatch passes, so UI elements always appear on top.
- Click/hover detection on GUM elements is handled by GUM's built-in cursor/event system or by manual bounds checking in `Game.Update()`.
- The atlas preview region is the remaining space after GUM toolbars and panels are laid out.

---

## Files to Create

| File | Purpose |
|------|---------|
| `apps/KernSmith.Ui/Rendering/AtlasPreviewRenderer.cs` | Main atlas rendering logic: SpriteBatch passes, checkerboard, atlas texture draw, zoom/pan transform |
| `apps/KernSmith.Ui/Rendering/TextPreviewRenderer.cs` | Sample text layout engine and glyph compositing from atlas textures |
| `apps/KernSmith.Ui/Rendering/OverlayRenderer.cs` | All overlay drawing: bounding boxes, metrics, grid, heatmap, SDF visualization |
| `apps/KernSmith.Ui/Rendering/GlyphHitTester.cs` | Spatial hash grid for O(1) glyph hit-testing from mouse position |
| `apps/KernSmith.Ui/Rendering/CheckerboardGenerator.cs` | Generates tiled checkerboard `Texture2D` for transparency background |
| `apps/KernSmith.Ui/Rendering/DiffRenderer.cs` | Pixel diff computation and diff `Texture2D` creation for comparison mode |
| `apps/KernSmith.Ui/State/PreviewState.cs` | Preview state: zoom, pan, selected glyph, overlays, generation orchestration |
| `apps/KernSmith.Ui/State/PreviewMode.cs` | Enum: `Atlas`, `Text`, `Comparison` |
| `apps/KernSmith.Ui/State/OverlayType.cs` | `[Flags]` enum: `BoundingBoxes`, `Padding`, `Grid`, `Metrics`, `Heatmap`, `Channels`, `SdfDistance` |
| `apps/KernSmith.Ui/Panels/AtlasPreviewPanel.cs` | GUM layout for the atlas preview region, toolbar buttons, page navigation |
| `apps/KernSmith.Ui/Panels/GlyphInspectorPanel.cs` | GUM panel for glyph detail view, metrics table, zoomed glyph rendering |
| `apps/KernSmith.Ui/Panels/KerningPairsPanel.cs` | GUM panel for kerning data grid, filter, statistics, inline previews |
| `apps/KernSmith.Ui/Panels/ComparisonPanel.cs` | Side-by-side comparison layout, settings diff, diff mode controls |
| `apps/KernSmith.Ui/Panels/SampleTextPanel.cs` | GUM panel for text input, preset selector, typography overlays |
| `apps/KernSmith.Ui/Effects/ChannelIsolation.fx` | HLSL pixel shader for R/G/B/A channel isolation |
| `apps/KernSmith.Ui/Effects/SdfVisualize.fx` | HLSL pixel shader for SDF distance field color mapping |

---

## Dependencies

| Dependency | Purpose | Version |
|------------|---------|---------|
| `MonoGame.Framework.DesktopGL` | Game framework: SpriteBatch, Texture2D, RenderTarget2D, input, render loop | 3.8.2+ |
| `Gum.MonoGame` | UI layout engine (code-only): ContainerRuntime, TextRuntime, SpriteRuntime | latest |
| `MonoGame.Extended` | Shape drawing primitives (DrawRectangle, DrawLine, FillRectangle), RectangleF | latest |
| `KernSmith` (project ref) | `BmFontResult`, `BmFontModel`, `AtlasPage`, all domain types | current |

No SkiaSharp, no Avalonia, no WriteableBitmap. All rendering is MonoGame SpriteBatch + Texture2D. All UI is GUM code-only.

---

## Core Library Notes (Document, Don't Fix)

These observations may inform future library changes but are out of scope for this phase:

1. **Individual glyph rasterization** — `IRasterizer.RasterizeGlyph(int codepoint, RasterOptions)` exists and could be used to render a single glyph preview without full generation. However, it returns a `RasterizedGlyph` without effects (outline, shadow). For a full-fidelity single-glyph preview, the pipeline from rasterization through compositing would need to run for one glyph. Currently, the pipeline is all-or-nothing.

2. **Progress reporting** — `PipelineMetrics` captures timing after the fact (stage durations), not during. There is no callback or progress-reporting mechanism during generation. For real-time progress bars, the pipeline would need to accept an `IProgress<GenerationProgress>` parameter.

3. **Generation time estimation** — No built-in estimation exists. A heuristic could be built from `PipelineMetrics` history: `estimated_time = (glyph_count / previous_glyph_count) * previous_total_time`, adjusted for effects. This would live in the UI layer, not the library.

4. **Cancellation support** — `BmFont.Generate()` does not accept a `CancellationToken`. Adding one would require threading it through the pipeline (font parser, rasterizer, packer, encoder). Until then, the UI can run generation on a background task and discard the result if superseded, but cannot cancel mid-generation. **Phase 55** tracks adding `CancellationToken` support to the core API; once available, tasks 7.3 and 7.7 should be updated to pass the token into `Generate()` for true mid-generation cancellation.

5. **Large character set performance** — Full Unicode coverage (65,000+ glyphs) may take 30+ seconds. The UI should detect this scenario and strongly suggest disabling auto-regeneration. The library's `Parallel.ForEach` in the rasterizer helps, but encoding and packing remain single-threaded bottlenecks. See also **Phase 55** for planned parallelism improvements.

6. **Texture2D on main thread** — MonoGame requires `Texture2D` creation and `SetData()` calls on the thread that owns the `GraphicsDevice`. The background generation task must return raw `byte[]` pixel data, and `Game.Update()` must create the `Texture2D` instances on the main thread. This is a MonoGame constraint, not a KernSmith library issue.

---

## Success Criteria

1. Atlas textures display correctly with zoom (25%-800%), pan, and checkered transparency background using MonoGame SpriteBatch rendering.
2. Hovering over any glyph shows a tooltip with complete metrics (character, codepoint, position, dimensions, offsets, advance).
3. Clicking a glyph shows detailed metrics with visual measurement overlays drawn via MonoGame.Extended shapes.
4. Sample text renders pixel-accurately using the generated font data, with kerning applied, via atlas compositing through SpriteBatch.Draw() source rectangles.
5. Kerning pairs are browsable, filterable, and visually previewable in GUM-based panels.
6. Side-by-side comparison mode allows comparing two generations with synchronized navigation.
7. Overlays (bounding boxes, grid, channel isolation via HLSL effects, metrics) toggle on/off without lag.
8. Auto-regeneration provides near-live feedback (< 2s for typical ASCII character sets) with background Task.Run() generation.
9. The UI remains responsive (60fps MonoGame game loop) even with large atlas textures.
10. All preview interactions work with both single-page and multi-page atlas results.

---

## Related Plans

- **phase-30-wasm-rasterization.md** — server-side vs client-side rendering decisions affect web preview
- **phase-50-layer-retention.md** — retained layers could enable per-layer preview visualization
- **phase-60-ui-mvp.md** — base MonoGame + GUM UI application structure this phase builds upon
- **phase-63-ui-atlas-texture-config.md** — atlas configuration controls that feed into the preview system
