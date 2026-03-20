using Bmfontier.Cli.Commands;
using Bmfontier.Cli.Utilities;

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
    ["inspect", .. var rest] => InspectCommand.Execute(rest),
    ["convert", .. var rest] => ConvertCommand.Execute(rest),
    ["list-fonts", .. var rest] => ListFontsCommand.Execute(rest),
    ["info", .. var rest] => InfoCommand.Execute(rest),
    _ => UnknownCommand(processedArgs[0])
};

static int ShowHelp()
{
    Console.WriteLine("""
        bmfontier - Cross-platform BMFont generator

        Usage:
          bmfontier generate -f <font> -s <size> [options]
          bmfontier inspect <path>
          bmfontier convert <input> -o <output> [--format <text|xml|binary>]
          bmfontier list-fonts [--filter <pattern>]
          bmfontier info <path>
          bmfontier --help

        Commands:
          generate      Generate BMFont files from a font
          inspect       Inspect an existing .fnt file
          convert       Convert between BMFont formats (text/xml/binary)
          list-fonts    List system-installed fonts
          info          Show font file metadata (TTF/OTF/WOFF)

        Generate options:
          -f, --font <path>            Font file path (required)
          -s, --size <pixels>          Font size in pixels (required)
          -o, --output <path>          Output path, without extension (default: ./<fontname>)
          -c, --charset <set>          Character set: ascii (default), extended, latin,
                                       or a literal string of characters
          --format <fmt>               Output format: text (default), xml, binary
          --packer <alg>               Packing algorithm: maxrects (default), skyline
          --padding <n>                Padding in pixels (default: 0)
          --spacing <n>                Spacing in pixels (default: 1)
          --sdf                        Enable SDF rendering
          --outline <n>                Outline width in pixels (default: 0)
          --max-texture <n>            Max texture size (default: 1024)
          --max-texture-size <n>       Max texture size (alias)
          -b, --bold                   Enable synthetic bold
          -i, --italic                 Enable synthetic italic
          --gradient <top> <bottom>    Vertical gradient, colors as hex
          --no-kerning                 Disable kerning pair extraction
          --axis <tag>=<value>         Set variation axis (repeatable)
          --config <path>              Load settings from a .bmfc configuration file
          --save-config <path>         Save current settings to a .bmfc file
          --dry-run                    Show what would be generated without writing files

        Global Options:
          --help, -h    Show help
          --version     Show version
          --no-color    Disable colored output
          -v, --verbose Show detailed progress
          -q, --quiet   Suppress all output except errors

        Run 'bmfontier <command> --help' for command-specific options.
        """);
    return ExitCodes.Success;
}

static int ShowVersion()
{
    var version = typeof(Bmfontier.BmFont).Assembly.GetName().Version;
    Console.WriteLine($"bmfontier {version?.ToString(3) ?? "0.1.0"}");
    return ExitCodes.Success;
}

static int UnknownCommand(string command)
{
    ConsoleOutput.WriteError($"Unknown command: {command}");
    Console.Error.WriteLine("Run 'bmfontier --help' for usage information.");
    return ExitCodes.InvalidArguments;
}
