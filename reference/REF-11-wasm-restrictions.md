# WASM Restrictions Reference for KernSmith

This document compiles research findings from Phase 31 on .NET Blazor WebAssembly restrictions that affect KernSmith. The goal is to ensure the core KernSmith library (and specifically the Phase 32 StbTrueType managed rasterizer) can run in browser WASM without runtime failures. These findings inform design constraints for all WASM-compatible code paths and CI gates to prevent regressions.

---

## 1. Native Interop Restrictions

### Findings

- P/Invoke and `DllImport` are **not blocked outright** but only work with WASM-compiled native code (Emscripten-compiled, statically linked via `NativeFileReference`).
- Attempting to P/Invoke a library not compiled to WASM produces `EntryPointNotFoundException` at runtime.
- `LibraryImport` (source-generated) has the same restrictions as `DllImport`.
- `NativeLibrary.SetDllImportResolver` is available as API surface but functionally limited -- can only resolve libraries statically linked into `dotnet.wasm`.
- Detection approach: scan assemblies for `DllImport`/`LibraryImport` attributes using `MetadataLoadContext` or the `MethodAttributes.PinvokeImpl` flag.
- CI options: `dotnet publish` for `browser-wasm` as a build gate, assembly scanning unit tests, CA1416 platform compatibility analyzer.

### KernSmith Impact

FreeTypeSharp uses `[DllImport("freetype")]` extensively. It will **not** work in browser WASM without compiling FreeType to WASM via Emscripten -- a major undertaking. This is the primary motivation for Phase 30 extracting FreeType to a plugin package, keeping the core library free of native dependencies.

### Mitigation

- Phase 30 extracts FreeType usage into a separate plugin package (`KernSmith.Rasterizer.FreeType`).
- The core `KernSmith` package must contain zero `DllImport`/`LibraryImport` attributes.
- CI assembly scanning test enforces this boundary.

---

## 2. File System Restrictions

### Findings

- `System.IO.File` APIs **do work** but operate against Emscripten's MEMFS virtual filesystem (in-memory, non-persistent, lost on page refresh).
- `File.ReadAllBytes`, `File.WriteAllBytes`, `Directory.GetFiles`, and `FileStream` all work against MEMFS.
- `FileSystemWatcher`, `System.IO.Pipes`, and `DriveInfo` throw `PlatformNotSupportedException`.
- No access to the user's real local filesystem -- the browser sandbox prevents this.
- OPFS (Origin Private File System) is developing but .NET does not support it natively yet.
- Save operations require JS interop with `DotNetStreamReference` to trigger browser file downloads.
- Font loading in Blazor uses the `InputFile` component with `IBrowserFile.OpenReadStream()`.

### KernSmith Impact

- `DefaultSystemFontProvider` uses filesystem scanning and Win32 Registry -- will crash in WASM.
- `BmFontResult.ToFile()` uses `File.WriteAllBytes` -- works in MEMFS but not for actual user-facing file saves.
- Blazor apps need a JS interop download pattern instead of direct filesystem writes.

### Mitigation

- `DefaultSystemFontProvider` must not be referenced in WASM-targeted code paths. Font data should come from byte arrays or streams.
- Provide `ToStream()` / `ToBytes()` alternatives alongside `ToFile()` so Blazor apps can use JS interop for downloads.
- Document the `InputFile` + `OpenReadStream()` pattern for Blazor consumers.

---

## 3. Threading Model

### Findings

- Blazor WASM is **single-threaded by default** -- a hard constraint of the browser JS/WASM execution model.
- `WasmEnableThreads` requires `SharedArrayBuffer` + COOP/COEP headers; experimental in .NET 8-9, work-in-progress for .NET 10.
- `Task.Run` does **not** offload to a separate thread -- it schedules on the same thread.
- `Parallel.ForEach` throws `PlatformNotSupportedException` (`Monitor.Wait` not supported) -- tracked in dotnet/runtime#43411.
- `ThreadPool` exists as API surface but has no real threads backing it in single-threaded mode.
- `async/await` works but everything runs on the same thread -- this is the **recommended** pattern for Blazor WASM.
- `.Result`, `.Wait()`, `.WaitAll()`, and `Thread.Sleep` **deadlock** the single thread. Never use these in WASM-compatible code.

### KernSmith Impact

- Must audit `BmFont.cs`, `AtlasBuilder`, and all internal code for `Parallel.ForEach`, `Task.Wait()`, `.Result`, or `Thread.Sleep`.
- Batch rasterization operations (`RasterizeAll`) must be async with periodic `Task.Delay(1)` yields for UI responsiveness.

### Mitigation

- Replace any `Parallel.ForEach` with sequential `foreach` (or async equivalent with yields).
- Provide async API variants that yield periodically.
- Never use blocking synchronization primitives in the core library.

---

## 4. Memory Constraints

### Findings

- Default WASM heap is approximately 127 MB, grows dynamically, configurable via `EmccMaximumHeapSize` (practical limit ~1.7-2 GB).
- WASM memory can **never shrink** -- once the heap grows, it stays until page reload.
- GC runs but reclaims managed objects internally; the underlying WASM linear memory never returns to the OS.
- GC mode is Batch (workstation-like only), no server GC.
- .NET 9 has confirmed GC bugs (not collecting aggressively enough) -- workaround: `GC.GetTotalMemory(true)`.
- `ArrayPool<T>` works the same but is more important in WASM (buffer reuse prevents permanent heap growth).
- `GCHandle.Alloc` with `Pinned` works but fragments the heap permanently.
- `Marshal.AllocHGlobal`/`FreeHGlobal` work -- they map to Emscripten `malloc`/`free`.
- CJK font estimate: 200-500 MB total (font + glyphs + atlas). Within limits but significant. Mobile Safari may cap at 256-512 MB.

### KernSmith Impact

- Process glyphs in batches and return `ArrayPool` buffers promptly.
- Avoid holding all glyph bitmaps simultaneously.
- Consider streaming/chunked processing for large character sets.
- Mobile targets may need reduced atlas sizes.

### Mitigation

- Use `ArrayPool<byte>` for all glyph bitmap buffers in the managed rasterizer.
- Free unmanaged allocations immediately after copying to managed arrays.
- Support chunked processing mode for CJK and other large character sets.
- Document mobile memory limits for consumers.

---

## 5. IL Trimming

### Findings

- Blazor WASM trims on every Release publish automatically (implicit `PublishTrimmed`).
- Default `TrimMode` is `partial` (only framework + opt-in libraries), **not** `full`.
- Reflection-based code gets trimmed -- JSON serialization and custom formatters are affected.
- Known example: `System.Text.Json` + `Tuple` deserialization breaks after trimming.
- Use `[DynamicallyAccessedMembers]` and `[RequiresUnreferencedCode]` attributes to preserve types.
- `TrimmerSingleWarn=false` shows all detailed warnings (default collapses to one per assembly).
- Source generators are the primary recommended pattern for trim-safe code.
- Library authors should set `IsTrimmable=true` in csproj and use `EnableTrimAnalyzer=true`.
- Test trim compatibility by creating a trim test app with `TrimmerRootAssembly`.

### KernSmith Impact

- `BmFontBinaryFormatter` and `BmFontReader` may use reflection for serialization -- verify.
- TTF table parsers use direct struct reads (trim-safe).
- Consider adding `IsTrimmable` to `KernSmith.csproj`.

### Mitigation

- Audit all serialization code for reflection usage; replace with source-generated alternatives.
- Add `<IsTrimmable>true</IsTrimmable>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` to the core library csproj.
- Use `[DynamicallyAccessedMembers]` where reflection is unavoidable.

---

## 6. AOT Compilation

### Findings

- `RunAOTCompilation` (Blazor WASM) and `PublishAot` (desktop Native AOT) are **distinct** properties.
- The `wasm-tools` workload is required: `dotnet workload install wasm-tools`.
- Interpreted WASM: smaller download, slower execution, more compatible. AOT WASM: ~2x download size, much faster execution, some restrictions.
- `Reflection.Emit` is **not supported** in AOT. `Expression.Compile()` falls back to interpreted (slower but works). `DynamicMethod` does not work.
- `Type.MakeGenericType()` with unknown types may fail in AOT.
- AOT build takes "several minutes on small projects, much longer for larger."
- `IsAotCompatible=true` enables all analyzers (trim + AOT).

### KernSmith Impact

- Verify KernSmith uses no `Reflection.Emit` or `DynamicMethod`.
- TTF parsing and rasterization are primarily compute-bound -- AOT would significantly improve performance.
- Consider marking the library as `IsAotCompatible`.

### Mitigation

- Add `<IsAotCompatible>true</IsAotCompatible>` to the core library csproj (also implies trim compatibility).
- Audit for `Reflection.Emit`, `DynamicMethod`, and open generic usage.
- Document that AOT compilation is recommended for WASM consumers doing heavy rasterization.

---

## 7. API Availability

### Findings

- Approximately 4,500 methods throw `PlatformNotSupportedException` in browser WASM (dotnet/runtime#41087).
- **Unsupported:** `System.Diagnostics.Process`, `Microsoft.Win32.Registry`, `System.Drawing` (GDI+ dependency), `System.Net.Sockets`, `System.IO.Pipes`.
- **Fully available:** `Span<T>`, `Memory<T>` (pure managed), `BinaryPrimitives` (critical for TTF parsing), `Marshal` class (managed marshalling methods), `GCHandle`.
- StbImageWriteSharp is a pure managed C# port with zero native dependencies -- should work.

### KernSmith Impact

- `BinaryPrimitives` and `Span<T>` (core of TTF parsing) are safe.
- StbImageWriteSharp (PNG/TGA encoding) is safe.
- Registry usage in `DefaultSystemFontProvider` is a problem.
- Any `System.Drawing` usage must be eliminated from WASM code paths.

### Mitigation

- Ensure `DefaultSystemFontProvider` is isolated in the plugin layer, not the core library.
- Audit all usings for `System.Drawing`, `System.Diagnostics.Process`, and `Microsoft.Win32.Registry`.
- The core TTF parser and managed rasterizer rely on `BinaryPrimitives` and `Span<T>` -- no issues expected.

---

## 8. StbTrueTypeSharp WASM Concerns

### Findings

- `unsafe` code blocks, `fixed` statements, and pointer arithmetic **all work** in WASM (both interpreted and AOT).
- Float precision: IEEE 754 compliant in WASM, no differences from desktop.
- No alignment concerns -- WASM handles unaligned access transparently.
- `Marshal.AllocHGlobal` works (maps to Emscripten `malloc`). Leak risk in the constrained WASM heap.
- `CRuntime.malloc`/`free`/`memcpy` all map to `Marshal` -- all work.
- SDF rendering (`stbtt_GetCodepointSDF`) works but has a known quality bug (StbSharp issue #1, open since 2020).
- Blazor WASM support has been implemented and validated (see `samples/KernSmith.Samples.BlazorWasm/`).
- Performance: interpreter mode is extremely slow for compute-intensive rasterization (5-10 seconds for simple ops based on ImageSharp precedent). AOT or Jiterpreter recommended.
- SafeStbTrueTypeSharp exists but is abandoned (2020) and based on old version 1.24.
- Alternative: Typography (LayoutFarm) -- active, full TTF/OTF reader + renderer.

### KernSmith Impact

- StbTrueTypeSharp is viable for WASM and has been validated in the Blazor WASM sample app.
- All unmanaged allocations must be wrapped in `try/finally`.
- Bitmap data must be copied to managed `byte[]` immediately, then the unmanaged buffer freed.
- Performance optimization (AOT) will be critical for acceptable user experience.

### Mitigation

- Wrap every `Marshal.AllocHGlobal` call in `try/finally` with `Marshal.FreeHGlobal`.
- Copy unmanaged bitmap data to managed arrays immediately after rasterization.
- Test SDF output quality early and document any issues.
- Recommend AOT compilation for consumers doing heavy rasterization.

---

## 9. Blazor Component Integration

### Findings

- `InputFile` default `maxAllowedSize` is 512,000 bytes (500 KB) -- must override for fonts (5-20 MB).
- Override via `file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024)`.
- File download: use `DotNetStreamReference` + JS interop (anchor click pattern).
- Long-running ops freeze the UI -- use `StateHasChanged()` + `await Task.Delay(1)` to yield to the render cycle.
- Do not yield every single glyph -- yield every N iterations (e.g., every 10 glyphs) to avoid yield overhead.
- `Task.Yield()` does **not** reliably yield to the browser rendering pipeline -- use `Task.Delay(1)` instead.
- `Task.Run` is useless in Blazor WASM -- does not create a real thread.

### KernSmith Impact

- A Blazor UI (if built) needs a progress reporting pattern for rasterization.
- `BmFont.Generate()` would need an async variant with a progress callback.
- File output needs a JS interop download wrapper, not `File.WriteAllBytes`.

### Mitigation

- Provide `GenerateAsync(IProgress<GenerationProgress>)` overload on `BmFont`.
- Yield every N glyphs (e.g., 10) during rasterization via `Task.Delay(1)`.
- Document the JS interop file download pattern for Blazor consumers.
- Document the `InputFile` `maxAllowedSize` override for font loading.

---

## 10. Testing in WASM

### Findings

- xUnit cannot run directly in WASM -- no official support.
- No dedicated .NET WASM test frameworks exist (confirmed gap).
- bUnit runs on desktop CLR, **not** in WASM -- cannot catch WASM-specific issues.
- Playwright .NET is the best option for browser E2E testing: launches a real browser and exercises through the DOM.
- Assembly scanning unit test: scan for `DllImport`/`LibraryImport` via `MethodAttributes.PinvokeImpl` -- runs on desktop CLR and catches native dependency leaks.
- `dotnet publish` CI gate: publish a minimal Blazor WASM app referencing KernSmith -- catches linker failures but **not** runtime P/Invoke resolution failures.

### Recommended Layered Approach

| Layer | Effort | Approach | What It Catches |
|-------|--------|----------|-----------------|
| 1 | Low | Assembly scanning unit test for P/Invoke detection | Native dependency leaks |
| 2 | Medium | `dotnet publish` CI gate with Blazor WASM shell app | Linker/trim failures |
| 3 | High | Playwright E2E tests against Blazor WASM test harness | Runtime WASM failures |

### KernSmith Impact

- Standard xUnit tests run on desktop CLR and validate logic but cannot validate WASM-specific behavior.
- A layered CI approach is needed to catch different classes of WASM failures.

### Mitigation

- Implement Layer 1 (assembly scanning) immediately as part of Phase 32.
- Implement Layer 2 (publish gate) when a Blazor WASM project exists.
- Implement Layer 3 (Playwright) when a Blazor UI is built.

---

## Design Constraints Checklist for Phase 32 and Phase 33

Based on all findings above, the Phase 32 `StbTrueType` managed rasterizer must satisfy these constraints:

- [ ] **Zero native dependencies** -- no `DllImport`, no `LibraryImport`, no `NativeLibrary.Load`
- [ ] **No `Parallel.ForEach`** or `Task.Wait()` / `.Result` -- single-threaded WASM deadlocks
- [ ] **No `Thread.Sleep`** -- deadlocks the browser tab
- [ ] **No `System.Drawing`** -- GDI+ not available in WASM
- [ ] **No `Microsoft.Win32.Registry`** -- not available in WASM
- [ ] **No `System.Diagnostics.Process`** -- not available in WASM
- [ ] **Wrap all `Marshal.AllocHGlobal`** in `try/finally` with `Marshal.FreeHGlobal`
- [ ] **Copy unmanaged bitmap data to managed `byte[]` immediately**, then free the unmanaged buffer
- [ ] **Set `IsTrimmable` and `IsAotCompatible`** MSBuild properties in the core library csproj
- [ ] **No `Reflection.Emit` or `DynamicMethod`**
- [ ] **Prefer source-generated JSON** over reflection-based serialization
- [ ] **Provide async API variants** with progress callbacks for Blazor UI responsiveness
- [ ] **Use `ArrayPool<T>`** for buffer reuse (prevents permanent WASM heap growth)
- [ ] **Support streaming/chunked glyph processing** for large character sets (CJK)
- [ ] **Test SDF output quality** (known StbTrueTypeSharp bug, StbSharp issue #1)

> **Note:** The CI Gate Proposal section below serves as the Phase 33 (WASM Validation) requirements.

---

## CI Gate Proposal

### Layer 1: Assembly Scanning Unit Test (Immediate)

Add an xUnit test that scans `KernSmith.dll` for methods with the `MethodAttributes.PinvokeImpl` flag. The test should:

- Load the assembly via `MetadataLoadContext` (avoids executing any code).
- Enumerate all methods across all types.
- Flag any method with `PinvokeImpl` set.
- **Pass** if zero P/Invoke methods are found in the core library.
- **Fail** if any P/Invoke methods exist outside the FreeType plugin package.

### Layer 2: Blazor WASM Publish Gate (When Blazor Project Exists)

Add a CI step that:

- Creates or maintains a minimal Blazor WASM app that references `KernSmith` core.
- Runs `dotnet publish -c Release` targeting `browser-wasm`.
- Catches IL trimming failures, missing API surfaces, and linker errors.
- Does **not** catch runtime `PlatformNotSupportedException` or `EntryPointNotFoundException`.

### Layer 3: Playwright E2E Tests (When Blazor UI Is Built)

Add Playwright .NET tests that:

- Launch a real browser (Chromium) against the Blazor WASM test harness.
- Exercise font loading, rasterization, and atlas generation through the UI.
- Validate that the full pipeline completes without errors in a real WASM environment.
- Catch runtime failures that Layers 1 and 2 cannot detect.

---

## Sources

- [Blazor WASM hosting model](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models)
- [IL trimming for Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/configure-trimmer)
- [WASM build tools and AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot)
- [Blazor file downloads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads)
- [Blazor file uploads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-uploads)
- [Blazor synchronization context](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context)
- [CA1416 platform compatibility](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1416)
- [MetadataLoadContext](https://learn.microsoft.com/en-us/dotnet/standard/assembly/inspect-contents-using-metadataloadcontext)
- [Blazor native dependencies](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies)
- [Unsupported browser APIs design doc](https://github.com/dotnet/designs/blob/main/accepted/2020/blazor-unsupported-apis/blazor-unsupported-apis.md)
- [Mark APIs unsupported on browser (runtime#41087)](https://github.com/dotnet/runtime/issues/41087)
- [Parallel.For in Blazor WASM (runtime#43411)](https://github.com/dotnet/runtime/issues/43411)
- [WASM multithreading tracking (runtime#68162)](https://github.com/dotnet/runtime/issues/68162)
- [Blazor WASM multithreaded runtime (aspnetcore#54365)](https://github.com/dotnet/aspnetcore/issues/54365)
- [WASM memory limits (aspnetcore#22694)](https://github.com/dotnet/aspnetcore/issues/22694)
- [Blazor WASM GC issues (runtime#110100)](https://github.com/dotnet/runtime/issues/110100)
- [WASM memory never freed (runtime#64047)](https://github.com/dotnet/runtime/issues/64047)
- [Playwright .NET](https://playwright.dev/dotnet/)
- [bUnit](https://bunit.dev/)
- [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp)
- [StbTrueTypeSharp SDF bug (issue #1)](https://github.com/StbSharp/StbTrueTypeSharp/issues/1)
- [StbImageWriteSharp](https://github.com/StbSharp/StbImageWriteSharp)
- [KernSmith GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)
