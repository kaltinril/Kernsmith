using Bmfontier.Cli.Config;
using Bmfontier.Cli.Utilities;
using Bmfontier.Output;

namespace Bmfontier.Cli.Commands;

internal sealed class GenerateCommand
{
    public static int Execute(string[] args)
    {
        if (args.Length == 0 || args is ["--help"])
        {
            ShowHelp();
            return ExitCodes.Success;
        }

        try
        {
            var options = ParseArgs(args);

            // Load config file first, then overlay CLI flags
            if (options.ConfigPath != null)
            {
                var configOptions = BmfcParser.Parse(options.ConfigPath);
                MergeConfigIntoOptions(configOptions, options);
                options = configOptions;
            }

            // Apply global flags
            ConsoleOutput.SetVerbose(options.Verbose);
            ConsoleOutput.SetQuiet(options.Quiet);

            // Validate
            if (options.FontPath == null && options.SystemFontName == null)
            {
                ConsoleOutput.WriteError("--font is required.");
                return ExitCodes.InvalidArguments;
            }

            if (options.Size == null)
            {
                ConsoleOutput.WriteError("--size is required.");
                return ExitCodes.InvalidArguments;
            }

            if (options.FontPath != null && !File.Exists(options.FontPath))
            {
                ConsoleOutput.WriteError($"Font file not found: {options.FontPath}");
                return ExitCodes.InvalidArguments;
            }

            // Build character set
            var characters = BuildCharacterSet(options);

            // Dry run
            if (options.DryRun)
            {
                PrintDryRun(options, characters);
                return ExitCodes.Success;
            }

            // Build FontGeneratorOptions
            var genOptions = new FontGeneratorOptions
            {
                Size = options.Size.Value,
                Characters = characters,
                Bold = options.Bold,
                Italic = options.Italic,
                AntiAlias = options.AntiAlias,
                MaxTextureSize = options.MaxTextureSize,
                Padding = options.Padding,
                Spacing = options.Spacing,
                PackingAlgorithm = options.PackingAlgorithm,
                Kerning = options.Kerning ?? true,
                Outline = options.Outline,
                Sdf = options.Sdf,
                PowerOfTwo = options.PowerOfTwo ?? true,
                Dpi = options.Dpi,
                FaceIndex = options.FaceIndex,
                ChannelPacking = options.ChannelPacking,
                SuperSampleLevel = options.SuperSampleLevel,
                FallbackCharacter = options.FallbackCharacter,
                EnableHinting = options.EnableHinting ?? true,
                AutofitTexture = options.AutofitTexture,
                EqualizeCellHeights = options.EqualizeCellHeights,
                ForceOffsetsToZero = options.ForceOffsetsToZero,
                HeightPercent = options.HeightPercent,
                MatchCharHeight = options.MatchCharHeight,
                ColorFont = options.ColorFont,
                ColorPaletteIndex = options.ColorPaletteIndex,
            };

            // Apply texture format if specified
            if (options.TextureFormat != null)
            {
                genOptions.TextureFormat = options.TextureFormat.ToLowerInvariant() switch
                {
                    "png" => Bmfontier.TextureFormat.Png,
                    "tga" => Bmfontier.TextureFormat.Tga,
                    "dds" => Bmfontier.TextureFormat.Dds,
                    var f => throw new ArgumentException($"Unknown texture format: {f}. Use png, tga, or dds.")
                };
            }

            // Apply independent max texture width/height if specified
            if (options.MaxTextureWidth.HasValue)
                genOptions.MaxTextureWidth = options.MaxTextureWidth.Value;
            if (options.MaxTextureHeight.HasValue)
                genOptions.MaxTextureHeight = options.MaxTextureHeight.Value;

            if (options.VariationAxes.Count > 0)
                genOptions.VariationAxes = new Dictionary<string, float>(options.VariationAxes);

            // Effects are now driven by FontGeneratorOptions properties.
            // The pipeline builds the correct layered effects automatically.
            if (options.GradientTop != null && options.GradientBottom != null)
            {
                var top = ColorParser.Parse(options.GradientTop);
                var bottom = ColorParser.Parse(options.GradientBottom);
                genOptions.GradientStartR = top.R;
                genOptions.GradientStartG = top.G;
                genOptions.GradientStartB = top.B;
                genOptions.GradientEndR = bottom.R;
                genOptions.GradientEndG = bottom.G;
                genOptions.GradientEndB = bottom.B;
                genOptions.GradientAngle = options.GradientAngle;
                genOptions.GradientMidpoint = options.GradientMidpoint;
            }
            if (options.Outline > 0 && options.OutlineColor != null)
            {
                var oc = ColorParser.Parse(options.OutlineColor);
                genOptions.OutlineR = oc.R;
                genOptions.OutlineG = oc.G;
                genOptions.OutlineB = oc.B;
            }
            if (options.ShadowOffsetX != 0 || options.ShadowOffsetY != 0 || options.ShadowColor != null)
            {
                genOptions.ShadowOffsetX = options.ShadowOffsetX;
                genOptions.ShadowOffsetY = options.ShadowOffsetY;
                genOptions.ShadowBlur = options.ShadowBlur;
                if (options.ShadowColor != null)
                {
                    var sc = ColorParser.Parse(options.ShadowColor);
                    genOptions.ShadowR = sc.R;
                    genOptions.ShadowG = sc.G;
                    genOptions.ShadowB = sc.B;
                }
            }

            // Determine output path
            string baseName;
            if (options.FontPath != null)
                baseName = Path.GetFileNameWithoutExtension(options.FontPath);
            else
                baseName = options.SystemFontName!.Replace(" ", "");

            var outputPath = options.OutputPath;
            if (outputPath == null)
            {
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), baseName);
            }
            else if (Directory.Exists(outputPath) || outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                Directory.CreateDirectory(outputPath);
                outputPath = Path.Combine(outputPath, baseName);
            }

            // Generate
            var fontDisplay = options.FontPath ?? options.SystemFontName ?? "font";
            ConsoleOutput.WriteStdout($"Loading font: {fontDisplay}");
            ConsoleOutput.WriteStdout($"Size: {options.Size}px, Charset: {options.CharsetPreset ?? "custom"}, Format: {options.OutputFormat.ToString().ToLowerInvariant()}");
            ConsoleOutput.WriteStdout($"Rasterizing {characters.Count} glyphs...");

            BmFontResult result;
            if (options.SystemFontName != null)
                result = BmFont.GenerateFromSystem(options.SystemFontName, genOptions);
            else
                result = BmFont.Generate(options.FontPath!, genOptions);

            ConsoleOutput.WriteStdout($"Packing into atlas ({result.Pages.Count} page(s))...");
            ConsoleOutput.WriteStdout($"Writing output to {outputPath}...");

            result.ToFile(outputPath, options.OutputFormat);

            ConsoleOutput.WriteStdout("Done.");

            // Save config if requested
            if (options.SaveConfigPath != null)
            {
                BmfcWriter.Write(options, options.SaveConfigPath);
                ConsoleOutput.WriteProgress($"Config saved to {options.SaveConfigPath}");
            }

            return ExitCodes.Success;
        }
        catch (FileNotFoundException ex)
        {
            ConsoleOutput.WriteError($"File not found: {ex.FileName ?? ex.Message}");
            return ExitCodes.FileNotFound;
        }
        catch (FontParsingException ex)
        {
            ConsoleOutput.WriteError($"Font error: {ex.Message}");
            return ExitCodes.FontParseError;
        }
        catch (InvalidOperationException ex)
        {
            ConsoleOutput.WriteError($"Generation error: {ex.Message}");
            return ExitCodes.GenerationError;
        }
        catch (IOException ex)
        {
            ConsoleOutput.WriteError($"I/O error: {ex.Message}");
            return ExitCodes.OutputWriteError;
        }
        catch (FormatException ex)
        {
            ConsoleOutput.WriteError($"Format error: {ex.Message}");
            return ExitCodes.ConfigParseError;
        }
        catch (ArgumentException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
    }

    private static CliOptions ParseArgs(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-f":
                case "--font":
                    options.FontPath = NextArg(args, ref i, args[i]);
                    break;
                case "--system-font":
                    options.SystemFontName = NextArg(args, ref i, args[i]);
                    break;
                case "-s":
                case "--size":
                    options.Size = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "-o":
                case "--output":
                    options.OutputPath = NextArg(args, ref i, args[i]);
                    break;
                case "--format":
                    options.OutputFormat = NextArg(args, ref i, args[i]).ToLowerInvariant() switch
                    {
                        "text" => OutputFormat.Text,
                        "xml" => OutputFormat.Xml,
                        "binary" => OutputFormat.Binary,
                        var f => throw new ArgumentException($"Unknown format: {f}. Use text, xml, or binary.")
                    };
                    break;
                case "-c":
                case "--charset":
                    options.CharsetPreset = NextArg(args, ref i, args[i]);
                    break;
                case "--chars":
                    options.ExplicitChars = NextArg(args, ref i, args[i]);
                    break;
                case "--chars-file":
                    options.CharsFilePath = NextArg(args, ref i, args[i]);
                    break;
                case "--range":
                    var rangeStr = NextArg(args, ref i, args[i]);
                    var parts = rangeStr.Split('-', 2);
                    if (parts.Length != 2)
                        throw new ArgumentException($"Invalid range: {rangeStr}. Expected format: 0020-007E");
                    options.UnicodeRanges.Add((Convert.ToInt32(parts[0], 16), Convert.ToInt32(parts[1], 16)));
                    break;
                case "--padding":
                    var padStr = NextArg(args, ref i, args[i]);
                    options.Padding = ParsePaddingArg(padStr);
                    break;
                case "--spacing":
                    var spcStr = NextArg(args, ref i, args[i]);
                    options.Spacing = ParseSpacingArg(spcStr);
                    break;
                case "--max-texture":
                case "--max-texture-size":
                    options.MaxTextureSize = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--packer":
                    options.PackingAlgorithm = NextArg(args, ref i, args[i]).ToLowerInvariant() switch
                    {
                        "maxrects" => PackingAlgorithm.MaxRects,
                        "skyline" => PackingAlgorithm.Skyline,
                        var p => throw new ArgumentException($"Unknown packer: {p}. Use maxrects or skyline.")
                    };
                    break;
                case "--pot":
                    options.PowerOfTwo = true;
                    break;
                case "--no-pot":
                    options.PowerOfTwo = false;
                    break;
                case "--channel-pack":
                    options.ChannelPacking = true;
                    break;
                case "--sdf":
                    options.Sdf = true;
                    break;
                case "--mono":
                    options.AntiAlias = AntiAliasMode.None;
                    break;
                case "--aa":
                    options.AntiAlias = NextArg(args, ref i, args[i]).ToLowerInvariant() switch
                    {
                        "none" => AntiAliasMode.None,
                        "grayscale" => AntiAliasMode.Grayscale,
                        "light" => AntiAliasMode.Light,
                        "lcd" => AntiAliasMode.Lcd,
                        var a => throw new ArgumentException($"Unknown anti-alias mode: {a}")
                    };
                    break;
                case "--dpi":
                    options.Dpi = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "-b":
                case "--bold":
                    options.Bold = true;
                    break;
                case "-i":
                case "--italic":
                    options.Italic = true;
                    break;
                case "--outline":
                    var outlineArg = NextArg(args, ref i, args[i]);
                    if (outlineArg.Contains(','))
                    {
                        var outlineParts = outlineArg.Split(',', 2);
                        options.Outline = int.Parse(outlineParts[0]);
                        options.OutlineColor = outlineParts[1];
                    }
                    else
                    {
                        options.Outline = int.Parse(outlineArg);
                    }
                    break;
                case "--gradient":
                    var gradientArg = NextArg(args, ref i, "--gradient");
                    if (gradientArg.Contains(','))
                    {
                        var gradientParts = gradientArg.Split(',', 2);
                        options.GradientTop = gradientParts[0];
                        options.GradientBottom = gradientParts[1];
                    }
                    else
                    {
                        options.GradientTop = gradientArg;
                        var bottomArg = NextArg(args, ref i, "--gradient");
                        if (bottomArg.StartsWith('-') && !bottomArg.StartsWith('#'))
                            throw new ArgumentException(
                                $"Invalid gradient bottom color '{bottomArg}'. " +
                                "Use --gradient <top>,<bottom> or --gradient <top> <bottom> with valid hex colors.");
                        options.GradientBottom = bottomArg;
                    }
                    break;
                case "--gradient-angle":
                    options.GradientAngle = float.Parse(NextArg(args, ref i, "--gradient-angle"));
                    break;
                case "--gradient-midpoint":
                    options.GradientMidpoint = float.Parse(NextArg(args, ref i, "--gradient-midpoint"));
                    break;
                case "--kerning":
                    options.Kerning = true;
                    break;
                case "--no-kerning":
                    options.Kerning = false;
                    break;
                case "--axis":
                    var axisStr = NextArg(args, ref i, args[i]);
                    var eqIdx = axisStr.IndexOf('=');
                    if (eqIdx < 0)
                        throw new ArgumentException($"Invalid axis: {axisStr}. Expected format: tag=value (e.g., wght=700)");
                    var tag = axisStr[..eqIdx];
                    var val = float.Parse(axisStr[(eqIdx + 1)..]);
                    options.VariationAxes[tag] = val;
                    break;
                case "--shadow":
                    var shadowArg = NextArg(args, ref i, "--shadow");
                    ParseShadowArg(options, shadowArg);
                    break;
                case "--instance":
                    options.InstanceName = NextArg(args, ref i, args[i]);
                    break;
                case "--face":
                    options.FaceIndex = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--super-sample":
                    options.SuperSampleLevel = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--fallback-char":
                    var fbArg = NextArg(args, ref i, args[i]);
                    options.FallbackCharacter = fbArg.Length == 1 ? fbArg[0] : (char)int.Parse(fbArg);
                    break;
                case "--texture-format":
                    options.TextureFormat = NextArg(args, ref i, args[i]).ToLowerInvariant();
                    break;
                case "--hinting":
                    options.EnableHinting = true;
                    break;
                case "--no-hinting":
                    options.EnableHinting = false;
                    break;
                case "--autofit":
                    options.AutofitTexture = true;
                    break;
                case "--equalize-heights":
                    options.EqualizeCellHeights = true;
                    break;
                case "--force-offsets-zero":
                    options.ForceOffsetsToZero = true;
                    break;
                case "--height-percent":
                    options.HeightPercent = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--match-char-height":
                    options.MatchCharHeight = true;
                    break;
                case "--color-font":
                    options.ColorFont = true;
                    break;
                case "--color-palette":
                    options.ColorPaletteIndex = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--max-texture-width":
                    options.MaxTextureWidth = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--max-texture-height":
                    options.MaxTextureHeight = int.Parse(NextArg(args, ref i, args[i]));
                    break;
                case "--config":
                    options.ConfigPath = NextArg(args, ref i, args[i]);
                    break;
                case "--save-config":
                    options.SaveConfigPath = NextArg(args, ref i, args[i]);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "-q":
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "--no-color":
                    ConsoleOutput.SetNoColor(true);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }

        return options;
    }

    /// <summary>
    /// Merges CLI-provided options over config-loaded options.
    /// CLI flags that were explicitly set override config values.
    /// </summary>
    private static void MergeConfigIntoOptions(CliOptions config, CliOptions cli)
    {
        // CLI values override config values (only override if explicitly set by CLI)
        if (cli.FontPath != null) config.FontPath = cli.FontPath;
        if (cli.SystemFontName != null) config.SystemFontName = cli.SystemFontName;
        if (cli.Size != null) config.Size = cli.Size;
        if (cli.OutputPath != null) config.OutputPath = cli.OutputPath;
        if (cli.SaveConfigPath != null) config.SaveConfigPath = cli.SaveConfigPath;
        if (cli.ExplicitChars != null) config.ExplicitChars = cli.ExplicitChars;
        if (cli.CharsFilePath != null) config.CharsFilePath = cli.CharsFilePath;
        if (cli.UnicodeRanges.Count > 0) config.UnicodeRanges = cli.UnicodeRanges;
        if (cli.GradientTop != null) config.GradientTop = cli.GradientTop;
        if (cli.GradientBottom != null) config.GradientBottom = cli.GradientBottom;
        if (cli.VariationAxes.Count > 0) config.VariationAxes = cli.VariationAxes;

        // GC-1: Previously missing fields — these CLI values were silently dropped.
        if (cli.OutputFormat != OutputFormat.Text) config.OutputFormat = cli.OutputFormat;
        if (cli.AntiAlias != AntiAliasMode.Grayscale) config.AntiAlias = cli.AntiAlias;
        if (cli.MaxTextureSize != 1024) config.MaxTextureSize = cli.MaxTextureSize;
        if (cli.Outline > 0) { config.Outline = cli.Outline; config.OutlineColor = cli.OutlineColor ?? config.OutlineColor; }
        if (cli.Dpi != 72) config.Dpi = cli.Dpi;
        if (cli.FaceIndex != 0) config.FaceIndex = cli.FaceIndex;
        if (cli.PackingAlgorithm != PackingAlgorithm.MaxRects) config.PackingAlgorithm = cli.PackingAlgorithm;
        if (cli.CharsetPreset != null && cli.CharsetPreset != "ascii") config.CharsetPreset = cli.CharsetPreset;

        // Bool/value flags: these are trickier because defaults look like "not set".
        // For the CLI overlay, we simply copy all values that differ from defaults.
        // This is imperfect but practical: the CLI user must use the flag to override.
        if (cli.Bold) config.Bold = true;
        if (cli.Italic) config.Italic = true;
        if (cli.Sdf) config.Sdf = true;
        if (cli.DryRun) config.DryRun = true;
        if (cli.Verbose) config.Verbose = true;
        if (cli.Quiet) config.Quiet = true;
        if (cli.ChannelPacking) config.ChannelPacking = true;
        if (cli.Kerning.HasValue) config.Kerning = cli.Kerning;
        if (cli.PowerOfTwo.HasValue) config.PowerOfTwo = cli.PowerOfTwo;
        if (cli.EnableHinting.HasValue) config.EnableHinting = cli.EnableHinting;
        if (cli.AutofitTexture) config.AutofitTexture = true;
        if (cli.EqualizeCellHeights) config.EqualizeCellHeights = true;
        if (cli.ForceOffsetsToZero) config.ForceOffsetsToZero = true;
        if (cli.MatchCharHeight) config.MatchCharHeight = true;
        if (cli.ColorFont) config.ColorFont = true;
        if (cli.ColorPaletteIndex != 0) config.ColorPaletteIndex = cli.ColorPaletteIndex;
        if (cli.SuperSampleLevel != 1) config.SuperSampleLevel = cli.SuperSampleLevel;
        if (cli.FallbackCharacter.HasValue) config.FallbackCharacter = cli.FallbackCharacter;
        if (cli.TextureFormat != null) config.TextureFormat = cli.TextureFormat;
        if (cli.HeightPercent != 100) config.HeightPercent = cli.HeightPercent;
        if (cli.MaxTextureWidth.HasValue) config.MaxTextureWidth = cli.MaxTextureWidth;
        if (cli.MaxTextureHeight.HasValue) config.MaxTextureHeight = cli.MaxTextureHeight;
        if (cli.InstanceName != null) config.InstanceName = cli.InstanceName;
        if (cli.ShadowOffsetX != 0 || cli.ShadowOffsetY != 0) { config.ShadowOffsetX = cli.ShadowOffsetX; config.ShadowOffsetY = cli.ShadowOffsetY; }
        if (cli.ShadowColor != null) config.ShadowColor = cli.ShadowColor;
        if (cli.ShadowBlur != 0) config.ShadowBlur = cli.ShadowBlur;
    }

    private static CharacterSet BuildCharacterSet(CliOptions options)
    {
        var sets = new List<CharacterSet>();

        // Preset
        if (options.CharsetPreset != null)
        {
            sets.Add(options.CharsetPreset.ToLowerInvariant() switch
            {
                "ascii" => CharacterSet.Ascii,
                "extended" => CharacterSet.ExtendedAscii,
                "latin" => CharacterSet.Latin,
                _ => CharacterSet.FromChars(options.CharsetPreset)
            });
        }

        // Explicit chars
        if (options.ExplicitChars != null)
            sets.Add(CharacterSet.FromChars(options.ExplicitChars));

        // Chars file
        if (options.CharsFilePath != null)
        {
            if (!File.Exists(options.CharsFilePath))
                throw new FileNotFoundException($"Character file not found: {options.CharsFilePath}", options.CharsFilePath);
            var content = File.ReadAllText(options.CharsFilePath);
            sets.Add(CharacterSet.FromChars(content));
        }

        // Unicode ranges
        if (options.UnicodeRanges.Count > 0)
        {
            sets.Add(CharacterSet.FromRanges(options.UnicodeRanges.ToArray()));
        }

        if (sets.Count == 0)
            return CharacterSet.Ascii;

        return sets.Count == 1 ? sets[0] : CharacterSet.Union(sets.ToArray());
    }

    private static void PrintDryRun(CliOptions options, CharacterSet characters)
    {
        var fontSource = options.FontPath ?? $"system:{options.SystemFontName}";
        Console.WriteLine($"[dry-run] Font:      {fontSource}");
        Console.WriteLine($"[dry-run] Size:      {options.Size}px");
        Console.WriteLine($"[dry-run] Charset:   {options.CharsetPreset ?? "custom"} ({characters.Count} codepoints)");
        Console.WriteLine($"[dry-run] Atlas:     max {options.MaxTextureSize}x{options.MaxTextureSize}, {(options.PackingAlgorithm == PackingAlgorithm.MaxRects ? "maxrects" : "skyline")} packer");

        var effects = new List<string>();
        if (options.Bold) effects.Add("bold");
        if (options.Italic) effects.Add("italic");
        if (options.Sdf) effects.Add("SDF");
        if (options.Outline > 0) effects.Add($"outline {options.Outline}px");
        if (options.GradientTop != null) effects.Add($"gradient {options.GradientTop}->{options.GradientBottom}");
        Console.WriteLine($"[dry-run] Effects:   {(effects.Count > 0 ? string.Join(", ", effects) : "none")}");

        var outPath = options.OutputPath ?? Path.GetFileNameWithoutExtension(options.FontPath ?? options.SystemFontName ?? "font");
        Console.WriteLine($"[dry-run] Output:    ./{outPath}.fnt ({options.OutputFormat.ToString().ToLowerInvariant()} format)");
        Console.WriteLine("[dry-run] No files written.");
    }

    private static Padding ParsePaddingArg(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 4)
            return new Padding(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        return new Padding(int.Parse(parts[0]));
    }

    private static Spacing ParseSpacingArg(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
            return new Spacing(int.Parse(parts[0]), int.Parse(parts[1]));
        return new Spacing(int.Parse(parts[0]));
    }

    /// <summary>
    /// Parses a shadow argument string in the form "offsetX,offsetY[,color[,blur]]".
    /// </summary>
    private static void ParseShadowArg(CliOptions options, string arg)
    {
        var parts = arg.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            throw new ArgumentException(
                $"Invalid shadow format '{arg}'. Expected: offsetX,offsetY[,color[,blur]]");

        options.ShadowOffsetX = int.Parse(parts[0]);
        options.ShadowOffsetY = int.Parse(parts[1]);

        if (parts.Length >= 3)
            options.ShadowColor = parts[2];
        if (parts.Length >= 4)
            options.ShadowBlur = int.Parse(parts[3]);
    }

    private static string NextArg(string[] allArgs, ref int i, string flag)
    {
        i++;
        if (i >= allArgs.Length)
            throw new ArgumentException($"Missing value for {flag}");
        return allArgs[i];
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Generate BMFont files from a font.

            Usage: bmfontier generate -f <font> -s <size> [options]

            Font Source (one required):
              -f, --font <path>           Font file path (TTF, OTF, WOFF)
              --system-font <name>        Use a system-installed font by family name

            Output:
              -o, --output <path>         Output file path (default: ./<fontname>)
              --format <text|xml|binary>  Output format (default: text)
              --texture-format <fmt>      Texture format: png (default), tga, dds

            Size & Rendering:
              -s, --size <n>              Font size in pixels (required)
              --dpi <n>                   DPI (default: 72)
              --aa <none|grayscale|light|lcd>  Anti-aliasing mode (default: grayscale)
              --sdf                       Enable Signed Distance Field rendering
              --mono                      Disable anti-aliasing (alias for --aa none)
              --super-sample <n>          Super sampling level 1-4 (default: 1)
              --hinting / --no-hinting    Enable/disable FreeType hinting (default: on)
              --height-percent <n>        Vertical height scaling percentage (default: 100)
              --match-char-height         Match rendered height to requested pixel height
              --fallback-char <char>      Fallback character for missing glyphs (char or codepoint)

            Style:
              -b, --bold                  Enable synthetic bold
              -i, --italic                Enable synthetic italic
              --color-font                Enable color font rendering (COLR/CPAL)
              --color-palette <n>         Color palette index (default: 0)

            Character Set:
              -c, --charset <preset>      Character set preset: ascii, extended, latin (default: ascii)
              --chars <string>            Explicit characters to include
              --chars-file <path>         Read characters from a text file (UTF-8)
              --range <start-end>         Unicode range (hex), repeatable (e.g., --range 0020-007E)

            Texture Atlas:
              --max-texture <n>           Maximum texture size in pixels (default: 1024)
              --max-texture-width <n>     Maximum texture width (independent of height)
              --max-texture-height <n>    Maximum texture height (independent of width)
              --autofit                   Auto-fit smallest texture size for all glyphs
              --padding <n>               Padding around each glyph in pixels (default: 0)
              --padding <u,r,d,l>         Per-side padding (up,right,down,left)
              --spacing <n>               Spacing between glyphs in pixels (default: 1)
              --spacing <h,v>             Horizontal,vertical spacing
              --pot                       Force power-of-two texture dimensions (default: on)
              --no-pot                    Allow non-power-of-two textures
              --packer <maxrects|skyline> Packing algorithm (default: maxrects)
              --channel-pack              Pack glyphs into individual RGBA channels
              --equalize-heights          Equalize all glyph cell heights
              --force-offsets-zero        Set all xoffset/yoffset to zero

            Effects:
              --outline <n>[,color]        Outline width in pixels, optional hex color (e.g., --outline 2,FF0000)
              --gradient <top>,<bottom>   Vertical gradient, colors as hex
              --gradient <top> <bottom>   Vertical gradient (two-argument form)
              --shadow <x>,<y>[,color[,blur]]  Drop shadow (e.g., --shadow 2,2,000000,1)

            Kerning:
              --no-kerning                Disable kerning pair extraction
              --kerning                   Enable kerning (default: on)

            Variable Fonts:
              --axis <tag>=<value>        Set variation axis (repeatable)
              --instance <name>           Use a named instance
              --face <n>                  Face index for .ttc collections (default: 0)

            Configuration:
              --config <path>             Load settings from a .bmfc configuration file
              --save-config <path>        Save current settings to a .bmfc file
              --dry-run                   Show what would be generated without writing files

            Verbosity:
              -v, --verbose               Show detailed progress
              -q, --quiet                 Suppress all output except errors
              --no-color                  Disable colored output

            Examples:
              bmfontier generate -f arial.ttf -s 32
              bmfontier generate -f roboto.ttf -s 48 -b -i --outline 2
              bmfontier generate -f font.ttf -s 24 --super-sample 2 --no-hinting
              bmfontier generate --config mygame.bmfc --save-config updated.bmfc
            """);
    }
}
