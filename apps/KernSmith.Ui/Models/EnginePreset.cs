namespace KernSmith.Ui.Models;

public record EnginePreset(
    string Name,
    string ShortName,
    int MaxWidth, int MaxHeight,
    bool PowerOfTwo, bool AutofitTexture,
    int Padding, int Spacing,
    string DescriptorFormat, // "Text", "Xml", "Binary"
    bool IncludeKerning,
    string Description);

public static class EnginePresets
{
    public static readonly EnginePreset Unity = new("Unity", "UY", 2048, 2048, true, true, 1, 1, "Xml", true, "Unity TextMesh Pro compatible");
    public static readonly EnginePreset Godot = new("Godot", "GD", 1024, 1024, true, true, 1, 1, "Text", true, "Godot BMFont import compatible");
    public static readonly EnginePreset MonoGame = new("MonoGame", "MG", 1024, 1024, true, true, 1, 1, "Xml", true, "MonoGame SpriteFont compatible");
    public static readonly EnginePreset Unreal = new("Unreal", "UR", 2048, 2048, true, true, 2, 2, "Text", true, "Unreal Engine UMG compatible");
    public static readonly EnginePreset Phaser = new("Phaser", "PH", 512, 512, true, true, 1, 1, "Xml", false, "Phaser.js bitmap font compatible");
    public static readonly EnginePreset Custom = new("Custom", "CU", 1024, 1024, true, true, 1, 1, "Text", true, "Custom configuration");

    public static readonly IReadOnlyList<EnginePreset> All = new[] { Unity, Godot, MonoGame, Unreal, Phaser, Custom };
}
