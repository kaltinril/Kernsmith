# Rasterizers

KernSmith supports multiple rasterizer backends through a pluggable `IRasterizer` interface. The default FreeType backend is cross-platform. Two additional Windows-only backends are available as separate NuGet packages. You can also [write your own](custom-backend.md).

## Backends

| Backend | Package | Platform |
|---------|---------|----------|
| [FreeType](freetype.md) | Built into `KernSmith` | Cross-platform |
| [GDI](gdi.md) | `KernSmith.Rasterizers.Gdi` | Windows only |
| [DirectWrite](directwrite.md) | `KernSmith.Rasterizers.DirectWrite.TerraFX` | Windows only |

## Capability Comparison

| Feature | FreeType | GDI | DirectWrite |
|---------|----------|-----|-------------|
| Cross-platform | Yes | No | No |
| Color fonts (COLR/CPAL) | No | No | Yes |
| Variable fonts | No | No | Yes |
| SDF rendering | Yes | No | No |
| Outline stroke | Yes | No | No |
| System font loading | No | Yes | Yes |
| BMFont.exe parity | No | Yes | No |
| ClearType / subpixel | No | No | Yes |
| Synthetic bold/italic | Yes | Partial | Yes |
| Font formats | TTF, OTF, WOFF, WOFF2 | System fonts only | TTF, OTF, WOFF, WOFF2 |

## Auto-Registration

The GDI and DirectWrite packages use `[ModuleInitializer]` to register themselves automatically. Simply referencing the NuGet package is enough -- no manual setup code is required.

## Choosing a Backend

Use this decision tree to pick the right backend:

1. **Need cross-platform support?** Use [FreeType](freetype.md) -- it is the only backend that runs on Linux, macOS, and Windows.
2. **Need pixel-perfect BMFont.exe compatibility?** Use [GDI](gdi.md) -- it matches BMFont's native rasterizer output.
3. **Need color fonts or variable font axes?** Use [DirectWrite](directwrite.md) -- it is the only backend with COLR/CPAL and fvar support.
4. **Need SDF (Signed Distance Field) output?** Use [FreeType](freetype.md) -- it is the only backend with SDF rendering.
5. **Not sure?** Start with FreeType. It covers the majority of use cases and requires no additional packages.

## Selecting a Backend in Code

Pass the `RasterizerBackend` enum in your options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.FreeType      // default
    // RasterizerBackend = RasterizerBackend.Gdi         // Windows GDI
    // RasterizerBackend = RasterizerBackend.DirectWrite  // Windows DirectWrite
};
```

From the CLI, use `kernsmith list-rasterizers` to see which backends are available on the current machine.
