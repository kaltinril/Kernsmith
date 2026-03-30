using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Output;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// Fluent builder for configuring and generating bitmap fonts.
/// </summary>
public sealed class BmFontBuilder
{
    private readonly FontGeneratorOptions _options = new();
    private byte[]? _fontData;
    private string? _fontPath;
    private string? _systemFontFamily;

    internal BmFontBuilder() { }

    /// <summary>Loads settings from a .bmfc config file as a starting point.</summary>
    /// <param name="bmfcPath">Path to the .bmfc configuration file.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder FromConfig(string bmfcPath)
    {
        ArgumentNullException.ThrowIfNull(bmfcPath);
        return FromConfig(BmfcConfigReader.Read(bmfcPath));
    }

    /// <summary>Loads settings from a parsed .bmfc config as a starting point.</summary>
    /// <param name="config">The parsed .bmfc configuration.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder FromConfig(BmfcConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Copy options from config
        CopyOptions(config.Options);

        // Set font source
        if (!string.IsNullOrEmpty(config.FontFile))
        {
            _fontPath = config.FontFile;
            _fontData = null;
            _systemFontFamily = null;
        }
        else if (!string.IsNullOrEmpty(config.FontName))
        {
            _systemFontFamily = config.FontName;
            _fontData = null;
            _fontPath = null;
        }

        return this;
    }

    /// <summary>Loads a font from raw byte data.</summary>
    /// <param name="fontData">The font file bytes.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithFont(byte[] fontData)
    {
        _fontData = fontData;
        _fontPath = null;
        _systemFontFamily = null;
        return this;
    }

    /// <summary>Loads a font from a file path.</summary>
    /// <param name="fontPath">Path to a .ttf, .otf, or .woff file.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithFont(string fontPath)
    {
        _fontPath = fontPath;
        _fontData = null;
        _systemFontFamily = null;
        return this;
    }

    /// <summary>Uses a system-installed font by family name (e.g., "Arial").</summary>
    /// <param name="familyName">Font family name.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithSystemFont(string familyName)
    {
        _systemFontFamily = familyName;
        _fontData = null;
        _fontPath = null;
        return this;
    }

    /// <summary>Sets the font size in points.</summary>
    /// <param name="size">Font size in points.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithSize(int size) { _options.Size = size; return this; }

    /// <summary>Sets which characters to include in the output.</summary>
    /// <param name="characters">The character set to render.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithCharacters(CharacterSet characters) { _options.Characters = characters; return this; }

    /// <summary>Requests bold. Uses a native bold face when available, falling back to synthetic.</summary>
    /// <param name="bold">Enable bold.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithBold(bool bold = true) { _options.Bold = bold; return this; }

    /// <summary>Requests italic. Uses a native italic face when available, falling back to synthetic.</summary>
    /// <param name="italic">Enable italic.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithItalic(bool italic = true) { _options.Italic = italic; return this; }

    /// <summary>Forces synthetic bold even when a native bold face exists.</summary>
    /// <param name="force">Enable forced synthetic bold.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithForceSyntheticBold(bool force = true) { _options.Bold = true; _options.ForceSyntheticBold = force; return this; }

    /// <summary>Forces synthetic italic even when a native italic face exists.</summary>
    /// <param name="force">Enable forced synthetic italic.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithForceSyntheticItalic(bool force = true) { _options.Italic = true; _options.ForceSyntheticItalic = force; return this; }

    /// <summary>Sets the anti-aliasing mode.</summary>
    /// <param name="mode">Anti-aliasing mode.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithAntiAlias(AntiAliasMode mode) { _options.AntiAlias = mode; return this; }

    /// <summary>Sets the max texture size (both width and height) in pixels.</summary>
    /// <param name="size">Max width and height in pixels.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithMaxTextureSize(int size) { _options.MaxTextureSize = size; return this; }

    /// <summary>Sets the max texture width and height separately.</summary>
    /// <param name="width">Max texture width in pixels.</param>
    /// <param name="height">Max texture height in pixels.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithMaxTextureSize(int width, int height) { _options.MaxTextureWidth = width; _options.MaxTextureHeight = height; return this; }

    /// <summary>Sets per-side glyph padding in pixels.</summary>
    /// <param name="up">Top padding.</param>
    /// <param name="right">Right padding.</param>
    /// <param name="down">Bottom padding.</param>
    /// <param name="left">Left padding.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPadding(int up, int right, int down, int left) { _options.Padding = new Padding(up, right, down, left); return this; }

    /// <summary>Sets uniform glyph padding on all sides.</summary>
    /// <param name="all">Padding in pixels for every side.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPadding(int all) { _options.Padding = new Padding(all); return this; }

    /// <summary>Sets the gap between glyphs in the atlas.</summary>
    /// <param name="horizontal">Horizontal gap in pixels.</param>
    /// <param name="vertical">Vertical gap in pixels.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithSpacing(int horizontal, int vertical) { _options.Spacing = new Spacing(horizontal, vertical); return this; }

    /// <summary>Sets uniform glyph spacing in the atlas.</summary>
    /// <param name="both">Gap in pixels for both axes.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithSpacing(int both) { _options.Spacing = new Spacing(both); return this; }

    /// <summary>Sets the atlas packing algorithm.</summary>
    /// <param name="algorithm">Packing algorithm.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPackingAlgorithm(PackingAlgorithm algorithm) { _options.PackingAlgorithm = algorithm; return this; }

    /// <summary>If true, includes kerning pairs in the output.</summary>
    /// <param name="kerning">Enable kerning.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithKerning(bool kerning = true) { _options.Kerning = kerning; return this; }

    /// <summary>Adds an outline around each glyph.</summary>
    /// <param name="outline">Outline width in pixels.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithOutline(int outline) { _options.Outline = outline; return this; }

    /// <summary>Adds a colored outline around each glyph.</summary>
    /// <param name="width">Outline width in pixels.</param>
    /// <param name="r">Red (0-255).</param>
    /// <param name="g">Green (0-255).</param>
    /// <param name="b">Blue (0-255).</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithOutline(int width, byte r, byte g = 0, byte b = 0)
    {
        _options.Outline = width;
        _options.OutlineR = r;
        _options.OutlineG = g;
        _options.OutlineB = b;
        return this;
    }

    /// <summary>If true, generates signed distance field (SDF) output instead of bitmaps.</summary>
    /// <param name="sdf">Enable SDF.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithSdf(bool sdf = true) { _options.Sdf = sdf; return this; }

    /// <summary>If true, rounds texture dimensions up to powers of two.</summary>
    /// <param name="powerOfTwo">Enable power-of-two sizing.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPowerOfTwo(bool powerOfTwo = true) { _options.PowerOfTwo = powerOfTwo; return this; }

    /// <summary>Sets the DPI used for font size calculation.</summary>
    /// <param name="dpi">Dots per inch.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithDpi(int dpi) { _options.Dpi = dpi; return this; }

    /// <summary>Selects which face to use in a font collection (.ttc) file.</summary>
    /// <param name="faceIndex">Zero-based face index.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithFaceIndex(int faceIndex) { _options.FaceIndex = faceIndex; return this; }

    /// <summary>If true, packs multiple glyphs into separate RGBA channels to save space.</summary>
    /// <param name="channelPacking">Enable channel packing.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithChannelPacking(bool channelPacking = true) { _options.ChannelPacking = channelPacking; return this; }

    /// <summary>If true, renders color font layers (emoji, etc.).</summary>
    /// <param name="colorFont">Enable color font rendering.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithColorFont(bool colorFont = true) { _options.ColorFont = colorFont; return this; }

    /// <summary>Selects which CPAL color palette to use for color fonts.</summary>
    /// <param name="index">Zero-based palette index.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithColorPaletteIndex(int index) { _options.ColorPaletteIndex = index; return this; }

    /// <summary>Sets a variable font axis value (e.g., "wght" = 700 for bold weight).</summary>
    /// <param name="tag">Four-character axis tag like "wght" or "wdth".</param>
    /// <param name="value">Axis value.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithVariationAxis(string tag, float value)
    {
        _options.VariationAxes ??= new Dictionary<string, float>();
        _options.VariationAxes[tag] = value;
        return this;
    }

    /// <summary>Uses a custom rasterizer instead of the default FreeType one.</summary>
    /// <param name="rasterizer">Rasterizer to use.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithRasterizer(IRasterizer rasterizer) { _options.Rasterizer = rasterizer; return this; }

    /// <summary>Uses a custom atlas packer instead of the default.</summary>
    /// <param name="packer">Packer to use.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPacker(IAtlasPacker packer) { _options.Packer = packer; return this; }

    /// <summary>Uses a custom atlas encoder instead of the default.</summary>
    /// <param name="encoder">Encoder to use.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithEncoder(IAtlasEncoder encoder) { _options.AtlasEncoder = encoder; return this; }

    /// <summary>Uses a custom font reader instead of the default.</summary>
    /// <param name="reader">Font reader to use.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithFontReader(IFontReader reader) { _options.FontReader = reader; return this; }

    /// <summary>Sets the super-sampling level (1 = off, 2 = 2x, 4 = 4x). Higher values give smoother edges.</summary>
    /// <param name="level">Super-sampling multiplier.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithSuperSampling(int level) { _options.SuperSampleLevel = level; return this; }

    /// <summary>Sets the character shown when a glyph is missing from the font.</summary>
    /// <param name="fallbackChar">Fallback character.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithFallbackCharacter(char fallbackChar) { _options.FallbackCharacter = fallbackChar; return this; }

    /// <summary>Sets the fallback codepoint used when a requested character is not available in the font.
    /// Supports supplementary plane characters (above U+FFFF) unlike <see cref="WithFallbackCharacter(char)"/>.</summary>
    /// <param name="codepoint">Unicode codepoint to use as fallback (e.g., 0x25A1 for '&#x25A1;').</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithFallbackCodepoint(int codepoint)
    {
        if (codepoint < 0 || codepoint > 0x10FFFF)
            throw new ArgumentOutOfRangeException(nameof(codepoint), "Codepoint must be between 0 and 0x10FFFF.");
        if (codepoint >= 0xD800 && codepoint <= 0xDFFF)
            throw new ArgumentOutOfRangeException(nameof(codepoint), "Surrogate codepoints (U+D800 to U+DFFF) are not valid Unicode scalar values.");
        _options.FallbackCodepoint = codepoint;
        return this;
    }

    /// <summary>Sets the output texture format (PNG, TGA, or DDS).</summary>
    /// <param name="format">Texture format.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithTextureFormat(TextureFormat format) { _options.TextureFormat = format; return this; }

    /// <summary>If true, enables font hinting for sharper small text.</summary>
    /// <param name="enable">Enable hinting.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithHinting(bool enable = true) { _options.EnableHinting = enable; return this; }

    /// <summary>If true, shrinks the texture to fit the glyphs tightly.</summary>
    /// <param name="autofit">Enable auto-fit.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithAutofitTexture(bool autofit = true) { _options.AutofitTexture = autofit; return this; }

    /// <summary>If true, makes all glyph cells the same height.</summary>
    /// <param name="equalize">Enable equal cell heights.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithEqualizeCellHeights(bool equalize = true) { _options.EqualizeCellHeights = equalize; return this; }

    /// <summary>If true, forces all glyph x/y offsets to zero.</summary>
    /// <param name="force">Enable zeroed offsets.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithForceOffsetsToZero(bool force = true) { _options.ForceOffsetsToZero = force; return this; }

    /// <summary>Sets how each RGBA channel is used in the output texture.</summary>
    /// <param name="config">Channel configuration.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithChannels(ChannelConfig config) { _options.Channels = config; return this; }

    /// <summary>Sets what each RGBA channel contains and whether to invert it.</summary>
    /// <param name="alpha">What the alpha channel holds.</param>
    /// <param name="red">What the red channel holds.</param>
    /// <param name="green">What the green channel holds.</param>
    /// <param name="blue">What the blue channel holds.</param>
    /// <param name="invertAlpha">Invert alpha channel.</param>
    /// <param name="invertRed">Invert red channel.</param>
    /// <param name="invertGreen">Invert green channel.</param>
    /// <param name="invertBlue">Invert blue channel.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithChannels(
        ChannelContent alpha = ChannelContent.Glyph,
        ChannelContent red = ChannelContent.Glyph,
        ChannelContent green = ChannelContent.Glyph,
        ChannelContent blue = ChannelContent.Glyph,
        bool invertAlpha = false,
        bool invertRed = false,
        bool invertGreen = false,
        bool invertBlue = false)
    {
        _options.Channels = new ChannelConfig(alpha, red, green, blue, invertAlpha, invertRed, invertGreen, invertBlue);
        return this;
    }

    /// <summary>Scales glyph height as a percentage. 100 = normal, 200 = double height.</summary>
    /// <param name="percent">Height as a percentage.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithHeightPercent(int percent) { _options.HeightPercent = percent; return this; }

    /// <summary>Adds a custom bitmap for a specific character code.</summary>
    /// <param name="codepoint">Unicode character code to replace.</param>
    /// <param name="glyph">The custom glyph data.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithCustomGlyph(int codepoint, CustomGlyph glyph)
    {
        _options.CustomGlyphs ??= new Dictionary<int, CustomGlyph>();
        _options.CustomGlyphs[codepoint] = glyph;
        return this;
    }

    /// <summary>Adds a custom bitmap for a specific character code from raw pixels.</summary>
    /// <param name="codepoint">Unicode character code to replace.</param>
    /// <param name="width">Glyph width in pixels.</param>
    /// <param name="height">Glyph height in pixels.</param>
    /// <param name="pixelData">Raw pixel data.</param>
    /// <param name="format">Pixel format of the data.</param>
    /// <param name="xAdvance">Custom horizontal advance, or null to auto-calculate.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithCustomGlyph(int codepoint, int width, int height, byte[] pixelData, PixelFormat format = PixelFormat.Rgba32, int? xAdvance = null)
    {
        return WithCustomGlyph(codepoint, new CustomGlyph(width, height, pixelData, format, xAdvance));
    }

    /// <summary>If true, scales custom glyph advances to match the font's character height.</summary>
    /// <param name="match">Enable height matching.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithMatchCharHeight(bool match = true) { _options.MatchCharHeight = match; return this; }

    /// <summary>Sets the expected packing efficiency for atlas size estimation. 0.0 = worst, 1.0 = perfect.</summary>
    /// <param name="efficiency">Packing efficiency between 0.0 and 1.0.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPackingEfficiency(float efficiency) { _options.PackingEfficiencyHint = efficiency; return this; }

    /// <summary>Adds a drop shadow behind each glyph.</summary>
    /// <param name="offsetX">Horizontal offset in pixels.</param>
    /// <param name="offsetY">Vertical offset in pixels.</param>
    /// <param name="blur">Blur radius in pixels.</param>
    /// <param name="color">Shadow color as (R, G, B); defaults to black.</param>
    /// <param name="opacity">Shadow opacity, 0.0 to 1.0.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithShadow(int offsetX = 2, int offsetY = 2, int blur = 0,
        (byte R, byte G, byte B)? color = null, float opacity = 1.0f)
    {
        var c = color ?? (0, 0, 0);
        _options.ShadowOffsetX = offsetX;
        _options.ShadowOffsetY = offsetY;
        _options.ShadowBlur = blur;
        _options.ShadowR = c.R;
        _options.ShadowG = c.G;
        _options.ShadowB = c.B;
        _options.ShadowOpacity = opacity;
        return this;
    }

    /// <summary>Uses a hard (binarized) shadow silhouette instead of the antialiased glyph alpha.</summary>
    /// <param name="enabled">Enable hard shadow.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithHardShadow(bool enabled = true) { _options.HardShadow = enabled; return this; }

    /// <summary>Applies a color gradient across all glyphs.</summary>
    /// <param name="startColor">Start color as (R, G, B).</param>
    /// <param name="endColor">End color as (R, G, B).</param>
    /// <param name="angleDegrees">Angle in degrees. 0 = left-to-right, 90 = top-to-bottom.</param>
    /// <param name="midpoint">Where the midpoint falls, 0.0 to 1.0.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithGradient((byte R, byte G, byte B) startColor, (byte R, byte G, byte B) endColor, float angleDegrees = 90f, float midpoint = 0.5f)
    {
        _options.GradientStartR = startColor.R;
        _options.GradientStartG = startColor.G;
        _options.GradientStartB = startColor.B;
        _options.GradientEndR = endColor.R;
        _options.GradientEndG = endColor.G;
        _options.GradientEndB = endColor.B;
        _options.GradientAngle = angleDegrees;
        _options.GradientMidpoint = midpoint;
        return this;
    }

    /// <summary>Adds a custom post-processing step that runs on each glyph after rasterization.</summary>
    /// <param name="processor">The post-processor to add.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithPostProcessor(IGlyphPostProcessor processor)
    {
        var list = _options.PostProcessors?.ToList() ?? new List<IGlyphPostProcessor>();
        list.Add(processor);
        _options.PostProcessors = list;
        return this;
    }

    /// <summary>Enables collection of pipeline performance metrics on the result.</summary>
    /// <param name="collect">Whether to collect metrics (default true).</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithCollectMetrics(bool collect = true) { _options.CollectMetrics = collect; return this; }

    /// <summary>Sets the rasterizer backend to use for glyph rendering.</summary>
    /// <param name="backend">The backend to use.</param>
    /// <returns>This builder.</returns>
    public BmFontBuilder WithBackend(RasterizerBackend backend) { _options.Backend = backend; return this; }

    private void CopyOptions(FontGeneratorOptions source)
    {
        _options.Size = source.Size;
        _options.Characters = source.Characters;
        _options.Bold = source.Bold;
        _options.Italic = source.Italic;
        _options.ForceSyntheticBold = source.ForceSyntheticBold;
        _options.ForceSyntheticItalic = source.ForceSyntheticItalic;
        _options.AntiAlias = source.AntiAlias;
        _options.MaxTextureWidth = source.MaxTextureWidth;
        _options.MaxTextureHeight = source.MaxTextureHeight;
        _options.Padding = source.Padding;
        _options.Spacing = source.Spacing;
        _options.PackingAlgorithm = source.PackingAlgorithm;
        _options.Kerning = source.Kerning;
        _options.Outline = source.Outline;
        _options.OutlineR = source.OutlineR;
        _options.OutlineG = source.OutlineG;
        _options.OutlineB = source.OutlineB;
        _options.Sdf = source.Sdf;
        _options.PowerOfTwo = source.PowerOfTwo;
        _options.Dpi = source.Dpi;
        _options.FaceIndex = source.FaceIndex;
        _options.ChannelPacking = source.ChannelPacking;
        _options.ColorFont = source.ColorFont;
        _options.ColorPaletteIndex = source.ColorPaletteIndex;
        _options.VariationAxes = source.VariationAxes != null ? new Dictionary<string, float>(source.VariationAxes) : null;
        _options.SuperSampleLevel = source.SuperSampleLevel;
        _options.FallbackCharacter = source.FallbackCharacter;
        _options.FallbackCodepoint = source.FallbackCodepoint;
        _options.TextureFormat = source.TextureFormat;
        _options.EnableHinting = source.EnableHinting;
        _options.AutofitTexture = source.AutofitTexture;
        _options.EqualizeCellHeights = source.EqualizeCellHeights;
        _options.ForceOffsetsToZero = source.ForceOffsetsToZero;
        _options.Channels = source.Channels;
        _options.HeightPercent = source.HeightPercent;
        _options.MatchCharHeight = source.MatchCharHeight;
        _options.PackingEfficiencyHint = source.PackingEfficiencyHint;
        _options.ShadowOffsetX = source.ShadowOffsetX;
        _options.ShadowOffsetY = source.ShadowOffsetY;
        _options.ShadowBlur = source.ShadowBlur;
        _options.ShadowR = source.ShadowR;
        _options.ShadowG = source.ShadowG;
        _options.ShadowB = source.ShadowB;
        _options.ShadowOpacity = source.ShadowOpacity;
        _options.HardShadow = source.HardShadow;
        _options.GradientStartR = source.GradientStartR;
        _options.GradientStartG = source.GradientStartG;
        _options.GradientStartB = source.GradientStartB;
        _options.GradientEndR = source.GradientEndR;
        _options.GradientEndG = source.GradientEndG;
        _options.GradientEndB = source.GradientEndB;
        _options.GradientAngle = source.GradientAngle;
        _options.GradientMidpoint = source.GradientMidpoint;
        _options.CollectMetrics = source.CollectMetrics;
        _options.CustomGlyphs = source.CustomGlyphs != null ? new Dictionary<int, CustomGlyph>(source.CustomGlyphs) : null;
        _options.PostProcessors = source.PostProcessors?.ToList();
        _options.Backend = source.Backend;
        _options.Rasterizer = source.Rasterizer;
        _options.Packer = source.Packer;
        _options.AtlasEncoder = source.AtlasEncoder;
        _options.FontReader = source.FontReader;
    }

    /// <summary>Generates the bitmap font, returning the descriptor model and atlas texture pages.</summary>
    /// <returns>The generated bitmap font result.</returns>
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
