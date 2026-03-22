# Phase 70 — UI Manual Review & Bug Fixes

> **Status**: In Progress
> **Created**: 2026-03-21
> **Goal**: Address all issues found during manual testing of the Phase 60-69 UI implementation.

---

## Issues Found

### Layout & Sizing

| # | Issue | Severity | Details |
|---|-------|----------|---------|
| 1 | Engine preset buttons overflow scroll area | High | The top row of engine preset buttons (Unity, Godot, MonoGame, Unreal, Phaser) extends outside the ScrollViewer — "Unreal" is clipped/hidden. The entire left panel content should be inside the scroll area. |
| 2 | Font style checkboxes don't line up | Medium | Bold/Italic and Anti-Alias/Hinting checkboxes are misaligned. The horizontal StackPanel rows have inconsistent widths. "Hinting" is truncated to "Hintin" — the right panel is too narrow or text doesn't fit. |
| 5 | Scroll bar hides UI content | High | The vertical scrollbar in the left panel overlaps/covers some controls. Content width needs to account for scrollbar width. |
| 6 | Input TextBoxes too wide for small values | Medium | Padding and spacing inputs (1-2 digit values) use oversized TextBoxes. Should be ~40px wide, not 80-100px. |
| 11 | Zoom controls overlap Preview/Characters tabs | High | The zoom slider and atlas info text overlap with the Preview/Characters tab buttons. These need to be in separate rows or the tabs need to be above the zoom bar. |
| 12 | Unicode blocks display is broken | High | The character selection panel's Unicode block checkboxes have overlapping text, making them unreadable. Block names and counts run into each other. |

### Text & Labels

| # | Issue | Severity | Details |
|---|-------|----------|---------|
| 3 | SDF label is too verbose | Low | "SDF (Signed Distance Field)" — just "SDF" is sufficient. Everyone who needs SDF knows what it means. |
| 4 | Font size default too large | Low | Default font size appears visually large on screen. The 32pt default may be fine for generation but the label styling in GUM makes section headers look oversized. |
| 10 | Sample text hidden behind glyphs | Medium | The "Sample Text" input area at the bottom of the preview is obscured by the atlas image when the atlas is large. Needs proper layout separation. |

### Functionality

| # | Issue | Severity | Details |
|---|-------|----------|---------|
| 7 | "Regular" should be auto-selected | Medium | When selecting a system font family, the Style dropdown should auto-select "Regular" (or the first available style) and load the font immediately, rather than requiring two separate selections. |
| 8 | Generation takes 2 seconds | Low | Font generation feels slow. May be due to the background thread marshaling overhead or the debounce mechanism. Profile and optimize. |
| 9 | Second generation fails | Critical | Generating a font works the first time but fails on subsequent attempts. Likely a disposed Texture2D, stale state, or thread marshaling issue. Must fix — core workflow is broken. |

---

## Priority Order

1. **Critical**: #9 — Second generation fails (core workflow broken)
2. **High**: #1, #5, #11, #12 — Layout overflow/overlap issues
3. **Medium**: #2, #6, #7, #10 — Alignment, sizing, UX defaults
4. **Low**: #3, #4, #8 — Label text, defaults, performance
