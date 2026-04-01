# Phase 31 — WASM Platform Restrictions Research

> **Status**: Done
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction — needed so core is native-free)
> **Blocks**: Phase 32 (StbTrueType rasterizer), Phase 33 (WASM validation)
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)

## Goal

Comprehensive research and documentation of all platform restrictions, limitations, and gotchas for running .NET code in WASM/Blazor/AOT environments, specifically as they affect KernSmith.

## Why This Phase Exists

Phase 30 extracts FreeType from core, and Phase 32 builds a managed rasterizer. But before building, we need to understand ALL the constraints — not just "no native DLLs." There are many subtle restrictions around threading, file I/O, reflection, trimming, memory, and APIs that could cause runtime failures. This research phase ensures we design the managed rasterizer correctly the first time.

## Research Areas

### 1. Native Interop Restrictions

- P/Invoke and DllImport are blocked in browser WASM
- LibraryImport (source-generated) — also blocked?
- NativeLibrary.SetDllImportResolver — available?
- How to programmatically verify no native dependencies in an assembly (reflection scanning for DllImport/LibraryImport attributes)
- How to add CI checks that catch native dependency regressions

### 2. File System Restrictions

- System.IO.File APIs — which work, which throw?
- DefaultSystemFontProvider uses filesystem scanning and Win32 Registry — will crash on WASM
- BmFontResult.ToFile() uses File.WriteAllBytes — unusable in browser
- Font loading from InputFile component vs filesystem
- How to handle "save" operations (JavaScript interop for file downloads)
- Virtual filesystem (emscripten VFS) — available in .NET WASM?

### 3. Threading Model

- Blazor WASM is single-threaded by default
- WasmEnableThreads requires SharedArrayBuffer and COOP/COEP headers
- Task.Run, Parallel.ForEach, ThreadPool — do they work or deadlock?
- Does KernSmith use any parallelism internally? (Check BmFont.cs, AtlasBuilder, etc.)
- Async/await — works but runs on the same thread
- Implications for RasterizeAll batch operations

### 4. Memory Constraints

- Default WASM heap: 256 MB (configurable)
- Large font rasterization memory usage (CJK fonts with 20,000+ glyphs)
- GC behavior differences in WASM runtime
- ArrayPool/buffer reuse — works the same?
- Pinned memory (GCHandle) — works in WASM?

### 5. IL Trimming

- Blazor WASM publishes with trimming enabled by default
- Reflection-based code gets trimmed (JSON serialization in BmFontBinaryFormatter, BmFontReader)
- TrimmerSingleWarn=false to surface all warnings
- [DynamicallyAccessedMembers] and [RequiresUnreferencedCode] attributes
- Source generators vs reflection for trim-safe code
- Phase 90 (AOT compliance) overlap — document relationship

### 6. AOT Compilation

- RunAOTCompilation vs PublishTrimmed — distinct concerns
- AOT increases build time dramatically (minutes vs seconds)
- wasm-tools workload requirement: `dotnet workload install wasm-tools`
- Interpreted vs AOT WASM — different failure modes
- Dynamic code generation (Reflection.Emit, Expression.Compile) — blocked in AOT
- Does KernSmith use any dynamic code generation?

### 7. API Availability

- Which .NET APIs throw PlatformNotSupportedException in WASM?
- System.Runtime.InteropServices — partially available
- Microsoft.Win32.Registry — not available
- System.Diagnostics.Process — not available
- System.Drawing — not available (GDI rasterizer dependency)
- Span<T> and Memory<T> — fully available
- BinaryPrimitives — fully available (critical for TTF parsing)
- StbImageWriteSharp — pure managed, should work (verify)

### 8. StbTrueTypeSharp Specific WASM Concerns

- Uses `unsafe` extensively — works in WASM AOT?
- Pointer arithmetic in WASM — any precision/alignment differences?
- stbtt_GetCodepointBitmap allocates via Marshal — works in WASM?
- Memory freeing pattern (stbtt_FreeBitmap equivalent)
- SDF rendering (stbtt_GetCodepointSDF) — any WASM-specific issues?

### 9. Blazor Component Integration

- InputFile component: default 512 KB MaxFileSize — fonts can be 5-20 MB
- File download via JS interop (no File.WriteAll)
- Blazor render cycle and long-running operations (should use Task.Yield or similar)
- Progress reporting for large font generation

### 10. Testing in WASM

- Can xUnit tests run directly in WASM? (wasm-experimental workload)
- Playwright/Selenium for browser E2E testing
- bUnit for component testing (runs on server CLR, NOT WASM — limited value)
- CI integration: dotnet publish for browser-wasm as a build gate
- Assembly scanning tests (verify no DllImport attributes)

## Deliverables

1. **Research document** — `reference/REF-11-wasm-restrictions.md` with all findings organized by category
2. **KernSmith impact assessment** — which specific files/APIs are affected and how
3. **Design constraints checklist** — concrete requirements for Phase 32 (StbTrueType rasterizer) and Phase 33 (WASM validation)
4. **CI gate proposal** — how to prevent native dependency regressions in CI

## Success Criteria

- [ ] All 10 research areas documented with findings
- [ ] Each finding includes: what the restriction is, how it affects KernSmith, and the mitigation strategy
- [ ] Design constraints checklist is concrete enough to guide Phase 32 implementation
- [ ] CI gate proposal is actionable

## References

- [Blazor WASM hosting model](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models)
- [IL trimming for Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/configure-trimmer)
- [WASM build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot)
- [Blazor file downloads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads)
- [.NET WASM threading](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/import-export-interop)
- [Playwright .NET](https://playwright.dev/dotnet/)
- [bUnit](https://bunit.dev/)
- [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp)
- [Phase 90 — AOT compliance](phase-90-aot-compliance.md) — overlapping concerns
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)

## Estimated Effort

Research only — no code changes. 1-2 sessions.
