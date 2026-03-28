# Phase 200 -- FontCrafter & Platform-Specific Rasterizer Distribution

> **Status**: Idea
> **Created**: 2026-03-28
> **Dependencies**: Phase 78 (pluggable rasterizers), Phase 78D (CLI/UI integration)
> **Goal**: (1) Ship platform-aware CLI/UI binaries so Windows users get all three rasterizers while Linux/macOS get FreeType only, and (2) build KernSmith.FontCrafter -- a simple vector font creation tool that doubles as a proof-of-concept custom rasterizer plugin.

---

## Part A: Platform-Specific Rasterizer Distribution

### Problem

Phase 78D wires up GDI and DirectWrite backends via compile-time `#if WINDOWS`, but the published binaries need to be built and distributed correctly so that:
- **Windows** binaries include FreeType + GDI + DirectWrite (3 rasterizers)
- **Linux/macOS** binaries include FreeType only (no Windows-only assemblies bundled)

### Requirements

1. **CLI (`KernSmith.Cli`)**: Platform-specific publish profiles (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`) that conditionally include backend packages
2. **UI (`KernSmith.Ui`)**: Same conditional inclusion -- GDI/DirectWrite `.dll` files must not appear in Linux/macOS builds
3. **CI/CD**: GitHub Actions matrix build that produces per-platform artifacts with the correct rasterizer set
4. **No runtime reflection or assembly scanning** -- keep the compile-time `#if WINDOWS` approach from Phase 78D
5. **User experience**: `kernsmith list-rasterizers` shows only what's actually available on the current platform

### Design Notes

- The `.csproj` conditional TFM + conditional `<PackageReference>` pattern from Phase 78D is the foundation -- this phase ensures it works end-to-end through CI/CD and published artifacts
- Platform matrix in CI: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64` (at minimum)
- Consider self-contained publish (`--self-contained`) so users don't need .NET runtime installed

---

## Part B: KernSmith.FontCrafter

### Vision

A font creation tool where users build bitmap fonts from scratch by designing glyphs as vector shapes (polygons with curves). The tool proves that anyone can write a custom `IRasterizer` and plug it into KernSmith to produce `.fnt` + `.png` output.

### Core Concept

1. User starts with a standard character set (A-Z, a-z, 0-9, punctuation)
2. Each glyph is a set of **nodes** connected by **lines/curves**
3. User manipulates glyphs by:
   - **Dragging points** (polygon vertices)
   - **Adding/removing points** on edges
   - **Setting curve type** per segment (linear, quadratic bezier, cubic bezier)
   - **Adjusting curve control points** (handles for bezier tangents)
   - **Setting line properties** (thickness, etc.)
4. The tool rasterizes these vector definitions into bitmap glyphs
5. Output feeds into KernSmith's standard pipeline (atlas packing, `.fnt` generation)

### Architecture

| Component | Purpose |
|-----------|---------|
| `KernSmith.FontCrafter` | Core library -- glyph model, file format, rasterizer |
| `KernSmith.FontCrafter.Ui` | Editor UI (likely Avalonia, consistent with KernSmith.Ui) |
| `.fontcrafter` format | Custom file format storing glyph vector definitions |

### The FontCrafter Rasterizer

The key deliverable is a custom `IRasterizer` implementation that:
- Reads `.fontcrafter` files instead of TTF/OTF
- Rasterizes vector glyph definitions to bitmaps (scan-line fill of polygons + curve segments)
- Reports capabilities via `IRasterizerCapabilities` (e.g., no hinting, no system fonts, no variable font axes)
- Registers with `RasterizerFactory` like any other backend
- **Proves the plugin architecture works end-to-end** -- if FontCrafter can plug in, so can any third-party rasterizer

### Glyph Model (Initial)

```
Glyph
  - Unicode codepoint
  - Advance width
  - Bearing X/Y
  - Contours[] (closed paths)
    - Points[]
      - X, Y (normalized coordinates, e.g., 0-1000 em units)
      - PointType: OnCurve | QuadraticControl | CubicControl
    - Segments[] (derived from points)
      - SegmentType: Line | QuadraticBezier | CubicBezier
```

This mirrors the TrueType/OpenType glyph model at a simplified level, making it educational and practical.

### File Format (`.fontcrafter`)

- JSON or binary format storing:
  - Font metadata (name, em size, ascender, descender, line gap)
  - Glyph definitions (contours, points, metrics)
  - Kerning pairs (manually defined)
- Start with JSON for readability/debuggability; consider binary later for large fonts

### Phased Approach

| Sub-Phase | Name | Description |
|-----------|------|-------------|
| 200A | Glyph Model & Format | Define the `.fontcrafter` format and in-memory glyph model |
| 200B | FontCrafter Rasterizer | `IRasterizer` implementation that rasterizes vector glyphs to bitmaps |
| 200C | Basic Editor UI | Avalonia UI for creating/editing glyphs -- point dragging, adding points, curve handles |
| 200D | KernSmith Integration | Wire FontCrafter rasterizer into CLI/UI as a selectable backend |
| 200E | Platform Rasterizer Distribution | CI/CD matrix builds producing correct per-platform binaries (Part A above) |

### What This Is NOT (Initially)

- Not a full font editor (no OpenType features, no complex scripts, no variable font axes)
- Not a TTF/OTF exporter (output is `.fontcrafter` -> KernSmith -> `.fnt` + `.png`)
- Not trying to compete with FontForge/Glyphs -- it's a simplified tool for bitmap font creation
- Curves and effects can be expanded over time, but v1 is intentionally minimal

### Success Criteria

1. A user can create a simple bitmap font from scratch using the editor
2. The FontCrafter rasterizer plugs into KernSmith via `IRasterizer` with zero changes to core
3. `kernsmith generate --rasterizer fontcrafter --input myfont.fontcrafter` produces valid `.fnt` + `.png`
4. The example proves that third-party rasterizer plugins are viable and documented
