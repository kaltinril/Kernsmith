namespace Bmfontier;

/// <summary>
/// Specifies what content a single RGBA channel holds in the atlas texture.
/// Values match the BMFont specification for the common block channel fields.
/// </summary>
public enum ChannelContent
{
    /// <summary>Channel holds glyph data (value 0).</summary>
    Glyph = 0,

    /// <summary>Channel holds outline data (value 1).</summary>
    Outline = 1,

    /// <summary>Channel holds combined glyph and outline data (value 2).</summary>
    GlyphAndOutline = 2,

    /// <summary>Channel is always zero (value 3).</summary>
    Zero = 3,

    /// <summary>Channel is always one / 255 (value 4).</summary>
    One = 4
}
