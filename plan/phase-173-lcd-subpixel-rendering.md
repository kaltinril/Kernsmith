# Phase 173 — Native Rasterizer: LCD Subpixel Rendering

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 164 (scanline rasterizer core)

## Goal

Implement LCD subpixel rendering to triple effective horizontal resolution by exploiting RGB subpixel layout, improving text clarity on LCD displays.

## Scope

### Harmony Method (Recommended — Simpler, Patent-Free)

Render the glyph three times with horizontal outline shifts:
1. **Red channel**: shift outline by -1/3 pixel left
2. **Green channel**: render at original position  
3. **Blue channel**: shift outline by +1/3 pixel right

Each render produces an 8-bit grayscale coverage map. Combine into RGB:
```
output[x].R = render_left[x]
output[x].G = render_center[x]
output[x].B = render_right[x]
output[x].A = max(R, G, B)  // or average for smoother blending
```

**Advantages**: No color fringing by construction. No filter needed. Simple.

### ClearType-Style Method (Alternative)

Render at 3× horizontal resolution, then apply FIR filter:
1. Render glyph into a bitmap 3× wider than target
2. Apply 5-tap FIR low-pass filter to reduce color fringing:
   `filtered[x] = 1/9 * input[x-2] + 2/9 * input[x-1] + 3/9 * input[x] + 2/9 * input[x+1] + 1/9 * input[x+2]`
3. Sample every 3rd filtered value for R, G, B channels

**Advantages**: Sharper rendering. Industry standard.
**Disadvantages**: Can produce visible color fringing on high-contrast edges.

### Implementation

- Implement Harmony method first (simpler, good results)
- ClearType-style as optional alternative
- New `AntiAliasMode.Lcd` support in the rasterizer
- Output format: `PixelFormat.Rgba32` (RGB channels carry subpixel data)

### Gamma Correction

LCD rendering requires gamma correction for correct appearance:
```csharp
float gamma = 1.8f; // typical LCD gamma
byte corrected = (byte)(Math.Pow(coverage / 255f, 1f / gamma) * 255f);
```

Apply gamma correction AFTER subpixel rendering, BEFORE output.

### Configuration

- LCD orientation: horizontal (default) or vertical
- Subpixel order: RGB (default) or BGR
- Gamma value (default: 1.8)

### Integration

- Add `AntiAliasMode.Lcd` to `SupportedAntiAliasModes` in capabilities
- When LCD mode requested, use subpixel rendering pipeline
- Output: RGBA bitmap where RGB = subpixel coverage, A = combined coverage

### AntiAliasMode.Light Support

The existing `AntiAliasMode` enum includes `Light` (used by FreeType for light auto-hinting). The Native rasterizer does not support `Light` mode because it has no hinting engine in the MVP (Phases 161-165).

After Phase 174 (Auto-Hinting) is implemented, `Light` mode can be mapped to: auto-hinting with reduced grid-fitting strength (snap blue zones but don't quantize stem widths). Until then:

- If a user requests `AntiAliasMode.Light` with the Native backend, fall back to `Grayscale` silently
- Document this fallback behavior in the capabilities
- Phase 174 should add `Light` to `SupportedAntiAliasModes` when auto-hinting is available

## Testing

- LCD render 'l' (thin vertical): verify tripled resolution visible in RGB channels
- Compare Harmony vs ClearType approaches
- Gamma correction: verify correct application
- BGR mode: verify reversed channel order
- Visual test: render text at 12-16px, compare LCD vs grayscale clarity
- Color fringing test: verify Harmony method produces no visible color artifacts

## Success Criteria

- [ ] Harmony LCD rendering produces correct per-channel coverage
- [ ] ClearType-style rendering with FIR filter works
- [ ] Gamma correction applied
- [ ] RGB/BGR orientation support
- [ ] `AntiAliasMode.Lcd` in supported modes
- [ ] Visual quality improvement visible at small sizes
- [ ] All tests pass
