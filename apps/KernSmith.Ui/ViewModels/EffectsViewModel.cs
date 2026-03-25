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
    public string OutlineColor { get => Get<string>(); set => Set(value); }

    // Shadow
    public bool ShadowEnabled { get => Get<bool>(); set => Set(value); }
    public int ShadowOffsetX { get => Get<int>(); set => Set(value); }
    public int ShadowOffsetY { get => Get<int>(); set => Set(value); }
    public int ShadowBlur { get => Get<int>(); set => Set(value); }
    public string ShadowColor { get => Get<string>(); set => Set(value); }
    public int ShadowOpacity { get => Get<int>(); set => Set(value); }
    public bool HardShadow { get => Get<bool>(); set => Set(value); }

    // Gradient
    public bool GradientEnabled { get => Get<bool>(); set => Set(value); }
    public string GradientStartColor { get => Get<string>(); set => Set(value); }
    public string GradientEndColor { get => Get<string>(); set => Set(value); }
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
        OutlineColor = "#000000";
        ShadowColor = "#000000";
        GradientStartColor = "#FFFFFF";
        GradientEndColor = "#000000";
        GradientAngle = 90;
        FallbackCharacter = "?";
    }

    /// <summary>Parses a hex color string (#RRGGBB) into R, G, B bytes. Returns black on invalid input.</summary>
    public static (byte R, byte G, byte B) ParseHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return (0, 0, 0);
        var h = hex.StartsWith('#') ? hex[1..] : hex;
        if (h.Length == 6 &&
            byte.TryParse(h[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(h[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(h[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return (r, g, b);
        }
        return (0, 0, 0);
    }

    /// <summary>Formats R, G, B bytes as a hex color string (#RRGGBB).</summary>
    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
