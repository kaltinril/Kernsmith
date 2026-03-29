# Rasterizers

KernSmith supports multiple rasterizer backends. The default FreeType backend is cross-platform. Two additional Windows-only backends are available as separate NuGet packages.

## Backends

| Backend | Package | Platform |
|---------|---------|----------|
| [FreeType](freetype.md) | Built into `KernSmith` | Cross-platform |
| [GDI](gdi.md) | `KernSmith.Rasterizers.Gdi` | Windows only |
| [DirectWrite](directwrite.md) | `KernSmith.Rasterizers.DirectWrite.TerraFX` | Windows only |

## Auto-Registration

The GDI and DirectWrite packages use `[ModuleInitializer]` to register themselves automatically. Simply referencing the NuGet package is enough -- no manual setup code is required.

## Choosing a Backend

- **FreeType** -- default, cross-platform, supports SDF
- **GDI** -- pixel-perfect BMFont.exe compatibility on Windows
- **DirectWrite** -- color fonts, variable fonts, ClearType rendering on Windows
