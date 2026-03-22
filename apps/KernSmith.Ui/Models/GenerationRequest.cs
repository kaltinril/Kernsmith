using KernSmith;

namespace KernSmith.Ui.Models;

public record GenerationRequest
{
    public byte[]? FontData { get; init; }
    public string? FontFilePath { get; init; }
    public string? SystemFontFamily { get; init; }
    public FontSourceKind SourceKind { get; init; }
    public int FontSize { get; init; }
    public CharacterSet Characters { get; init; } = CharacterSet.Ascii;

    // Atlas config
    public int MaxWidth { get; init; } = 1024;
    public int MaxHeight { get; init; } = 1024;
    public bool PowerOfTwo { get; init; } = true;
    public bool AutofitTexture { get; init; } = true;
    public int PaddingUp { get; init; } = 1;
    public int PaddingRight { get; init; } = 1;
    public int PaddingDown { get; init; } = 1;
    public int PaddingLeft { get; init; } = 1;
    public int SpacingH { get; init; } = 1;
    public int SpacingV { get; init; } = 1;
    public bool IncludeKerning { get; init; } = true;

    // Effects
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool AntiAlias { get; init; } = true;
    public bool Hinting { get; init; } = true;
    public int SuperSampleLevel { get; init; } = 1;
    public bool OutlineEnabled { get; init; }
    public int OutlineWidth { get; init; } = 1;
    public byte OutlineColorR { get; init; }
    public byte OutlineColorG { get; init; }
    public byte OutlineColorB { get; init; }
    public bool ShadowEnabled { get; init; }
    public int ShadowOffsetX { get; init; } = 2;
    public int ShadowOffsetY { get; init; } = 2;
    public int ShadowBlur { get; init; }
    public byte ShadowColorR { get; init; }
    public byte ShadowColorG { get; init; }
    public byte ShadowColorB { get; init; }
    public int ShadowOpacity { get; init; } = 100;
    public bool GradientEnabled { get; init; }
    public byte GradientStartR { get; init; } = 255;
    public byte GradientStartG { get; init; } = 255;
    public byte GradientStartB { get; init; } = 255;
    public byte GradientEndR { get; init; }
    public byte GradientEndG { get; init; }
    public byte GradientEndB { get; init; }
    public int GradientAngle { get; init; } = 90;
    public bool ChannelPackingEnabled { get; init; }
    public bool SdfEnabled { get; init; }
    public bool ColorFontEnabled { get; init; }
    public int PackingAlgorithmIndex { get; init; }

    // TTC face selection
    public int FaceIndex { get; init; }

    // Variable font axes
    public Dictionary<string, float>? VariationAxisValues { get; init; }

    // Fallback character
    public string FallbackCharacter { get; init; } = "?";
}
