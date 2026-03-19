using Bmfontier;
using Bmfontier.Font;

if (args.Length == 0 || args[0] == "--help")
{
    ShowHelp();
    return 0;
}

if (args[0] == "list-fonts")
{
    ListFonts();
    return 0;
}

if (args[0] == "generate")
{
    return Generate(args.Skip(1).ToArray());
}

Console.Error.WriteLine($"Unknown command: {args[0]}");
Console.Error.WriteLine("Run with --help for usage information.");
return 1;

static void ShowHelp()
{
    Console.WriteLine("""
        Usage:
          bmfontier generate -f <font> -s <size> [options]
          bmfontier list-fonts
          bmfontier --help

        Commands:
          generate      Generate BMFont files from a font
          list-fonts    List installed system fonts

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
          --max-texture-size <n>       Max texture size (default: 1024)
        """);
}

static void ListFonts()
{
    Console.WriteLine("Scanning system fonts...");
    var provider = new DefaultSystemFontProvider();
    var fonts = provider.GetInstalledFonts();

    var grouped = fonts
        .OrderBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(f => f.StyleName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    Console.WriteLine($"Found {grouped.Count} font faces:");
    Console.WriteLine();

    foreach (var font in grouped)
    {
        Console.WriteLine($"  {font.FamilyName} - {font.StyleName}");
    }
}

static int Generate(string[] generateArgs)
{
    string? fontPath = null;
    int? size = null;
    string? output = null;
    string charset = "ascii";
    string format = "text";
    string packer = "maxrects";
    int padding = 0;
    int spacing = 1;
    bool sdf = false;
    int outline = 0;
    int maxTextureSize = 1024;

    for (int i = 0; i < generateArgs.Length; i++)
    {
        switch (generateArgs[i])
        {
            case "-f":
            case "--font":
                fontPath = NextArg(generateArgs, ref i, generateArgs[i]);
                break;
            case "-s":
            case "--size":
                size = int.Parse(NextArg(generateArgs, ref i, generateArgs[i]));
                break;
            case "-o":
            case "--output":
                output = NextArg(generateArgs, ref i, generateArgs[i]);
                break;
            case "-c":
            case "--charset":
                charset = NextArg(generateArgs, ref i, generateArgs[i]);
                break;
            case "--format":
                format = NextArg(generateArgs, ref i, generateArgs[i]);
                break;
            case "--packer":
                packer = NextArg(generateArgs, ref i, generateArgs[i]);
                break;
            case "--padding":
                padding = int.Parse(NextArg(generateArgs, ref i, generateArgs[i]));
                break;
            case "--spacing":
                spacing = int.Parse(NextArg(generateArgs, ref i, generateArgs[i]));
                break;
            case "--sdf":
                sdf = true;
                break;
            case "--outline":
                outline = int.Parse(NextArg(generateArgs, ref i, generateArgs[i]));
                break;
            case "--max-texture-size":
                maxTextureSize = int.Parse(NextArg(generateArgs, ref i, generateArgs[i]));
                break;
            default:
                Console.Error.WriteLine($"Unknown option: {generateArgs[i]}");
                return 1;
        }
    }

    if (fontPath == null)
    {
        Console.Error.WriteLine("Error: --font is required.");
        return 1;
    }

    if (size == null)
    {
        Console.Error.WriteLine("Error: --size is required.");
        return 1;
    }

    if (!File.Exists(fontPath))
    {
        Console.Error.WriteLine($"Error: Font file not found: {fontPath}");
        return 1;
    }

    // Resolve character set
    CharacterSet characters = charset.ToLowerInvariant() switch
    {
        "ascii" => CharacterSet.Ascii,
        "extended" => CharacterSet.ExtendedAscii,
        "latin" => CharacterSet.Latin,
        _ => CharacterSet.FromChars(charset)
    };

    // Resolve output format
    OutputFormat outputFormat = format.ToLowerInvariant() switch
    {
        "text" => OutputFormat.Text,
        "xml" => OutputFormat.Xml,
        "binary" => OutputFormat.Binary,
        _ => throw new ArgumentException($"Unknown format: {format}. Use text, xml, or binary.")
    };

    // Resolve packing algorithm
    PackingAlgorithm packingAlgorithm = packer.ToLowerInvariant() switch
    {
        "maxrects" => PackingAlgorithm.MaxRects,
        "skyline" => PackingAlgorithm.Skyline,
        _ => throw new ArgumentException($"Unknown packer: {packer}. Use maxrects or skyline.")
    };

    // Build options
    var options = new FontGeneratorOptions
    {
        Size = size.Value,
        Characters = characters,
        Padding = new Padding(padding),
        Spacing = new Spacing(spacing),
        PackingAlgorithm = packingAlgorithm,
        Sdf = sdf,
        Outline = outline,
        MaxTextureSize = maxTextureSize
    };

    // Determine output path
    if (output == null)
    {
        var fontName = Path.GetFileNameWithoutExtension(fontPath);
        output = Path.Combine(Directory.GetCurrentDirectory(), fontName);
    }

    Console.WriteLine($"Loading font: {fontPath}");
    Console.WriteLine($"Size: {size.Value}px, Charset: {charset}, Format: {format}");
    Console.WriteLine($"Rasterizing {characters.Count} glyphs...");

    var result = BmFont.Generate(fontPath, options);

    Console.WriteLine($"Packing into atlas ({result.Pages.Count} page(s))...");
    Console.WriteLine($"Writing output to {output}...");

    result.ToFile(output, outputFormat);

    Console.WriteLine("Done.");
    return 0;
}

static string NextArg(string[] allArgs, ref int i, string flag)
{
    i++;
    if (i >= allArgs.Length)
        throw new ArgumentException($"Missing value for {flag}");
    return allArgs[i];
}
