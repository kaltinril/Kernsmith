# Phase 80 — Atlas Preview Rendering Quality

> **Status**: Complete
> **Created**: 2026-03-22
> **Completed**: 2026-03-24
> **Goal**: Fix atlas preview rendering in the UI to match the saved PNG at 1:1 zoom.

---

## Problem

The atlas preview in the Preview tab showed minor rendering artifacts compared to the saved PNG file. PointClamp sampler state improved it but some glyphs still appeared slightly degraded (e.g., thick center strokes on certain characters). The saved PNG was correct — purely a UI display issue.

## Root Cause

Sub-pixel misalignment in the GUM SpriteRuntime rendering pipeline:

1. **Pan offsets used floats** — sprite X/Y could land on non-integer values after panning, causing texture sampling to straddle pixel boundaries
2. **Zoom scaling truncated** — `(int)(texture.Width * zoom)` truncated instead of rounding, losing up to 1px at certain zoom levels
3. **Camera.Zoom compounded the issue** — UI scale magnified fractional pixel offsets

## Changes

### `apps/KernSmith.Ui/Layout/PreviewPanel.cs`

- **Pixel-aligned pan positions** — wrapped sprite X/Y assignments with `Math.Round()` to ensure integer pixel positions after panning
- **Rounded zoom scaling** — changed `(int)(w * zoom)` to `(int)Math.Round(w * zoom)` in both `ShowAtlas()` and `ApplyZoom()` to prevent 1px truncation
- **Preserved zoom on auto-regenerate** — added `_hasUserZoom` flag so changing settings with auto-regenerate enabled no longer resets the zoom level; only the first display auto-fits

## Key Source Files

| What | Location |
|------|----------|
| Atlas sprite setup | `apps/KernSmith.Ui/Layout/PreviewPanel.cs` |
| Sampler state | `apps/KernSmith.Ui/KernSmithGame.cs` (Draw method) |
| GUM renderer | GUM library internals |
