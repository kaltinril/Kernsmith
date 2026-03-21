namespace KernSmith;

/// <summary>
/// Holds the result of reading a .bmfc configuration file.
/// Combines <see cref="FontGeneratorOptions"/> with font source and output path
/// information that lives outside the options type.
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
}
