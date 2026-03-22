# Phase 71 — UI Stabilization & Visual Quality

> **Status**: In Progress
> **Created**: 2026-03-21
> **Goal**: Fix remaining bugs, improve visual quality, and prepare the UI for release. The app is feature-complete — this phase is about making it solid and presentable.

---

## Wave 1: Bug Verification & Regression Testing

Re-test all Phase 70 fixes and find any remaining issues.

| # | Task | Details |
|---|------|---------|
| 1.1 | Verify second generation works | Load font, generate, change size, generate again — must not crash |
| 1.2 | Verify engine presets don't overflow | All 5 preset buttons visible and clickable |
| 1.3 | Verify scrollbar doesn't hide content | All controls accessible in left panel |
| 1.4 | Verify zoom doesn't overlap tabs | Preview/Characters tabs separate from zoom bar |
| 1.5 | Verify Unicode blocks are readable | All block names fully visible |
| 1.6 | Verify auto-select first style | Picking a font family should auto-load Regular |
| 1.7 | Test save/load .bmfc round-trip | Save project, close, reopen, load — all settings restored |
| 1.8 | Test drag-and-drop | Drop .ttf, .otf, .bmfc files onto window |
| 1.9 | Test batch generation | Queue 2-3 fonts, generate all |
| 1.10 | Test all keyboard shortcuts | Ctrl+O, Ctrl+S, Ctrl+G, Ctrl+Shift+S |

## Wave 2: Visual Quality

The app works but looks unpolished. These are purely cosmetic fixes.

| # | Task | Details |
|---|------|---------|
| 2.1 | Consistent control widths | All controls in the left panel should be the same width, aligned to the panel edges |
| 2.2 | Section header styling | Headers (FONT FILE, SIZE, etc.) should stand out more — perhaps with a subtle background bar |
| 2.3 | Effects panel alignment | Bold/Italic/AA/Hinting checkboxes should be in a clean grid, not wrapping oddly |
| 2.4 | Status bar readability | Ensure status text is always legible against the background, especially error states |
| 2.5 | Disabled state clarity | Generate button should look visually distinct when disabled vs enabled |
| 2.6 | Splitter visibility | The dividers between panels should be visible enough to indicate they're draggable |
| 2.7 | Preview panel padding | Atlas image should have some margin from panel edges |
| 2.8 | Sample text area polish | The sample text section needs visual separation from the atlas area |

## Wave 3: Edge Cases & Error Handling

| # | Task | Details |
|---|------|---------|
| 3.1 | Handle invalid font gracefully | Loading a non-font file (e.g., .txt renamed to .ttf) should show error dialog, not crash |
| 3.2 | Handle empty character set | Generating with 0 characters selected should show validation error |
| 3.3 | Handle very large fonts | Generating at 500pt with Latin charset — should warn about atlas size or handle gracefully |
| 3.4 | Handle missing system fonts | If a system font file is deleted between enumeration and load, show error |
| 3.5 | Window resize stability | Rapidly resizing the window should not cause layout glitches or crashes |

---

## Out of Scope

These were in the original plans but are unnecessary for a bitmap font generator:

- Undo/redo — not needed, settings are simple toggles/sliders
- Command palette — overkill for this app
- Tutorial overlay — the UI is straightforward enough
- Splash screen — unnecessary for a utility tool
- Marketing assets — not a code task
- Platform packaging (MSI/AppImage) — `dotnet publish` is sufficient
- Screen reader accessibility — MonoGame doesn't support it
- Light/dark theme switching — dark theme is fine, switching adds complexity for no benefit
