# Phase 72 — UI Issues Round 2

> **Status**: Complete
> **Created**: 2026-03-22
> **Goal**: Fix remaining UI issues found during manual testing.

---

## Issues

| # | Issue | Category |
|---|-------|----------|
| 1 | Right panel (effects) needs centering/padding like left panel | Layout |
| 2 | Color inputs should be HEX or RGB with optional color picker popup | UX |
| 3 | Everything needs tooltips — e.g., "what is hinting?" | UX |
| 4 | Window resize should adjust left/right panels and status bar | Layout |
| 5 | Without anti-alias fonts look broken — monochrome 1bpp not unpacked to 8bpp grayscale | Bug |
| 6 | Sample text label/textbox doesn't do anything visible | Feature gap |
| 7 | Zoom slider range is unbalanced — too little on one side, too much on other | UX |
| 8 | File menu save/export options don't work — can't save .fnt/.png/.bmfc | Critical |
| 9 | SDF should auto-disable when incompatible options are selected | UX |
| 10 | Color font and gradient are mutually exclusive — need validation | UX |
| 11 | Color font checkbox doesn't seem to do anything | Bug |
| 12 | View > Reset Layout is a stub | Feature gap |
| 13 | Character set shows up twice (left panel + Characters tab) — redundant | UX |
| 14 | Atlas size setting seems ignored — output was 256x256 despite 1024x1024 | Bug |
| 15 | Font Size textbox is too wide — only needs room for 3 digits | Layout |
| 16 | Keyboard shortcuts dialog: columns misaligned + transparent background | Bug |
| 17 | Double-click panel splitter should reset panel to default size | UX |
| 18 | UI scaling / accessibility zoom (Ctrl+=/- to scale entire UI for vision accessibility) | Accessibility |
| 19 | MaxRects packer fills top-to-bottom instead of left-to-right — missing BSSF secondary sort | Bug |

---

## Progress

| # | Issue | Status | Notes |
|---|-------|--------|-------|
| 16 | Keyboard shortcuts dialog alignment + transparency | **Fixed** | Widened key column 120→140px; added opaque `ColoredRectangleRuntime` backdrop with `Theme.Panel` |
| 17 | Double-click splitter resets panel to default size | **Fixed** | Hooked `InteractiveGue.DoubleClick` on both splitters → resets to 280px |
| 18 | UI scaling / accessibility zoom | **Fixed** | Ctrl++/- scales UI 50%-200% via `Camera.Zoom`; Ctrl+0 resets; scroll wheel still zooms atlas preview |
| 1 | Right panel (effects) needs centering/padding like left panel | **Fixed** | Added `Y = 4` top padding and matched `StackSpacing = 6` to FontConfigPanel; also added `Y = 4` to FontConfigPanel for consistency |
| 4 | Window resize should adjust left/right panels and status bar | **Fixed** | Layout already uses `RelativeToParent` dims + `Ratio` center; added `ClipsChildren = true` on body StackPanel to prevent overflow on small windows |
| 15 | Font Size textbox is too wide | **Fixed** | Reduced `sizeTextBox.Width` from 50 to 42px — fits 3 digits comfortably |
| 7 | Zoom slider range is unbalanced | **Fixed** | Changed max from 300 to 400 for balanced 25%-400% range around 100%; widened slider to 120px |
| 9 | SDF should auto-disable when incompatible options selected | **Fixed** | SDF auto-unchecks when outline/shadow/gradient/super-sampling are active; shows warning with reason |
| 10 | Color font and gradient are mutually exclusive | **Fixed** | Mutual exclusion: enabling one auto-disables the other with warning message |
| 13 | Character set shows up twice — redundant | **Fixed** | Removed dead `SelectedPreset`/`CustomCharacters`/`GetCharacterSet`/`UpdateCharacterCount` from `FontConfigViewModel`; characters tab is the single source |
| 11 | Color font checkbox doesn't seem to do anything | **Fixed** | Feature is fully implemented; added warning when enabled on a font without color tables so users understand why nothing changes |
| 14 | Atlas size setting seems ignored — output was 256x256 despite 1024x1024 | **Fixed** | Working as designed — Autofit shrinks atlas to fit. Improved tooltips on Max Size and Autofit to explain the relationship |
| 20 | Sample text preview — background not transparent, bottom pixels clipped | **Fixed** | Rewrote to per-glyph sprites with padding-aware texture coords; converted grayscale atlas to premultiplied alpha for transparency |
| 21 | Atlas preview rendering slightly degraded vs saved PNG | **Moved** | Moved to Phase 80 — cosmetic preview issue, saved PNG is correct |

---

## Root Cause Analysis

### Issue #5 — Anti-alias off produces garbled glyphs

**Root cause**: `FreeTypeRasterizer.RasterizeGlyph()` does not unpack monochrome (1bpp) bitmap data.

When anti-alias is OFF, FreeType renders with `FT_RENDER_MODE_MONO` → `FT_PIXEL_MODE_MONO`, which packs 8 pixels per byte (MSB-first). The rasterizer copies the raw bytes and labels them `PixelFormat.Grayscale8` (lines 267-269), so every downstream consumer interprets each byte as one pixel instead of 8. This causes 8x horizontal compression with garbage intensity values — the garbled vertical stripes seen in the UI.

**Fix**: In `FreeTypeRasterizer.cs` after rendering, detect `FT_PIXEL_MODE_MONO` and unpack 1bpp → 8bpp (255 for set bits, 0 for unset). The unpacked buffer is then genuinely `Grayscale8`. No downstream changes needed.

**File**: [FreeTypeRasterizer.cs](src/KernSmith/Rasterizer/FreeTypeRasterizer.cs) lines 242-281

### Issue #19 — MaxRects packer fills top-to-bottom

**Root cause**: The BSSF scoring in `MaxRectsPacker.TryPlace()` uses only `Math.Min(leftoverX, leftoverY)` without a secondary sort on `Math.Max(leftoverX, leftoverY)`.

After placing the first glyph at (0,0) in a 256x256 atlas, the split produces a tall-narrow right strip and a short-wide bottom strip. For typical glyph sizes, the bottom strip consistently wins on short-side score because its height leftover is slightly smaller. Without the long-side tiebreaker, every subsequent glyph stacks below the previous one.

**Fix**: Add `bestLongSide` tracking and compare `(shortSide < bestShortSide || (shortSide == bestShortSide && longSide < bestLongSide))`. This matches the standard MaxRects BSSF algorithm from the Jukka Jylänki paper.

**File**: [MaxRectsPacker.cs](src/KernSmith/Atlas/MaxRectsPacker.cs) lines 63-80

---

## Priority

1. **Critical**: #8 — File save/export broken
2. **Bug**: #5, #11, #14, #19 — Core functionality issues
3. **Layout**: #1, #4, #15 — Visual alignment
4. **UX**: #2, #3, #7, #9, #10, #13 — Usability improvements
5. **Feature gap**: #6, #12 — Stubs that need implementation or removal
