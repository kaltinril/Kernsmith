using Gum.Mvvm;
using KernSmith.Font.Tables;

namespace KernSmith.Ui.ViewModels;

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

    // Shadow
    public bool ShadowEnabled { get => Get<bool>(); set => Set(value); }
    public int ShadowOffsetX { get => Get<int>(); set => Set(value); }
    public int ShadowOffsetY { get => Get<int>(); set => Set(value); }
    public int ShadowBlur { get => Get<int>(); set => Set(value); }

    // SDF
    public bool SdfEnabled { get => Get<bool>(); set => Set(value); }

    // Color Font
    public bool ColorFontEnabled { get => Get<bool>(); set => Set(value); }

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
        FallbackCharacter = "?";
    }
}
