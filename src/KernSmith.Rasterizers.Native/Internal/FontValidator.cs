namespace KernSmith.Rasterizers.Native.Internal;

/// <summary>
/// Validates that a parsed font contains the tables required for rasterization.
/// </summary>
internal static class FontValidator
{
    /// <summary>Tables every supported font must provide.</summary>
    private static readonly string[] RequiredTables =
        ["head", "cmap", "hhea", "hmtx", "maxp", "name", "OS/2", "post"];

    /// <summary>
    /// Verifies that all mandatory SFNT tables are present, plus the outline tables
    /// appropriate for the font's flavour (TrueType <c>glyf</c>/<c>loca</c> or CFF).
    /// </summary>
    /// <exception cref="FontFormatException">If any required table is missing.</exception>
    public static void Validate(TableProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (string tag in RequiredTables)
        {
            if (!provider.HasTable(tag))
                throw new FontFormatException($"Font is missing the required '{tag}' table.");
        }

        if (provider.IsCff)
        {
            if (!provider.HasTable("CFF ") && !provider.HasTable("CFF2"))
                throw new FontFormatException("CFF font is missing both the 'CFF ' and 'CFF2' tables.");
        }
        else
        {
            if (!provider.HasTable("glyf"))
                throw new FontFormatException("TrueType font is missing the required 'glyf' table.");
            if (!provider.HasTable("loca"))
                throw new FontFormatException("TrueType font is missing the required 'loca' table.");
        }
    }
}
