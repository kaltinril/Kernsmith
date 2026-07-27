namespace KernSmith.Atlas;

/// <summary>
/// Packs rects from multiple independent glyph sources into a single shared
/// <see cref="IAtlasPacker"/> pass, then splits the resulting placements back out
/// per source so each can feed its own <c>BmFontModel</c> build.
/// </summary>
/// <remarks>
/// <see cref="GlyphRect.Id"/>/<see cref="GlyphPlacement.Id"/> is usually a Unicode codepoint,
/// so two sources (e.g. a primary font and a shadow variant with the same character set) will
/// typically collide on id. This builder mangles each source's ids into a composite id before
/// calling <see cref="IAtlasPacker.Pack"/>, then restores the original id when splitting results
/// back out. The mangling is entirely internal — callers only ever see original ids.
/// </remarks>
public static class AtlasGroupBuilder
{
    /// <summary>Maximum original <see cref="GlyphRect.Id"/> value supported (24-bit mask).</summary>
    private const int MaxOriginalId = 0xFFFFFF;

    /// <summary>Maximum number of sources supported (7-bit source index, keeps composite id non-negative).</summary>
    private const int MaxSourceCount = 0x7F;

    /// <summary>One tagged glyph-rect source to be packed as part of a shared group.</summary>
    /// <param name="Tag">Identifies this source when splitting placements back out.</param>
    /// <param name="Rects">This source's glyph rects, with their original (un-mangled) ids.</param>
    public readonly record struct GroupSource(string Tag, IReadOnlyList<GlyphRect> Rects);

    /// <summary>
    /// Packs all sources' rects into one shared atlas, then splits the placements back out
    /// per source, keyed by each source's own original id.
    /// </summary>
    /// <param name="packer">The packer to use for the single combined pack call.</param>
    /// <param name="sources">The tagged glyph-rect sources to pack together.</param>
    /// <param name="maxWidth">Max page width in pixels.</param>
    /// <param name="maxHeight">Max page height in pixels.</param>
    /// <returns>
    /// The shared <see cref="PackResult"/> from the single combined pack, plus a placement
    /// dictionary per source tag (keyed by that source's original id, with <see cref="GlyphPlacement.Id"/>
    /// restored to the original id).
    /// </returns>
    public static (PackResult Shared, IReadOnlyDictionary<string, IReadOnlyDictionary<int, GlyphPlacement>> PlacementsByTag)
        Pack(IAtlasPacker packer, IReadOnlyList<GroupSource> sources, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(packer);
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count > MaxSourceCount)
            throw new ArgumentOutOfRangeException(nameof(sources),
                sources.Count, $"AtlasGroupBuilder supports at most {MaxSourceCount} sources per group.");

        var combined = new List<GlyphRect>();

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            foreach (var rect in sources[sourceIndex].Rects)
            {
                if (rect.Id < 0 || rect.Id > MaxOriginalId)
                    throw new ArgumentOutOfRangeException(nameof(sources),
                        rect.Id, $"Glyph id {rect.Id} does not fit in the {MaxOriginalId}-value composite-id mask used by AtlasGroupBuilder.");

                var compositeId = MakeCompositeId(sourceIndex, rect.Id);
                combined.Add(rect with { Id = compositeId });
            }
        }

        var shared = packer.Pack(combined, maxWidth, maxHeight);

        var placementsByTag = new Dictionary<string, IReadOnlyDictionary<int, GlyphPlacement>>();
        var perSourceDicts = new Dictionary<int, GlyphPlacement>[sources.Count];
        for (var i = 0; i < sources.Count; i++)
            perSourceDicts[i] = new Dictionary<int, GlyphPlacement>();

        foreach (var placement in shared.Placements)
        {
            var (sourceIndex, originalId) = SplitCompositeId(placement.Id);
            perSourceDicts[sourceIndex][originalId] = placement with { Id = originalId };
        }

        for (var i = 0; i < sources.Count; i++)
            placementsByTag[sources[i].Tag] = perSourceDicts[i];

        return (shared, placementsByTag);
    }

    /// <summary>
    /// Computes the composite id <see cref="Pack"/> uses internally for a given source index
    /// and original glyph id. Exposed so callers that need to render a combined atlas page
    /// directly (matching ids against <see cref="PackResult.Placements"/> on the shared result)
    /// can reproduce the same mangling without redoing the whole pack.
    /// </summary>
    internal static int MakeCompositeId(int sourceIndex, int originalId) => (sourceIndex << 24) | originalId;

    private static (int SourceIndex, int OriginalId) SplitCompositeId(int compositeId) =>
        (compositeId >> 24, compositeId & MaxOriginalId);
}
