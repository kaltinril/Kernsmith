namespace KernSmith.Imaging;

/// <summary>
/// The data is not a PNG this decoder accepts: bad signature, truncated or corrupt chunks, a CRC
/// mismatch, invalid header values, corrupt compressed data, or an image over the
/// <see cref="PngDecodeLimits"/>. Every failure to decode surfaces as this type.
/// </summary>
/// <remarks>
/// Derives from <see cref="FormatException"/> rather than a KernSmith exception so this assembly
/// stays dependency-free (the core library may use it later); callers inside KernSmith wrap it in
/// their own exception types.
/// </remarks>
public sealed class PngFormatException : FormatException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What is wrong with the data.</param>
    public PngFormatException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the underlying error.</summary>
    /// <param name="message">What is wrong with the data.</param>
    /// <param name="inner">The underlying error (for example the inflater's).</param>
    public PngFormatException(string message, Exception inner) : base(message, inner) { }
}
