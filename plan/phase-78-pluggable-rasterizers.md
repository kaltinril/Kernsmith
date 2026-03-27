# Phase 78 -- Pluggable Rasterizer Backends (Overview)

> **Status**: In Progress
> **Created**: 2026-03-22
> **Updated**: 2026-03-25
> **Goal**: Make the rasterizer backend pluggable so users can choose FreeType (cross-platform default), GDI (BMFont parity), DirectWrite (modern Windows), or custom backends.

---

## Summary

KernSmith currently uses FreeType (via FreeTypeSharp) as its sole rasterizer. This phase adds capability reporting to the existing `IRasterizer` interface, builds real alternative backends (GDI for BMFont parity, DirectWrite for modern Windows rendering), and packages them as optional NuGet add-ons.

The existing `IRasterizer` interface is already the right abstraction -- this phase extends it rather than replacing it.

## User-Facing Model

- **Two main choices**: FreeType (cross-platform default) or a platform-native backend (GDI, DirectWrite)
- Backend packages use platform-specific TFMs (e.g. `net10.0-windows`); the factory uses runtime `OperatingSystem.IsWindows()` checks for availability
- Users can also write custom backends implementing `IRasterizer` + `IRasterizerCapabilities`
- Linux's native font rendering IS FreeType -- there is no separate Linux native backend. macOS Core Text is a possibility but deferred (no demand yet)

## Package Structure

| Package | Contents | Platform |
|---------|----------|----------|
| `KernSmith` | Core library, includes FreeType (the default everyone installs) | All |
| `KernSmith.Rasterizers.Gdi` | Optional Windows add-on, matches BMFont output | Windows |
| `KernSmith.Rasterizers.DirectWrite` | Optional Windows add-on, modern rendering | Windows |

FreeType stays bundled in core -- don't force every user to install a second package for the obvious default.

Third-party packages can implement `IRasterizer` + `IRasterizerCapabilities` and register with `RasterizerFactory` to work as a KernSmith backend. No special packaging required -- just depend on `KernSmith` and call `RasterizerFactory.Register()` (e.g. `MyCompany.MyFancyRasterizer`).

## Sub-Phases

| Phase | Name | Size | Description |
|-------|------|------|-------------|
| [78A](phase-78a-rasterizer-foundation.md) | Foundation | Small | `IRasterizerCapabilities`, `RasterizerBackend` enum, `RasterizerFactory`, wire into `BmFont.cs` |
| [78B](done/phase-78b-gdi-backend.md) | GDI Backend | Medium | `GdiRasterizer` via Win32 P/Invoke -- highest-value backend, matches BMFont output. **Complete.** |
| [78BB](done/phase-78bb-gdi-parity.md) | GDI Parity Fixes | Medium | GDI parity fixes -- metrics from GDI TEXTMETRIC, kerning from GetKerningPairs, sizing bypass. **Complete.** |
| [78C](phase-78c-directwrite-backend.md) | DirectWrite Backend | Medium-Large | `DirectWriteRasterizer` via Vortice.Windows -- color fonts, variable fonts, modern rendering |
| [78D](phase-78d-cli-ui-integration.md) | CLI and UI Integration | Small | `--rasterizer` flag, UI dropdown, capability-aware option graying |
| [78E](phase-78e-plugin-template.md) | Plugin Template | Small (deferred) | `dotnet new` template and docs for custom backends -- only after 2+ backends exist |

## Key Design Decisions

1. **Two user-facing choices: FreeType or a platform-native backend.** FreeType is the cross-platform default bundled with `KernSmith`. Platform-native backends (GDI, DirectWrite) are optional NuGet add-ons that users install only when they need them.

2. **FreeType stays in core.** Don't force every user to install a second package for the obvious default. FreeType works everywhere. Only users specifically chasing BMFont-identical output or Windows-native rendering need add-ons.

3. **No metapackage.** We considered a `KernSmith.Rasterizers.All` metapackage (MonoGame-style) but rejected it. KernSmith is a library, not a project template -- the MonoGame pattern doesn't translate. A metapackage would force Windows users to download GDI P/Invoke and Vortice.Windows as transitive dependencies even if they only want FreeType. Keep it explicit: install what you need.

4. **Linux native IS FreeType.** There is no separate "Linux native" backend. Native on Linux maps to FreeType -- the toggle is effectively a no-op. macOS Core Text is a real possibility but deferred (no demand yet).

5. **Build GDI first, generalize later.** Don't build the plugin framework/template (78E) before having 2+ backends. GDI (78B) will pressure-test whether `IRasterizer` is sufficient or needs changes. The graveyard of OSS projects is full of beautiful plugin architectures with exactly one plugin.

6. **The primary goal is BMFont parity via GDI.** DirectWrite is a nice-to-have for modern rendering (color fonts, variable fonts, subpixel positioning). If scope needs to be cut, 78C (DirectWrite) goes before 78B (GDI).

7. **`IRasterizer` is the plugin API** -- it already exists; just needs `IRasterizerCapabilities` added.

8. **GDI `LoadFont` uses `AddFontMemResourceEx`** -- registers raw bytes as a private font, keeping the `IRasterizer.LoadFont(ReadOnlyMemory<byte>)` interface unchanged. No need for name-based overloads.

9. **ClearType excluded from atlas output.** ClearType produces 3x-wide RGB subpixel bitmaps that don't fit the Grayscale8/Rgba32 pipeline. Game engines don't use subpixel rendering. Explicitly not supported for atlas generation.

10. **Platform TFMs over `#if` directives.** We discussed using `#if` compiler directives to pick native implementations at compile time. Decision: use platform-specific TFMs on the backend packages (e.g. `net10.0-windows` for GDI) which handles compilation targeting. The factory uses runtime `OperatingSystem.IsWindows()` checks to determine availability. JIT eliminates dead platform branches anyway.

11. **Third-party extensibility is a first-class goal.** Anyone can publish their own NuGet package (e.g. `MyCompany.MyFancyRasterizer`) implementing `IRasterizer` + `IRasterizerCapabilities` and registering with `RasterizerFactory`. The official backends (GDI, DirectWrite) use the exact same mechanism -- no privileged internal APIs.

12. **Plugin contract is intentionally minimal.** Just two interfaces: `IRasterizer` and `IRasterizerCapabilities`. Phase 78E (template + docs) makes it easier but doesn't gate it.

13. **DirectWrite uses Vortice.Windows** -- a community .NET wrapper for Windows native APIs (DirectX, Direct2D, DirectWrite). It's a heavy dependency, which is another reason it's an optional add-on, not bundled in core.

14. **`RasterizerBackend` enum**: `FreeType`, `Gdi`, `DirectWrite` (extensible for future backends). No `Auto` value — users explicitly choose their backend, defaulting to FreeType.

15. **Precedence**: `FontGeneratorOptions.Rasterizer` (direct DI) > `FontGeneratorOptions.Backend` (factory enum) > default (FreeType).

16. **Ownership**: Factory-created rasterizers are owned/disposed by BmFont. User-injected rasterizers via `FontGeneratorOptions.Rasterizer` are NOT disposed (caller owns them).

17. **Performance impact is effectively zero.** The core is only loosely coupled to FreeType -- `BmFont.cs` directly creates `new FreeTypeRasterizer()` which gets replaced by a factory call (one line change). The `IRasterizer` interface already exists and is already what gets called during rasterization. The factory is a switch statement that runs once at startup, not per-glyph. Virtual dispatch on the interface is ~1-2ns. No perceptible performance impact.

18. **Core decoupling is minimal work.** The only FreeType coupling in core is the direct `new FreeTypeRasterizer()` call in `BmFont.cs` and the FreeTypeSharp package dependency. `IRasterizer` already serves as the abstraction layer.

## Risks and Open Questions

- **ClearType subpixel data** doesn't fit the current Grayscale8/Rgba32 pixel pipeline -- would require a new pixel format and render target changes
- **Metrics divergence** across backends -- GDI uses TEXTMETRIC, FreeType uses hhea/OS2 tables. Same font at same size may produce different ascent/descent/lineHeight values
- **`IRasterizer` interface stability** -- whether the interface needs changes after building the GDI backend (e.g., additional font loading options, kerning queries)
- **Module initializer reliability** -- static registration via `[ModuleInitializer]` may have ordering issues; may need explicit `RasterizerFactory.Register()` call instead

## Key Source Files

| What | Location |
|------|----------|
| IRasterizer interface | `src/KernSmith/Rasterizer/IRasterizer.cs` |
| FreeTypeRasterizer | `src/KernSmith/Rasterizer/FreeTypeRasterizer.cs` |
| RasterOptions | `src/KernSmith/Rasterizer/RasterOptions.cs` |
| FontGeneratorOptions | `src/KernSmith/Config/FontGeneratorOptions.cs` |
| BmFont orchestration | `src/KernSmith/BmFont.cs` |
| Font metrics reference | `reference/REF-09-font-metrics-and-sizing.md` |

## Reference Material

### Prior Art

- **FontStashSharp** -- C# bitmap font library with pluggable rasterizers via `IFontLoader`. Ships three backends: StbTrueType, FreeType, SixLabors.Fonts. Each in its own NuGet package. Closest architectural reference.
- **SDL_ttf 3.0** -- Text engine abstraction for swappable rendering backends
- **Avalonia UI** -- `IDrawingContextImpl` for swappable rendering (Skia, Direct2D)

---

> **Review 2026-03-25**: Rewrote as skeleton overview with links to sub-phase docs (78A-78E). Moved detailed implementation tasks into individual sub-phase documents.
