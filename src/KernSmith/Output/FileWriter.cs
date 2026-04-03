using System.Text;
using KernSmith.Atlas;
using KernSmith.Output.Model;

namespace KernSmith.Output;

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
                File.WriteAllText(fntPath, text, new UTF8Encoding(false));
                break;

            case OutputFormat.Xml:
                var xml = new XmlFormatter().FormatText(model);
                File.WriteAllText(fntPath, xml, new UTF8Encoding(false));
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
        var baseName = Path.GetFileNameWithoutExtension(outputPath);

        // Encode all pages in parallel
        var encodedPages = new byte[pages.Count][];
        Parallel.For(0, pages.Count, i =>
        {
            var page = pages[i];
            encodedPages[i] = encoder.Encode(page.PixelData, page.Width, page.Height, page.Format);
        });

        // Write sequentially (I/O bound)
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var fileName = $"{baseName}_{page.PageIndex}{encoder.FileExtension}";
            var filePath = Path.Combine(directory, fileName);
            File.WriteAllBytes(filePath, encodedPages[i]);
        }
    }
}
