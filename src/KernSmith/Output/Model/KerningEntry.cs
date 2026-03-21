namespace KernSmith.Output.Model;

/// <summary>
/// BMFont kerning entry — adjustment between a pair of characters.
/// </summary>
public sealed record KerningEntry(
    int First,
    int Second,
    int Amount);
