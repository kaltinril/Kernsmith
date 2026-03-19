using Bmfontier.Atlas;
using Bmfontier.Font;
using Bmfontier.Rasterizer;

namespace Bmfontier;

/// <summary>
/// Configuration options for BMFont generation.
/// </summary>
public class FontGeneratorOptions
{
    public int Size { get; set; } = 32;
    public CharacterSet Characters { get; set; } = CharacterSet.Ascii;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;
    public int MaxTextureSize { get; set; } = 1024;
    public Padding Padding { get; set; } = new Padding(0, 0, 0, 0);
    public Spacing Spacing { get; set; } = new Spacing(1, 1);
    public PackingAlgorithm PackingAlgorithm { get; set; } = PackingAlgorithm.MaxRects;
    public bool Kerning { get; set; } = true;
    public int Outline { get; set; }
    public bool Sdf { get; set; }
    public bool PowerOfTwo { get; set; } = true;
    public int Dpi { get; set; } = 72;
    public int FaceIndex { get; set; }
    public bool ChannelPacking { get; set; }

    // Swappable components (null = use defaults)
    public IFontReader? FontReader { get; set; }
    public IRasterizer? Rasterizer { get; set; }
    public IAtlasPacker? Packer { get; set; }
    public IAtlasEncoder? AtlasEncoder { get; set; }
    public IReadOnlyList<IGlyphPostProcessor>? PostProcessors { get; set; }
}
