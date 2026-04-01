# KernSmith Blazor WASM Sample

Demonstrates bitmap font generation running entirely client-side in the browser using the StbTrueType rasterizer backend.

## Running Locally

### Development (interpreter mode)

```
dotnet run --project samples/KernSmith.Samples.BlazorWasm/KernSmith.Samples.BlazorWasm.csproj
```

Open http://localhost:5000 in your browser.

### Published with AOT (recommended for performance)

```
dotnet publish samples/KernSmith.Samples.BlazorWasm/KernSmith.Samples.BlazorWasm.csproj -c Release
dotnet serve -d samples/KernSmith.Samples.BlazorWasm/bin/Release/net10.0/publish/wwwroot -p 5050
```

Open http://localhost:5050 in your browser.

AOT compilation requires the `wasm-tools` workload:

```
dotnet workload install wasm-tools
```

## What It Demonstrates

- **Automated checks**: Verifies StbTrueType registers, FreeType is absent, capabilities report correctly, system font provider doesn't crash
- **Font generation**: Upload a TTF file, generate a bitmap font with configurable size and SDF option, preview the atlas, download .fnt and .png
- **Zero native dependencies**: Runs entirely on managed C# code — no FreeType, no P/Invoke

## Performance (Roboto-Regular, 32px, ASCII)

| Mode | Interpreter | AOT (first load) | AOT (warm) |
|------|-------------|-------------------|------------|
| Normal | ~256 ms | ~60 ms | ~14 ms |
| SDF | ~1,344 ms | ~110 ms | ~51 ms |

AOT compilation is strongly recommended for production use. The browser caches compiled WASM modules, so subsequent loads are near-native speed.

## WASM Limitations

- **No system font loading** — use `LoadFont()` with font bytes or `BmFont.RegisterFont()`
- **No FreeType backend** — only StbTrueType is available (pure C#)
- **File I/O** — `ToFile()` is unavailable; use in-memory APIs (`FntText`, `GetPngData()`)
- **Single-threaded** — batch generation parallelism is automatically disabled
- **Memory** — default WASM heap is ~127 MB; large CJK fonts may require subsetting

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Forces StbTrueType assembly load to prevent trimming |
| `Pages/Index.razor` | Validation checks + font generation UI |
| `wwwroot/index.html` | Host page with JS download helper |

See the [KernSmith repository](https://github.com/kaltinril/KernSmith) for full project documentation.
