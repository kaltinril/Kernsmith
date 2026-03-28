using KernSmith;
using KernSmith.Rasterizer;

namespace KernSmith.Ui.Models;

/// <summary>
/// Immutable snapshot of all UI settings needed to generate a bitmap font.
/// Created by <see cref="ViewModels.MainViewModel"/> and consumed by <see cref="Services.GenerationService"/>.
/// </summary>
public record GenerationRequest
{
    // --- Font source (exactly one of FontData/FontFilePath/SystemFontFamily is used) ---

    /// <summary>Raw font file bytes. Used when the font was loaded into memory.</summary>
    public byte[]? FontData { get; init; }
    /// <summary>Absolute path to the font file on disk.</summary>
    public string? FontFilePath { get; init; }
    /// <summary>System font family name for lookup via the OS font provider.</summary>
    public string? SystemFontFamily { get; init; }
    /// <summary>Determines which font source property is used for generation.</summary>
    public FontSourceKind SourceKind { get; init; }
    /// <summary>Font size in points. Valid range: 4..500.</summary>
    public int FontSize { get; init; }
    /// <summary>Set of Unicode codepoints to include in the generated font.</summary>
    public CharacterSet Characters { get; init; } = CharacterSet.Ascii;

    // --- Atlas configuration ---

    /// <summary>Maximum atlas texture width in pixels. Common values: 128..8192.</summary>
    public int MaxWidth { get; init; } = 1024;
    /// <summary>Maximum atlas texture height in pixels. Common values: 128..8192.</summary>
    public int MaxHeight { get; init; } = 1024;
    /// <summary>Constrain atlas dimensions to powers of two.</summary>
    public bool PowerOfTwo { get; init; } = true;
    /// <summary>Shrink the atlas to the smallest size that fits all packed glyphs.</summary>
    public bool AutofitTexture { get; init; } = true;
    /// <summary>Padding above each glyph in pixels. Range: 0..32.</summary>
    public int PaddingUp { get; init; } = 1;
    /// <summary>Padding to the right of each glyph in pixels. Range: 0..32.</summary>
    public int PaddingRight { get; init; } = 1;
    /// <summary>Padding below each glyph in pixels. Range: 0..32.</summary>
    public int PaddingDown { get; init; } = 1;
    /// <summary>Padding to the left of each glyph in pixels. Range: 0..32.</summary>
    public int PaddingLeft { get; init; } = 1;
    /// <summary>Horizontal spacing between glyphs in pixels. Range: 0..32.</summary>
    public int SpacingH { get; init; } = 1;
    /// <summary>Vertical spacing between glyphs in pixels. Range: 0..32.</summary>
    public int SpacingV { get; init; } = 1;
    /// <summary>Include GPOS/kern kerning pairs in the output.</summary>
    public bool IncludeKerning { get; init; } = true;

    // --- Effects ---

    /// <summary>Apply synthetic bold (only when the loaded font file is not already bold).</summary>
    public bool Bold { get; init; }
    /// <summary>Apply synthetic italic (only when the loaded font file is not already italic).</summary>
    public bool Italic { get; init; }
    /// <summary>Enable grayscale anti-aliasing for glyph rasterization.</summary>
    public bool AntiAlias { get; init; } = true;
    /// <summary>Enable font hinting for sharper rendering at small sizes.</summary>
    public bool Hinting { get; init; } = true;
    /// <summary>Super-sampling multiplier. Valid values: 1 (off), 2, or 4.</summary>
    public int SuperSampleLevel { get; init; } = 1;
    /// <summary>Whether to render an outline border around each glyph.</summary>
    public bool OutlineEnabled { get; init; }
    /// <summary>Outline thickness in pixels. Range: 1..10.</summary>
    public int OutlineWidth { get; init; } = 1;
    /// <summary>Outline color red component. Range: 0..255.</summary>
    public byte OutlineColorR { get; init; }
    /// <summary>Outline color green component. Range: 0..255.</summary>
    public byte OutlineColorG { get; init; }
    /// <summary>Outline color blue component. Range: 0..255.</summary>
    public byte OutlineColorB { get; init; }
    /// <summary>Whether to render a drop shadow behind each glyph.</summary>
    public bool ShadowEnabled { get; init; }
    /// <summary>Shadow horizontal offset in pixels. Range: -10..10.</summary>
    public int ShadowOffsetX { get; init; } = 2;
    /// <summary>Shadow vertical offset in pixels. Range: -10..10.</summary>
    public int ShadowOffsetY { get; init; } = 2;
    /// <summary>Shadow blur radius in pixels. Range: 0..10.</summary>
    public int ShadowBlur { get; init; }
    /// <summary>Shadow color red component. Range: 0..255.</summary>
    public byte ShadowColorR { get; init; }
    /// <summary>Shadow color green component. Range: 0..255.</summary>
    public byte ShadowColorG { get; init; }
    /// <summary>Shadow color blue component. Range: 0..255.</summary>
    public byte ShadowColorB { get; init; }
    /// <summary>Shadow opacity as a percentage. Range: 0..100.</summary>
    public int ShadowOpacity { get; init; } = 100;
    /// <summary>Use a hard (binarized) shadow silhouette instead of soft antialiased edges.</summary>
    public bool HardShadow { get; init; }
    /// <summary>Whether to apply a vertical color gradient across each glyph.</summary>
    public bool GradientEnabled { get; init; }
    /// <summary>Gradient start color red component. Range: 0..255.</summary>
    public byte GradientStartR { get; init; } = 255;
    /// <summary>Gradient start color green component. Range: 0..255.</summary>
    public byte GradientStartG { get; init; } = 255;
    /// <summary>Gradient start color blue component. Range: 0..255.</summary>
    public byte GradientStartB { get; init; } = 255;
    /// <summary>Gradient end color red component. Range: 0..255.</summary>
    public byte GradientEndR { get; init; }
    /// <summary>Gradient end color green component. Range: 0..255.</summary>
    public byte GradientEndG { get; init; }
    /// <summary>Gradient end color blue component. Range: 0..255.</summary>
    public byte GradientEndB { get; init; }
    /// <summary>Gradient angle in degrees. Range: 0..360.</summary>
    public int GradientAngle { get; init; } = 90;
    /// <summary>Pack glyph data into separate RGBA channels for multi-font atlases.</summary>
    public bool ChannelPackingEnabled { get; init; }
    /// <summary>Generate Signed Distance Field glyphs for scalable rendering.</summary>
    public bool SdfEnabled { get; init; }
    /// <summary>Render color glyphs from COLR/CPAL tables (e.g., emoji fonts).</summary>
    public bool ColorFontEnabled { get; init; }
    /// <summary>Index into the packing algorithm list: 0 = MaxRects, 1 = Skyline.</summary>
    public int PackingAlgorithmIndex { get; init; }

    // --- Rasterizer backend ---

    /// <summary>Which rasterizer backend to use for glyph rendering.</summary>
    public RasterizerBackend Backend { get; init; } = RasterizerBackend.FreeType;

    // --- TTC face selection ---

    /// <summary>Face index within a TrueType Collection (.ttc). 0 for single-face fonts.</summary>
    public int FaceIndex { get; init; }

    // --- Variable font axes ---

    /// <summary>Variation axis tag-to-value map for variable fonts (e.g., "wght" = 700).</summary>
    public Dictionary<string, float>? VariationAxisValues { get; init; }

    // --- Fallback character ---

    /// <summary>Character rendered for missing glyphs. Typically "?".</summary>
    public string FallbackCharacter { get; init; } = "?";
}
