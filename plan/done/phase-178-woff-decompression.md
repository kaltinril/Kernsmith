# Phase 178 — WOFF2 Decompression (Core)

> **Status**: Complete — core WOFF2 decoding, `KernSmith.Fonts.Web` on WOFF2, verified under browser-wasm; the CFF-in-WOFF2 test moved to Phase 166 (needs its `.otf` fixture)
> **Created**: 2026-04-01
> **Depends on**: nothing

## Goal

Decode WOFF2 in core so every backend — Native included — accepts `.woff2` files, and `KernSmith.Fonts.Web` can use the WOFF2 files CSS font CDNs serve.

## Where it lives

- `BmFont` and `PreparedRasterization.Prepare` detect WOFF/WOFF2 by signature and decompress **before** any backend sees the bytes, so this is core-only — **no change to `NativeRasterizer.LoadFont`**.
- `WoffDecompressor.Decompress` routes `wOF2` to `Font/Woff2/Woff2Decoder` (container, collection directory, sfnt/TTC reassembly), `Woff2GlyfTransform`, `Woff2HmtxTransform`; Brotli is `Font/Brotli/ManagedBrotliDecoder` with the static dictionary as an embedded raw-deflate resource.
- `KernSmith.Fonts.Web`: `CssParser.ExtractFontUrl` prefers a rule's WOFF2 source (by `format()` hint, else extension) and falls back to WOFF; `WebFontSource` sends a modern Chrome User-Agent outside the browser only (Google Fonts serves TTF to unknown agents, WOFF2 to browsers; in the browser a custom UA breaks CORS in Firefox); `WebFontCache` names disk files by signature.

## Constraint: our own managed Brotli decoder

WOFF2's Brotli stage is a pure managed decoder we write in core — not `System.IO.Compression.BrotliDecoder`, not a third-party package (decision and reason in `project_decisions.md` → Native Rasterizer). In short: `BrotliDecoder` wraps the runtime's native Brotli library, which browser-wasm does not ship, so it throws `PlatformNotSupportedException` there — the platform `KernSmith.Fonts.Web` exists for.

## Work items

### Brotli decoder (RFC 7932)

- Bit reader; meta-block headers (including uncompressed and metadata blocks); prefix codes (simple and complex, code-length codes); block-type/count switching for literals, commands and distances; context modeling (literal context modes, distance contexts); distance ring buffer and the distance code mapping; insert-and-copy lengths; the static dictionary (RFC 7932 Appendix A) and its 121 word transforms; sliding window up to 16 MiB.
- The static dictionary is ~120 KB of data — embed it as a resource or generated array.
- Hardened: every length/index bounds-checked, output size capped by the WOFF2 header's `totalSfntSize` and an absolute limit, malformed input → `FontParsingException` (as `WoffDecompressor` already throws), never a hang.

### WOFF2 container

- Header (`wOF2`), table directory with known-tag flags and `UIntBase128` lengths, collection directory for `.ttc` payloads.
- One Brotli stream for all tables; slice per table by `transformLength`/`origLength`.

### Table transforms

- `glyf`/`loca` (transform version 0): the seven substreams (nContour, nPoints, flags, glyph, composite, bbox bitmap + bbox, instructions), triplet decoding, `255UInt16`, composite glyph copy, explicit vs computed bboxes, `overlapSimpleBitmap`; rebuild `loca` in the `head.indexToLocFormat` size.
- `hmtx` (transform version 1): reconstruct omitted LSBs from `glyf` xMin.
- Null transforms pass through; reject unknown transform versions.

### sfnt reassembly

- Rebuild the table directory (sorted tags, 4-byte padding, checksums, `head.checkSumAdjustment`) so the output is a valid sfnt for the core parser and every backend.

### Follow-ups in the same phase

- Done: `KernSmith.Fonts.Web` prefers `.woff2`, `.woff` fallback; desktop UI dialogs/drag-and-drop, the Blazor WASM sample picker and CLI help accept `.woff2`; docs and CHANGELOG updated.

### Reference material

- W3C WOFF2 Recommendation (frozen) and RFC 7932 are the source of truth.
- The decoder and transforms are written from those two specs; no third-party source is copied and there is no package dependency.

## Testing

- Brotli: RFC 7932 test vectors and round trips against .NET's `BrotliEncoder` output (test project only — the BCL encoder is fine on desktop test runs)
- WOFF2 fixture(s) committed with their OFL `.ttf` source: decoded sfnt tables match the source byte-for-byte where the format allows (transformed `glyf` may differ in padding; compare parsed glyphs)
- `.woff2` and `.ttf` of the same font produce identical BMFont output on every backend (extend `WoffAllBackendsTests`)
- A WOFF2 collection. (A `.woff2` wrapping CFF moved to Phase 166, which adds the `.otf` fixture.)
- Malformed/truncated/oversized inputs fail with a clear exception
- Run under browser-wasm: done manually with a throwaway page in `samples/KernSmith.Samples.BlazorWasm` (not committed) driven headless in Chrome (CDP) and Firefox (WebDriver BiDi). A local `.woff2` decoded and generated with StbTrueType, and `WebFontSource` fetched and decoded Roboto WOFF2 from Bunny and Google Fonts. This run is what found the Firefox User-Agent/CORS bug

**Regression coverage**: add `tests/bmfont-compare/gum-bmfont/woff2-plain.bmfc` with `fontFile` pointing at the committed `.woff2` fixture, force-added. Every backend should produce output identical to the same font as `.ttf`; assert that in a test, since the harness tolerates a failing pair.

## Success Criteria

- [x] WOFF 1.0 decoded in core
- [x] Managed Brotli decoder, hardened, no BCL Brotli and no new packages
- [x] WOFF2 `glyf`/`loca`/`hmtx` transforms reversed; valid sfnt out
- [x] `.woff2` output identical to the source `.ttf` on every backend
- [x] `KernSmith.Fonts.Web` uses WOFF2 when offered
- [x] All tests pass; `regression_check.py` run with `woff2-plain.bmfc`
