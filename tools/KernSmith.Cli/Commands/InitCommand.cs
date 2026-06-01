using KernSmith.Cli.Config;
using KernSmith.Cli.Utilities;

namespace KernSmith.Cli.Commands;

/// <summary>
/// Generates a .bmfc or .hiero configuration file from CLI flags without rendering a font.
/// This lets users scaffold a config, tweak it by hand, then run <c>kernsmith generate --config</c>.
/// </summary>
internal sealed class InitCommand
{
    /// <summary>
    /// Parses the provided CLI arguments and writes a .bmfc or .hiero configuration file to disk.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded from the top-level dispatcher.</param>
    /// <returns>An exit code indicating success or the category of failure.</returns>
    public static int Execute(string[] args)
    {
        if (args is ["--help"])
        {
            ShowHelp();
            return ExitCodes.Success;
        }

        try
        {
            var options = GenerateCommand.ParseArgs(args);

            // Apply global flags
            ConsoleOutput.SetVerbose(options.Verbose);
            ConsoleOutput.SetQuiet(options.Quiet);

            // Determine output path — default to font.bmfc
            var outputPath = options.OutputPath ?? "font.bmfc";
            outputPath = outputPath.Trim();
            // If the path has no extension, append the canonical default (.bmfc); BMFont is
            // the default init format. An explicit extension is respected as-is and decides
            // the written format via ConfigFormatFactory (.hiero -> Hiero, anything else ->
            // BMFont). Users opt into Hiero by typing a .hiero extension (e.g. -o myfont.hiero).
            // Trim a lone trailing '.' so "myfont." becomes "myfont.bmfc", not "myfont..bmfc".
            if (!Path.HasExtension(outputPath))
                outputPath = outputPath.TrimEnd('.') + ".bmfc";

            // Write the config file
            BmfcWriter.Write(options, outputPath);

            ConsoleOutput.WriteStdout($"Config written to {outputPath}");

            return ExitCodes.Success;
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

    private static void ShowHelp()
    {
        Console.WriteLine("""
            Generate a .bmfc or .hiero configuration file without rendering a font.

            Usage: kernsmith init [options]

            The init command accepts all the same flags as 'generate' but writes a
            config file instead of producing bitmap font output. You can then edit the
            file and run 'kernsmith generate --config <path>'.

            Font Source:
              -f, --font <path>           Font file path (TTF, OTF, WOFF)
              --system-font <name>        Use a system-installed font by family name

            Output:
              -o, --output <path>         Output config file path (default: font.bmfc).
                                          No extension defaults to .bmfc; an explicit
                                          extension is respected as-is (the format is chosen
                                          from it). Use a .hiero extension for Hiero/libGDX.

            All other generate flags (--size, --charset, --outline, --gradient, etc.)
            are accepted and written into the configuration file.

            Examples:
              kernsmith init --system-font "Rockwell Extra Bold" -s 48 --outline 3,0055AA -o my-font.bmfc
              kernsmith init -f ./fonts/MyFont.ttf -s 32 -c extended -o game-font.bmfc
              kernsmith init -f arial.ttf -s 24 -o my-font.hiero
            """);
    }
}
