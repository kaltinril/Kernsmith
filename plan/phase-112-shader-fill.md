# Phase 112 — Shader Fill for Glyphs (companion to Phase 111: Texture Fill)

> **Status**: Deferred / speculative — not started. No `ShaderFillEffect` or `IGlyphShader` exists in `src/KernSmith/Rasterizer/`. The design content below is intact for if/when this is picked up.
> **Created**: 2026-06-20
> **Companion to**: [Phase 111 — Texture Fill](phase-111-texture-fill.md) (texture fill *samples* an image per pixel; shader fill *computes* color per pixel). They share the same fill-stage plumbing.
> **Depends on**: Core rasterizer + effects pipeline
> **Framing (locked)**: **Bake-time / generation-time, pure C#.** The shader fill is a managed per-pixel function evaluated once during glyph compositing and *burned into* the output `.png`/`.tga`/`.dds` atlas. There is no GPU, no runtime, and no GLSL/HLSL execution in this phase. (Real shader *source* and runtime evaluation are a separate, deferred future track — see *Deferred Future Track*.)
> **Goal**: Allow glyph interiors to be filled by a managed, CPU-evaluated per-pixel function — a "shader" in the fragment-shader sense — so glyph color can be procedurally computed (gradients beyond two-stop, noise, patterns, stripes, checker, SDF-driven coloring, metal/poison/mold/burn looks) instead of sampled from an image or limited to flat/linear-gradient fills.

---

## Background

KernSmith is a **CPU rasterizer that bakes static bitmap atlases**. It has no GPU and no runtime: every effect runs once at generation time and is burned into the output `.png`/`.tga`/`.dds`. So a "shader" here cannot mean a GLSL/HLSL program executed on a GPU. Instead it means a **managed per-pixel fill function evaluated on the CPU during glyph compositing** — the moral equivalent of a fragment shader, called once per glyph pixel.

KernSmith currently supports these fill modes for glyphs:
- **White/flat**: Default — solid white (the game engine tints at runtime)
- **Gradient**: Two-color linear gradient via `GradientPostProcessor` / `GradientEffect`
- **Outline + fill**: Outline color with inner fill via `OutlinePostProcessor` / `OutlineEffect`

Phase 111 adds a fourth: **texture fill**, where the glyph's alpha acts as a mask and each glyph pixel is replaced by a *sampled* texture pixel. Shader fill is the natural sibling: same mask, same insertion point, but the per-pixel color is *computed* from inputs (normalized glyph UV, pixel coords, coverage/alpha, distance-to-edge) rather than read from an image. Where texture fill answers "what color is the image here?", shader fill answers "what color should this pixel be, given where it is in the glyph?".

This generalizes the existing gradient path. `GradientPostProcessor.Process` already walks every pixel, computes a normalized parameter `t`, and writes an RGBA color with `alpha = glyph.BitmapData[srcIdx]`. A two-stop linear gradient is just one possible per-pixel function. Shader fill exposes that per-pixel step as a user-supplied (or preset) delegate.

## Proposed Capabilities

### 1. Shader Fill Effect

A new fill that composites a **computed** color into the glyph shape, plugging into the pipeline exactly where texture fill does (see *Relationship to Texture Fill*).

- **Input**: A per-pixel fill function plus configuration (seed, scale, etc.)
- **Behavior**: The glyph's alpha channel is the mask. For every covered pixel the function is invoked with a context describing that pixel; the returned color is written, and the original alpha (coverage) is preserved.
- **Output**: A grayscale glyph promoted to RGBA, identical in shape, recolored by the function — the same grayscale-to-RGBA promotion `GradientPostProcessor` already performs.

### 2. The Shader Context (inputs)

The function receives a small read-only context per pixel. Everything in it is already available where gradient/texture fill run, so nothing new has to be plumbed through the rasterizer:

- **Normalized glyph UV** — `(U, V)` in `[0,1]` across the glyph's rendered box (the same projection `GradientEffect` does over `width`/`height`)
- **Pixel coordinates** — integer `(X, Y)` within the glyph bitmap, plus `Width`/`Height`
- **Coverage / alpha** — the source anti-aliased mask value at this pixel (`glyph.BitmapData[srcIdx]`), so the function can fade, threshold, or ignore it
- **Distance-to-edge (SDF)** — *optional*; only meaningful when the glyph was rasterized with `RasterOptions.Sdf` (then `BitmapData` already encodes signed distance via `SdfSpread`). Lets a fill color by distance from the contour (e.g. inner glow, banded outlines)
- **Seed** — a deterministic integer/float for reproducible noise and pattern variation (see Open Questions on determinism)
- **Glyph metrics & codepoint** — from `GlyphMetrics` / `RasterizedGlyph.Codepoint`, so a fill can vary per glyph (e.g. checker offset by codepoint)

Deliberately **not** included: wall-clock time, frame index, or any animated input — atlases are static (see *Out of Scope*).

### 3. Built-in Catalog + Registry Extension Point

Delivery is a **built-in catalog of named effects** plus a **C# registry** so callers can register their own. Both are convenience layers over one mechanism: "a function from context to color".

**Built-in catalog** — start with looks that bake well as a *static* image (no animation needed):

- **gradient** — N color stops, linear or radial (beyond the current two-stop limit)
- **metal** — banded value ramp for a brushed/polished-metal look
- **poison** — sickly green mottle driven by seeded noise
- **mold** — organic blotch noise (pairs with the *bounds-changing* spill described below)
- **burn / eaten** — charred/erosion look (the *bounds-changing*, coverage-shrinking case below)
- **noise** — seeded, scale-controlled value noise / plasma
- **stripes / bands**, **checker** — angled bands or cells, two colors
- **sdf-bands** — concentric color rings driven by distance-to-edge (requires SDF input)

**Registry extension point** — mirror the existing rasterizer-backend registry `RasterizerFactory.Register` (`src/KernSmith/Rasterizer/RasterizerFactory.cs`): a thread-safe `Register(name, factory)` that resolves a name to an `IGlyphShader`. The same trimming/AOT posture applies — keep built-in name resolution a static `switch` (not reflection), and let consumers `Register` their own custom `IGlyphShader` explicitly:

```csharp
// Illustrative — mirrors RasterizerFactory.Register
GlyphShaderRegistry.Register("my-effect", () => new MyGlyphShader(...));
```

A "drop your shader source in a folder" authoring model is **not** part of this phase — it belongs to the deferred shader-source/runtime track (see *Deferred Future Track*).

### 4. Blending with Existing Effects

Shader fill is a body/fill layer, so it composes with the rest of the chain the same way texture and gradient fills do:

- **Shader + Outline** — computed interior with a solid (`OutlinePostProcessor`) border
- **Shader + Shadow** — computed glyph casting a `ShadowPostProcessor` shadow
- **Shader over Texture (or vice-versa)** — run a texture fill, then a shader fill in "compute on top of / blend with the sampled pixel" mode. Because both stages share the fill plumbing, a shader function could even take the already-sampled texture pixel as one of its inputs and modulate it (tint, posterize, SDF-mask). This is the natural place texture and shader fill meet.

### 5. Configuration

```csharp
// Proposed options shape (illustrative — not implemented)
ShaderFillOptions
{
    IGlyphShader Shader     // the per-pixel function (preset or user-supplied)
    // or, for the lightweight path:
    Func<ShaderContext, (byte R, byte G, byte B, byte A)> Fill

    int   Seed              // deterministic seed for noise/patterns
    float ScaleX, ScaleY    // feature scale in glyph/UV space
    float OffsetX, OffsetY  // origin shift in UV space
    bool  UseCoverageAsAlpha // multiply returned A by source coverage (default true)
}
```

## Architecture Considerations

### Implementation: post-processor vs. effect

Two equally valid hook points, matching the two real contracts and exactly mirroring how gradient fill exists in both forms today:

- **`IGlyphPostProcessor`** (public) — like `GradientPostProcessor`. `Process(RasterizedGlyph)` skips already-RGBA glyphs, loops every pixel, invokes the fill function, and returns a new RGBA `RasterizedGlyph` (`Pitch = Width * 4`, `Format = PixelFormat.Rgba32`). Simplest to expose publicly and to chain.
- **`IGlyphEffect`** (internal) — like `GradientEffect` (ZOrder `2`, the body layer). `Generate(alphaData, width, height, pitch, metrics)` returns a `GlyphLayer` the `GlyphCompositor` blends back-to-front. Use this if shader fill needs to participate in layered compositing alongside outline/shadow layers.

Shader fill should slot in at the **same insertion point Phase 111 chooses for texture fill** (Phase 111 proposes the body/base layer, equivalent to `GradientEffect` ZOrder 2). Both are "the interior fill" and are mutually exclusive *as the base layer*, though one can run on top of the other (see capability 4).

### The per-pixel loop

The shape is identical to `GradientPostProcessor.Process` — only the `t → color` line changes from a gradient lerp to a function call:

```csharp
// Inside Process / Generate, per pixel (illustrative):
for (var y = 0; y < height; y++)
for (var x = 0; x < width; x++)
{
    var srcIdx = y * pitch + x;
    var coverage = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;

    var ctx = new ShaderContext(
        U: width  > 1 ? (float)x / (width  - 1) : 0f,
        V: height > 1 ? (float)y / (height - 1) : 0f,
        X: x, Y: y, Width: width, Height: height,
        Coverage: coverage,
        Seed: seed,
        Metrics: metrics,
        Codepoint: codepoint);

    var (r, g, b, a) = shader.Evaluate(ctx);

    var dstIdx = (y * width + x) * 4;
    rgba[dstIdx + 0] = r;
    rgba[dstIdx + 1] = g;
    rgba[dstIdx + 2] = b;
    // Preserve anti-aliased coverage by default (matches gradient/texture fill).
    rgba[dstIdx + 3] = useCoverageAsAlpha ? (byte)(a * coverage / 255) : a;
}
```

A tiny example fill (diagonal two-color stripes):

```csharp
// IGlyphShader.Evaluate, or a Func<ShaderContext, (byte,byte,byte,byte)>:
var band = (int)((ctx.U + ctx.V) * 8) & 1;        // 8 stripes across the glyph
return band == 0 ? (255, 215, 0, 255)             // gold
                 : (140, 100, 0, 255);            // dark gold
```

### Coordinate mapping

Reuse the corner-projection / normalization `GradientEffect` already performs. `(U, V)` come straight from that. `ScaleX/Y` and `OffsetX/Y` transform UV before the function sees it (same idea as the gradient's `scale`/`offset` knobs), so presets like noise and checker can be zoomed/shifted without bespoke math.

### Color types

KernSmith does not have a `Color` struct — existing effects pass plain `byte R, G, B` (see `GradientPostProcessor`, `OutlinePostProcessor`, `ShadowPostProcessor`). Shader fill should follow that: the function returns a `(byte R, byte G, byte B, byte A)` tuple (or a tiny internal `struct`), **not** a new public color type, to stay consistent and avoid surface-area churn.

## Bounds & Metrics Correctness

Most shader fills only *recolor* — but some (glow, fire, mold, burn/erosion) change the glyph's footprint. Getting the bounds and BMFont char-block metrics right is the load-bearing part of this phase. KernSmith already solves this exact problem for outline and shadow; bounds-changing shader fills must reuse that machinery rather than invent their own.

### Two classes of shader fill

- **Recolor-only** — fills *within* the existing alpha mask. The drawn shape is unchanged; only color changes. This is exactly how `GradientEffect`/`GradientPostProcessor` behave today (same `width`/`height`, `OffsetX:0`/`OffsetY:0`, ZOrder 2). No bounds change, no metric change. Most catalog entries (gradient, metal, poison, stripes, checker, noise, sdf-bands) are recolor-only.
- **Bounds-changing** — the look spills *out* (glow/fire/mold) or chews *in* (erosion / "eaten" / burn). A spill draws pixels beyond the original glyph box; an erosion removes coverage. Only spill changes the box; erosion stays within it (or trims).

### How a bounds-changing layer is registered (the real mechanism)

This is already implemented for outline and shadow — the verified path:

1. **An effect that grows the footprint emits a larger layer with a negative offset.** `OutlineEffect.Generate` (`src/KernSmith/Rasterizer/OutlineEffect.cs`) builds a bitmap of `dstW = width + 2*ow`, `dstH = height + 2*ow` and returns `new GlyphLayer(dst, dstW, dstH, OffsetX: -ow, OffsetY: -ow, ZOrder)`. `ShadowEffect.Generate` (`src/KernSmith/Rasterizer/ShadowEffect.cs`) does the same per-side: `dstW = width + expandLeft + expandRight`, returning `OffsetX: -expandLeft, OffsetY: -expandTop`. The `GlyphLayer` record (`IGlyphEffect.cs`) carries `OffsetX`/`OffsetY` *relative to the original glyph origin*.
2. **`GlyphCompositor.Composite` unions all layer bounds.** It seeds the union with the original glyph box (`minX=0, minY=0, maxX=srcW, maxY=srcH`) then, per layer, `minX = Min(minX, layer.OffsetX)` / `maxX = Max(maxX, layer.OffsetX + layer.Width)` (and Y likewise). `canvasW = maxX - minX`, `canvasH = maxY - minY`. A negative `OffsetX`/`OffsetY` is what grows the canvas to the left/up; a layer wider than the glyph grows it right/down.
3. **The composited glyph's metrics are re-registered against the new canvas.** Still in `Composite`: the original glyph stays registered because the bearings absorb the canvas growth — `BearingX: metrics.BearingX + minX`, `BearingY: metrics.BearingY - minY`, `Width: canvasW`, `Height: canvasH`, and crucially **`Advance: metrics.Advance` (unchanged)**.

So a bounds-changing shader fill needs no new plumbing: it implements `IGlyphEffect`, sizes its output bitmap to include the spill, and reports the per-side growth via the layer's `OffsetX`/`OffsetY`. The compositor, packer, and char-block code already do the rest.

### The spacing rule: `xadvance` stays put, the quad grows

This is the key correctness invariant, and it holds in the real code. The BMFont char block is built in `BmFontModelBuilder` (`src/KernSmith/Output/BmFontModelBuilder.cs`) from the *composited* glyph's metrics:

- `XOffset = glyph.Metrics.BearingX` (then minus `pad.Left`)
- `YOffset = baseLine - glyph.Metrics.BearingY` (then minus `pad.Up`)
- `Width = glyph.Width + pad.Left + pad.Right`, `Height = glyph.Height + pad.Up + pad.Down`
- `XAdvance = glyph.Metrics.Advance + advanceAdjustX`

Because the compositor leaves `Advance` untouched while folding canvas growth into `BearingX`/`BearingY`/`Width`/`Height`, a *visual* effect grows the drawn quad (bigger `width`/`height`, shifted `xoffset`/`yoffset`) while **`xadvance` — the layout advance that drives text spacing — is unchanged**. The original glyph stays registered at the same pen position; the effect simply extends into the margin. This is precisely what already happens for outline and shadow.

Changing `xadvance` to "make room" for a visual effect would disturb line layout and is therefore **never the default** — it would be an explicit opt-in (and even then is usually wrong for a purely visual fill).

### Atlas padding / spacing

When an effect spills, the larger composited cell is what gets packed — but adjacent cells in the atlas must not bleed into one another. The atlas **padding/spacing** must accommodate the growth so a glow/fire/mold halo doesn't leak into the neighbor's cell. **Channel packing makes this worse**: glyphs sharing a cell across R/G/B/A are spatially overlapped, so any spill that exceeds the spacing contaminates a *different glyph on another channel*. A bounds-changing shader fill should declare its growth so the packer can budget spacing accordingly.

### Erosion is the mirror image

Erosion ("eaten" / burn) *removes* coverage rather than adding it. It shrinks the visible shape but should generally **keep the original bounding box** (or trim conservatively) so registration stays stable — and, like every visual effect here, it **keeps `xadvance` intact**. A glyph that's been partly chewed away still occupies the same layout advance.

### Proposed shape

A shader effect declares its per-side **margin growth (or shrink)** (e.g. `MarginLeft/Right/Top/Bottom`, positive = spill out, negative = erode in). The effect renders into a bitmap sized to that margin and returns a `GlyphLayer` with the matching negative `OffsetX`/`OffsetY` — identical to how `OutlineEffect`/`ShadowEffect` already report growth. The existing `GlyphCompositor` → packer → `BmFontModelBuilder` machinery then handles registration, atlas placement, and char-block metrics automatically. No new compositor or metrics code is required — only the per-side margin declaration and a correctly-sized layer.

## CLI Integration

Presets map cleanly to flags; a fully custom function is API-only (you can't pass a delegate on a command line):

```
kernsmith generate -f MyFont.ttf -s 48 --shader-fill stripes --shader-colors FFD700,8C6400 --shader-scale 8
kernsmith generate -f MyFont.ttf -s 48 --shader-fill checker --shader-seed 42
kernsmith generate -f MyFont.ttf -s 48 --shader-fill noise   --shader-scale 0.25 --shader-seed 7
```

## Relationship to Texture Fill (Phase 111)

Texture fill and shader fill are two implementations of one idea — **fill the glyph mask with a computed-or-sampled color** — and should share plumbing:

| | Texture Fill (111) | Shader Fill (112) |
|---|---|---|
| Per-pixel color source | **Sampled** from an image | **Computed** by a function |
| Primary input | Texture image + mapping mode | `ShaderContext` (UV, coords, coverage, SDF, seed) |
| Hook point | Body/base fill layer | Same body/base fill layer |
| Memory | Holds decoded texture buffer | None (pure function) |
| AOT/trim | Image decoders | Delegate dispatch (see Open Questions) |
| Determinism | Image is fixed | Seed-driven; must be reproducible |

They can **compose**: a shader function can take the texture-sampled pixel as an input and modulate it (tint, posterize, SDF-mask the sample). If implemented, both should share the same fill-stage interface so the two can be swapped or stacked rather than duplicated.

## Open Questions

1. **CPU delegate vs. real GPU shader source — RESOLVED in favor of CPU C#.** This phase is bake-time, managed C# only (see *Framing* and *Deferred Future Track*). Accepting and *executing* GLSL/HLSL source is out of scope for a CPU atlas baker; the runtime/shader-source idea is recorded as a separate future track, not an open decision here.
2. **Presets vs. user-delegate-only.** Built-in presets (stripes, checker, noise, SDF bands) make the feature usable without code and CLI-addressable, but every preset is surface area to test and document. Ship presets, or expose only a single user delegate and let callers bring their own?
3. **Determinism / seeding.** Atlases must be byte-reproducible across runs and machines. Any noise/pattern preset needs a fixed, platform-independent PRNG (not `System.Random`'s unspecified sequence) keyed off `Seed` (+ optionally codepoint). Which algorithm, and is per-codepoint variation desirable or surprising?
4. **Performance of per-pixel managed delegates.** A delegate invocation per glyph pixel, across a full atlas, is far more call overhead than the inlined gradient loop. Is that acceptable for a one-time bake? Do we need a non-delegate fast path (e.g. presets implemented as concrete `IGlyphShader` classes the JIT can devirtualize) for large charsets / supersampling?
5. **AOT / trim friendliness.** KernSmith targets `net8.0;net10.0` and cares about trimming. User-supplied `Func<>`/delegates are fine, but reflection-driven preset lookup (e.g. resolving a `--shader-fill` name to a type) could trip trimming/AOT. Keep preset resolution a static `switch`, not reflection?
6. **SDF coupling.** The distance-to-edge input is only valid when the glyph was rendered with `RasterOptions.Sdf`. Should SDF-driven presets hard-require SDF mode (error if absent), silently no-op, or fall back to coverage? Mixing SDF fills with non-SDF atlases is an easy footgun.
7. **Color font interaction.** Like gradient/texture fill, shader fill should skip glyphs already in `PixelFormat.Rgba32` (color emoji / COLR / sbix). Confirmed by `GradientPostProcessor` skipping RGBA — same guard here. Override, blend, or skip? (Skip is the consistent default.)

## Deferred Future Track — runtime user-supplied shaders (NOT this phase)

A separate, later idea: let users supply *real* shader source (HLSL) and apply it to text **at runtime** in the Gum integrations (MonoGame/KNI/FNA, incl. WASM), rather than baking a static look. This is explicitly **out of scope for Phase 112** and recorded here only so the boundary is clear:

- **ShadowDusk** (the author's separate repo) is a *runtime* cross-platform HLSL→`.mgfx` compiler for MonoGame/KNI/FNA. It compiles shaders for *GPU execution at runtime*.
- Baking a static atlas needs the opposite tool: a **CPU evaluator at generation time**, not a runtime GPU compiler. Phase 112 burns the result into the PNG; nothing ships a shader to a GPU.
- So ShadowDusk fits a possible *future, separate* "runtime user-supplied shaders on text" feature living in the **Gum integrations** (`integrations/`), not in this bake-time phase. The "drop your shader source in a folder" authoring model also belongs to that track, not here.

Keeping these apart avoids conflating "compute a color in C# and bake it" (this phase) with "ship HLSL to a GPU and run it live" (the deferred track).

## Non-Goals (for this phase)

- **Executing GLSL/HLSL** (or any GPU shading language) — KernSmith has no GPU and no shader compiler; only managed CPU functions are in scope. Real shader source + runtime evaluation is the *Deferred Future Track* above.
- **GPU rendering** of any kind.
- **Runtime or animated atlases** — no time/frame inputs, no sprite-sheet cycling; output is a single static baked atlas. (Animation belongs to the consuming engine.)
- **Procedural *texture* export** — generating a standalone procedural image file (vs. filling glyphs) is a different feature.
- **3D / lighting** (normal maps, bump, environment shading on glyphs).
