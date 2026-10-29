# KernSmith.Imaging

Pure managed image decoding for KernSmith. Today it holds one thing: a hardened PNG decoder,
written from the W3C PNG specification (Third Edition), used by the Native rasterizer for the
PNG glyph images inside `CBDT` and `sbix` color fonts.

- No `unsafe` code, no P/Invoke, no dependencies beyond the BCL — runs on browser-wasm, iOS
  NativeAOT and everywhere else .NET runs. Inflation uses `System.IO.Compression.ZLibStream`,
  which the browser-wasm runtime ships (zlib is part of the runtime; only Brotli is missing there).
- Not published to NuGet yet (`IsPackable=false`): its only consumer, the Native rasterizer, is
  unpublished. Packing it is part of that release decision (Phase 179).

## Usage

```csharp
using KernSmith.Imaging;

PngImage image = PngDecoder.Decode(bytes);            // or Decode(bytes, new PngDecodeLimits(...))
// image.Width, image.Height, image.Pixels: RGBA, 4 bytes per pixel, top row first, straight alpha

PngHeader header = PngDecoder.ReadHeader(bytes);      // size, depth, color type; no pixel decoding
bool isPng = PngDecoder.HasSignature(bytes);
```

## What it decodes

- Every color type (gray, RGB, palette, gray + alpha, RGBA) at every allowed bit depth
  (1, 2, 4, 8, 16), interlaced (Adam7) or not, all five scanline filters.
- `PLTE` and `tRNS` are applied. Samples are rescaled to 8 bits with the spec's most accurate
  rule, `round(v * 255 / (2^depth - 1))`, after the `tRNS` comparison (which uses the full
  16-bit value).
- **No color management.** `gAMA`, `cHRM`, `sRGB`, `iCCP`, `cICP` and every other ancillary chunk
  are skipped (their CRC is still checked), so samples come out exactly as stored. APNG animation
  chunks are ancillary too: an APNG decodes as its default image.

## Untrusted input

Every failure is a `PngFormatException` (a `FormatException`; it does not derive from KernSmith's
`BmFontException` so this assembly stays dependency-free — KernSmith callers wrap it):

- signature, chunk framing, chunk type characters and every CRC are checked; truncated data,
  unknown critical chunks, non-consecutive `IDAT`s, a missing `IEND`, an invalid header, an
  unknown filter type, out-of-range palette indices and a malformed or misplaced `PLTE` throw;
- a malformed or misplaced `tRNS` is ignored (ancillary, as the spec allows);
- the declared size is checked against `PngDecodeLimits` (default 16384 px per side and 64 Mi
  pixels) before anything is allocated, a header the compressed data could not possibly fill is
  rejected up front (deflate expands at most 1032:1), and inflation stops at exactly the size the
  header implies, so a small file cannot become a large allocation;
- bytes after the image data or after `IEND` are ignored.

## Tests

`tests/KernSmith.Tests/Imaging/`: the full PngSuite (`Fixtures/PngSuite/`, with its licence)
compared pixel for pixel against StbImageSharp (a test-only oracle; 16-bit images may differ by
one because StbImageSharp drops the low byte instead of rounding), hand-built images for each
rule above, and hostile inputs — every truncation, seeded mutations with and without repaired
CRCs, and a decompression bomb.
