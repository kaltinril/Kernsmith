using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// A live glyph-addition session (phase 183): the font is parsed once (unfiltered, so any
/// character the font contains stays addable), the rasterizer stays loaded, and the MaxRects
/// free-rectangle state is held between calls — so each <see cref="AddGlyphs(string)"/> costs
/// work proportional to the glyphs being added, never to the font or the atlas. Glyphs that
/// are already placed never move ("stable packing"): the caller can blit
/// <see cref="AddedGlyph"/> bytes straight into a live GPU texture.
/// </summary>
/// <remarks>
/// <para><b>Not thread-safe.</b> All members must be called from one thread at a time.</para>
/// <para>Created via <see cref="BmFont.BeginIncremental"/> (empty session; the first
/// <see cref="AddGlyphs(string)"/> sizes the atlas exactly as
/// <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/>
/// would) or <see cref="BmFont.ResumeIncremental"/> (occupancy recovered from an existing
/// <see cref="BmFontModel"/>).</para>
/// <para>Additions always pack with MaxRects. A <see cref="PackingAlgorithm.Skyline"/>
/// configuration — or a custom <see cref="FontGeneratorOptions.Packer"/> — falls back to
/// MaxRects for additions: arbitrary packer state cannot be reconstructed from occupied
/// rectangles, but existing placements stay valid; only the placement strategy for new
/// glyphs differs.</para>
/// </remarks>
public sealed class BmFontIncrementalSession : IDisposable
{
    /// <summary>A kerning pair touching the indexed codepoint. Amount is pre-scaled pixels
    /// when the index was built from rasterizer pairs, else raw font design units.</summary>
    private readonly record struct IndexedKerningPair(int Left, int Right, int Amount);

    private readonly PreparedRasterization _prepared;
    private readonly FontGeneratorOptions _options;
    private readonly AdditionOverflowPolicy _overflowPolicy;
    private readonly HashSet<int> _availableCodepoints;
    private readonly HashSet<int> _present = new();
    private readonly List<CharEntry> _chars = new();
    private readonly List<KerningEntry> _kerning = new();
    private readonly int _advanceAdjustX;

    // Resume-only: reused verbatim in CurrentModel so a resumed session keeps referring
    // to the caller's existing page files and info block.
    private readonly InfoBlock? _existingInfo;
    private readonly IReadOnlyList<PageEntry>? _existingPages;

    // Kerning index: codepoint -> pairs touching it, built once on the first rasterization
    // (which is when rasterizer-provided pair availability is known).
    private Dictionary<int, List<IndexedKerningPair>>? _kerningIndex;
    private bool _kerningPreScaled;

    private MaxRectsState? _packState;
    private int? _equalizeTarget;
    private (int LineHeight, int BaseLine)? _lineMetrics;
    private bool _fallbackResolved;
    private BmFontModel? _cachedModel;
    private bool _disposed;

    private BmFontIncrementalSession(
        PreparedRasterization prepared,
        FontGeneratorOptions options,
        AdditionOverflowPolicy overflowPolicy,
        InfoBlock? existingInfo,
        IReadOnlyList<PageEntry>? existingPages)
    {
        _prepared = prepared;
        _options = options;
        _overflowPolicy = overflowPolicy;
        _existingInfo = existingInfo;
        _existingPages = existingPages;
        _availableCodepoints = new HashSet<int>(prepared.FontInfo.AvailableCodepoints);
        _advanceAdjustX = (int)Math.Round(options.AdvanceAdjustX, MidpointRounding.AwayFromZero);
    }

    /// <summary>Current page width in pixels; 0 before the first <see cref="AddGlyphs(string)"/> of a begin-session.</summary>
    public int PageWidth => _packState?.PageWidth ?? 0;

    /// <summary>Current page height in pixels; 0 before the first <see cref="AddGlyphs(string)"/> of a begin-session.</summary>
    public int PageHeight => _packState?.PageHeight ?? 0;

    /// <summary>Current page count; 0 before the first <see cref="AddGlyphs(string)"/> of a begin-session.</summary>
    public int PageCount => _packState?.PageCount ?? 0;

    internal static BmFontIncrementalSession Begin(
        byte[] fontData, FontGeneratorOptions options, AdditionOverflowPolicy overflowPolicy)
    {
        // Unfiltered parse (F7): a charset-subsetted parse would silently drop
        // later-added characters and their kerning pairs.
        var prepared = PreparedRasterization.Prepare(
            fontData, options, systemFontName: null, subsetToCharacterSet: false);
        try
        {
            return new BmFontIncrementalSession(prepared, options, overflowPolicy,
                existingInfo: null, existingPages: null);
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    internal static BmFontIncrementalSession Resume(
        byte[] fontData, FontGeneratorOptions options, BmFontModel existing,
        AdditionOverflowPolicy overflowPolicy)
    {
        // A wrong-options resume must fail loudly: padding/spacing shape both the occupied
        // geometry and every future CharEntry, so a mismatch would silently corrupt the atlas.
        if (options.Padding != existing.Info.Padding)
            throw new ArgumentException(
                $"Options padding {options.Padding} does not match the existing model's padding " +
                $"{existing.Info.Padding}. Resume must use the same options the model was generated with.",
                nameof(options));
        if (options.Spacing != existing.Info.Spacing)
            throw new ArgumentException(
                $"Options spacing {options.Spacing} does not match the existing model's spacing " +
                $"{existing.Info.Spacing}. Resume must use the same options the model was generated with.",
                nameof(options));
        // External BMFont .fnt files may carry a negative size (match-char-height mode),
        // so the magnitude is compared; KernSmith itself writes options.Size verbatim.
        if (Math.Abs(Math.Abs(existing.Info.Size) - options.Size) > 0.001f)
            throw new ArgumentException(
                $"Options size {options.Size} does not match the existing model's size " +
                $"{existing.Info.Size}. Resume must use the same options the model was generated with.",
                nameof(options));
        if (options.Outline != existing.Info.Outline)
            throw new ArgumentException(
                $"Options outline {options.Outline} does not match the existing model's outline " +
                $"{existing.Info.Outline}. Resume must use the same options the model was generated with.",
                nameof(options));
        if (existing.Common.ScaleW <= 0 || existing.Common.ScaleH <= 0)
            throw new ArgumentException(
                $"Existing model has invalid page dimensions {existing.Common.ScaleW}x{existing.Common.ScaleH}.",
                nameof(existing));

        var prepared = PreparedRasterization.Prepare(
            fontData, options, systemFontName: null, subsetToCharacterSet: false);
        try
        {
            var session = new BmFontIncrementalSession(prepared, options, overflowPolicy,
                existingInfo: existing.Info, existingPages: existing.Pages);

            var padding = options.Padding;

            session._packState = BuildPackState(
                existing.Characters, options.Spacing,
                existing.Common.ScaleW, existing.Common.ScaleH, existing.Common.Pages);

            foreach (var c in existing.Characters)
            {
                session._present.Add(c.Id);
                session._chars.Add(c);
            }
            session._kerning.AddRange(existing.KerningPairs);

            // Line metrics come from the model so added CharEntries stay consistent with
            // the existing atlas (yoffset is relative to the same base line).
            session._lineMetrics = (existing.Common.LineHeight, existing.Common.Base);

            // Equalized cell height is recoverable from any char (F3).
            if (options.EqualizeCellHeights && existing.Characters.Count > 0)
                session._equalizeTarget = existing.Characters[0].Height - padding.Up - padding.Down;

            return session;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds the unique characters of <paramref name="characters"/> to the atlas.
    /// Characters already in the session are reported in
    /// <see cref="GlyphAdditionResult.AlreadyPresent"/>; characters the font cannot render
    /// land in <see cref="GlyphAdditionResult.FailedCodepoints"/>. Surrogate pairs are
    /// interpreted as single codepoints.
    /// </summary>
    public GlyphAdditionResult AddGlyphs(string characters)
    {
        ArgumentNullException.ThrowIfNull(characters);

        var codepoints = new List<int>(characters.Length);
        var seen = new HashSet<int>();
        foreach (var cp in CharacterSet.EnumerateCodepoints(characters))
        {
            if (seen.Add(cp))
                codepoints.Add(cp);
        }

        return AddGlyphsCore(codepoints);
    }

    /// <summary>
    /// Adds the given unique codepoints to the atlas. See <see cref="AddGlyphs(string)"/>.
    /// </summary>
    public GlyphAdditionResult AddGlyphs(IEnumerable<int> codepoints)
    {
        ArgumentNullException.ThrowIfNull(codepoints);

        var list = new List<int>();
        var seen = new HashSet<int>();
        foreach (var cp in codepoints)
        {
            if (seen.Add(cp))
                list.Add(cp);
        }

        return AddGlyphsCore(list);
    }

    private GlyphAdditionResult AddGlyphsCore(List<int> requested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A begin-session's first add appends the configured fallback character, exactly
        // as PreparedRasterization.ResolveCodepoints does for Generate (FallbackCodepoint
        // takes precedence over FallbackCharacter). A resumed model already contains its
        // fallback.
        if (_existingInfo is null && !_fallbackResolved)
        {
            _fallbackResolved = true;
            var fallback = _options.FallbackCodepoint
                ?? (_options.FallbackCharacter.HasValue ? (int)_options.FallbackCharacter.Value : (int?)null);
            if (fallback.HasValue && !requested.Contains(fallback.Value)
                && _availableCodepoints.Contains(fallback.Value))
            {
                requested.Add(fallback.Value);
            }
        }

        var alreadyPresent = new List<int>();
        var failed = new List<int>();
        var toAdd = new List<int>(requested.Count);
        foreach (var cp in requested)
        {
            if (_present.Contains(cp))
                alreadyPresent.Add(cp);
            else if (!_availableCodepoints.Contains(cp))
                failed.Add(cp);
            else
                toAdd.Add(cp);
        }

        if (toAdd.Count == 0)
            return EmptyResult(failed, alreadyPresent);

        // Canonical ascending order, matching CharacterSet.Resolve in Generate: the packer's
        // height sort is stable, so batch order is the tie-breaker for equal-sized glyphs —
        // this keeps AddGlyphs("ASDF") placement-identical to Generate("ASDF") regardless of
        // the order the caller listed the characters in.
        toAdd.Sort();

        // Rasterize through the cached pipeline. _equalizeTarget is null on a begin-session's
        // first add — that batch establishes the target below. A glyph taller than an
        // established target throws InvalidOperationException before any state changes.
        var raster = _prepared.RasterizeAndProcess(toAdd, _equalizeTarget);
        foreach (var cp in raster.FailedCodepoints)
            failed.Add(cp);

        // Phase 78F outline empty-glyph substitution already ran inside RasterizeAndProcess.
        var glyphs = raster.Glyphs;

        if (glyphs.Count == 0)
            return EmptyResult(failed, alreadyPresent);

        // A begin-session's first equalized batch with visible glyphs establishes the
        // session's cell height. A whitespace-only batch (all 0-height glyphs) must not
        // establish a 0px target — that would make every later add throw; the target
        // stays unestablished until a batch with a visible glyph arrives.
        if (_options.EqualizeCellHeights && _equalizeTarget is null)
        {
            var max = 0;
            foreach (var g in glyphs)
                if (g.Height > max) max = g.Height;
            if (max > 0)
                _equalizeTarget = max;
        }

        _lineMetrics ??= BmFontModelBuilder.ComputeLineMetrics(
            _prepared.FontInfo, _prepared.EffectiveSize, raster.RasterizerFontMetrics);

        _kerningIndex ??= BuildKerningIndex(raster.RasterizerKerningPairs);

        // Packed rect = glyph + padding + spacing, exactly as Generate builds them.
        var padding = _options.Padding;
        var spacing = _options.Spacing;
        var glyphByCodepoint = new Dictionary<int, RasterizedGlyph>(glyphs.Count);
        var rects = new GlyphRect[glyphs.Count];
        for (var i = 0; i < glyphs.Count; i++)
        {
            var g = glyphs[i];
            glyphByCodepoint[g.Codepoint] = g;
            rects[i] = new GlyphRect(
                g.Codepoint,
                g.Width + padding.Left + padding.Right + spacing.Horizontal,
                g.Height + padding.Up + padding.Down + spacing.Vertical);
        }

        // Height-desc, width-desc, stable — the same order as MaxRectsPacker.
        var sorted = MaxRectsPacker.SortForPacking(rects);

        var createdInitialState = false;
        if (_packState is null)
        {
            _packState = CreateInitialState(sorted);
            createdInitialState = true;
        }

        // Transactional batch: nothing is committed to session state until every
        // placement succeeds, so a mid-batch overflow (Throw policy, or Grow at the
        // maximum size) leaves the session exactly at its pre-batch state. The pack
        // state — already mutated by the batch's successful placements — is rebuilt
        // from the committed occupancy, the same reconstruction Resume performs.
        var preBatchWidth = _packState.PageWidth;
        var preBatchHeight = _packState.PageHeight;
        var preBatchPages = _packState.PageCount;
        var pageGrown = false;
        var added = new List<AddedGlyph>(sorted.Count);
        try
        {
            foreach (var rect in sorted)
            {
                var placement = _packState.TryPlace(rect) ?? PlaceWithOverflow(rect, ref pageGrown);
                var g = glyphByCodepoint[rect.Id];

                // CharEntry construction shares BmFontModelBuilder's helper, using the
                // session's held base line so entries stay consistent with the existing atlas.
                var charEntry = BmFontModelBuilder.BuildCharEntry(
                    g, placement, _options, _lineMetrics.Value.BaseLine, _advanceAdjustX, channel: 15);

                added.Add(new AddedGlyph(
                    g.Codepoint, g.Width, g.Height, placement.PageIndex, placement.X, placement.Y,
                    charEntry, g.BitmapData, g.Format, g.Pitch));
            }
        }
        catch
        {
            _packState = createdInitialState
                ? null
                : BuildPackState(_chars, spacing, preBatchWidth, preBatchHeight, preBatchPages);
            throw;
        }

        // All placements succeeded — commit the batch.
        var addedSet = new HashSet<int>(added.Count);
        foreach (var a in added)
        {
            _chars.Add(a.Char);
            _present.Add(a.Codepoint);
            addedSet.Add(a.Codepoint);
        }
        var newKerning = ComputeKerningDelta(addedSet);
        _kerning.AddRange(newKerning);

        _cachedModel = null;

        return new GlyphAdditionResult(
            added, newKerning, failed, alreadyPresent, pageGrown, PageWidth, PageHeight, PageCount);
    }

    private GlyphAdditionResult EmptyResult(List<int> failed, List<int> alreadyPresent)
    {
        return new GlyphAdditionResult(
            Array.Empty<AddedGlyph>(), Array.Empty<KerningEntry>(),
            failed, alreadyPresent, pageGrown: false, PageWidth, PageHeight, PageCount);
    }

    /// <summary>
    /// Reconstructs the MaxRects free-list state from character occupancy — occupied
    /// rect = char + spacing per F2 (padding is already inside Char.Width/Height),
    /// subtracted in canonical order via <see cref="MaxRectsState.SubtractAll"/> so the
    /// state is deterministic and matches PackInto. Used by Resume and by the mid-batch
    /// rollback in <see cref="AddGlyphsCore"/>.
    /// </summary>
    private static MaxRectsState BuildPackState(
        IReadOnlyList<CharEntry> chars, Spacing spacing, int pageWidth, int pageHeight, int pageCount)
    {
        var occupied = new List<OccupiedRect>(chars.Count);
        foreach (var c in chars)
        {
            occupied.Add(new OccupiedRect(
                c.Page, c.X, c.Y,
                c.Width + spacing.Horizontal,
                c.Height + spacing.Vertical));
        }

        var state = new MaxRectsState(pageWidth, pageHeight, Math.Max(pageCount, 1));
        state.SubtractAll(occupied);
        return state;
    }

    /// <summary>
    /// The full BMFont model for everything added so far. Round-trips through the standard
    /// formatters/readers like a <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/> model. Materialized on demand
    /// and cached; each <see cref="AddGlyphs(string)"/> invalidates the cache.
    /// </summary>
    public BmFontModel CurrentModel
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _cachedModel ??= BuildModel();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _prepared.Dispose();
    }

    // ---------------------------------------------------------------
    // Packing
    // ---------------------------------------------------------------

    /// <summary>
    /// Computes the initial page size for a begin-session's first add exactly as
    /// <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/> does: estimate, size constraints, and the
    /// AutofitTexture verify+bump.
    /// </summary>
    private MaxRectsState CreateInitialState(IReadOnlyList<GlyphRect> sortedRects)
    {
        var sizingOptions = AtlasSizeEstimator.BuildSizingOptions(_options);
        var (pageWidth, pageHeight) = AtlasSizeEstimator.ComputeInitialPageSize(
            sortedRects, _options, sizingOptions,
            (w, h) => FitsOnOnePage(sortedRects, w, h));

        return new MaxRectsState(pageWidth, pageHeight, 1);
    }

    private static bool FitsOnOnePage(IReadOnlyList<GlyphRect> sortedRects, int pageWidth, int pageHeight)
    {
        var candidate = new MaxRectsState(pageWidth, pageHeight, 1);
        foreach (var rect in sortedRects)
        {
            if (candidate.TryPlace(rect) is null)
                return false;
        }
        return true;
    }

    private GlyphPlacement PlaceWithOverflow(GlyphRect rect, ref bool pageGrown)
    {
        var state = _packState!;
        switch (_overflowPolicy)
        {
            case AdditionOverflowPolicy.Throw:
                throw new AtlasPackingException(
                    $"Glyph U+{rect.Id:X4} ({rect.Width}x{rect.Height}) does not fit in the atlas " +
                    $"({state.PageWidth}x{state.PageHeight}, {state.PageCount} page(s)) and the " +
                    $"overflow policy is {nameof(AdditionOverflowPolicy.Throw)}.");

            case AdditionOverflowPolicy.NewPage:
                {
                    state.AddPage();
                    var placement = state.TryPlace(rect);
                    if (placement is null)
                        throw new AtlasPackingException(
                            $"Glyph U+{rect.Id:X4} ({rect.Width}x{rect.Height}) is larger than an entire " +
                            $"{state.PageWidth}x{state.PageHeight} page.");
                    return placement.Value;
                }

            default: // AdditionOverflowPolicy.Grow
                {
                    while (true)
                    {
                        var width = state.PageWidth;
                        var height = state.PageHeight;

                        // POT-double the smaller growable dimension (mirroring the
                        // AutofitTexture bump), capped at the configured maximums.
                        var (newWidth, newHeight) = AtlasSizeEstimator.BumpSize(
                            width, height, powerOfTwo: true,
                            _options.MaxTextureWidth, _options.MaxTextureHeight);

                        if (newWidth == width && newHeight == height)
                            throw new AtlasPackingException(
                                $"Glyph U+{rect.Id:X4} ({rect.Width}x{rect.Height}) does not fit and the atlas " +
                                $"is already at its maximum size ({width}x{height}, " +
                                $"MaxTextureWidth={_options.MaxTextureWidth}, MaxTextureHeight={_options.MaxTextureHeight}).");

                        state.Grow(newWidth, newHeight);
                        pageGrown = true;

                        var placement = state.TryPlace(rect);
                        if (placement is not null)
                            return placement.Value;
                    }
                }
        }
    }

    // ---------------------------------------------------------------
    // Kerning
    // ---------------------------------------------------------------

    private Dictionary<int, List<IndexedKerningPair>> BuildKerningIndex(
        IReadOnlyList<ScaledKerningPair>? rasterizerKerningPairs)
    {
        var index = new Dictionary<int, List<IndexedKerningPair>>();

        // Mirror BmFontModelBuilder's preference: rasterizer-provided pre-scaled pairs
        // when available, else the font tables' pairs (scaled at delta time).
        if (_options.Kerning && rasterizerKerningPairs is not null)
        {
            _kerningPreScaled = true;
            foreach (var pair in rasterizerKerningPairs)
                AddToIndex(index, new IndexedKerningPair(pair.LeftCodepoint, pair.RightCodepoint, pair.Amount));
        }
        else if (_options.Kerning && _prepared.FontInfo.KerningPairs.Count > 0)
        {
            foreach (var pair in _prepared.FontInfo.KerningPairs)
                AddToIndex(index, new IndexedKerningPair(pair.LeftCodepoint, pair.RightCodepoint, pair.XAdvanceAdjustment));
        }

        return index;

        static void AddToIndex(Dictionary<int, List<IndexedKerningPair>> index, IndexedKerningPair pair)
        {
            if (!index.TryGetValue(pair.Left, out var leftList))
                index[pair.Left] = leftList = new List<IndexedKerningPair>();
            leftList.Add(pair);

            if (pair.Right == pair.Left)
                return;
            if (!index.TryGetValue(pair.Right, out var rightList))
                index[pair.Right] = rightList = new List<IndexedKerningPair>();
            rightList.Add(pair);
        }
    }

    /// <summary>
    /// Computes the kerning delta for a batch: pairs where one side is newly added and the
    /// counterpart is anywhere in the session (<c>_present</c> already includes the batch).
    /// O(pairs touching the new codepoints), never O(all pairs).
    /// </summary>
    private List<KerningEntry> ComputeKerningDelta(HashSet<int> addedSet)
    {
        var result = new List<KerningEntry>();
        if (_kerningIndex is null || _kerningIndex.Count == 0)
            return result;

        foreach (var codepoint in addedSet)
        {
            if (!_kerningIndex.TryGetValue(codepoint, out var pairs))
                continue;

            foreach (var pair in pairs)
            {
                // Emit each pair once: from its left side, or from its right side only
                // when the left side is not part of this batch.
                if (pair.Left != codepoint && (pair.Right != codepoint || addedSet.Contains(pair.Left)))
                    continue;
                if (!_present.Contains(pair.Left) || !_present.Contains(pair.Right))
                    continue;

                var amount = pair.Amount;
                if (!_kerningPreScaled)
                {
                    amount = (int)Math.Round(
                        (double)amount * _prepared.EffectiveSize / _prepared.FontInfo.UnitsPerEm,
                        MidpointRounding.AwayFromZero);
                }
                if (amount == 0)
                    continue;

                result.Add(new KerningEntry(pair.Left, pair.Right, amount));
            }
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Model materialization
    // ---------------------------------------------------------------

    private BmFontModel BuildModel()
    {
        var info = _existingInfo ?? BmFontModelBuilder.BuildInfo(_prepared.FontInfo, _options);
        var (lineHeight, baseLine) = _lineMetrics ?? default;
        var common = BmFontModelBuilder.BuildCommon(
            _options, lineHeight, baseLine, PageWidth, PageHeight, PageCount);

        var (pageStem, pageExtension) = ResolvePageNaming();
        var pages = new List<PageEntry>(PageCount);
        for (var i = 0; i < PageCount; i++)
        {
            if (_existingPages is not null && i < _existingPages.Count)
                pages.Add(_existingPages[i]);
            else
                pages.Add(new PageEntry(i, $"{pageStem}_{i}{pageExtension}"));
        }

        return new BmFontModel
        {
            Info = info,
            Common = common,
            Pages = pages,
            Characters = _chars.ToArray(),
            KerningPairs = _kerning.ToArray(),
            Extended = BmFontModelBuilder.BuildExtendedMetadata(_options)
        };
    }

    /// <summary>
    /// Overflow pages of a resumed session keep the existing pages' naming: the stem and
    /// extension come from the last existing page's file name when it follows the
    /// <c>{stem}_{index}.{ext}</c> pattern. Begin-sessions (and unparseable names) use
    /// <c>{FamilyName}_{index}{TextureExtension}</c>, matching full generation.
    /// </summary>
    private (string Stem, string Extension) ResolvePageNaming()
    {
        if (_existingPages is { Count: > 0 })
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                _existingPages[^1].File, @"^(.+)_\d+(\.[^.]+)$");
            if (match.Success)
                return (match.Groups[1].Value, match.Groups[2].Value);
        }

        return (_prepared.FontInfo.FamilyName, BmFontModelBuilder.TextureExtension(_options));
    }
}
