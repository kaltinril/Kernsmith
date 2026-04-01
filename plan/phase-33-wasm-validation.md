# Phase 33 — WASM Integration & Validation

> **Status**: Future
> **Created**: 2026-03-30
> **Depends on**: Phase 30 (FreeType extraction), Phase 31 (WASM restrictions research), Phase 32 (StbTrueType plugin)
> **Related**: [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)
> **Note**: Phase 31 research findings should inform the validation approach used here.

## Goal

Validate that the core KernSmith library + StbTrueType plugin works in a Blazor WASM environment, and document the integration path for web consumers.

## Scope

### In Scope

1. **Blazor WASM test project** — minimal Blazor WASM app that references `KernSmith` + `KernSmith.Rasterizers.StbTrueType` and generates a bitmap font client-side
2. **Verify no native dependencies leak** — confirm the core library loads without `PlatformNotSupportedException`
3. **Automated native dependency verification** — CI gate and reflection-based scanning (see below)
4. **Integration test** — upload TTF, generate BMFont, download result (all in-browser)
5. **Documentation** — usage guide for WASM consumers
6. **Trimming/AOT compatibility** — verify the StbTrueType path works with Blazor WASM publishing, testing trimming and AOT independently
7. **DefaultSystemFontProvider behavior** — validate graceful handling on WASM
8. **File I/O limitations** — document and validate in-memory API usage
9. **Threading behavior** — verify pipeline works on single-threaded WASM runtime
10. **Memory constraints** — document heap limits and recommend subsetting

### Out of Scope

- Full web UI (KNI/Blazor WebGL UI is a separate, much larger effort)
- Server-side rasterization API (viable but separate from this validation)
- SkiaSharp WASM rasterizer (evaluated in Phase 30 research; not needed if StbTrueType works)
- FreeType via Emscripten (rejected — maintenance burden too high)

## Tools Required

| Tool | Purpose | Required? |
|------|---------|-----------|
| wasm-tools workload | AOT compilation | Yes |
| Microsoft.AspNetCore.Components.WebAssembly | Blazor WASM | Yes |
| Browser DevTools | Runtime validation | Yes (manual) |
| Playwright.NET | E2E browser testing | Optional |

## Implementation Plan

### Step 1: Create validation project

```
samples/KernSmith.Samples.BlazorWasm/
├── KernSmith.Samples.BlazorWasm.csproj   (Blazor WASM, net10.0)
├── Pages/
│   └── Index.razor                        (font upload + generate + preview)
├── Program.cs
└── wwwroot/
    └── index.html
```

### Step 2: Verify core library loads

- Reference only `KernSmith` + `KernSmith.Rasterizers.StbTrueType` (no FreeType plugin)
- Confirm no `PlatformNotSupportedException` on startup
- Confirm `RasterizerFactory.IsRegistered(RasterizerBackend.StbTrueType)` returns true

### Step 3: Automated native dependency verification

- After `dotnet publish` for browser-wasm, inspect `_framework/` output for unexpected native binaries
- Add unit test that scans KernSmith assemblies for `[DllImport]`/`[LibraryImport]` attributes via reflection — any P/Invoke in the core library or StbTrueType plugin is a WASM blocker
- Add CI gate: `dotnet publish` of Blazor WASM sample must succeed in CI pipeline (catches regressions without needing a browser)

### Step 4: DefaultSystemFontProvider

- `DefaultSystemFontProvider.cs` uses `System.Runtime.InteropServices`, `Microsoft.Win32.Registry`, and filesystem scanning to enumerate installed fonts
- On WASM, `GetInstalledFonts()` will throw — there is no filesystem font directory and no Windows registry
- Mitigation: return an empty list gracefully when running on WASM, or document as explicitly unsupported
- `StbTrueTypeCapabilities` correctly sets `SupportsSystemFonts = false` — callers should check this before calling

### Step 5: File I/O limitations

- `BmFontResult.ToFile()` uses `System.IO.File` — this throws in browser WASM
- Users must use in-memory APIs: `FntText`, `GetPngData()`, etc.
- File download requires JavaScript interop (see References)
- **InputFile component**: default `MaxFileSize` is 512 KB — font files can be 5–20 MB, so the Blazor sample must explicitly set `MaxFileSize` on `<InputFile>`

### Step 6: End-to-end font generation

- User uploads a TTF file via `<InputFile>` (with increased `MaxFileSize`)
- Call `BmFont.Generate()` with `Backend = RasterizerBackend.StbTrueType`
- Display the generated atlas PNG in the browser
- Offer `.fnt` + `.png` download via JS interop
- Test SDF rendering specifically — known StbTrueTypeSharp SDF quality bug (StbSharp/StbTrueTypeSharp#1, open since 2020); document any visual artifacts

### Step 7: Performance baseline

- Measure generation time for Roboto-Regular, 32px, ASCII charset in WASM
- Compare with native (.NET console app) performance
- Document expected WASM overhead
- Define "acceptable" performance thresholds for benchmarks

### Step 8: Threading

- Blazor WASM is single-threaded by default
- `WasmEnableThreads` requires `SharedArrayBuffer` + COOP/COEP headers on the server
- Audit KernSmith for `Parallel.ForEach` or `Task.Run` usage — `Parallel.ForEach` throws `PlatformNotSupportedException` in WASM and `Task.Run` does not offload to a real thread
- `Parallel.ForEach` throws `PlatformNotSupportedException` in WASM (dotnet/runtime#43411) — any usage must be replaced with sequential `foreach`
- `.Result`, `.Wait()`, `.WaitAll()`, and `Thread.Sleep` deadlock the single browser thread — audit and remove from all WASM code paths
- For UI responsiveness during long rasterization, yield every N glyphs via `Task.Delay(1)` — do NOT use `Task.Yield()` as it does not reliably yield to the browser render pipeline
- Verify pipeline threading behavior and document any issues

### Step 9: Memory constraints

- WASM default heap: ~127 MB (grows dynamically, configurable via `EmccMaximumHeapSize`, practical limit ~1.7-2 GB). Mobile Safari may cap at 256-512 MB total.
- Large CJK fonts (20,000+ glyphs) at high resolution could exhaust the heap
- Document limits and recommend subsetting for WASM users

### Step 10: Trimming and AOT (separate concerns)

`PublishTrimmed` (default for Blazor WASM publish) and `RunAOTCompilation` are distinct — test independently since trimming can break things AOT does not, and vice versa.

**Trimming**:
- Build with `<PublishTrimmed>true</PublishTrimmed>` (default for Blazor WASM)
- Use `<TrimmerSingleWarn>false</TrimmerSingleWarn>` to surface all trimming warnings
- Verify no trimming warnings from KernSmith or StbTrueTypeSharp
- Note Phase 90 overlap for reflection/JSON serialization issues

**AOT**:
- Build with `<RunAOTCompilation>true</RunAOTCompilation>`
- Verify generation still works
- Confirm no AOT-specific failures

### Step 11: StbImageWriteSharp WASM compatibility

- StbImageWriteSharp is pure managed C# — should work on WASM, but explicitly verify PNG encoding in the browser runtime
- Test generating and downloading a PNG atlas to confirm end-to-end

## Testing Strategy

| Test Type | What It Validates | WASM-Realistic? |
|-----------|-------------------|-----------------|
| CI gate: `dotnet publish` Blazor WASM sample | Build succeeds, no missing native deps | Partial — build only |
| Assembly scanning unit test (reflection for DllImport) | No P/Invoke in WASM path | Yes — catches regressions |
| Playwright E2E test | Full browser runtime behavior | Yes — gold standard |
| bUnit tests | Component logic only | **No** — runs on server CLR, NOT WASM |

- **bUnit limitation**: bUnit runs on the server CLR, not the WASM runtime. It does not validate the WASM execution path. Use it for component logic only, not WASM compatibility.
- **Playwright E2E** is optional but recommended for full confidence in the browser runtime.
- **CI gate** (`dotnet publish`) is the minimum bar — catches most regressions without needing a browser.

## KNI Web UI Notes

For a full web UI (not just validation), the path is:
1. Swap MonoGame → KNI (`nkast.Xna.Framework.Blazor`)
2. Swap `Gum.MonoGame` → `Gum.KNI`
3. Swap `MonoGame.Extended` → `KNI.Extended`
4. Use StbTrueType backend for rasterization

This is a much larger effort and is NOT part of Phase 33. Tracked separately as a future phase.

## Server-Side Alternative

If WASM performance or quality is insufficient, a server-side API is the fallback:
- ASP.NET Core API that accepts font bytes + options, runs FreeType backend, returns BMFont result
- Zero library changes needed
- Full feature parity (SDF, effects, color fonts, variable fonts)
- Tracked separately if needed

## Success Criteria

- [ ] Blazor WASM sample project builds and runs
- [ ] Font generation works entirely client-side (no server)
- [ ] No native dependency errors
- [ ] CI gate: `dotnet publish` of Blazor WASM sample succeeds in CI pipeline
- [ ] Assembly scanning test passes (no DllImport in WASM path)
- [ ] Generated output matches expected BMFont format
- [ ] Trimming works without warnings (tested independently from AOT)
- [ ] AOT compilation works (tested independently from trimming)
- [ ] DefaultSystemFontProvider does not throw on WASM
- [ ] In-memory APIs work correctly (no File I/O dependency)
- [ ] Threading behavior verified (no deadlocks on single-threaded runtime)
- [ ] No `Parallel.ForEach`, `.Result`, `.Wait()`, or `Thread.Sleep` in WASM code paths
- [ ] SDF rendering quality validated (StbSharp/StbTrueTypeSharp#1 documented)
- [ ] Memory constraints documented
- [ ] StbImageWriteSharp PNG encoding verified in WASM
- [ ] Performance documented and acceptable

## References

- [Blazor WASM hosting models](https://learn.microsoft.com/en-us/aspnet/core/blazor/hosting-models)
- [IL trimming configuration](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/configure-trimmer)
- [WASM build tools & AOT](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot)
- [Blazor file downloads](https://learn.microsoft.com/en-us/aspnet/core/blazor/file-downloads)
- [Playwright for .NET](https://playwright.dev/dotnet/)
- [bUnit](https://bunit.dev/)
- [GitHub Issue #39](https://github.com/kaltinril/Kernsmith/issues/39)
- [KNI Blazor support](https://github.com/kniEngine/kni)
- Cross-reference: Phase 90 (AOT compliance)
