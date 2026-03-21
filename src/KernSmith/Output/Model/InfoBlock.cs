namespace KernSmith.Output.Model;

/// <summary>
/// BMFont info block — font metadata and generation settings.
/// </summary>
public sealed record InfoBlock(
    string Face,
    int Size,
    bool Bold,
    bool Italic,
    bool Unicode,
    bool Smooth,
    bool FixedHeight,
    int StretchH,
    string Charset,
    int Aa,
    Padding Padding,
    Spacing Spacing)
{
    /// <summary>Horizontal stretch percentage.</summary>
    public int StretchH { get; init; } = StretchH is 0 ? 100 : StretchH;

    /// <summary>Character set string.</summary>
    public string Charset { get; init; } = Charset ?? "";

    /// <summary>Anti-aliasing level.</summary>
    public int Aa { get; init; } = Aa is 0 ? 1 : Aa;
}
