# Phase 30 — WASM Rasterization Investigation

> **Status**: Planning (requires live research — prior findings are preliminary, not validated)
> **Created**: 2026-03-20

## Findings

### FreeTypeSharp WASM Status

FreeTypeSharp 3.1.0 ships native binaries for Windows, Linux, macOS, Android, iOS, and tvOS.
**There is no browser-wasm target.** The NuGet package does not include a
`browser-wasm` RID folder, and the CI that builds native binaries
(github.com/ryancheung/freetype, `csharp-patch` branch) does not produce an
Emscripten/WASM build. No forks with WASM support were found in public
repositories or NuGet. The project has not announced any WASM plans as of
March 2026.

### FreeType via Emscripten

Compiling libfreetype to WASM with Emscripten is **technically feasible and
well-documented** by the .NET platform:

1. **Blazor WebAssembly Native Dependencies** — .NET supports adding native C/C++
   code via `<NativeFileReference>` items in the project file. Emscripten
   compiles these into the WASM runtime, and .NET code calls them via
   `[DllImport]` / P/Invoke. This is the same mechanism SkiaSharp uses for its
   Blazor WASM support.

2. **Build process** — libfreetype must be compiled with the *same Emscripten
   version* used by the .NET WebAssembly build tools for the target .NET
   version. The output can be an object file (`.o`), archive (`.a`), bitcode
   (`.bc`), or standalone `.wasm` module.

3. **FreeTypeSharp P/Invoke layer** — FreeTypeSharp's managed API maps almost
   1:1 to the native FreeType C API via P/Invoke. If a WASM-compiled
   `libfreetype.a` is provided, the existing P/Invoke calls *should* work with
   minimal changes, provided the function signatures match.

**Challenges:**
- FreeType has optional dependencies (zlib, libpng, brotli, harfbuzz) that may
  also need Emscripten builds or must be disabled.
- The Emscripten version must match .NET's SDK version exactly, which creates a
  maintenance burden on every .NET SDK update.
- AOT compilation is recommended for acceptable performance, increasing build
  times significantly.
- FreeTypeSharp's `FreeTypeLibrary` class loads the native library by name/path;
  this initialization code would need adaptation for the WASM environment.

### Alternative WASM-Compatible Rasterizers

#### SkiaSharp (Recommended for WASM)
- **WASM support: Yes.** `SkiaSharp.Views.Blazor` 3.119.1 (Sept 2025) provides
  full Blazor WebAssembly support, including font rendering.
- Skia includes its own font rasterizer, text shaping (via embedded HarfBuzz),
  and drawing API.
- The native Skia library is compiled to WASM via Emscripten and bundled in the
  NuGet package — no manual Emscripten builds required.
- **Tradeoff:** SkiaSharp is a large dependency (~5-10 MB WASM payload). It is a
  full 2D graphics library, not just a font rasterizer.
- **Integration path:** Implement `IRasterizer` backed by `SKCanvas`/`SKFont`
  for glyph rendering.

#### Browser Canvas API via JS Interop
- Use the browser's native `CanvasRenderingContext2D.fillText()` or
  `OffscreenCanvas` to rasterize glyphs, called from C# via JS interop.
- **Pros:** Zero native dependency overhead; uses the browser's own font engine
  (system FreeType/CoreText/DirectWrite under the hood).
- **Cons:** Limited control over rasterization parameters (no SDF, no exact
  pixel metrics matching, no variable font axis control, no color palette
  selection). Glyph metrics may differ from FreeType output. Async JS interop
  adds latency per glyph.
- **Verdict:** Viable for basic use cases but cannot replicate KernSmith's full
  feature set (SDF, effects, color fonts, variable axes).

#### Fontdue (Rust, compilable to WASM)
- Pure Rust TrueType/OpenType rasterizer; extremely fast; no_std compatible.
- Can be compiled to WASM via `wasm-pack` and called from JS or .NET via
  interop.
- **Cons:** Does not do text shaping. Limited OpenType feature support. Calling
  Rust-WASM from .NET WASM adds complexity (two WASM modules). No .NET
  bindings exist.
- **Verdict:** Interesting for performance-critical scenarios but integration
  cost is high and feature coverage is narrower than FreeType.

#### HarfBuzz WASM
- HarfBuzz 8.0+ includes an experimental WASM shaper that allows fonts to embed
  custom shaping logic as WASM. This is *not* a rasterizer — it handles text
  shaping/layout only.
- Rustybuzz (Rust port of HarfBuzz shaping) can be compiled to WASM.
- **Verdict:** Not a replacement for font rasterization. Relevant only if
  KernSmith adds text shaping in the future.

#### FontKit (JS/WASM)
- JavaScript/WASM library backed by FreeType and HarfBuzz.
- Designed for JS consumers, not .NET. Would require JS interop from Blazor.
- **Verdict:** Adds unnecessary indirection for a .NET project.

### Server-Side Rasterization

A hybrid architecture where the web UI sends font files and options to a backend
API, which runs the existing FreeTypeSharp-based pipeline:

**Pros:**
- Zero changes to the core library — existing `FreeTypeRasterizer` works as-is.
- No WASM payload size concerns; no Emscripten build maintenance.
- Full feature parity (SDF, effects, color fonts, variable fonts).
- Server can cache results for repeated requests.
- Simplest implementation path by far.

**Cons:**
- Requires a running server (no fully static/offline web app).
- Upload latency for large font files.
- Server compute cost scales with concurrent users.
- Not suitable for real-time preview during parameter adjustment (though
  debounced requests with low-res previews can mitigate this).

### IRasterizer Abstraction Assessment

The current `IRasterizer` interface is well-designed for swappable implementations:

```csharp
public interface IRasterizer : IDisposable
{
    void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0);
    RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options);
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);
}
```

**Strengths:**
- Clean separation — `FreeTypeRasterizer` is `internal`; consumers interact only
  with `IRasterizer`.
- `FontGeneratorOptions.Rasterizer` property and `BmFontBuilder.WithRasterizer()`
  allow users to inject custom rasterizer implementations.
- `RasterOptions` and `RasterizedGlyph` are pure data types with no FreeType
  dependencies.

**Gaps for WASM:**
- The interface is synchronous. Browser WASM environments often require async
  operations (JS interop, canvas rendering). A WASM-targeting rasterizer using
  JS interop would need an async adapter or must run on a synchronous path.
- `FreeTypeRasterizer` has `internal` methods (`SelectColorPalette`,
  `SetVariationAxes`) called by the pipeline. A WASM rasterizer would need
  equivalent hooks, or these features would need to be folded into `RasterOptions`
  (which already carries `ColorPaletteIndex` and `VariationAxes`).
- `LoadFont` accepts `ReadOnlyMemory<byte>`, which works well for in-memory
  scenarios including WASM (no file system dependency).

**Verdict:** The abstraction is sound. A new `IRasterizer` implementation (e.g.,
`SkiaSharpRasterizer` or `CanvasRasterizer`) can be plugged in without changes
to the core pipeline. The only friction point is sync vs. async, which is
manageable.

### KNI as Web UI Framework

A separate concern from rasterization is the UI framework itself. The desktop UI (Phases 60-69) uses MonoGame DesktopGL + GUM UI + MonoGame.Extended. Stock MonoGame has **no WASM/web target**. However, KNI (nkast's MonoGame fork) adds Blazor WebGL support.

**KNI Overview:**
- KNI is an API-compatible fork of MonoGame by nkast (Nikos Kastellanos)
- Adds `nkast.Xna.Framework.Blazor` for WebAssembly/WebGL rendering
- Same API surface as MonoGame — switching requires changing NuGet references, not code
- Production-ready (v4.0.9001+), actively maintained
- GUM has a `Gum.KNI` NuGet package with identical API to `Gum.MonoGame`
- MonoGame.Extended has `KNI.Extended` NuGet package with identical API

**Web UI Path:**
If a web version of the KernSmith UI is desired, the architecture would be:
1. **UI Layer**: Swap `MonoGame.Framework.DesktopGL` → `nkast.Xna.Framework.Blazor`, `Gum.MonoGame` → `Gum.KNI`, `MonoGame.Extended` → `KNI.Extended`
2. **Rasterization**: Use one of the approaches documented above (server-side recommended)
3. **Code changes**: Minimal — KNI is API-compatible with MonoGame

**KNI Web Limitations:**
- No GamePad or Touchscreen input in the browser
- WebGL2/GLES2 shader constraints
- `VertexBuffer.GetData()` not supported (WebGL limitation)
- Slower load times (~3-6 seconds vs sub-second for native JS)
- Blazor WASM bundle size (~2-3 MB with brotli compression)

**Verdict:** KNI is the clear path for a web-deployed KernSmith UI. Building on MonoGame DesktopGL now does not lock out KNI later — the swap is a NuGet reference change. The rasterization problem (FreeTypeSharp in WASM) remains the primary blocker regardless of UI framework choice.

## Recommendation

**Short term: Server-side rasterization (Phase 13a)**

For a web UI, use server-side rasterization via an ASP.NET Core API. This
requires zero library changes, delivers full feature parity, and can be built
in days rather than weeks. The web client uploads the font + options, the server
runs the existing pipeline, and returns the BMFont result.

**Medium term: SkiaSharp WASM rasterizer (Phase 13b)**

If a fully client-side experience is required, implement a `SkiaSharpRasterizer :
IRasterizer` that uses SkiaSharp's Blazor WASM support. SkiaSharp already solves
the Emscripten build problem and provides font rasterization, though some
KernSmith features (SDF, layered effects) would need to be reimplemented using
Skia's drawing API.

**Web UI Framework:** When a web version of the UI is pursued (post Phase 69), swap MonoGame → KNI for the Blazor WebGL target. The desktop UI code (Phases 60-69) is designed on MonoGame DesktopGL, which is API-compatible with KNI. See Phases 60-69 for the UI architecture.

**Not recommended:**
- DIY Emscripten builds of libfreetype — high maintenance burden for marginal
  benefit over SkiaSharp.
- Browser Canvas JS interop — too limited for KernSmith's feature set.
- Fontdue/Rust WASM — integration complexity not justified given SkiaSharp
  exists.

## Sources

- [FreeTypeSharp 3.1.0 on NuGet](https://www.nuget.org/packages/FreeTypeSharp)
- [ASP.NET Core Blazor WebAssembly native dependencies](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies?view=aspnetcore-10.0)
- [SkiaSharp.Views.Blazor 3.119.1 on NuGet](https://www.nuget.org/packages/SkiaSharp.Views.Blazor)
- [SkiaSharp Graphics in Blazor WebAssembly](https://codefrydev.in/Updates/blog/blazor/skiasharponblazorwebassembly/)
- [Using C/C++ Native Dependencies in Blazor WASM](https://ilovedotnet.org/blogs/using-c-c++-native-dependencies-in-blazor-wasm/)
- [HarfBuzz 8.0 WASM Shaper](https://github.com/harfbuzz/harfbuzz/blob/main/docs/wasm-shaper.md)
- [Fontdue on GitHub](https://github.com/losfair/fontdue)
- [FontKit (JS/WASM)](https://github.com/rsms/fontkit)
- [Compiling C# game engines to WASM](https://kylekukshtel.com/csharp-wasm-game-engine-compile-web-emscripten)
- [Blazor WebAssembly AOT compilation](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0)
- [KNI Engine GitHub](https://github.com/kniEngine/kni)
- [Gum.KNI NuGet](https://www.nuget.org/packages/Gum.KNI)
- [KNI.Extended NuGet](https://www.nuget.org/packages/KNI.Extended)
- [KNI Blazor Discussion](https://github.com/kniEngine/kni/discussions/1863)
