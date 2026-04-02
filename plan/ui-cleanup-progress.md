# UI Cleanup Progress Snapshot

**Date:** 2026-04-02
**Branch:** `feature/ui-cleanup`
**Status:** In progress — paused to add Gum layout debug tooling

## What's Been Done

### Phase 1: Extract Shared UI Components (COMPLETE)
- Created `apps/KernSmith.Ui/Styling/UiFactory.cs` — shared factory with: `AddSectionHeader`, `AddCollapsibleHeader`, `AddCollapsibleSection`, `AddDivider`, `AddSliderRow`, `AddColorRow`, `CreateScrollablePanel`
- Added layout constants to `apps/KernSmith.Ui/Styling/Theme.cs` — `SectionHeaderBg`, `CollapsibleContentBg`, `SectionSpacing` (8), `PanelPadding` (8), `ControlSpacing` (4), `LabelWidth` (70), `SectionHeaderHeight` (24)
- Removed all duplicate helper methods from FontConfigPanel, EffectsPanel, CharacterSelectionPanel, FontInspectorDialog
- All panels now use `UiFactory.*` calls

### Phase 2: Collapsible Section Headers (MOSTLY COMPLETE)
- `AddCollapsibleHeader` added to UiFactory — clickable header bar with `v`/`>` ASCII chevron, content area with subtle background + indent
- `AddCollapsibleSection` (checkbox variant) updated to match — now has header bar background instead of floating checkbox
- **FontConfigPanel:** FONT FILE, SIZE, ATLAS, OUTPUT sections all collapsible
- **EffectsPanel:** FONT STYLE, ADVANCED, FALLBACK CHARACTER converted to collapsible headers. OUTLINE/SHADOW/GRADIENT/CHANNELS keep checkbox-toggle (now with header bar treatment). Variable Font section left as-is (special dynamic visibility).
- **CharacterSelectionPanel:** CHARACTER SET PRESET, ADD FROM TEXT, UNICODE BLOCKS all collapsible
- All inter-section dividers removed (headers provide their own separation)

### Generate Bar (BLOCKED — layout issue)
- Moved Generate button + Auto-regenerate out of scroll area into a fixed bottom bar
- Used ratio height for scroll area + absolute height for bottom bar in a TopToBottomStack container
- **Problem:** The bottom bar overlaps the scroll content. The ratio/stack layout isn't sizing correctly — the scroll area doesn't shrink to make room.
- Multiple approaches tried:
  1. `scrollViewer.Visual.Height = -60` relative to parent — didn't work (left panel content invisible)
  2. `this.Visual.ChildrenLayout = TopToBottomStack` — broke MainLayout's positioning of the panel
  3. Intermediate `root` ContainerRuntime with TopToBottomStack, ScrollViewer with Ratio height — bottom bar still overlaps
  4. Wrapper ContainerRuntime around ScrollViewer with ClipsChildren — same overlap
- **Root cause unclear** — need Gum layout debug dump to see actual computed positions/sizes
- **User is adding layout debug tooling to Gum** to enable diagnosing this

## What's Left To Do

### Immediate (when returning)
1. Use Gum debug dump to diagnose Generate bar overlap
2. Fix the layout so bottom bar sits below scroll area without overlap
3. Alternatively: abandon fixed bottom bar and place Generate at top of panel or end of scroll content

### Phase 3: Consistent Label:Control Grid (Left Panel)
- Font Size, Rasterizer, Packing Algorithm → grid rows with 70px label column
- Padding/Spacing cross layout → clean up with T/R/B/L labels, better alignment
- "Glyphs in font" → move inside FONT FILE section as labeled row

### Phase 4: Consistent Label:Control Grid (Right Panel)
- Font Style checkbox spacing → increase from 4 to 8
- Super Sample → labeled row

### Phase 5: Spacing and Final Polish
- Standardize spacing to 8px everywhere
- Remove remaining redundant dividers
- Fine-tune section header sizing

## Key Decisions Made
- **Padding/Spacing layout:** Keep cross/compass layout, just clean up labels and alignment
- **All sections start expanded** (user preference)
- **Chevrons:** ASCII `v`/`>` because Gum bitmap font doesn't include Unicode triangles
- **Panel reorganization** (moving controls between left/right) deferred to separate follow-up phase
- **Gum Dock.Fill** does NOT respect siblings — use Ratio height units for remaining-space layouts

## Files Modified (from clean main)
- `apps/KernSmith.Ui/Styling/Theme.cs` — added layout constants
- `apps/KernSmith.Ui/Styling/UiFactory.cs` — **new file**
- `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` — collapsible sections + generate bar (broken)
- `apps/KernSmith.Ui/Layout/EffectsPanel.cs` — uses UiFactory, collapsible headers
- `apps/KernSmith.Ui/Layout/CharacterSelectionPanel.cs` — uses UiFactory, collapsible headers
- `apps/KernSmith.Ui/Layout/FontInspectorDialog.cs` — uses UiFactory
- `plan/phase-200-ui-cleanup.md` — full plan doc
