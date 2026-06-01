namespace KernSmith;

/// <summary>
/// Dispatches config read/write operations to the correct format implementation.
/// <see cref="ReadConfig"/> auto-detects the format by inspecting the file CONTENT,
/// falling back to the extension only when the content is inconclusive.
/// <see cref="WriteConfig"/> selects by extension (<c>.hiero</c> selects Hiero;
/// any other extension, including none, selects BMFont).
/// </summary>
public static class ConfigFormatFactory
{
    /// <summary>
    /// Reads a config file, auto-detecting the format from its CONTENT. A definitive or
    /// majority Hiero signal selects the Hiero format; a majority BMFont signal selects BMFont.
    /// When the content is inconclusive, the extension is used as a tiebreaker: <c>.hiero</c>
    /// selects Hiero and any other extension (including none) selects BMFont. This means a
    /// mistyped or mismatched extension is still parsed according to the actual file contents.
    /// </summary>
    /// <param name="filePath">Path to a config file.</param>
    /// <returns>A <see cref="BmfcConfig"/> containing parsed options and font source info.</returns>
    /// <exception cref="FileNotFoundException">Thrown when <paramref name="filePath"/> does not exist.</exception>
    public static BmfcConfig ReadConfig(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        // Throw a uniform, friendly FileNotFoundException (matching the prior reader behavior and
        // the documented exception contract) before reading, so a missing directory does not
        // surface as a DirectoryNotFoundException from File.ReadAllText.
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Config file not found: {filePath}", filePath);

        // Read once to sniff the format. File.ReadAllText strips a UTF-8 BOM.
        var content = File.ReadAllText(filePath);
        var detected = ConfigFormatDetector.DetectFromContent(content);

        // Fall back to the extension only when the content is inconclusive.
        if (detected == DetectedConfigFormat.Unknown)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            detected = ext == ".hiero" ? DetectedConfigFormat.Hiero : DetectedConfigFormat.Bmfc;
        }

        // Call Read(filePath) (not Parse(content)) so relative font/output path resolution
        // against the config directory is preserved. The re-read of a tiny config is acceptable.
        return detected == DetectedConfigFormat.Hiero
            ? HieroConfigReader.Read(filePath)
            : BmfcConfigReader.Read(filePath);
    }

    /// <summary>
    /// Writes a config file, auto-detecting the format from its extension. The <c>.hiero</c>
    /// extension selects the Hiero format; any other extension (including none) is written as BMFont.
    /// </summary>
    /// <param name="config">The configuration to serialize.</param>
    /// <param name="filePath">Path to the destination file.</param>
    public static void WriteConfig(BmfcConfig config, string filePath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(filePath);
        // Trim the extension so a trailing space (e.g. "font.hiero ") still matches ".hiero" and
        // is not written as BMFont content into a Hiero-named file.
        var ext = Path.GetExtension(filePath).ToLowerInvariant().Trim();
        if (ext == ".hiero")
        {
            HieroConfigWriter.WriteToFile(config, filePath);
        }
        else
        {
            BmfcConfigWriter.WriteToFile(config, filePath);
        }
    }
}
