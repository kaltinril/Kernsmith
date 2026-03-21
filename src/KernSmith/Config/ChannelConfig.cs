namespace KernSmith;

/// <summary>
/// Configures what each RGBA channel contains in the atlas texture.
/// Matches BMFont.exe's per-channel export settings.
/// </summary>
public sealed record ChannelConfig(
    ChannelContent Alpha = ChannelContent.Glyph,
    ChannelContent Red = ChannelContent.Glyph,
    ChannelContent Green = ChannelContent.Glyph,
    ChannelContent Blue = ChannelContent.Glyph,
    bool InvertAlpha = false,
    bool InvertRed = false,
    bool InvertGreen = false,
    bool InvertBlue = false)
{
    /// <summary>
    /// True when all channels hold glyph data with no inversion (the default configuration).
    /// </summary>
    public bool IsDefault =>
        Alpha == ChannelContent.Glyph &&
        Red == ChannelContent.Glyph &&
        Green == ChannelContent.Glyph &&
        Blue == ChannelContent.Glyph &&
        !InvertAlpha && !InvertRed && !InvertGreen && !InvertBlue;
}
