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

### How Auto-Discovery Works

`RasterizerFactory.DiscoverBackends()` resolves a known, fixed set of backend assemblies **by name** using reflection (`Type.GetType(typeName)`). For each backend type it resolves, it invokes that type's `Register()` method via reflection (falling back to running the assembly's module constructor) so the backend registers itself. The first backend successfully registered becomes the default.

Because this discovery relies on reflection and name-based type resolution, it **does not work under Native AOT or trimming** -- see [Native AOT and Trimming](#native-aot-and-trimming).

## Native AOT and Trimming

Auto-discovery resolves backend assemblies by name via reflection, which is **not compatible with Native AOT or trimming**. Under AOT or trimming the backend types may be removed from the published output or cannot be resolved by name, so auto-discovery finds nothing and generation fails with a "backend is not registered" error.

**You must register a backend explicitly** before generating a font. The recommended AOT backend is [**StbTrueType**](stbtruetype.md): it is pure C#, has no native dependencies, and is the only backend marked AOT-compatible.

Force the backend assembly to load so its module initializer registers the rasterizer. Reference its public `StbTrueTypeRasterizer` type by name to keep it from being trimmed away:

```csharp
using System.Runtime.CompilerServices;
using KernSmith;
using KernSmith.Rasterizers.StbTrueType;

// Required under Native AOT / trimming -- auto-discovery cannot find the backend.
// Touching the type triggers its [ModuleInitializer] registration and prevents trimming.
RuntimeHelpers.RunClassConstructor(typeof(StbTrueTypeRasterizer).TypeHandle);

var result = BmFont.Generate("path/to/font.ttf", new FontGeneratorOptions
{
    Size = 32,
    Characters = CharacterSet.Ascii,
    Backend = RasterizerBackend.StbTrueType
});
```

Alternatively, register a factory directly through the public `RasterizerFactory` API:

```csharp
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;

RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
```

The FreeType, GDI, and DirectWrite backends use P/Invoke and native interop and are **not** targeted for AOT, so prefer StbTrueType in AOT or trimmed apps. The core `KernSmith` library itself is AOT- and trim-compatible.

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
