# Rasterizers

KernSmith supports multiple rasterizer backends through a pluggable `IRasterizer` interface. All backends are available as separate NuGet packages -- two cross-platform (FreeType, StbTrueType) and two Windows-only (GDI, DirectWrite). You can also [write your own](custom-backend.md).

## Backends

| Backend | Package | Platform |
|---------|---------|----------|
| [FreeType](freetype.md) | `KernSmith.Rasterizers.FreeType` | Cross-platform |
| [GDI](gdi.md) | `KernSmith.Rasterizers.Gdi` | Windows only |
| [DirectWrite](directwrite.md) | `KernSmith.Rasterizers.DirectWrite.TerraFX` | Windows only |
| [StbTrueType](stbtruetype.md) | `KernSmith.Rasterizers.StbTrueType` | Cross-platform (managed) |

## Capability Comparison

| Feature | FreeType | GDI | DirectWrite | StbTrueType |
|---------|----------|-----|-------------|-------------|
| Cross-platform | Yes | No | No | Yes |
| Color fonts (COLR/CPAL) | No | No | Yes | No |
| Variable fonts | No | No | Yes | No |
| SDF rendering | Yes | No | No | Yes |
| Outline stroke | Yes | No | No | No |
| System font loading | No | Yes | Yes | No |
| BMFont.exe parity | No | Yes | No | No |
| ClearType / subpixel | No | No | Yes | No |
| Synthetic bold/italic | Yes | Partial | Yes | Yes |
| Font formats | TTF, OTF, WOFF, WOFF2 | System fonts only | TTF, OTF, WOFF, WOFF2 | TTF only |
| Native dependencies | Yes | Yes | Yes | None |

## Auto-Registration

All built-in backend packages are auto-discovered by `RasterizerFactory` on first call to `Create()`, `GetAvailableBackends()`, or `IsRegistered()`. Simply referencing the NuGet package is enough -- no manual setup code is required. Third-party backends use `[ModuleInitializer]` for self-registration.

## Choosing a Backend

Use this decision tree to pick the right backend:

1. **Need cross-platform support?** Use [FreeType](freetype.md) -- it is the only backend that runs on Linux, macOS, and Windows.
2. **Need pixel-perfect BMFont.exe compatibility?** Use [GDI](gdi.md) -- it matches BMFont's native rasterizer output.
3. **Need color fonts or variable font axes?** Use [DirectWrite](directwrite.md) -- it is the only backend with COLR/CPAL and fvar support.
4. **Need SDF (Signed Distance Field) output?** Use [FreeType](freetype.md) or [StbTrueType](stbtruetype.md) -- they are the only backends with SDF rendering.
5. **Need cross-platform SDF without native binaries?** Use [StbTrueType](stbtruetype.md) -- it supports SDF and runs fully managed.
6. **Need WASM/Blazor or NativeAOT without native binaries?** Use [StbTrueType](stbtruetype.md) -- it is the only fully managed backend with zero native dependencies. See the [Blazor WASM sample](https://github.com/kaltinril/KernSmith/tree/main/samples/KernSmith.Samples.BlazorWasm) for a working example. Enable AOT compilation for production performance.
7. **Not sure?** Start with FreeType. It covers the majority of use cases.

## Selecting a Backend in Code

Pass the `RasterizerBackend` enum in your options:

```csharp
var options = new FontGeneratorOptions
{
    Size = 32,
    RasterizerBackend = RasterizerBackend.FreeType      // default
    // RasterizerBackend = RasterizerBackend.Gdi         // Windows GDI
    // RasterizerBackend = RasterizerBackend.DirectWrite  // Windows DirectWrite
    // RasterizerBackend = RasterizerBackend.StbTrueType  // managed, no native deps
};
```

From the CLI, use `kernsmith list-rasterizers` to see which backends are available on the current machine.
