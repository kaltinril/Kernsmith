# Phase 175 — Native Rasterizer: Basic GSUB Layout Features

> **Status**: Deferred — awaiting Jeremy's later decision on scope and home (see Open question); not scheduled
> **Created**: 2026-04-01
> **Depends on**: Phase 166 Part 1 (decoder by glyph index)

## Goal

Basic GSUB substitutions (ligatures, single substitution) without an external shaper.

## Open question (decide before any work)

The rasterizer is the wrong layer as currently specified:

- `IRasterizer.RasterizeAll` takes a list of **codepoints**, and every BMFont char id is a codepoint. A ligature glyph (`fi`) has no codepoint of its own, so it has no natural slot in the atlas or the `.fnt`.
- Substitution depends on the text being laid out, which the rasterizer never sees.

Options, for Jeremy to choose:

1. **Core text layout** — GSUB belongs with core's text layout engine (Phase 105) and the shared font parser, not a backend. Ligature glyphs would need a glyph-id-addressed atlas entry plus a `.fnt` extension (or private-use codepoints) to be addressable.
2. **Rasterize-by-glyph-index** — add an `IRasterizer` path that renders a glyph index, so core can request substituted glyphs from any backend.
3. **Drop** — BMFont output is codepoint-keyed; substitution is the consumer's shaping job.

The parsing work below is reusable under options 1 and 2.

## Scope (if pursued)

- Coverage tables (formats 1 and 2)
- Lookup type 1 (single substitution, formats 1 and 2) and type 4 (ligature substitution); type 7 (extension) wrapper since many fonts use it
- ScriptList/FeatureList/LookupList; default script `DFLT`/`latn`; features `ccmp` then `liga`
- Out of scope: types 2, 3, 5, 6, 8, complex scripts, RTL, full feature negotiation

## Testing (if pursued)

- `f`+`i` → ligature glyph in a font that has it; single substitution; `ccmp` before `liga`; fonts without GSUB are a no-op; both coverage formats

**Regression coverage**: depends on the option chosen — a `.bmfc` that requests ligatures exists only if a config surface is added.
