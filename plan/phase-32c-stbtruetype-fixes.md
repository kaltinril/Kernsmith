# Phase 32c — StbTrueType Validation Fixes

> **Status**: Partial
> **Created**: 2026-03-31
> **Depends on**: Phase 32 (StbTrueType rasterizer), Phase 32b (docs/publishing)
> **Related**: Phase 30, Phase 33 (WASM validation)

## Goal

Fix bugs, missing guards, and test coverage gaps identified during Phase 30/31/32 validation review.

## Phase 32 Rasterizer Bugs

### 1. Integer division truncation on SuperSample metrics (HIGH)

**File:** `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs`

When `SuperSample > 1`, advance and bearing calculations use integer division which truncates toward zero:

```csharp
int bearingX = ix0 / aa;
int bearingY = -iy0 / aa;
int scaledAdvance = (int)Math.Round(advance * scale) / aa;
```

This produces off-by-one pixel errors. The GDI rasterizer uses `Math.Ceiling` for its analogous division. Fix: use `(int)Math.Round((double)value / aa)` consistently across `RasterizeGlyph`, `GetGlyphMetrics`, and `RasterizeSdfGlyph`.

### 2. SDF + SuperSample is semantically wrong (MEDIUM)

**File:** `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs`

When both `Sdf = true` and `SuperSample > 1`, the code renders the SDF at the supersampled scale then box-averages the distance field values. Averaging distance values destroys the SDF semantics — distances don't average linearly like pixel intensities.

Fix: When `Sdf = true`, ignore `SuperSample` entirely (render at base size, skip downscale). SDF is resolution-independent by design, so supersampling is meaningless. This matches the existing guard in `BmFont.cs:95` that prevents SDF + SuperSampleLevel > 1 at the options level.

### 3. No validation when ColorFont = true (MEDIUM)

**File:** `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs`

Bold and Italic correctly throw `NotSupportedException`, but `ColorFont = true` is silently ignored (renders grayscale). Should throw `NotSupportedException` for consistency.

### 4. DownscaleBitmap discards remainder pixels (LOW)

**File:** `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs`

`newWidth = width / aa` discards remainder pixels when dimensions aren't exact multiples of `aa`. Could cause 1-pixel clipping on right/bottom edges. Consider ceiling division or padding the source bitmap.

### 5. Missing ObjectDisposedException checks (LOW)

**File:** `src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeRasterizer.cs`

`GetKerningPairs`, `LoadSystemFont`, `SetVariationAxes`, and `SelectColorPalette` don't check `_disposed` before executing. Should add `ObjectDisposedException.ThrowIf(_disposed, this)` for consistency.

## Pre-Existing Architecture Gaps

These are NOT caused by Phase 32 but were discovered during validation. They affect WASM/AOT readiness.

### 6. No SupportsSdf capability check in core pipeline (HIGH)

**File:** `src/KernSmith/BmFont.cs`

The pipeline checks `SupportsVariableFonts` and `SupportsColorFonts` before calling those features, but does NOT check `SupportsSdf`. Setting `Sdf = true` with GDI or DirectWrite (which report `SupportsSdf = false`) silently produces non-SDF output with no error.

Fix: Add a guard after the existing variable font / color font guards (~line 166) that throws if `options.Sdf && !rasterizer.Capabilities.SupportsSdf`.

### 7. ModuleInitializer may be trimmed in AOT/WASM (HIGH)

**File:** `src/KernSmith.Rasterizers.StbTrueType/KernSmith.Rasterizers.StbTrueType.csproj`

The StbTrueType package has `IsTrimmable=true` and `IsAotCompatible=true`. If the consuming app never directly references a type from the assembly, the trimmer may remove the entire assembly including the `[ModuleInitializer]`. This would cause "backend not registered" at runtime on Blazor WASM.

Fix: Add `[DynamicDependency]` attribute on the module initializer, or add a `<TrimmerRootAssembly>` directive, or document that consumers must root the assembly.

### 8. JsonSerializer without source generator (MEDIUM)

**File:** `src/KernSmith/Output/BmFontBinaryFormatter.cs:202`

`JsonSerializer.Serialize(Dictionary<string, object>)` uses reflection-based serialization. Will fail on AOT platforms (Blazor WASM with AOT, NativeAOT).

Fix: Add a `[JsonSerializable]` source-generated context, or switch to manual JSON string building.

### 9. Microsoft.Win32 import in core assembly (MEDIUM)

**File:** `src/KernSmith/Font/DefaultSystemFontProvider.cs:5`

`using Microsoft.Win32` at the top of the file may cause `TypeLoadException` on WASM/mobile where `Microsoft.Win32.Registry` is unavailable. The Registry usage is guarded by OS checks at runtime, but the type reference exists at the IL level.

Fix: Isolate Registry usage behind lazy type loading or move `DefaultSystemFontProvider` to a platform-specific package.

### 10. Options mutation side effect (MEDIUM)

**File:** `src/KernSmith/BmFont.cs:149-158`

`options.Bold` and `options.Italic` are mutated directly on the caller's `FontGeneratorOptions` instance. If a user reuses the same options object for multiple `Generate()` calls with different fonts, the second call sees the mutated values from the first.

Fix: Clone the options before mutating, or make `FontGeneratorOptions` a record with `with` syntax.

### 11. No bold/italic capability flag (MEDIUM)

**File:** `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs`

There is no `SupportsSyntheticBold` / `SupportsSyntheticItalic` capability flag. StbTrueType throws raw `NotSupportedException` from inside the rasterizer rather than the core pipeline catching it early with a clear message.

### 12. Unregistered backend error message lacks NuGet guidance (LOW)

**File:** `src/KernSmith/Rasterizer/RasterizerFactory.cs:31-34`

Error message says "Call RasterizerFactory.Register to add a backend" but doesn't tell users which NuGet package to install.

### 13. Parallel.For in batch API on WASM (LOW)

**File:** `src/KernSmith/BmFont.cs:1253`

`Parallel.For` in the batch generation path would throw `PlatformNotSupportedException` on WASM if a user explicitly sets `MaxParallelism > 1`. The default path is safe (ProcessorCount = 1 on WASM), but explicit parallelism should be guarded.

## Test Coverage Gaps

### 14. Missing StbTrueType tests

Add these tests to `tests/KernSmith.Tests/Rasterizer/StbTrueTypeRasterizerTests.cs`:

- SuperSample path (`SuperSample = 2`) — verify dimensions and metrics
- AntiAliasMode.None — verify all bitmap bytes are 0 or 255
- Invalid/corrupt font data — verify exception thrown
- Double LoadFont — verify `InvalidOperationException`
- Bold rejection — verify `NotSupportedException` for `Bold = true`
- Italic rejection — verify `NotSupportedException` for `Italic = true`
- SetVariationAxes — verify `NotSupportedException`
- SelectColorPalette — verify `NotSupportedException`
- Dispose then GetFontMetrics / RasterizeAll
- Dispose idempotency (double dispose doesn't throw)
- Cross-rasterizer metric comparison vs FreeType baseline (+/- 1px tolerance)
- End-to-end `BmFont.Generate()` with `Backend = RasterizerBackend.StbTrueType`

## Success Criteria

- [x] Integer truncation fixed — advance/bearings use rounded division when `aa > 1`
- [x] SDF ignores SuperSample (renders at base size)
- [x] ColorFont = true throws NotSupportedException
- [x] DownscaleBitmap handles non-multiple dimensions
- [x] ObjectDisposedException checks added to all public methods
- [ ] SDF capability guard added to BmFont.cs pipeline
- [ ] ModuleInitializer trimmer-safe for AOT/WASM publish
- [ ] JsonSerializer AOT-compatible in BmFontBinaryFormatter
- [ ] Microsoft.Win32 import isolated from WASM assembly loading
- [ ] Options mutation fixed (clone before mutating)
- [ ] All 12+ new tests pass
- [ ] Existing tests still pass
