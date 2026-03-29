# FreeType (Default)

The built-in rasterizer backend, included in the core `KernSmith` package. Uses [FreeTypeSharp](https://github.com/nicholasgasior/FreeTypeSharp) for glyph rasterization.

## Platform

Cross-platform -- Linux, macOS, Windows.

## Usage

FreeType is the default backend. No additional packages or configuration needed:

```csharp
var result = BmFont.Generate("path/to/font.ttf", new FontGeneratorOptions
{
    Size = 32
});
```

To explicitly select it:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.FreeType
};
```

## Capabilities

- TTF, OTF, WOFF, WOFF2 input
- Hinting and anti-aliasing
- SDF (Signed Distance Field) rendering
- Synthetic bold and italic
- Super sampling

## When to Use

Use FreeType for cross-platform projects or when you don't need Windows-specific features like color fonts or BMFont pixel-perfect parity.
