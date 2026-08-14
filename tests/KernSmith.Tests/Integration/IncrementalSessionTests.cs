using KernSmith.Output;
using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Phase 183 — <see cref="BmFontIncrementalSession"/>: stable-packing glyph addition at runtime.
/// </summary>
[Collection("RasterizerFactory")]
public class IncrementalSessionTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static FontGeneratorOptions Options() => new()
    {
        Size = 32
    };

    private static bool Overlaps(CharEntry a, CharEntry b)
    {
        if (a.Page != b.Page) return false;
        return a.X < b.X + b.Width && b.X < a.X + a.Width
            && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
    }

    // ---------------------------------------------------------------
    // 1. Stability: existing placements never move.
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_SecondAdd_DoesNotMoveExistingPlacements()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        session.AddGlyphs("ASDF");
        var before = session.CurrentModel.Characters.ToDictionary(c => c.Id);

        var result = session.AddGlyphs("Q");
        var after = session.CurrentModel.Characters.ToDictionary(c => c.Id);

        foreach (var ch in "ASDF")
        {
            after[ch].X.ShouldBe(before[ch].X, $"'{ch}' X moved after adding Q");
            after[ch].Y.ShouldBe(before[ch].Y, $"'{ch}' Y moved after adding Q");
            after[ch].Page.ShouldBe(before[ch].Page, $"'{ch}' page changed after adding Q");
        }

        result.Added.Count.ShouldBe(1);
        var q = after['Q'];
        foreach (var ch in "ASDF")
            Overlaps(q, after[ch]).ShouldBeFalse($"Q overlaps '{ch}'");
    }

    // ---------------------------------------------------------------
    // 2. Pixel parity with a fresh full rasterization.
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_GlyphBytes_MatchFreshFullRasterization()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());
        session.AddGlyphs("ASDF");

        var result = session.AddGlyphs("Q");
        var added = result.Added.ShouldHaveSingleItem();
        added.Codepoint.ShouldBe((int)'Q');

        var freshOptions = Options();
        freshOptions.Characters = CharacterSet.FromChars("Q");
        var fresh = BmFont.RasterizeFont(fontData, freshOptions);
        var freshQ = fresh.Glyphs.Single(g => g.Codepoint == 'Q');

        added.Width.ShouldBe(freshQ.Width);
        added.Height.ShouldBe(freshQ.Height);
        added.RawFormat.ShouldBe(freshQ.Format);
        added.RawPixels.ShouldBe(freshQ.BitmapData);
    }

    // ---------------------------------------------------------------
    // 3. F7 guard: chars outside the initial character set resolve
    //    and keep their kerning pairs.
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_CharOutsideInitialCharacterSet_GetsGlyphAndKerning()
    {
        var fontData = LoadTestFont();
        var options = Options();
        options.Characters = CharacterSet.FromChars("A"); // options charset only seeds defaults
        using var session = BmFont.BeginIncremental(fontData, options);

        session.AddGlyphs("A");
        var result = session.AddGlyphs("V");

        var v = result.Added.ShouldHaveSingleItem();
        v.Codepoint.ShouldBe((int)'V');
        v.Width.ShouldBeGreaterThan(0);
        v.Height.ShouldBeGreaterThan(0);
        v.RawPixels.Length.ShouldBeGreaterThan(0);

        // Roboto kerns A-V both directions; the delta must match a full generate of "AV".
        var fullOptions = Options();
        fullOptions.Characters = CharacterSet.FromChars("AV");
        var full = BmFont.Generate(fontData, fullOptions);
        var expected = full.Model.KerningPairs
            .Where(k => k.First == 'V' || k.Second == 'V')
            .Select(k => (k.First, k.Second, k.Amount))
            .ToList();
        expected.ShouldNotBeEmpty("Roboto is expected to kern A-V; the delta test would be vacuous");

        result.NewKerning
            .Select(k => (k.First, k.Second, k.Amount))
            .ShouldBe(expected, ignoreOrder: true);
    }

    // ---------------------------------------------------------------
    // 4. Model round-trip + kerning delta vs full generate.
    // ---------------------------------------------------------------

    [Fact]
    public void CurrentModel_AfterAdds_RoundTripsAndMatchesFullGenerateKerning()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());
        var first = session.AddGlyphs("ASDF");
        var second = session.AddGlyphs("QW");

        var model = session.CurrentModel;
        model.Characters.Count.ShouldBe(6);

        // Round-trip through the text formatter and reader.
        var text = new TextFormatter().FormatText(model);
        var parsed = BmFontReader.ReadText(text);
        parsed.Characters.Count.ShouldBe(6);
        parsed.KerningPairs.Count.ShouldBe(model.KerningPairs.Count);
        parsed.Common.ScaleW.ShouldBe(model.Common.ScaleW);
        parsed.Common.ScaleH.ShouldBe(model.Common.ScaleH);

        // The kerning delta of the second add equals the pairs-touching-{Q,W} subset
        // of a full generate over the union set.
        var fullOptions = Options();
        fullOptions.Characters = CharacterSet.FromChars("ASDFQW");
        var full = BmFont.Generate(fontData, fullOptions);

        var expectedDelta = full.Model.KerningPairs
            .Where(k => k.First is 'Q' or 'W' || k.Second is 'Q' or 'W')
            .Select(k => (k.First, k.Second, k.Amount))
            .ToList();
        expectedDelta.ShouldNotBeEmpty("Roboto is expected to kern A-W; the delta test would be vacuous");

        second.NewKerning
            .Select(k => (k.First, k.Second, k.Amount))
            .ShouldBe(expectedDelta, ignoreOrder: true);

        // And the accumulated model kerning equals the full generate's kerning.
        model.KerningPairs
            .Select(k => (k.First, k.Second, k.Amount))
            .ShouldBe(full.Model.KerningPairs.Select(k => (k.First, k.Second, k.Amount)), ignoreOrder: true);

        first.NewKerning.ShouldAllBe(k => k.First != 'Q' && k.Second != 'Q' && k.First != 'W' && k.Second != 'W');
    }

    // ---------------------------------------------------------------
    // 5. Resume-vs-live invariant.
    // ---------------------------------------------------------------

    [Fact]
    public void ResumeIncremental_FromGenerateModel_PlacesIdenticallyToLiveSession()
    {
        var fontData = LoadTestFont();

        // Live session: Begin + Add("ASDF") + Add("QW").
        using var live = BmFont.BeginIncremental(fontData, Options());
        live.AddGlyphs("ASDF");
        var liveSecond = live.AddGlyphs("QW");

        // Resume path: Generate("ASDF") -> Resume -> Add("QW"). The Begin session's first
        // add computes page size exactly as Generate does, so the page sizes agree.
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var generated = BmFont.Generate(fontData, genOptions);
        using var resumed = BmFont.ResumeIncremental(fontData, Options(), generated.Model);
        var resumedSecond = resumed.AddGlyphs("QW");

        resumed.PageWidth.ShouldBe(live.PageWidth);
        resumed.PageHeight.ShouldBe(live.PageHeight);

        // The four ASDF placements agree between Generate and the live session's first add.
        var liveChars = live.CurrentModel.Characters.ToDictionary(c => c.Id);
        foreach (var c in generated.Model.Characters)
        {
            liveChars[c.Id].X.ShouldBe(c.X, $"'{(char)c.Id}' X differs between Generate and live first add");
            liveChars[c.Id].Y.ShouldBe(c.Y, $"'{(char)c.Id}' Y differs between Generate and live first add");
            liveChars[c.Id].Page.ShouldBe(c.Page);
        }

        // The QW placements are identical between the resumed and live sessions.
        var liveAdded = liveSecond.Added.ToDictionary(g => g.Codepoint);
        foreach (var g in resumedSecond.Added)
        {
            liveAdded[g.Codepoint].X.ShouldBe(g.X, $"'{(char)g.Codepoint}' X differs between resumed and live add");
            liveAdded[g.Codepoint].Y.ShouldBe(g.Y, $"'{(char)g.Codepoint}' Y differs between resumed and live add");
            liveAdded[g.Codepoint].PageIndex.ShouldBe(g.PageIndex);
        }
    }

    // ---------------------------------------------------------------
    // 6. Overflow policies.
    // ---------------------------------------------------------------

    private static BmFontModel FullSyntheticModel(FontGeneratorOptions options)
    {
        // A 32x32 single-page model whose one fake char (+1px spacing on each axis)
        // occupies the entire page, so any add overflows.
        return new BmFontModel
        {
            Info = new InfoBlock(
                Face: "Roboto", Size: options.Size, Bold: false, Italic: false, Unicode: true,
                Smooth: true, FixedHeight: false, StretchH: 100, Charset: "", Aa: 1,
                Padding: options.Padding, Spacing: options.Spacing),
            Common = new CommonBlock(LineHeight: 38, Base: 30, ScaleW: 32, ScaleH: 32, Pages: 1),
            Pages = new[] { new PageEntry(0, "Roboto_0.png") },
            Characters = new[] { new CharEntry(Id: 1, X: 0, Y: 0, Width: 31, Height: 31, XOffset: 0, YOffset: 0, XAdvance: 31, Page: 0) }
        };
    }

    [Fact]
    public void AddGlyphs_NoFit_GrowPolicy_GrowsPageAndKeepsOldPlacements()
    {
        var fontData = LoadTestFont();
        var options = Options();
        using var session = BmFont.ResumeIncremental(
            fontData, options, FullSyntheticModel(options), AdditionOverflowPolicy.Grow);

        var result = session.AddGlyphs("Q");

        result.PageGrown.ShouldBeTrue();
        result.PageCount.ShouldBe(1);
        // POT-double of the smaller dimension: 32x32 grows its width first.
        (result.PageWidth * result.PageHeight).ShouldBeGreaterThan(32 * 32);

        // The pre-existing placement is verbatim.
        var old = session.CurrentModel.Characters.Single(c => c.Id == 1);
        old.X.ShouldBe(0);
        old.Y.ShouldBe(0);
        old.Width.ShouldBe(31);
        old.Height.ShouldBe(31);
        old.Page.ShouldBe(0);

        var q = result.Added.ShouldHaveSingleItem();
        var qChar = session.CurrentModel.Characters.Single(c => c.Id == 'Q');
        Overlaps(qChar, old).ShouldBeFalse("grown-page placement overlaps the existing glyph");
        (q.X + qChar.Width).ShouldBeLessThanOrEqualTo(result.PageWidth);
        (q.Y + qChar.Height).ShouldBeLessThanOrEqualTo(result.PageHeight);
    }

    [Fact]
    public void AddGlyphs_NoFit_NewPagePolicy_AddsPage()
    {
        var fontData = LoadTestFont();
        var options = Options();
        using var session = BmFont.ResumeIncremental(
            fontData, options, FullSyntheticModel(options), AdditionOverflowPolicy.NewPage);

        var result = session.AddGlyphs("Q");

        result.PageGrown.ShouldBeFalse();
        result.PageCount.ShouldBe(2);
        result.PageWidth.ShouldBe(32);
        result.PageHeight.ShouldBe(32);

        var q = result.Added.ShouldHaveSingleItem();
        q.PageIndex.ShouldBe(1);

        var old = session.CurrentModel.Characters.Single(c => c.Id == 1);
        old.X.ShouldBe(0);
        old.Y.ShouldBe(0);
        old.Page.ShouldBe(0);
    }

    [Fact]
    public void AddGlyphs_NoFit_ThrowPolicy_ThrowsAtlasPackingException()
    {
        var fontData = LoadTestFont();
        var options = Options();
        using var session = BmFont.ResumeIncremental(
            fontData, options, FullSyntheticModel(options), AdditionOverflowPolicy.Throw);

        Should.Throw<AtlasPackingException>(() => session.AddGlyphs("Q"));
    }

    // ---------------------------------------------------------------
    // 7. RGBA promote: lazy, cached, correct.
    // ---------------------------------------------------------------

    [Fact]
    public void AddedGlyph_Pixels_PromotesGrayscaleToRgbaAndCaches()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        var result = session.AddGlyphs("A");
        var g = result.Added.ShouldHaveSingleItem();

        g.RawFormat.ShouldBe(PixelFormat.Grayscale8);

        var pixels = g.Pixels;
        pixels.Length.ShouldBe(g.Width * g.Height * 4);
        for (var row = 0; row < g.Height; row++)
        {
            for (var col = 0; col < g.Width; col++)
            {
                var i = (row * g.Width + col) * 4;
                pixels[i].ShouldBe((byte)255);
                pixels[i + 1].ShouldBe((byte)255);
                pixels[i + 2].ShouldBe((byte)255);
                pixels[i + 3].ShouldBe(g.RawPixels[row * g.Pitch + col]);
            }
        }

        // Computed once — the second access returns the same cached array instance.
        ReferenceEquals(g.Pixels, pixels).ShouldBeTrue("Pixels must be cached after first access");
    }

    [Fact]
    public void AddedGlyph_PremultipliedPixels_GrayscaleIsAlphaInAllChannels()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        var g = session.AddGlyphs("A").Added.ShouldHaveSingleItem();
        g.RawFormat.ShouldBe(PixelFormat.Grayscale8);

        var premultiplied = g.PremultipliedPixels;
        premultiplied.Length.ShouldBe(g.Width * g.Height * 4);
        premultiplied.ShouldContain(b => b > 0, "an antialiased 'A' must have nonzero coverage");

        // White coverage premultiplied by alpha → every channel equals the alpha.
        for (var i = 0; i < premultiplied.Length; i += 4)
        {
            var a = g.Pixels[i + 3];
            premultiplied[i].ShouldBe(a);
            premultiplied[i + 1].ShouldBe(a);
            premultiplied[i + 2].ShouldBe(a);
            premultiplied[i + 3].ShouldBe(a);
        }
    }

    [Fact]
    public void AddedGlyph_PremultipliedPixels_MatchesManuallyPremultipliedPixels()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        var g = session.AddGlyphs("Q").Added.ShouldHaveSingleItem();

        var expected = (byte[])g.Pixels.Clone();
        for (var i = 0; i < expected.Length; i += 4)
        {
            var a = expected[i + 3];
            expected[i] = (byte)(expected[i] * a / 255);
            expected[i + 1] = (byte)(expected[i + 1] * a / 255);
            expected[i + 2] = (byte)(expected[i + 2] * a / 255);
        }

        g.PremultipliedPixels.ShouldBe(expected);
    }

    [Fact]
    public void AddedGlyph_PremultipliedPixels_IsCachedAfterFirstAccess()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        var g = session.AddGlyphs("B").Added.ShouldHaveSingleItem();

        var first = g.PremultipliedPixels;
        ReferenceEquals(g.PremultipliedPixels, first)
            .ShouldBeTrue("PremultipliedPixels must be cached after first access");
    }

    [Fact]
    public void AddedGlyph_PremultipliedPixels_DoesNotAffectPixelsLaziness()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        var g = session.AddGlyphs("A").Added.ShouldHaveSingleItem();
        g.RawFormat.ShouldBe(PixelFormat.Grayscale8);

        // Read the premultiplied view first, before Pixels has ever been computed.
        var premultiplied = g.PremultipliedPixels;

        // Pixels must still compute its own straight-alpha buffer, unaffected.
        var pixels = g.Pixels;
        ReferenceEquals(pixels, premultiplied)
            .ShouldBeFalse("Pixels and PremultipliedPixels must be distinct buffers");
        for (var row = 0; row < g.Height; row++)
        {
            for (var col = 0; col < g.Width; col++)
            {
                var i = (row * g.Width + col) * 4;
                pixels[i].ShouldBe((byte)255);
                pixels[i + 1].ShouldBe((byte)255);
                pixels[i + 2].ShouldBe((byte)255);
                pixels[i + 3].ShouldBe(g.RawPixels[row * g.Pitch + col]);
            }
        }
        ReferenceEquals(g.Pixels, pixels).ShouldBeTrue("Pixels must still be cached after first access");
    }

    // ---------------------------------------------------------------
    // 8. Guards, re-adds, unknown codepoints.
    // ---------------------------------------------------------------

    [Fact]
    public void BeginIncremental_WithVariants_ThrowsNotSupported()
    {
        var options = Options();
        options.Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) };
        var ex = Should.Throw<NotSupportedException>(() => BmFont.BeginIncremental(LoadTestFont(), options));
        ex.Message.ShouldContain("Variants", Case.Sensitive);
    }

    [Fact]
    public void BeginIncremental_WithChannelPacking_ThrowsNotSupported()
    {
        var options = Options();
        options.ChannelPacking = true;
        var ex = Should.Throw<NotSupportedException>(() => BmFont.BeginIncremental(LoadTestFont(), options));
        ex.Message.ShouldContain("ChannelPacking", Case.Sensitive);
    }

    [Fact]
    public void BeginIncremental_WithTargetRegion_ThrowsNotSupported()
    {
        var options = Options();
        options.TargetRegion = new AtlasTargetRegion { SourcePngData = new byte[1], Width = 64, Height = 64 };
        var ex = Should.Throw<NotSupportedException>(() => BmFont.BeginIncremental(LoadTestFont(), options));
        ex.Message.ShouldContain("TargetRegion", Case.Sensitive);
    }

    [Fact]
    public void BeginIncremental_WithCustomGlyphs_ThrowsNotSupported()
    {
        var options = Options();
        options.CustomGlyphs = new Dictionary<int, CustomGlyph>
        {
            [0xE000] = new CustomGlyph(4, 4, new byte[64])
        };
        var ex = Should.Throw<NotSupportedException>(() => BmFont.BeginIncremental(LoadTestFont(), options));
        ex.Message.ShouldContain("CustomGlyphs", Case.Sensitive);
    }

    [Fact]
    public void AddGlyphs_ReAddExistingChar_ReportsAlreadyPresentWithoutNewPlacement()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());
        session.AddGlyphs("A");

        var result = session.AddGlyphs("A");

        result.AlreadyPresent.ShouldBe(new[] { (int)'A' });
        result.Added.ShouldBeEmpty();
        result.FailedCodepoints.ShouldBeEmpty();
        session.CurrentModel.Characters.Count.ShouldBe(1);
    }

    [Fact]
    public void AddGlyphs_UnknownCodepoint_ReportsFailed()
    {
        var fontData = LoadTestFont();
        using var session = BmFont.BeginIncremental(fontData, Options());

        // Private Use Area codepoint — not mapped in Roboto.
        var result = session.AddGlyphs(new[] { 0xE000 });

        result.FailedCodepoints.ShouldBe(new[] { 0xE000 });
        result.Added.ShouldBeEmpty();
        result.AlreadyPresent.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // 9. Equalized cell heights.
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_EqualizedSession_LaterAddsPadToEstablishedCellHeight()
    {
        var fontData = LoadTestFont();
        var options = Options();
        options.EqualizeCellHeights = true;
        using var session = BmFont.BeginIncremental(fontData, options);

        session.AddGlyphs("Al"); // mixed heights — the first add establishes the target
        var chars = session.CurrentModel.Characters;
        var cellHeight = chars[0].Height;
        chars.ShouldAllBe(c => c.Height == cellHeight, "equalized cells must share one height");

        session.AddGlyphs("c"); // shorter glyph pads up to the same cell height
        var c = session.CurrentModel.Characters.Single(ch => ch.Id == 'c');
        c.Height.ShouldBe(cellHeight);
    }

    [Fact]
    public void ResumeIncremental_EqualizedModel_TallerGlyphThrows()
    {
        var fontData = LoadTestFont();
        var genOptions = Options();
        genOptions.EqualizeCellHeights = true;
        genOptions.Characters = CharacterSet.FromChars("ace");
        var generated = BmFont.Generate(fontData, genOptions);

        var resumeOptions = Options();
        resumeOptions.EqualizeCellHeights = true;
        using var session = BmFont.ResumeIncremental(fontData, resumeOptions, generated.Model);

        // 'l' is taller than the x-height cell of "ace" — stable packing forbids growing cells.
        Should.Throw<InvalidOperationException>(() => session.AddGlyphs("l"));
    }

    // ---------------------------------------------------------------
    // 10. Resume with mismatched options fails loudly.
    // ---------------------------------------------------------------

    [Fact]
    public void ResumeIncremental_MismatchedPadding_ThrowsArgumentException()
    {
        var fontData = LoadTestFont();
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var generated = BmFont.Generate(fontData, genOptions);

        var resumeOptions = Options();
        resumeOptions.Padding = new Padding(2);

        Should.Throw<ArgumentException>(() =>
            BmFont.ResumeIncremental(fontData, resumeOptions, generated.Model));
    }

    [Fact]
    public void ResumeIncremental_MismatchedSpacing_ThrowsArgumentException()
    {
        var fontData = LoadTestFont();
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var generated = BmFont.Generate(fontData, genOptions);

        var resumeOptions = Options();
        resumeOptions.Spacing = new Spacing(3, 3);

        Should.Throw<ArgumentException>(() =>
            BmFont.ResumeIncremental(fontData, resumeOptions, generated.Model));
    }

    [Fact]
    public void ResumeIncremental_MismatchedSize_ThrowsArgumentException()
    {
        var fontData = LoadTestFont();
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var generated = BmFont.Generate(fontData, genOptions);

        var resumeOptions = Options();
        resumeOptions.Size = 48;

        var ex = Should.Throw<ArgumentException>(() =>
            BmFont.ResumeIncremental(fontData, resumeOptions, generated.Model));
        ex.Message.ShouldContain("size", Case.Sensitive);
    }

    [Fact]
    public void ResumeIncremental_MismatchedOutline_ThrowsArgumentException()
    {
        var fontData = LoadTestFont();
        var genOptions = Options();
        genOptions.Characters = CharacterSet.FromChars("ASDF");
        var generated = BmFont.Generate(fontData, genOptions);

        var resumeOptions = Options();
        resumeOptions.Outline = 2;

        var ex = Should.Throw<ArgumentException>(() =>
            BmFont.ResumeIncremental(fontData, resumeOptions, generated.Model));
        ex.Message.ShouldContain("outline", Case.Sensitive);
    }

    [Fact]
    public void ResumeIncremental_NegativeSizeExternalModel_DoesNotThrow()
    {
        // External BMFont .fnt files may carry a negative size (match-char-height mode);
        // resume compares magnitudes so those models stay resumable.
        var fontData = LoadTestFont();
        var options = Options();
        var baseModel = FullSyntheticModel(options);
        var model = new BmFontModel
        {
            Info = baseModel.Info with { Size = -options.Size },
            Common = baseModel.Common,
            Pages = baseModel.Pages,
            Characters = baseModel.Characters
        };

        using var session = BmFont.ResumeIncremental(fontData, options, model);
        session.PageWidth.ShouldBe(32);
    }

    // ---------------------------------------------------------------
    // 11. Mid-batch overflow must not corrupt the session (transactional adds).
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_ThrowPolicy_MidBatchOverflow_RollsBackWholeBatch()
    {
        var fontData = LoadTestFont();
        var options = Options();

        // Measure the two glyphs so the synthetic atlas can be built with a free
        // right-edge strip that fits narrow 'l' but not wide 'W'.
        var measureOptions = Options();
        measureOptions.Characters = CharacterSet.FromChars("lW");
        var measured = BmFont.RasterizeFont(fontData, measureOptions);
        var l = measured.Glyphs.Single(g => g.Codepoint == 'l');
        var w = measured.Glyphs.Single(g => g.Codepoint == 'W');
        var spacing = options.Spacing;

        // Preconditions for the repro: 'l' places first (taller sorts first) and
        // fits the strip; 'W' is wider than the strip and overflows mid-batch.
        l.Height.ShouldBeGreaterThan(w.Height, "the fitting glyph must be placed before the overflowing one");
        var stripWidth = l.Width + spacing.Horizontal;
        (w.Width + spacing.Horizontal).ShouldBeGreaterThan(stripWidth, "'W' must not fit the free strip");

        const int occupiedWidth = 40;
        const int pageHeight = 64;
        var pageWidth = occupiedWidth + stripWidth;
        var model = new BmFontModel
        {
            Info = new InfoBlock(
                Face: "Roboto", Size: options.Size, Bold: false, Italic: false, Unicode: true,
                Smooth: true, FixedHeight: false, StretchH: 100, Charset: "", Aa: 1,
                Padding: options.Padding, Spacing: options.Spacing),
            Common = new CommonBlock(LineHeight: 38, Base: 30, ScaleW: pageWidth, ScaleH: pageHeight, Pages: 1),
            Pages = new[] { new PageEntry(0, "Roboto_0.png") },
            Characters = new[]
            {
                new CharEntry(Id: 1, X: 0, Y: 0,
                    Width: occupiedWidth - spacing.Horizontal, Height: pageHeight - spacing.Vertical,
                    XOffset: 0, YOffset: 0, XAdvance: 10, Page: 0)
            }
        };

        using var session = BmFont.ResumeIncremental(
            fontData, options, model, AdditionOverflowPolicy.Throw);

        Should.Throw<AtlasPackingException>(() => session.AddGlyphs("lW"));

        // All-or-nothing: nothing from the failed batch is committed.
        session.CurrentModel.Characters.Count.ShouldBe(1);
        session.CurrentModel.Characters[0].Id.ShouldBe(1);

        // The fitting glyph is re-addable — not reported AlreadyPresent — with pixels.
        var retry = session.AddGlyphs("l");
        retry.AlreadyPresent.ShouldBeEmpty("a rolled-back glyph must not be considered present");
        var added = retry.Added.ShouldHaveSingleItem();
        added.Codepoint.ShouldBe((int)'l');
        added.RawPixels.Length.ShouldBeGreaterThan(0);

        // The rebuilt pack state still respects the pre-batch occupancy.
        var chars = session.CurrentModel.Characters.ToDictionary(c => c.Id);
        Overlaps(chars['l'], chars[1]).ShouldBeFalse("rolled-back state must still respect existing occupancy");
        added.X.ShouldBeGreaterThanOrEqualTo(occupiedWidth);
        (added.X + chars['l'].Width).ShouldBeLessThanOrEqualTo(pageWidth);
    }

    // ---------------------------------------------------------------
    // 12. A whitespace-only first batch must not poison the equalize target.
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_EqualizedSession_WhitespaceOnlyFirstBatch_DoesNotPoisonCellHeight()
    {
        var fontData = LoadTestFont();
        var options = Options();
        options.EqualizeCellHeights = true;
        using var session = BmFont.BeginIncremental(fontData, options);

        // Space rasterizes to a 0x0 glyph — it must not establish a 0 cell height.
        session.AddGlyphs(" ");

        var a = session.AddGlyphs("A").Added.ShouldHaveSingleItem();
        a.Height.ShouldBeGreaterThan(0);

        // A shorter glyph pads up to the established cell height.
        session.AddGlyphs("c");
        var chars = session.CurrentModel.Characters.ToDictionary(c => c.Id);
        chars['c'].Height.ShouldBe(chars['A'].Height);
    }

    // ---------------------------------------------------------------
    // 13. FallbackCodepoint parity with Generate.
    // ---------------------------------------------------------------

    [Fact]
    public void AddGlyphs_BeginSessionWithFallbackCodepoint_AutoAddsFallback()
    {
        var fontData = LoadTestFont();
        var options = Options();
        options.FallbackCodepoint = '?';
        using var session = BmFont.BeginIncremental(fontData, options);

        session.AddGlyphs("abc");

        var genOptions = Options();
        genOptions.FallbackCodepoint = '?';
        genOptions.Characters = CharacterSet.FromChars("abc");
        var generated = BmFont.Generate(fontData, genOptions);
        generated.Model.Characters.ShouldContain(c => c.Id == '?');

        session.CurrentModel.Characters.Select(c => c.Id)
            .ShouldBe(generated.Model.Characters.Select(c => c.Id), ignoreOrder: true);
    }

    // ---------------------------------------------------------------
    // 14. Overflow pages on a resumed session keep the existing name stem.
    // ---------------------------------------------------------------

    [Fact]
    public void CurrentModel_ResumedSessionOverflowPage_KeepsExistingPageNameStem()
    {
        var fontData = LoadTestFont();
        var options = Options();
        var baseModel = FullSyntheticModel(options);
        var model = new BmFontModel
        {
            Info = baseModel.Info,
            Common = baseModel.Common,
            Pages = new[] { new PageEntry(0, "custom_0.png") },
            Characters = baseModel.Characters
        };

        using var session = BmFont.ResumeIncremental(
            fontData, options, model, AdditionOverflowPolicy.NewPage);
        var result = session.AddGlyphs("Q");
        result.PageCount.ShouldBe(2);

        session.CurrentModel.Pages[0].File.ShouldBe("custom_0.png");
        session.CurrentModel.Pages[1].File.ShouldBe("custom_1.png");
    }
}
