# StbTrueType

Managed-only rasterizer backend for KernSmith using [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp). No native dependencies -- runs on any .NET platform including Blazor WASM, NativeAOT, and serverless environments.

## Installation

```
dotnet add package KernSmith.Rasterizers.StbTrueType
```

**Platform:** Cross-platform (net8.0, net10.0). No native binaries required.

## Usage

The rasterizer auto-registers via `[ModuleInitializer]` -- referencing the package is sufficient. Select it in options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.StbTrueType
};

var result = BmFont.Generate("path/to/font.ttf", options);
```

## Capabilities

- TTF input
- Anti-aliasing (Grayscale and None modes)
- SDF (Signed Distance Field) rendering
- Super sampling

## Limitations

- No TrueType hinting (lower quality at small sizes < 16px)
- No color font support (COLR/CPAL)
- No variable font axis support
- No synthetic bold or italic
- No outline stroke support (use EDT outline effect instead)
- No system font loading -- provide font file bytes directly
- TTF only (no OTF/CFF outlines, no WOFF/WOFF2)

## When to Use

Use StbTrueType when you need bitmap font generation on platforms where native FreeType binaries are unavailable: Blazor WebAssembly, NativeAOT trimmed deployments, iOS AOT, serverless containers, or any environment where zero native dependencies is a requirement. For full-featured rendering, use the FreeType backend instead.

## Blazor WASM

StbTrueType is the recommended backend for Blazor WebAssembly. A complete working sample is available in `samples/KernSmith.Samples.BlazorWasm/`.

### Setup

1. Reference `KernSmith` and `KernSmith.Rasterizers.StbTrueType` (do NOT reference FreeType)
2. Force assembly load in `Program.cs` to prevent trimming:

```csharp
RuntimeHelpers.RunClassConstructor(
    typeof(KernSmith.Rasterizers.StbTrueType.StbTrueTypeRasterizer).TypeHandle);
```

3. Enable AOT compilation for production performance:

```xml
<RunAOTCompilation>true</RunAOTCompilation>
```

### WASM Considerations

- Use in-memory APIs (`FntText`, `GetPngData()`) — `ToFile()` is not available in the browser
- System font loading is unavailable — use `LoadFont()` with font bytes or `BmFont.RegisterFont()`
- The WASM runtime is single-threaded — batch parallelism is automatically disabled
- Default heap is ~127 MB — subsetting is recommended for large CJK fonts
- AOT compilation improves performance ~5-18x over the interpreter

### Performance (Roboto-Regular, 32px, ASCII)

| Mode | AOT (warm) | Interpreter |
|------|------------|-------------|
| Normal | ~14 ms | ~256 ms |
| SDF | ~51 ms | ~1,344 ms |
