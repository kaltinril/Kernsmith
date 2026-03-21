# Phase 14 — Benchmarking & Profiling Enhancement

> **Status**: Complete
> **Created**: 2026-03-20
> **Goal**: Build comprehensive benchmarking and profiling infrastructure that covers every pipeline stage, enables performance regression detection, and provides actionable profiling data for optimization work.

---

## Current State

The project has minimal benchmarking infrastructure:

- **4 BenchmarkDotNet scenarios** in `benchmarks/Bmfontier.Benchmarks/FontGenerationBenchmarks.cs`:
  - ASCII 32px with MaxRects packer
  - ASCII 32px with Skyline packer
  - ExtendedAscii 32px
  - ASCII 64px
- **`[MemoryDiagnoser]`** enabled on the single benchmark class for allocation tracking
- **`BenchmarkSwitcher`** with CLI argument support (`--filter`, `--job`)
- **No profiling** — no stage-level timing, no CPU trace capture
- **No CI integration** — benchmarks are not part of any GitHub Actions workflow
- **No baselines** — no stored reference results for regression detection

## Target State

- Comprehensive benchmark suite covering every pipeline stage and feature dimension
- Stage-level profiling via opt-in `PipelineMetrics` attached to `BmFontResult`
- CI workflow that runs a representative benchmark subset and detects >10% regressions
- Exportable JSON reports for trend tracking
- CLI `--profile` flag for user-facing timing breakdowns

---

## Phases

### Phase 1 — Expand Benchmark Scenarios

Add benchmark classes that cover the full feature matrix. Each benchmark class should focus on one dimension.

Tasks:
- [ ] **RasterizationBenchmarks** — isolate rasterization cost
  - ASCII 32px (baseline)
  - ASCII 64px, 128px (scaling with size)
  - Extended ASCII, Latin (scaling with glyph count)
  - SDF rendering vs normal
  - Mono/Light/LCD anti-aliasing modes
  - Variable font with axis variations
  - Color font (COLR/CPAL emoji)
  - Bold + Italic synthesis
  - Super sampling 1x vs 2x vs 4x
  - MatchCharHeight (two-pass rendering)
- [ ] **EffectsBenchmarks** — isolate effect compositing cost
  - Outline only (varying width: 1, 3, 6 pixels)
  - Gradient only (0, 45, 90, 135 degree angles)
  - Shadow only (varying blur: 0, 2, 4, 8)
  - Full stack: outline + gradient + shadow combined
  - Effects with different glyph counts (1, 95, 431 chars)
- [ ] **PackingBenchmarks** — isolate packer performance
  - MaxRects vs Skyline with 95 glyphs (ASCII)
  - MaxRects vs Skyline with 431 glyphs (Latin)
  - MaxRects vs Skyline with 1000+ glyphs (CJK subset)
  - Different max texture sizes (256, 512, 1024, 2048)
  - Power-of-two vs non-power-of-two
  - Autofit mode
- [ ] **EncodingBenchmarks** — isolate atlas encoding cost
  - PNG vs TGA vs DDS encoding
  - Small atlas (256x256) vs large (2048x2048)
  - Grayscale vs RGBA pixel format
- [ ] **EndToEndBenchmarks** — full pipeline, realistic scenarios
  - "Game UI font": ASCII, 32px, outline, gradient (typical game dev use case)
  - "Dialogue font": Latin, 24px, no effects (typical visual novel)
  - "Title font": ASCII, 96px, outline + shadow + gradient + super-sample 4x (hero text)
  - "Emoji sheet": Color font, COLR palette, 48px
  - "SDF atlas": ASCII, 48px, SDF mode
  - "Channel packed": ASCII, 32px, channel packing (4-in-1)
  - "Variable font": Variable font with custom axes
  - "Maximum stress": Extended charset, 128px, all effects, super-sample 4x, skyline packer
- [ ] **FontParsingBenchmarks** — isolate font reading
  - Small font (Roboto 299KB)
  - Variable font (RobotoFlex 1.8MB)
  - Large font (NotoColorEmoji 11MB)
  - WOFF decompression (if test WOFF files available)
  - Kerning pair extraction (GPOS parsing)

### Phase 2 — Stage-Level Profiling Infrastructure

Add internal timing instrumentation so individual pipeline stages can be measured without external profiling tools.

Tasks:
- [ ] **Add `PipelineMetrics` class** — lightweight timing data for each stage
  - Stages: FontParsing, CharsetResolution, Rasterization, EffectsCompositing, PostProcessing, SuperSampleDownscale, CellEqualization, AtlasSizeEstimation, AtlasPacking, AtlasEncoding, ModelAssembly
  - Each stage records: elapsed time (`Stopwatch`), allocation delta (optional)
  - Attached to `BmFontResult` so callers can inspect performance
  - Zero-overhead when not requested (opt-in via `FontGeneratorOptions.CollectMetrics` flag)
- [ ] **Instrument `BmFont.Generate()`** — wrap each pipeline stage with timing
- [ ] **Add `[EventPipeProfiler("cpu-sampling")]`** attribute to key benchmark classes for CPU flamegraph capture
- [ ] **Add benchmark for metrics overhead** — verify `CollectMetrics` adds <1% overhead

### Phase 3 — Memory & Allocation Profiling

Understand and track allocation patterns across the pipeline.

Tasks:
- [ ] **Add `[MemoryDiagnoser]` to ALL benchmark classes** (currently only on FontGenerationBenchmarks)
- [ ] **Add allocation-focused benchmarks**
  - Measure per-glyph allocation (`byte[]` for bitmap data)
  - Measure atlas page allocation (large `byte[]` arrays)
  - Measure model assembly allocation (string allocations in .fnt formatting)
  - Track GC pressure: Gen0/Gen1/Gen2 collections per scenario
- [ ] **Add `[ThreadingDiagnoser]`** for thread/lock contention analysis
- [ ] **Investigate ArrayPool usage** — can hot-path allocations use pooled arrays?

### Phase 4 — Comparative Benchmarks

Side-by-side comparisons to guide user choices and internal optimization.

Tasks:
- [ ] **Packer showdown** — MaxRects vs Skyline across glyph counts (10, 50, 100, 500, 1000, 5000) with efficiency % AND speed
- [ ] **Encoder showdown** — PNG vs TGA vs DDS at various atlas sizes, measuring both encode time and file size
- [ ] **AA mode comparison** — None vs Grayscale vs Light vs LCD, quality vs speed tradeoff
- [ ] **Super sample scaling** — 1x vs 2x vs 3x vs 4x, diminishing returns analysis
- [ ] **Effect cost matrix** — individual effect cost + combined cost, to show non-linear scaling

### Phase 5 — CI Benchmark Integration & Regression Detection

Make benchmarks part of the CI pipeline to catch performance regressions.

Tasks:
- [ ] **Create `benchmark.yml` GitHub Actions workflow**
  - Trigger: on push to main, on PR (optional/manual)
  - Run on ubuntu-latest (consistent environment)
  - Execute key benchmark subset (not full suite — too slow)
  - Export results as JSON artifacts
- [ ] **Select regression detection benchmarks** — pick 5-8 representative scenarios that cover the critical path
  - ASCII 32px end-to-end (baseline)
  - Latin 32px with effects (effect pipeline)
  - MaxRects packing 431 glyphs (packer)
  - PNG encoding 1024x1024 (encoder)
  - SDF rendering (SDF path)
- [ ] **Add `BenchmarkDotNet.Exporters.Json`** for machine-readable results
- [ ] **Store baseline results** in `benchmarks/baselines/` for comparison
- [ ] **Add PR comment with benchmark comparison** using `github-action-benchmark` or similar

### Phase 6 — CLI Profiling & Startup Optimization

The CLI currently pays ~1100ms .NET runtime startup cost per invocation (JIT compilation, assembly loading, FreeType native library initialization). Actual font generation is 5-50ms but is invisible to users. This phase makes real generation time visible and reduces perceived latency.

**Evidence**: Running 18 identical-complexity tests via `test_comparison.bat` shows every test takes ~1150ms regardless of font size, glyph count, or features — proving the time is dominated by startup, not generation.

Tasks:
- [ ] **Add `--time` flag to CLI generate command** — prints wall-clock generation time (excluding startup) after completion
  - Use `Stopwatch` around the `BmFont.Generate()` call only
  - Output format: `Generated in 12ms (95 glyphs, 1 page)`
  - Lightweight — no dependency on `PipelineMetrics`
- [ ] **Add `--profile` flag to CLI generate command** — prints full stage-level timing breakdown after generation
  - Requires `PipelineMetrics` from Phase 2
  - Output format: human-readable table with stage name, elapsed ms, % of total
  - Example:
    ```
    Stage                    Time      %
    ----------------------- ------- -----
    Font parsing              2ms    4.1%
    Charset resolution        0ms    0.0%
    Rasterization            32ms   65.3%
    Atlas packing             3ms    6.1%
    Atlas encoding (PNG)     11ms   22.4%
    Model assembly            1ms    2.0%
    ----------------------- ------- -----
    Total generation         49ms
    Process wall time      1152ms
    ```
  - The `Process wall time` vs `Total generation` gap reveals startup overhead
- [ ] **Add `benchmark` CLI command** — runs a quick self-benchmark with the user's font and options
  - Runs generation N times (default 10), reports min/mean/max/stddev
  - First run is warmup (JIT), excluded from stats
  - Output: `Mean: 12ms, Min: 11ms, Max: 15ms, StdDev: 1.2ms (10 iterations)`
- [ ] **~~Investigate AOT publishing~~** — DEFERRED
  - Requires MSVC linker (vswhere.exe) on PATH and 4 JSON source generator changes
  - Not worth the effort given R2R results and .NET 10 JIT performance
  - Revisit if startup time becomes a user complaint
- [x] **~~Investigate ReadyToRun (R2R)~~** — REJECTED
  - Tested on .NET 10: R2R is **15% slower** than JIT (25,170ms vs 21,830ms over 18 tests)
  - .NET 10's tiered JIT already optimizes aggressively enough that R2R pre-compiled code is a net negative
  - R2R cannot perform runtime-specific optimizations (CPU feature detection, profile-guided inlining)
  - Decision: do not use R2R for this project
- [ ] **Investigate batch mode** — accept multiple generation jobs in a single invocation
  - Pay startup cost once, run N generations
  - Input: multiple `--job` arguments, or a jobs file (JSON/YAML)
  - Useful for asset pipelines that generate many fonts at build time
  - Example: `bmfontier batch jobs.json` or `bmfontier generate --font A -s 32 --and --font B -s 24`

---

## Architectural Considerations

### Benchmark Isolation

Each benchmark class should test ONE dimension. End-to-end benchmarks are for realistic scenarios only. Stage-level benchmarks must isolate their target by pre-computing inputs.

### BenchmarkDotNet Best Practices

- Use `[GlobalSetup]` for font loading and one-time initialization
- Use `[IterationSetup]` sparingly (only when state must reset per iteration)
- Use `[Params]` attribute for parameterized benchmarks (font size, glyph count, etc.)
- Use `[BenchmarkCategory]` for filtering in CI
- Keep benchmark methods minimal — return a value to prevent dead-code elimination

### Profiling Without External Tools

`[EventPipeProfiler]` captures CPU traces that can be viewed in PerfView or speedscope without installing profiling tools on the machine. This is the recommended approach for CI flamegraphs.

### Metrics Overhead

`PipelineMetrics` must be zero-cost when disabled. Use `Stopwatch` only when `CollectMetrics == true`. Do NOT add conditional checks in hot loops — gate at the stage level only.

### Test Font Strategy

- **Roboto-Regular.ttf** (299KB): Primary benchmark font — small, fast to load, good baseline
- **RobotoFlex-Variable.ttf** (1.8MB): Variable font benchmarks
- **NotoColorEmoji.ttf** (11MB): Color font / large font benchmarks
- All already in `tests/Bmfontier.Tests/Fixtures/` — benchmarks should reference them via relative path

---

## Success Criteria

1. **Coverage**: Every pipeline stage has at least one dedicated benchmark
2. **Regression detection**: CI catches >10% regressions on critical-path benchmarks
3. **Actionable profiling**: Stage-level timing available via opt-in flag, no external tools needed
4. **Low overhead**: Metrics collection adds <1% to total generation time
5. **Developer experience**: `dotnet run --project benchmarks/Bmfontier.Benchmarks -- --filter *Effects*` works out of the box
6. **Startup transparency**: `--time` flag shows users that generation is fast (5-50ms) despite ~1100ms process wall time
7. **Startup reduction**: ~~AOT or R2R publishing reduces CLI cold-start from ~1100ms to <200ms~~ — R2R rejected (slower on .NET 10), AOT deferred
8. **Batch efficiency**: Asset pipelines can generate 100 fonts in a single invocation, paying startup cost only once

---

## Related Plans

- **phase-11-solution-restructure.md** — established the benchmark project structure
- **phase-12-pre-ship-polish.md** — independent, no overlap
- **phase-13-wasm-rasterization.md** — WASM rasterizer benchmarks would be added later
