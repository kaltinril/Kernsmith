# Phase 95 -- Performance Optimization & Bug Fixes

> **Status**: Planning
> **Size**: Large
> **Created**: 2026-03-29
> **Origin**: Full codebase audit (efficiency, speed, correctness)
> **Goal**: Fix confirmed bugs and improve generation performance without changing output.

## Bugs

### Bug 1: Binary formatter hardcodes outline=0

**File**: `src/KernSmith/Output/BmFontBinaryFormatter.cs:72`

The outline field is hardcoded to `(byte)0` instead of `(byte)info.Outline`. Fonts generated with an outline width (e.g., 3px) will have `outline=0` in binary .fnt output, while text and XML formatters correctly write the value.

**Fix**: Change `bw.Write((byte)0)` to `bw.Write((byte)info.Outline)`.

### Bug 2: All three BmFontReader parsers discard the Outline field

**File**: `src/KernSmith/Output/BmFontReader.cs` (~L335 text, ~L400 XML, ~L444 binary)

None of the three parsers read the `Outline` value when constructing `InfoBlock`. The binary reader skips `data[13]` with a comment but never stores it. Text and XML readers never call `GetInt`/`XmlAttrInt` for "outline". Round-tripping a .fnt file with `outline=3` always produces `outline=0`.

**Note**: `InfoBlock` is a positional `record` with `int Outline = 0` as an optional parameter, so all three call sites compile without it and silently default to 0. This is why the bug isn't caught by the compiler.

**Fix**:
- **Text parser** (`ParseInfoFromKvp`): add `Outline: GetInt(kvp, "outline")` to the `InfoBlock(...)` call
- **XML parser** (`ParseInfoFromXml`): add `Outline: XmlAttrInt(el, "outline")` to the `InfoBlock(...)` call
- **Binary parser** (`ParseInfoBlockBinary`): read `var outline = data[13];` and pass `Outline: outline` to the `InfoBlock(...)` call

### Bug 3: Combined batch mode truncates codepoints above U+FFFFF

**File**: `src/KernSmith/BmFont.cs:193-194`

Both `EncodeCombinedId` and `DecodeCombinedId` use only 20 bits for the codepoint. `EncodeCombinedId` uses `(fontIndex << 20) | (codepoint & 0xFFFFF)` and `DecodeCombinedId` uses `>> 20` / `& 0xFFFFF`. Valid Unicode goes to U+10FFFF (21 bits). Codepoints in U+100000..U+10FFFF will collide with U+00000..U+0FFFF in combined batch mode.

**Fix**: Both methods must be updated together:
- `EncodeCombinedId`: `(fontIndex << 21) | (codepoint & 0x1FFFFF)`
- `DecodeCombinedId`: `id >>> 21` for fontIndex, `id & 0x1FFFFF` for codepoint

**Caveat**: The return type is `int` (signed 32-bit). With `fontIndex << 21`, larger font indices can set bit 31 (the sign bit), making the result negative. `DecodeCombinedId` must use `>>>` (unsigned right shift, C# 11+) instead of `>>` (arithmetic, sign-extends) to avoid corrupting the font index. Alternatively, change both methods to use `uint`.

### Bug 4: `GenerateFromSystem` mutates the caller's options object

**File**: `src/KernSmith/BmFont.cs:477-520`

When the system font lookup finds a styled variant, the method directly mutates `options.Bold`, `options.Italic`, and `options.FaceIndex` on the caller's object. If the caller reuses the same options for another font, they get different behavior.

**Note**: `FontGeneratorOptions` (`src/KernSmith/Config/FontGeneratorOptions.cs`) is a plain `class` with no `Clone()` method and does not implement `ICloneable`.

**Fix**: Save and restore the three mutated fields locally rather than modifying the caller's object:
```csharp
var originalBold = options.Bold;
var originalItalic = options.Italic;
var originalFaceIndex = options.FaceIndex;
try
{
    // ... existing mutation logic ...
}
finally
{
    options.Bold = originalBold;
    options.Italic = originalItalic;
    options.FaceIndex = originalFaceIndex;
}
```
Alternatively, copy the three fields into local variables and use those locals throughout the method instead of mutating `options` at all.

## Performance -- High Impact

### Perf 1: `FT_Set_Char_Size` called on every glyph

**File**: `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs:128-170`

`RasterizeGlyph`, `RasterizeOutline`, and `GetGlyphMetrics` all call `FT_Set_Char_Size` (and potentially `FT_Set_Var_Design_Coordinates`) on every single glyph. The size/DPI never changes within a generation run.

**Fix**: Cache the last-set size/DPI and skip the call when unchanged. Eliminates N-1 redundant native interop calls.

### ~~Perf 2: Merge glyph pipeline into a single in-place pass~~ (DONE — Phase 37)

**File**: `src/KernSmith/BmFont.cs:128-165` (method `RasterizeFont`)

Multiple sequential `.Select(g => ...).ToList()` calls create up to 5 full copies of the glyph list with their bitmap data. For 5000 glyphs at size 32, this is ~100 MB of intermediate garbage.

#### Pipeline steps (exact order, lines 128-165)

| Step | Line | Gate | Can change list length? | Cross-glyph dependency? |
|------|------|------|------------------------|------------------------|
| A. Rasterize | 128 | Always | Yes (initial list) | N/A |
| B. Height stretch | 130-134 | `options.HeightPercent != 100` | No | No |
| C. Custom glyphs | 136-139 | `options.CustomGlyphs is { Count: > 0 }` | **Yes (can append)** | **Yes (needs full list)** |
| D. Effects composite | 141-143 | `effects.Count > 0` | No | No |
| E. Custom post-processors | 145-154 | `options.PostProcessors != null` | No | No |
| F. Super-sample downscale | 156-159 | `ssLevel > 1` | No | No |
| G. Equalize cell heights | 161-165 | `options.EqualizeCellHeights` | No | **Yes (needs `maxHeight` from all glyphs)** |

#### Ordering constraints

- **B before D**: Height stretch changes glyph dimensions; effects operate on the stretched alpha mask
- **C before D**: Custom glyphs must be injected before effects so they receive the same treatment
- **D before F**: Effects produce RGBA at super-sampled resolution; downscale must happen after
- **F before G**: Downscaling changes Height; equalize must see final heights
- **E before F**: Custom post-processors run at super-sampled resolution

#### Fix

Steps B, D, E, F are pure per-glyph transforms that can be merged into a single `for` loop. Step C (custom glyphs) must remain a list-level operation but only runs when custom glyphs are configured. Step G requires a reduce pass (`maxHeight`) then a parallel map.

```csharp
// Step A: rasterize (sequential, FreeType not thread-safe)
var glyphs = rasterizer.RasterizeAll(codepoints, effectiveRasterOptions).ToList();

// Step B+C: height stretch + custom glyphs (list-level, must be sequential)
if (needsStretch)
    for (int i = 0; i < glyphs.Count; i++)
        glyphs[i] = stretch.Process(glyphs[i]);
if (hasCustomGlyphs)
    ApplyCustomGlyphs(glyphs, ...);

// Steps D+E+F: per-glyph transforms (parallelizable, see Perf 3)
for (int i = 0; i < glyphs.Count; i++)
{
    var g = glyphs[i];
    if (hasEffects) g = GlyphCompositor.Composite(g, effects);
    foreach (var proc in filteredPostProcessors) g = proc.Process(g);
    if (ssLevel > 1) g = SuperSampleDownscale(g, ssLevel);
    glyphs[i] = g;
}

// Step G: equalize (reduce then map)
if (options.EqualizeCellHeights && glyphs.Count > 0)
{
    var maxHeight = glyphs.Max(g => g.Height);
    for (int i = 0; i < glyphs.Count; i++)
        glyphs[i] = EqualizeCellHeight(glyphs[i], maxHeight);
}
```

**Note**: `RasterizedGlyph` is a `sealed class` with `required init` properties (effectively immutable after construction). Each transform creates a new instance -- the originals are not mutated. This is safe for the in-place list update pattern above since each `glyphs[i] =` just replaces the reference.

### Perf 3: Parallelize per-glyph post-processing (see also Perf 6b)

**File**: `src/KernSmith/BmFont.cs:128-165`

#### Thread-safety audit (all confirmed safe)

| Component | Static fields | Mutable instance state | Parallel-safe? |
|-----------|--------------|----------------------|----------------|
| GlyphCompositor | None (static class, no static fields) | N/A | Yes |
| OutlineEffect | None | All `private readonly` | Yes |
| ShadowEffect | None | All `private readonly` | Yes |
| GradientEffect | None | All `private readonly` | Yes |
| OutlinePostProcessor | None | All get-only properties | Yes |
| ShadowPostProcessor | None | All get-only properties | Yes |
| GradientPostProcessor | None | All get-only properties | Yes |
| HeightStretchPostProcessor | None | Single get-only property | Yes |
| EuclideanDistanceTransform | 1 const only | N/A (static class) | Yes |
| SuperSampleDownscale | None | N/A (static method) | Yes |
| RasterizedGlyph | None | All `required init` (immutable after construction) | Yes |

No lazy initialization, no caches, no closures over shared mutable variables, no `lock` statements found in any of these files. A single effect/processor instance can be safely shared across parallel calls.

#### Fix

Wrap the merged D+E+F loop from Perf 2 with `Parallel.For`:

```csharp
Parallel.For(0, glyphs.Count, i =>
{
    var g = glyphs[i];
    if (hasEffects) g = GlyphCompositor.Composite(g, effects);
    foreach (var proc in filteredPostProcessors) g = proc.Process(g);
    if (ssLevel > 1) g = SuperSampleDownscale(g, ssLevel);
    glyphs[i] = g;
});
```

**Constraints**:
- FreeType rasterization (step A) MUST stay sequential -- the face handle is not thread-safe
- Custom glyphs (step C) is a list-level mutation, must stay sequential
- Equalize (step G) needs a barrier: compute `maxHeight` first, then the per-glyph padding can be parallelized
- `effects` list is `IReadOnlyList<IGlyphEffect>` -- shared read-only, safe for concurrent access

### Perf 4: O(n^2/n^3) `PruneContainedRects` in MaxRectsPacker

**File**: `src/KernSmith/Atlas/MaxRectsPacker.cs:135-151`

The nested loop is O(n^2) and `List.RemoveAt(i)` is O(n), making it O(n^3) worst case. Called after every glyph placement.

**Fix**: Mark rects for removal in the inner loop and batch-remove afterward (swap-remove or build a new list). Also applies to `SplitFreeRects` (lines 102-133) which has the same `RemoveAt` issue.

**Constraint**: Must produce identical packing output. The removal order determines which free rects survive, which affects subsequent placements. Use the same iteration order (outer: high-to-low index, inner: high-to-low) and the same containment check priority (remove the contained rect, keep the container).

### ~~Perf 5: Naive O(W*H*R) box blur~~ (DONE — Phase 37)

**Files**: `src/KernSmith/Rasterizer/ShadowEffect.cs:105-143`, `src/KernSmith/Rasterizer/ShadowPostProcessor.cs:197-235`

Both implementations are character-for-character identical. Both must be updated together.

#### Current algorithm (must be preserved for identical output)

| Property | Value |
|----------|-------|
| Data type | `float` (32-bit IEEE 754) |
| Kernel size | `2 * radius + 1` |
| Normalization | `sum * (1f / kernelSize)` -- multiply by precomputed float reciprocal |
| Edge handling | `Math.Clamp` (extend/repeat edge pixel) |
| Pass structure | Separable: horizontal then vertical |
| Accumulation order | Left-to-right, k from `-radius` to `+radius` |
| Passes | 1 (single box blur, not repeated) |

#### Fix

Replace the inner `for k` loop with a sliding-window approach for O(W*H) regardless of radius.

**Byte-identical warning**: A naive sliding-window (`sum += newVal; sum -= oldVal;`) accumulates different float rounding than per-pixel summation because float addition is not associative. Recommended: use `double` for the running sum and cast to `float` at output -- the rounding differences vanish below float32 quantization.

### Perf 6: No ArrayPool for glyph/atlas buffers

~45 `new byte[]` / `new float[]` allocations across the pipeline. Grep for `new byte[` and `new float[` in `src/KernSmith/` to find them all.

#### How to categorize each allocation

- **EASY** (temporary scratch): Buffer is allocated, used, and discarded within a single method. Rent at start, return at end. ~22 allocations, biggest is the 4 MB grayscale-to-RGBA promotion in `BmFont.cs`.
- **MEDIUM** (stored in returned object): Buffer ends up in `RasterizedGlyph.BitmapData` or `AtlasPage.PixelData`. Poolable with lifecycle management. Atlas pages are 1-4 MB LOH allocations -- highest value targets. ~13 allocations.
- **HARD** (final output): Buffer is returned to the caller as encoded DDS/TGA bytes or decompressed font data. Exact size required, don't pool. ~3 allocations.
- **Skip**: Anything under ~1 KB (TGA 18-byte header, PANOSE 10 bytes, formatter scratch).

#### Implementation strategy

**Phase A -- EASY scratch buffers (low risk, high reward)**:
- `ArrayPool<byte>.Shared.Rent()` / `Return()` for temporary buffers in effects, compositor, EDT, blur
- **Critical**: Rented arrays may be larger than requested -- always use the requested length for loop bounds, not `array.Length`
- **Critical**: Zero-initialize only if the buffer is read before being fully written (most aren't)
- Use `ArrayPool<float>.Shared` for float scratch (shadow blur, EDT)

**Phase B -- Atlas page buffers (medium risk, high reward)**:
- Atlas pages in `AtlasBuilder`, `ChannelCompositor`, `ChannelPackedAtlasBuilder` are 1-4 MB LOH allocations
- Pool at the build level: rent before atlas build, return after encoding completes
- Option: `AtlasPage` implements `IDisposable`

**Phase C -- Per-glyph `RasterizedGlyph.BitmapData` (higher risk)**:
- Each transform creates a new `RasterizedGlyph` with a new `byte[]`, discarding the previous
- In the merged single-pass loop (Perf 2), return the previous buffer before replacing
- **Risk**: Final glyph buffers in `BmFontResult` must NOT be pooled -- only intermediate ones during the transform pipeline

### Perf 6b: Parallelize per-glyph effects pipeline

**File**: `src/KernSmith/BmFont.cs` — the consolidated for-loop from Phase 37

Each glyph's transform pipeline (effects, post-processors, downscale) is independent — no shared state between glyphs. This is embarrassingly parallel. On the "Galaxy Swirl" test (64px, 4x SS, outline+gradient+shadow, 224 chars), serial processing takes ~740ms.

**Approach**: Use `Parallel.For` with configurable `MaxDegreeOfParallelism` for the per-glyph effects loop. Must remain serial on WASM (single-threaded runtime). Atlas packing must stay serial (bin-packing has ordering dependencies). Rasterization parallelism depends on backend thread safety — StbTrueType may be safe per-instance, FreeType is not per-face.

**Platform considerations**:
- **Desktop/Server**: `Parallel.For` or `Task.WhenAll` with `Environment.ProcessorCount`
- **WASM**: Must fall back to serial. Detect via `OperatingSystem.IsBrowser()` or a build-time flag
- **AOT**: `Parallel.For` is fully AOT-compatible (no reflection), uses `System.Threading.Tasks`

**Expected impact**: Near-linear speedup on multi-core for effect-heavy configs. The Galaxy Swirl test should drop from ~740ms to ~200ms on a 4-core machine.

## Performance -- Medium Impact

### Perf 7: Redundant font data copies

**Files**: `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs:35`, `src/KernSmith/Font/TtfParser.cs:43`

Both call `.ToArray()` on font data that's already a `byte[]` in all call sites. For a 10 MB font, each copy wastes 10 MB.

**Fix**: Use `MemoryMarshal.TryGetArray` in FreeTypeRasterizer to extract the underlying array. Add a `byte[]` constructor path in TtfParser to avoid the copy.

### Perf 8: Page encoding is sequential

**Files**: `src/KernSmith/Output/BmFontResult.cs:216-225`, `src/KernSmith/Output/FileWriter.cs:64-80`

`EncodeAllPages` and `WriteAtlasPages` process pages one at a time. Each page is independent.

**Fix**: Use `Parallel.For` to encode pages concurrently. For file writing, encode all pages in parallel first, then write.

### Perf 9: Per-pixel BGRA swap in TGA/DDS encoders

**Files**: `src/KernSmith/Atlas/TgaEncoder.cs:46-63`, `src/KernSmith/Atlas/DdsEncoder.cs:89-105`

Byte-by-byte RGBA-to-BGRA conversion in a per-pixel loop.

**Fix**: Use `Unsafe.As<byte, uint>()` for 4-byte-at-a-time bit manipulation, or `Vector<byte>` SIMD for 16-64 bytes at a time.

### Perf 10: Floating-point alpha blending per pixel

**Files**: `src/KernSmith/Rasterizer/GlyphCompositor.cs:181-194`, `src/KernSmith/Rasterizer/OutlinePostProcessor.cs:129-140`, `src/KernSmith/Rasterizer/ShadowPostProcessor.cs:155-168`

Alpha-over blending uses `float` division per pixel (`/ 255f`, `/ outA`). Appears in three files, runs for every pixel of every glyph with effects.

**Fix**: Use integer-only blending with `(a * 255 + 128) / 255` or shift-based approximations. Must verify identical output byte-for-byte.

## Performance -- Low Impact

### Perf 11: Per-pixel format check in inner loops

**File**: `src/KernSmith/Rasterizer/GlyphCompositor.cs:224-248`

`glyph.Format == PixelFormat.Rgba32` checked on every pixel. Also in `ChannelCompositor.GetGlyphAlpha` and related methods.

**Fix**: Hoist format check outside the loop; use two separate loop bodies.

### Perf 12: TextFormatter string interpolation per field per glyph

**File**: `src/KernSmith/Output/TextFormatter.cs:72-91`

Uses `$" id={ch.Id}"` per field instead of `sb.Append(" id=").Append(ch.Id)`. Creates ~10k temporary strings for 1000 glyphs.

**Fix**: Use `StringBuilder.Append()` chaining.

### Perf 13: `BmFontResult` properties re-format on every access

**File**: `src/KernSmith/Output/BmFontResult.cs:57-82`

`FntText`, `FntXml`, `FntBinary` are computed properties that re-serialize the model every time they're accessed.

**Fix**: Use `Lazy<T>` to cache formatted output.

### Perf 14: `MemoryStream` without pre-allocated capacity

**File**: `src/KernSmith/Atlas/StbPngEncoder.cs:21`

`new MemoryStream()` starts at 0 capacity and resizes repeatedly. Also uses `.ToArray()` which copies the buffer.

**Fix**: Pre-allocate with estimated capacity. Use `TryGetBuffer` instead of `ToArray`.

### Perf 15: EDT scratch arrays allocated per glyph

**File**: `src/KernSmith/Atlas/EuclideanDistanceTransform.cs:28-32`

Four scratch arrays (`float[]`, `int[]`) allocated per `Compute()` call (per glyph with outline effects).

**Fix**: Pool with `ArrayPool` or use `[ThreadStatic]` fields.

### Perf 16: `codepoints.Contains` is O(n) on a List

**File**: `src/KernSmith/BmFont.cs:63`

`List<int>.Contains` is a linear scan. Low impact since it's called once, not in a loop, but matters for large CJK character sets.

**Fix**: Use a `HashSet<int>` for membership checks.

## Implementation Order

1. **Bugs first** (1-4) -- correctness before performance
2. **High-impact perf** (1-6) -- biggest wins for generation time
3. **Medium-impact perf** (7-10) -- meaningful but less critical
4. **Low-impact perf** (11-16) -- polish
