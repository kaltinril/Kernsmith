namespace KernSmith;

/// <summary>
/// The config format inferred from a file's textual content.
/// </summary>
internal enum DetectedConfigFormat
{
    /// <summary>The content could not be classified; the caller should fall back to the extension.</summary>
    Unknown,

    /// <summary>BMFont (<c>.bmfc</c>) flat key=value format (camelCase, non-dotted keys).</summary>
    Bmfc,

    /// <summary>Hiero (<c>.hiero</c>, libGDX) flat key=value format (dotted keys, effect blocks).</summary>
    Hiero,
}

/// <summary>
/// Classifies config text as BMFont or Hiero by inspecting its content rather than its
/// file extension. This lets a mistyped or mismatched extension (for example a real Hiero
/// file named <c>.bmfc</c>/<c>.txt</c>, or a typo'd <c>.heiro</c>) still be parsed correctly.
/// </summary>
/// <remarks>
/// The implementation is allocation-light, culture-invariant (ordinal comparisons only),
/// and uses no reflection, so it is safe under Native AOT and trimming.
/// </remarks>
internal static class ConfigFormatDetector
{
    // Known Hiero dotted-key prefixes (REF-10). A key containing a '.' whose prefix matches
    // one of these is a strong Hiero signal.
    private static readonly string[] HieroKeyPrefixes =
    {
        "font.",
        "glyph.",
        "pad.",
        "effect.",
        "font2.",
    };

    // Known BMFont camelCase keys with NO dot (.bmfc, REF). Matching one of these exactly is a
    // BMFont signal. Distinguished from Hiero's dotted equivalents (e.g. "fontSize" vs "font.size").
    private static readonly string[] BmfcKeys =
    {
        "fileVersion",
        "fontFile",
        "fontName",
        "fontSize",
        "charSet",
        "chars",
        "outWidth",
        "outHeight",
        "fontDescFormat",
        "textureFormat",
        "outBitDepth",
        "outputPath",
        "paddingUp",
        "paddingDown",
        "paddingRight",
        "paddingLeft",
        "spacingHoriz",
        "spacingVert",
        "outlineThickness",
    };

    /// <summary>
    /// Classifies config <paramref name="content"/> as <see cref="DetectedConfigFormat.Bmfc"/>,
    /// <see cref="DetectedConfigFormat.Hiero"/>, or <see cref="DetectedConfigFormat.Unknown"/>.
    /// </summary>
    /// <param name="content">The raw config file text.</param>
    /// <returns>
    /// The detected format. <see cref="DetectedConfigFormat.Unknown"/> is returned when the
    /// content carries no decisive signals (or an equal number for each format), in which case
    /// the caller should fall back to the file extension.
    /// </returns>
    internal static DetectedConfigFormat DetectFromContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return DetectedConfigFormat.Unknown;

        int hieroSignals = 0;
        int bmfcSignals = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();

            // Ignore blank lines and comments. The AngelCode header comment is itself a BMFont signal.
            if (line.Length == 0)
                continue;
            if (line[0] == '#')
            {
                if (line.Contains("AngelCode Bitmap Font Generator", StringComparison.Ordinal))
                    bmfcSignals++;
                continue;
            }

            // Hiero's render_type is a non-dotted key but is a distinctive Hiero signal.
            if (line.StartsWith("render_type=", StringComparison.Ordinal))
            {
                hieroSignals++;
                continue;
            }

            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
                continue;

            var key = line[..eqIndex].Trim();
            if (key.Length == 0)
                continue;

            // Definitive: the libGDX effect class namespace only ever appears as the VALUE of a
            // Hiero "effect.class=" line. Scoping the check to that line avoids misclassifying a
            // legitimate BMFont file whose path value happens to contain "com.badlogic.gdx"
            // (for example outputPath into a libGDX project tree).
            if (line.StartsWith("effect.class=", StringComparison.Ordinal)
                && line.Contains("com.badlogic.gdx", StringComparison.Ordinal))
            {
                return DetectedConfigFormat.Hiero;
            }

            // Hiero keys are dotted (e.g. "font.size"); BMFont keys are camelCase without dots
            // (e.g. "fontSize"). Use the presence of a '.' to disambiguate before matching.
            if (key.IndexOf('.') >= 0)
            {
                if (StartsWithKnownHieroPrefix(key))
                    hieroSignals++;
            }
            else if (IsKnownBmfcKey(key))
            {
                bmfcSignals++;
            }
        }

        if (hieroSignals > bmfcSignals)
            return DetectedConfigFormat.Hiero;
        if (bmfcSignals > hieroSignals)
            return DetectedConfigFormat.Bmfc;
        return DetectedConfigFormat.Unknown;
    }

    private static bool StartsWithKnownHieroPrefix(string key)
    {
        foreach (var prefix in HieroKeyPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsKnownBmfcKey(string key)
    {
        foreach (var known in BmfcKeys)
        {
            if (string.Equals(key, known, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
