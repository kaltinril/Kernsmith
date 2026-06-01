namespace KernSmith;

/// <summary>
/// Contract for config-format readers that produce a <see cref="BmfcConfig"/>.
/// The built-in static readers (<see cref="BmfcConfigReader"/>, <see cref="HieroConfigReader"/>)
/// do not implement this interface; it defines the shape for potential future
/// non-static implementations.
/// </summary>
internal interface IConfigReader
{
    /// <summary>Reads a config file from disk and returns a <see cref="BmfcConfig"/>.</summary>
    /// <param name="filePath">Path to the config file.</param>
    BmfcConfig Read(string filePath);

    /// <summary>Parses config content from a string and returns a <see cref="BmfcConfig"/>.</summary>
    /// <param name="content">The config file content.</param>
    BmfcConfig Parse(string content);
}
