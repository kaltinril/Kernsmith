using KernSmith.Output.Model;

namespace KernSmith.Output;

/// <summary>
/// The outcome of one <see cref="BmFontIncrementalSession.AddGlyphs(string)"/> call:
/// the glyphs placed (with pixel bytes), the kerning pairs the additions introduced,
/// and any page-geometry change the caller must react to.
/// </summary>
public sealed class GlyphAdditionResult
{
    /// <summary>The glyphs added by this call, each with pixels and a reserved placement.</summary>
    public IReadOnlyList<AddedGlyph> Added { get; }

    /// <summary>
    /// Kerning pairs introduced by this call only (the delta): pairs where at least one
    /// side is a newly added character and the other side is already in the session.
    /// </summary>
    public IReadOnlyList<KerningEntry> NewKerning { get; }

    /// <summary>Requested codepoints the font cannot render (no cmap entry or rasterization failed).</summary>
    public IReadOnlyList<int> FailedCodepoints { get; }

    /// <summary>
    /// Requested codepoints that were already in the session. Re-adds are reported
    /// no-ops, never errors.
    /// </summary>
    public IReadOnlyList<int> AlreadyPresent { get; }

    /// <summary>
    /// True when <see cref="AdditionOverflowPolicy.Grow"/> enlarged the pages during this
    /// call. The caller must reallocate its texture(s) at the new
    /// <see cref="PageWidth"/> x <see cref="PageHeight"/> and copy old contents at (0,0).
    /// </summary>
    public bool PageGrown { get; }

    /// <summary>Current page width in pixels after this call.</summary>
    public int PageWidth { get; }

    /// <summary>Current page height in pixels after this call.</summary>
    public int PageHeight { get; }

    /// <summary>Current page count after this call.</summary>
    public int PageCount { get; }

    internal GlyphAdditionResult(
        IReadOnlyList<AddedGlyph> added,
        IReadOnlyList<KerningEntry> newKerning,
        IReadOnlyList<int> failedCodepoints,
        IReadOnlyList<int> alreadyPresent,
        bool pageGrown,
        int pageWidth,
        int pageHeight,
        int pageCount)
    {
        Added = added;
        NewKerning = newKerning;
        FailedCodepoints = failedCodepoints;
        AlreadyPresent = alreadyPresent;
        PageGrown = pageGrown;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        PageCount = pageCount;
    }
}
