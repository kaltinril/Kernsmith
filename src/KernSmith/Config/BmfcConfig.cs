namespace KernSmith;

/// <summary>
/// Holds the result of reading a font-generation configuration file. This is the shared
/// model for both the BMFont/AngelCode <c>.bmfc</c> and libGDX Hiero <c>.hiero</c> formats
/// (see <see cref="ConfigFormatFactory"/>). Combines <see cref="FontGeneratorOptions"/> with
/// font source and output path information that lives outside the options type.
/// </summary>
public sealed class BmfcConfig
{
    /// <summary>Font generation options parsed from the .bmfc file.</summary>
    public FontGeneratorOptions Options { get; set; } = new();

    /// <summary>Path to a font file (TTF/OTF/WOFF), if specified in the config.</summary>
    public string? FontFile { get; set; }

    /// <summary>System font family name, if specified in the config.</summary>
    public string? FontName { get; set; }

    /// <summary>Output file path, if specified in the config.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Output format for the font descriptor file (Text, Xml, or Binary). Default is Text.</summary>
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    /// <summary>
    /// Creates a <see cref="BmfcConfig"/> from existing <see cref="FontGeneratorOptions"/>
    /// with optional font source and output path. Useful for standalone config export
    /// via <see cref="BmfcConfigWriter.Write"/> (BMFont <c>.bmfc</c>),
    /// <see cref="HieroConfigWriter.Write"/> (libGDX Hiero <c>.hiero</c>), or
    /// <see cref="ConfigFormatFactory.WriteConfig"/> (selected by extension) without parsing an existing file.
    /// </summary>
    /// <param name="options">Font generation options to wrap.</param>
    /// <param name="fontFile">Path to the font file, or null.</param>
    /// <param name="fontName">System font family name, or null.</param>
    /// <param name="outputPath">Output path, or null.</param>
    /// <param name="outputFormat">Output format (default <see cref="OutputFormat.Text"/>).</param>
    /// <returns>A new <see cref="BmfcConfig"/> wrapping the given options.</returns>
    public static BmfcConfig FromOptions(
        FontGeneratorOptions options,
        string? fontFile = null,
        string? fontName = null,
        string? outputPath = null,
        OutputFormat outputFormat = OutputFormat.Text)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BmfcConfig
        {
            Options = options,
            FontFile = fontFile,
            FontName = fontName,
            OutputPath = outputPath,
            OutputFormat = outputFormat
        };
    }
}
