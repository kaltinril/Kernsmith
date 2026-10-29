# KernSmith.Benchmarks

Performance benchmarks for the KernSmith library using BenchmarkDotNet.

## Overview

This project measures the performance of key KernSmith operations including font parsing, glyph rasterization, atlas packing, and output formatting.

## Running

```
dotnet run --project benchmarks/KernSmith.Benchmarks/KernSmith.Benchmarks.csproj -c Release -f net10.0
```

The project multi-targets, so `-f` is required. Add `-- --filter <pattern>` to pick classes, and `--job dry` (or `short`) for a quick smoke run. Every class loads its font from `tests/KernSmith.Tests/Fixtures` through `BenchmarkFixtures`, which walks up from the BenchmarkDotNet child process's directory to find it.

See the [root README](../../README.md) for full project documentation.
