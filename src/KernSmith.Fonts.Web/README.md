# KernSmith.Fonts.Web

A web font source for KernSmith. Fetches WOFF2 (or WOFF) fonts from CSS-based font CDNs by parsing `@font-face` responses.

## Overview

`WebFontSource` implements the core `KernSmith.Font.IFontSource` abstraction. It fetches a stylesheet from a font CDN (Bunny Fonts, Google Fonts, or any compatible custom endpoint), parses the `@font-face` rules, and downloads the font file for the requested Unicode subset: the `.woff2` when offered (smaller), otherwise the `.woff`. KernSmith core decodes both, WOFF2 with its own managed Brotli decoder, so it works in the browser.

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

- **WOFF2 or WOFF only** — a source is picked by its `format()` hint, else its file extension; TTF/OTF/EOT/SVG sources are ignored, so a subset offering neither WOFF2 nor WOFF throws `InvalidOperationException`.
- **User-Agent** — outside a browser the source sends a modern Chrome User-Agent (unless your `HttpClient` already has one), because Google Fonts serves TTF to unknown agents. In the browser it sends none and the browser's own UA is used: a custom `User-Agent` is not CORS-safelisted, and the CDNs reject the preflight it causes in Firefox.
- **No family listing** — CSS CDNs do not expose a discovery endpoint, so `ListFamiliesAsync` returns an empty list.

## Build

```
dotnet build src/KernSmith.Fonts.Web/KernSmith.Fonts.Web.csproj
```

See the [KernSmith repository](https://github.com/kaltinril/Kernsmith) for full project documentation.
