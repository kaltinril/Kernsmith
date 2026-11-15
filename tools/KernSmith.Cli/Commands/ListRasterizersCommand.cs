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
            // FreeTypeSharp ships no native library for Linux arm64, iOS, Mac Catalyst or WASM.
            [RasterizerBackend.FreeType] = "Windows, Linux x64, macOS, Android",
            [RasterizerBackend.StbTrueType] = "All platforms",
            [RasterizerBackend.Gdi] = "Windows only",
            [RasterizerBackend.DirectWrite] = "Windows only",
            [RasterizerBackend.Native] = "All platforms",
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
                if (caps.SupportsMsdf) capList.Add("MSDF");
                if (caps.SupportsOutlineStroke) capList.Add("Vector Outline");
                if (caps.SupportsSyntheticBold) capList.Add("Synthetic Bold");
                if (caps.SupportsSyntheticItalic) capList.Add("Synthetic Italic");
                if (caps.SupportsWidthScale) capList.Add("Width Scale");
                if (caps.SupportsItalicAngle) capList.Add("Italic Angle");
                if (caps.SupportsSystemFonts) capList.Add("System Fonts");
                var capStr = capList.Count > 0 ? string.Join(", ", capList) : "Grayscale";

                ConsoleOutput.WriteLine($"  {name,-15} {status,-15} {platform,-36} {capStr}");
                ConsoleOutput.WriteLine($"  {"",-15} Anti-alias:  {JoinLower(caps.SupportedAntiAliasModes)}");
                ConsoleOutput.WriteLine($"  {"",-15} Hint levels: {JoinLower(caps.SupportedHintLevels, "default only")}");
            }
            else
            {
                ConsoleOutput.WriteLine($"  {name,-15} {"(not available)",-15} {platform}");
            }
        }

        return ExitCodes.Success;
    }

    private static string JoinLower<T>(IReadOnlyList<T> values, string whenEmpty = "none")
        where T : struct, Enum =>
        values.Count > 0
            ? string.Join(", ", values.Select(value => value.ToString().ToLowerInvariant()))
            : whenEmpty;
}
