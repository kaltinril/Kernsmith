# bmfontier -- Testing Strategy

> Part of the [Master Plan](master-plan.md).
> Related: [Project Structure](plan-project-structure.md), [API Design](plan-api-design.md)

All data types are defined in [plan-data-types.md](plan-data-types.md). Error types are defined in the "Error Handling Strategy" section of that document.

**Framework: xUnit** with `FluentAssertions` for readable assertions. Test project: `Bmfontier.Tests` targeting `net8.0`.

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

| Font | Purpose | Source | License |
|------|---------|--------|---------|
| **Roboto-Regular.ttf** | General testing, well-known metrics | Google Fonts | Apache 2.0 |
| **Liberation Sans** (older version) | kern-table-only kerning | Fedora repos / GitHub | SIL OFL |
| **Inter-Regular.ttf** | GPOS-only kerning (modern font) | Google Fonts | SIL OFL |
| **Noto Sans-Regular.ttf** | Both kern + GPOS, large Unicode coverage | Google Fonts | SIL OFL |
| **NotoSansCJK-Regular.ttc** | Font collection (.ttc) testing | Google Fonts | SIL OFL |
| **Minimal synthetic TTF** | Edge cases, minimal valid font | Our own (hand-crafted or generated via FontForge script) | MIT |

---

## Golden Data Generation

Reference values for parser unit tests are generated using `ttx` (from fonttools):

```bash
# Extract specific tables for reference
ttx -t head -t OS/2 -t hhea -t name -t kern -t GPOS Roboto-Regular.ttf
```

Store expected values in `tests/fixtures/expected/roboto-metrics.json`:
```json
{
  "unitsPerEm": 2048,
  "ascender": 1900,
  "descender": -500,
  "lineGap": 0,
  "isBold": false,
  "weightClass": 400
}
```

For BMFont output validation, generate a reference `.fnt` file using the original BMFont tool (Windows) and store in `tests/fixtures/expected/`.

---

## Additional Test Categories

### Rasterizer Tests
- Verify `FreeTypeRasterizer` produces non-empty bitmap data for ASCII codepoints
- Verify glyph metrics (advance > 0 for printable chars, advance > 0 for space with empty bitmap)
- Verify `null` return for codepoints not in the font
- Verify `RasterizationException` when font not loaded

### CharacterSet Tests
- `CharacterSet.Ascii` produces codepoints 32-126
- `CharacterSet.FromRanges` with custom ranges
- `CharacterSet.FromChars("Hello")` deduplicates
- `CharacterSet.Union` combines sets
- `Resolve()` filters to available codepoints

### Configuration Tests
- `FontGeneratorOptions` defaults are valid (Size=32, MaxTextureSize=1024, etc.)
- `Padding(1)` convenience constructor sets all four sides
- `Spacing(1)` convenience constructor sets both dimensions

### Error Handling Tests
- Invalid font data → `FontParsingException`
- Glyph exceeds max texture size → `AtlasPackingException`
- Size <= 0 → `ArgumentException`

---

## Validation Criteria

### BMFont Output Correctness
A generated `.fnt` file is correct if:
1. It parses without error in MonoGame.Extended's `BitmapFontReader` (integration test)
2. Character metrics (xoffset, yoffset, xadvance) are within ±1 pixel of reference values from FontForge
3. Atlas coordinates (x, y, width, height) point to non-empty pixel regions in the PNG
4. Kerning pairs match the font's kern/GPOS table entries (scaled to the target size)
5. `lineHeight`, `base`, `scaleW`, `scaleH` values are consistent with the atlas dimensions

### Cross-Platform Tolerance
- Parser and formatter tests: deterministic, must match exactly across platforms
- Rasterizer tests: glyph bitmaps may differ by ±1 pixel across OS due to FreeType hinting differences. Use metric-based assertions (dimensions, advance) not pixel-exact comparison
- Atlas layout tests: packing results are deterministic (same algorithm, same input order → same output)

---

## CI Workflow

Run the full test suite on GitHub Actions with a matrix of:

- **OS**: Windows (latest), macOS (latest), Ubuntu (latest)
- **Framework**: .NET 8 (LTS)

This ensures FreeTypeSharp's native binaries work correctly on all supported platforms and our parser produces consistent results regardless of endianness or platform-specific behavior.

GitHub Actions, `.github/workflows/ci.yml`:
- **Matrix**: `{ os: [windows-latest, macos-latest, ubuntu-latest], dotnet: ['8.0.x'] }`
- **Steps**: checkout → setup-dotnet → restore → build → test
- **Artifacts**: upload generated `.fnt` + `.png` files on test failure for visual inspection
- All tests run on all platforms. Platform-conditional assertions use `RuntimeInformation.IsOSPlatform()`.
