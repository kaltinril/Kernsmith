# CLI Tool

`kernsmith` is a cross-platform command-line tool for generating BMFont-compatible bitmap fonts from TTF, OTF, and WOFF files. It produces `.fnt` + `.png` pairs ready for use in game engines and rendering frameworks.

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
| [init](commands.md#init) | Scaffold a `.bmfc` config file without rendering |
| [batch](commands.md#batch) | Process multiple `.bmfc` files in one invocation |
| [benchmark](commands.md#benchmark) | Benchmark generation performance |
| [inspect](commands.md#inspect) | Inspect an existing `.fnt` file |
| [convert](commands.md#convert) | Convert between `.fnt` formats (text, XML, binary) |
| [list-fonts](commands.md#list-fonts) | List system-installed fonts |
| [info](commands.md#info) | Show metadata from a font file |

See the [Command Reference](commands.md) for full details on every command and flag.

## Configuration Files

Settings can be stored in `.bmfc` files and loaded with `--config`. Use the `init` command or `--save-config` to scaffold a config, then edit by hand.

```bash
# Scaffold a config
kernsmith init --system-font "Arial" -s 32 -o my-font.bmfc

# Generate from it
kernsmith generate --config my-font.bmfc

# Override individual settings
kernsmith generate --config my-font.bmfc -s 48 --format xml
```

## Global Options

| Flag | Description |
|------|-------------|
| `--help, -h` | Show help |
| `--version` | Show version |
| `--no-color` | Disable colored output |
| `-v, --verbose` | Show detailed progress |
| `-q, --quiet` | Suppress all output except errors |
