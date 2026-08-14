# Incremental Glyph Addition

A `BmFontIncrementalSession` adds glyphs to an existing atlas **at runtime** without moving
anything already placed ("stable packing",
[issue #208](https://github.com/kaltinril/Kernsmith/issues/208)). Each `AddGlyphs` call returns
the new glyphs' pixel bytes plus a reserved position, so a renderer can blit them straight into
a live GPU texture (e.g. a MonoGame `RenderTarget2D`) — no regenerate, no re-upload of the
whole page.

The session is built for a game loop: the font is parsed once (unfiltered, so *any* character
the font contains stays addable later), the rasterizer stays loaded, and the packer's
free-rectangle state is held between calls. A per-add costs work proportional to the glyphs
being added, never to the font or the atlas.

## Starting a session

```csharp
// Fresh session — the first AddGlyphs sizes the atlas exactly as Generate would.
using var session = BmFont.BeginIncremental(fontBytes, new FontGeneratorOptions { Size = 32 });
session.AddGlyphs("ASDF");

// Later, at runtime: a text box receives a character the atlas doesn't have.
GlyphAdditionResult result = session.AddGlyphs("Q");
```

Or resume from an existing generation — occupancy, characters, and kerning are recovered from
the `BmFontModel` (which a `.fnt` loads back into via `BmFont.LoadModel`):

```csharp
var generated = BmFont.Generate(fontBytes, options);
// ... upload generated.Pages[0] to a texture ...

using var session = BmFont.ResumeIncremental(fontBytes, options, generated.Model);
var result = session.AddGlyphs("QW");
```

`ResumeIncremental` must be given the **same options** the model was generated with — padding,
spacing, size and outline are verified against the model's info block and a mismatch throws.
`Bold`, `Italic` and `MatchCharHeight` cannot be verified from the model and must match the
original generation.

## What an add returns

`GlyphAdditionResult` carries everything needed to update a live font:

| Member | Description |
|--------|-------------|
| `Added` | One `AddedGlyph` per newly placed glyph: pixel bytes + placement + ready-to-use `CharEntry`. |
| `NewKerning` | Kerning-pair **delta** only — pairs touching the new glyphs, against everything present. |
| `FailedCodepoints` | Codepoints the font cannot render. |
| `AlreadyPresent` | Re-adds are a no-op, reported here rather than thrown. |
| `PageGrown`, `PageWidth`, `PageHeight`, `PageCount` | Current page geometry; see [overflow policies](#when-a-glyph-doesnt-fit). |

`session.CurrentModel` materializes the full up-to-date `BmFontModel` on demand (it round-trips
through the standard formatters/readers like a `Generate` model), for when you want to persist
the grown font as a `.fnt`.

## The blit contract

Each `AddedGlyph` is blitted at:

```
(destX, destY) = (glyph.X + Padding.Left, glyph.Y + Padding.Up)   on page glyph.PageIndex
```

— the same rule the atlas builder uses for full generation. `X`/`Y` are the origin of the
packed cell (which includes padding); `Char` carries the padded cell dimensions for the `.fnt`
side.

Two pixel views are offered:

| Property | Layout | Use |
|----------|--------|-----|
| `Pixels` | Tightly packed RGBA32, `Width * Height * 4` bytes; grayscale coverage promoted to `(255, 255, 255, alpha)` | The default path — ready for `Texture2D.SetData<Color>`. Computed lazily on first access and cached. |
| `RawPixels` + `RawFormat` + `Pitch` | The rasterization pipeline's own buffer, zero-copy (usually `Grayscale8`, `Pitch` bytes per row) | Max-performance callers that consume the native format directly. Treat as read-only. |

Callers that read only `RawPixels` never pay for the RGBA expansion.

```csharp
foreach (var g in result.Added)
{
    // MonoGame: write the glyph into the atlas RenderTarget2D.
    var dest = new Rectangle(g.X + padding.Left, g.Y + padding.Up, g.Width, g.Height);
    texture.SetData(0, dest, MemoryMarshal.Cast<byte, Color>(g.Pixels).ToArray(), 0, g.Width * g.Height);
    // Merge g.Char and result.NewKerning into your glyph lookup.
}
```

## When a glyph doesn't fit

The no-fit behavior is chosen per session via `AdditionOverflowPolicy`
(third argument to `BeginIncremental`/`ResumeIncremental`):

| Policy | Behavior |
|--------|----------|
| `Grow` (default) | Double the smaller page dimension (power-of-two, like `AutofitTexture`) up to `MaxTextureWidth`/`MaxTextureHeight`, then throw `AtlasPackingException`. Existing placements stay valid. |
| `NewPage` | Add another page; never fails (unless a single glyph exceeds an entire page). |
| `Throw` | Throw `AtlasPackingException` immediately — for fixed-size consumers. |

After a `Grow`, the result reports `PageGrown = true` with the new `PageWidth`/`PageHeight`:
reallocate the texture and copy the old contents to `(0, 0)` — old pixels never move. After a
`NewPage`, `PageCount` increases and new glyphs may carry a higher `PageIndex`.

## v1 limitations

- **One session = one set of settings.** Every add uses the options the session was started
  with; mixing sizes or styles in one atlas means one session (and one atlas) per style.
- Not supported in a session (throws at `BeginIncremental`/`ResumeIncremental`): `Variants`,
  `ChannelPacking`, `TargetRegion`, `CustomGlyphs`.
- **Placements differ from a full regenerate — by design.** Stable packing never moves an
  existing glyph, so a glyph added later lands wherever free space remains, not where a full
  repack of the combined set would put it. Packing efficiency degrades accordingly; regenerate
  when you want an optimal layout.
- With `EqualizeCellHeights`, a new glyph *taller* than the atlas's established cell height
  throws (`InvalidOperationException`) — accepting it would have changed every existing cell.
  Regenerate to include it.
- A `PackingAlgorithm.Skyline` configuration — or a custom `FontGeneratorOptions.Packer` —
  falls back to MaxRects for additions (arbitrary packer state cannot be reconstructed from
  occupied rectangles); existing placements stay valid, only the placement strategy for new
  glyphs differs.
