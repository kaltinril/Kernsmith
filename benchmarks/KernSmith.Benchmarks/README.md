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

The `NativeComparison*` classes compare the Native, StbTrueType and FreeType backends on Roboto-Regular, plus GDI and DirectWrite in the `net10.0-windows` build (other builds leave those two out): font load, per-glyph `RasterizeAll` (ASCII at 12/32/72px, Grayscale and None), `GetGlyphMetrics` throughput, and full `BmFont.Generate` (ASCII, and ASCII + Latin-1, at 32px). Per-glyph rows use `OperationsPerInvoke`, so Mean and Allocated are per glyph. The baseline lives in [phase 177](../../plan/phase-177-performance-optimization.md).

```
dotnet run -c Release -f net10.0-windows --project benchmarks/KernSmith.Benchmarks -- --filter *NativeComparison* --job short
```

### Native feature paths

The `NativeFeature*` classes time each Native feature path next to the plain Grayscale baseline and next to FreeType and StbTrueType doing the same work, to find slow Native paths before optimizing. Each row is a `Case` labelled `feature (backend)`; a backend gets a case only when its capabilities advertise the feature, so unsupported combinations are absent rather than failing.

| Class | Measures | Cases |
|-------|----------|-------|
| `NativeFeatureRasterizeBenchmarks` | `RasterizeAll`, printable ASCII, per glyph | Grayscale; SDF (spread 8) at 12/32/72px; MSDF, MTSDF; LCD; Light + hinting; synthetic bold, italic, bold + italic; width 150% and italic angle 30° (Native only); CFF (`SourceSerif4-Regular.otf`); CFF2 variable (`SourceCodeVF-Upright.otf`, wght 900); TrueType variable (`RobotoFlex-Variable.ttf`, wght 900, `gvar`); COLR v0 (`BungeeColor-Regular.ttf`). 32px unless noted. |
| `NativeFeatureMetricsBenchmarks` | `GetGlyphMetrics`, same cases, per glyph | as above |
| `NativeFeatureColorGlyphBenchmarks` | `RasterizeAll` and `GetGlyphMetrics`, 10 glyphs, per glyph | COLR v1 (`ReemKufiInk-Regular.ttf`, U+0660-0669, Native only: FreeType draws v1 as grayscale); CBDT emoji (`NotoColorEmoji.ttf`; FreeType returns the strike's own size, Native scales it to 32px) |
| `NativeFeatureCompositeBenchmarks` | `RasterizeAll` and `GetGlyphMetrics`, per glyph | Roboto's 53 composite glyphs in U+00C0-00FF |
| `NativeFeatureEndToEndBenchmarks` | `BmFont.Generate`, ASCII 32px | outline 2 as effect, outline 2 as `OutlineMode.Vector` (Native only), SDF, MSDF |

```
dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeFeature*
```

### Cold start (JIT, ReadyToRun, NativeAOT)

BenchmarkDotNet reports warm steady state: it runs warm-up iterations, so JIT compilation never shows. `benchmarks/KernSmith.ColdStart` measures what a fresh process pays instead. One process times the 1st, 2nd ... Nth `BmFont.Generate` call (default 50) from font bytes to finished output (the `.fnt` text and every page encoded to PNG in memory), records OS process start to `Main` (best effort, from `Process.StartTime`), the JIT counters from `System.Runtime.JitInfo` after the first call (all zero under NativeAOT), and writes JSON:

```
dotnet run -c Release --project benchmarks/KernSmith.ColdStart -- --backend native --font tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf --size 32 --charset ascii --repeat 50 --out report.json
```

`--charset` takes `ascii` (default), `extended`, `latin` or literal characters. Backends register through reflection-based auto-discovery when dynamic code is available and explicitly (`RasterizerFactory.Register`) under NativeAOT, which removes that reflection; `--registration auto|explicit` overrides it. A failure (an unsupported backend, a backend that cannot load under AOT) is reported in the JSON with `"ok": false` and exit code 1.

`run_coldstart.py` (Python 3, standard library only) publishes the tool once per mode — framework-dependent JIT, `-p:PublishReadyToRun=true`, and `-p:PublishAot=true` (needs the platform's native toolchain; on Windows the Visual Studio C++ build tools, and the script puts the VS Installer folder on `PATH` because `vcvarsall.bat` runs `vswhere.exe` by bare name) — into `bin/coldstart-publish/<mode>`, then launches K fresh processes (default 20) per backend × mode round-robin, and writes `summary.txt`, `summary.csv`, `summary.json`, `runs.csv` and every per-process JSON to `bin/coldstart-results/<timestamp>/`. The table gives, per backend × mode: median and worst first call, median second call, median steady state (calls 30-50), the first/steady ratio, OS process start to first result, the same from the driver's wall clock (an upper bound that includes process teardown), and JIT methods and compile time after the first call. A failed publish or run is recorded as failed, not fatal; the script exits 1 if anything failed.

```
python benchmarks/KernSmith.ColdStart/run_coldstart.py                       # all modes, all backends, K=20, N=50
python benchmarks/KernSmith.ColdStart/run_coldstart.py --runs 2 --repeat 5   # smoke
python benchmarks/KernSmith.ColdStart/run_coldstart.py --skip-publish --modes aot --backends native,freetype
```

The repo's decision not to ship ReadyToRun (`project_decisions.md`, Tooling) is about how KernSmith's own packages are built. This tool measures something else: what a consumer app gets when it publishes itself as ReadyToRun or NativeAOT, which precompiles KernSmith along with the app whatever the packages ship.

What it found (Roboto ASCII 32px, K=20, full table in [phase 177](../../plan/phase-177-performance-optimization.md#measured-results)): under JIT the first `Generate` takes 89-111 ms depending on the backend, 10-15x its steady state, most of it compiling 830-1,170 methods. ReadyToRun halves that (38-51 ms) and NativeAOT removes it (3.7-6.9 ms, 1-2x steady). Process start to first result is 146-169 ms (JIT), 93-104 ms (ReadyToRun) and 17-20 ms (NativeAOT). An app that cares about first-call latency — a CLI, a serverless function, anything that generates one font per process — should publish with ReadyToRun or NativeAOT; a long-running app pays the JIT cost once.

### WOFF2 decoding

`Woff2DecodeBenchmarks` times KernSmith's managed Brotli decoder against the BCL's native one on the same stream (Roboto-Regular.woff2), and `BmFont.ReadFontInfo` on the WOFF2 against the plain TTF. Results are in [phase 177](../../plan/phase-177-performance-optimization.md).

```
dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *Woff2Decode*
```

See the [root README](../../README.md) for full project documentation.
