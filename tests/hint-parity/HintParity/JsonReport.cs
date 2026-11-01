using System.Text.Json;

namespace KernSmith.HintParity;

/// <summary>Writes the machine-readable report (one object per cell, per-glyph detail included).</summary>
internal static class JsonReport
{
    public static void Write(string path, IReadOnlyList<CellResult> results, IReadOnlyDictionary<string, string> runOptions)
    {
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        w.WriteStartObject();
        w.WriteString("tool", "HintParity");
        w.WriteString("generated", DateTimeOffset.Now.ToString("O"));
        w.WriteStartObject("options");
        foreach (var (key, value) in runOptions)
            w.WriteString(key, value);
        w.WriteEndObject();

        w.WriteStartObject("bar");
        w.WriteNumber("advanceMatchMin", 1.0);
        w.WriteNumber("boxWithin1Min", ParityBar.BoxWithin1Min);
        w.WriteNumber("meanSsimMin", ParityBar.MeanSsimMin);
        w.WriteNumber("worstSsimMin", ParityBar.WorstSsimMin);
        w.WriteNumber("monoIdenticalMin", ParityBar.MonoIdenticalMin);
        w.WriteEndObject();

        w.WriteStartArray("cells");
        foreach (var r in results)
            WriteCell(w, r);
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteCell(Utf8JsonWriter w, CellResult r)
    {
        w.WriteStartObject();
        w.WriteString("key", r.Cell.Key);
        w.WriteString("font", r.Cell.Font.Name);
        w.WriteString("fontPath", r.Cell.Font.Path);
        w.WriteNumber("size", r.Cell.Size);
        w.WriteString("antiAlias", Cell.AaName(r.Cell.AntiAlias));
        w.WriteString("native", r.Cell.Native.Label);
        w.WriteString("nativeAntiAlias", Cell.AaName(r.Cell.Native.Resolve(r.Cell.AntiAlias)));
        w.WriteBoolean("nativeEnableHinting", r.Cell.Native.EnableHinting);
        w.WriteString("freeTypeEngine", r.Engine.Name);
        w.WriteString("freeTypeEngineDetail", r.Engine.Detail);

        w.WriteStartObject("lineMetrics");
        w.WriteNumber("freeTypeLineHeight", r.FreeType.LineHeight);
        w.WriteNumber("nativeLineHeight", r.Native.LineHeight);
        w.WriteNumber("freeTypeBase", r.FreeType.Base);
        w.WriteNumber("nativeBase", r.Native.Base);
        w.WriteBoolean("equal", r.LineMetricsEqual);
        w.WriteEndObject();

        w.WriteNumber("glyphs", r.GlyphCount);
        w.WriteNumber("inkedGlyphs", r.Inked.Count);
        w.WriteNumber("advanceMatches", r.AdvanceMatches);
        w.WriteNumber("advanceMatchRatio", r.AdvanceMatchRatio);
        w.WriteNumber("advanceSignedSum", r.AdvanceSignedSum);
        w.WriteNumber("boxIdentical", r.BoxIdentical);
        w.WriteNumber("boxIdenticalRatio", r.BoxIdenticalRatio);
        w.WriteNumber("boxWithin1", r.BoxWithin1);
        w.WriteNumber("boxWithin1Ratio", r.BoxWithin1Ratio);
        w.WriteNumber("pixelIdentical", r.PixelIdentical);
        w.WriteNumber("pixelIdenticalRatio", r.PixelIdenticalRatio);
        w.WriteNumber("meanAbsDiff", r.MeanAbsDiff);
        w.WriteNumber("maxAbsDiff", r.MaxAbsDiff);
        w.WriteNumber("meanSsim", r.MeanSsim);
        w.WriteNumber("worstSsim", r.WorstSsim);
        w.WriteNumber("worstSsimGlyph", r.WorstSsimGlyph);
        w.WriteNumber("maxDiffLe16Ratio", r.MaxDiffLe16Ratio);
        w.WriteNumber("monoIdentical", r.MonoIdentical);
        w.WriteNumber("monoIdenticalRatio", r.MonoIdenticalRatio);
        w.WriteNumber("xorTotal", r.XorTotal);
        w.WriteNumber("worstXor", r.WorstXor);
        w.WriteNumber("freeTypeMs", r.FreeType.Elapsed.TotalMilliseconds);
        w.WriteNumber("nativeMs", r.Native.Elapsed.TotalMilliseconds);

        WriteQuality(w, "freeTypeQuality", r.FreeTypeQuality);
        WriteQuality(w, "nativeQuality", r.NativeQuality);

        w.WriteBoolean("barPassed", r.BarPassed);
        w.WriteStartArray("barFailures");
        foreach (var f in r.BarFailures)
            w.WriteStringValue(f);
        w.WriteEndArray();

        w.WriteStartArray("glyphDetail");
        foreach (var g in r.Glyphs)
        {
            w.WriteStartObject();
            w.WriteNumber("id", g.Id);
            w.WriteString("char", g.Id is >= 0x20 and < 0x7F or >= 0xA0 ? ((char)g.Id).ToString() : $"U+{g.Id:X4}");
            w.WriteNumber("advFt", g.AdvanceA);
            w.WriteNumber("advNat", g.AdvanceB);
            WriteBox(w, "boxFt", g.A);
            WriteBox(w, "boxNat", g.B);
            w.WriteBoolean("boxIdentical", g.BoxIdentical);
            w.WriteBoolean("boxWithin1", g.BoxWithin1);
            w.WriteBoolean("pixelIdentical", g.PixelIdentical);
            w.WriteNumber("meanAbsDiff", g.MeanAbsDiff);
            w.WriteNumber("maxAbsDiff", g.MaxAbsDiff);
            w.WriteNumber("ssim", g.Ssim);
            w.WriteNumber("xor", g.XorPixels);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
    }

    private static void WriteBox(Utf8JsonWriter w, string name, GlyphBitmap g)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(g.Width);
        w.WriteNumberValue(g.Height);
        w.WriteNumberValue(g.XOffset);
        w.WriteNumberValue(g.BaselineTop);
        w.WriteEndArray();
    }

    private static void WriteQuality(Utf8JsonWriter w, string name, QualityStats q)
    {
        w.WriteStartObject(name);
        w.WriteNumber("edgeSharpness", q.EdgeSharpness);
        w.WriteNumber("solidFraction", q.SolidFraction);
        w.WriteNumber("litPixels", q.LitPixels);
        w.WriteNumber("stemMeanCoverage", q.StemMeanCoverage);
        w.WriteNumber("stemVarianceCoverage", q.StemVarianceCoverage);
        w.WriteNumber("stemMeanThreshold", q.StemMeanThreshold);
        w.WriteNumber("stemVarianceThreshold", q.StemVarianceThreshold);
        w.WriteStartArray("stemGlyphs");
        foreach (var s in q.StemGlyphs)
            w.WriteStringValue(s);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
