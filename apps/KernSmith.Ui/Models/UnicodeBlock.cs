namespace KernSmith.Ui.Models;

public record UnicodeBlock(string Name, int Start, int End)
{
    public int Count => End - Start + 1;

    public static readonly IReadOnlyList<UnicodeBlock> StandardBlocks = new[]
    {
        new UnicodeBlock("Basic Latin", 0x0020, 0x007F),
        new UnicodeBlock("Latin-1 Supplement", 0x0080, 0x00FF),
        new UnicodeBlock("Latin Extended-A", 0x0100, 0x017F),
        new UnicodeBlock("Latin Extended-B", 0x0180, 0x024F),
        new UnicodeBlock("Greek and Coptic", 0x0370, 0x03FF),
        new UnicodeBlock("Cyrillic", 0x0400, 0x04FF),
        new UnicodeBlock("Arabic", 0x0600, 0x06FF),
        new UnicodeBlock("Devanagari", 0x0900, 0x097F),
        new UnicodeBlock("Thai", 0x0E00, 0x0E7F),
        new UnicodeBlock("CJK Unified Ideographs", 0x4E00, 0x9FFF),
        new UnicodeBlock("Hangul Syllables", 0xAC00, 0xD7AF),
        new UnicodeBlock("General Punctuation", 0x2000, 0x206F),
        new UnicodeBlock("Currency Symbols", 0x20A0, 0x20CF),
        new UnicodeBlock("Letterlike Symbols", 0x2100, 0x214F),
        new UnicodeBlock("Arrows", 0x2190, 0x21FF),
        new UnicodeBlock("Mathematical Operators", 0x2200, 0x22FF),
        new UnicodeBlock("Box Drawing", 0x2500, 0x257F),
        new UnicodeBlock("Block Elements", 0x2580, 0x259F),
        new UnicodeBlock("Geometric Shapes", 0x25A0, 0x25FF),
        new UnicodeBlock("Miscellaneous Symbols", 0x2600, 0x26FF),
    };
}
