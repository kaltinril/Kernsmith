# KernSmith.Fonts.Web

A web font source for KernSmith. Fetches WOFF fonts from CSS-based font CDNs by parsing `@font-face` responses.

## Overview

`WebFontSource` implements the core `KernSmith.Font.IFontSource` abstraction. It fetches a stylesheet from a font CDN (Bunny Fonts, Google Fonts, or any compatible custom endpoint), parses the `@font-face` rules, and downloads the `.woff` file for the requested Unicode subset.

This is primarily useful in browser/WASM scenarios (e.g. running KernSmith in Blazor WebAssembly) where there is no filesystem and fonts must be obtained over the network.

**Platform**: Cross-platform (`net8.0`, `net10.0`). Uses `HttpClient` from the BCL.

## Usage

```
dotnet add package KernSmith.Fonts.Web
```

```csharp
using KernSmith;
using KernSmith.Font;
using KernSmith.Fonts.Web;

var source = new WebFontSource(WebFontProvider.BunnyFonts);
byte[] bytes = await source.GetFontAsync("Roboto", weight: 400);

BmFont.RegisterFont("Roboto", bytes);
var result = BmFont.GenerateFromSystem("Roboto", size: 32);
```

### Providers

```csharp
new WebFontSource(WebFontProvider.BunnyFonts);                 // privacy-friendly default
new WebFontSource(WebFontProvider.GoogleFonts);                // fonts.googleapis.com
new WebFontSource(WebFontProvider.Custom("https://my.cdn/css")); // any compatible endpoint
```

### Subsets and styles

```csharp
await source.GetFontAsync("Roboto", weight: 700, style: FontStyle.Italic, subset: "latin");
await source.GetFontAsync("Roboto", subset: "cyrillic");
```

### Caching

Fetched fonts are cached in memory (keyed by family + weight + style + subset). Pass a
`diskCachePath` to also persist across runs (leave it null on WASM):

```csharp
new WebFontSource(WebFontProvider.BunnyFonts, diskCachePath: "cache/fonts");
```

## Limitations

- **WOFF only** — `.woff2` URLs are ignored because WOFF2 uses Brotli compression, which KernSmith does not yet decode.
- **No family listing** — CSS CDNs do not expose a discovery endpoint, so `ListFamiliesAsync` returns an empty list.

## Build

```
dotnet build src/KernSmith.Fonts.Web/KernSmith.Fonts.Web.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project documentation.
