# Phase 185 — Font Sourcing: IFontSource + Web Font Package

> **Status**: Planned
> **Created**: 2026-04-09
> **Related**: Phase 178 (WOFF decompression)

## Goal

Provide a thin abstraction for obtaining font bytes from various sources, and ship a web font package that fetches WOFF files from CSS-based font CDNs (Bunny Fonts, Google Fonts). Primary motivation: enable WASM/browser scenarios (XnaFiddle) where there is no filesystem.

## Context

KernSmith expects callers to provide raw font bytes (TTF, OTF, WOFF). How those bytes are obtained is left entirely to the consumer. This works, but the web font use case is non-trivial — it requires parsing CSS `@font-face` responses and extracting WOFF URLs. File loading (`File.ReadAllBytes`) and system font enumeration are already simple enough that they don't need library support.

The rasterizer backend pattern (`IRasterizer` in core, implementations in separate packages) is the proven model. Font sourcing follows the same shape.

### XnaFiddle Use Case

XnaFiddle is a browser-based KNI game runner (Blazor WASM). Users write C# in a Monaco editor and run it in the browser. There is no filesystem — custom samples that use KernSmith need a way to obtain font bytes without file uploads. A web font source solves this:

```csharp
var source = new WebFontSource(WebFontProvider.BunnyFonts);
byte[] bytes = await source.GetFontAsync("Roboto", weight: 400);
BmFont.RegisterFont("Roboto", bytes);
```

## Design Decisions

### Interface in core, implementations separate (Option C)

**Decision**: Define `IFontSource` in the core library. Each source type is its own package.

Rationale:
- Matches the existing rasterizer backend pattern
- Keeps core dependency-free (no HttpClient, no platform-specific APIs)
- File loading is a one-liner — no package needed
- System font enumeration is already handled — no package needed
- The only non-trivial source is web fonts

### Async-only interface

**Decision**: `IFontSource` returns `Task<byte[]>`. Sync sources use `Task.FromResult()`.

Rationale:
- The primary use case (web fonts) is inherently async
- Making the interface sync penalizes the only source type that actually needs the abstraction
- `Task.FromResult()` is zero-allocation on .NET 8+ for common cases

### WOFF only (not WOFF2)

**Decision**: Filter CSS responses for `.woff` URLs. Ignore `.woff2`.

Rationale:
- KernSmith already supports WOFF input
- WOFF2 uses Brotli compression — a separate format requiring separate decompression (Phase 178)
- WOFF2 support can be added later without API changes

### Default to latin subset

**Decision**: Default to the `latin` Unicode subset. Expose subset as an optional parameter.

Rationale:
- Web font CDNs split responses by Unicode range (latin, cyrillic, greek, etc.)
- Most game/UI use cases are latin-only
- Don't hide the complexity — just make the common case easy

### Simple caching

**Decision**: In-memory `ConcurrentDictionary<string, byte[]>` with an optional disk cache path.

Rationale:
- Web fonts should not be re-fetched on every call
- A full caching framework is overkill — keep it simple
- Callers can always cache externally if they need more control

## Package Structure

### Core addition: `IFontSource`

In `src/KernSmith/`, namespace `KernSmith.Font`:

```csharp
public interface IFontSource
{
    /// <summary>
    /// Gets font bytes by family name.
    /// </summary>
    Task<byte[]> GetFontAsync(
        string family,
        int weight = 400,
        FontStyle style = FontStyle.Normal,
        string subset = "latin",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available font families from this source.
    /// </summary>
    Task<IReadOnlyList<string>> ListFamiliesAsync(
        CancellationToken cancellationToken = default);
}
```

### New package: `KernSmith.Fonts.Web`

Project: `src/KernSmith.Fonts.Web/KernSmith.Fonts.Web.csproj`
Namespace: `KernSmith.Fonts.Web`
Dependencies: core library + `System.Net.Http`
Targets: `net8.0;net10.0`

Key types:

| Type | Purpose |
|------|---------|
| `WebFontSource` | `IFontSource` implementation that fetches from CSS-based CDNs |
| `WebFontProvider` | Enum or static class: `BunnyFonts`, `GoogleFonts`, `Custom(string baseUrl)` |
| `CssParser` | Internal — parses `@font-face` CSS to extract WOFF URLs by subset |
| `FontCache` | Internal — in-memory + optional disk caching |

### Web font flow

```
1. Build URL:  https://fonts.bunny.net/css?family={family}:{weight}
2. GET the CSS (text/css response)
3. Parse @font-face rules — extract .woff URL for the requested subset
4. GET the .woff file (binary)
5. Cache the bytes (keyed by family+weight+style+subset)
6. Return byte[]
```

## What We Are NOT Building

- **`FileFontSource` package** — `File.ReadAllBytes()` is a one-liner; no abstraction needed
- **`SystemFontSource` package** — system font enumeration is already handled
- **WOFF2 support** — separate phase (178); can be added later without API changes
- **Font discovery/metadata API** — out of scope; this is just "get me the bytes"

## Implementation Strategy

| Commit | Description | Risk |
|--------|-------------|------|
| 1 | Add `IFontSource` interface to core library | Low |
| 2 | Create `KernSmith.Fonts.Web` project with `WebFontSource`, `CssParser` | Medium |
| 3 | Add `FontCache` (in-memory + optional disk) | Low |
| 4 | Add tests — mock CSS responses, verify WOFF URL extraction | Low |
| 5 | Add sample showing XnaFiddle use case | Low |

## Files Modified

- `src/KernSmith/Font/IFontSource.cs` — **new file**, interface definition
- `src/KernSmith.Fonts.Web/KernSmith.Fonts.Web.csproj` — **new project**
- `src/KernSmith.Fonts.Web/WebFontSource.cs` — **new file**, main implementation
- `src/KernSmith.Fonts.Web/WebFontProvider.cs` — **new file**, provider enum/config
- `src/KernSmith.Fonts.Web/Internal/CssParser.cs` — **new file**, CSS @font-face parser
- `src/KernSmith.Fonts.Web/Internal/FontCache.cs` — **new file**, caching layer
- `KernSmith.sln` — add new project reference
- `Directory.Build.props` — may need update if new project needs special settings

## Verification

1. Unit test: parse known Bunny Fonts CSS response, extract correct `.woff` URL for latin subset
2. Unit test: ignore `.woff2` URLs, only return `.woff`
3. Unit test: caching — second call for same font returns cached bytes without HTTP request
4. Integration test: fetch Roboto 400 latin from Bunny Fonts, verify returned bytes are valid WOFF
5. Verify WASM compatibility: no `Parallel.ForEach`, no blocking `.Result`/`.Wait()` calls

## Future Follow-up

- **WOFF2 support** (Phase 178) — once decompression is available, `WebFontSource` can prefer `.woff2` URLs for smaller downloads
- **Provider-specific features** — Google Fonts API v2 (JSON metadata), Bunny Fonts family listing
- **Subset merging** — fetch multiple subsets and merge into a single font (complex, likely not needed for game use cases)
