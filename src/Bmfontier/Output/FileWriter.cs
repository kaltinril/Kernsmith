using System.Text;
using Bmfontier.Atlas;
using Bmfontier.Output.Model;

namespace Bmfontier.Output;

/// <summary>
/// Writes BMFont descriptor files and atlas page images to disk.
/// </summary>
internal static class FileWriter
{
    /// <summary>
    /// Writes the .fnt descriptor and atlas page images to the specified output path.
    /// </summary>
    /// <param name="model">The BMFont model to serialize.</param>
    /// <param name="pages">Atlas page bitmaps to encode and write.</param>
    /// <param name="outputPath">Base path without extension (e.g., "output/myfont").</param>
    /// <param name="format">The output format for the .fnt file.</param>
    /// <param name="textFormatter">Formatter for text-based output.</param>
    /// <param name="encoder">Encoder for atlas page images.</param>
    public static void Write(
        BmFontModel model,
        IReadOnlyList<AtlasPage> pages,
        string outputPath,
        OutputFormat format,
        IBmFontTextFormatter textFormatter,
        IAtlasEncoder encoder)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        WriteFntFile(model, outputPath, format, textFormatter);
        WriteAtlasPages(model, pages, outputPath, encoder);
    }

    private static void WriteFntFile(
        BmFontModel model,
        string outputPath,
        OutputFormat format,
        IBmFontTextFormatter textFormatter)
    {
        var fntPath = outputPath + ".fnt";

        switch (format)
        {
            case OutputFormat.Text:
                var text = textFormatter.FormatText(model);
                File.WriteAllText(fntPath, text, Encoding.UTF8);
                break;

            case OutputFormat.Xml:
                var xml = new XmlFormatter().FormatText(model);
                File.WriteAllText(fntPath, xml, Encoding.UTF8);
                break;

            case OutputFormat.Binary:
                var binary = new BmFontBinaryFormatter().FormatBinary(model);
                File.WriteAllBytes(fntPath, binary);
                break;
        }
    }

    private static void WriteAtlasPages(
        BmFontModel model,
        IReadOnlyList<AtlasPage> pages,
        string outputPath,
        IAtlasEncoder encoder)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? ".";
        var fontName = model.Info.Face;

        foreach (var page in pages)
        {
            var fileName = $"{fontName}_{page.PageIndex}{encoder.FileExtension}";
            var filePath = Path.Combine(directory, fileName);
            var encoded = encoder.Encode(page.PixelData, page.Width, page.Height, page.Format);
            File.WriteAllBytes(filePath, encoded);
        }
    }
}
