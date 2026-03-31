# RegisterFont File-Path Overload

## Problem

Every `RegisterFont` call today requires the caller to read font bytes themselves:

```csharp
KernSmithFontCreator.RegisterFont("Bungee",
    System.IO.File.ReadAllBytes("Content/Fonts/Bungee-Regular.ttf"));
```

This has two issues:

1. **Unnecessary ceremony** — reading a file from disk is the 90% case, but every user must write the `File.ReadAllBytes` boilerplate.
2. **Broken on XNA-like platforms** — `File.ReadAllBytes("Content/...")` doesn't work on Android, iOS, or consoles. XNA-like frameworks (MonoGame, KNI, FNA) require `TitleContainer.OpenStream` to locate content files correctly. Users who copy the current docs example will hit runtime errors on non-desktop platforms.

## Proposed Change

Add a `string filePath` overload to `RegisterFont` on the integration wrapper (`KernSmithFontCreator`), using `TitleContainer.OpenStream` for file loading.

### Why Not on Core `BmFont`?

The core `KernSmith` library has no XNA dependency. It's used by Raylib, Skia, Blazor WASM, and server-side consumers — none of which have `TitleContainer`. A file-path overload on `BmFont` would have to use `File.ReadAllBytes`, which:

- Doesn't work on Android/iOS/consoles (the main reason we're doing this)
- Creates a confusing split where two APIs with the same signature do different things

Instead, the file-path convenience belongs **only** on the integration layer, where `TitleContainer` is always available. Non-XNA consumers who want file loading can still call `File.ReadAllBytes` themselves — that's a one-liner they already understand, and we can't improve on it without an XNA-like content system.

### Integration Wrapper (`KernSmithFontCreator.cs`)

The shared `KernSmithFontCreator.cs` (compiled into MonoGameGum, KniGum, and FnaGum via linked Compile) adds an overload that uses `TitleContainer.OpenStream`. This works correctly everywhere — desktop, Android, iOS, consoles — because `TitleContainer` abstracts the platform's content file system.

```csharp
/// <summary>
/// Registers a font file under a family name by reading it via
/// TitleContainer.OpenStream, which resolves content files correctly
/// on all platforms (desktop, Android, iOS, consoles).
/// </summary>
/// <param name="familyName">Font family name (e.g., "Arial").</param>
/// <param name="filePath">
/// Path to a .ttf, .otf, or .woff font file, relative to the
/// title container root (typically the Content directory).
/// </param>
/// <param name="style">
/// Optional style name (e.g., "Bold", "Italic", "Bold Italic").
/// When null, registers as the default/regular variant.
/// </param>
/// <param name="faceIndex">TTC face index (0 for single-face font files).</param>
public static void RegisterFont(string familyName, string filePath,
    string? style = null, int faceIndex = 0)
{
    ArgumentNullException.ThrowIfNull(familyName);
    ArgumentNullException.ThrowIfNull(filePath);

    byte[] fontData;
    using (Stream stream = TitleContainer.OpenStream(filePath))
    using (MemoryStream ms = new())
    {
        stream.CopyTo(ms);
        fontData = ms.ToArray();
    }

    BmFont.RegisterFont(familyName, fontData, style, faceIndex);
}
```

**Note:** `TitleContainer` is from `Microsoft.Xna.Framework` (MonoGame), `nkast.Xna.Framework` (KNI), or FNA — all three provide it. Since each integration project already references its respective XNA framework, no new dependencies are needed.

## What Stays the Same

The existing `byte[]` overloads remain unchanged on both `BmFont` and `KernSmithFontCreator`. They are still needed for:

- Embedded resources (`Assembly.GetManifestResourceStream`)
- Fonts bundled in zip/archive files
- Fonts fetched over HTTP (e.g., Blazor WASM downloading from a CDN)
- Fonts loaded via platform-specific APIs the caller controls
- Unit tests that provide synthetic font data

## Files to Change

| File | Change |
|---|---|
| `integrations/KernSmith.MonoGameGum/KernSmithFontCreator.cs` | Add `RegisterFont(string familyName, string filePath, ...)` overload using `TitleContainer.OpenStream` |

Since KniGum and FnaGum compile `KernSmithFontCreator.cs` via linked `<Compile Include>`, changing the MonoGame file automatically covers all three XNA-like integrations. No changes needed to core `BmFont.cs`.

## Tests to Add

### `KernSmithFontCreator.RegisterFont(string, string)`

Testing `TitleContainer.OpenStream` requires an XNA-like runtime context, which may not be feasible in pure unit tests. Recommended approach:

1. **Compilation verification** — Ensure the overload compiles and links in all three integration projects (MonoGameGum, KniGum, FnaGum). This is automatic since they share the same source file.

2. **RegisterFont_FilePath_NullFamilyName_ThrowsArgumentNullException** — Call `RegisterFont(null!, "some/path.ttf")`, assert `ArgumentNullException`. Does not require TitleContainer.

3. **RegisterFont_FilePath_NullPath_ThrowsArgumentNullException** — Call `RegisterFont("Test", (string)null!)`, assert `ArgumentNullException`. Does not require TitleContainer.

4. **Manual integration test** — In a MonoGame sample project, register a .ttf via file path, render text with it, and verify it displays correctly on desktop. If mobile targets are available, verify there too.

## Doc Updates After Shipping

Once this ships, update the Gum docs to use the simpler API:

**Before (current):**
```csharp
KernSmithFontCreator.RegisterFont("Bungee",
    System.IO.File.ReadAllBytes("Content/Fonts/Bungee-Regular.ttf"));
```

**After:**
```csharp
KernSmithFontCreator.RegisterFont("Bungee", "Content/Fonts/Bungee-Regular.ttf");
```

Files to update in the Gum repo:
- `docs/code/standard-visuals/textruntime/fonts.md` — RegisterFont examples in the "Registering Custom .ttf Fonts" section
- Any other docs referencing `RegisterFont` with `File.ReadAllBytes`
