# bmfontier -- Testing Strategy

> Part of the [Master Plan](master-plan.md).
> Related: [Project Structure](plan-project-structure.md), [API Design](plan-api-design.md)

---

## Unit Tests

| Area | What to Test |
|------|-------------|
| **Table parsers** | Parse known font files, verify extracted values against reference (e.g., compare head.unitsPerEm, OS/2.sTypoAscender against values from FontForge or `ttx`). |
| **cmap parser** | Verify Unicode-to-glyph mapping for known characters in test fonts. |
| **kern parser** | Verify kerning pairs match reference tool output. |
| **GPOS parser** | Test with fonts that have GPOS-only kerning (e.g., Google Fonts). Verify pair values. |
| **MaxRects packer** | Pack known rectangle sets, verify 100% placement, verify no overlaps, verify efficiency. |
| **Skyline packer** | Same as MaxRects. |
| **Text formatter** | Round-trip: generate BMFont text, verify it matches expected format exactly. |
| **XML formatter** | Same. Validate against XML schema. |
| **Binary formatter** | Same. Verify byte-level correctness (header, block structure, endianness). |

---

## Integration Tests

| Test | Description |
|------|-------------|
| **End-to-end ASCII** | Generate BMFont from a TTF for ASCII chars, verify .fnt parses correctly, verify .png contains expected glyphs. |
| **End-to-end Unicode** | Generate BMFont for ASCII + Cyrillic, verify multi-page support works. |
| **In-memory round-trip** | Generate entirely in memory (byte[] in, BmFontResult out, no disk). |
| **Format compatibility** | Load generated .fnt with MonoGame.Extended's parser to verify real-world compatibility. |

---

## Validation Tests

| Test | Description |
|------|-------------|
| **Reference comparison** | Generate BMFont output for the same font/size/settings as the original BMFont tool, compare character metrics and kerning values. |
| **Cross-platform CI** | Run test suite on Windows, macOS, Linux (GitHub Actions). |

---

## Test Fonts

Maintain a `tests/fixtures/` directory with:

| Font | Purpose | License |
|------|---------|---------|
| A well-known open-source TTF (e.g., Roboto) | General testing | Apache 2.0 |
| A font with kern-table-only kerning | Verify kern parser | Open source |
| A font with GPOS-only kerning | Verify GPOS parser | Open source |
| A font with both kern + GPOS | Verify merging behavior | Open source |
| A .ttc font collection | Verify collection support | Open source |
| A minimal synthetic TTF | Edge case testing | Our own |

---

## Cross-Platform CI

Run the full test suite on GitHub Actions with a matrix of:

- **OS**: Windows (latest), macOS (latest), Ubuntu (latest)
- **Framework**: .NET 8 (LTS)

This ensures FreeTypeSharp's native binaries work correctly on all supported platforms and our parser produces consistent results regardless of endianness or platform-specific behavior.
