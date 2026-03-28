# BMFont Comparison Tools

Side-by-side comparison of glyph output across four backends: FreeType, GDI, DirectWrite, and BMFont64.

## Quick Start

From the repo root:

```bash
# Generate all backends from .bmfc files
dotnet run --project tests/bmfont-compare/GenerateAll/ --framework net10.0-windows -- tests/bmfont-compare/gum-bmfont temp-test

# Generate comparison images
dotnet run --project tests/bmfont-compare/CompareGlyphs/ --framework net10.0-windows -- temp-test
```

Output:
- `temp-test/comparison.png` — fire effect (outline + gradient + shadow)
- `temp-test/comparison2.png` — plain (no effects)

## Tools

### GenerateAll

Reads `.bmfc` files from a source directory and generates `.fnt` + `.png` output for each backend.

```
dotnet run --project tests/bmfont-compare/GenerateAll/ --framework net10.0-windows -- <bmfc-dir> <output-dir>
```

- Runs FreeType, GDI, and DirectWrite via KernSmith
- Runs BMFont64.exe if found at `c:\tools\bmfont64.exe` or on PATH
- Output naming: `{configname}-{backend}.fnt` / `{configname}-{backend}_0.png`

### CompareGlyphs

Extracts individual glyphs from atlas PNGs using `.fnt` coordinates and produces side-by-side comparison images.

```
dotnet run --project tests/bmfont-compare/CompareGlyphs/ --framework net10.0-windows -- <data-dir>
```

- Looks for `fire-{backend}.fnt`/`.png` and `plain-{backend}.fnt`/`.png`
- Produces `comparison.png` (fire) and `comparison2.png` (plain)
- Backends: freetype, gdi, directwrite, bmfont (skips missing ones)

### GenerateGdi / GenerateDirectWrite

Run all 15 Gum UI `.bmfc` configs through a single backend. Output goes to `gum-gdi/` or `gum-directwrite/`.

```
dotnet run --project tests/bmfont-compare/GenerateGdi/ --framework net10.0-windows
dotnet run --project tests/bmfont-compare/GenerateDirectWrite/ --framework net10.0-windows
```

### Python Diff Scripts

```bash
# Compare metrics across all 15 fonts between two backend output dirs
python tests/bmfont-compare/diff_all_fonts.py

# Deep comparison of a single .fnt pair
python tests/bmfont-compare/diff_fnt.py <file_a.fnt> <file_b.fnt>
```

## Input Files

`.bmfc` configs are in `gum-bmfont/`:
- `fire.bmfc` — Georgia 56pt with outline, gradient, shadow (fire effect)
- `plain.bmfc` — Georgia 56pt plain
- 15 Gum UI font configs (various fonts, sizes, styles with negative fontSize)

## Requirements

- Windows (GDI and DirectWrite are Windows-only)
- .NET 10.0
- BMFont64.exe at `c:\tools\bmfont64.exe` (optional, for reference output)
