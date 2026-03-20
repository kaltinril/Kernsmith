namespace Bmfontier.Cli.Utilities;

/// <summary>
/// Parses hex color strings into RGB byte tuples.
/// </summary>
internal static class ColorParser
{
    public static (byte R, byte G, byte B) Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException("Color string cannot be empty.");

        var s = hex.TrimStart('#');

        if (s.Length == 3)
        {
            // Expand shorthand: F00 -> FF0000
            s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
        }

        if (s.Length == 8)
        {
            // Strip alpha channel
            s = s[..6];
        }

        if (s.Length != 6)
            throw new ArgumentException(
                $"Invalid color '{hex}': expected a hex color (e.g., FF0000, F00, #FF0000)");

        try
        {
            var r = Convert.ToByte(s[..2], 16);
            var g = Convert.ToByte(s[2..4], 16);
            var b = Convert.ToByte(s[4..6], 16);
            return (r, g, b);
        }
        catch (FormatException)
        {
            throw new ArgumentException(
                $"Invalid color '{hex}': expected a hex color (e.g., FF0000, F00, #FF0000)");
        }
    }
}
