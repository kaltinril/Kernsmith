# UI Cleanup Progress Snapshot

**Date:** 2026-04-02 (original snapshot) — **corrected against the code 2026-07-28**
**Branch:** `feature/ui-cleanup` (deleted after merge) — this work **merged via PR #58 on 2026-04-05 and shipped in v0.12.3**. The first-pass commit is `6db981c` "First pass UI cleanup." (2026-04-02). Note: the stale `origin/UiCleanup` branch pointer is *not* this work — it points at the v0.12.0 release commit and does not contain `UiFactory.cs`.
**Status (as of 2026-04-02):** In progress — Phase 2 BLOCKED on the Generate-bar overlap issue (FontConfigPanel); Ratio/TopToBottomStack sizing not respecting sibling hierarchy. Paused to add Gum layout debug tooling.
**Status (2026-07-28 re-read):** The collapsible work below has since been rewritten on top of Gum's `Expander` control (see [Root cause](#root-cause-hand-rolled-collapsibles-replaced-by-gums-expander) below). The Generate-bar blocker **needs re-validation with Jeremy** — it is neither confirmed nor refuted; see [Generate Bar](#generate-bar-needs-re-validation-with-jeremy).

## Root cause: hand-rolled collapsibles replaced by Gum's `Expander`

Everything this snapshot says about ASCII chevrons, header bars, and `AddCollapsibleHeader`/`AddCollapsibleSection` describes code that **no longer exists**. During the Gum bump in 0.17.0 (commit `f5b818a`, `chore/gum-integration-migration`) the hand-rolled collapsible was replaced by Gum's built-in `Expander` Forms control, wrapped by `UiFactory.CreateExpander` (`apps/KernSmith.Ui/Styling/UiFactory.cs:19-33`). Chevron glyphs and colors now come from the `Expander` template.

## What's Been Done

### Phase 1: Extract Shared UI Components (mostly complete — scope changed)
- Created `apps/KernSmith.Ui/Styling/UiFactory.cs`. Actual shipped surface: `CreateExpander` (`:19`), `AddSectionHeader` (`:38`), `AddSliderRow` (`:49`), `AddFloatSliderRow` (`:98`), `AddFloatBoxRow` (`:145`), `CreateSmallFloatBox` (`:174`), `AddColorRow` (`:192`), `CreateScrollablePanel` (`:282`). **`AddCollapsibleHeader`, `AddCollapsibleSection`, `AddDivider`, and `CreateLabeledRow` do not exist** anywhere in `apps/`.
- `AddSectionHeader` is a bare `TextRuntime` colored `Theme.SectionHeaderText` — no container and no background.
- Added layout constants to `apps/KernSmith.Ui/Styling/Theme.cs` (`:36-54`) — `SectionHeaderText` (200,200,200), `SectionHeaderBg` (42,42,46), `CollapsibleContentBg` (37,37,38), `SectionSpacing` (8), `PanelPadding` (8), `ControlSpacing` (4), `LabelWidth` (70), `SectionHeaderHeight` (24), `ExpanderContentIndent` (10).
- Removed all duplicate helper methods from FontConfigPanel, EffectsPanel, CharacterSelectionPanel, FontInspectorDialog
- All panels now use `UiFactory.*` calls

### Phase 2: Collapsible Section Headers (delivered via `Expander`)
- ~~`AddCollapsibleHeader` added to UiFactory — clickable header bar with `v`/`>` ASCII chevron~~ — replaced by `UiFactory.CreateExpander`, which applies `Theme.ExpanderContentIndent` to the expander's `ContentContainer`.
- ~~`AddCollapsibleSection` (checkbox variant) updated to match~~ — no longer exists.
- **FontConfigPanel:** FONT FILE, SIZE, ATLAS, OUTPUT sections all collapsible
- **EffectsPanel:** every section is now a `CreateExpander` — Font Style (`EffectsPanel.cs:58`), Effects (`:321`, with OUTLINE/SHADOW/GRADIENT/CHANNELS as rows inside it rather than four checkbox-toggle sections), Advanced (`:495`), Fallback Character (`:645`), and **Variable Font (`:666`, collapsed by default)** — the Variable Font section was *not* left as-is.
- **CharacterSelectionPanel:** CHARACTER SET PRESET, ADD FROM TEXT, UNICODE BLOCKS all collapsible
- All inter-section dividers removed (headers provide their own separation)

### Generate Bar (NEEDS RE-VALIDATION WITH JEREMY)
- Moved Generate button + Auto-regenerate out of scroll area into a fixed bottom bar
- Used ratio height for scroll area + absolute height for bottom bar in a TopToBottomStack container
- **Problem as reported (2026-04-02):** The bottom bar overlaps the scroll content. The ratio/stack layout isn't sizing correctly — the scroll area doesn't shrink to make room.
- Approaches tried at the time:
  1. `scrollViewer.Visual.Height = -60` relative to parent — didn't work (left panel content invisible)
  2. `this.Visual.ChildrenLayout = TopToBottomStack` — broke MainLayout's positioning of the panel
  3. Intermediate `root` ContainerRuntime with TopToBottomStack, ScrollViewer with Ratio height — bottom bar still overlaps
  4. Wrapper ContainerRuntime around ScrollViewer with ClipsChildren — same overlap
- **Hypothesis at the time:** Ratio/TopToBottomStack sizing not respecting sibling hierarchy — the scroll area doesn't shrink to leave room for the fixed bottom bar.
- **2026-07-28 status:** Cannot be confirmed or refuted by reading the source, and **none of the four approaches above match the current code**. Today the shape is `FontConfigPanel.cs:50` `scrollArea.HeightUnits = DimensionUnitType.Ratio` plus `BuildGenerateBar` (`:318-327`) sizing `RelativeToChildren`. Do **not** treat this as fixed, and do not treat it as still blocked — run the app and confirm the observed behavior first.

## What's Left To Do

### Immediate (when returning)
1. Run the UI and confirm whether the Generate-bar overlap still reproduces (see above)
2. If it does: use a Gum layout debug dump to diagnose, then fix the layout so the bottom bar sits below the scroll area without overlap
3. Alternatively: abandon fixed bottom bar and place Generate at top of panel or end of scroll content

### Phase 3: Consistent Label:Control Grid (Left Panel)
- ~~Font Size, Rasterizer, Packing Algorithm → grid rows with 70px label column~~ — **done**: `FontConfigPanel.cs:280`, `:293`, `:474`
- Padding/Spacing cross layout → clean up with T/R/B/L labels, better alignment — still open
- ~~"Glyphs in font" → move inside FONT FILE section as labeled row~~ — **done**: `FontConfigPanel.cs:239` `fontGrid.AddRow("Glyphs:", glyphCount)`

### Phase 4: Consistent Label:Control Grid (Right Panel)
- Font Style checkbox spacing → increase from 4 to 8 — still open
- ~~Super Sample → labeled row~~ — **done**: `EffectsPanel.cs:315` `ssGrid.AddRow("Super Sample:", ssCombo)`

### Phase 5: Spacing and Final Polish
- Standardize spacing to 8px everywhere
- Remove remaining redundant dividers
- Fine-tune section header sizing

### Phase 6: Visual Refinement (egui-inspired)
- Reduce font size for denser, more professional feel
- Tone down section header colors — muted gray/white instead of bright blue accent
- Reduce or remove section header background bars (less visual weight)
- Mute checkbox/radio accent colors
- Reduce overall contrast — fewer distinct background shades
- Goal: quiet, professional UI that lets content speak (reference: egui default style)

### Phase 7: MVVM Binding Refactor
- Replace imperative `PropertyChanged` event wiring and manual visibility toggling with Gum's MVVM binding system
- ViewModels should inherit from Gum's `ViewModel` base class (using `Get<T>()`/`Set(value)` pattern)
- Add computed properties with `[DependsOn]` (e.g., `IsBrowseMode` / `IsSystemFontMode`)
- Use `SetBinding` + `BindingContext` propagation instead of manual event handlers
- `IsVisible` is bindable on all Forms controls — use it for mode-switching visibility
- Start with FontConfigPanel, then apply pattern to EffectsPanel and other panels
- Reference: https://docs.flatredball.com/gum/code/binding-viewmodels

## Key Decisions Made
- **Padding/Spacing layout:** Keep cross/compass layout, just clean up labels and alignment
- **All sections start expanded** (user preference) — except Variable Font, which now starts collapsed (`EffectsPanel.cs:666`)
- ~~**Chevrons:** ASCII `v`/`>` because Gum bitmap font doesn't include Unicode triangles~~ — obsolete: the hand-rolled chevron is gone; Gum's `Expander` template supplies the indicator
- **Panel reorganization** (moving controls between left/right) deferred to separate follow-up phase
- **Gum Dock.Fill** does NOT respect siblings — use Ratio height units for remaining-space layouts

## Files Modified (from clean main)
- `apps/KernSmith.Ui/Styling/Theme.cs` — added layout constants
- `apps/KernSmith.Ui/Styling/UiFactory.cs` — **new file**
- `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` — collapsible sections + generate bar (overlap status needs re-validation, see above)
- `apps/KernSmith.Ui/Layout/EffectsPanel.cs` — uses UiFactory, collapsible headers
- `apps/KernSmith.Ui/Layout/CharacterSelectionPanel.cs` — uses UiFactory, collapsible headers
- `apps/KernSmith.Ui/Layout/FontInspectorDialog.cs` — uses UiFactory
- `plan/phase-250-ui-cleanup.md` — full plan doc (this file previously pointed at `phase-200-ui-cleanup.md`, which is a different phase — FontCrafter)
