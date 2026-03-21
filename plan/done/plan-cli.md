# KernSmith CLI -- Full-Featured BMFont Generator

> Plan for a production-ready CLI tool that replaces AngelCode's BMFont.exe.
> This is a separate project (`KernSmith.Cli`) distributed as a .NET global tool.

---

## Why

The original BMFont.exe is:
- Windows-only (Win32 GUI application)
- No programmatic API -- must be wrapped with automation hacks
- No CLI mode -- cannot be used in pipelines
- Unmaintained (last update ~2014)
- Limited to the features it shipped with

KernSmith CLI is:
- Cross-platform (Windows, macOS, Linux)
- Fully scriptable -- designed for CI/CD pipelines and build automation
- Configuration file support -- save and reuse settings
- Superset of BMFont.exe features -- plus SDF, channel packing, gradients, variable fonts, WOFF
- Open source (MIT)

---

## Installation

```bash
# Install as a .NET global tool
dotnet tool install -g KernSmith.Cli

# Or run without installing
dotnet tool run KernSmith -- generate -f font.ttf -s 32
```

---

## Commands

### `KernSmith generate`

Generate BMFont files from a font.

```
KernSmith generate [options]

Font Source (one required):
  -f, --font <path>           Font file path (TTF, OTF, WOFF)
  --system-font <name>        Use a system-installed font by family name

Output:
  -o, --output <path>         Output file path (default: ./<fontname>)
  --format <text|xml|binary>  Output format (default: text)

Size & Rendering:
  -s, --size <n>              Font size in pixels (required)
  --dpi <n>                   DPI (default: 72)
  --aa <none|grayscale|light|lcd>  Anti-aliasing mode (default: grayscale)
  --sdf                       Enable Signed Distance Field rendering
  --mono                      Disable anti-aliasing (alias for --aa none)

Style:
  -b, --bold                  Enable synthetic bold
  -i, --italic                Enable synthetic italic
  -u, --underline             (reserved for future use)

Character Set:
  -c, --charset <preset>      Character set preset: ascii, extended, latin (default: ascii)
  --chars <string>            Explicit characters to include (e.g., "ABCabc123")
  --chars-file <path>         Read characters from a text file (UTF-8)
  --range <start-end>         Unicode range (hex), repeatable (e.g., --range 0020-007E --range 0400-04FF)

Texture Atlas:
  --max-texture <n>           Maximum texture size in pixels (default: 1024)
  --padding <n>               Padding around each glyph in pixels (default: 0)
  --padding <u,r,d,l>         Per-side padding (up,right,down,left)
  --spacing <n>               Spacing between glyphs in pixels (default: 1)
  --spacing <h,v>             Horizontal,vertical spacing
  --pot                       Force power-of-two texture dimensions (default: on)
  --no-pot                    Allow non-power-of-two textures
  --packer <maxrects|skyline> Packing algorithm (default: maxrects)
  --channel-pack              Pack glyphs into individual RGBA channels (4x density)

Effects:
  --outline <n>               Outline width in pixels
  --gradient <top> <bottom>   Vertical gradient, colors as hex (e.g., --gradient FF0000 0000FF)

Kerning:
  --no-kerning                Disable kerning pair extraction
  --kerning                   Enable kerning (default: on)

Variable Fonts:
  --axis <tag>=<value>        Set variation axis (repeatable, e.g., --axis wght=700 --axis wdth=75)
  --instance <name>           Use a named instance (e.g., --instance "Bold")

Font Collections (.ttc):
  --face <n>                  Face index for .ttc collections (default: 0)

Configuration:
  --config <path>             Load settings from a .bmfc configuration file
  --save-config <path>        Save current settings to a .bmfc file
  --dry-run                   Show what would be generated without writing files

Verbosity:
  -v, --verbose               Show detailed progress
  -q, --quiet                 Suppress all output except errors
```

#### Examples

```bash
# Basic ASCII font
KernSmith generate -f arial.ttf -s 32

# Bold italic with outline
KernSmith generate -f roboto.ttf -s 48 -b -i --outline 2

# SDF font for runtime scaling
KernSmith generate -f opensans.ttf -s 64 --sdf --padding 8

# Gradient gold text
KernSmith generate -f impact.ttf -s 72 --gradient FFD700 DC143C

# Variable font with specific weight
KernSmith generate -f "RobotoFlex.ttf" -s 32 --axis wght=700

# System font, XML output, Cyrillic charset
KernSmith generate --system-font "Arial" -s 24 --format xml --range 0020-007E --range 0400-04FF

# Channel-packed atlas for maximum density
KernSmith generate -f font.ttf -s 16 --channel-pack --max-texture 512

# From a config file
KernSmith generate --config mygame-fonts.bmfc

# Save settings for reuse
KernSmith generate -f font.ttf -s 32 --outline 2 --save-config mypreset.bmfc
```

### `KernSmith inspect`

Inspect an existing BMFont file.

```
KernSmith inspect <path>

  <path>                      Path to a .fnt file (text, XML, or binary)

Output:
  --json                      Output as JSON instead of human-readable table
```

Shows: font name, size, character count, kerning pair count, page count, texture dimensions, format, character set ranges.

#### Implementation Notes

Uses `BmFontReader.Read(byte[])` which auto-detects format (text, XML, binary). The reader
returns a `BmFontModel` containing:

- `Info` block: face name, size, bold/italic flags, padding, spacing, anti-alias level
- `Common` block: line height, base, texture dimensions (ScaleW/ScaleH), page count, channel info
- `Pages` list: page id and filename pairs
- `Characters` list: per-character metrics (id, x, y, width, height, xoffset, yoffset, xadvance, page, channel)
- `KerningPairs` list: first/second/amount triplets

The `inspect` command should compute and display:

| Field | Source |
|-------|--------|
| Font face | `model.Info.Face` |
| Font size | `model.Info.Size` |
| Bold / Italic | `model.Info.Bold`, `model.Info.Italic` |
| Character count | `model.Characters.Count` |
| Kerning pair count | `model.KerningPairs.Count` |
| Page count | `model.Pages.Count` |
| Texture dimensions | `model.Common.ScaleW` x `model.Common.ScaleH` |
| Line height | `model.Common.LineHeight` |
| Base | `model.Common.Base` |
| Packed | `model.Common.Packed` |
| Channel config | alphaChnl, redChnl, greenChnl, blueChnl values |
| Unicode ranges | Derived by grouping sorted character IDs into contiguous runs |
| Detected format | Text / XML / Binary (from the header detection logic) |

#### Human-readable output example

```
Font:        Roboto Regular
Size:        32px
Style:       Regular
Characters:  95
Kerning:     1247 pairs
Pages:       1
Texture:     512 x 512
Line height: 38
Base:        30
Format:      Text

Unicode ranges:
  U+0020..U+007E  (95 chars, Basic Latin)
```

#### JSON output example (`--json`)

```json
{
  "face": "Roboto Regular",
  "size": 32,
  "bold": false,
  "italic": false,
  "characterCount": 95,
  "kerningPairCount": 1247,
  "pageCount": 1,
  "textureWidth": 512,
  "textureHeight": 512,
  "lineHeight": 38,
  "base": 30,
  "packed": false,
  "pages": [
    { "id": 0, "file": "roboto_0.png" }
  ],
  "unicodeRanges": [
    { "start": "0020", "end": "007E", "count": 95 }
  ]
}
```

### `KernSmith convert`

Convert between BMFont formats.

```
KernSmith convert <input> -o <output> [--format <text|xml|binary>]
```

#### Implementation Notes

1. Read input via `BmFontReader.Read(byte[])` (auto-detects format)
2. Determine output format from `--format` flag, or infer from output file extension (`.fnt` = text, `.xml` = xml)
3. Write using `TextFormatter`, `XmlFormatter`, or `BmFontBinaryFormatter`
4. Copy associated atlas page PNG files from the input directory to the output directory (preserving filenames)

### `KernSmith list-fonts`

List system-installed fonts.

```
KernSmith list-fonts [--filter <pattern>]

  --filter <pattern>          Filter by family name (case-insensitive substring match)
  --json                      Output as JSON
```

#### Implementation Notes

Uses `DefaultSystemFontProvider.GetInstalledFonts()` which returns `IReadOnlyList<SystemFontInfo>`.
Each entry has `FamilyName` and `StyleName` properties.

Output is grouped by family, sorted alphabetically. With `--filter`, only families containing the
substring (case-insensitive) are shown.

#### Human-readable output example

```
Found 342 font faces:

  Arial
    Regular, Bold, Italic, Bold Italic
  Consolas
    Regular, Bold, Italic, Bold Italic
  Roboto
    Thin, Light, Regular, Medium, Bold, Black
  ...
```

#### JSON output example (`--json`)

```json
[
  {
    "family": "Arial",
    "styles": ["Regular", "Bold", "Italic", "Bold Italic"]
  },
  ...
]
```

### `KernSmith info`

Show font file metadata (without generating).

```
KernSmith info <path>

  <path>                      Path to a font file (TTF, OTF, WOFF)
  --json                      Output as JSON
```

Shows: family name, style, units per em, glyph count, kerning pair count, variation axes, supported Unicode ranges.

#### Implementation Notes

Uses `TtfFontReader.ReadFont(byte[], int)` which returns a `FontInfo` containing parsed font
metadata. The font file is loaded but no rasterization or atlas packing occurs. WOFF files are
auto-decompressed via `WoffDecompressor` before parsing.

#### Human-readable output example

```
File:        RobotoFlex-VariableFont.ttf
Family:      Roboto Flex
Style:       Regular
Format:      TrueType (TTF)
Glyphs:      897
Kerning:     2341 pairs
Units/Em:    2048

Variation axes:
  wght  Weight     100..900  (default: 400)
  wdth  Width       75..125  (default: 100)
  opsz  Optical Size 8..144  (default: 14)

Unicode coverage:
  U+0020..U+007E  Basic Latin
  U+00A0..U+00FF  Latin-1 Supplement
  U+0100..U+017F  Latin Extended-A
  ...
```

### `KernSmith --version`

Print version and exit.

### `KernSmith --help`

Print usage and exit.

---

## Configuration File (.bmfc)

The `.bmfc` file is a simple key=value format (INI-like) that stores all generation settings. This allows teams to check font configs into source control.

```ini
# KernSmith configuration file
# Generated by: KernSmith generate --save-config

[font]
path = fonts/Roboto-Regular.ttf
# system-font = Arial           # alternative: use system font
face-index = 0

[rendering]
size = 32
dpi = 72
anti-alias = grayscale
# sdf = true
bold = false
italic = false

[characters]
# preset: ascii | extended | latin
preset = ascii
# chars = ABCDEFGabcdefg0123456789
# chars-file = charsets/japanese.txt
# ranges = 0020-007E, 0400-04FF

[atlas]
max-texture-size = 1024
padding = 0
spacing = 1
power-of-two = true
packer = maxrects
# channel-pack = true

[effects]
outline = 0
# gradient-top = FFD700
# gradient-bottom = DC143C

[kerning]
enabled = true

[variable]
# axes: tag = value
# wght = 700
# wdth = 75

[output]
format = text
# path = output/myfont
```

### Config File Behavior

- `--config` loads settings FIRST, then CLI flags override individual settings
- `--save-config` writes the final merged settings (CLI + defaults) to the file
- Relative paths in the config file are resolved relative to the config file's directory
- Comments start with `#`
- Empty lines are ignored
- Unknown keys produce a warning but don't fail

### Config-to-Options Mapping

Each config section maps directly to `FontGeneratorOptions` and the CLI's own options model:

| Config Key | `FontGeneratorOptions` Property | CLI Flag |
|------------|-------------------------------|----------|
| `[font] path` | *(CLI-level: font file path)* | `-f, --font` |
| `[font] system-font` | *(CLI-level: system font name)* | `--system-font` |
| `[font] face-index` | `FaceIndex` | `--face` |
| `[rendering] size` | `Size` | `-s, --size` |
| `[rendering] dpi` | `Dpi` | `--dpi` |
| `[rendering] anti-alias` | `AntiAlias` (enum: None, Grayscale, Light, Lcd) | `--aa` |
| `[rendering] sdf` | `Sdf` | `--sdf` |
| `[rendering] bold` | `Bold` | `-b, --bold` |
| `[rendering] italic` | `Italic` | `-i, --italic` |
| `[characters] preset` | `Characters` (via `CharacterSet.Ascii/ExtendedAscii/Latin`) | `-c, --charset` |
| `[characters] chars` | `Characters` (via `CharacterSet.FromChars(string)`) | `--chars` |
| `[characters] chars-file` | `Characters` (via `CharacterSet.FromChars(File.ReadAllText(...))`) | `--chars-file` |
| `[characters] ranges` | `Characters` (via `CharacterSet.FromRanges(...)`) | `--range` |
| `[atlas] max-texture-size` | `MaxTextureSize` | `--max-texture` |
| `[atlas] padding` | `Padding` (single int or `u,r,d,l`) | `--padding` |
| `[atlas] spacing` | `Spacing` (single int or `h,v`) | `--spacing` |
| `[atlas] power-of-two` | `PowerOfTwo` | `--pot / --no-pot` |
| `[atlas] packer` | `PackingAlgorithm` (enum: MaxRects, Skyline) | `--packer` |
| `[atlas] channel-pack` | `ChannelPacking` | `--channel-pack` |
| `[effects] outline` | `Outline` | `--outline` |
| `[effects] gradient-top` | *(post-processor: GradientPostProcessor top color)* | `--gradient` arg 1 |
| `[effects] gradient-bottom` | *(post-processor: GradientPostProcessor bottom color)* | `--gradient` arg 2 |
| `[kerning] enabled` | `Kerning` | `--kerning / --no-kerning` |
| `[variable] <tag>` | `VariationAxes[tag]` | `--axis <tag>=<value>` |
| `[output] format` | *(CLI-level: output format)* | `--format` |
| `[output] path` | *(CLI-level: output path)* | `-o, --output` |

### Character Set Merging

When multiple character source flags are provided, they are unioned together:

```bash
# This includes the ascii preset PLUS the explicit characters PLUS the Unicode range
KernSmith generate -f font.ttf -s 32 -c ascii --chars "@#$" --range 0400-04FF
```

The implementation uses `CharacterSet.Union(...)` to merge all sources.

---

## Project Structure

```
cli/
  KernSmith.Cli/
    KernSmith.Cli.csproj        # .NET global tool package
    Program.cs                   # Entry point, command routing
    Commands/
      GenerateCommand.cs         # The main generate command
      InspectCommand.cs          # BMFont file inspector
      ConvertCommand.cs          # Format converter
      ListFontsCommand.cs        # System font lister
      InfoCommand.cs             # Font file metadata viewer
    Config/
      BmfcParser.cs              # .bmfc config file parser
      BmfcWriter.cs              # .bmfc config file writer
      CliOptions.cs              # Parsed CLI options model
    Utilities/
      ArgParser.cs               # Argument parser (no external deps)
      ColorParser.cs             # Hex color string parser
      ConsoleOutput.cs           # Formatted console output helpers
```

### File Responsibilities

#### `Program.cs`

Entry point. Parses the top-level command name (`generate`, `inspect`, `convert`, `list-fonts`,
`info`) and delegates to the appropriate command class. Handles `--help` and `--version` at the
top level. Returns the exit code from the command.

```
args[0] -> command name -> CommandClass.Execute(args[1..]) -> exit code
```

#### `Commands/GenerateCommand.cs`

The core command. Responsibilities:

1. Parse all CLI flags into a `CliOptions` instance via `ArgParser`
2. If `--config` is specified, load the `.bmfc` file via `BmfcParser`, then overlay CLI flags
3. Validate required fields (font source, size)
4. Build `FontGeneratorOptions` from the merged `CliOptions`
5. Wire up post-processors (outline via `OutlinePostProcessor`, gradient via `GradientPostProcessor`)
6. Call `BmFont.Generate()` or `BmFont.GenerateFromSystem()`
7. Call `result.ToFile()` to write output
8. If `--save-config`, write the merged options via `BmfcWriter`
9. If `--dry-run`, print what would be generated and exit before step 6

#### `Commands/InspectCommand.cs`

Loads a `.fnt` file via `BmFontReader.Read()`, formats the model data as a human-readable table
or JSON, and prints to stdout.

#### `Commands/ConvertCommand.cs`

Loads a `.fnt` file via `BmFontReader.Read()`, re-serializes in the target format, writes to
the output path. Copies atlas PNGs if the output directory differs from the input directory.

#### `Commands/ListFontsCommand.cs`

Instantiates `DefaultSystemFontProvider`, calls `GetInstalledFonts()`, groups by family name,
applies optional filter, and prints the result.

#### `Commands/InfoCommand.cs`

Reads raw font bytes, auto-decompresses WOFF if needed, parses via `TtfFontReader.ReadFont()`,
and displays font metadata. No rasterization or atlas generation occurs.

#### `Config/CliOptions.cs`

A plain data class that holds all parsed settings in a format-agnostic way (not tied to
`FontGeneratorOptions`). This is the intermediate representation between CLI args, config files,
and the library options.

```csharp
public class CliOptions
{
    // Font source (mutually exclusive)
    public string? FontPath { get; set; }
    public string? SystemFontName { get; set; }
    public int FaceIndex { get; set; }

    // Rendering
    public int? Size { get; set; }
    public int Dpi { get; set; } = 72;
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;
    public bool Sdf { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    // Characters
    public string? CharsetPreset { get; set; } = "ascii";
    public string? ExplicitChars { get; set; }
    public string? CharsFilePath { get; set; }
    public List<(int Start, int End)> UnicodeRanges { get; set; } = new();

    // Atlas
    public int MaxTextureSize { get; set; } = 1024;
    public Padding Padding { get; set; } = new(0);
    public Spacing Spacing { get; set; } = new(1);
    public bool PowerOfTwo { get; set; } = true;
    public PackingAlgorithm PackingAlgorithm { get; set; } = PackingAlgorithm.MaxRects;
    public bool ChannelPacking { get; set; }

    // Effects
    public int Outline { get; set; }
    public string? GradientTop { get; set; }     // hex color string
    public string? GradientBottom { get; set; }   // hex color string

    // Kerning
    public bool Kerning { get; set; } = true;

    // Variable fonts
    public Dictionary<string, float> VariationAxes { get; set; } = new();
    public string? InstanceName { get; set; }

    // Output
    public string? OutputPath { get; set; }
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    // Meta
    public string? ConfigPath { get; set; }
    public string? SaveConfigPath { get; set; }
    public bool DryRun { get; set; }
    public bool Verbose { get; set; }
    public bool Quiet { get; set; }
}
```

#### `Config/BmfcParser.cs`

Reads a `.bmfc` INI file into a `CliOptions` instance.

Parsing rules:
- Lines starting with `#` are comments (ignored)
- Empty/whitespace-only lines are ignored
- `[section]` lines set the current section context
- `key = value` lines set the property identified by `section.key`
- Leading/trailing whitespace around keys and values is trimmed
- Inline comments after `#` are supported: `key = value  # comment`
- Unknown keys log a warning to stderr but do not cause failure

#### `Config/BmfcWriter.cs`

Serializes a `CliOptions` instance to `.bmfc` format. Writes a header comment with the
generation command, then each section with its key-value pairs. Disabled/default values are
written as comments for discoverability.

#### `Utilities/ArgParser.cs`

Zero-dependency argument parser. Handles:
- Short flags (`-f`, `-s`, `-b`, `-i`, `-v`, `-q`)
- Long flags (`--font`, `--size`, `--bold`, `--sdf`)
- Value arguments (`-f value`, `--font value`, `--font=value`)
- Boolean flags (presence = true)
- Repeatable arguments (`--range` can appear multiple times)
- Compound key=value (`--axis wght=700`)
- Multi-value arguments (`--gradient FF0000 0000FF` consumes two values)
- `--` to stop flag parsing

Returns a structured parse result that commands can query by flag name.

#### `Utilities/ColorParser.cs`

Parses hex color strings (3, 6, or 8 character formats) into RGB byte tuples.

```
"FF0000"   -> (255, 0, 0)
"F00"      -> (255, 0, 0)
"#FF0000"  -> (255, 0, 0)    (leading # is stripped)
"FF0000FF" -> (255, 0, 0)    (alpha channel ignored for gradient use)
```

#### `Utilities/ConsoleOutput.cs`

Helpers for formatted console output:
- `WriteTable(headers, rows)` -- aligned column output
- `WriteError(message)` -- writes to stderr in red (if terminal supports color)
- `WriteWarning(message)` -- writes to stderr in yellow
- `WriteSuccess(message)` -- writes to stdout in green
- `WriteProgress(message)` -- writes to stdout, respects quiet mode
- `WriteVerbose(message)` -- writes only if verbose mode is on

Respects the `NO_COLOR` environment variable (see https://no-color.org/).

---

## NuGet Tool Package

The CLI is distributed as a .NET global tool:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>KernSmith</ToolCommandName>
    <PackageId>KernSmith.Cli</PackageId>
    <Version>0.1.0</Version>
    <Description>Cross-platform BMFont generator CLI -- replacement for BMFont.exe</Description>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
    <PackageTags>bmfont;bitmap-font;cli;font-generator;game-dev</PackageTags>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="KernSmith" Version="0.1.0" />
  </ItemGroup>
</Project>
```

### Publishing Workflow

```bash
# Build the tool package
dotnet pack cli/KernSmith.Cli -c Release

# Test locally
dotnet tool install -g --add-source ./cli/KernSmith.Cli/nupkg KernSmith.Cli

# Publish to NuGet
dotnet nuget push cli/KernSmith.Cli/nupkg/KernSmith.Cli.0.1.0.nupkg -k <api-key> -s https://api.nuget.org/v3/index.json
```

---

## Comparison: KernSmith CLI vs BMFont.exe

| Feature | BMFont.exe | KernSmith CLI |
|---------|-----------|---------------|
| Platform | Windows only | Windows, macOS, Linux |
| Interface | GUI only | CLI (scriptable) |
| Configuration | Binary .bmfc format | Human-readable .bmfc (INI) |
| Output formats | Text, XML, Binary | Text, XML, Binary |
| Input formats | System fonts, TTF | TTF, OTF, WOFF, system fonts, .ttc |
| SDF rendering | No | Yes |
| Channel packing | Yes | Yes |
| Outline | Yes | Yes |
| Gradient | No | Yes |
| Variable fonts | No | Yes |
| Kerning sources | kern table only | kern + GPOS (modern fonts) |
| CI/CD friendly | No (GUI) | Yes (CLI + config files) |
| Font inspection | No | Yes (`inspect`, `info` commands) |
| Format conversion | No | Yes (`convert` command) |
| License | Freeware | MIT open source |

---

## Implementation Phases

### Phase A -- Core CLI (replace current samples/KernSmith.Cli)

| Task | Description |
|------|-------------|
| A1 | Set up `cli/KernSmith.Cli` project with .NET global tool config |
| A2 | Implement `ArgParser` -- no external deps, handles flags, values, repeatable args |
| A3 | Implement `GenerateCommand` with all rendering, charset, atlas, and effect options |
| A4 | Implement `ColorParser` for hex color strings |
| A5 | Implement `ConsoleOutput` -- progress, tables, errors, verbose/quiet modes |

#### A1: Project Setup

Create `cli/KernSmith.Cli/KernSmith.Cli.csproj` with the NuGet tool configuration shown above.
Add a project reference to `../../src/KernSmith/KernSmith.csproj` for local development (the
NuGet package reference is for the published version).

Create `Program.cs` with top-level command routing:

```csharp
return args switch
{
    [] or ["--help"] => ShowHelp(),
    ["--version"] => ShowVersion(),
    ["generate", ..var rest] => new GenerateCommand().Execute(rest),
    ["inspect", ..var rest] => new InspectCommand().Execute(rest),
    ["convert", ..var rest] => new ConvertCommand().Execute(rest),
    ["list-fonts", ..var rest] => new ListFontsCommand().Execute(rest),
    ["info", ..var rest] => new InfoCommand().Execute(rest),
    _ => UnknownCommand(args[0])
};
```

#### A2: ArgParser Design

The parser should return a result object supporting:

```csharp
var result = ArgParser.Parse(args, spec);
result.HasFlag("sdf");                    // bool
result.GetValue("font");                  // string?
result.GetValue("size", int.Parse);       // int? (with converter)
result.GetValues("range");               // List<string>
result.GetPositional(0);                  // string? (positional arg)
```

The spec defines each argument:

```csharp
new ArgSpec()
    .Flag("sdf", "Enable SDF rendering")
    .Option("f", "font", "Font file path", required: true)
    .Option("s", "size", "Font size", required: true)
    .Repeatable("range", "Unicode range")
    .MultiValue("gradient", 2, "Gradient top and bottom colors")
    .Positional("path", "Input file path");
```

#### A3: GenerateCommand Flow

```
1. Parse args via ArgParser
2. If --config: load .bmfc -> CliOptions
3. Overlay CLI args onto CliOptions (CLI wins)
4. Validate: must have font source + size
5. If --dry-run: print summary and exit 0

6. Build CharacterSet:
   a. Start with preset (ascii/extended/latin) if specified
   b. Union with --chars (CharacterSet.FromChars)
   c. Union with --chars-file (CharacterSet.FromChars(File.ReadAllText))
   d. Union with --range entries (CharacterSet.FromRanges)

7. Build FontGeneratorOptions from CliOptions
8. Build post-processor list:
   a. If outline > 0: add OutlinePostProcessor(outline)
   b. If gradient specified: add GradientPostProcessor.Create(top, bottom)

9. Call BmFont.Generate() or BmFont.GenerateFromSystem()
10. Call result.ToFile(outputPath, format)
11. If --save-config: write .bmfc via BmfcWriter

12. Print summary (unless --quiet):
    "Generated myfont.fnt (95 chars, 1247 kern pairs, 1 page 512x512)"
```

#### A4: ColorParser

Simple utility, no edge cases beyond the formats listed. Should throw a clear
`ArgumentException` on invalid input with a message like:

```
Invalid color 'GGHHII': expected a hex color (e.g., FF0000, F00, #FF0000)
```

#### A5: ConsoleOutput

Key design decisions:
- Use `Console.Error` for errors and warnings, `Console.Out` for data
- Detect `NO_COLOR` env var and `Console.IsOutputRedirected` to decide on ANSI colors
- All methods take a `CliOptions` reference to check verbose/quiet flags

### Phase B -- Config Files

| Task | Description |
|------|-------------|
| B1 | Implement `BmfcParser` -- read .bmfc INI files into `CliOptions` |
| B2 | Implement `BmfcWriter` -- serialize `CliOptions` to .bmfc format |
| B3 | Wire `--config` and `--save-config` into GenerateCommand |
| B4 | Implement `--dry-run` mode |

#### B1: BmfcParser

State machine: track current section name, parse key=value lines, map to `CliOptions` properties
using the config-to-options mapping table above. Handle inline comments.

Path resolution: when `[font] path` is a relative path, resolve it relative to the directory
containing the `.bmfc` file, not the current working directory.

```csharp
public static CliOptions Parse(string filePath)
{
    var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
    // ... parse lines ...
    // When resolving [font] path:
    options.FontPath = Path.GetFullPath(Path.Combine(dir, rawPath));
}
```

#### B2: BmfcWriter

Write all sections in order. For values that match the default, write them as comments
so the user can see what's available. Always write the generation timestamp and command
as a header comment.

#### B4: Dry-Run

When `--dry-run` is active, print a summary of what would happen:

```
[dry-run] Font:      roboto.ttf
[dry-run] Size:      32px
[dry-run] Charset:   ascii (95 codepoints)
[dry-run] Atlas:     max 1024x1024, maxrects packer
[dry-run] Effects:   outline 2px
[dry-run] Output:    ./roboto.fnt (text format)
[dry-run] No files written.
```

### Phase C -- Utility Commands

| Task | Description |
|------|-------------|
| C1 | Implement `InspectCommand` -- load .fnt, display stats table |
| C2 | Implement `ConvertCommand` -- read one format, write another |
| C3 | Implement `ListFontsCommand` -- enumerate and filter system fonts |
| C4 | Implement `InfoCommand` -- parse font file, display metadata |

#### C1: InspectCommand

Accept one positional argument (the `.fnt` path). Load via `BmFontReader.Read(File.ReadAllBytes(path))`.
Format output as described in the `inspect` command section above.

For JSON output, use `System.Text.Json.JsonSerializer` (part of the BCL, not an external dep).

#### C2: ConvertCommand

Accept one positional argument (input path), require `-o` for output. Detect input format
automatically. If `--format` is not specified, infer from output extension:
- `.fnt` -> text
- `.xml` -> xml
- `.bin` -> binary
- Otherwise -> text

Also handle atlas page files:
- Parse page filenames from the model (`model.Pages[*].File`)
- Copy PNG files from the input directory to the output directory
- Update page filenames in the model if the output base name differs

#### C3: ListFontsCommand

Group fonts by family, show styles within each family. With `--filter`, apply
case-insensitive substring match on family name.

#### C4: InfoCommand

Load font file bytes. If WOFF, decompress via `WoffDecompressor`. Parse via `TtfFontReader`.
Display the metadata fields. This command does NOT generate any atlas or rasterize any glyphs.

### Phase D -- Polish + Publish

| Task | Description |
|------|-------------|
| D1 | Error handling -- clear messages for all failure modes |
| D2 | `--help` for each command with examples |
| D3 | NuGet tool packaging and publish workflow |
| D4 | Integration tests -- CLI invocation tests |

#### D1: Error Handling Strategy

All commands follow the same pattern:

```csharp
public int Execute(string[] args)
{
    try
    {
        // ... do work ...
        return ExitCodes.Success;
    }
    catch (FileNotFoundException ex)
    {
        ConsoleOutput.WriteError($"File not found: {ex.FileName}");
        return ExitCodes.FileNotFound;
    }
    catch (FontParsingException ex)
    {
        ConsoleOutput.WriteError($"Font error: {ex.Message}");
        return ExitCodes.FontParseError;
    }
    catch (FormatException ex)
    {
        ConsoleOutput.WriteError($"Format error: {ex.Message}");
        return ExitCodes.ConfigParseError;
    }
    // ... etc
}
```

Exception types from the library and their mapping to exit codes:

| Exception | Exit Code | Meaning |
|-----------|-----------|---------|
| `ArgumentException` (from arg parser) | 1 | Invalid arguments |
| `FileNotFoundException` | 2 | Font/config file not found |
| `FontParsingException` | 3 | Font file corrupt or unsupported |
| `InvalidOperationException` (from packer) | 4 | Glyphs don't fit in max texture |
| `IOException` | 5 | Cannot write output files |
| `FormatException` (from BmfcParser) | 10 | Config file syntax error |

#### D2: Per-Command Help

Each command supports `--help` which prints:
1. One-line description
2. Usage syntax
3. All options with descriptions
4. 2-3 examples

Example for `generate --help`:

```
Generate BMFont files from a font.

Usage: KernSmith generate -f <font> -s <size> [options]

Font Source (one required):
  -f, --font <path>           Font file path (TTF, OTF, WOFF)
  --system-font <name>        Use a system-installed font by family name
  ...

Examples:
  KernSmith generate -f arial.ttf -s 32
  KernSmith generate -f roboto.ttf -s 48 -b --outline 2 --format xml
  KernSmith generate --config mygame.bmfc --save-config updated.bmfc
```

#### D3: NuGet Packaging

The `.csproj` file contains all NuGet metadata. The CI workflow:

1. `dotnet build cli/KernSmith.Cli -c Release`
2. `dotnet pack cli/KernSmith.Cli -c Release -o ./nupkg`
3. `dotnet nuget push ./nupkg/KernSmith.Cli.*.nupkg -k $NUGET_API_KEY -s https://api.nuget.org/v3/index.json`

Version is managed in the `.csproj` file. Consider using `MinVer` or similar for automatic
versioning from git tags.

#### D4: Integration Tests

Test the CLI as a black box by invoking the built executable and checking:
- Exit codes
- Stdout/stderr content
- Generated output files

```csharp
[Fact]
public void Generate_BasicAscii_ProducesValidOutput()
{
    var result = RunCli("generate", "-f", TestFontPath, "-s", "32");

    Assert.Equal(0, result.ExitCode);
    Assert.True(File.Exists("testfont.fnt"));
    Assert.True(File.Exists("testfont_0.png"));

    // Verify the .fnt file is valid by loading it back
    var model = BmFontReader.Read(File.ReadAllBytes("testfont.fnt"));
    Assert.True(model.Characters.Count > 0);
}

[Fact]
public void Generate_MissingFont_ReturnsExitCode2()
{
    var result = RunCli("generate", "-f", "nonexistent.ttf", "-s", "32");
    Assert.Equal(2, result.ExitCode);
    Assert.Contains("not found", result.Stderr);
}

[Fact]
public void Inspect_TextFormat_ShowsCorrectStats()
{
    // First generate a font
    RunCli("generate", "-f", TestFontPath, "-s", "32");

    var result = RunCli("inspect", "testfont.fnt");
    Assert.Equal(0, result.ExitCode);
    Assert.Contains("Characters:", result.Stdout);
}

[Fact]
public void Convert_TextToXml_PreservesAllData()
{
    RunCli("generate", "-f", TestFontPath, "-s", "32", "--format", "text");
    RunCli("convert", "testfont.fnt", "-o", "testfont-xml.fnt", "--format", "xml");

    var original = BmFontReader.Read(File.ReadAllBytes("testfont.fnt"));
    var converted = BmFontReader.Read(File.ReadAllBytes("testfont-xml.fnt"));

    Assert.Equal(original.Characters.Count, converted.Characters.Count);
    Assert.Equal(original.KerningPairs.Count, converted.KerningPairs.Count);
}

[Fact]
public void Config_SaveAndLoad_RoundTrips()
{
    RunCli("generate", "-f", TestFontPath, "-s", "32", "--outline", "2",
           "--save-config", "test.bmfc");

    Assert.True(File.Exists("test.bmfc"));

    var result = RunCli("generate", "--config", "test.bmfc");
    Assert.Equal(0, result.ExitCode);
}
```

---

## Dependencies

- `KernSmith` NuGet package (the library) -- all font generation logic
- No other external dependencies -- arg parsing, config parsing, console output are all custom
- `System.Text.Json` (BCL, ships with .NET 8) -- for `--json` output in inspect/list-fonts/info
- .NET 8.0+

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Invalid arguments or usage error |
| 2 | Font file not found or unreadable |
| 3 | Font parsing error |
| 4 | Generation error (packing, rasterization) |
| 5 | Output write error |
| 10 | Config file parse error |

---

## Library API Surface Used

This section documents every `KernSmith` library API that the CLI depends on, so implementers
know exactly which types and methods to call.

### Font Generation

| API | Used By |
|-----|---------|
| `BmFont.Generate(string fontPath, FontGeneratorOptions?)` | GenerateCommand (file path input) |
| `BmFont.Generate(byte[] fontData, FontGeneratorOptions?)` | GenerateCommand (when pre-processing needed) |
| `BmFont.GenerateFromSystem(string familyName, FontGeneratorOptions?)` | GenerateCommand (`--system-font`) |
| `BmFontResult.ToFile(string outputPath, OutputFormat format)` | GenerateCommand (write output) |

### Font Reading / Inspection

| API | Used By |
|-----|---------|
| `BmFontReader.Read(byte[] fntData)` | InspectCommand, ConvertCommand |
| `TtfFontReader.ReadFont(byte[] fontData, int faceIndex)` | InfoCommand |
| `WoffDecompressor.IsWoff(byte[])` / `.IsWoff2(byte[])` / `.Decompress(byte[])` | InfoCommand |

### Configuration Types

| Type | Properties Used |
|------|----------------|
| `FontGeneratorOptions` | All properties: Size, Characters, Bold, Italic, AntiAlias, MaxTextureSize, Padding, Spacing, PackingAlgorithm, Kerning, Outline, Sdf, PowerOfTwo, Dpi, FaceIndex, ChannelPacking, VariationAxes, PostProcessors |
| `CharacterSet` | Static: `.Ascii`, `.ExtendedAscii`, `.Latin`; Factory: `.FromRanges()`, `.FromChars(string)`, `.FromChars(IEnumerable<int>)`; Instance: `.Union()`, `.Count` |
| `Padding` | Constructor: `Padding(int all)`, `Padding(int up, int right, int down, int left)` |
| `Spacing` | Constructor: `Spacing(int both)`, `Spacing(int horizontal, int vertical)` |
| `PackingAlgorithm` | Enum: `MaxRects`, `Skyline` |
| `AntiAliasMode` | Enum: `None`, `Grayscale`, `Light`, `Lcd` |
| `OutputFormat` | Enum: `Text`, `Xml`, `Binary` |

### Post-Processors

| API | Used By |
|-----|---------|
| `OutlinePostProcessor(int outlineWidth)` | GenerateCommand (`--outline`) |
| `GradientPostProcessor.Create((R,G,B) top, (R,G,B) bottom)` | GenerateCommand (`--gradient`) |

### System Fonts

| API | Used By |
|-----|---------|
| `DefaultSystemFontProvider.GetInstalledFonts()` | ListFontsCommand |
| `DefaultSystemFontProvider.LoadFont(string familyName, string? styleName)` | *(used internally by BmFont.GenerateFromSystem)* |

### Output Formatters (for ConvertCommand)

| API | Used By |
|-----|---------|
| `TextFormatter.FormatText(BmFontModel)` | ConvertCommand (text output) |
| `XmlFormatter.FormatText(BmFontModel)` | ConvertCommand (XML output) |
| `BmFontBinaryFormatter.FormatBinary(BmFontModel)` | ConvertCommand (binary output) |

### Output Model (for InspectCommand)

| Type | Properties Used |
|------|----------------|
| `BmFontModel` | Info, Common, Pages, Characters, KerningPairs |
| `InfoBlock` | Face, Size, Bold, Italic, Unicode, Smooth, Aa, Padding, Spacing |
| `CommonBlock` | LineHeight, Base, ScaleW, ScaleH, Pages, Packed, AlphaChnl/RedChnl/GreenChnl/BlueChnl |
| `PageEntry` | Id, File |
| `CharEntry` | Id, X, Y, Width, Height, XOffset, YOffset, XAdvance, Page, Channel |
| `KerningEntry` | First, Second, Amount |
