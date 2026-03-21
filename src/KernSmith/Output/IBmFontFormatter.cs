using KernSmith.Output.Model;

namespace KernSmith.Output;

/// <summary>
/// Base interface for BMFont descriptor formatters.
/// </summary>
public interface IBmFontFormatter
{
    /// <summary>
    /// Format identifier: "text", "xml", or "binary".
    /// </summary>
    string Format { get; }
}

/// <summary>
/// Writes a <see cref="BmFontModel"/> as a text-format .fnt file.
/// </summary>
public interface IBmFontTextFormatter : IBmFontFormatter
{
    /// <summary>
    /// Converts the font model to BMFont text format.
    /// </summary>
    /// <param name="model">The font model to write.</param>
    /// <returns>The .fnt file content as a string.</returns>
    string FormatText(BmFontModel model);
}

/// <summary>
/// Writes a <see cref="BmFontModel"/> as a binary-format .fnt file.
/// </summary>
public interface IBmFontBinaryFormatter : IBmFontFormatter
{
    /// <summary>
    /// Converts the font model to BMFont binary format.
    /// </summary>
    /// <param name="model">The font model to write.</param>
    /// <returns>The .fnt file content as bytes.</returns>
    byte[] FormatBinary(BmFontModel model);
}
