# KernSmith CLI

A cross-platform command-line tool for generating BMFont-compatible bitmap fonts from TTF, OTF, and WOFF files. Produces `.fnt` + `.png` pairs ready for use in game engines and rendering frameworks.

## Quick Start

Generate a bitmap font with default settings (ASCII charset, 1024x1024 max texture, text format):

```
kernsmith generate -f MyFont.ttf -s 32
```

Generate bold + italic at 48px and save as XML:

```
kernsmith generate -f MyFont.ttf -s 48 -b -i --format xml
```

Generate with extended Latin characters and a 2px outline:

```
kernsmith generate -f MyFont.ttf -s 24 -c latin --outline 2
```

Use a system-installed font instead of a file:

```
kernsmith generate --system-font "Arial" -s 32
```

## Commands

### generate

Generate BMFont files from a font. This is the main command.

```
kernsmith generate -f <font> -s <size> [options]
```

#### Font Source (one required)

| Flag | Description |
|------|-------------|
| `-f, --font <path>` | Font file path (TTF, OTF, WOFF) |
| `--system-font <name>` | Use a system-installed font by family name |
| `--face <n>` | Face index for .ttc collections (default: 0) |

#### Size and Rendering

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
| `--fallback-char <char>` | Fallback character for missing glyphs (char or codepoint) |

#### Style

| Flag | Description |
|------|-------------|
| `-b, --bold` | Enable synthetic bold |
| `-i, --italic` | Enable synthetic italic |
| `--color-font` | Enable color font rendering (COLR/CPAL) |
| `--color-palette <n>` | Color palette index (default: 0) |

#### Character Set

| Flag | Description |
|------|-------------|
| `-c, --charset <preset>` | Preset: `ascii` (default), `extended`, `latin` |
| `--chars <string>` | Explicit characters to include |
| `--chars-file <path>` | Read characters from a UTF-8 text file |
| `--range <start-end>` | Unicode range in hex, repeatable |

Character set options can be combined. Multiple `--range` flags are allowed.

```
# ASCII preset (default)
kernsmith generate -f font.ttf -s 32

# Extended ASCII (0x00-0xFF)
kernsmith generate -f font.ttf -s 32 -c extended

# Latin characters
kernsmith generate -f font.ttf -s 32 -c latin

# Specific characters only
kernsmith generate -f font.ttf -s 32 --chars "ABCabc0123"

# Characters from a file
kernsmith generate -f font.ttf -s 32 --chars-file my-chars.txt

# Unicode range (Basic Latin + Latin-1 Supplement)
kernsmith generate -f font.ttf -s 32 --range 0020-007E --range 00A0-00FF
```

#### Texture Atlas

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

```
# Auto-fit to smallest texture
kernsmith generate -f font.ttf -s 32 --autofit

# Large atlas with per-side padding
kernsmith generate -f font.ttf -s 64 --max-texture 2048 --padding 2,4,2,4

# Non-power-of-two, skyline packer
kernsmith generate -f font.ttf -s 32 --no-pot --packer skyline
```

#### Effects

| Flag | Description |
|------|-------------|
| `--outline <n>[,color]` | Outline width in pixels, optional hex color |
| `--gradient <top>,<bottom>` | Vertical gradient with hex colors |
| `--gradient-angle <degrees>` | Gradient rotation angle |
| `--gradient-midpoint <0.0-1.0>` | Gradient midpoint / bias |
| `--shadow <x>,<y>[,color[,blur]]` | Drop shadow with offset, optional color and blur |
| `--hard-shadow` | Use crisp shadow silhouette instead of soft antialiased edges |

See the [Effects](#effects) section below for detailed examples.

#### Kerning

| Flag | Description |
|------|-------------|
| `--kerning` | Enable kerning (default: on) |
| `--no-kerning` | Disable kerning pair extraction |

#### Variable Fonts

| Flag | Description |
|------|-------------|
| `--axis <tag>=<value>` | Set a variation axis value (repeatable) |
| `--instance <name>` | Use a named font instance |

```
# Set weight and width axes
kernsmith generate -f RobotoFlex.ttf -s 32 --axis wght=700 --axis wdth=90

# Use a named instance
kernsmith generate -f RobotoFlex.ttf -s 32 --instance "Bold"
```

#### Output

| Flag | Description |
|------|-------------|
| `-o, --output <path>` | Output path without extension (default: `./<fontname>`) |
| `--format <fmt>` | Output format: `text` (default), `xml`, `binary` |
| `--texture-format <fmt>` | Texture format: `png` (default), `tga`, `dds` |

```
# Output XML format with TGA textures
kernsmith generate -f font.ttf -s 32 --format xml --texture-format tga

# Output to a specific directory
kernsmith generate -f font.ttf -s 32 -o output/myfont
```

#### Configuration and Debugging

| Flag | Description |
|------|-------------|
| `--config <path>` | Load settings from a .bmfc configuration file |
| `--save-config <path>` | Save current settings to a .bmfc file |
| `--dry-run` | Show what would be generated without writing files |
| `--time` | Print actual generation time (excludes CLI startup) |
| `--profile` | Print stage-level timing breakdown (font parsing, rasterization, packing, etc.) |
| `-v, --verbose` | Show detailed progress |
| `-q, --quiet` | Suppress all output except errors |
| `--no-color` | Disable colored output |

---

### init

Generate a `.bmfc` configuration file from CLI flags without rendering a font. This lets you scaffold a config, tweak it by hand, then run `kernsmith generate --config`.

The `init` command accepts all the same flags as `generate` (font source, size, effects, atlas settings, etc.). The `-o` flag sets the output `.bmfc` file path instead of the font output path.

```
kernsmith init [options]
```

```
# Create a basic config for a system font
kernsmith init --system-font "Arial" -s 32 -o my-font.bmfc

# Scaffold a config with effects
kernsmith init --system-font "Arial" -s 48 --outline 3,0055AA -o my-font.bmfc

# Create a config from a font file with extended Latin
kernsmith init -f fonts/Roboto-Regular.ttf -s 24 -c latin --kerning -o roboto.bmfc

# Then generate from the config (with optional overrides)
kernsmith generate --config roboto.bmfc
```

If no `-o` flag is provided, the default output is `font.bmfc` in the current directory.

---

### inspect

Inspect an existing `.fnt` file. Auto-detects text, XML, and binary formats.

```
kernsmith inspect <path> [--json]
```

```
# Human-readable summary
kernsmith inspect myfont.fnt

# JSON output for scripting
kernsmith inspect myfont.fnt --json
```

Shows: font face, size, style, character count, kerning pairs, page count, texture dimensions, line height, Unicode ranges, and page filenames.

---

### convert

Convert between BMFont `.fnt` formats (text, XML, binary). Atlas page image files are automatically copied to the output directory.

```
kernsmith convert <input> -o <output> [--format <text|xml|binary>]
```

The output format is inferred from the file extension when `--format` is not specified:
- `.fnt` -- text
- `.xml` -- XML
- `.bin` -- binary

```
# Convert text to XML
kernsmith convert myfont.fnt -o myfont.xml

# Convert to binary with explicit format
kernsmith convert myfont.fnt -o output/myfont.fnt --format binary
```

---

### list-fonts

List system-installed fonts, grouped by family.

```
kernsmith list-fonts [--filter <pattern>] [--json]
```

```
# List all fonts
kernsmith list-fonts

# Filter by name (case-insensitive substring match)
kernsmith list-fonts --filter "roboto"

# JSON output
kernsmith list-fonts --json
```

---

### info

Show metadata from a font file (TTF, OTF, WOFF). Displays family name, style, glyph count, kerning pairs, variation axes, named instances, and Unicode coverage.

```
kernsmith info <path> [--json]
```

```
# Human-readable
kernsmith info Roboto-Regular.ttf

# JSON output
kernsmith info RobotoFlex.ttf --json
```

---

### batch

Process multiple `.bmfc` config files in a single invocation. Output paths are checked for collisions before any generation starts. A failed job does not stop other jobs from running.

```
kernsmith batch <config1.bmfc> [config2.bmfc ...] [options]
```

| Flag | Description |
|------|-------------|
| `<paths>` | One or more `.bmfc` config file paths (supports glob patterns) |
| `--jobs <file>` | Text file listing `.bmfc` paths (one per line, `#` comments) |
| `--parallel <n>` | Max parallel jobs (default: 1, 0 = all CPU cores) |
| `--time` | Show total elapsed time in summary |

```
# Process all .bmfc files in a directory with 4 parallel jobs
kernsmith batch fonts/*.bmfc --parallel 4 --time

# Process from a jobs file using all CPU cores
kernsmith batch --jobs jobs.txt --parallel 0
```

---

### benchmark

Benchmark font generation performance. Runs N+1 iterations (first is warmup) and reports timing statistics. No output files are written.

```
kernsmith benchmark -f <font> -s <size> [options]
```

| Flag | Description |
|------|-------------|
| `-f, --font <path>` | Font file path (TTF, OTF, WOFF) |
| `--system-font <name>` | Use a system-installed font by family name |
| `-s, --size <n>` | Font size in pixels (default: 32) |
| `-c, --charset <preset>` | Character set: `ascii` (default), `extended`, `latin` |
| `--packer <maxrects\|skyline>` | Packing algorithm (default: maxrects) |
| `--iterations <n>` | Number of timed iterations (default: 10) |

```
# Benchmark with 20 iterations
kernsmith benchmark -f roboto.ttf -s 32 --iterations 20

# Benchmark a system font
kernsmith benchmark --system-font "Arial" -s 48
```

## Effects

Effects can be combined freely. All color values are specified as hex (e.g., `FF0000` for red, `000000` for black).

### Outline

Add an outline around each glyph. Specify width in pixels, optionally followed by a color.

```
# 2px black outline (default color)
kernsmith generate -f font.ttf -s 32 --outline 2

# 3px red outline
kernsmith generate -f font.ttf -s 32 --outline 3,FF0000
```

### Gradient

Apply a vertical gradient across each glyph. Colors are specified as hex values, either comma-separated or as two arguments.

```
# White-to-gray gradient
kernsmith generate -f font.ttf -s 32 --gradient FFFFFF,888888

# Two-argument form
kernsmith generate -f font.ttf -s 32 --gradient FFFFFF 888888

# Rotated gradient (45 degrees)
kernsmith generate -f font.ttf -s 32 --gradient FF0000,0000FF --gradient-angle 45

# Shift the gradient midpoint (0.0-1.0, default 0.5)
kernsmith generate -f font.ttf -s 32 --gradient FFFFFF,000000 --gradient-midpoint 0.3
```

### Shadow

Add a drop shadow with X/Y offset, optional color, and optional blur radius.

```
# Simple 2px shadow
kernsmith generate -f font.ttf -s 32 --shadow 2,2

# Black shadow with blur
kernsmith generate -f font.ttf -s 32 --shadow 2,2,000000,1

# Colored shadow, no blur
kernsmith generate -f font.ttf -s 32 --shadow 1,1,444444
```

### Hard Shadow

Use `--hard-shadow` with `--shadow` to get a crisp binarized silhouette instead of soft antialiased edges.

```
# Soft shadow (default)
kernsmith generate -f font.ttf -s 32 --shadow 2,2

# Hard shadow — same offset, crisp edges
kernsmith generate -f font.ttf -s 32 --shadow 2,2 --hard-shadow
```

### Combining Effects

```
# Outline + gradient
kernsmith generate -f font.ttf -s 48 --outline 2,000000 --gradient FFFFFF,AAAAAA

# Outline + shadow
kernsmith generate -f font.ttf -s 48 --outline 2 --shadow 2,2,000000,1

# All three
kernsmith generate -f font.ttf -s 48 --outline 2,000000 --gradient FFFFFF,888888 --shadow 2,3,000000,2
```

## Config Files

Settings can be stored in `.bmfc` files (INI-like format) and loaded with `--config`. Use `--save-config` to export your current settings.

### Sample .bmfc File

```ini
# mygame-font.bmfc

[font]
path = fonts/Roboto-Regular.ttf
face-index = 0

[rendering]
size = 32
dpi = 72
anti-alias = grayscale
bold = false
italic = false
super-sample = 2
hinting = true

[characters]
preset = latin
# chars = ABCabc
# chars-file = my-chars.txt
# ranges = 0020-007E, 00A0-00FF

[atlas]
max-texture-size = 1024
padding = 2
spacing = 1
power-of-two = true
packer = maxrects
texture-format = png
autofit = false

[effects]
outline = 2
gradient-top = FFFFFF
gradient-bottom = 888888
shadow-offset-x = 1
shadow-offset-y = 1
shadow-color = 000000
shadow-blur = 1
hard-shadow = false

[kerning]
enabled = true

[variable]
instance = Bold
# wght = 700
# wdth = 90

[output]
format = text
path = output/roboto
```

### Using Config Files

```
# Generate from a config file
kernsmith generate --config mygame-font.bmfc

# Config + CLI overrides (CLI flags take precedence)
kernsmith generate --config mygame-font.bmfc -s 48 --format xml

# Save current settings to a config file
kernsmith generate -f font.ttf -s 32 --outline 2 --save-config my-settings.bmfc

# Load, tweak, and re-save
kernsmith generate --config old.bmfc -s 64 --save-config updated.bmfc
```

Lines starting with `#` are comments. Boolean values accept `true`/`false`, `1`/`0`, or `yes`/`no`. Paths in the config file are resolved relative to the config file's directory.

## Output Formats

### .fnt Descriptor Formats

| Format | Flag | Description |
|--------|------|-------------|
| Text | `--format text` | BMFont text format (default). Human-readable, widely supported. |
| XML | `--format xml` | BMFont XML format. Easy to parse in any language. |
| Binary | `--format binary` | BMFont binary format. Compact, fast to load. |

### Texture Formats

| Format | Flag | Description |
|--------|------|-------------|
| PNG | `--texture-format png` | Default. Lossless, widely supported. |
| TGA | `--texture-format tga` | Uncompressed. Common in older game engines. |
| DDS | `--texture-format dds` | DirectX texture format. GPU-ready. |

## Advanced Features

### Super Sampling

Render glyphs at a higher resolution and downsample for smoother edges. Levels 1-4, where 1 is no super sampling.

```
kernsmith generate -f font.ttf -s 32 --super-sample 2
kernsmith generate -f font.ttf -s 16 --super-sample 4
```

### Signed Distance Fields (SDF)

Generate SDF textures for resolution-independent text rendering. Commonly used with custom shaders in game engines.

```
kernsmith generate -f font.ttf -s 32 --sdf --padding 4
```

SDF fonts typically need extra padding to store the distance field. A padding of 4-8 pixels is recommended.

### Variable Fonts

Set variation axes directly or use named instances. Use `kernsmith info` to discover available axes and instances.

```
# Check what axes are available
kernsmith info RobotoFlex.ttf

# Set specific axis values
kernsmith generate -f RobotoFlex.ttf -s 32 --axis wght=700 --axis wdth=90

# Use a named instance
kernsmith generate -f RobotoFlex.ttf -s 32 --instance "Bold"
```

### Channel Packing

Pack up to 4 monochrome glyphs into the R, G, B, and A channels of a single texture pixel. Reduces texture memory by 4x for monochrome fonts.

```
kernsmith generate -f font.ttf -s 32 --channel-pack
```

### Autofit Texture

Automatically find the smallest texture size that fits all glyphs, instead of using the fixed maximum.

```
kernsmith generate -f font.ttf -s 32 --autofit
```

### Font Subsetting

Include only the characters you need to minimize texture size.

```
# Only digits and basic punctuation
kernsmith generate -f font.ttf -s 32 --chars "0123456789.,:-"

# Characters from a file (one per line or all on one line)
kernsmith generate -f font.ttf -s 32 --chars-file game-strings.txt

# Specific Unicode range (CJK Unified Ideographs subset)
kernsmith generate -f font.ttf -s 24 --range 4E00-4FFF
```

### Color Fonts

Render color glyphs from fonts with COLR/CPAL tables (e.g., emoji fonts). Select a specific color palette if the font provides multiple.

```
kernsmith generate -f NotoColorEmoji.ttf -s 32 --color-font
kernsmith generate -f NotoColorEmoji.ttf -s 32 --color-font --color-palette 1
```

### Dry Run

Preview what would be generated without writing any files.

```
kernsmith generate -f font.ttf -s 32 --outline 2 -c latin --dry-run
```

Output shows font source, size, character count, effects, and output path.

## Global Options

These flags work with any command:

| Flag | Description |
|------|-------------|
| `--help, -h` | Show help |
| `--version` | Show version |
| `--no-color` | Disable colored output |
| `-v, --verbose` | Show detailed progress |
| `-q, --quiet` | Suppress all output except errors |
