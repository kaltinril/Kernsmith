namespace KernSmith;

/// <summary>
/// How strongly glyph outlines are fitted to the pixel grid before rasterizing. An explicit level
/// decides hinting on its own; <see cref="Default"/> leaves the decision to the backend's
/// existing reading of <see cref="FontGeneratorOptions.EnableHinting"/> and
/// <see cref="FontGeneratorOptions.AntiAlias"/>. A backend that does not list a level in
/// <see cref="Rasterizer.IRasterizerCapabilities.SupportedHintLevels"/> renders the nearest
/// lower level it does list (<see cref="Full"/> → <see cref="Normal"/> → <see cref="Light"/> →
/// <see cref="None"/>), else <see cref="Default"/>; the step down is silent, as for an
/// unsupported <see cref="AntiAliasMode"/>. Distance fields are never hinted at any level.
/// </summary>
public enum HintLevel
{
    /// <summary>
    /// The backend's current behaviour, unchanged: hinted when <see cref="FontGeneratorOptions.EnableHinting"/>
    /// is on, in whatever way the backend hints (FreeType, GDI and DirectWrite hint every mode;
    /// Native hints at <see cref="Normal"/> in every mode, tapered by size to unhinted output
    /// from 40 ppem; StbTrueType never hints). Every existing caller and config renders as the
    /// backend's defaults say with this value.
    /// </summary>
    Default,

    /// <summary>No grid fitting: the scaled outline is rasterized as is.</summary>
    None,

    /// <summary>
    /// Vertical-only fitting: horizontal stems and the baseline, x-height and cap-height zones
    /// snap to whole pixels; advances and horizontal positions stay unhinted. FreeType's
    /// light target; Native's own y-only auto-hinter.
    /// </summary>
    Light,

    /// <summary>
    /// Fitting in both axes by an auto-hinter: vertical stems snap too, with the fitted ink
    /// placed inside an advance that stays the font's. Native's own both-axis auto-hinter, and
    /// its default with hinting on; FreeType does not list it (it would need its forced
    /// auto-hinter) and steps it down to <see cref="Light"/>.
    /// </summary>
    Normal,

    /// <summary>
    /// The font's own hinting where it has it (TrueType bytecode, CFF hints) and the
    /// <see cref="Normal"/> auto-hinter otherwise — the engine selection FreeType makes by
    /// default, so output matches the FreeType backend's default output.
    /// </summary>
    Full
}
