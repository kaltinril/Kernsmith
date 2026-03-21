# Phase 03 — Ecosystem

> **Status**: Complete
> **Original doc**: plan-implementation-order.md
> **Date**: 2026-03-19

---

Goal: WOFF support, channel packing, color fonts, CLI tool, benchmarks, NuGet publishing, font subsetting.

### Group M — Format Support

| ID  | Task                    | Depends On       | Status | Description                                                                                        | Docs to Read                                                                  |
|-----|-------------------------|-----------------|--------|----------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------|
| 13A | **WOFF decompression**  | Phase 2 complete | DONE | Implement WOFF/WOFF2 header parsing and zlib/Brotli decompression to extract inner TTF/OTF          | reference/REF-04-other-font-formats-reference.md (WOFF/WOFF2 sections)    |
| 13B | **Color font support**  | Phase 2 complete | MOVED | COLRv0/CPAL layer rendering, sbix bitmap extraction, CBDT/CBLC bitmap extraction                    | Moved to [plan-phase-future.md](plan-phase-future.md)    |
| 13C | **Font subsetting**     | Phase 2 complete | MOVED | Strip unused glyphs from font data before processing (reduces memory for large CJK fonts)           | Moved to [plan-phase-future.md](plan-phase-future.md)                |

### Group N — Advanced Atlas Features

| ID  | Task                    | Depends On       | Status | Description                                                                       | Docs to Read                                                                                                         |
|-----|-------------------------|-----------------|--------|-----------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------|
| 14A | **Channel packing**     | Phase 2 complete | DONE | Pack monochrome glyphs into individual RGBA channels for 4x density                | reference/REF-06-texture-packing-reference.md (Channel Packing section), reference/REF-05-bmfont-format-reference.md (Channel section) |

### Group O — Tooling + Publishing

| ID  | Task                        | Depends On                       | Status | Description                                                                                          | Docs to Read                 |
|-----|-----------------------------|----------------------------------|--------|------------------------------------------------------------------------------------------------------|------------------------------|
| 15A | **Reference CLI tool**      | Phase 2 complete                 | DONE | Simple CLI wrapper: `KernSmith generate -f font.ttf -s 32 -o output/`                                | New plan doc needed          |
| 15B | **Performance benchmarks**  | Phase 2 complete                 | DONE | Benchmark suite: measure time/memory for ASCII set at various sizes, compare MaxRects vs Skyline      | New plan doc needed          |
| 15C | **NuGet publishing**        | Phase 2 complete, license decided | MOVED | Configure CI for NuGet pack + push, README, package icon                                             | Moved to [plan-phase-future.md](plan-phase-future.md)    |

### Group P — Phase 3 Tests

| ID  | Task                        | Depends On | Status | Description                                                              | Docs to Read     |
|-----|-----------------------------|-----------|--------|--------------------------------------------------------------------------|-----------------|
| 16A | **Tests: WOFF**             | 13A       | DONE | Load WOFF/WOFF2 font, verify decompression and generation                 | plan-testing.md |
| 16B | **Tests: channel packing**  | 14A       | DONE | Verify channel-packed atlas has glyphs in correct channels                 | plan-testing.md |
| 16C | **Tests: CLI**              | 15A       | MOVED | End-to-end CLI invocation tests                                           | Moved to [plan-phase-future.md](plan-phase-future.md) |

### Phase 3 Parallelism

```
Phase 2 complete
       |
       +───────────────────────────+
       |           |           |    |
    Group M     Group N     Group O
   13A 13B 13C    14A      15A 15B 15C
       |           |           |
       v           v           v
            Group P (tests)
           16A  16B  16C
```
