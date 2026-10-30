using static KernSmith.Rasterizers.Native.Tests.SyntheticBitmapTables;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Whole synthetic bitmap fonts for the fuzz harness: every table format in one face.</summary>
internal static class SyntheticBitmapFonts
{
    /// <summary>Roboto without outlines, with CBDT strikes in every index format and PNG image format.</summary>
    public static byte[] Cbdt()
    {
        int a = RobotoGlyph('A'), g = RobotoGlyph('g'), o = RobotoGlyph('O'), one = RobotoGlyph('1'), amp = RobotoGlyph('&');
        CbdtGlyph Glyph(int id, byte shade) => new(id, Png(6, 5, (x, y) => (shade, (byte)(x * 40), (byte)(y * 50), (byte)(255 - (x * 20)))), 6, 5, 1, 5, 8);
        var (cblc, cbdt) = SyntheticBitmapTables.Cbdt(
            new CbdtStrike(12, 1, 17, [Glyph(a, 10), Glyph(g, 20)]),
            new CbdtStrike(18, 3, 18, [Glyph(o, 30), Glyph(a, 40)]),
            new CbdtStrike(24, 4, 17, [Glyph(one, 50), Glyph(amp, 60)]),
            new CbdtStrike(30, 2, 19, [Glyph(a, 70), Glyph(g, 80)]),
            new CbdtStrike(36, 5, 19, [Glyph(o, 90), Glyph(amp, 100)]));
        return BitmapOnlyRoboto(("CBLC", cblc), ("CBDT", cbdt));
    }

    /// <summary>Roboto (outlines kept) with two sbix strikes, a dupe and a non-PNG record.</summary>
    public static byte[] Sbix()
    {
        int a = RobotoGlyph('A'), g = RobotoGlyph('g'), o = RobotoGlyph('O'), one = RobotoGlyph('1');
        var sbix = SyntheticBitmapTables.Sbix(RobotoGlyphCount,
            new SbixStrike(16, 72, [new SbixEntry(a, 0, -2, "png ", SolidPng(5, 7, 200, 10, 10)), new SbixEntry(g, 0, 0, "dupe", Dupe(a))]),
            new SbixStrike(32, 144, [new SbixEntry(o, 1, -1, "png ", SolidPng(9, 9, 10, 200, 10, 128)), new SbixEntry(one, 0, 0, "jpg ", [0xFF, 0xD8, 0xFF])]));
        return MixedRoboto(("sbix", sbix));
    }
}
