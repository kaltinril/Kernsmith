using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports. As the Phase 161 scaffold only
/// parses font tables (no glyph rasterization yet), every capability is currently
/// <c>false</c> and anti-aliasing is limited to <see cref="AntiAliasMode.None"/> and
/// <see cref="AntiAliasMode.Grayscale"/>. These will expand as outline decoding, scaling,
/// and effects land in later phases.
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => false;
    public bool SupportsSdf => false;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => false;
    public bool SupportsSyntheticItalic => false;

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale];
}
