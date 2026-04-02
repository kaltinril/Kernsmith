# Phase 171 — Native Rasterizer: Variable Font Support

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration), Phase 166 (CFF2 for variable CFF fonts)

## Goal

Implement variable font axis interpolation so the Native rasterizer can handle fonts with weight, width, italic, optical size, and custom axes.

## Background

Variable fonts contain a single file with multiple styles defined as axes (e.g., weight 100-900). Instead of separate font files for Regular, Bold, Light, etc., a variable font interpolates between masters using delta sets.

## Scope

### fvar Table Parser
- Parse axis records: tag (e.g., `wght`), minValue, defaultValue, maxValue, name
- Parse named instances (e.g., "Bold" = wght:700)
- Standard axes: `wght` (weight), `wdth` (width), `ital` (italic), `slnt` (slant), `opsz` (optical size)

### avar Table Parser
- Piecewise linear axis remapping (avar v1)
- Maps user-space coordinates to normalized coordinates
- Segment map: array of (fromCoordinate, toCoordinate) pairs per axis
- Linear interpolation between segments

### Coordinate Normalization

Convert user axis values to normalized [-1, 0, 1]:
```
if value == default: normalized = 0
if value < default: normalized = -(default - value) / (default - min)
if value > default: normalized = (value - default) / (max - default)
```
Then apply avar remapping if present.

### gvar Table Parser (TrueType variation)

Per-glyph tuple variation data:
1. Parse shared tuples and per-glyph tuple headers
2. For each tuple: peak coordinates, start/end coordinates (intermediate region), deltas
3. Compute scalar for each tuple based on normalized axis coordinates:
   - At peak: scalar = 1.0
   - Between start and peak, or peak and end: linear interpolation
   - Outside start-end: scalar = 0.0
4. Scale deltas by scalar, sum across all tuples
5. Apply delta sum to glyph control points

### IUP (Interpolate Untouched Points)

When gvar specifies deltas for only some points, interpolate the rest:
1. For each contour, find points with explicit deltas
2. For points between two explicit-delta points, interpolate delta linearly by position
3. For points outside the range of explicit-delta points on a contour, use nearest explicit delta

### HVAR / MVAR Table Parser

- HVAR: deltas for horizontal advance widths and left side bearings
- MVAR: deltas for global metrics (ascender, descender, line gap, etc.)
- Uses ItemVariationStore format (shared with other tables)

### ItemVariationStore Parser

Common format used by HVAR, MVAR, VVAR, and others:
- Variation region list: defines N-dimensional regions in axis space
- Item variation data: delta sets indexed by outer/inner indices
- Compute effective delta by evaluating region scalars and summing

### CFF2 blend Integration

**Note**: This is significant work if Phase 166 only stubbed CFF2.

Completing CFF2 variable support requires:
1. **CFF2 structure differences** (vs CFF1): no Name/String INDEX, single Top DICT, simplified header. Phase 166 should have parsed the basic structure; this phase completes the variable-specific parts.
2. **`blend` operator**: Pops N×K region deltas + N default values from the stack, applies ItemVariationStore scalars, pushes N interpolated values. This is the core of CFF2 variation — each charstring inline-interpolates its own coordinates.
3. **`vsindex` operator**: Selects which ItemVariationStore subtable to use for subsequent `blend` operations within the same charstring.
4. **Estimated effort**: ~200-300 lines for blend/vsindex + ItemVariationStore evaluation. Non-trivial because it touches the charstring interpreter's inner loop.

If CFF2 variable fonts are rare in the target use cases, this sub-scope can be deferred without blocking TrueType variable font support (which uses gvar, not blend).

### Integration

- `SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes)` — store axis values, compute normalized coordinates
- Apply gvar deltas during outline extraction (before transforms)
- Apply HVAR deltas to advance widths
- Apply MVAR deltas to font metrics
- Update `IRasterizerCapabilities.SupportsVariableFonts = true`

## Testing

- Parse fvar from a variable font (e.g., Roboto Flex, Inter Variable)
- Set weight axis: verify glyph outlines change (bolder/lighter strokes)
- Set width axis: verify glyph advance widths change
- avar remapping: verify non-linear axis behavior
- gvar interpolation: verify control point deltas applied correctly
- IUP: verify untouched points interpolated correctly
- HVAR: verify advance width deltas
- MVAR: verify global metric deltas
- Named instances: "Bold" instance matches direct wght:700
- Edge cases: multiple axes, intermediate regions, axis at min/max bounds

## Success Criteria

- [ ] fvar axes parsed and exposed correctly
- [ ] Axis normalization and avar remapping correct
- [ ] gvar deltas applied to TrueType glyph control points
- [ ] IUP interpolation correct for untouched points
- [ ] HVAR/MVAR deltas applied to metrics
- [ ] CFF2 blend operator works (if CFF2 font available for testing)
- [ ] `SupportsVariableFonts = true` in capabilities
- [ ] All tests pass
