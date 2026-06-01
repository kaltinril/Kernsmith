namespace KernSmith;

/// <summary>
/// Contract for config-format writers that serialize a <see cref="BmfcConfig"/>.
/// The built-in static writers (<see cref="BmfcConfigWriter"/>, <see cref="HieroConfigWriter"/>)
/// do not implement this interface; it defines the shape for potential future
/// non-static implementations.
/// </summary>
internal interface IConfigWriter
{
    /// <summary>Serializes a <see cref="BmfcConfig"/> to a string.</summary>
    /// <param name="config">The configuration to serialize.</param>
    string Write(BmfcConfig config);

    /// <summary>Serializes a <see cref="BmfcConfig"/> to a file on disk.</summary>
    /// <param name="config">The configuration to serialize.</param>
    /// <param name="filePath">The destination file path.</param>
    void WriteToFile(BmfcConfig config, string filePath);
}
