# CLI Tool

`kernsmith` is a cross-platform command-line tool for generating BMFont-compatible bitmap fonts from TTF, OTF, WOFF, and WOFF2 files. It produces `.fnt` + `.png` pairs ready for use in game engines and rendering frameworks.

## Installation

The CLI is located in `tools/KernSmith.Cli/` and can be run with:

```bash
dotnet run --project tools/KernSmith.Cli -- <command> [options]
```

## Quick Examples

```bash
# Basic generation with ASCII charset
kernsmith generate -f MyFont.ttf -s 32

# Bold italic at 48px, XML output
kernsmith generate -f MyFont.ttf -s 48 -b -i --format xml

# Extended Latin with 2px outline
kernsmith generate -f MyFont.ttf -s 24 -c latin --outline 2

# System font
kernsmith generate --system-font "Arial" -s 32
```

## Commands

| Command | Description |
|---------|-------------|
| [generate](commands.md#generate) | Generate BMFont files from a font (main command) |
| [init](commands.md#init) | Scaffold a `.bmfc` or `.hiero` config file without rendering |
| [batch](commands.md#batch) | Process multiple `.bmfc` / `.hiero` files in one invocation |
| [benchmark](commands.md#benchmark) | Benchmark generation performance |
| [benchmark-fonts](commands.md#benchmark-fonts) | Benchmark font resolution (`LoadFont`) across installed families |
| [inspect](commands.md#inspect) | Inspect an existing `.fnt` file |
| [convert](commands.md#convert) | Convert between `.fnt` formats (text, XML, binary) |
| [list-fonts](commands.md#list-fonts) | List system-installed fonts |
| [list-rasterizers](commands.md#list-rasterizers) | List available rasterizer backends |
| [info](commands.md#info) | Show metadata from a font file |

See the [Command Reference](commands.md) for full details on every command and flag.

## Configuration Files

Settings can be stored in BMFont `.bmfc` files or Hiero `.hiero` (libGDX) files and loaded with `--config`. When loading, the format is auto-detected by inspecting the file content (the extension is used only as a fallback when the content is inconclusive). Use the `init` command or `--save-config` to scaffold a config, then edit by hand. `.bmfc` boolean keys accept `1`/`0`, `true`/`false` or `yes`/`no` in any case; KernSmith writes `1`/`0`.

```bash
# Scaffold a .bmfc config
kernsmith init --system-font "Arial" -s 32 -o my-font.bmfc

# Or scaffold a .hiero (libGDX) config
kernsmith init --system-font "Arial" -s 32 -o my-font.hiero

# Generate from it (.bmfc or .hiero)
kernsmith generate --config my-font.hiero

# Override individual settings
kernsmith generate --config my-font.bmfc -s 48 --format xml
```

## Global Options

Only `--no-color` is truly global -- it is stripped before command dispatch, so it works alongside any command. The rest are scoped:

| Flag | Scope | Description |
|------|-------|-------------|
| `--no-color` | Any command | Disable colored output |
| `--help, -h` | Alone (`kernsmith --help`), or `kernsmith <command> --help` | Show help |
| `--version` | Alone (`kernsmith --version`) | Show version |
| `-v, --verbose` | Parsed per command | Show detailed progress |
| `-q, --quiet` | Parsed per command | Suppress all output except errors |
