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
}
