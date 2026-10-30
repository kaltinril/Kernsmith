using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Cff;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 166 Part 2 — the CFF decoder against real fonts: Source Serif 4 (CFF, subroutinized,
/// hint masks, flex) and Source Code Pro's variable font (CFF2, blend), plus seeded fuzzing of
/// both tables.
/// </summary>
public class CffFixtureTests
{
    private static readonly RasterOptions Options = new() { Size = 32 };

    private static CffOutlineDecoder Decoder(byte[] font) =>
        (CffOutlineDecoder)OutlineDecoders.ForFace(NativeFontFace.Load(font));

    /// <summary>
    /// Reference paths recorded with fontTools' RecordingPen (an independent CFF/CFF2
    /// interpreter), default instance. Exact control points, in font units.
    /// </summary>
    [Theory]
    [InlineData("serif", 'l',
        "M 25,0 L 273,0 L 273,36 L 193,50 C 192,103 191,157 191,210 L 191,589 L 195,732 L 181,740 L 25,695 L 25,659 L 107,649 L 107,210 C 107,157 106,104 105,50 L 25,36 Z")]
    [InlineData("serif", 'o',
        "M 274,-13 C 390,-13 504,70 504,235 C 504,401 389,488 274,488 C 158,488 45,401 45,235 C 45,70 157,-13 274,-13 Z M 274,32 C 187,32 140,101 140,235 C 140,370 187,442 274,442 C 360,442 409,370 409,235 C 409,101 360,32 274,32 Z")]
    [InlineData("code", 'l',
        "M 416,-12 C 462,-12 492,-2 532,18 L 520,42 C 484,24 450,16 418,16 C 346,16 310,54 310,132 L 310,722 L 94,722 L 94,694 L 280,694 L 280,138 C 280,31 330,-12 416,-12 Z")]
    [InlineData("code", 'o',
        "M 300,-12 C 425,-12 530,80 530,238 C 530,398 425,490 300,490 C 175,490 70,398 70,238 C 70,80 175,-12 300,-12 Z M 300,16 C 186,16 102,106 102,238 C 102,370 186,462 300,462 C 414,462 498,370 498,238 C 498,106 414,16 300,16 Z")]
    public void Outline_MatchesReferenceInterpreter(string font, char character, string expected)
    {
        var bytes = font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes();
        var face = NativeFontFace.Load(bytes);
        var decoder = OutlineDecoders.ForFace(face);

        var outline = decoder.Decode(face.GetGlyphIndex(character), Options).Outline;
        Type2CharstringTests.Describe(outline.Commands).ShouldBe(expected);
    }

    [Theory]
    [InlineData("serif")]
    [InlineData("code")]
    public void EveryGlyph_Decodes(string font)
    {
        var bytes = font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes();
        var face = NativeFontFace.Load(bytes);
        var decoder = Decoder(bytes);

        decoder.Font.CharStrings.Count.ShouldBe(face.Maxp.NumGlyphs);

        int inked = 0;
        for (int glyph = 0; glyph < face.Maxp.NumGlyphs; glyph++)
        {
            var decoded = decoder.Decode(glyph, Options);
            if (!decoded.Outline.IsEmpty)
                inked++;
        }

        // Nearly every glyph in a text font draws something; only spaces and the like are empty.
        inked.ShouldBeGreaterThan(face.Maxp.NumGlyphs * 9 / 10);
    }

    [Fact]
    public void CharstringWidths_MatchHmtx_ForEveryGlyph()
    {
        // The width operand (nominalWidthX + operand, or defaultWidthX) is exactly what the font
        // compiler wrote to hmtx, so agreement for every glyph proves width parsing — and with
        // it, which operand is the width and which are hints or coordinates.
        var bytes = TestFonts.SourceSerifOtfBytes();
        var face = NativeFontFace.Load(bytes);
        var decoder = Decoder(bytes);

        int defaults = 0;
        for (int glyph = 0; glyph < face.Maxp.NumGlyphs; glyph++)
        {
            double width = decoder.CharstringWidth(glyph);
            width.ShouldBe(face.Hmtx.GetAdvanceWidth(glyph), $"glyph {glyph}");
            if (width.Equals(decoder.Font.PrivateFor(glyph).DefaultWidthX))
                defaults++;
        }

        // Both the encoded-width and the default-width paths are exercised.
        defaults.ShouldBeGreaterThan(0);
        defaults.ShouldBeLessThan(face.Maxp.NumGlyphs);
    }

    [Fact]
    public void Fixtures_ExerciseSubroutinesHintMasksAndBlend()
    {
        // Guards the tests above against a fixture swap that silently drops coverage.
        var serif = Decoder(TestFonts.SourceSerifOtfBytes()).Font;
        serif.GlobalSubrs.Count.ShouldBeGreaterThan(0);
        serif.PrivateFor(0).LocalSubrs.Count.ShouldBeGreaterThan(0);

        var code = Decoder(TestFonts.SourceCodeVariableOtfBytes()).Font;
        code.IsCff2.ShouldBeTrue();
        code.RegionCounts.Count.ShouldBeGreaterThan(0);

        var operators = new HashSet<int>();
        foreach (var font in new[] { serif, code })
        {
            for (int g = 0; g < font.CharStrings.Count; g++)
                CollectOperators(font.CharStrings[g].Span, operators);
            for (int s = 0; s < font.GlobalSubrs.Count; s++)
                CollectOperators(font.GlobalSubrs[s].Span, operators);
            for (int s = 0; s < font.PrivateFor(0).LocalSubrs.Count; s++)
                CollectOperators(font.PrivateFor(0).LocalSubrs[s].Span, operators);
        }

        operators.ShouldContain(19);   // hintmask
        operators.ShouldContain(10);   // callsubr
        operators.ShouldContain(29);   // callgsubr
        operators.ShouldContain(16);   // blend (CFF2)
        // Neither fixture uses flex; the flex operators are covered by Type2CharstringTests.
    }

    /// <summary>
    /// Collects operator codes from a charstring by scanning its byte encoding. Hint mask data is
    /// not skipped (that needs the stem count), so this over-collects a little — enough for a
    /// "these operators occur" guard.
    /// </summary>
    private static void CollectOperators(ReadOnlySpan<byte> code, HashSet<int> operators)
    {
        for (int i = 0; i < code.Length;)
        {
            byte b = code[i];
            if (b == 28) { i += 3; continue; }
            if (b == 255) { i += 5; continue; }
            if (b >= 247) { i += 2; continue; }
            if (b >= 32) { i++; continue; }
            if (b == 12 && i + 1 < code.Length) { operators.Add(1200 + code[i + 1]); i += 2; continue; }
            operators.Add(b);
            i++;
        }
    }

    // ---- Fuzzing ---------------------------------------------------------------------------

    public static TheoryData<string, string> Tables => new() { { "serif", "CFF " }, { "code", "CFF2" } };

    [Theory]
    [MemberData(nameof(Tables))]
    public void MutatedTable_FailsOnlyWithFontFormatException(string font, string tag)
    {
        var bytes = font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes();
        var record = TableProvider.Parse(bytes).TryGetRecord(tag)!.Value;
        var original = bytes.AsSpan((int)record.Offset, (int)record.Length).ToArray();

        var random = new Random(166);
        int loaded = 0;
        for (int iteration = 0; iteration < 150; iteration++)
        {
            var table = (byte[])original.Clone();
            int flips = random.Next(1, 8);
            for (int f = 0; f < flips; f++)
            {
                // One mutation in five hits the first 2 KB, where the header, INDEX headers and
                // DICTs live; the rest land anywhere, mostly in charstrings and subroutines, so
                // most tables still parse and the interpreter sees the damage.
                int at = random.Next(5) == 0 ? random.Next(Math.Min(2048, table.Length)) : random.Next(table.Length);
                table[at] = (byte)random.Next(256);
            }

            if (DecodeAllOrFormatError(table, tag == "CFF2", $"{font} iteration {iteration}"))
                loaded++;
        }

        // A fuzzer whose every input fails at the header would test nothing past it.
        loaded.ShouldBeGreaterThan(50);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void TruncatedTable_FailsOnlyWithFontFormatException(string font, string tag)
    {
        var bytes = font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes();
        var record = TableProvider.Parse(bytes).TryGetRecord(tag)!.Value;
        var original = bytes.AsSpan((int)record.Offset, (int)record.Length).ToArray();

        for (int length = 0; length < original.Length; length += 1 + (length / 8))
            DecodeAllOrFormatError(original[..length], tag == "CFF2", $"{font} truncated to {length}");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void RandomCharstrings_FailOnlyWithFontFormatException(string font, string tag)
    {
        // Byte flips rarely build long operand runs or odd operator sequences; random charstrings
        // do (seven bytes in eight are operands). They run against the real font's subroutines,
        // so calls land in genuine code with a hostile stack.
        var bytes = font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes();
        var cff = Decoder(bytes).Font;
        var privateData = cff.PrivateFor(0);
        var context = new CharstringContext
        {
            GlobalSubrs = cff.GlobalSubrs,
            LocalSubrs = privateData.LocalSubrs,
            NominalWidthX = privateData.NominalWidthX,
            DefaultWidthX = privateData.DefaultWidthX,
            IsCff2 = cff.IsCff2,
            VsIndex = privateData.VsIndex,
            RegionCounts = cff.RegionCounts,
        };

        var random = new Random(tag == "CFF2" ? 2 : 1);
        for (int iteration = 0; iteration < 3000; iteration++)
        {
            var charstring = new byte[random.Next(1, 700)];
            random.NextBytes(charstring);

            try
            {
                Type2Charstring.Interpret(charstring, context);
            }
            catch (FontFormatException)
            {
            }
            catch (Exception ex)
            {
                throw new ShouldAssertException(
                    $"{font} iteration {iteration}: threw {ex.GetType().Name}: {ex.Message} — charstring {Convert.ToHexString(charstring)}", ex);
            }
        }
    }

    [Fact]
    public void MutatedFont_ThroughTheRasterizer_FailsOnlyWithBmFontExceptions()
    {
        var bytes = TestFonts.SourceSerifOtfBytes();
        var record = TableProvider.Parse(bytes).TryGetRecord("CFF ")!.Value;
        var random = new Random(1660);
        var options = new RasterOptions { Size = 24 };

        for (int iteration = 0; iteration < 40; iteration++)
        {
            var font = (byte[])bytes.Clone();
            for (int f = 0; f < 4; f++)
                font[(int)record.Offset + random.Next((int)record.Length)] = (byte)random.Next(256);

            try
            {
                using var rasterizer = new NativeRasterizer();
                rasterizer.LoadFont(font);
                for (int c = 32; c < 127; c++)
                {
                    try
                    {
                        rasterizer.RasterizeGlyph(c, options);
                    }
                    catch (RasterizationException)
                    {
                    }
                }
            }
            catch (FontFormatException)
            {
            }
        }
    }

    /// <summary>
    /// Parses a (possibly broken) table and decodes every glyph, allowing only
    /// <see cref="FontFormatException"/>. Returns true when the table itself parsed.
    /// </summary>
    private static bool DecodeAllOrFormatError(byte[] table, bool cff2, string what)
    {
        CffFont font;
        try
        {
            font = cff2 ? CffFont.ParseCff2(table) : CffFont.ParseCff(table);
        }
        catch (FontFormatException)
        {
            return false;
        }
        catch (Exception ex)
        {
            throw new ShouldAssertException($"{what}: parsing threw {ex.GetType().Name}: {ex.Message}", ex);
        }

        for (int glyph = 0; glyph < font.CharStrings.Count; glyph++)
        {
            try
            {
                var privateData = font.PrivateFor(glyph);
                var context = new CharstringContext
                {
                    GlobalSubrs = font.GlobalSubrs,
                    LocalSubrs = privateData.LocalSubrs,
                    DefaultWidthX = privateData.DefaultWidthX,
                    NominalWidthX = privateData.NominalWidthX,
                    IsCff2 = font.IsCff2,
                    VsIndex = privateData.VsIndex,
                    RegionCounts = font.RegionCounts,
                    SeacComponent = font.IsCff2 || font.IsCidKeyed ? null : Component,
                };
                Type2Charstring.Interpret(font.CharStrings[glyph], context);
            }
            catch (FontFormatException)
            {
            }
            catch (Exception ex)
            {
                throw new ShouldAssertException($"{what}, glyph {glyph}: threw {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        return true;

        ReadOnlyMemory<byte>? Component(int code)
        {
            int g = font.GlyphForStandardCode(code);
            return g < 0 ? null : font.CharStrings[g];
        }
    }
}
