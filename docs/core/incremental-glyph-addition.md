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
being added, never to the font or the atlas. The session is not thread-safe: use one session
per thread, or synchronize access externally.

## The normal case: a character your font doesn't have

You made a font the usual way — `BmFont.Generate`, or a `.fnt` already on disk — with, say,
ASCII. Later the string `"café"` needs `é`, which was never in the character set:

```csharp
// day 1 — the normal one-shot path, no sessions involved
var result = BmFont.Generate(fontBytes, options);      // Characters = ASCII: no 'é'
// ... ship result.FntText and result.GetPngData(0) ...

// later — 'é' comes up missing
var model = BmFont.LoadModel(fntText);                 // or result.Model in-process
using var session = BmFont.ResumeIncremental(fontBytes, options, model);
var add = session.AddGlyphs("é");                      // bytes + position for just 'é'
```

This works even though `é` was outside the original character set: the session parses the font
unfiltered, so any character the font file contains is addable. Occupancy, characters, and
kerning are all recovered from the model.

The natural integration is a draw-time miss handler — the session is created lazily the first
time any character is missing, then kept for the rest of the run:

```csharp
if (!glyphs.ContainsKey(ch))
{
    _session ??= BmFont.ResumeIncremental(fontBytes, options, loadedModel);
    var add = _session.AddGlyphs(ch.ToString());
    foreach (var g in add.Added)
    {
        // blit g at (g.X + pad.Left, g.Y + pad.Up) — see the blit contract below
        glyphs[g.Codepoint] = g.Char;                  // also merge add.NewKerning
    }
}
```

Persist `session.CurrentModel` as the updated `.fnt` on shutdown to make the character
permanent — or don't; re-adding it next run costs microseconds.

`ResumeIncremental` must be given the **same options** the model was generated with — padding,
spacing, size and outline are verified against the model's info block and a mismatch throws.
`Bold`, `Italic` and `MatchCharHeight` cannot be verified from the model and must match the
original generation.

## Starting from scratch

`BeginIncremental` builds an atlas from nothing instead — the first `AddGlyphs` sizes the page
exactly as `Generate` would:

```csharp
using var session = BmFont.BeginIncremental(fontBytes, new FontGeneratorOptions { Size = 32 });
session.AddGlyphs("ASDF");
GlyphAdditionResult result = session.AddGlyphs("Q");
```

## How positions are known (important)

You never compute or track a glyph's position — you are told it:

1. **At add time**: `AddGlyphs` returns each new glyph's `PageIndex`/`X`/`Y`. The session keeps
   a free-rectangle map of the atlas, updated on every add, so each call knows where everything
   already is.
2. **After that**: `session.CurrentModel.Characters` holds one `CharEntry` per glyph ever
   placed — literally the `.fnt` chars block (`char id=.. x=.. y=.. width=.. height=.. page=..`),
   the same data any BMFont renderer uses at draw time.

A returned position never changes. Each add writes one new rectangle to the texture and appends
one row to the model; nothing already uploaded is ever repainted.

```csharp
using var session = BmFont.BeginIncremental(fontBytes, options);
Blit(session.AddGlyphs("ABCDEFG"));   // initial set — 7 positions returned
Blit(session.AddGlyphs("Q"));         // Q slots into a remaining hole; A–G untouched
Blit(session.AddGlyphs("I"));
Blit(session.AddGlyphs("H"));
```

Example run (Georgia 32 px): `ABCDEFG` fills a 128×128 page; `Q` then lands at `(19,94)` in the
gap beside `F`, `I` at `(20,71)` beside `E`, `H` at `(24,47)` beside `A`.

### Across restarts: the `.fnt` is the occupancy map

The packer never reads the atlas image. The free-rectangle map lives only as long as the
session, so on shutdown persist `CurrentModel` as a `.fnt` next to the texture. On the next run,
`ResumeIncremental` rebuilds the same map by subtracting the ledger's rectangles from an empty
page:

```
seed: one free rect per page          subtract each char rect        what's left = free space
┌────────────────┐                    ┌──G──┬─B──┬───────┐          ┌─────┬────┬───────┐
│                │                    ├──C──┼─D──┤       │          │     │    │ free  │
│   all free     │   -- for each  →   ├──A──┴─┬──┘       │    →     │     │    │       │
│                │      char rect     ├──E──┬─┘          │          │     ├────┘       │
│                │                    ├──F──┘            │          │     │   free     │
└────────────────┘                    └──────────────────┘          └─────┴────────────┘
```

Free space is derived from the rectangles, not from how they were originally packed — so resume
works with an atlas from an older run, a different packer, or a hand-authored `.fnt`. A resumed
session places subsequent glyphs identically to a session that never died (tested).

One consequence: **anything not in the chars block is assumed free.** Non-glyph art sharing the
texture is invisible to the packer and can be placed over. v1 cannot declare extra reserved
rectangles — keep incremental atlases glyphs-only.

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

Three pixel views are offered:

| Property | Layout | Use |
|----------|--------|-----|
| `Pixels` | Tightly packed RGBA32, `Width * Height * 4` bytes; grayscale coverage promoted to `(255, 255, 255, alpha)` | The default path — ready for `Texture2D.SetData<Color>`. Computed lazily on first access and cached. |
| `PremultipliedPixels` | Premultiplied RGBA32 (`(a,a,a,a)` for grayscale coverage) | MonoGame/XNA default `BlendState.AlphaBlend` pipelines. Lazy, cached. |
| `RawPixels` + `RawFormat` + `Pitch` | The rasterization pipeline's own buffer, zero-copy (usually `Grayscale8`, `Pitch` bytes per row) | Max-performance callers that consume the native format directly. Treat as read-only. |

Callers that read only `RawPixels` never pay for the RGBA expansions.

> **Alpha note (MonoGame/XNA):** `Pixels` is straight (non-premultiplied) alpha — draw it with
> `BlendState.NonPremultiplied`. Or upload `PremultipliedPixels` instead and keep the default
> `BlendState.AlphaBlend`.

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
