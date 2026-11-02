using System.Globalization;
using System.Text;
using System.Text.Json;
using KernSmith;
using KernSmith.HintParity.Legibility;
using KernSmith.Rasterizers.Native.Internal.Hinting;

namespace KernSmith.HintParity;

/// <summary>One of the <c>Normal</c> hinter's constants as the sweep sees it.</summary>
internal sealed record SweepParameter(string Name, float[] Grid, Func<HintTuning, float> Get, Func<HintTuning, float, HintTuning> Set);

/// <summary>What one sweep run optimises over.</summary>
internal sealed class SweepOptions
{
    public required IReadOnlyList<FontSpec> TuningFonts { get; init; }
    public required IReadOnlyList<FontSpec> HoldoutFonts { get; init; }
    public required IReadOnlyList<int> Sizes { get; init; }
    public required IReadOnlyList<AntiAliasMode> AntiAlias { get; init; }
    public required CharacterSet Characters { get; init; }
    public required string OutDir { get; init; }
    public int Rounds { get; init; } = 2;
    public IReadOnlyList<string>? Parameters { get; init; }
    public HintTuning Start { get; init; } = HintTuning.Default;
    /// <summary>Cells (Font:Size) to write before/after text strips for.</summary>
    public IReadOnlyList<string> StripCells { get; init; } = ["Roboto-Regular:10", "Roboto-Regular:12"];
}

/// <summary>
/// Coordinate descent over the <c>Normal</c> hinter's constants (<see cref="HintTuning"/>),
/// maximising the mean <see cref="LegibilityScore"/> of Native's output over the tuning fonts ×
/// sizes × anti-alias modes, with the hold-out fonts scored before and after to show whether
/// the gain generalises. Each parameter is tried on a fixed grid of values (deterministic and
/// reproducible); a change is kept only when the objective improves. The tuning in force is
/// <see cref="HintTuning.Current"/>, restored to its starting value when the sweep ends.
/// </summary>
internal static class Sweep
{
    /// <summary>The tunable constants and the values each is tried at. The taper window is
    /// listed for <c>--tuning</c> but has no effect at or under 16 px, so it is not swept by default.</summary>
    public static readonly SweepParameter[] Parameters =
    [
        new("MinEdgeLength", [0.02f, 0.05f, 0.1f, 0.2f], t => t.MinEdgeLength, (t, v) => t with { MinEdgeLength = v }),
        new("MinLoneEdgeLength", [0.4f, 0.55f, 0.7f, 0.85f, 1.0f, 1.3f], t => t.MinLoneEdgeLength, (t, v) => t with { MinLoneEdgeLength = v }),
        new("MergeDistance", [0.15f, 0.2f, 0.3f, 0.4f, 0.5f], t => t.MergeDistance, (t, v) => t with { MergeDistance = v }),
        new("StandardStemTolerance", [0.05f, 0.1f, 0.15f, 0.2f, 0.3f], t => t.StandardStemTolerance, (t, v) => t with { StandardStemTolerance = v }),
        new("PhaseShiftPenalty", [0f, 0.1f, 0.25f, 0.5f, 1.0f], t => t.PhaseShiftPenalty, (t, v) => t with { PhaseShiftPenalty = v }),
        new("MaxShift", [0.5f, 0.75f, 1.0f, 1.25f, 1.5f], t => t.MaxShift, (t, v) => t with { MaxShift = v }),
        new("MinCounter", [0.3f, 0.4f, 0.5f, 0.65f, 0.8f], t => t.MinCounter, (t, v) => t with { MinCounter = v }),
        new("XHeightRoundUp", [0.5f, 0.6f, 2f / 3f, 0.75f, 0.85f], t => t.XHeightRoundUp, (t, v) => t with { XHeightRoundUp = v }),
        new("OriginNudge", [0.1f, 0.25f, 0.4f], t => t.OriginNudge, (t, v) => t with { OriginNudge = v }),
        new("SlopeToleranceX", [0.04f, 0.07f, 0.1f, 0.12f], t => t.SlopeToleranceX, (t, v) => t with { SlopeToleranceX = v }),
        new("SlopeToleranceY", [0.07f, 0.1f, 0.12f, 0.15f, 0.2f], t => t.SlopeToleranceY, (t, v) => t with { SlopeToleranceY = v }),
        new("LeftBearingSlack", [0.1f, 0.25f, 0.4f, 0.5f], t => t.LeftBearingSlack, (t, v) => t with { LeftBearingSlack = v }),
        new("TaperStartPpem", [20f, 24f, 28f], t => t.TaperStartPpem, (t, v) => t with { TaperStartPpem = v }),
        new("TaperEndPpem", [32f, 40f, 48f], t => t.TaperEndPpem, (t, v) => t with { TaperEndPpem = v }),
    ];

    private static readonly string[] NotSweptByDefault = ["TaperStartPpem", "TaperEndPpem"];

    /// <summary>Parses <c>Name=value,Name=value</c> over <paramref name="start"/>.</summary>
    public static HintTuning ParseTuning(string text, HintTuning start)
    {
        var tuning = start;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0)
                throw new ArgumentException($"Tuning '{part}' is not Name=value.");
            string name = part[..eq];
            var parameter = Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown tuning constant '{name}' ({string.Join(", ", Parameters.Select(p => p.Name))}).");
            tuning = parameter.Set(tuning, float.Parse(part[(eq + 1)..], CultureInfo.InvariantCulture));
        }

        return tuning;
    }

    public static string Describe(HintTuning t) =>
        string.Join(",", Parameters.Select(p => $"{p.Name}={p.Get(t).ToString("0.###", CultureInfo.InvariantCulture)}"));

    public static int Run(SweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(options.OutDir);

        var parameters = Parameters.Where(p => options.Parameters is { } names
            ? names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
            : !NotSweptByDefault.Contains(p.Name)).ToArray();

        var evaluator = new Evaluator(options);
        var log = new List<Evaluation>();
        var original = HintTuning.Current;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var current = options.Start;
            var best = evaluator.Evaluate(current, options.TuningFonts, "start");
            log.Add(best);
            Console.WriteLine($"start   {best.Score:0.0000}  {Terms(best)}");

            for (int round = 1; round <= options.Rounds; round++)
            {
                bool improved = false;
                foreach (var parameter in parameters)
                {
                    float currentValue = parameter.Get(current);
                    foreach (float candidate in parameter.Grid)
                    {
                        if (candidate.Equals(currentValue))
                            continue;

                        var trial = parameter.Set(current, candidate);
                        var result = evaluator.Evaluate(trial, options.TuningFonts, $"r{round} {parameter.Name}={candidate.ToString("0.###", CultureInfo.InvariantCulture)}");
                        log.Add(result);
                        bool keep = result.Score > best.Score + 1e-4;
                        Console.WriteLine($"{result.Label,-34} {result.Score:0.0000} {(keep ? "  <- keep" : "")}");
                        if (keep)
                        {
                            best = result;
                            current = trial;
                            currentValue = candidate;
                            improved = true;
                        }
                    }
                }

                if (!improved)
                    break;
            }

            // Hold-out: did the gain generalise?
            Evaluation? holdoutBefore = null, holdoutAfter = null;
            if (options.HoldoutFonts.Count > 0)
            {
                holdoutBefore = evaluator.Evaluate(options.Start, options.HoldoutFonts, "holdout start");
                holdoutAfter = evaluator.Evaluate(current, options.HoldoutFonts, "holdout best");
            }

            // Before/after text strips for the requested cells.
            var strips = new List<string>();
            foreach (var cellName in options.StripCells)
            {
                int colon = cellName.LastIndexOf(':');
                if (colon < 0)
                    continue;
                string fontName = cellName[..colon];
                int size = int.Parse(cellName[(colon + 1)..], CultureInfo.InvariantCulture);
                var font = options.TuningFonts.Concat(options.HoldoutFonts).FirstOrDefault(f => string.Equals(f.Name, fontName, StringComparison.OrdinalIgnoreCase));
                if (font is null)
                    continue;
                foreach (var aa in options.AntiAlias)
                {
                    string path = Path.Combine(options.OutDir, $"text-{font.Name}-{size}-{Cell.AaName(aa)}-before-after.png");
                    evaluator.WriteBeforeAfterStrip(path, font, size, aa, options.Start, current);
                    strips.Add(path);
                }
            }

            watch.Stop();
            WriteReport(options, parameters, log, log[0], best, holdoutBefore, holdoutAfter, strips, watch.Elapsed);
            Console.WriteLine();
            Console.WriteLine($"best    {best.Score:0.0000}  (start {log[0].Score:0.0000})  {Describe(current)}");
            if (holdoutBefore is not null && holdoutAfter is not null)
                Console.WriteLine($"holdout {holdoutAfter.Score:0.0000}  (start {holdoutBefore.Score:0.0000})");
            Console.WriteLine($"{log.Count} evaluations in {watch.Elapsed.TotalSeconds:0} s; report: {Path.Combine(options.OutDir, "sweep.json")}");
            return 0;
        }
        finally
        {
            HintTuning.Current = original;
        }
    }

    private static string Terms(Evaluation e) =>
        string.Join(" ", e.MeanTerms.Select(kv => $"{kv.Key[..Math.Min(4, kv.Key.Length)]}={kv.Value:0.000}"));

    private static void WriteReport(
        SweepOptions options, SweepParameter[] parameters, List<Evaluation> log, Evaluation start, Evaluation best,
        Evaluation? holdoutBefore, Evaluation? holdoutAfter, List<string> strips, TimeSpan elapsed)
    {
        string jsonPath = Path.Combine(options.OutDir, "sweep.json");
        using (var stream = File.Create(jsonPath))
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("tool", "HintParity sweep");
            w.WriteString("generated", DateTimeOffset.Now.ToString("O"));
            w.WriteString("tuningFonts", string.Join(",", options.TuningFonts.Select(f => f.Name)));
            w.WriteString("holdoutFonts", string.Join(",", options.HoldoutFonts.Select(f => f.Name)));
            w.WriteString("sizes", string.Join(",", options.Sizes));
            w.WriteString("antiAlias", string.Join(",", options.AntiAlias.Select(Cell.AaName)));
            w.WriteString("parameters", string.Join(",", parameters.Select(p => p.Name)));
            w.WriteNumber("rounds", options.Rounds);
            w.WriteNumber("elapsedSeconds", elapsed.TotalSeconds);
            w.WriteStartObject("weights");
            foreach (var (name, weight) in LegibilityScore.Weights)
                w.WriteNumber(name, weight);
            w.WriteEndObject();

            WriteEvaluation(w, "start", start);
            WriteEvaluation(w, "best", best);
            if (holdoutBefore is not null) WriteEvaluation(w, "holdoutStart", holdoutBefore);
            if (holdoutAfter is not null) WriteEvaluation(w, "holdoutBest", holdoutAfter);

            w.WriteStartArray("strips");
            foreach (var s in strips)
                w.WriteStringValue(s);
            w.WriteEndArray();

            w.WriteStartArray("evaluations");
            foreach (var e in log)
                WriteEvaluation(w, null, e);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"start  {start.Score:0.0000}  {Describe(start.Tuning)}");
        sb.AppendLine($"best   {best.Score:0.0000}  {Describe(best.Tuning)}");
        sb.AppendLine("term deltas (best - start), tuning set:");
        foreach (var (name, value) in best.MeanTerms)
            sb.AppendLine($"  {name,-14} {start.MeanTerms[name]:0.0000} -> {value:0.0000}  ({value - start.MeanTerms[name]:+0.0000;-0.0000})");
        if (holdoutBefore is not null && holdoutAfter is not null)
        {
            sb.AppendLine($"holdout start {holdoutBefore.Score:0.0000} -> best {holdoutAfter.Score:0.0000} ({holdoutAfter.Score - holdoutBefore.Score:+0.0000;-0.0000})");
            foreach (var (name, value) in holdoutAfter.MeanTerms)
                sb.AppendLine($"  {name,-14} {holdoutBefore.MeanTerms[name]:0.0000} -> {value:0.0000}  ({value - holdoutBefore.MeanTerms[name]:+0.0000;-0.0000})");
        }
        sb.AppendLine("per cell (best):");
        foreach (var (key, terms) in best.Cells)
            sb.AppendLine($"  {key,-40} {terms.Score:0.0000}  (start {start.Cells[key].Score:0.0000})");
        File.WriteAllText(Path.Combine(options.OutDir, "sweep.txt"), sb.ToString());
    }

    private static void WriteEvaluation(Utf8JsonWriter w, string? name, Evaluation e)
    {
        if (name is null) w.WriteStartObject(); else w.WriteStartObject(name);
        w.WriteString("label", e.Label);
        w.WriteNumber("score", e.Score);
        w.WriteStartObject("tuning");
        foreach (var p in Parameters)
            w.WriteNumber(p.Name, p.Get(e.Tuning));
        w.WriteEndObject();
        w.WriteStartObject("meanTerms");
        foreach (var (term, value) in e.MeanTerms)
            w.WriteNumber(term, value);
        w.WriteEndObject();
        w.WriteStartObject("cells");
        foreach (var (key, terms) in e.Cells)
            JsonReport.WriteLegibility(w, key, terms);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    internal sealed record Evaluation(string Label, HintTuning Tuning, double Score, IReadOnlyDictionary<string, double> MeanTerms, IReadOnlyDictionary<string, LegibilityTerms> Cells);

    /// <summary>Generates Native <c>Normal</c> output under a tuning and scores it; the ideals are rendered once per (font, size).</summary>
    private sealed class Evaluator(SweepOptions options)
    {
        private readonly Dictionary<string, byte[]> _fontData = [];
        private readonly Dictionary<(string, int), IdealFont> _ideals = [];
        private readonly Dictionary<(string, int, AntiAliasMode), GeneratedFont> _freeType = [];

        public Evaluation Evaluate(HintTuning tuning, IReadOnlyList<FontSpec> fonts, string label)
        {
            HintTuning.Current = tuning;
            var cells = new Dictionary<string, LegibilityTerms>();
            foreach (var font in fonts)
            {
                foreach (int size in options.Sizes)
                {
                    var ideal = Ideal(font, size);
                    foreach (var aa in options.AntiAlias)
                    {
                        var generated = GeneratedFont.Generate(Data(font), NativeOptions(font, size, aa));
                        cells[$"{font.Name}-{size}-{Cell.AaName(aa)}"] = LegibilityScore.Measure(generated.Glyphs, ideal, aa == AntiAliasMode.None);
                    }
                }
            }

            var means = new Dictionary<string, double>();
            foreach (var name in LegibilityScore.Weights.Keys)
                means[name] = cells.Count == 0 ? 0 : cells.Values.Average(c => c.Named().First(t => t.Name == name).Value);
            double score = cells.Count == 0 ? 0 : cells.Values.Average(c => c.Score);
            return new Evaluation(label, tuning, score, means, cells);
        }

        /// <summary>Rows: FreeType (reference), Native Normal before, Native Normal after.</summary>
        public void WriteBeforeAfterStrip(string path, FontSpec font, int size, AntiAliasMode aa, HintTuning before, HintTuning after)
        {
            HintTuning.Current = before;
            var first = GeneratedFont.Generate(Data(font), NativeOptions(font, size, aa));
            HintTuning.Current = after;
            var second = GeneratedFont.Generate(Data(font), NativeOptions(font, size, aa));
            var freeType = FreeType(font, size, aa);
            TextStrip.Write(path,
            [
                (freeType.Glyphs, freeType.LineHeight),
                (first.Glyphs, first.LineHeight),
                (second.Glyphs, second.LineHeight),
            ]);
        }

        private FontGeneratorOptions NativeOptions(FontSpec font, int size, AntiAliasMode aa) => new()
        {
            Size = size,
            Characters = options.Characters,
            Backend = RasterizerBackend.Native,
            AntiAlias = aa,
            EnableHinting = true,
            HintLevel = HintLevel.Normal,
            Kerning = false,
        };

        private GeneratedFont FreeType(FontSpec font, int size, AntiAliasMode aa)
        {
            if (!_freeType.TryGetValue((font.Name, size, aa), out var generated))
            {
                generated = GeneratedFont.Generate(Data(font), new FontGeneratorOptions
                {
                    Size = size,
                    Characters = options.Characters,
                    Backend = RasterizerBackend.FreeType,
                    AntiAlias = aa,
                    EnableHinting = true,
                    Kerning = false,
                });
                _freeType[(font.Name, size, aa)] = generated;
            }

            return generated;
        }

        private IdealFont Ideal(FontSpec font, int size)
        {
            if (!_ideals.TryGetValue((font.Name, size), out var ideal))
            {
                ideal = IdealFont.Render(Data(font), size, options.Characters);
                _ideals[(font.Name, size)] = ideal;
            }

            return ideal;
        }

        private byte[] Data(FontSpec font)
        {
            if (!_fontData.TryGetValue(font.Path, out var data))
            {
                data = File.ReadAllBytes(font.Path);
                _fontData[font.Path] = data;
            }

            return data;
        }
    }
}
