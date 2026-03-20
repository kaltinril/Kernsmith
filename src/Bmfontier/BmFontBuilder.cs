using Bmfontier.Atlas;
using Bmfontier.Font;
using Bmfontier.Output;
using Bmfontier.Rasterizer;

namespace Bmfontier;

/// <summary>
/// Fluent builder for BMFont generation. Syntactic sugar over FontGeneratorOptions.
/// </summary>
public sealed class BmFontBuilder
{
    private readonly FontGeneratorOptions _options = new();
    private byte[]? _fontData;
    private string? _fontPath;
    private string? _systemFontFamily;

    internal BmFontBuilder() { }

    public BmFontBuilder WithFont(byte[] fontData)
    {
        _fontData = fontData;
        _fontPath = null;
        _systemFontFamily = null;
        return this;
    }

    public BmFontBuilder WithFont(string fontPath)
    {
        _fontPath = fontPath;
        _fontData = null;
        _systemFontFamily = null;
        return this;
    }

    public BmFontBuilder WithSystemFont(string familyName)
    {
        _systemFontFamily = familyName;
        _fontData = null;
        _fontPath = null;
        return this;
    }

    public BmFontBuilder WithSize(int size) { _options.Size = size; return this; }
    public BmFontBuilder WithCharacters(CharacterSet characters) { _options.Characters = characters; return this; }
    public BmFontBuilder WithBold(bool bold = true) { _options.Bold = bold; return this; }
    public BmFontBuilder WithItalic(bool italic = true) { _options.Italic = italic; return this; }
    public BmFontBuilder WithAntiAlias(AntiAliasMode mode) { _options.AntiAlias = mode; return this; }
    public BmFontBuilder WithMaxTextureSize(int size) { _options.MaxTextureSize = size; return this; }
    public BmFontBuilder WithMaxTextureSize(int width, int height) { _options.MaxTextureWidth = width; _options.MaxTextureHeight = height; return this; }
    public BmFontBuilder WithPadding(int up, int right, int down, int left) { _options.Padding = new Padding(up, right, down, left); return this; }
    public BmFontBuilder WithPadding(int all) { _options.Padding = new Padding(all); return this; }
    public BmFontBuilder WithSpacing(int horizontal, int vertical) { _options.Spacing = new Spacing(horizontal, vertical); return this; }
    public BmFontBuilder WithSpacing(int both) { _options.Spacing = new Spacing(both); return this; }
    public BmFontBuilder WithPackingAlgorithm(PackingAlgorithm algorithm) { _options.PackingAlgorithm = algorithm; return this; }
    public BmFontBuilder WithKerning(bool kerning = true) { _options.Kerning = kerning; return this; }
    public BmFontBuilder WithOutline(int outline) { _options.Outline = outline; return this; }
    public BmFontBuilder WithSdf(bool sdf = true) { _options.Sdf = sdf; return this; }
    public BmFontBuilder WithPowerOfTwo(bool powerOfTwo = true) { _options.PowerOfTwo = powerOfTwo; return this; }
    public BmFontBuilder WithDpi(int dpi) { _options.Dpi = dpi; return this; }
    public BmFontBuilder WithFaceIndex(int faceIndex) { _options.FaceIndex = faceIndex; return this; }
    public BmFontBuilder WithChannelPacking(bool channelPacking = true) { _options.ChannelPacking = channelPacking; return this; }
    public BmFontBuilder WithColorFont(bool colorFont = true) { _options.ColorFont = colorFont; return this; }
    public BmFontBuilder WithColorPaletteIndex(int index) { _options.ColorPaletteIndex = index; return this; }
    public BmFontBuilder WithVariationAxis(string tag, float value)
    {
        _options.VariationAxes ??= new Dictionary<string, float>();
        _options.VariationAxes[tag] = value;
        return this;
    }

    public BmFontBuilder WithRasterizer(IRasterizer rasterizer) { _options.Rasterizer = rasterizer; return this; }
    public BmFontBuilder WithPacker(IAtlasPacker packer) { _options.Packer = packer; return this; }
    public BmFontBuilder WithEncoder(IAtlasEncoder encoder) { _options.AtlasEncoder = encoder; return this; }
    public BmFontBuilder WithFontReader(IFontReader reader) { _options.FontReader = reader; return this; }

    public BmFontBuilder WithSuperSampling(int level) { _options.SuperSampleLevel = level; return this; }
    public BmFontBuilder WithFallbackCharacter(char fallbackChar) { _options.FallbackCharacter = fallbackChar; return this; }
    public BmFontBuilder WithTextureFormat(TextureFormat format) { _options.TextureFormat = format; return this; }
    public BmFontBuilder WithHinting(bool enable = true) { _options.EnableHinting = enable; return this; }
    public BmFontBuilder WithAutofitTexture(bool autofit = true) { _options.AutofitTexture = autofit; return this; }
    public BmFontBuilder WithEqualizeCellHeights(bool equalize = true) { _options.EqualizeCellHeights = equalize; return this; }
    public BmFontBuilder WithForceOffsetsToZero(bool force = true) { _options.ForceOffsetsToZero = force; return this; }

    public BmFontBuilder WithShadow(int offsetX = 2, int offsetY = 2, int blur = 0,
        (byte R, byte G, byte B)? color = null, float opacity = 1.0f)
    {
        var c = color ?? (0, 0, 0);
        return WithPostProcessor(new ShadowPostProcessor(offsetX, offsetY, blur, c.R, c.G, c.B, opacity));
    }

    public BmFontBuilder WithGradient((byte R, byte G, byte B) startColor, (byte R, byte G, byte B) endColor, float angleDegrees = 90f, float midpoint = 0.5f)
        => WithPostProcessor(GradientPostProcessor.Create(startColor, endColor, angleDegrees, midpoint));

    public BmFontBuilder WithPostProcessor(IGlyphPostProcessor processor)
    {
        var list = _options.PostProcessors?.ToList() ?? new List<IGlyphPostProcessor>();
        list.Add(processor);
        _options.PostProcessors = list;
        return this;
    }

    public BmFontResult Build()
    {
        if (_systemFontFamily != null)
            return BmFont.GenerateFromSystem(_systemFontFamily, _options);

        if (_fontPath != null)
            return BmFont.Generate(_fontPath, _options);

        if (_fontData != null)
            return BmFont.Generate(_fontData, _options);

        throw new InvalidOperationException("No font specified. Call WithFont() or WithSystemFont() before Build().");
    }
}
