# bmfontier -- Font Subsetting Plan (Task 13C)

> Logical subsetting: filter TTF parser output to requested codepoints during parsing.
> Binary TTF subsetting (rewriting font files) is NOT recommended — FreeType loads glyphs lazily.
>
> **Date**: 2026-03-19

---

## Analysis

The real memory waste is in `TtfParser` eagerly expanding ALL cmap entries, kerning pairs (kern + GPOS), and hmtx data — even when only a small character set is requested. For a 20K-glyph CJK font requesting 200 chars, this means:

- Full cmap dictionary (20K+ entries) instead of ~200
- GPOS Format 2 class-based kerning expands to cartesian product — potentially millions of KerningPair objects
- Font bytes copied twice (once for TtfParser, once for FreeType)

FreeType itself is already efficient — `FT_Load_Glyph` loads lazily from the memory-mapped font buffer.

---

## Approach: Logical Subsetting

Pass the requested `CharacterSet` codepoints into `TtfParser` as a filter hint. Skip irrelevant entries during parsing rather than parsing everything and filtering after.

---

## Task Breakdown

| # | Task | File(s) | Effort | Impact |
|---|------|---------|--------|--------|
| 1 | Add `requestedCodepoints` parameter to `TtfParser` constructor | `TtfParser.cs` | Small | Plumbing |
| 2 | Filter cmap entries during parsing (Format 4 + Format 12) | `TtfParser.cs` | Small | High (CJK) |
| 3 | Build relevant glyph set after cmap; filter kern/GPOS pairs during parsing | `TtfParser.cs` | Medium | High (CJK) |
| 4 | Thread `CharacterSet` from `BmFont.Generate` → `TtfFontReader` → `TtfParser` | `BmFont.cs`, `TtfFontReader.cs` | Small | Plumbing |
| 5 | Eliminate duplicate font byte copy (share between parser + FreeType) | `BmFont.cs`, `TtfParser.cs` | Medium | Medium |
| 6 | Audit/remove unused `_advanceWidths` in hmtx parsing | `TtfParser.cs` | Trivial | Low |
| 7 | Tests with CJK font fixture + benchmarks | `tests/` | Medium | Verification |

---

## API Design

**Option chosen: internal-only change (no breaking public API).**

`IFontReader` interface stays unchanged. `TtfFontReader` accepts the character set via a property set by `BmFont.Generate()` before calling `ReadFont()`. Custom `IFontReader` implementations are unaffected.

The raw codepoints from `CharacterSet` are passed as a hint (pre-intersection). The parser filters cmap to only these codepoints. `FontInfo.AvailableCodepoints` then contains only the intersection, which is the same result `CharacterSet.Resolve()` would produce.

---

## Edge Cases

- **`CharacterSet.All` or very large sets**: Skip filtering (pass null hint) — no benefit to filtering
- **Kerning pairs outside subset**: Correctly dropped — if both glyphs aren't in the subset, the pair is irrelevant
- **`AvailableCodepoints` semantics**: After subsetting, this contains only requested+available codepoints, not all font codepoints. Document this change.
- **Composite glyphs**: Not relevant — we're not rewriting the binary font

---

## Estimated Effort

- **Total**: 2-3 days focused work
- **Risk**: Low — additive optimizations, no behavioral changes
- **Needs**: CJK font fixture for meaningful benchmarks (Roboto has only ~1K glyphs)
