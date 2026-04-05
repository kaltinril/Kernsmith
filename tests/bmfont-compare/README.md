# BMFont Comparison Tools

Side-by-side comparison of glyph output across four backends: FreeType, GDI, DirectWrite, and BMFont64.

## Quick Start

### Regression check (main vs feature branch)

The fastest way to verify a change doesn't alter output. From the repo root:

```bash
# Compare current branch against main (handles git stash/checkout automatically)
python tests/bmfont-compare/regression_check.py

# Compare a specific branch against main
python tests/bmfont-compare/regression_check.py --branch feature/my-change

# Re-run diff only (skip regeneration, uses existing main_ baselines)
python tests/bmfont-compare/regression_check.py --skip-generate

# With pixel tolerance for antialiasing
python tests/bmfont-compare/regression_check.py --tolerance 1
```

This automates the full workflow: stash changes, checkout main, generate baselines, checkout branch, restore stash, generate current, run diff. On failure or Ctrl+C it restores your original branch and stash.

Exit codes: `0` = all identical, `1` = differences found, `2` = error.

### Generate comparison images only

```bash
# Generate all backends from .bmfc files
dotnet run --project tests/bmfont-compare/GenerateAll/ --framework net10.0-windows -- tests/bmfont-compare/gum-bmfont tests/bmfont-compare/output
```

Output includes per-backend `.fnt` + `.png` files and `comparison*.png` side-by-side images.

## Tools

### regression_check.py — Automated regression workflow

Handles the full git stash/checkout/generate/diff cycle automatically so baselines are always generated from the correct branch.

```bash
python tests/bmfont-compare/regression_check.py [options]
```

| Flag | Default | Description |
|------|---------|-------------|
| `--base` | `main` | Base branch for baseline generation |
| `--branch` | current branch | Feature branch to test |
| `--output` | `tests/bmfont-compare/output` | Output directory |
| `--tolerance` | `0` | Per-channel pixel tolerance |
| `--skip-generate` | off | Skip generation, only run diff |

Steps performed:
1. Stash uncommitted changes (if any)
2. Checkout base branch, generate fonts, copy output as `main_`-prefixed baselines
3. Checkout feature branch, restore stash, generate fonts
4. Run `diff_comparisons.py` to compare baselines vs current
5. Restore original branch on failure

### GenerateAll

Reads `.bmfc` files from a source directory and generates `.fnt` + `.png` output for each backend.

```
dotnet run --project tests/bmfont-compare/GenerateAll/ --framework net10.0-windows -- <bmfc-dir> <output-dir>
```

- Runs FreeType, GDI, DirectWrite, and StbTrueType via KernSmith
- Runs BMFont64.exe if found at `c:\tools\bmfont64.exe` or on PATH
- Output naming: `{configname}-{backend}.fnt` / `{configname}-{backend}_0.png`
- Also generates `comparison*.png` side-by-side images

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

#### diff_comparisons.py — Full regression detection (PNGs + FNT metadata)

Compares baseline vs current output across three categories:

1. **Hardcoded comparison PNGs** (`comparison.png` through `comparison4.png`) — magenta-highlighted pixel diffs
2. **Per-font comparison PNGs** (`comparison-*.png`) — auto-discovered, same magenta diff format
3. **FNT metadata files** (`*.fnt`) — line-by-line text diff, skipping the `kernsmith` version line (contains commit hash that always differs between branches)

Identical pixels are dimmed, different pixels are bright magenta (#FF00FF).

```bash
# Full regression check: PNGs + per-font PNGs + FNT metadata
python tests/bmfont-compare/diff_comparisons.py --dir tests/bmfont-compare/output

# Compare two separate directories
python tests/bmfont-compare/diff_comparisons.py --baseline dir_a --current dir_b --output diffs/

# With per-channel tolerance (e.g., for antialiasing differences)
python tests/bmfont-compare/diff_comparisons.py --dir tests/bmfont-compare/output --tolerance 1
```

Exit codes: `0` = all identical, `1` = differences found, `2` = error.

#### diff_images.py / diff_fnt.py / diff_all_fonts.py — Atlas and metrics diffs

```bash
# Compare PNG atlas textures between two directories (4x amplified diff)
python tests/bmfont-compare/diff_images.py [dir_a] [dir_b] --output-dir diffs/

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
- Python 3 + Pillow (`pip install Pillow`) for diff scripts
- BMFont64.exe at `c:\tools\bmfont64.exe` (optional, for reference output)
