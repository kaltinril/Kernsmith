# KernSmith.Benchmarks

Performance benchmarks for the KernSmith library using BenchmarkDotNet.

## Overview

This project measures the performance of key KernSmith operations including font parsing, glyph rasterization, atlas packing, and output formatting.

## Running

```
dotnet run --project benchmarks/KernSmith.Benchmarks/KernSmith.Benchmarks.csproj -c Release -f net10.0
```

The project multi-targets, so `-f` is required. Add `-- --filter <pattern>` to pick classes, and `--job dry` (or `short`) for a quick smoke run. Every class loads its font from `tests/KernSmith.Tests/Fixtures` through `BenchmarkFixtures`, which walks up from the BenchmarkDotNet child process's directory to find it.

### Native rasterizer comparison

The `NativeComparison*` classes compare the Native, StbTrueType and FreeType backends on Roboto-Regular: font load, per-glyph `RasterizeAll` (ASCII at 12/32/72px, Grayscale and None), `GetGlyphMetrics` throughput, and full `BmFont.Generate` (ASCII + Latin-1 at 32px). Per-glyph rows use `OperationsPerInvoke`, so Mean and Allocated are per glyph. The baseline lives in [phase 177](../../plan/phase-177-performance-optimization.md).

```
dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeComparison* --job short
```

### WOFF2 decoding

`Woff2DecodeBenchmarks` times KernSmith's managed Brotli decoder against the BCL's native one on the same stream (Roboto-Regular.woff2), and `BmFont.ReadFontInfo` on the WOFF2 against the plain TTF. Results are in [phase 177](../../plan/phase-177-performance-optimization.md).

```
dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *Woff2Decode*
```

See the [root README](../../README.md) for full project documentation.
