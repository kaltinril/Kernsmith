namespace KernSmith.Ui.Models;

/// <summary>
/// Predefined atlas/output configuration for a specific game engine.
/// Applying a preset sets texture size, padding, spacing, descriptor format, and kerning.
/// </summary>
/// <param name="Name">Display name shown in UI when selected.</param>
/// <param name="ShortName">Abbreviated label for unselected preset buttons.</param>
/// <param name="MaxWidth">Maximum atlas texture width in pixels.</param>
/// <param name="MaxHeight">Maximum atlas texture height in pixels.</param>
/// <param name="PowerOfTwo">Whether atlas dimensions must be powers of two.</param>
/// <param name="AutofitTexture">Whether to shrink the atlas to fit the packed glyphs.</param>
/// <param name="Padding">Padding in pixels around each glyph (applied uniformly).</param>
/// <param name="Spacing">Spacing in pixels between glyphs (applied uniformly).</param>
/// <param name="DescriptorFormat">Output format name: "Text", "Xml", or "Binary".</param>
/// <param name="IncludeKerning">Whether to include kerning pairs in the output.</param>
/// <param name="Description">Human-readable description shown below the preset buttons.</param>
public record EnginePreset(
    string Name,
    string ShortName,
    int MaxWidth, int MaxHeight,
    bool PowerOfTwo, bool AutofitTexture,
    int Padding, int Spacing,
    string DescriptorFormat, // "Text", "Xml", "Binary"
    bool IncludeKerning,
    string Description);

/// <summary>
/// Built-in engine presets and the ordered list used to populate the UI.
/// </summary>
public static class EnginePresets
{
    public static readonly EnginePreset Unity = new("Unity", "UY", 2048, 2048, true, true, 1, 1, "Xml", true, "Unity TextMesh Pro compatible");
    public static readonly EnginePreset Godot = new("Godot", "GD", 1024, 1024, true, true, 1, 1, "Text", true, "Godot BMFont import compatible");
    public static readonly EnginePreset MonoGame = new("XNA", "XNA", 1024, 1024, true, true, 1, 1, "Xml", true, "XNA SpriteFont compatible");
    public static readonly EnginePreset Unreal = new("Unreal", "UR", 2048, 2048, true, true, 2, 2, "Text", true, "Unreal Engine UMG compatible");
    public static readonly EnginePreset Phaser = new("Phaser", "PH", 512, 512, true, true, 1, 1, "Xml", false, "Phaser.js bitmap font compatible");
    public static readonly EnginePreset Custom = new("Custom", "CU", 1024, 1024, true, true, 1, 1, "Text", true, "Custom configuration");

    public static readonly IReadOnlyList<EnginePreset> All = new[] { MonoGame, Unity, Godot, Unreal, Phaser, Custom };
}
