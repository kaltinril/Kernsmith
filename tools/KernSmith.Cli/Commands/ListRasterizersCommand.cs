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

        var platformMap = new Dictionary<RasterizerBackend, string>
        {
            [RasterizerBackend.FreeType] = "All platforms",
            [RasterizerBackend.Gdi] = "Windows only",
            [RasterizerBackend.DirectWrite] = "Windows only",
        };

        foreach (var backend in Enum.GetValues<RasterizerBackend>())
        {
            var name = backend.ToString().ToLowerInvariant();
            var platform = platformMap.GetValueOrDefault(backend, "Unknown");

            if (available.Contains(backend))
            {
                var status = backend == RasterizerBackend.FreeType ? "(default)" : "(available)";

                using var rasterizer = RasterizerFactory.Create(backend);
                var caps = rasterizer.Capabilities;
                var capList = new List<string>();
                if (caps.SupportsColorFonts) capList.Add("Color");
                if (caps.SupportsVariableFonts) capList.Add("Variable");
                if (caps.SupportsSdf) capList.Add("SDF");
                if (caps.SupportsOutlineStroke) capList.Add("Outline");
                if (caps.SupportsSystemFonts) capList.Add("System Fonts");
                var capStr = capList.Count > 0 ? string.Join(", ", capList) : "Grayscale";

                ConsoleOutput.WriteLine($"  {name,-15} {status,-15} {platform,-20} {capStr}");
            }
            else
            {
                ConsoleOutput.WriteLine($"  {name,-15} {"(not available)",-15} {platform,-20}");
            }
        }

        return ExitCodes.Success;
    }
}
