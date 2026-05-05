# Phase 105: Text Layout Engine (Core) + Framework Rendering Examples

**Status:** Ready — pixel format decision resolved (see below)
**Date:** 2026-04-10

## Resolved Decision: Pixel format helpers on `AtlasPage`

**Decision:** Keep raw pixel format auto-detection (current behavior). Add **on-demand format helpers** to `AtlasPage` so consumers can request a specific layout when they need it.

**Why this approach.** Raw rasterization is grayscale across virtually every backend (FreeType, stb_truetype, GDI, DirectWrite — all default to 8-bit alpha coverage). Color fonts and effect-laden glyphs (outline, shadow, gradient, channel packing) are the only natural sources of RGBA pixels. The current pipeline already auto-detects this — atlases stay grayscale until something genuinely needs more channels. Forcing RGBA everywhere wastes memory; forcing grayscale loses information for effects. **The right move is to keep the raw format until someone asks for the glyph in a specific form.**

This was the principle that resolved the original ambiguity: KernSmith's grayscale bytes have meaning (alpha coverage of a rasterized glyph), but the API never exposed that meaning. The fix isn't to change what bytes are produced — it's to add intent-declaring accessors so consumers don't have to guess.

**API to add on `AtlasPage`:**

```csharp
/// <summary>
/// Returns pixel data as RGBA32 bytes regardless of the page's native format.
/// Grayscale pages expand to (255, 255, 255, v) — the canonical Angelcode BMFont
/// alpha-coverage layout (see REF-05). RGBA pages return PixelData unchanged.
/// </summary>
public byte[] GetRgbaPixelData();

/// <summary>
/// Returns pixel data as 8-bit alpha coverage bytes regardless of the page's
/// native format. RGBA pages collapse to the alpha channel only. Grayscale pages
/// return PixelData unchanged.
/// </summary>
public byte[] GetAlpha8PixelData();
```

**What this enables:**
- **Phase 105 sample code** becomes a clean two-liner: `tex.SetData(page.GetRgbaPixelData())` — works for both formats with no `if (page.Format == ...)` branching
- **UI cleanup** — [apps/KernSmith.Ui/Layout/PreviewPanel.cs](apps/KernSmith.Ui/Layout/PreviewPanel.cs) `IsRgba` branching collapses; the "two interpretations of grayscale" bug disappears because `GetRgbaPixelData()` has exactly one defined behavior
- **SDF / channel-packed / memory-constrained users** keep using `PixelData` + `Format` directly — no breaking change, no waste
- **Channel metadata in BMFont output** can finally be truthful — when the consumer asks for RGBA, the bytes match `alphaChnl=0, redChnl=4, greenChnl=4, blueChnl=4`

**Scope:** ~30 LOC implementation + tests. No public API breaking changes. No changes to `BmFont.Generate` output behavior. Grayscale stays grayscale on disk; the helpers convert on demand.

**Sequencing:** Implement these helpers as a small pre-cursor to Phase 105 (separate branch / commit). Once landed, Phase 105's sample code is correct as-drafted. The UI cleanup and integration README updates can use the helpers right away.

**Options considered but rejected:**

| Option | Why rejected |
|--------|-------------|
| **A. Flip default to RGBA** | 4× memory waste for the common plain-text case. Inverts the natural rasterizer output. Forces everyone to pay for the RGBA option. |
| **B. Force grayscale default with helpers** | Doesn't address effects/color-font cases that genuinely need RGBA. Same code complexity as the chosen approach. |
| **C-alt. Semantic `PixelFormat` enum (`Alpha8`/`Luminance8`/`Rgba32`)** | Public API breaking change. Forces every consumer that reads `PixelFormat` to update. Helpers achieve the same disambiguation without breakage. |

**The chosen approach (let's call it Option C for continuity with prior discussions) is the minimal-blast-radius fix that resolves the ambiguity at the consumer boundary, not deep in the pipeline.**

---

## Problem

KernSmith generates complete BMFont data — glyph atlas textures, source rectangles, offsets, advances, and kerning pairs — but stops there. To actually **render text**, every consumer must:

1. Build a `char → CharEntry` lookup dictionary
2. Iterate characters, apply kerning, advance the cursor
3. Handle line breaks and text measurement
4. Draw textured quads using their framework's sprite batch

This is 30-50 lines of boilerplate that every MonoGame, Raylib, FNA, or raw OpenGL user must write from scratch. The only existing text rendering integration is `KernSmith.MonoGameGum`, which is **Gum-specific** — it feeds data into Gum's `BitmapFont` class and can't be used without Gum.

### The NuGet package problem

The repo already ships **8 NuGet packages**. Adding per-framework rendering packages (`KernSmith.Rendering.MonoGame`, `KernSmith.Rendering.Raylib`, `KernSmith.Rendering.FNA`, etc.) would balloon this further. Each framework adapter is ~10 lines of actual framework-specific code — not enough to justify a NuGet package.

## Solution

Put the **text layout engine in core `KernSmith`** (zero new packages). The layout engine does all the hard work — glyph lookup, cursor advancement, kerning, measurement, line handling — and outputs a list of **positioned glyphs** that any framework can render with a trivial loop.

### Design

```
BmFontResult
  └── .CreateTextRenderer()  →  TextRenderer (cached glyph lookup + kerning map)
        ├── .LayoutText(string)         →  IReadOnlyList<GlyphLayout>
        ├── .LayoutText(string, float maxWidth)  →  IReadOnlyList<GlyphLayout>  (word wrap)
        ├── .MeasureString(string)      →  (float Width, float Height)
        ├── .LineHeight                 →  int
        └── .Base                       →  int
```

**`GlyphLayout`** — a simple struct describing one positioned glyph:

```csharp
namespace KernSmith.Output;

/// <summary>
/// A single glyph positioned for rendering. Framework-agnostic —
/// use SourceX/Y/Width/Height as the source rectangle from the atlas
/// texture identified by Page, and draw it at DestX/DestY.
/// </summary>
public readonly record struct GlyphLayout(
    int Codepoint,
    int Page,
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    float DestX,
    float DestY);
```

**`TextRenderer`** — the cached layout engine:

```csharp
namespace KernSmith.Output;

/// <summary>
/// Lays out text using a BmFontModel's glyph metrics and kerning data.
/// Create once from a BmFontResult, reuse for multiple DrawString calls.
/// </summary>
public sealed class TextRenderer
{
    // Internals: Dictionary<int, CharEntry> for O(1) glyph lookup,
    // Dictionary<(int,int), int> for kerning pair lookup,
    // LineHeight/Base from CommonBlock.

    public int LineHeight { get; }
    public int Base { get; }

    public IReadOnlyList<GlyphLayout> LayoutText(string text);
    public IReadOnlyList<GlyphLayout> LayoutText(string text, float maxWidth);
    public (float Width, float Height) MeasureString(string text);
}
```

### Entry point

```csharp
// Extension method on BmFontResult — natural discovery
public static TextRenderer CreateTextRenderer(this BmFontResult result);

// Also available from a BmFontModel directly (for BmFontReader users who loaded from disk)
public static TextRenderer CreateTextRenderer(this BmFontModel model);
```

### Framework rendering — no packages, just a loop

The entire point is that rendering becomes a trivial `foreach` in any framework:

**MonoGame / FNA / KNI:**
```csharp
var result = BmFont.Generate(fontData);
var renderer = result.CreateTextRenderer();
var glyphs = renderer.LayoutText("Hello, World!");

// In Draw():
spriteBatch.Begin(blendState: BlendState.AlphaBlend);
foreach (var g in glyphs)
{
    spriteBatch.Draw(
        textures[g.Page],
        new Vector2(g.DestX, g.DestY),
        new Rectangle(g.SourceX, g.SourceY, g.SourceWidth, g.SourceHeight),
        Color.White);
}
spriteBatch.End();
```

**Raylib-cs:**
```csharp
foreach (var g in glyphs)
{
    Raylib.DrawTextureRec(
        textures[g.Page],
        new Rectangle(g.SourceX, g.SourceY, g.SourceWidth, g.SourceHeight),
        new Vector2(g.DestX, g.DestY),
        Color.White);
}
```

**Raw OpenGL / Silk.NET / any framework:**
```csharp
foreach (var g in glyphs)
{
    // bind textures[g.Page], draw quad at (g.DestX, g.DestY) with UV from source rect
}
```

The framework-specific code is **4-8 lines** — not a NuGet package, just a code example in the README or a sample project.

## Implementation

### Files to create

| File | Purpose |
|------|---------|
| `src/KernSmith/Output/GlyphLayout.cs` | `readonly record struct` — positioned glyph data |
| `src/KernSmith/Output/TextRenderer.cs` | Layout engine — glyph lookup, kerning, cursor, measurement, word wrap |
| `src/KernSmith/Output/TextRendererExtensions.cs` | `CreateTextRenderer()` extension methods on `BmFontResult` and `BmFontModel` |
| `tests/KernSmith.Tests/Output/TextRendererTests.cs` | Unit tests for layout, measurement, kerning, word wrap, missing glyphs |

### Files to modify

| File | Change |
|------|--------|
| `samples/KernSmith.Samples.Minimal/Game1.cs` | Replace atlas-only display with actual text rendering using `TextRenderer` |

### No files to create in `integrations/`

This is the key decision. No new integration packages. The rendering loop is framework-specific boilerplate that belongs in samples and docs, not in a library.

### TextRenderer internals

```csharp
public sealed class TextRenderer
{
    private readonly Dictionary<int, CharEntry> _glyphs;
    private readonly Dictionary<long, int> _kerning; // key = (first << 32) | second
    private readonly int _lineHeight;
    private readonly int _base;

    internal TextRenderer(BmFontModel model)
    {
        _glyphs = new Dictionary<int, CharEntry>(model.Characters.Count);
        foreach (var c in model.Characters)
            _glyphs[c.Id] = c;

        _kerning = new Dictionary<long, int>(model.KerningPairs.Count);
        foreach (var k in model.KerningPairs)
            _kerning[((long)k.First << 32) | (uint)k.Second] = k.Amount;

        _lineHeight = model.Common.LineHeight;
        _base = model.Common.Base;
    }

    public int LineHeight => _lineHeight;
    public int Base => _base;

    public IReadOnlyList<GlyphLayout> LayoutText(string text)
    {
        var result = new List<GlyphLayout>(text.Length);
        float cursorX = 0;
        float cursorY = 0;
        int prevCodepoint = -1;

        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            if (codepoint == '\n')
            {
                cursorX = 0;
                cursorY += _lineHeight;
                prevCodepoint = -1;
                continue;
            }

            if (codepoint == '\r')
            {
                continue; // strip carriage returns
            }

            if (codepoint == '\t')
            {
                // Advance by 4x space width
                if (_glyphs.TryGetValue(' ', out var spaceEntry))
                    cursorX += spaceEntry.XAdvance * 4;
                prevCodepoint = -1;
                continue;
            }

            if (!_glyphs.TryGetValue(codepoint, out var entry))
            {
                if (codepoint != '?' && _glyphs.TryGetValue('?', out entry))
                {
                    // Use '?' as replacement for missing glyphs
                }
                else
                {
                    continue; // skip if no replacement available
                }
            }

            // Apply kerning
            if (prevCodepoint >= 0)
            {
                long key = ((long)prevCodepoint << 32) | (uint)codepoint;
                if (_kerning.TryGetValue(key, out int amount))
                    cursorX += amount;
            }

            if (entry.Width > 0 && entry.Height > 0)
            {
                result.Add(new GlyphLayout(
                    codepoint,
                    entry.Page,
                    entry.X, entry.Y,
                    entry.Width, entry.Height,
                    cursorX + entry.XOffset,
                    cursorY + entry.YOffset));
            }

            cursorX += entry.XAdvance;
            prevCodepoint = codepoint;
        }

        return result;
    }

    public IReadOnlyList<GlyphLayout> LayoutText(string text, float maxWidth)
    {
        if (maxWidth <= 0)
            return LayoutText(text);

        var result = new List<GlyphLayout>(text.Length);
        float cursorX = 0;
        float cursorY = 0;
        int prevCodepoint = -1;
        int lastSpaceIndex = -1;      // index into result list
        float cursorXAtLastSpace = 0; // cursorX right after the space glyph

        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            if (codepoint == '\n')
            {
                cursorX = 0;
                cursorY += _lineHeight;
                prevCodepoint = -1;
                lastSpaceIndex = -1;
                continue;
            }

            if (codepoint == '\r')
            {
                continue; // strip carriage returns
            }

            if (codepoint == '\t')
            {
                // Advance by 4x space width; treat as word boundary
                if (_glyphs.TryGetValue(' ', out var spaceEntry))
                    cursorX += spaceEntry.XAdvance * 4;
                prevCodepoint = -1;
                lastSpaceIndex = result.Count;
                cursorXAtLastSpace = cursorX;
                continue;
            }

            if (!_glyphs.TryGetValue(codepoint, out var entry))
            {
                if (codepoint != '?' && _glyphs.TryGetValue('?', out entry))
                {
                    // Use '?' as replacement for missing glyphs
                }
                else
                {
                    continue; // skip if no replacement available
                }
            }

            if (prevCodepoint >= 0)
            {
                long key = ((long)prevCodepoint << 32) | (uint)codepoint;
                if (_kerning.TryGetValue(key, out int amount))
                    cursorX += amount;
            }

            // Check if this glyph would exceed maxWidth
            float glyphRight = cursorX + entry.XOffset + entry.Width;
            if (cursorX > 0 && glyphRight > maxWidth)
            {
                if (lastSpaceIndex >= 0)
                {
                    // Word wrap: reflow glyphs after last space to next line
                    cursorY += _lineHeight;
                    float offsetX = cursorXAtLastSpace;
                    for (int j = lastSpaceIndex; j < result.Count; j++)
                    {
                        var g = result[j];
                        result[j] = g with
                        {
                            DestX = g.DestX - offsetX,
                            DestY = g.DestY + _lineHeight
                        };
                    }
                    cursorX -= offsetX;
                }
                else
                {
                    // No word boundary — hard break at character level
                    cursorX = 0;
                    cursorY += _lineHeight;
                }
                lastSpaceIndex = -1;
                prevCodepoint = -1;
            }

            if (entry.Width > 0 && entry.Height > 0)
            {
                result.Add(new GlyphLayout(
                    codepoint, entry.Page,
                    entry.X, entry.Y, entry.Width, entry.Height,
                    cursorX + entry.XOffset, cursorY + entry.YOffset));
            }

            cursorX += entry.XAdvance;
            prevCodepoint = codepoint;

            // Track word boundaries AFTER advancing past the space
            if (codepoint == ' ')
            {
                lastSpaceIndex = result.Count;
                cursorXAtLastSpace = cursorX;
            }
        }

        return result;
    }

    public (float Width, float Height) MeasureString(string text)
    {
        float cursorX = 0;
        float maxX = 0;
        float maxRight = 0; // tracks glyph overhang past XAdvance
        float cursorY = 0;
        int prevCodepoint = -1;

        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            if (codepoint == '\n')
            {
                if (cursorX > maxX) maxX = cursorX;
                cursorX = 0;
                cursorY += _lineHeight;
                prevCodepoint = -1;
                continue;
            }

            if (codepoint == '\r')
            {
                continue; // strip carriage returns
            }

            if (codepoint == '\t')
            {
                // Advance by 4x space width
                if (_glyphs.TryGetValue(' ', out var spaceEntry))
                    cursorX += spaceEntry.XAdvance * 4;
                prevCodepoint = -1;
                continue;
            }

            if (!_glyphs.TryGetValue(codepoint, out var entry))
            {
                if (codepoint != '?' && _glyphs.TryGetValue('?', out entry))
                {
                    // Use '?' as replacement for missing glyphs
                }
                else
                {
                    continue; // skip if no replacement available
                }
            }

            if (prevCodepoint >= 0)
            {
                long key = ((long)prevCodepoint << 32) | (uint)codepoint;
                if (_kerning.TryGetValue(key, out int amount))
                    cursorX += amount;
            }

            // Track glyph overhang (italic/negative right-side bearing)
            float right = cursorX + entry.XOffset + entry.Width;
            if (right > maxRight) maxRight = right;

            cursorX += entry.XAdvance;
            prevCodepoint = codepoint;
        }

        if (cursorX > maxX) maxX = cursorX;
        return (Math.Max(maxX, maxRight), cursorY + _lineHeight);
    }
}
```

### Edge cases to handle

| Case | Behavior |
|------|----------|
| Missing glyph | Replace with `?` glyph if available, else skip |
| Empty string | Return empty list / (0, LineHeight) for measure |
| Newline `\n` | Reset cursorX, advance cursorY by LineHeight |
| `\r\n` | `\r` stripped explicitly, `\n` triggers line break |
| `\r` (standalone) | Stripped (no visible output) |
| Tab `\t` | Advance by 4 × space XAdvance (if space exists), else skip; treated as word boundary for word wrap |
| Kerning pair not found | No adjustment (advance by XAdvance only) |
| Surrogate pairs (emoji) | Detected via `char.IsHighSurrogate`, composed via `char.ConvertToUtf32` |
| `maxWidth <= 0` | Falls through to unwrapped `LayoutText(string)` overload |

### Alternatives considered

**1. Per-framework NuGet packages (`KernSmith.Rendering.MonoGame`, etc.)**
Rejected. The repo already ships 8 NuGet packages. Each framework adapter is ~10 lines of actual framework-specific code — a `foreach` loop calling the framework's sprite draw method. That's sample/README material, not a library.

**2. `ITextureRenderer` interface that frameworks implement**
Rejected. An interface like `ITextureRenderer.DrawRegion(page, destX, destY, srcX, srcY, srcW, srcH)` hides control from the consumer. They can't set per-glyph color, apply transforms, skip glyphs, or do anything the interface didn't anticipate. The `foreach` loop over `GlyphLayout` gives full control with less code than implementing an interface + wiring it up.

**3. Leave it to consumers entirely**
Rejected. Without `TextRenderer`, every consumer must build their own glyph lookup dictionary, implement kerning, handle line breaks, and measure strings. That's 30-50 lines of boilerplate that's easy to get wrong (especially kerning). The layout engine is framework-agnostic and belongs in core.

**Chosen approach: `GlyphLayout` data contract.** The layout engine outputs positioned glyphs as plain data. Any framework renders them with a trivial `foreach` loop. The data contract IS the interface — no abstraction layer needed.

### What this does NOT include (intentionally)

- **Color per character** — that's a rendering concern, not a layout concern. The consumer controls color in their draw loop.
- **Text alignment** (center, right) — trivial to do with `MeasureString`: `destX = screenWidth/2 - measured.Width/2`. Not worth baking in.
- **Rich text / markup** — out of scope. Users can call `LayoutText` for each styled run and offset manually.
- **Caching / object pooling** — premature. `List<GlyphLayout>` allocations are fine for game text. Can optimize later if profiling shows a need.
- **SDF rendering** — SDF is a shader concern. The layout positions are identical; the consumer just uses a different shader/blend state.

## Testing

```
TextRendererTests
├── LayoutText_SingleCharacter_CorrectPosition
├── LayoutText_MultipleCharacters_AppliesXAdvance
├── LayoutText_WithKerning_AdjustsCursorPosition
├── LayoutText_Newline_WrapsToNextLine
├── LayoutText_MissingGlyph_ReplacedWithQuestionMark
├── LayoutText_MissingGlyph_NoQuestionMark_Skipped
├── LayoutText_EmptyString_ReturnsEmptyList
├── LayoutText_Tab_AdvancesByFourSpaces
├── LayoutText_SurrogatePair_ProducesCorrectGlyph
├── LayoutText_CarriageReturn_Stripped
├── LayoutText_StandaloneCarriageReturn_Stripped
├── LayoutText_WithMaxWidth_WrapsAtWordBoundary
├── LayoutText_WithMaxWidth_LongWord_BreaksAtCharacter
├── LayoutText_WithMaxWidth_WrappedLineStartsAtXZero
├── LayoutText_WithMaxWidth_MultipleWraps_AllLinesStartAtXZero
├── LayoutText_WithMaxWidth_ZeroWidth_DelegatesToUnwrapped
├── LayoutText_WithMaxWidth_NegativeWidth_DelegatesToUnwrapped
├── MeasureString_SingleLine_ReturnsCorrectDimensions
├── MeasureString_MultiLine_ReturnsMaxWidthAndTotalHeight
├── MeasureString_EmptyString_ReturnsZeroWidthLineHeight
├── MeasureString_MatchesLayoutBoundingBox
├── MeasureString_ItalicOverhang_IncludesGlyphExtent
├── LayoutText_WithMaxWidth_TabAsWordBoundary_WrapsCleanly
├── CreateTextRenderer_FromBmFontResult_Works
├── CreateTextRenderer_FromBmFontModel_Works
├── CreateTextRenderer_DuplicateCharEntries_LastWins
├── CreateTextRenderer_DuplicateKerningPairs_LastWins
```

## Sample update

Update `samples/KernSmith.Samples.Minimal/Game1.cs` to render actual text instead of just displaying the atlas:

```csharp
protected override void LoadContent()
{
    _spriteBatch = new SpriteBatch(GraphicsDevice);

    var fontData = File.ReadAllBytes(fontPath);
    var result = BmFont.Generate(fontData, new FontGeneratorOptions
    {
        Size = 32,
        Backend = RasterizerBackend.StbTrueType
    });

    // Create textures from atlas pages
    _textures = result.Pages.Select(page =>
    {
        var tex = new Texture2D(GraphicsDevice, page.Width, page.Height, false, SurfaceFormat.Color);
        tex.SetData(page.PixelData);
        return tex;
    }).ToArray();

    // Layout some text — this is the new API
    _renderer = result.CreateTextRenderer();
    _glyphs = _renderer.LayoutText("Hello from KernSmith!");
}

protected override void Draw(GameTime gameTime)
{
    GraphicsDevice.Clear(Color.CornflowerBlue);

    _spriteBatch.Begin(blendState: BlendState.AlphaBlend);
    foreach (var g in _glyphs)
    {
        _spriteBatch.Draw(
            _textures[g.Page],
            new Vector2(g.DestX + 20, g.DestY + 20),
            new Rectangle(g.SourceX, g.SourceY, g.SourceWidth, g.SourceHeight),
            Color.White);
    }
    _spriteBatch.End();

    base.Draw(gameTime);
}
```

## Rollout: Repository-Wide Updates

This phase touches the entire repo — not just the core implementation. Every doc, sample, integration, and tooling surface that references KernSmith's output model or shows usage examples must be updated to reflect the new `TextRenderer` API.

### README updates

| File | Change |
|------|--------|
| `README.md` (root) | Add "Text Layout Engine" to features list; add `TextRenderer` usage to Quick Start section |
| `src/KernSmith/README.md` | Add `CreateTextRenderer()` example to usage section |
| `tools/KernSmith.Cli/README.md` | Document new `measure` command (if added) |
| `samples/KernSmith.Samples/README.md` | Add fifth scenario: text layout with `TextRenderer` |
| `samples/KernSmith.Samples.Minimal/README.md` | Create — explain what the minimal MonoGame sample demonstrates |
| `samples/KernSmith.Samples.BlazorWasm/README.md` | Add note that `TextRenderer` works in WASM (no platform dependencies) |
| `integrations/KernSmith.MonoGameGum/README.md` | Note that `TextRenderer` is available for non-Gum MonoGame users |
| `integrations/KernSmith.GumCommon/README.md` | Note `TextRenderer` availability as alternative to Gum's `BitmapFont` |
| `tests/KernSmith.Tests/README.md` | Add `Output/TextRendererTests.cs` to test categories |
| `benchmarks/KernSmith.Benchmarks/README.md` | Add `TextRendererBenchmarks` to benchmark list |

### Sample updates

| Sample | Change |
|--------|--------|
| `samples/KernSmith.Samples/Program.cs` | Add scenario 5: generate font → `CreateTextRenderer()` → `LayoutText()` → print glyph positions and `MeasureString()` result |
| `samples/KernSmith.Samples.Minimal/Game1.cs` | Replace atlas-only display with actual text rendering via `TextRenderer` (see code in Sample Update section above) |
| `samples/KernSmith.Samples.BlazorWasm/` | Optional: add `TextRenderer` usage to the Blazor sample to prove it works in WASM |

### Integration updates

The Gum integrations (`MonoGameGum`, `KniGum`, `FnaGum`) currently delegate text rendering entirely to Gum's `BitmapFont` class. They don't need to change — but their READMEs should mention that non-Gum users can use `TextRenderer` directly.

| Integration | Change |
|-------------|--------|
| `integrations/KernSmith.MonoGameGum/README.md` | Add "Using KernSmith without Gum" section pointing to `TextRenderer` |
| `integrations/KernSmith.KniGum/README.md` | Same |
| `integrations/KernSmith.FnaGum/README.md` | Same |
| `integrations/KernSmith.GumCommon/README.md` | Same |

### NuGet package metadata

| File | Change |
|------|--------|
| `src/KernSmith/KernSmith.csproj` `<Description>` | Append: "Includes text layout engine for kerning-aware glyph positioning and string measurement." |

### CLI tool

| File | Change |
|------|--------|
| `tools/KernSmith.Cli/` | Consider adding a `measure` command: `kernsmith measure -f myfont.fnt "Hello World"` → prints `(width, height)`. Low priority but useful for scripting and CI validation. |

### Documentation (DocFX)

| File | Change |
|------|--------|
| `docs/core/index.md` | Add "Text Layout Engine" subsection with API overview and code example |
| `docs/core/toc.yml` | Add navigation entry for TextRenderer (API docs auto-generated from XML comments) |
| `docs/integrations/index.md` | Mention `TextRenderer` as the non-Gum path for game framework users |
| `docs/index.md` | Add text layout to feature matrix |

### Plan docs

| File | Change |
|------|--------|
| `plan/master-plan.md` | Add Phase 105 to active phases table |
| `plan/done/plan-data-types.md` | Add `GlyphLayout` and `TextRenderer` to the data types reference |

### Tests

| File | Change |
|------|--------|
| `tests/KernSmith.Tests/Output/TextRendererTests.cs` | **Create** — full test suite (see Testing section above) |
| `tests/KernSmith.Tests/Integration/EndToEndTests.cs` | Add test: `BmFont.Generate()` → `CreateTextRenderer()` → `LayoutText()` → verify glyph positions are sane |

### Benchmarks

| File | Change |
|------|--------|
| `benchmarks/KernSmith.Benchmarks/` | **Create** `TextRendererBenchmarks.cs` — benchmark `CreateTextRenderer()`, `LayoutText()` (short/medium/long strings), `MeasureString()` |

### CI/CD

| File | Change |
|------|--------|
| `.github/workflows/ci.yml` | No structural changes needed — new tests run automatically. Verify `TextRendererTests` pass on all 3 platforms (Windows/Linux/macOS). |
| `.github/workflows/benchmark.yml` | Add `TextRendererBenchmarks` to benchmark run if benchmarks are gated by class. |
| `.github/workflows/docs.yml` | No changes — DocFX auto-discovers new public types from XML comments. |

### Release artifacts

| File | Change |
|------|--------|
| `CHANGELOG.md` | Add to next version's "Added" section: `TextRenderer` class, `GlyphLayout` struct, `CreateTextRenderer()` extensions, word wrap, measurement |
| `RELEASING.md` | Add checklist item: "Verify TextRenderer API docs render correctly in DocFX output" |

### Apps (UI)

| File | Change |
|------|--------|
| `apps/KernSmith.Ui/` | If the UI has a text preview panel, consider using `TextRenderer` internally instead of custom layout code. Low priority — the UI is a separate concern. |

## Implementation order

1. **Core implementation** — `GlyphLayout`, `TextRenderer`, `TextRendererExtensions` (the API)
2. **Tests** — `TextRendererTests` (prove it works)
3. **Sample update** — `KernSmith.Samples.Minimal/Game1.cs` (prove it renders)
4. **Console sample update** — `KernSmith.Samples/Program.cs` (scenario 5)
5. **NuGet description** — `KernSmith.csproj`
6. **Root README** — features list + quick start
7. **Package READMEs** — `src/KernSmith/README.md`, integration READMEs
8. **Docs** — DocFX pages, data types reference
9. **Plan docs** — master plan, data types
10. **Benchmarks** — `TextRendererBenchmarks`
11. **CLI** — `measure` command (optional, can defer)
12. **CHANGELOG + RELEASING** — release prep

## Success criteria

1. `TextRenderer` lives in core `KernSmith` — no new NuGet packages
2. Rendering text in any framework requires <=10 lines of framework-specific code
3. Word wrap and measurement work correctly
4. All tests pass
5. Minimal sample renders text on screen
6. Every README that shows usage examples includes `TextRenderer`
7. NuGet package description mentions text layout
8. DocFX docs cover the new API
9. CHANGELOG documents the addition
10. Benchmarks cover `LayoutText` and `MeasureString` performance
