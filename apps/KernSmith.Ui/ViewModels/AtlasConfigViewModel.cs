using Gum.Mvvm;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

public class AtlasConfigViewModel : ViewModel
{
    // Atlas size
    public int MaxWidth { get => Get<int>(); set => Set(value); }
    public int MaxHeight { get => Get<int>(); set => Set(value); }
    public bool PowerOfTwo { get => Get<bool>(); set => Set(value); }
    public bool AutofitTexture { get => Get<bool>(); set => Set(value); }

    // Padding
    public int PaddingUp { get => Get<int>(); set => Set(value); }
    public int PaddingRight { get => Get<int>(); set => Set(value); }
    public int PaddingDown { get => Get<int>(); set => Set(value); }
    public int PaddingLeft { get => Get<int>(); set => Set(value); }

    // Spacing
    public int SpacingH { get => Get<int>(); set => Set(value); }
    public int SpacingV { get => Get<int>(); set => Set(value); }

    // Output format
    public OutputFormat DescriptorFormat { get => Get<OutputFormat>(); set => Set(value); }
    public bool IncludeKerning { get => Get<bool>(); set => Set(value); }

    // Packing algorithm
    public int PackingAlgorithmIndex { get => Get<int>(); set => Set(value); }

    // Engine preset tracking
    public string SelectedPresetName { get => Get<string>(); set => Set(value); }

    public AtlasConfigViewModel()
    {
        MaxWidth = 1024;
        MaxHeight = 1024;
        PowerOfTwo = true;
        AutofitTexture = true;
        PaddingUp = 1;
        PaddingRight = 1;
        PaddingDown = 1;
        PaddingLeft = 1;
        SpacingH = 1;
        SpacingV = 1;
        DescriptorFormat = OutputFormat.Text;
        IncludeKerning = true;
        SelectedPresetName = "";
    }

    public void ApplyPreset(EnginePreset preset)
    {
        MaxWidth = preset.MaxWidth;
        MaxHeight = preset.MaxHeight;
        PowerOfTwo = preset.PowerOfTwo;
        AutofitTexture = preset.AutofitTexture;
        PaddingUp = preset.Padding;
        PaddingRight = preset.Padding;
        PaddingDown = preset.Padding;
        PaddingLeft = preset.Padding;
        SpacingH = preset.Spacing;
        SpacingV = preset.Spacing;
        IncludeKerning = preset.IncludeKerning;
        DescriptorFormat = Enum.TryParse<OutputFormat>(preset.DescriptorFormat, out var fmt)
            ? fmt : OutputFormat.Text;
        SelectedPresetName = preset.Name;
    }
}
