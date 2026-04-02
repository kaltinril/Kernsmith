# Phase 175 — Native Rasterizer: Basic GSUB Layout Features

> **Status**: Future
> **Created**: 2026-04-01
> **Depends on**: Phase 165 (IRasterizer integration)

## Goal

Implement basic GSUB (Glyph Substitution) features so the Native rasterizer can handle common ligatures and glyph alternates without relying on external shaping libraries.

## Background

GSUB defines rules for substituting glyphs based on context. For Latin/Cyrillic/Greek text, the most important features are ligatures (fi, fl, ffi, ffl) and contextual alternates. Full complex script shaping (Arabic, Devanagari, etc.) requires a complete shaper like HarfBuzz — that is out of scope.

Note: GSUB is typically handled by the font reading layer, not the rasterizer. However, since the Native rasterizer has its own font parser, basic GSUB support here means it can self-serve common substitutions.

## Scope

### Coverage Table Parser
- Format 1: individual glyph list
- Format 2: range records (startGlyph, endGlyph, startCoverageIndex)

### GSUB Lookup Types (Subset)

**Type 1 — Single Substitution**: Replace one glyph with another
- Format 1: replace by adding delta to glyph ID
- Format 2: replace by direct mapping array

**Type 4 — Ligature Substitution**: Replace sequence of glyphs with one
- Coverage on first glyph, then match subsequent glyphs
- e.g., 'f' + 'i' → 'fi' ligature glyph

### Feature List Parser
- Parse feature records: tag + lookup list indices
- Support features: `liga` (standard ligatures), `ccmp` (glyph composition/decomposition)
- Note: `calt` (contextual alternates) requires Lookup Type 6 (Chaining Contextual), which is out of scope for this phase. Add `calt` support when Type 5/6 lookups are implemented.

### Script/Language Selection
- Parse script list and language system records
- Default script selection: find 'latn' (Latin) or 'DFLT' (default)
- Apply features in standard order: ccmp, liga

### Feature Processing

Given a sequence of glyph indices:
1. Apply `ccmp` lookups (composition/decomposition)
2. Apply `liga` lookups (ligature substitution)
3. Return modified glyph sequence

### Integration

- GSUB processing happens in the `RasterizeAll` path
- Before rasterizing individual glyphs, run GSUB on the codepoint list
- The substituted glyph indices go to the rasterizer
- Note: this only works when the caller provides the text sequence, not individual codepoints

### Out of Scope

- Lookup Types 2 (multiple substitution), 3 (alternate substitution), 5/6 (contextual/chaining contextual), 7 (extension), 8 (reverse chaining)
- Complex script features (Arabic: init/medi/fina/isol, Devanagari: half forms, etc.)
- Full OpenType feature negotiation
- Right-to-left processing

## Testing

- Ligature: 'f'+'i' → fi ligature glyph (in fonts that have it)
- Single substitution: verify glyph replacement
- Feature ordering: verify ccmp applied before liga
- No-op: verify fonts without GSUB features handled gracefully
- Coverage formats: test both Format 1 and Format 2

## Success Criteria

- [ ] Coverage tables parsed (Format 1, 2)
- [ ] Single substitution (Type 1) works
- [ ] Ligature substitution (Type 4) works
- [ ] Feature list parsed, correct features selected
- [ ] Standard feature order applied (ccmp → liga)
- [ ] All tests pass
