using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KernSmith.Atlas;
using KernSmith.Output;

namespace KernSmith.Determinism;

/// <summary>
/// One SHA-256 over everything a <see cref="BmFontResult"/> says about a font: the normalized
/// <c>.fnt</c> text (every metric, char and kerning record) followed by each atlas page's raw
/// pixels. Two hosts that agree on this hash produced byte-identical atlases and metrics.
/// </summary>
/// <remarks>
/// This file is compiled into the Native test project, the Blazor WASM sample and the
/// NativeAOT probe (as linked source), so the hash a browser or an AOT binary reports is
/// comparable with the committed <c>manifest.json</c>. Only the public KernSmith API and the
/// BCL are used. Raw pixels, not PNG bytes: the PNG encoder's deflate output follows the
/// runtime's zlib (.NET 8 and 10 differ), the pixels never do.
/// </remarks>
internal static class DeterminismHash
{
    private const string VersionField = "kernsmith version=\"";

    /// <summary>The hex SHA-256 (lower case) of the result's canonical bytes.</summary>
    public static string Hash(BmFontResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in Parts(result))
            hash.AppendData(part.Bytes);

        return ToHex(hash.GetHashAndReset());
    }

    /// <summary>
    /// Short per-part fingerprints ("fnt=1a2b3c4d page0=…"), so a mismatch report can say
    /// whether the metrics or a page moved; the full hash decides pass or fail.
    /// </summary>
    public static string Describe(BmFontResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var text = new StringBuilder();
        foreach (var part in Parts(result))
        {
            if (text.Length > 0)
                text.Append(' ');
            text.Append(part.Name).Append('=').Append(ToHex(SHA256.HashData(part.Bytes)), 0, 8);
        }

        return text.ToString();
    }

    /// <summary>
    /// The <c>.fnt</c> text with LF line endings and the generator version blanked, so a
    /// version bump or a Windows newline does not count as a difference.
    /// </summary>
    public static string NormalizeFnt(string fnt)
    {
        if (fnt is null)
            throw new ArgumentNullException(nameof(fnt));

        string text = fnt.Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = text.IndexOf(VersionField, StringComparison.Ordinal);
        if (start < 0)
            return text;

        int valueStart = start + VersionField.Length;
        int end = text.IndexOf('"', valueStart);
        return end < 0 ? text : string.Concat(text.AsSpan(0, valueStart), "*", text.AsSpan(end));
    }

    private static IEnumerable<(string Name, byte[] Bytes)> Parts(BmFontResult result)
    {
        yield return ("fnt", Encoding.UTF8.GetBytes(NormalizeFnt(result.FntText)));

        foreach (AtlasPage page in result.Pages)
        {
            var bytes = new byte[20 + page.PixelData.Length];
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0), page.PageIndex);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), page.Width);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), page.Height);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), (int)page.Format);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), page.PixelData.Length);
            page.PixelData.CopyTo(bytes, 20);
            yield return ("page" + page.PageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), bytes);
        }
    }

    private static string ToHex(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            text.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
