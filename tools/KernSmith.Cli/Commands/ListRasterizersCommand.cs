using KernSmith.Cli.Utilities;
using KernSmith.Rasterizer;

namespace KernSmith.Cli.Commands;

/// <summary>
/// Lists available rasterizer backends on the current platform.
/// </summary>
internal static class ListRasterizersCommand
{
    /// <summary>
    /// Prints all rasterizer backends and their availability on this platform.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded from the top-level dispatcher.</param>
    /// <returns>An exit code indicating success or the category of failure.</returns>
    internal static int Execute(string[] args)
    {
        var available = RasterizerFactory.GetAvailableBackends();
        ConsoleOutput.WriteLine("Available rasterizer backends:");
        ConsoleOutput.WriteLine();

        var rows = new List<(string Name, string Platform, string Status, string Capabilities)>
        {
            ("freetype",    "All platforms", available.Contains(RasterizerBackend.FreeType)    ? "(default)"       : "(not available)", "Color, Variable, SDF, Outline"),
            ("gdi",         "Windows only",  available.Contains(RasterizerBackend.Gdi)         ? "(available)"     : "(not available)", "Grayscale only"),
            ("directwrite", "Windows only",  available.Contains(RasterizerBackend.DirectWrite) ? "(available)"     : "(not available)", "Color, Variable"),
        };

        foreach (var (name, platform, status, caps) in rows)
            ConsoleOutput.WriteLine($"  {name,-15} {status,-15} {platform,-20} {caps}");

        return ExitCodes.Success;
    }
}
