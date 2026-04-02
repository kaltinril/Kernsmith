# Phase 170 — Native Rasterizer: Multi-Channel SDF (MSDF) Generation

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 169 (single-channel SDF)

## Goal

Implement Multi-Channel Signed Distance Field (MSDF) generation, which preserves sharp corners that single-channel SDF rounds off. MSDF uses RGB channels to encode distance to different edge segments.

## Background

Single-channel SDF can't distinguish sharp corners from smooth curves at the same distance. MSDF (by Viktor Chlumsky / msdfgen) assigns edge segments to R, G, or B channels. The median of the three channels reconstructs the true distance, while individual channels preserve corner sharpness.

## Scope

### Edge Coloring Algorithm

Assign each outline edge segment a color (Red, Green, or Blue):
1. At sharp corners (angle > threshold, e.g., 3°), adjacent edges MUST have different colors
2. Use a simple heuristic: cycle through colors at each corner
3. For smooth segments between corners: assign same color as the chain

This is the "edgeColoringSimple" algorithm from msdfgen.

### Per-Channel Distance Computation

For each texel:
1. Compute signed distance to nearest Red edge → store in R channel
2. Compute signed distance to nearest Green edge → store in G channel  
3. Compute signed distance to nearest Blue edge → store in B channel

At render time: `distance = median(R, G, B)` gives the SDF with sharp corners.

### MTSDF Option

Optionally, add a true SDF in the alpha channel alongside MSDF in RGB:
- R, G, B = multi-channel distances (for corner preservation)
- A = true signed distance (for smooth operations like shadow/glow)
- Output format: `PixelFormat.Rgba32`

### Corner Detection

A corner exists where two adjacent edge segments meet at a sharp angle:
```csharp
float crossProduct = tangent1.X * tangent2.Y - tangent1.Y * tangent2.X;
float dotProduct = tangent1.X * tangent2.X + tangent1.Y * tangent2.Y;
bool isCorner = Math.Abs(crossProduct) > threshold || dotProduct < 0;
```

### Output

- SDF mode (Phase 169): `Grayscale8` — single channel
- MSDF mode: `Rgba32` — R/G/B = multi-channel distances, A = 255 or true SDF
- MTSDF mode: `Rgba32` — R/G/B = multi-channel, A = true SDF

### Configuration

New enum (in `KernSmith` root namespace):
```csharp
public enum SdfMode
{
    None = 0,       // Standard rasterization (no SDF)
    Sdf = 1,        // Single-channel signed distance field
    Msdf = 2,       // Multi-channel SDF (RGB = per-edge-color distances)
    Mtsdf = 3       // Multi-channel + true SDF in alpha channel
}
```

Changes to `RasterOptions`:
- Add `SdfMode SdfMode = SdfMode.None` property
- Deprecate existing `bool Sdf` property (map to `SdfMode.Sdf` for backward compatibility)
- When `SdfMode == Msdf || SdfMode == Mtsdf`, output format is `PixelFormat.Rgba32`

Changes to `IRasterizerCapabilities`:
- Add `bool SupportsMsdf { get; }` property (default false)
- Native rasterizer sets `SupportsMsdf = true` after this phase

Update `NativeRasterizer`:
- Dispatch on `SdfMode`: None → coverage raster, Sdf → Phase 169 path, Msdf/Mtsdf → this phase's path

## Testing

- MSDF 'A': verify sharp corners at apex and baseline preserved
- Compare MSDF median vs single-channel SDF: should match on smooth edges, differ at corners
- Edge coloring: verify no two adjacent edges at a corner share a color
- Visual test: render MSDF at low resolution, verify corner quality
- Round-trip: MSDF → median → threshold → compare with rasterized glyph

## Success Criteria

- [ ] Edge coloring correctly assigns colors at corners
- [ ] Per-channel distances computed correctly
- [ ] MSDF median matches single-channel SDF on smooth regions
- [ ] Sharp corners preserved in MSDF output
- [ ] MTSDF alpha channel matches single-channel SDF
- [ ] All tests pass
