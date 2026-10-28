# KernSmith.Benchmarks

Performance benchmarks for the KernSmith library using BenchmarkDotNet.

## Overview

This project measures the performance of key KernSmith operations including font parsing, glyph rasterization, atlas packing, and output formatting.

## Running

```
dotnet run --project benchmarks/KernSmith.Benchmarks/KernSmith.Benchmarks.csproj -c Release
```

### Native rasterizer comparison

The `NativeComparison*` classes compare the Native, StbTrueType and FreeType backends on Roboto-Regular: font load, per-glyph `RasterizeAll` (ASCII at 12/32/72px, Grayscale and None), `GetGlyphMetrics` throughput, and full `BmFont.Generate` (ASCII + Latin-1 at 32px). Per-glyph rows use `OperationsPerInvoke`, so Mean and Allocated are per glyph. The baseline lives in [phase 177](../../plan/phase-177-performance-optimization.md).

```
dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeComparison* --job short
```

See the [root README](../../README.md) for full project documentation.
