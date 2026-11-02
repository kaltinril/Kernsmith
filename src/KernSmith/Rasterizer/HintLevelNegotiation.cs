namespace KernSmith.Rasterizer;

/// <summary>
/// Resolves a requested <see cref="HintLevel"/> against what a backend advertises in
/// <see cref="IRasterizerCapabilities.SupportedHintLevels"/>, so no backend is handed a level it
/// does not claim. Degradation is silent, as in <see cref="AntiAliasNegotiation"/>: the caller's
/// options are left untouched and the backend simply receives the effective level.
/// </summary>
internal static class HintLevelNegotiation
{
    /// <summary>The level the backend should render.</summary>
    /// <param name="requested">The caller's level.</param>
    /// <param name="supported">The backend's advertised levels.</param>
    /// <returns>
    /// <paramref name="requested"/> when it is <see cref="HintLevel.Default"/> or advertised.
    /// Otherwise the first advertised level below it, walking <see cref="HintLevel.Full"/> →
    /// <see cref="HintLevel.Normal"/> → <see cref="HintLevel.Light"/> → <see cref="HintLevel.None"/>,
    /// and <see cref="HintLevel.Default"/> when none is — the backend's own behaviour, as before
    /// levels existed.
    /// </returns>
    public static HintLevel Resolve(HintLevel requested, IReadOnlyList<HintLevel> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);

        for (var level = requested; level != HintLevel.Default; level--)
        {
            if (supported.Contains(level))
                return level;
        }

        return HintLevel.Default;
    }
}
