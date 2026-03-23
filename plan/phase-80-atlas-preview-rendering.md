# Phase 80 — Atlas Preview Rendering Quality

> **Status**: Planning
> **Created**: 2026-03-22
> **Goal**: Fix atlas preview rendering in the UI to match the saved PNG at 1:1 zoom.

---

## Problem

The atlas preview in the Characters tab shows minor rendering artifacts compared to the saved PNG file. PointClamp sampler state improved it but some glyphs still appear slightly degraded (e.g., thick center strokes on certain characters).

The saved PNG is correct — this is purely a UI display issue.

## Investigation Areas

- GUM SpriteRuntime rendering pipeline vs raw MonoGame SpriteBatch
- Sub-pixel alignment when sprite position/size doesn't land on integer boundaries
- UI scaling (`Camera.Zoom`) interaction with texture sampling
- Whether a custom SpriteBatch draw call would produce better results than GUM's sprite

## Key Source Files

| What | Location |
|------|----------|
| Atlas sprite setup | `apps/KernSmith.Ui/Layout/PreviewPanel.cs` |
| Sampler state | `apps/KernSmith.Ui/KernSmithGame.cs` (Draw method) |
| GUM renderer | GUM library internals |
