using KernSmith.Font.Models;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// Intermediate result of the rasterization phase, before atlas packing.
/// </summary>
internal sealed class RasterizationResult
{
    public required FontInfo FontInfo { get; init; }
    public required List<RasterizedGlyph> Glyphs { get; init; }
    public required List<int> Codepoints { get; init; }
    public required List<int> FailedCodepoints { get; init; }
    public required FontGeneratorOptions Options { get; init; }
}
