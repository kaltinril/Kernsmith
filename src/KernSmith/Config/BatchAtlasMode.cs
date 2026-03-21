namespace KernSmith;

/// <summary>
/// Controls how atlas textures are generated during batch font generation.
/// </summary>
public enum BatchAtlasMode
{
    /// <summary>Each font gets its own independent atlas texture(s).</summary>
    Separate,

    /// <summary>All fonts share a single combined atlas texture.</summary>
    Combined
}
