# Phase 187 — BmFontBuilder.FromConfig Field Coverage

> **Status**: Not started
> **Related**: `src/KernSmith/BmFontBuilder.cs` (`CopyOptions`), `src/KernSmith/Config/FontGeneratorOptions.cs`, `src/KernSmith/Config/BmfcConfigReader.cs`

## Problem

`BmFontBuilder.FromConfig` copies the parsed `FontGeneratorOptions` field by field in `CopyOptions`, and the list has fallen behind the options class. These fields are silently reset to their defaults, so `BmFont.Builder().FromConfig(path).Build()` renders differently from `BmFont.FromConfig(path)` (which passes `config.Options` straight through):

| Field(s) | Read from `.bmfc` by `BmfcConfigReader` |
|---|---|
| `Gamma` | `gamma` |
| `FillColorR/G/B/A` | `fillColor` |
| `AdvanceAdjustX`, `AdvanceAdjustY` | `advanceAdjustX`, `advanceAdjustY` |
| `GradientOffset`, `GradientScale`, `GradientCyclic` | `gradientOffset`, `gradientScale`, `gradientCyclic` |
| `ShadowBlurKernelSize`, `ShadowBlurPasses` | `shadowBlurKernelSize`, `shadowBlurPasses` |
| `Variants` | `variantShadow` |
| `SizeConstraints`, `TargetRegion` | not in `.bmfc` (API-only; dropped when a caller builds a `BmfcConfig` in code) |

The `tests/bmfont-compare` harness generates through `Builder().FromConfig`, so its `shadow-variant.bmfc` config (`variantShadow=1`) currently renders without the shadow variant on every backend.

## Work items

1. Copy every missing field in `CopyOptions`; deep-copy the collection-valued ones (`Variants`) the same way `VariationAxes`/`CustomGlyphs` are copied.
2. Add a guard test that fails when `FontGeneratorOptions` gains a settable property `CopyOptions` does not carry (reflection over public properties, set each to a non-default value, assert the builder's options match), so the list cannot drift again.
3. Per-field behavioural tests for the `.bmfc`-readable fields: builder output equals `BmFont.FromConfig` output for a config setting that field.

## Risk

Fixing this changes generated output for any builder-based caller whose config sets one of these fields — including the harness's `shadow-variant.bmfc`, which will start emitting its variant. That is the intended correction, but it shows up as a harness diff and as a behaviour change for library users of `Builder().FromConfig`; note it in the CHANGELOG under Fixed.

## Testing plan

- Red→green for the guard test and each per-field test (run against unmodified `CopyOptions` first).
- Full `dotnet test --solution KernSmith.sln`.
- Regression harness (`python tests/bmfont-compare/regression_check.py`): expect `shadow-variant` to change on every backend (variant now present) and every other config to stay identical. Add a `.bmfc` exercising `gamma`/`fillColor`/`advanceAdjustX`/gradient offset-scale-cyclic/blur kernel-passes if no tracked config covers them, so the harness actually runs those paths.
