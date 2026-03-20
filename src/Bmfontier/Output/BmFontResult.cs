using Bmfontier.Atlas;
using Bmfontier.Output.Model;

namespace Bmfontier.Output;

/// <summary>
/// The result of a BMFont generation pipeline, containing the font model and atlas pages.
/// </summary>
public sealed class BmFontResult
{
    /// <summary>The BMFont descriptor model.</summary>
    public BmFontModel Model { get; }

    /// <summary>The rendered atlas pages.</summary>
    public IReadOnlyList<AtlasPage> Pages { get; }

    /// <summary>
    /// Codepoints that were requested but could not be rasterized (missing from the font).
    /// </summary>
    public IReadOnlyList<int> FailedCodepoints { get; }

    internal BmFontResult(BmFontModel model, IReadOnlyList<AtlasPage> pages, IReadOnlyList<int>? failedCodepoints = null)
    {
        Model = model;
        Pages = pages;
        FailedCodepoints = failedCodepoints ?? Array.Empty<int>();
    }

    /// <summary>
    /// Returns the BMFont descriptor in text format.
    /// </summary>
    public override string ToString() => new TextFormatter().FormatText(Model);

    /// <summary>
    /// Returns the BMFont descriptor in XML format.
    /// </summary>
    public string ToXml() => new XmlFormatter().FormatText(Model);

    /// <summary>
    /// Returns the BMFont descriptor in binary format.
    /// </summary>
    public byte[] ToBinary() => new BmFontBinaryFormatter().FormatBinary(Model);

    /// <summary>
    /// Writes the BMFont descriptor and atlas page images to disk.
    /// </summary>
    /// <param name="outputPath">Base path without extension (e.g., "output/myfont").</param>
    /// <param name="format">The output format for the .fnt file.</param>
    public void ToFile(string outputPath, OutputFormat format = OutputFormat.Text)
    {
        var textFormatter = new TextFormatter();
        var encoder = new StbPngEncoder();
        FileWriter.Write(Model, Pages, outputPath, format, textFormatter, encoder);
    }
}
