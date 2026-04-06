# Phase 250: UI Cleanup & Polish

## Context

The KernSmith UI (Gum/MonoGame) works but looks cramped and inconsistent compared to professional tools like Rerun.io and egui. This phase brings visual polish: collapsible sections, consistent label:control grids, generous spacing, clean visual hierarchy. Panel reorganization (moving controls between left/right) is deferred to a follow-up phase.

## Phase 1: Extract Shared UI Components (Foundation)

**No visual changes — pure refactor to eliminate duplication and establish shared primitives.**

### New file: `apps/KernSmith.Ui/Styling/UiFactory.cs`

Extract duplicated helpers from FontConfigPanel, EffectsPanel, CharacterSelectionPanel, FontInspectorDialog:

| Method | Source | Purpose |
|--------|--------|---------|
| `AddSectionHeader(parent, text)` | FontConfigPanel + EffectsPanel | 24px container + bg + accent text |
| `AddCollapsibleSection(parent, title, buildContent, ...)` | EffectsPanel | Checkbox-header + togglable content |
| `AddCollapsibleHeader(parent, title, buildContent, startExpanded)` | New | Plain expand/collapse (no checkbox) |
| `AddDivider(parent)` | FontConfigPanel + EffectsPanel | 1px separator line |
| `AddSliderRow(parent, label, min, max, ...)` | EffectsPanel | Grid: label \| slider \| value |
| `AddColorRow(device, parent, label, ...)` | EffectsPanel | Grid: label \| swatch \| hex input |
| `CreateScrollablePanel(parent)` | Both panels | ScrollViewer + inner stack + padding pattern |
| `CreateLabeledRow(parent, label, labelWidth)` | New | Standardized label:control horizontal row |

### Expand: `apps/KernSmith.Ui/Styling/Theme.cs`

Add constants for currently-hardcoded values:

- `SectionHeaderBg` = `new Color(50, 50, 55)`
- `CollapsibleContentBg` = `new Color(40, 40, 44)`
- `SectionSpacing` = `8` (bump from inconsistent 4/6)
- `PanelPadding` = `8`
- `ControlSpacing` = `4`
- `LabelWidth` = `70`
- `SectionHeaderHeight` = `24` (bump from 22)

### Mechanical changes in existing files

Remove local `AddSectionHeader`, `AddDivider`, `AddLabeledDivider`, `AddCollapsibleSection`, `AddSliderRow`, `AddColorRow` from:
- `FontConfigPanel.cs`
- `EffectsPanel.cs`
- `CharacterSelectionPanel.cs`
- `FontInspectorDialog.cs`

Replace all calls with `UiFactory.*` equivalents. Replace hardcoded spacing/padding values with Theme constants.

---

## Phase 2: Collapsible Section Headers

**All section headers become expandable/collapsible with chevron indicators (matching Rerun.io).**

### FontConfigPanel.cs

Convert sections to collapsible (all start expanded):
- **FONT FILE** — collapsible
- **SIZE** — collapsible
- **ATLAS** — collapsible
- **OUTPUT** — collapsible
- **Generate button + Auto-regenerate** — stay *outside* any section (always visible)

### EffectsPanel.cs

- **FONT STYLE** — convert to plain collapsible (start expanded)
- OUTLINE/SHADOW/GRADIENT/CHANNELS — keep checkbox-toggle + add chevron
- **ADVANCED** — plain collapsible (start expanded)
- **FALLBACK CHARACTER** — plain collapsible (start expanded)

### CharacterSelectionPanel.cs

- CHARACTER SET PRESET, ADD FROM TEXT, UNICODE BLOCKS — all collapsible, start expanded

---

## Phase 3: Consistent Label:Control Grid (Left Panel)

**Replace ad-hoc layouts with uniform grid rows in FontConfigPanel.**

| Current | Change |
|---------|--------|
| Font Size: stacked label + text box | Grid row: `[Font Size:  ] [42] [pt]` |
| Rasterizer: stacked label + combo | Grid row: `[Rasterizer: ] [ComboBox]` |
| Packing Algorithm: stacked | Grid row: `[Algorithm:  ] [ComboBox]` |
| Padding/Spacing: confusing 5-box cross layout | Keep cross layout but clean up: add T/R/B/L labels, improve alignment, better visual grouping |
| "Glyphs in font: 0" floating | Move inside FONT FILE section as labeled row |

---

## Phase 4: Consistent Label:Control Grid (Right Panel)

**Standardize EffectsPanel rows.**

- Font Style checkboxes: increase spacing from 4 to 8 in the 2-column layout
- Super Sample: convert to labeled row `[Supersample:] [1x] [2x] [4x]`
- All slider/color rows already use 70px label column via helpers — just route through UiFactory

---

## Phase 5: Spacing and Final Polish

- Remove redundant dividers between collapsible headers (headers provide their own separation)
- Standardize inner stack spacing to 8px everywhere
- Add 4px spacer after section headers before content
- Standardize collapsible content padding to X=8, Y=6, Width=-16
- Apply ScrollViewer background transparency fix to all panels via UiFactory
- Bump section header height from 22 to 24, improve text vertical centering
- Chevron uses `Theme.TextMuted`, title uses `Theme.Accent`

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
