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
