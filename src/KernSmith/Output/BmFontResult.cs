using KernSmith.Atlas;
using KernSmith.Output.Model;

namespace KernSmith.Output;

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

    /// <summary>
    /// Pipeline timing metrics. Only populated when <see cref="FontGeneratorOptions.CollectMetrics"/> is enabled.
    /// </summary>
    public PipelineMetrics? Metrics { get; }

    /// <summary>The generation options used to produce this result, if available.</summary>
    internal FontGeneratorOptions? SourceOptions { get; }

    /// <summary>The font file path used to produce this result, if available.</summary>
    internal string? SourceFontFile { get; }

    /// <summary>The system font family name used to produce this result, if available.</summary>
    internal string? SourceFontName { get; }

    /// <summary>
    /// Additional character-set renderings requested via <see cref="FontGeneratorOptions.Variants"/>,
    /// keyed by <see cref="AtlasVariant.Name"/>. Empty when no variants were requested.
    /// </summary>
    public IReadOnlyDictionary<string, BmFontModel> VariantModels { get; }

    /// <summary>Atlas pages for each entry in <see cref="VariantModels"/>, keyed the same way.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AtlasPage>> VariantPages { get; }

    private readonly Lazy<string> _fntText;
    private readonly Lazy<string> _fntXml;
    private readonly Lazy<byte[]> _fntBinary;

    internal BmFontResult(
        BmFontModel model,
        IReadOnlyList<AtlasPage> pages,
        IReadOnlyList<int>? failedCodepoints = null,
        PipelineMetrics? metrics = null,
        FontGeneratorOptions? sourceOptions = null,
        string? sourceFontFile = null,
        string? sourceFontName = null,
        IReadOnlyDictionary<string, BmFontModel>? variantModels = null,
        IReadOnlyDictionary<string, IReadOnlyList<AtlasPage>>? variantPages = null)
    {
        Model = model;
        Pages = pages;
        FailedCodepoints = failedCodepoints ?? Array.Empty<int>();
        Metrics = metrics;
        SourceOptions = sourceOptions;
        SourceFontFile = sourceFontFile;
        SourceFontName = sourceFontName;
        VariantModels = variantModels ?? new Dictionary<string, BmFontModel>();
        VariantPages = variantPages ?? new Dictionary<string, IReadOnlyList<AtlasPage>>();

        _fntText = new Lazy<string>(() => new TextFormatter().FormatText(Model));
        _fntXml = new Lazy<string>(() => new XmlFormatter().FormatText(Model));
        _fntBinary = new Lazy<byte[]>(() => new BmFontBinaryFormatter().FormatBinary(Model));
    }

    /// <summary>
    /// Returns the BMFont descriptor in text format.
    /// </summary>
    public override string ToString() => FntText;

    /// <summary>
    /// Returns the BMFont descriptor in XML format.
    /// </summary>
    public string ToXml() => FntXml;

    /// <summary>
    /// Returns the BMFont descriptor in binary format.
    /// </summary>
    public byte[] ToBinary() => (byte[])FntBinary.Clone();

    /// <summary>
    /// Returns the BMFont descriptor in text format.
    /// </summary>
    public string FntText => _fntText.Value;

    /// <summary>
    /// Returns the BMFont descriptor in XML format.
    /// </summary>
    public string FntXml => _fntXml.Value;

    /// <summary>
    /// Returns the BMFont descriptor in binary format.
    /// </summary>
    public byte[] FntBinary => _fntBinary.Value;

    /// <summary>
    /// Encodes all atlas pages as PNG byte arrays.
    /// </summary>
    /// <returns>An array of PNG-encoded byte arrays, one per page.</returns>
    public byte[][] GetPngData()
    {
        var encoder = new StbPngEncoder();
        return EncodeAllPages(encoder);
    }

    /// <summary>
    /// Encodes a single atlas page as PNG bytes.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>PNG-encoded byte array for the specified page.</returns>
    public byte[] GetPngData(int pageIndex)
    {
        var encoder = new StbPngEncoder();
        return EncodePage(encoder, pageIndex);
    }

    /// <summary>
    /// Encodes all atlas pages as TGA byte arrays.
    /// </summary>
    /// <returns>An array of TGA-encoded byte arrays, one per page.</returns>
    public byte[][] GetTgaData()
    {
        var encoder = new TgaEncoder();
        return EncodeAllPages(encoder);
    }

    /// <summary>
    /// Encodes a single atlas page as TGA bytes.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>TGA-encoded byte array for the specified page.</returns>
    public byte[] GetTgaData(int pageIndex)
    {
        var encoder = new TgaEncoder();
        return EncodePage(encoder, pageIndex);
    }

    /// <summary>
    /// Encodes all atlas pages as DDS byte arrays.
    /// </summary>
    /// <returns>An array of DDS-encoded byte arrays, one per page.</returns>
    public byte[][] GetDdsData()
    {
        var encoder = new DdsEncoder();
        return EncodeAllPages(encoder);
    }

    /// <summary>
    /// Encodes a single atlas page as DDS bytes.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>DDS-encoded byte array for the specified page.</returns>
    public byte[] GetDdsData(int pageIndex)
    {
        var encoder = new DdsEncoder();
        return EncodePage(encoder, pageIndex);
    }

    /// <summary>
    /// Serializes this result's source options to a BMFont/AngelCode <c>.bmfc</c> config string.
    /// Requires that this result was produced by a generation call (not loaded from disk).
    /// See <see cref="ToHiero"/> for the libGDX Hiero equivalent.
    /// </summary>
    /// <returns>The <c>.bmfc</c> config file content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no source options are available.</exception>
    public string ToBmfc()
    {
        if (SourceOptions is null)
            throw new InvalidOperationException(
                "Cannot generate .bmfc content: this result was not produced by a generation call, " +
                "so no source options are available.");

        var config = new BmfcConfig
        {
            Options = SourceOptions,
            FontFile = SourceFontFile,
            FontName = SourceFontName,
        };

        return BmfcConfigWriter.Write(config);
    }

    /// <summary>
    /// Serializes this result's source options to a libGDX Hiero <c>.hiero</c> config string.
    /// Requires that this result was produced by a generation call (not loaded from disk).
    /// See <see cref="ToBmfc"/> for the BMFont/AngelCode equivalent.
    /// </summary>
    /// <returns>The <c>.hiero</c> config file content.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no source options are available.</exception>
    public string ToHiero()
    {
        if (SourceOptions is null)
            throw new InvalidOperationException(
                "Cannot generate .hiero content: this result was not produced by a generation call, " +
                "so no source options are available.");

        var config = new BmfcConfig
        {
            Options = SourceOptions,
            FontFile = SourceFontFile,
            FontName = SourceFontName,
        };

        return HieroConfigWriter.Write(config);
    }

    /// <summary>
    /// Writes the BMFont descriptor and atlas page images to disk.
    /// </summary>
    /// <param name="outputPath">Base path without extension (e.g., "output/myfont").</param>
    /// <param name="format">The output format for the .fnt file.</param>
    public void ToFile(string outputPath, OutputFormat format = OutputFormat.Text)
    {
        var textFormatter = new TextFormatter();
        var encoder = new StbPngEncoder();

        // Rebuild page entries to match the output base name
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        var fixedPages = new List<PageEntry>();
        for (int i = 0; i < Model.Pages.Count; i++)
        {
            var ext = Path.GetExtension(Model.Pages[i].File);
            fixedPages.Add(new PageEntry(Model.Pages[i].Id, $"{baseName}_{i}{ext}"));
        }
        var fixedModel = new BmFontModel
        {
            Info = Model.Info,
            Common = Model.Common,
            Pages = fixedPages,
            Characters = Model.Characters,
            KerningPairs = Model.KerningPairs,
            Extended = Model.Extended
        };

        FileWriter.Write(fixedModel, Pages, outputPath, format, textFormatter, encoder);

        // Write each variant as its own .fnt + atlas image, named "<outputPath>-<variantName>".
        foreach (var (variantName, variantModel) in VariantModels)
        {
            if (!VariantPages.TryGetValue(variantName, out var variantPages))
                continue;

            var variantOutputPath = $"{outputPath}-{variantName}";
            var variantBaseName = Path.GetFileNameWithoutExtension(variantOutputPath);
            var variantFixedPages = new List<PageEntry>();
            for (int i = 0; i < variantModel.Pages.Count; i++)
            {
                var ext = Path.GetExtension(variantModel.Pages[i].File);
                variantFixedPages.Add(new PageEntry(variantModel.Pages[i].Id, $"{variantBaseName}_{i}{ext}"));
            }
            var variantFixedModel = new BmFontModel
            {
                Info = variantModel.Info,
                Common = variantModel.Common,
                Pages = variantFixedPages,
                Characters = variantModel.Characters,
                KerningPairs = variantModel.KerningPairs,
                Extended = variantModel.Extended
            };

            FileWriter.Write(variantFixedModel, variantPages, variantOutputPath, format, textFormatter, encoder);
        }

        // Write .bmfc file if source options are available
        if (SourceOptions is not null)
        {
            var bmfcPath = outputPath + ".bmfc";
            var config = new BmfcConfig
            {
                Options = SourceOptions,
                FontFile = SourceFontFile,
                FontName = SourceFontName,
                OutputPath = outputPath,
                OutputFormat = format,
            };
            BmfcConfigWriter.WriteToFile(config, bmfcPath);
        }
    }

    private byte[][] EncodeAllPages(IAtlasEncoder encoder)
    {
        var result = new byte[Pages.Count][];

        void EncodePage(int i)
        {
            var page = Pages[i];
            result[i] = encoder.Encode(page.PixelData, page.Width, page.Height, page.Format);
        }

        if (OperatingSystem.IsBrowser())
        {
            for (var i = 0; i < Pages.Count; i++)
                EncodePage(i);
        }
        else
        {
            Parallel.For(0, Pages.Count, EncodePage);
        }

        return result;
    }

    private byte[] EncodePage(IAtlasEncoder encoder, int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex),
                $"Page index {pageIndex} is out of range. This result has {Pages.Count} page(s).");

        var page = Pages[pageIndex];
        return encoder.Encode(page.PixelData, page.Width, page.Height, page.Format);
    }
}
