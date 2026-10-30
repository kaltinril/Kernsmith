namespace KernSmith.Ui.Services;

/// <summary>
/// Parses the <c>--ui-rasterizer &lt;backend&gt;</c> launch argument, which selects the backend
/// the app's own Gum text is rendered with (the Gum integration's <c>KernSmithFontCreator</c>).
/// Independent of the Rasterizer dropdown, which drives the font being generated.
/// </summary>
public static class UiFontRasterizer
{
    /// <summary>The launch argument name.</summary>
    public const string Argument = "--ui-rasterizer";

    /// <summary>
    /// Returns the requested backend, or null when the argument is absent (keep the theme's default).
    /// </summary>
    /// <exception cref="ArgumentException">The value is missing or not a backend name.</exception>
    public static RasterizerBackend? Parse(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], Argument, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Count)
                throw new ArgumentException($"{Argument} needs a backend name (e.g. {Argument} native).");

            var value = args[i + 1];
            // Enum.TryParse also accepts numbers; only names are meaningful here.
            if (!value.All(char.IsLetter)
                || !Enum.TryParse<RasterizerBackend>(value, ignoreCase: true, out var backend))
            {
                var names = string.Join(", ", Enum.GetNames<RasterizerBackend>().Select(n => n.ToLowerInvariant()));
                throw new ArgumentException($"Unknown {Argument} value '{value}'. Use one of: {names}.");
            }

            return backend;
        }

        return null;
    }
}
