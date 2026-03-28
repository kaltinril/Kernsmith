using KernSmith.Cli.Commands;
using KernSmith.Cli.Utilities;

// Handle global flags before command dispatch
var filtered = new List<string>();
foreach (var arg in args)
{
    switch (arg)
    {
        case "--no-color":
            ConsoleOutput.SetNoColor(true);
            break;
        default:
            filtered.Add(arg);
            break;
    }
}

var processedArgs = filtered.ToArray();

return processedArgs switch
{
    [] or ["--help"] or ["-h"] => ShowHelp(),
    ["--version"] => ShowVersion(),
    ["generate", .. var rest] => GenerateCommand.Execute(rest),
    ["init", .. var rest] => InitCommand.Execute(rest),
    ["batch", .. var rest] => BatchCommand.Execute(rest),
    ["benchmark", .. var rest] => BenchmarkCommand.Execute(rest),
    ["inspect", .. var rest] => InspectCommand.Execute(rest),
    ["convert", .. var rest] => ConvertCommand.Execute(rest),
    ["list-fonts", .. var rest] => ListFontsCommand.Execute(rest),
    ["list-rasterizers", .. var rest] => ListRasterizersCommand.Execute(rest),
    ["info", .. var rest] => InfoCommand.Execute(rest),
    _ => UnknownCommand(processedArgs[0])
};

static int ShowHelp()
{
    Console.WriteLine("""
        kernsmith - Cross-platform BMFont generator

        Usage:
          kernsmith generate -f <font> -s <size> [options]
          kernsmith init [options] -o <path>
          kernsmith batch <config1.bmfc> [config2.bmfc ...] [options]
          kernsmith benchmark -f <font> -s <size> [options]
          kernsmith inspect <path>
          kernsmith convert <input> -o <output> [--format <text|xml|binary>]
          kernsmith list-fonts [--filter <pattern>]
          kernsmith list-rasterizers
          kernsmith info <path>
          kernsmith --help

        Commands:
          generate      Generate BMFont files from a font
          init          Generate a .bmfc config file without rendering
          batch         Process multiple .bmfc configs in a single invocation
          benchmark     Benchmark font generation performance
          inspect       Inspect an existing .fnt file
          convert       Convert between BMFont formats (text/xml/binary)
          list-fonts        List system-installed fonts
          list-rasterizers  List available rasterizer backends on this platform
          info              Show font file metadata (TTF/OTF/WOFF)

        Generate options:
          -f, --font <path>            Font file path (required)
          --system-font <name>         System font family name (alternative to --font)
          -s, --size <pixels>          Font size in pixels (required)
          -o, --output <path>          Output path, without extension (default: ./<fontname>)
          -c, --charset <set>          Character set: ascii (default), extended, latin,
                                       or a literal string of characters
          --format <fmt>               Output format: text (default), xml, binary
          --texture-format <fmt>       Texture format: png (default), tga, dds
          --packer <alg>               Packing algorithm: maxrects (default), skyline
          --padding <n>                Padding in pixels (default: 0)
          --spacing <n>                Spacing in pixels (default: 1)
          --sdf                        Enable SDF rendering
          --outline <n>                Outline width in pixels (default: 0)
          --gradient <top>,<bottom>    Vertical gradient (comma or space separated)
          --shadow <x>,<y>,<color>,<blur>  Drop shadow
          --max-texture <n>            Max texture size (default: 1024)
          --max-texture-size <n>       Max texture size (alias)
          --max-texture-width <n>      Max texture width (independent)
          --max-texture-height <n>     Max texture height (independent)
          --autofit                    Auto-fit smallest texture size
          -b, --bold                   Enable synthetic bold
          -i, --italic                 Enable synthetic italic
          --super-sample <n>           Super sampling 1-4 (default: 1)
          --rasterizer <backend>       Rasterizer backend: freetype (default), gdi, directwrite
          --hinting / --no-hinting     Enable/disable hinting (default: on)
          --height-percent <n>         Height scaling percentage (default: 100)
          --match-char-height          Match rendered to pixel height
          --fallback-char <char>       Fallback character for missing glyphs
          --color-font                 Enable color font rendering
          --color-palette <n>          Color palette index (default: 0)
          --equalize-heights           Equalize glyph cell heights
          --force-offsets-zero         Set all offsets to zero
          --no-kerning                 Disable kerning pair extraction
          --kerning                    Enable kerning (default: on)
          --aa <mode>                  Anti-aliasing: none, grayscale, light, lcd
          --mono                       Disable anti-aliasing
          --dpi <n>                    DPI (default: 72)
          --pot / --no-pot             Power-of-two textures (default: on)
          --channel-pack               Pack into RGBA channels
          --axis <tag>=<value>         Set variation axis (repeatable)
          --instance <name>            Use a named font instance
          --face <n>                   Face index for .ttc (default: 0)
          --config <path>              Load settings from a .bmfc configuration file
          --save-config <path>         Save current settings to a .bmfc file
          --dry-run                    Show what would be generated without writing files
          --time                       Print generation time (excludes CLI startup)
          --profile                    Show per-stage pipeline timing breakdown

        Global Options:
          --help, -h    Show help
          --version     Show version
          --no-color    Disable colored output
          -v, --verbose Show detailed progress
          -q, --quiet   Suppress all output except errors

        Run 'kernsmith <command> --help' for command-specific options.
        """);
    return ExitCodes.Success;
}

static int ShowVersion()
{
    var version = typeof(KernSmith.BmFont).Assembly.GetName().Version;
    Console.WriteLine($"kernsmith {version?.ToString(3) ?? "0.0.0"}");
    return ExitCodes.Success;
}

static int UnknownCommand(string command)
{
    ConsoleOutput.WriteError($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'kernsmith --help' for usage information.");
    return ExitCodes.InvalidArguments;
}
