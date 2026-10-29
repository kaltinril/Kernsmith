namespace KernSmith.Rasterizer;

/// <summary>
/// Resolves a requested <see cref="AntiAliasMode"/> against what a backend advertises in
/// <see cref="IRasterizerCapabilities.SupportedAntiAliasModes"/>, so no backend is handed a mode
/// it does not claim. Degradation is silent: the caller's options are left untouched and the
/// backend simply receives the effective mode.
/// </summary>
/// <remarks>
/// INVARIANT: the result is <see cref="AntiAliasMode.None"/> exactly when the request is. The
/// <c>.fnt</c> <c>smooth</c> flag and the outline effect's smoothing test
/// <c>AntiAlias != None</c> on the caller's options, and stay truthful only because negotiation
/// never crosses that line.
/// </remarks>
internal static class AntiAliasNegotiation
{
    /// <summary>The mode the backend should render.</summary>
    /// <param name="requested">The caller's mode.</param>
    /// <param name="supported">The backend's advertised modes.</param>
    /// <returns>
    /// <paramref name="requested"/> when advertised, or when it is <see cref="AntiAliasMode.None"/>
    /// (every backend can threshold). Otherwise <see cref="AntiAliasMode.Grayscale"/> when that is
    /// advertised — <c>Light</c> and <c>Lcd</c> both fall back to it. A backend advertising
    /// neither gets the request unchanged, as before negotiation existed.
    /// </returns>
    public static AntiAliasMode Resolve(AntiAliasMode requested, IReadOnlyList<AntiAliasMode> supported)
    {
        ArgumentNullException.ThrowIfNull(supported);

        if (requested == AntiAliasMode.None || supported.Contains(requested))
            return requested;

        return supported.Contains(AntiAliasMode.Grayscale) ? AntiAliasMode.Grayscale : requested;
    }
}
