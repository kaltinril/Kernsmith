# KernSmith.Samples

Usage examples demonstrating the KernSmith bitmap font generation library.

## Samples

The `Program.cs` demonstrates these scenarios:

- **Basic generation** -- Load a TTF, generate a bitmap font with `BmFont.Generate()`, and write `.fnt` + `.png` to disk.
- **FromConfig (.bmfc)** -- Load a `.bmfc` config file and generate a font from it with `BmFont.FromConfig()`.
- **FromConfig (.hiero)** -- Load the `sample.hiero` (Hiero/libGDX) config with the same `BmFont.FromConfig()` call. The format is auto-detected by inspecting the file content (the extension is only a fallback tiebreaker), so `.bmfc` and `.hiero` work through the same API.
- **Builder pattern** -- Use the fluent `BmFont.Builder()` API to apply layered effects (outline, shadow, gradient).
- **In-memory access** -- Get the `.fnt` descriptor as text/XML and atlas pages as PNG byte arrays without any file I/O, and export the config with `result.ToBmfc()` or `result.ToHiero()`.

## Running

```
dotnet run --project samples/KernSmith.Samples/KernSmith.Samples.csproj
```

See the [root README](../../README.md) for full project documentation.
