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

**File**: `src/KernSmith/Output/BmFontReader.cs` (~L345 text, ~L410 XML, ~L475 binary)

None of the three parsers read the `Outline` value when constructing `InfoBlock`. The binary reader reads `data[13]` but discards it. Text and XML readers never call `GetInt(kvp, "outline")` or `XmlAttrInt(el, "outline")`. Round-tripping a .fnt file with `outline=3` always produces `outline=0`.

**Fix**: Add `Outline:` parameter to all three `InfoBlock` constructors in the reader.

### Bug 3: Combined batch mode truncates codepoints above U+FFFFF

**File**: `src/KernSmith/BmFont.cs:193`

`EncodeCombinedId` uses `(fontIndex << 20) | (codepoint & 0xFFFFF)`, allocating only 20 bits for the codepoint. Valid Unicode goes to U+10FFFF (21 bits). Codepoints in U+100000..U+10FFFF will collide with U+00000..U+0FFFF in combined batch mode.

**Fix**: Use 21 bits for codepoint: `(fontIndex << 21) | (codepoint & 0x1FFFFF)`. This still allows up to 2047 fonts.

### Bug 4: `GenerateFromSystem` mutates the caller's options object

**File**: `src/KernSmith/BmFont.cs:487-519`

When the system font lookup finds a styled variant, the method directly mutates `options.Bold`, `options.Italic`, and `options.FaceIndex` on the caller's object. If the caller reuses the same options for another font, they get different behavior.

**Fix**: Clone the options before mutating, or work with a local copy.

## Performance -- High Impact

### Perf 1: `FT_Set_Char_Size` called on every glyph

**File**: `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs:128-170`

`RasterizeGlyph`, `RasterizeOutline`, and `GetGlyphMetrics` all call `FT_Set_Char_Size` (and potentially `FT_Set_Var_Design_Coordinates`) on every single glyph. The size/DPI never changes within a generation run.

**Fix**: Cache the last-set size/DPI and skip the call when unchanged. Eliminates N-1 redundant native interop calls.

### Perf 2: 5 intermediate List copies in the glyph pipeline

**File**: `src/KernSmith/BmFont.cs:128-165`

Multiple sequential `.Select(g => ...).ToList()` calls create up to 5 full copies of the glyph list with their bitmap data. For 5000 glyphs at size 32, this is ~100 MB of intermediate garbage.

**Fix**: Combine transforms into a single in-place pass:
```csharp
for (int i = 0; i < glyphs.Count; i++)
{
    var g = glyphs[i];
    if (needsStretch) g = stretch.Process(g);
    if (hasEffects) g = GlyphCompositor.Composite(g, effects);
    if (needsDownscale) g = SuperSampleDownscale(g, ssLevel);
    glyphs[i] = g;
}
```

### Perf 3: Post-processing is not parallelized

**File**: `src/KernSmith/BmFont.cs:128-165`

Per-glyph transforms (outline, shadow, gradient, super-sample) are pure functions on independent data but run sequentially. These are embarrassingly parallel.

**Fix**: After combining into a single pass (Perf 2), wrap with `Parallel.For` or `Parallel.ForEach`. FreeType rasterization itself must stay sequential (face handle is not thread-safe), but everything after `RasterizeAll` can be parallelized.

### Perf 4: O(n^2/n^3) `PruneContainedRects` in MaxRectsPacker

**File**: `src/KernSmith/Atlas/MaxRectsPacker.cs:135-151`

The nested loop is O(n^2) and `List.RemoveAt(i)` is O(n), making it O(n^3) worst case. Called after every glyph placement.

**Fix**: Mark rects for removal in the inner loop and batch-remove afterward (swap-remove or build a new list). Also applies to `SplitFreeRects` (lines 102-133) which has the same `RemoveAt` issue.

### Perf 5: Naive O(W*H*R) box blur

**Files**: `src/KernSmith/Rasterizer/ShadowEffect.cs:105-143`, `src/KernSmith/Rasterizer/ShadowPostProcessor.cs:197-235`

Both `BoxBlur` implementations use a naive triple-nested loop. For blur radius 10 on a 100x100 glyph, this is 21x more work than necessary.

**Fix**: Use a sliding-window (prefix-sum) approach for O(W*H) regardless of radius. Maintain a running sum, add entering value, subtract leaving value.

### Perf 6: No ArrayPool for glyph/atlas buffers

**Files**: Many locations across `FreeTypeRasterizer.cs`, `AtlasBuilder.cs`, `ChannelPackedAtlasBuilder.cs`, `ChannelCompositor.cs`, `GlyphCompositor.cs`, `OutlineEffect.cs`, `ShadowEffect.cs`, `OutlinePostProcessor.cs`, `ShadowPostProcessor.cs`, `GradientEffect.cs`

Dozens of `new byte[]` allocations per glyph across effects, compositing, and atlas building. Atlas pages are 1-16 MB LOH allocations that fragment the heap.

**Fix**: Use `ArrayPool<byte>.Shared.Rent()` / `Return()` for temporary buffers. Consider making `RasterizedGlyph` implement `IDisposable` for pooled buffer lifetime.

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
