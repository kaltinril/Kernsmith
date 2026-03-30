# Command Reference

## generate

Generate BMFont files from a font. This is the main command.

```
kernsmith generate -f <font> -s <size> [options]
```

### Font Source (one required)

| Flag | Description |
|------|-------------|
| `-f, --font <path>` | Font file path (TTF, OTF, WOFF) |
| `--system-font <name>` | Use a system-installed font by family name |
| `--face <n>` | Face index for .ttc collections (default: 0) |

### Size and Rendering

| Flag | Description |
|------|-------------|
| `-s, --size <n>` | Font size in pixels (required) |
| `--dpi <n>` | DPI (default: 72) |
| `--aa <mode>` | Anti-aliasing: `none`, `grayscale`, `light`, `lcd` (default: grayscale) |
| `--mono` | Disable anti-aliasing (alias for `--aa none`) |
| `--sdf` | Enable Signed Distance Field rendering |
| `--super-sample <n>` | Super sampling level 1-4 (default: 1) |
| `--hinting / --no-hinting` | Enable/disable FreeType hinting (default: on) |
| `--height-percent <n>` | Vertical height scaling percentage (default: 100) |
| `--match-char-height` | Match rendered height to requested pixel height |
| `--fallback-char <char>` | Fallback character for missing glyphs |

### Style

| Flag | Description |
|------|-------------|
| `-b, --bold` | Enable bold (uses native face when available, falls back to synthetic) |
| `-i, --italic` | Enable italic (uses native face when available, falls back to synthetic) |
| `--synthetic-bold` | Force synthetic bold, skip native bold face lookup |
| `--synthetic-italic` | Force synthetic italic, skip native italic face lookup |
| `--color-font` | Enable color font rendering (COLR/CPAL) |
| `--color-palette <n>` | Color palette index (default: 0) |

With `--font` (file path), `--bold` and `--synthetic-bold` produce identical results since there is no font family to search. With `--system-font`, `--bold` tries the native bold face first; `--synthetic-bold` forces synthetic on the regular face. GDI backend limitation: cannot apply synthetic bold when a native bold face exists -- use FreeType or DirectWrite.

### Character Set

| Flag | Description |
|------|-------------|
| `-c, --charset <preset>` | Preset: `ascii` (default), `extended`, `latin` |
| `--chars <string>` | Explicit characters to include |
| `--chars-file <path>` | Read characters from a UTF-8 text file |
| `--range <start-end>` | Unicode range in hex (repeatable) |

Character set options can be combined. Multiple `--range` flags are allowed.

```bash
# Specific characters only
kernsmith generate -f font.ttf -s 32 --chars "ABCabc0123"

# Unicode range
kernsmith generate -f font.ttf -s 32 --range 0020-007E --range 00A0-00FF
```

### Texture Atlas

| Flag | Description |
|------|-------------|
| `--max-texture <n>` | Maximum texture size in pixels (default: 1024) |
| `--max-texture-width <n>` | Maximum texture width (independent of height) |
| `--max-texture-height <n>` | Maximum texture height (independent of width) |
| `--autofit` | Auto-fit smallest texture size for all glyphs |
| `--padding <n>` | Uniform padding around each glyph (default: 0) |
| `--padding <u,r,d,l>` | Per-side padding (up, right, down, left) |
| `--spacing <n>` | Spacing between glyphs (default: 1) |
| `--spacing <h,v>` | Horizontal and vertical spacing |
| `--pot / --no-pot` | Force power-of-two textures (default: on) |
| `--packer <alg>` | Packing algorithm: `maxrects` (default), `skyline` |
| `--channel-pack` | Pack glyphs into individual RGBA channels |
| `--equalize-heights` | Equalize all glyph cell heights |
| `--force-offsets-zero` | Set all xoffset/yoffset to zero |

### Effects

| Flag | Description |
|------|-------------|
| `--outline <n>[,color]` | Outline width in pixels, optional hex color |
| `--gradient <top>,<bottom>` | Vertical gradient with hex colors |
| `--gradient-angle <degrees>` | Gradient rotation angle |
| `--gradient-midpoint <0.0-1.0>` | Gradient midpoint / bias |
| `--shadow <x>,<y>[,color[,blur]]` | Drop shadow with offset, optional color and blur |
| `--hard-shadow` | Use a crisp shadow silhouette instead of soft antialiased edges |

Effects can be combined freely. All color values are hex (e.g., `FF0000` for red).

```bash
# 3px red outline
kernsmith generate -f font.ttf -s 32 --outline 3,FF0000

# Gradient with shadow
kernsmith generate -f font.ttf -s 48 --gradient FFFFFF,888888 --shadow 2,2,000000,1

# All three effects
kernsmith generate -f font.ttf -s 48 --outline 2,000000 --gradient FFFFFF,888888 --shadow 2,3,000000,2
```

### Kerning

| Flag | Description |
|------|-------------|
| `--kerning` | Enable kerning (default: on) |
| `--no-kerning` | Disable kerning pair extraction |

### Variable Fonts

| Flag | Description |
|------|-------------|
| `--axis <tag>=<value>` | Set a variation axis value (repeatable) |
| `--instance <name>` | Use a named font instance |

```bash
kernsmith generate -f RobotoFlex.ttf -s 32 --axis wght=700 --axis wdth=90
kernsmith generate -f RobotoFlex.ttf -s 32 --instance "Bold"
```

### Output

| Flag | Description |
|------|-------------|
| `-o, --output <path>` | Output path without extension (default: `./<fontname>`) |
| `--format <fmt>` | Output format: `text` (default), `xml`, `binary` |
| `--texture-format <fmt>` | Texture format: `png` (default), `tga`, `dds` |

### Configuration and Debugging

| Flag | Description |
|------|-------------|
| `--config <path>` | Load settings from a `.bmfc` configuration file |
| `--save-config <path>` | Save current settings to a `.bmfc` file |
| `--dry-run` | Show what would be generated without writing files |
| `--time` | Print actual generation time (excludes CLI startup) |
| `--profile` | Print stage-level timing breakdown |
| `-v, --verbose` | Show detailed progress |
| `-q, --quiet` | Suppress all output except errors |

---

## init

Generate a `.bmfc` configuration file from CLI flags without rendering a font. Accepts all the same flags as `generate`.

```
kernsmith init [options]
```

The `-o` flag sets the output `.bmfc` file path (default: `font.bmfc`).

```bash
# Scaffold a config
kernsmith init --system-font "Arial" -s 32 -o my-font.bmfc

# Config with effects
kernsmith init --system-font "Arial" -s 48 --outline 3,0055AA -o my-font.bmfc

# Then generate from it
kernsmith generate --config my-font.bmfc
```

---

## batch

Process multiple `.bmfc` config files in a single invocation. A failed job does not stop other jobs.

```
kernsmith batch <config1.bmfc> [config2.bmfc ...] [options]
```

| Flag | Description |
|------|-------------|
| `<paths>` | One or more `.bmfc` paths (supports glob patterns) |
| `--jobs <file>` | Text file listing `.bmfc` paths (one per line, `#` comments) |
| `--parallel <n>` | Max parallel jobs (default: 1, 0 = all CPU cores) |
| `--time` | Show total elapsed time in summary |

```bash
kernsmith batch fonts/*.bmfc --parallel 4 --time
kernsmith batch --jobs jobs.txt --parallel 0
```

---

## benchmark

Benchmark font generation performance. Runs N+1 iterations (first is warmup) and reports timing statistics. No files are written.

```
kernsmith benchmark -f <font> -s <size> [options]
```

| Flag | Description |
|------|-------------|
| `-f, --font <path>` | Font file path |
| `--system-font <name>` | System font by family name |
| `-s, --size <n>` | Font size in pixels (default: 32) |
| `-c, --charset <preset>` | Character set (default: ascii) |
| `--packer <alg>` | Packing algorithm (default: maxrects) |
| `--iterations <n>` | Number of timed iterations (default: 10) |

```bash
kernsmith benchmark -f roboto.ttf -s 32 --iterations 20
```

---

## inspect

Inspect an existing `.fnt` file. Auto-detects text, XML, and binary formats.

```
kernsmith inspect <path> [--json]
```

Shows font face, size, style, character count, kerning pairs, page count, texture dimensions, line height, Unicode ranges, and page filenames.

```bash
kernsmith inspect myfont.fnt
kernsmith inspect myfont.fnt --json
```

---

## convert

Convert between BMFont `.fnt` formats. Atlas page images are automatically copied to the output directory.

```
kernsmith convert <input> -o <output> [--format <text|xml|binary>]
```

Output format is inferred from the file extension when `--format` is not specified (`.fnt` = text, `.xml` = XML, `.bin` = binary).

```bash
kernsmith convert myfont.fnt -o myfont.xml
kernsmith convert myfont.fnt -o output/myfont.fnt --format binary
```

---

## list-fonts

List system-installed fonts, grouped by family.

```
kernsmith list-fonts [--filter <pattern>] [--json]
```

```bash
kernsmith list-fonts
kernsmith list-fonts --filter "roboto"
kernsmith list-fonts --json
```

---

## list-rasterizers

List available rasterizer backends on the current platform. Shows each backend's availability, platform support, and capabilities (color fonts, variable fonts, SDF, outline, system fonts).

```
kernsmith list-rasterizers
```

FreeType is the default backend and is available on all platforms. The GDI and DirectWrite backends are Windows-only alternatives.

```bash
kernsmith list-rasterizers
```

---

## info

Show metadata from a font file (TTF, OTF, WOFF). Displays family name, style, glyph count, kerning pairs, variation axes, named instances, and Unicode coverage.

```
kernsmith info <path> [--json]
```

```bash
kernsmith info Roboto-Regular.ttf
kernsmith info RobotoFlex.ttf --json
```
