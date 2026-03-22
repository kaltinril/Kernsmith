# Phase 72 — UI Issues Round 2

> **Status**: In Progress
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
| 5 | Without anti-alias fonts look broken (core library issue?) | Bug |
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

---

## Progress

| # | Issue | Status | Notes |
|---|-------|--------|-------|
| 16 | Keyboard shortcuts dialog alignment + transparency | **Fixed** | Widened key column 120→140px; added opaque `ColoredRectangleRuntime` backdrop with `Theme.Panel` |
| 17 | Double-click splitter resets panel to default size | **Fixed** | Hooked `InteractiveGue.DoubleClick` on both splitters → resets to 280px |
| 18 | UI scaling / accessibility zoom | **Fixed** | Ctrl++/- scales UI 50%-200% via `Camera.Zoom`; Ctrl+0 resets; scroll wheel still zooms atlas preview |

---

## Priority

1. **Critical**: #8 — File save/export broken
2. **Bug**: #5, #11, #14 — Core functionality issues
3. **Layout**: #1, #4, #15 — Visual alignment
4. **UX**: #2, #3, #7, #9, #10, #13 — Usability improvements
5. **Feature gap**: #6, #12 — Stubs that need implementation or removal
