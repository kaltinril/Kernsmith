namespace KernSmith.Output.Model;

/// <summary>
/// BMFont common block — metrics shared across all glyphs.
/// </summary>
public sealed record CommonBlock(
    int LineHeight,
    int Base,
    int ScaleW,
    int ScaleH,
    int Pages,
    bool Packed = false,
    int AlphaChnl = 0,
    int RedChnl = 0,
    int GreenChnl = 0,
    int BlueChnl = 0);
