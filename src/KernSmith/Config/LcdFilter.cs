namespace KernSmith;

/// <summary>
/// The low-pass filter an <see cref="AntiAliasMode.Lcd"/> render runs across its one-third-pixel
/// coverage before storing it in the colour channels: stronger filtering trades sharpness for
/// weaker colour fringes. The names follow FreeType's and fontconfig's LCD filter presets. Every
/// filter is symmetric and conserves ink. Only backends that render subpixel output honour it
/// (the Native backend); the others ignore it.
/// </summary>
public enum LcdFilter
{
    /// <summary>
    /// The backend's own choice (default). On the Native backend this is <see cref="Light"/>,
    /// which matches the FreeType backend's LCD output per channel.
    /// </summary>
    Default,

    /// <summary>
    /// A one-pixel box, weights [1, 1, 1] / 3: each subpixel is the coverage of the whole-pixel
    /// window centred on it — the same as three renders shifted by a third of a pixel. Spreads
    /// ink one subpixel past each edge.
    /// </summary>
    Light,

    /// <summary>
    /// A five-tap triangle, weights [1, 2, 3, 2, 1] / 9 — the one-pixel box applied twice, the
    /// classic ClearType-era kernel. Weaker colour fringes than <see cref="Light"/>, slightly
    /// softer stems. Spreads ink two subpixels past each edge.
    /// </summary>
    Smooth,

    /// <summary>
    /// No filtering: each channel is the raw coverage of its one-third-pixel strip. Sharpest,
    /// with the strongest colour fringes.
    /// </summary>
    None,
}
