namespace KernSmith.Output.Model;

/// <summary>
/// BMFont page entry — maps a page id to an atlas image filename.
/// </summary>
public sealed record PageEntry(
    int Id,
    string File);
