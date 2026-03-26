# Phase 78A -- Rasterizer Foundation

> **Status**: Planning
> **Size**: Small
> **Created**: 2026-03-25
> **Dependencies**: None
> **Parent**: [Phase 78 -- Pluggable Rasterizer Backends](phase-78-pluggable-rasterizers.md)
> **Goal**: Add capability reporting, backend enum, factory, and wire the pluggable rasterizer infrastructure into the existing pipeline.

---

## Key Design Context

- **Registration API is universal.** ALL backends -- including first-party GDI and DirectWrite -- register via the same `RasterizerFactory.Register()` API. There are no privileged internal APIs for official backends. Third-party backends use the exact same mechanism.
- **Precedence order:** `FontGeneratorOptions.Rasterizer` (direct DI) > `FontGeneratorOptions.Backend` (factory enum) > default (FreeType).
- **Ownership semantics:** Factory-created rasterizers (via `Backend` enum) are owned and disposed by BmFont when generation completes. User-injected rasterizers (via `Rasterizer` property) are NOT disposed by BmFont -- the caller owns them and is responsible for their lifecycle.
- **Performance impact: zero.** The factory runs once at startup (switch statement returning a rasterizer instance). The `IRasterizer` interface is already the dispatch mechanism for all glyph rasterization -- that virtual dispatch already exists today. Adding alternative backends does not add any per-glyph overhead. JIT virtual dispatch is ~1-2ns.
- **Core decoupling scope is small.** Replace `new FreeTypeRasterizer()` in `BmFont.cs` with factory call. Add `Backend` property to options. That's the extent of the coupling change.

## Tasks

### 1. Add `IRasterizerCapabilities` Interface

Namespace: `KernSmith.Rasterizer`
File: `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs`

Reports what a backend supports:

```csharp
public interface IRasterizerCapabilities
{
    bool SupportsColorFonts { get; }
    bool SupportsVariableFonts { get; }
    bool SupportsSdf { get; }
    bool SupportsOutlineStroke { get; }
    IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; }
}
```

### 2. Add `Capabilities` Property to `IRasterizer`

File: `src/KernSmith/Rasterizer/IRasterizer.cs`

```csharp
IRasterizerCapabilities Capabilities { get; }
```

This is a breaking change to the interface. Existing `FreeTypeRasterizer` must implement it immediately.

### 3. Add `RasterizerBackend` Enum

Namespace: `KernSmith` (root)
File: `src/KernSmith/Config/RasterizerBackend.cs`

```csharp
public enum RasterizerBackend
{
    Auto,
    FreeType,
    Gdi,
    DirectWrite
}
```

Extensible for future backends (SkiaSharp, SixLabors, etc.) but only these four values for now.

### 4. Add `Backend` Property to `FontGeneratorOptions`

File: `src/KernSmith/Config/FontGeneratorOptions.cs`

```csharp
public RasterizerBackend Backend { get; set; } = RasterizerBackend.FreeType;
```

Note: `Rasterizer` property already exists for direct DI injection. `Backend` is the enum-based alternative.

### 5. Add `RasterizerFactory` Static Class

Namespace: `KernSmith.Rasterizer`
File: `src/KernSmith/Rasterizer/RasterizerFactory.cs`

- Resolves `RasterizerBackend` enum to an `IRasterizer` instance
- Initially only returns `FreeTypeRasterizer`
- Uses runtime `OperatingSystem.IsWindows()` checks
- Throws `PlatformNotSupportedException` for unavailable backends
- Static registration API for backend packages:

```csharp
public static class RasterizerFactory
{
    public static void Register(RasterizerBackend backend, Func<IRasterizer> factory);
    public static IRasterizer Create(RasterizerBackend backend);
    public static IReadOnlyList<RasterizerBackend> GetAvailableBackends();
}
```

When `Auto` is requested: return FreeType everywhere by default. Backend packages can override `Auto` resolution for their platform.

### 6. Implement `IRasterizerCapabilities` on `FreeTypeRasterizer`

File: `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs`

Report actual FreeType capabilities:
- `SupportsColorFonts`: true (COLR/CPAL support exists)
- `SupportsVariableFonts`: true (variable font support exists)
- `SupportsSdf`: true (SDF generation exists)
- `SupportsOutlineStroke`: true (FT_Stroker)
- `SupportedAntiAliasModes`: all currently supported modes

### 7. Update `BmFont.cs` to Use Factory

File: `src/KernSmith/BmFont.cs`

Precedence logic:
1. If `FontGeneratorOptions.Rasterizer` is set (direct DI), use it -- user owns lifecycle
2. If `FontGeneratorOptions.Backend` is set, use `RasterizerFactory.Create()` -- BmFont owns lifecycle, disposes when done
3. Default: create `FreeTypeRasterizer` directly (current behavior)

Factory-created rasterizers are owned/disposed by BmFont. User-provided rasterizers via the `Rasterizer` property are NOT disposed by BmFont.

### 8. Parse `rasterizer=` from `.bmfc` Config Files

File: `src/KernSmith/Config/BmfcConfigReader.cs` (or wherever .bmfc parsing lives)

Map `rasterizer=freetype|gdi|directwrite|auto` to `RasterizerBackend` enum. Default is `FreeType` for backward compatibility when the key is absent.

## Files Changed

| File | Change |
|------|--------|
| `src/KernSmith/Rasterizer/IRasterizerCapabilities.cs` | New -- capabilities interface |
| `src/KernSmith/Rasterizer/IRasterizer.cs` | Add `Capabilities` property |
| `src/KernSmith/Config/RasterizerBackend.cs` | New -- backend enum |
| `src/KernSmith/Config/FontGeneratorOptions.cs` | Add `Backend` property |
| `src/KernSmith/Rasterizer/RasterizerFactory.cs` | New -- factory with registration API |
| `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` | Implement `IRasterizerCapabilities` |
| `src/KernSmith/BmFont.cs` | Use factory when `Backend` is set, ownership semantics |
| `src/KernSmith/Config/BmfcConfigReader.cs` | Parse `rasterizer=` key |

## Testing

- Unit test: `RasterizerFactory.Create(RasterizerBackend.FreeType)` returns a `FreeTypeRasterizer`
- Unit test: `RasterizerFactory.Create(RasterizerBackend.Gdi)` throws `PlatformNotSupportedException` when GDI package is not registered
- Unit test: `FreeTypeRasterizer.Capabilities` reports expected values
- Unit test: `BmFont.Generate()` with `Backend = RasterizerBackend.FreeType` produces same output as current default
- Unit test: `BmFont.Generate()` with both `Rasterizer` and `Backend` set uses `Rasterizer` (DI precedence)
- Unit test: `.bmfc` parsing reads `rasterizer=` key correctly
