namespace KernSmith.Cli.Config;

/// <summary>
/// Intermediate representation of all parsed CLI/config settings.
/// </summary>
internal sealed class CliOptions
{
    // Font source (mutually exclusive)
    public string? FontPath { get; set; }
    public string? SystemFontName { get; set; }
    public int FaceIndex { get; set; }

    // Rendering
    public int? Size { get; set; }
    public int Dpi { get; set; } = 72;
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;
    public bool Sdf { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    // Characters
    public string? CharsetPreset { get; set; } = "ascii";
    public string? ExplicitChars { get; set; }
    public string? CharsFilePath { get; set; }
    public List<(int Start, int End)> UnicodeRanges { get; set; } = new();

    // Atlas
    public int MaxTextureSize { get; set; } = 1024;
    public int? MaxTextureWidth { get; set; }
    public int? MaxTextureHeight { get; set; }
    public Padding Padding { get; set; } = new(0);
    public Spacing Spacing { get; set; } = new(1);
    public bool? PowerOfTwo { get; set; } = true;
    public PackingAlgorithm PackingAlgorithm { get; set; } = PackingAlgorithm.MaxRects;
    public bool ChannelPacking { get; set; }
    public bool AutofitTexture { get; set; }
    public string? TextureFormat { get; set; }

    // Effects
    public int Outline { get; set; }
    public string? OutlineColor { get; set; }
    public string? GradientTop { get; set; }
    public string? GradientBottom { get; set; }
    public float GradientAngle { get; set; } = 90f;
    public float GradientMidpoint { get; set; } = 0.5f;
    public int ShadowOffsetX { get; set; }
    public int ShadowOffsetY { get; set; }
    public string? ShadowColor { get; set; }
    public int ShadowBlur { get; set; }

    // Kerning
    public bool? Kerning { get; set; } = true;

    // Rendering extras
    public int SuperSampleLevel { get; set; } = 1;
    public char? FallbackCharacter { get; set; }
    public bool? EnableHinting { get; set; }
    public bool EqualizeCellHeights { get; set; }
    public bool ForceOffsetsToZero { get; set; }
    public int HeightPercent { get; set; } = 100;
    public bool MatchCharHeight { get; set; }
    public bool ColorFont { get; set; }
    public int ColorPaletteIndex { get; set; }


    // Variable fonts
    public Dictionary<string, float> VariationAxes { get; set; } = new();
    public string? InstanceName { get; set; }

    // Output
    public string? OutputPath { get; set; }
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    // Meta
    public string? ConfigPath { get; set; }
    public string? SaveConfigPath { get; set; }
    public bool DryRun { get; set; }
    public bool ShowTime { get; set; }
    public bool ShowProfile { get; set; }
    public bool Verbose { get; set; }
    public bool Quiet { get; set; }
}
