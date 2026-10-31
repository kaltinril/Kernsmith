# Phase 250: UI Cleanup & Polish

## Status At A Glance

> Where the plan below and the shipped code disagree, the code wins and the plan text has been corrected. The first pass merged in PR #58; the original progress snapshot is archived as [done/ui-cleanup-progress.md](done/ui-cleanup-progress.md).

- **Phase 1 (UiFactory extraction + Theme constants)** — **MOSTLY COMPLETE, scope changed**. `apps/KernSmith.Ui/Styling/UiFactory.cs` and the `Theme` layout constants both exist, but the planned collapsible/divider/labeled-row helpers were never built as specified: `AddCollapsibleSection`, `AddCollapsibleHeader`, `AddDivider`, and `CreateLabeledRow` do not exist anywhere in `apps/`. Collapsing is now handled by Gum's built-in `Expander` Forms control via `UiFactory.CreateExpander` — see [Root cause](#root-cause-hand-rolled-collapsibles-replaced-by-gums-expander).
- **Phase 2 (collapsible section headers)** — **DELIVERED via `Expander`; the Generate-bar layout issue NEEDS RE-VALIDATION WITH JEREMY**. Collapsible sections exist in FontConfigPanel, EffectsPanel, and CharacterSelectionPanel. The originally-reported Generate-bar overlap cannot be confirmed or refuted by reading the code: none of the four failed approaches (listed under [Phase 2](#fontconfigpanelcs)) match the current shape, which is `FontConfigPanel.cs:51` `scrollArea.HeightUnits = DimensionUnitType.Ratio` plus `BuildGenerateBar` (`:352`) using `RelativeToChildren`. Someone needs to run the app and report whether the overlap still happens before this is called fixed or still blocked.
- **Phases 3-5 (grid layout consistency, spacing/polish)** — **largely done**. Phase 3's grid rows all exist (`FontConfigPanel.cs:302` `sizeGrid.AddRow("Size (pt):", …)`, `:326` `AddRow("Rasterizer:", …)`, `:512` `AddRow("Packing:", …)`, and `:267` `fontGrid.AddRow("Glyphs:", glyphCount)` — the "Glyphs in font" item). Phase 4 is done: the Super Sample labeled row exists at `EffectsPanel.cs:354`, and the Font Style checkbox columns stack at 8. Remaining: the padding/spacing cross-layout cleanup and the Phase 5 spacing sweep.
- **Phase 6 (egui-inspired visual polish)** — not started.
- **Phase 7 (MVVM binding)** — partly there: every view model already derives from Gum's `ViewModel`, and the `UiFactory` row helpers bind each control to a view-model property; the rest of the imperative `PropertyChanged` wiring and visibility toggling remains.

### Root cause: hand-rolled collapsibles replaced by Gum's `Expander`

The original plan described a hand-rolled collapsible header — a clickable bar with an ASCII `v`/`>` chevron, a header background, and an indented content area. That code no longer exists. During the Gum bump in 0.17.0 (commit `f5b818a`, `chore/gum-integration-migration`) it was replaced by Gum's built-in `Expander` Forms control, wrapped by `UiFactory.CreateExpander` (`UiFactory.cs:22-36`), which applies `Theme.ExpanderContentIndent` to the expander's `ContentContainer`. Chevron glyphs, chevron colors, and header-bar backgrounds are now the `Expander` template's business, not ours.

## Context

The KernSmith UI (Gum/MonoGame) works but looks cramped and inconsistent compared to professional tools like Rerun.io and egui. This phase brings visual polish: collapsible sections, consistent label:control grids, generous spacing, clean visual hierarchy. Panel reorganization (moving controls between left/right) is deferred to a follow-up phase.

## Phase 1: Extract Shared UI Components (Foundation)

**No visual changes — pure refactor to eliminate duplication and establish shared primitives.**

### New file: `apps/KernSmith.Ui/Styling/UiFactory.cs`

Extract duplicated helpers from FontConfigPanel, EffectsPanel, CharacterSelectionPanel, FontInspectorDialog:

Actual public surface as shipped (`UiFactory.cs`):

| Method | Line | Purpose |
|--------|------|---------|
| `CreateExpander(header, isExpanded = true)` | `:22` | Gum `Expander` with `Theme.ExpanderContentIndent` applied to its `ContentContainer` — this is what replaced the planned collapsible helpers |
| `AddSectionHeader(parent, text)` | `:41` | Bare `TextRuntime` colored `Theme.SectionHeaderText`. No container, no background, not accent-colored (the plan's "24px container + bg + accent text" was never built) |
| `AddSliderRow(parent, label, min, max, ...)` | `:56` | Grid: label \| slider \| value (int) |
| `AddFloatSliderRow(...)` | `:124` | Grid: label \| slider \| value (float) |
| `AddFloatBoxRow(...)` | `:190` | Grid: label \| float text box |
| `CreateSmallFloatBox(source, propertyName, getValue, setValue)` | `:225` | Compact float text box for cross/compass layouts |
| `BindCheckBox(checkBox, source, propertyName, getValue, setValue)` | `:260` | Shows a view-model bool and writes the user's clicks back |
| `AddColorRow(device, parent, label, ...)` | `:291` | Grid: label \| swatch \| hex input |
| `CreateScrollablePanel(panel)` | `:404` | ScrollViewer + inner stack + padding pattern |

**Planned but never built** (do not cite these as existing): `AddCollapsibleSection`, `AddCollapsibleHeader`, `AddDivider`, `CreateLabeledRow`.

### Expand: `apps/KernSmith.Ui/Styling/Theme.cs`

Constants as shipped (`Theme.cs:36-54` — the two colors landed darker than planned):

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

> **NEEDS RE-VALIDATION WITH JEREMY**: the Generate-bar overlap that blocked this phase cannot be confirmed or refuted from the source. The current layout is `FontConfigPanel.cs:51` `scrollArea.HeightUnits = DimensionUnitType.Ratio` with `BuildGenerateBar` (`:352-406`) sizing `RelativeToChildren`. Run the app and report what you see before marking this fixed or still blocked.
>
> As originally reported, the fixed bottom bar (Generate + Auto-regenerate, outside the scroll area) overlapped the scroll content because the scroll area did not shrink to make room. Approaches tried then, none of which is the current code: (1) `scrollViewer.Visual.Height = -60` relative to the parent — the left panel's content disappeared; (2) `this.Visual.ChildrenLayout = TopToBottomStack` — broke `MainLayout`'s positioning of the panel; (3) an intermediate `root` `ContainerRuntime` with `TopToBottomStack` and a `Ratio`-height `ScrollViewer` — still overlapped; (4) a wrapper `ContainerRuntime` with `ClipsChildren` around the `ScrollViewer` — same overlap. Gum's `Dock.Fill` does not respect siblings; use `Ratio` height units for remaining-space layouts. If the overlap still reproduces, diagnose with a Gum layout debug dump, or drop the fixed bar and put Generate at the top of the panel or the end of the scroll content.

### EffectsPanel.cs

As shipped, every section is a `UiFactory.CreateExpander(...)`:

- **Font Style** — `EffectsPanel.cs:58`, expanded
- **Effects** — `:541`. OUTLINE/SHADOW/GRADIENT/CHANNELS did *not* stay as four separate checkbox-toggle sections with chevrons; they are now rows inside this one expander (e.g. the outline checkbox at `:562-564`)
- **Advanced** — `:826`, expanded
- **Fallback Character** — `:1033`, expanded
- **Variable Font** — `:1055`, `isExpanded: false` (it was *not* left as-is; it became an expander like the rest)

### CharacterSelectionPanel.cs

- CHARACTER SET PRESET, ADD FROM TEXT, UNICODE BLOCKS — all collapsible, start expanded

---

## Phase 3: Consistent Label:Control Grid (Left Panel)

**Replace ad-hoc layouts with uniform grid rows in FontConfigPanel.**

| Current | Change | Status |
|---------|--------|--------|
| Font Size: stacked label + text box | Grid row: `[Font Size:  ] [42] [pt]` | Done — `FontConfigPanel.cs:302` `sizeGrid.AddRow("Size (pt):", sizeTextBox)` |
| Rasterizer: stacked label + combo | Grid row: `[Rasterizer: ] [ComboBox]` | Done — `:326` `AddRow("Rasterizer:", _rasterizerCombo)` |
| Packing Algorithm: stacked | Grid row: `[Algorithm:  ] [ComboBox]` | Done — `:512` `atlasGrid.AddRow("Packing:", packAlgoCombo)` |
| Padding/Spacing: confusing 5-box cross layout | Keep cross layout but clean up: add T/R/B/L labels, improve alignment, better visual grouping | Not done |
| "Glyphs in font: 0" floating | Move inside FONT FILE section as labeled row | Done — `:267` `fontGrid.AddRow("Glyphs:", glyphCount)` |

---

## Phase 4: Consistent Label:Control Grid (Right Panel)

**Standardize EffectsPanel rows.**

- Font Style checkboxes: spacing 8 in the 2-column layout — **done** (`leftCol`/`rightCol` `StackSpacing = 8` in the Font Style expander)
- Super Sample: labeled row — **done**, `EffectsPanel.cs:354` `ssGrid.AddRow("Super Sample:", ssCombo)`
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

## Phase 6: Visual Refinement (egui-inspired)

- Reduce font size for a denser, more professional feel
- Tone down section header colors (muted gray/white rather than a bright accent)
- Reduce or remove section header background bars
- Mute checkbox/radio accent colors
- Reduce overall contrast: fewer distinct background shades
- Goal: a quiet, professional UI that lets content speak (reference: egui's default style)

## Phase 7: MVVM Binding

Every view model already derives from Gum's `ViewModel` (`Get<T>()`/`Set(value)`), and the `UiFactory` row helpers and `BindCheckBox` bind each control to a view-model property: they show it now and on `PropertyChanged`, and write user edits back. What remains:

- Move the rest of the imperative `PropertyChanged` wiring and manual visibility toggling to `SetBinding` + `BindingContext` propagation
- Computed properties with `[DependsOn]` (e.g. `IsBrowseMode` / `IsSystemFontMode`); `IsVisible` is bindable on every Forms control, so mode-switching visibility can bind too
- Start with FontConfigPanel, then EffectsPanel and the other panels
- Reference: https://docs.flatredball.com/gum/code/binding-viewmodels

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
