using Gum.Mvvm;
using KernSmith.Font.Tables;

namespace KernSmith.Ui.ViewModels;

/// <summary>
/// Holds all glyph rendering effect settings: font style (bold/italic/AA/hinting/super-sample),
/// outline, shadow, gradient, channel packing, SDF, color font, variable font axes, and
/// the fallback character.
/// </summary>
public class EffectsViewModel : ViewModel
{
    // Font Style (always active)
    public bool Bold { get => Get<bool>(); set => Set(value); }
    public bool Italic { get => Get<bool>(); set => Set(value); }
    public bool AntiAlias { get => Get<bool>(); set => Set(value); }
    public bool Hinting { get => Get<bool>(); set => Set(value); }
    public int SuperSampleLevel { get => Get<int>(); set => Set(value); }

    // Outline
    public bool OutlineEnabled { get => Get<bool>(); set => Set(value); }
    public int OutlineWidth { get => Get<int>(); set => Set(value); }
    public byte OutlineColorR { get => Get<byte>(); set => Set(value); }
    public byte OutlineColorG { get => Get<byte>(); set => Set(value); }
    public byte OutlineColorB { get => Get<byte>(); set => Set(value); }

    // Shadow
    public bool ShadowEnabled { get => Get<bool>(); set => Set(value); }
    public int ShadowOffsetX { get => Get<int>(); set => Set(value); }
    public int ShadowOffsetY { get => Get<int>(); set => Set(value); }
    public int ShadowBlur { get => Get<int>(); set => Set(value); }
    public byte ShadowColorR { get => Get<byte>(); set => Set(value); }
    public byte ShadowColorG { get => Get<byte>(); set => Set(value); }
    public byte ShadowColorB { get => Get<byte>(); set => Set(value); }
    public int ShadowOpacity { get => Get<int>(); set => Set(value); }

    // Gradient
    public bool GradientEnabled { get => Get<bool>(); set => Set(value); }
    public byte GradientStartR { get => Get<byte>(); set => Set(value); }
    public byte GradientStartG { get => Get<byte>(); set => Set(value); }
    public byte GradientStartB { get => Get<byte>(); set => Set(value); }
    public byte GradientEndR { get => Get<byte>(); set => Set(value); }
    public byte GradientEndG { get => Get<byte>(); set => Set(value); }
    public byte GradientEndB { get => Get<byte>(); set => Set(value); }
    public int GradientAngle { get => Get<int>(); set => Set(value); }

    // Channel Packing
    public bool ChannelPackingEnabled { get => Get<bool>(); set => Set(value); }

    // SDF
    public bool SdfEnabled { get => Get<bool>(); set => Set(value); }

    // Color Font
    public bool ColorFontEnabled { get => Get<bool>(); set => Set(value); }
    /// <summary>Whether the currently loaded font has color glyph tables (COLR/CPAL/CBDT).</summary>
    public bool HasColorGlyphs { get => Get<bool>(); set => Set(value); }

    // Variable Font
    public bool HasVariationAxes { get => Get<bool>(); set => Set(value); }
    public IReadOnlyList<VariationAxis>? VariationAxesList { get => Get<IReadOnlyList<VariationAxis>?>(); set => Set(value); }
    public Dictionary<string, float> VariationAxisValues { get; } = new();

    // Fallback Character
    public string FallbackCharacter { get => Get<string>(); set => Set(value); }

    public EffectsViewModel()
    {
        AntiAlias = true;
        Hinting = true;
        SuperSampleLevel = 1;
        OutlineWidth = 1;
        ShadowOffsetX = 2;
        ShadowOffsetY = 2;
        ShadowOpacity = 100;
        GradientStartR = 255;
        GradientStartG = 255;
        GradientStartB = 255;
        GradientAngle = 90;
        FallbackCharacter = "?";
    }
}
