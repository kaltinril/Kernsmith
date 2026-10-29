using System.Collections.Concurrent;
using System.Globalization;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Tests.Goldens;

/// <summary>A committed fixture font and the glyphs the goldens render from it.</summary>
internal sealed record GoldenFont(string Id, string FileName, string Glyphs)
{
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    /// <summary>The font file's bytes (read once per process).</summary>
    public byte[] Bytes() => Cache.GetOrAdd(FileName, name =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    /// <summary>The glyph set as codepoints, in order.</summary>
    public int[] Codepoints()
    {
        var codepoints = new List<int>();
        for (int i = 0; i < Glyphs.Length; i += char.IsSurrogatePair(Glyphs, i) ? 2 : 1)
            codepoints.Add(char.ConvertToUtf32(Glyphs, i));

        return [.. codepoints];
    }
}

/// <summary>Which test class runs a raster case: the groups run in parallel.</summary>
internal enum GoldenGroup
{
    /// <summary>Every font x size x anti-alias mode.</summary>
    Modes,

    /// <summary>SDF, MSDF and MTSDF.</summary>
    DistanceFields,

    /// <summary>DPI, synthetic styles, variation instances and color.</summary>
    Features,
}

/// <summary>One rasterizer-level golden: a font, the options, and optional instance/palette.</summary>
internal sealed record GoldenCase(
    GoldenGroup Group,
    string Id,
    GoldenFont Font,
    RasterOptions Options,
    Dictionary<string, float>? Axes = null,
    int Palette = 0,
    bool Reference = false);

/// <summary>One pipeline-level golden: <see cref="BmFont.Generate(byte[], FontGeneratorOptions)"/>
/// output for a font and options.</summary>
internal sealed record GenerateCase(string Id, GoldenFont Font, Func<FontGeneratorOptions> Options, bool Reference = false);

/// <summary>
/// The golden-master matrix. Every case id here must have exactly one manifest entry, and every
/// manifest entry a case (<c>NativeGoldenMasterTests.Manifest_CoversExactlyTheMatrix</c>).
/// </summary>
internal static class GoldenCatalog
{
    /// <summary>
    /// Latin sample: blank (space), punctuation, digits, round/straight/diagonal capitals,
    /// descenders, composites (é, ç, Å are composite glyphs in Roboto) and an unmapped emoji.
    /// </summary>
    private const string Latin = " !%&,.0123?@ABEGHMOQRSWagjmoyçéÅ\U0001F600";

    /// <summary>Reem Kufi Ink: a few Latin glyphs (no color records) and Arabic color glyphs
    /// (letters, Arabic-Indic digits, marks).</summary>
    private const string Arabic = " AHao0?ابجدعكلمنهوي١٤؟ءَّ۵\U0001F600";

    public static readonly GoldenFont Roboto = new("roboto", "Roboto-Regular.ttf", Latin);
    public static readonly GoldenFont NotoSans = new("notosans", "NotoSans-Regular-Latin.ttf", Latin);
    public static readonly GoldenFont NotoSansCff = new("notosans-cff", "NotoSans-Regular-Latin-CFF.otf", Latin);
    public static readonly GoldenFont SourceSerif = new("sourceserif", "SourceSerif4-Regular.otf", Latin);
    public static readonly GoldenFont SourceCode = new("sourcecode", "SourceCodeVF-Upright.otf", Latin);
    public static readonly GoldenFont RobotoFlex = new("robotoflex", "RobotoFlex-Variable.ttf", Latin);
    public static readonly GoldenFont RobotoFlexLatin = new("robotoflex-latin", "RobotoFlex-Latin.ttf", Latin);
    public static readonly GoldenFont DancingScript = new("dancingscript", "DancingScript-Variable.ttf", Latin);
    public static readonly GoldenFont Bungee = new("bungee", Path.Combine("Color", "BungeeColor-Regular.ttf"), Latin);
    public static readonly GoldenFont ReemKufi = new("reemkufi", Path.Combine("Color", "ReemKufiInk-Regular.ttf"), Arabic);

    /// <summary>Noto Color Emoji (CBDT bitmaps only, one 109 ppem strike): a blank, a keycap base,
    /// emoji, and an unmapped Latin letter.</summary>
    public static readonly GoldenFont NotoEmoji = new("notoemoji", "NotoColorEmoji.ttf", " #😀❤⭐🚀🎉A");

    /// <summary>
    /// Every committed outline fixture. <c>NotoColorEmoji.ttf</c> is deliberately absent: it has no
    /// outlines, so the mode, distance-field and style matrices do not apply; its own color-bitmap
    /// cases are listed separately below.
    /// </summary>
    public static readonly GoldenFont[] Fonts =
        [Roboto, NotoSans, NotoSansCff, SourceSerif, SourceCode, RobotoFlex, RobotoFlexLatin, DancingScript, Bungee, ReemKufi];

    public static readonly float[] Sizes = [8f, 12f, 16f, 24f, 48f, 96f];

    public static readonly AntiAliasMode[] Modes =
        [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Light, AntiAliasMode.Lcd];

    /// <summary>The cases that also commit a contact-sheet PNG, for human review and so a
    /// failure can show expected/actual/diff images.</summary>
    private static readonly HashSet<string> ReferenceIds =
    [
        "roboto/none/16px",
        "roboto/grayscale/16px",
        "roboto/light/16px",
        "roboto/lcd/16px",
        "roboto/lcd-smooth/16px",
        "roboto/lcd-vrgb/16px",
        "roboto/grayscale/48px",
        "sourceserif/grayscale/24px",
        "sourcecode/grayscale/24px/wght=900",
        "robotoflex/grayscale/24px/wght=100",
        "robotoflex/grayscale/24px/wght=900",
        "roboto/grayscale/24px/bold-italic",
        "roboto/sdf/32px",
        "roboto/msdf/32px",
        "bungee/color/48px/palette=6",
        "reemkufi/color/48px",
        "notoemoji/color/32px",
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, GoldenCase>> RasterCases = new(BuildRasterCases);
    private static readonly Lazy<IReadOnlyDictionary<string, GenerateCase>> GenerateCases = new(BuildGenerateCases);

    public static IReadOnlyDictionary<string, GoldenCase> Raster => RasterCases.Value;

    public static IReadOnlyDictionary<string, GenerateCase> Generate => GenerateCases.Value;

    private static IReadOnlyDictionary<string, GoldenCase> BuildRasterCases()
    {
        var cases = new List<GoldenCase>();
        var group = GoldenGroup.Modes;
        void Add(string id, GoldenFont font, RasterOptions options, Dictionary<string, float>? axes = null, int palette = 0) =>
            cases.Add(new GoldenCase(group, id, font, options, axes, palette, ReferenceIds.Contains(id)));

        // Every font x size x anti-alias mode. Light keeps EnableHinting on, so it is the
        // auto-hinted path; the others render unhinted whatever EnableHinting says.
        foreach (var font in Fonts)
        {
            foreach (float size in Sizes)
            {
                foreach (var mode in Modes)
                    Add($"{font.Id}/{Name(mode)}/{Px(size)}", font, new RasterOptions { Size = size, AntiAlias = mode });
            }
        }

        // DPI scaling: 12pt at 96 dpi is 16px.
        group = GoldenGroup.Features;
        Add("roboto/grayscale/12pt@96dpi", Roboto, new RasterOptions { Size = 12, Dpi = 96 });
        Add("roboto/none/12pt@96dpi", Roboto, new RasterOptions { Size = 12, Dpi = 96, AntiAlias = AntiAliasMode.None });

        // Distance fields at two sizes, every font (MSDF at 48px costs ~0.4 s a case in Debug).
        group = GoldenGroup.DistanceFields;
        foreach (var font in Fonts)
        {
            foreach (float size in new[] { 16f, 32f })
            {
                Add($"{font.Id}/sdf/{Px(size)}", font, new RasterOptions { Size = size, Sdf = true });
                Add($"{font.Id}/msdf/{Px(size)}", font, new RasterOptions { Size = size, SdfMode = SdfMode.Msdf });
                Add($"{font.Id}/mtsdf/{Px(size)}", font, new RasterOptions { Size = size, SdfMode = SdfMode.Mtsdf });
            }
        }

        group = GoldenGroup.Features;

        // Synthetic styles at two sizes, every font (color fonts render their plain outlines here).
        foreach (var font in Fonts)
        {
            foreach (float size in new[] { 16f, 48f })
            {
                Add($"{font.Id}/grayscale/{Px(size)}/bold", font, new RasterOptions { Size = size, Bold = true });
                Add($"{font.Id}/grayscale/{Px(size)}/italic", font, new RasterOptions { Size = size, Italic = true });
                Add($"{font.Id}/grayscale/{Px(size)}/bold-italic", font, new RasterOptions { Size = size, Bold = true, Italic = true });
            }
        }

        // Styles through the other render paths.
        Add("roboto/grayscale/24px/bold-italic", Roboto, new RasterOptions { Size = 24, Bold = true, Italic = true });
        Add("roboto/none/24px/bold-italic", Roboto, new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.None, Bold = true, Italic = true });
        Add("roboto/light/24px/bold-italic", Roboto, new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Light, Bold = true, Italic = true });
        Add("roboto/lcd/24px/bold-italic", Roboto, new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Lcd, Bold = true, Italic = true });
        Add("roboto/sdf/24px/bold", Roboto, new RasterOptions { Size = 24, Sdf = true, Bold = true });
        Add("roboto/msdf/24px/italic", Roboto, new RasterOptions { Size = 24, SdfMode = SdfMode.Msdf, Italic = true });

        // LCD filter presets and subpixel orders (Default/Rgb is the plain lcd matrix above).
        foreach (var filter in new[] { LcdFilter.Smooth, LcdFilter.None })
        {
            foreach (float size in new[] { 12f, 16f, 32f })
                Add($"roboto/lcd-{Name(filter)}/{Px(size)}", Roboto, new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Lcd, LcdFilter = filter });
        }

        foreach (var order in new[] { LcdSubpixelOrder.Bgr, LcdSubpixelOrder.VRgb, LcdSubpixelOrder.VBgr })
        {
            foreach (float size in new[] { 12f, 16f, 32f })
                Add($"roboto/lcd-{Name(order)}/{Px(size)}", Roboto, new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Lcd, LcdSubpixelOrder = order });
        }

        Add("roboto/lcd-vbgr-smooth/24px/bold-italic", Roboto, new RasterOptions
        {
            Size = 24, AntiAlias = AntiAliasMode.Lcd, LcdFilter = LcdFilter.Smooth, LcdSubpixelOrder = LcdSubpixelOrder.VBgr, Bold = true, Italic = true,
        });
        Add("sourceserif/lcd-vrgb/24px", SourceSerif, new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Lcd, LcdSubpixelOrder = LcdSubpixelOrder.VRgb });
        Add("notosans-cff/lcd-smooth/24px", NotoSansCff, new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Lcd, LcdFilter = LcdFilter.Smooth });

        // Variation instances (gvar/HVAR/MVAR for TrueType, CFF2 blend for Source Code).
        var instances = new (GoldenFont Font, string Axes)[]
        {
            (RobotoFlex, "wght=100"),
            (RobotoFlex, "wght=900"),
            (SourceCode, "wght=900"),
            (DancingScript, "wght=700"),
        };
        foreach (var (font, axesText) in instances)
        {
            var axes = Axes(axesText);
            foreach (float size in new[] { 12f, 24f, 48f })
                Add($"{font.Id}/grayscale/{Px(size)}/{axesText}", font, new RasterOptions { Size = size }, axes);

            foreach (var mode in new[] { AntiAliasMode.None, AntiAliasMode.Light, AntiAliasMode.Lcd })
                Add($"{font.Id}/{Name(mode)}/16px/{axesText}", font, new RasterOptions { Size = 16, AntiAlias = mode }, axes);

            Add($"{font.Id}/sdf/32px/{axesText}", font, new RasterOptions { Size = 32, Sdf = true }, axes);
            Add($"{font.Id}/msdf/32px/{axesText}", font, new RasterOptions { Size = 32, SdfMode = SdfMode.Msdf }, axes);
        }

        Add("robotoflex/grayscale/24px/wght=900/bold-italic", RobotoFlex, new RasterOptions { Size = 24, Bold = true, Italic = true }, Axes("wght=900"));

        // Color: COLR v0 (Bungee, two palettes) and COLR v1 (Reem Kufi Ink).
        foreach (float size in new[] { 16f, 48f, 96f })
        {
            Add($"bungee/color/{Px(size)}/palette=0", Bungee, new RasterOptions { Size = size, ColorFont = true });
            Add($"bungee/color/{Px(size)}/palette=6", Bungee, new RasterOptions { Size = size, ColorFont = true, ColorPaletteIndex = 6 }, palette: 6);
            Add($"reemkufi/color/{Px(size)}", ReemKufi, new RasterOptions { Size = size, ColorFont = true });
        }

        Add("bungee/color-light/16px/palette=0", Bungee, new RasterOptions { Size = 16, ColorFont = true, AntiAlias = AntiAliasMode.Light });
        Add("reemkufi/color-light/16px", ReemKufi, new RasterOptions { Size = 16, ColorFont = true, AntiAlias = AntiAliasMode.Light });
        Add("bungee/color-none/24px/palette=6", Bungee, new RasterOptions { Size = 24, ColorFont = true, ColorPaletteIndex = 6, AntiAlias = AntiAliasMode.None }, palette: 6);
        Add("bungee/color/48px/palette=0/bold-italic", Bungee, new RasterOptions { Size = 48, ColorFont = true, Bold = true, Italic = true });
        Add("reemkufi/color/48px/bold-italic", ReemKufi, new RasterOptions { Size = 48, ColorFont = true, Bold = true, Italic = true });
        Add("bungee/color-sdf/32px/palette=0", Bungee, new RasterOptions { Size = 32, ColorFont = true, Sdf = true });
        Add("reemkufi/color-sdf/32px", ReemKufi, new RasterOptions { Size = 32, ColorFont = true, Sdf = true });

        // Color bitmaps (CBDT): downscaled, at the strike, upscaled, and through the other options.
        foreach (float size in new[] { 16f, 32f, 109f, 160f })
            Add($"notoemoji/color/{Px(size)}", NotoEmoji, new RasterOptions { Size = size, ColorFont = true });

        Add("notoemoji/grayscale/32px", NotoEmoji, new RasterOptions { Size = 32 });
        Add("notoemoji/color-none/32px", NotoEmoji, new RasterOptions { Size = 32, ColorFont = true, AntiAlias = AntiAliasMode.None });
        Add("notoemoji/color/32px/bold-italic", NotoEmoji, new RasterOptions { Size = 32, ColorFont = true, Bold = true, Italic = true });
        Add("notoemoji/color-sdf/32px", NotoEmoji, new RasterOptions { Size = 32, ColorFont = true, Sdf = true });

        return cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    /// <summary>Text for the pipeline-level goldens: kerning pairs (AV, To, Wa), composites, digits.</summary>
    private const string GenerateText = "AVWTaeoy.,!?0123ÀÉçö&@";

    private const string GenerateArabic = "AHo0ابجدعكلمنهوي١٤؟";

    private static IReadOnlyDictionary<string, GenerateCase> BuildGenerateCases()
    {
        static FontGeneratorOptions Options(float size, string text) => new()
        {
            Size = size,
            Characters = CharacterSet.FromChars(text),
            Backend = RasterizerBackend.Native,
        };

        var cases = new GenerateCase[]
        {
            // The four with WOFF2 twins: Generate_FromWoff2_MatchesTheTtfGolden reuses these entries.
            new("generate/roboto/grayscale-28", Roboto, () => Options(28, GenerateText), Reference: true),
            new("generate/notosans/grayscale-28", NotoSans, () => Options(28, GenerateText)),
            new("generate/notosans-cff/grayscale-28", NotoSansCff, () => Options(28, GenerateText)),
            new("generate/robotoflex-latin/grayscale-28", RobotoFlexLatin, () => Options(28, GenerateText)),

            new("generate/roboto/none-24", Roboto, () => { var o = Options(24, GenerateText); o.AntiAlias = AntiAliasMode.None; return o; }),
            new("generate/notosans/lcd-24", NotoSans, () => { var o = Options(24, GenerateText); o.AntiAlias = AntiAliasMode.Lcd; return o; }),
            new("generate/notosans/lcd-24-vrgb-smooth", NotoSans, () =>
            {
                var o = Options(24, GenerateText);
                o.AntiAlias = AntiAliasMode.Lcd;
                o.LcdSubpixelOrder = LcdSubpixelOrder.VRgb;
                o.LcdFilter = LcdFilter.Smooth;
                return o;
            }),
            new("generate/sourceserif/light-bold-italic-24", SourceSerif, () =>
            {
                var o = Options(24, GenerateText);
                o.AntiAlias = AntiAliasMode.Light;
                o.Bold = true;
                o.Italic = true;
                return o;
            }),
            new("generate/sourcecode/grayscale-24-wght900", SourceCode, () =>
            {
                var o = Options(24, GenerateText);
                o.VariationAxes = Axes("wght=900");
                return o;
            }),
            new("generate/robotoflex/sdf-32-wght900", RobotoFlex, () =>
            {
                var o = Options(32, GenerateText);
                o.Sdf = true;
                o.VariationAxes = Axes("wght=900");
                return o;
            }),
            new("generate/roboto/msdf-32", Roboto, () => { var o = Options(32, GenerateText); o.SdfMode = SdfMode.Msdf; return o; }),
            new("generate/bungee/color-32-palette6", Bungee, () =>
            {
                var o = Options(32, GenerateText);
                o.ColorFont = true;
                o.ColorPaletteIndex = 6;
                return o;
            }, Reference: true),
            new("generate/reemkufi/color-32", ReemKufi, () =>
            {
                var o = Options(32, GenerateArabic);
                o.ColorFont = true;
                return o;
            }),
            new("generate/notoemoji/color-32", NotoEmoji, () =>
            {
                var o = Options(32, NotoEmoji.Glyphs);
                o.ColorFont = true;
                return o;
            }, Reference: true),

            // Phase 168 opt-ins: vector outlines (composited, aliased, routed to a channel; round,
            // square and diamond pens; a CFF face) and synthetic width/italic angle.
            new("generate/roboto/outline-vector-28-w2", Roboto, () => Vector(Options(28, GenerateText), 2), Reference: true),
            new("generate/roboto/outline-vector-none-24-w2", Roboto, () =>
            {
                var o = Vector(Options(24, GenerateText), 2);
                o.AntiAlias = AntiAliasMode.None;
                return o;
            }),
            new("generate/roboto/outline-vector-routed-28-w3-square", Roboto, () =>
            {
                var o = Vector(Options(28, GenerateText), 3);
                o.OutlineShape = OutlineShape.Square;
                o.Channels = new ChannelConfig(Alpha: ChannelContent.Outline);
                return o;
            }),
            new("generate/sourceserif/outline-vector-shadow-28-w2-plus", SourceSerif, () =>
            {
                var o = Vector(Options(28, GenerateText), 2);
                o.OutlineShape = OutlineShape.Plus;
                o.ShadowOffsetX = 2;
                o.ShadowOffsetY = 2;
                o.ShadowBlur = 1;
                return o;
            }),
            new("generate/roboto/width75-italic20-28", Roboto, () =>
            {
                var o = Options(28, GenerateText);
                o.WidthPercent = 75;
                o.Italic = true;
                o.ItalicAngle = 20f;
                return o;
            }, Reference: true),
        };

        static FontGeneratorOptions Vector(FontGeneratorOptions o, int width)
        {
            o.Outline = width;
            o.OutlineMode = OutlineMode.Vector;
            return o;
        }

        return cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    /// <summary>A file-system-safe name for a case id ("roboto/grayscale/16px" -> "roboto_grayscale_16px").</summary>
    public static string FileName(string id) =>
        id.Replace('/', '_').Replace('=', '-').Replace('@', '_');

    internal static Dictionary<string, float> Axes(string instance) =>
        instance.Split(',').Select(p => p.Split('=')).ToDictionary(
            p => p[0], p => float.Parse(p[1], CultureInfo.InvariantCulture));

    private static string Px(float size) => size.ToString(CultureInfo.InvariantCulture) + "px";

    private static string Name(AntiAliasMode mode) => mode.ToString().ToLowerInvariant();

    private static string Name(LcdFilter filter) => filter.ToString().ToLowerInvariant();

    private static string Name(LcdSubpixelOrder order) => order.ToString().ToLowerInvariant();
}
