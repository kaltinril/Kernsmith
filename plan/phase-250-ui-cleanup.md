# Phase 250: UI Cleanup & Polish

> **Live progress snapshot**: see [`ui-cleanup-progress.md`](ui-cleanup-progress.md) for the dated, day-to-day status of this work.

## Status At A Glance

> Verified against the code on 2026-07-28. Where the plan below and the shipped code disagree, the code wins and the plan text has been corrected.

- **Phase 1 (UiFactory extraction + Theme constants)** — **MOSTLY COMPLETE, scope changed**. `apps/KernSmith.Ui/Styling/UiFactory.cs` and the `Theme` layout constants both exist, but the planned collapsible/divider/labeled-row helpers were never built as specified: `AddCollapsibleSection`, `AddCollapsibleHeader`, `AddDivider`, and `CreateLabeledRow` do not exist anywhere in `apps/`. Collapsing is now handled by Gum's built-in `Expander` Forms control via `UiFactory.CreateExpander` — see [Root cause](#root-cause-hand-rolled-collapsibles-replaced-by-gums-expander).
- **Phase 2 (collapsible section headers)** — **DELIVERED via `Expander`; the Generate-bar layout issue NEEDS RE-VALIDATION WITH JEREMY**. Collapsible sections exist in FontConfigPanel, EffectsPanel, and CharacterSelectionPanel. The originally-reported Generate-bar overlap cannot be confirmed or refuted by reading the code: none of the four failed approaches recorded in `ui-cleanup-progress.md` match the current shape, which is `FontConfigPanel.cs:50` `scrollArea.HeightUnits = DimensionUnitType.Ratio` plus `BuildGenerateBar` (`:318`) using `RelativeToChildren`. Someone needs to run the app and report whether the overlap still happens before this is called fixed or still blocked.
- **Phases 3-5 (grid layout consistency, spacing/polish)** — **largely done**. Phase 3's grid rows all exist (`FontConfigPanel.cs:280` `sizeGrid.AddRow("Size (pt):", …)`, `:293` `AddRow("Rasterizer:", …)`, `:474` `AddRow("Packing:", …)`, and `:239` `fontGrid.AddRow("Glyphs:", glyphCount)` — the "Glyphs in font" item). Phase 4's Super Sample labeled row exists at `EffectsPanel.cs:315`. Remaining: the padding/spacing cross-layout cleanup and the Phase 5 spacing sweep.
- **Phase 6 (egui-inspired visual polish)** — not started.

### Root cause: hand-rolled collapsibles replaced by Gum's `Expander`

The original plan (and the progress snapshot) described a hand-rolled collapsible header — a clickable bar with an ASCII `v`/`>` chevron, a header background, and an indented content area. That code no longer exists. During the Gum bump in 0.17.0 (commit `f5b818a`, `chore/gum-integration-migration`) it was replaced by Gum's built-in `Expander` Forms control, wrapped by `UiFactory.CreateExpander` (`UiFactory.cs:19-33`), which applies `Theme.ExpanderContentIndent` to the expander's `ContentContainer`. Chevron glyphs, chevron colors, and header-bar backgrounds are now the `Expander` template's business, not ours.

## Context

The KernSmith UI (Gum/MonoGame) works but looks cramped and inconsistent compared to professional tools like Rerun.io and egui. This phase brings visual polish: collapsible sections, consistent label:control grids, generous spacing, clean visual hierarchy. Panel reorganization (moving controls between left/right) is deferred to a follow-up phase.

## Phase 1: Extract Shared UI Components (Foundation)

**No visual changes — pure refactor to eliminate duplication and establish shared primitives.**

### New file: `apps/KernSmith.Ui/Styling/UiFactory.cs`

Extract duplicated helpers from FontConfigPanel, EffectsPanel, CharacterSelectionPanel, FontInspectorDialog:

Actual public surface as shipped (`UiFactory.cs`, verified 2026-07-28):

| Method | Line | Purpose |
|--------|------|---------|
| `CreateExpander(header, isExpanded = true)` | `:19` | Gum `Expander` with `Theme.ExpanderContentIndent` applied to its `ContentContainer` — this is what replaced the planned collapsible helpers |
| `AddSectionHeader(parent, text)` | `:38` | Bare `TextRuntime` colored `Theme.SectionHeaderText`. No container, no background, not accent-colored (the plan's "24px container + bg + accent text" was never built) |
| `AddSliderRow(parent, label, min, max, ...)` | `:49` | Grid: label \| slider \| value (int) |
| `AddFloatSliderRow(...)` | `:98` | Grid: label \| slider \| value (float) |
| `AddFloatBoxRow(...)` | `:145` | Grid: label \| float text box |
| `CreateSmallFloatBox(initialValue, onChanged)` | `:174` | Compact float text box for cross/compass layouts |
| `AddColorRow(device, parent, label, ...)` | `:192` | Grid: label \| swatch \| hex input |
| `CreateScrollablePanel(panel)` | `:282` | ScrollViewer + inner stack + padding pattern |

**Planned but never built** (do not cite these as existing): `AddCollapsibleSection`, `AddCollapsibleHeader`, `AddDivider`, `CreateLabeledRow`.

### Expand: `apps/KernSmith.Ui/Styling/Theme.cs`

Constants as shipped (`Theme.cs:36-54`, verified 2026-07-28 — the two colors landed darker than planned):

- `SectionHeaderText` = `new Color(200, 200, 200)` (`:38`) — added during implementation, not in the original plan
- `SectionHeaderBg` = `new Color(42, 42, 46)` (`:40`) — plan said `(50, 50, 55)`
- `CollapsibleContentBg` = `new Color(37, 37, 38)` (`:42`) — plan said `(40, 40, 44)`
- `SectionSpacing` = `8` (`:44`)
- `PanelPadding` = `8` (`:46`)
- `ControlSpacing` = `4` (`:48`)
- `LabelWidth` = `70` (`:50`)
- `SectionHeaderHeight` = `24` (`:52`)
- `ExpanderContentIndent` = `10` (`:54`) — added for `CreateExpander`, not in the original plan

### Mechanical changes in existing files

Remove local `AddSectionHeader`, `AddDivider`, `AddLabeledDivider`, `AddCollapsibleSection`, `AddSliderRow`, `AddColorRow` from:
- `FontConfigPanel.cs`
- `EffectsPanel.cs`
- `CharacterSelectionPanel.cs`
- `FontInspectorDialog.cs`

Replace all calls with `UiFactory.*` equivalents. Replace hardcoded spacing/padding values with Theme constants.

---

## Phase 2: Collapsible Section Headers

**All section headers become expandable/collapsible with chevron indicators (matching Rerun.io).** Delivered with Gum's `Expander` control rather than the hand-rolled header originally specced — see [Root cause](#root-cause-hand-rolled-collapsibles-replaced-by-gums-expander).

### FontConfigPanel.cs

Convert sections to collapsible (all start expanded):
- **FONT FILE** — collapsible
- **SIZE** — collapsible
- **ATLAS** — collapsible
- **OUTPUT** — collapsible
- **Generate button + Auto-regenerate** — stay *outside* any section (always visible)

> **NEEDS RE-VALIDATION WITH JEREMY**: the Generate-bar overlap that blocked this phase cannot be confirmed or refuted from the source. The current layout is `FontConfigPanel.cs:50` `scrollArea.HeightUnits = DimensionUnitType.Ratio` with `BuildGenerateBar` (`:318-327`) sizing `RelativeToChildren` — none of the four approaches recorded as failed in `ui-cleanup-progress.md` are what's in the file today. Run the app and report what you see before marking this fixed or still blocked.

### EffectsPanel.cs

As shipped, every section is a `UiFactory.CreateExpander(...)`:

- **Font Style** — `EffectsPanel.cs:58`, expanded
- **Effects** — `:321`. OUTLINE/SHADOW/GRADIENT/CHANNELS did *not* stay as four separate checkbox-toggle sections with chevrons; they are now rows inside this one expander (e.g. the outline checkbox at `:342-344`)
- **Advanced** — `:495`, expanded
- **Fallback Character** — `:645`, expanded
- **Variable Font** — `:666`, `isExpanded: false` (it was *not* left as-is; it became an expander like the rest)

### CharacterSelectionPanel.cs

- CHARACTER SET PRESET, ADD FROM TEXT, UNICODE BLOCKS — all collapsible, start expanded

---

## Phase 3: Consistent Label:Control Grid (Left Panel)

**Replace ad-hoc layouts with uniform grid rows in FontConfigPanel.**

| Current | Change | Status |
|---------|--------|--------|
| Font Size: stacked label + text box | Grid row: `[Font Size:  ] [42] [pt]` | Done — `FontConfigPanel.cs:280` `sizeGrid.AddRow("Size (pt):", sizeTextBox)` |
| Rasterizer: stacked label + combo | Grid row: `[Rasterizer: ] [ComboBox]` | Done — `:293` `AddRow("Rasterizer:", _rasterizerCombo)` |
| Packing Algorithm: stacked | Grid row: `[Algorithm:  ] [ComboBox]` | Done — `:474` `atlasGrid.AddRow("Packing:", packAlgoCombo)` |
| Padding/Spacing: confusing 5-box cross layout | Keep cross layout but clean up: add T/R/B/L labels, improve alignment, better visual grouping | Not done |
| "Glyphs in font: 0" floating | Move inside FONT FILE section as labeled row | Done — `:239` `fontGrid.AddRow("Glyphs:", glyphCount)` |

---

## Phase 4: Consistent Label:Control Grid (Right Panel)

**Standardize EffectsPanel rows.**

- Font Style checkboxes: increase spacing from 4 to 8 in the 2-column layout
- ~~Super Sample: convert to labeled row~~ — **done**, `EffectsPanel.cs:315` `ssGrid.AddRow("Super Sample:", ssCombo)`
- All slider/color rows already use 70px label column via helpers — just route through UiFactory

---

## Phase 5: Spacing and Final Polish

- Remove redundant dividers between collapsible headers (headers provide their own separation)
- Standardize inner stack spacing to 8px everywhere
- Add 4px spacer after section headers before content
- Standardize collapsible content padding to X=8, Y=6, Width=-16
- Apply ScrollViewer background transparency fix to all panels via UiFactory
- Bump section header height from 22 to 24, improve text vertical centering
- ~~Chevron uses `Theme.TextMuted`, title uses `Theme.Accent`~~ — obsolete. There is no hand-rolled chevron any more; chevron glyph and colors come from Gum's `Expander` template (see [Root cause](#root-cause-hand-rolled-collapsibles-replaced-by-gums-expander)). Plain section-header text uses `Theme.SectionHeaderText`, not `Theme.Accent`.

---

## Implementation Strategy

One feature branch with incremental commits. All phases build on each other.

| Commit | Phase | Risk |
|--------|-------|------|
| 1 | Phase 1: Extract UiFactory + Theme constants | Low (mechanical refactor) |
| 2 | Phase 2: Collapsible sections | Medium (layout structure change) |
| 3 | Phases 3+4: Grid layout consistency | Medium (padding/spacing cleanup) |
| 4 | Phase 5: Spacing polish | Low (cosmetic tweaks) |

## Files Modified

- `apps/KernSmith.Ui/Styling/Theme.cs` — add constants
- `apps/KernSmith.Ui/Styling/UiFactory.cs` — **new file**, shared UI helpers
- `apps/KernSmith.Ui/Layout/FontConfigPanel.cs` — major layout changes
- `apps/KernSmith.Ui/Layout/EffectsPanel.cs` — extract helpers + collapsible headers
- `apps/KernSmith.Ui/Layout/CharacterSelectionPanel.cs` — extract helpers + collapsible headers
- `apps/KernSmith.Ui/Layout/FontInspectorDialog.cs` — extract helpers

## Verification

1. Build: `dotnet build apps/KernSmith.Ui/`
2. Run the app and verify:
   - All sections collapse/expand with chevron click
   - Label:control alignment is uniform across both side panels
   - Padding/spacing section is cleaner with T/R/B/L labels
   - Spacing feels consistent and spacious
   - No controls are cut off or overlapping
   - Generate button remains visible regardless of collapse state
3. Run existing tests: `dotnet test tests/KernSmith.Tests/`

## Future Follow-up

Panel reorganization (moving Font Style, Super Sample, Fallback Character from right to left panel) is a separate phase to be done after this cleanup. The restyle makes reorganization easier by establishing shared helpers and consistent patterns.

## Ideas Backlog

- **Opt-in outline advance adjustment**: Add an option (e.g. `AdjustAdvanceForOutline = true`, default `false`) that bakes `+ 2 * outlineThickness` into xadvance during .fnt generation. The BMFont spec says outline does NOT modify xadvance (renderers are expected to handle it), but some users don't control their renderer and want the adjustment baked in. Expose in UI as a checkbox near the outline controls, and in .bmfc as a key. See `plan/done/phase-98-outline-advance-bug.md` for background.
