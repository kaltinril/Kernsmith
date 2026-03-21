using System.Text;
using KernSmith.Cli.Utilities;
using KernSmith.Output;
using KernSmith.Output.Model;

namespace KernSmith.Cli.Commands;

internal sealed class ConvertCommand
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
            string? inputPath = null;
            string? outputPath = null;
            string? formatStr = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o":
                    case "--output":
                        i++;
                        if (i >= args.Length)
                            throw new ArgumentException("Missing value for --output");
                        outputPath = args[i];
                        break;
                    case "--format":
                        i++;
                        if (i >= args.Length)
                            throw new ArgumentException("Missing value for --format");
                        formatStr = args[i];
                        break;
                    case "--no-color":
                        ConsoleOutput.SetNoColor(true);
                        break;
                    case "-v":
                    case "--verbose":
                        ConsoleOutput.SetVerbose(true);
                        break;
                    case "-q":
                    case "--quiet":
                        ConsoleOutput.SetQuiet(true);
                        break;
                    default:
                        if (args[i].StartsWith('-'))
                            throw new ArgumentException($"Unknown option: {args[i]}");
                        inputPath = args[i];
                        break;
                }
            }

            if (inputPath == null)
            {
                ConsoleOutput.WriteError("Input .fnt file path is required.");
                return ExitCodes.InvalidArguments;
            }

            if (outputPath == null)
            {
                ConsoleOutput.WriteError("Output path is required. Use -o/--output.");
                return ExitCodes.InvalidArguments;
            }

            if (!File.Exists(inputPath))
            {
                ConsoleOutput.WriteError($"File not found: {inputPath}");
                return ExitCodes.FileNotFound;
            }

            // Determine output format
            OutputFormat format;
            if (formatStr != null)
            {
                format = formatStr.ToLowerInvariant() switch
                {
                    "text" => OutputFormat.Text,
                    "xml" => OutputFormat.Xml,
                    "binary" => OutputFormat.Binary,
                    _ => throw new ArgumentException($"Unknown format: {formatStr}. Use text, xml, or binary.")
                };
            }
            else
            {
                // Infer from output extension
                var ext = Path.GetExtension(outputPath).ToLowerInvariant();
                format = ext switch
                {
                    ".xml" => OutputFormat.Xml,
                    ".bin" => OutputFormat.Binary,
                    _ => OutputFormat.Text
                };
            }

            // Read input
            var inputData = File.ReadAllBytes(inputPath);
            var model = BmFontReader.Read(inputData);

            // Write output
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            switch (format)
            {
                case OutputFormat.Text:
                    var text = new TextFormatter().FormatText(model);
                    File.WriteAllText(outputPath, text, Encoding.UTF8);
                    break;
                case OutputFormat.Xml:
                    var xml = new XmlFormatter().FormatText(model);
                    File.WriteAllText(outputPath, xml, Encoding.UTF8);
                    break;
                case OutputFormat.Binary:
                    var binary = new BmFontBinaryFormatter().FormatBinary(model);
                    File.WriteAllBytes(outputPath, binary);
                    break;
            }

            // Copy atlas page PNG files if directories differ
            var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
            var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";

            if (!string.Equals(Path.GetFullPath(inputDir), Path.GetFullPath(outDir), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var page in model.Pages)
                {
                    // Sanitize page file path: strip absolute/rooted paths to just the filename.
                    var pageFileName = Path.IsPathRooted(page.File)
                        ? Path.GetFileName(page.File)
                        : page.File;

                    var srcPng = Path.Combine(inputDir, pageFileName);
                    if (File.Exists(srcPng))
                    {
                        var dstPng = Path.Combine(outDir, pageFileName);

                        // Ensure subdirectories exist for nested page paths.
                        var dstPngDir = Path.GetDirectoryName(dstPng);
                        if (!string.IsNullOrEmpty(dstPngDir))
                            Directory.CreateDirectory(dstPngDir);

                        File.Copy(srcPng, dstPng, overwrite: true);
                        ConsoleOutput.WriteVerbose($"Copied atlas page: {pageFileName}");
                    }
                    else
                    {
                        ConsoleOutput.WriteWarning($"Atlas page not found: {srcPng}");
                    }
                }
            }

            ConsoleOutput.WriteSuccess($"Converted {Path.GetFileName(inputPath)} -> {Path.GetFileName(outputPath)} ({format.ToString().ToLowerInvariant()} format)");
            return ExitCodes.Success;
        }
        catch (FormatException ex)
        {
            ConsoleOutput.WriteError($"Format error: {ex.Message}");
            return ExitCodes.FontParseError;
        }
        catch (IOException ex)
        {
            ConsoleOutput.WriteError($"I/O error: {ex.Message}");
            return ExitCodes.OutputWriteError;
        }
        catch (ArgumentException ex)
        {
            ConsoleOutput.WriteError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Convert between BMFont formats.

            Usage: kernsmith convert <input> -o <output> [--format <text|xml|binary>]

              <input>                     Path to input .fnt file (auto-detects format)
              -o, --output <path>         Output file path (required)
              --format <text|xml|binary>  Output format (default: inferred from extension)

            Format inference from extension:
              .fnt  -> text
              .xml  -> xml
              .bin  -> binary

            Atlas page PNG files are automatically copied to the output directory.

            Examples:
              kernsmith convert myfont.fnt -o myfont.xml --format xml
              kernsmith convert myfont.fnt -o output/myfont.bin --format binary
            """);
    }
}
