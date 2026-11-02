using System.Globalization;
using System.Text;
using KernSmith;
using KernSmith.HintParity;
using KernSmith.HintParity.Legibility;
using KernSmith.Rasterizers.Native.Internal.Hinting;

// HintParity: measures how close the Native backend's output is to the FreeType backend's default
// output (Phase 188), through the public BmFont.Generate path, over a font x size x anti-alias
// matrix. Prints a summary table, writes parity.json (+ summary.txt) and optional x4 PNG strips,
// and exits 1 when a cell misses the parity bar (unless --no-bar). With --legibility it also
// scores both sides on the FreeType-independent legibility metric; --sweep tunes the Normal
// hinter's constants against that metric instead of running the parity matrix.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var fonts = new List<FontSpec>();
var sizes = new List<int>();
var antiAlias = new List<AntiAliasMode>();
var nativeConfigs = new List<NativeConfig>();
CharacterSet characters = CharacterSet.Ascii;
string charsetName = "ascii";
string? outDir = null;
bool enforceBar = true;
bool pngAll = false;
var pngFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
bool legibility = false;
bool textPng = false;
HintTuning? tuning = null;
bool sweep = false;
int sweepRounds = 2;
List<string>? sweepParameters = null;
var holdout = new List<FontSpec>();
bool holdoutGiven = false;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
        switch (args[i])
        {
            case "-h" or "--help":
                PrintHelp();
                return 0;
            case "--fonts":
                foreach (var f in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    fonts.Add(ParityMatrix.ResolveFont(f));
                break;
            case "--sizes":
                foreach (var s in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    sizes.Add(int.Parse(s, CultureInfo.InvariantCulture));
                break;
            case "--aa":
                foreach (var a in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    antiAlias.Add(ParityMatrix.ParseAntiAlias(a));
                break;
            case "--charset":
                charsetName = Next();
                characters = ParityMatrix.ParseCharset(charsetName);
                break;
            case "--native":
                nativeConfigs.Add(ParityMatrix.ParseNativeConfig(Next()));
                break;
            case "--out":
                outDir = Path.GetFullPath(Next());
                break;
            case "--bar":
                enforceBar = true;
                break;
            case "--no-bar":
                enforceBar = false;
                break;
            case "--png":
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    foreach (var f in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        pngFilters.Add(f);
                }
                else
                {
                    pngAll = true;
                }
                break;
            case "--text-png":
                textPng = true;
                break;
            case "--legibility":
                legibility = true;
                break;
            case "--tuning":
                tuning = Sweep.ParseTuning(Next(), tuning ?? HintTuning.Default);
                break;
            case "--sweep":
                sweep = true;
                break;
            case "--sweep-rounds":
                sweepRounds = int.Parse(Next(), CultureInfo.InvariantCulture);
                break;
            case "--sweep-params":
                sweepParameters = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                break;
            case "--holdout":
                holdoutGiven = true;
                foreach (var f in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    holdout.Add(ParityMatrix.ResolveFont(f));
                break;
            default:
                throw new ArgumentException($"Unknown option {args[i]}. Try --help.");
        }
    }
}
catch (Exception e) when (e is ArgumentException or FormatException or FileNotFoundException or NotSupportedException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

if (tuning is not null)
    HintTuning.Current = tuning;

if (sweep)
{
    if (fonts.Count == 0)
        foreach (var name in ParityMatrix.SweepTuningFonts)
            fonts.Add(ParityMatrix.ResolveFont(name));
    if (!holdoutGiven)
        foreach (var name in ParityMatrix.SweepHoldoutFonts)
            holdout.Add(ParityMatrix.ResolveFont(name));
    if (sizes.Count == 0) sizes.AddRange(ParityMatrix.SweepSizes);
    if (antiAlias.Count == 0) antiAlias.AddRange(ParityMatrix.DefaultAntiAlias);
    outDir ??= Path.Combine(ParityMatrix.ToolDirectory(), "output", "sweep");

    try
    {
        return Sweep.Run(new SweepOptions
        {
            TuningFonts = fonts,
            HoldoutFonts = holdout,
            Sizes = sizes,
            AntiAlias = antiAlias,
            Characters = characters,
            OutDir = outDir,
            Rounds = sweepRounds,
            Parameters = sweepParameters,
            Start = tuning ?? HintTuning.Default,
        });
    }
    catch (Exception e) when (e is not OutOfMemoryException)
    {
        Console.Error.WriteLine($"error: {e.GetType().Name}: {e.Message}");
        return 2;
    }
}

if (fonts.Count == 0) fonts.AddRange(ParityMatrix.DefaultFonts());
if (sizes.Count == 0) sizes.AddRange(ParityMatrix.DefaultSizes);
if (antiAlias.Count == 0) antiAlias.AddRange(ParityMatrix.DefaultAntiAlias);
if (nativeConfigs.Count == 0) nativeConfigs.AddRange(ParityMatrix.DefaultNativeConfigs);
outDir ??= Path.Combine(ParityMatrix.ToolDirectory(), "output");
Directory.CreateDirectory(outDir);

var results = new List<CellResult>();
var errors = new List<string>();
var totalWatch = System.Diagnostics.Stopwatch.StartNew();

foreach (var font in fonts)
{
    byte[] fontData;
    try
    {
        fontData = File.ReadAllBytes(font.Path);
    }
    catch (IOException e)
    {
        errors.Add($"{font.Name}: {e.Message}");
        continue;
    }

    var engine = FreeTypeEngine.Infer(fontData);
    Console.WriteLine($"{font.Name}: FreeType engine {engine}");

    foreach (int size in sizes)
    {
        IdealFont? ideal = null;
        foreach (var aa in antiAlias)
        {
            GeneratedFont? freeType = null;
            LegibilityTerms? freeTypeLegibility = null;
            foreach (var native in nativeConfigs)
            {
                if (!native.AppliesTo(aa))
                    continue;

                var cell = new Cell(font, size, aa, native);
                try
                {
                    freeType ??= GeneratedFont.Generate(fontData, ParityMatrix.FreeTypeOptions(cell, characters));
                    var nativeFont = GeneratedFont.Generate(fontData, ParityMatrix.NativeOptions(cell, characters));

                    var glyphs = freeType.Glyphs.Keys.Intersect(nativeFont.Glyphs.Keys).Order()
                        .Select(id => GlyphComparison.Compare(freeType.Glyphs[id], nativeFont.Glyphs[id], aa == AntiAliasMode.None))
                        .ToArray();

                    LegibilityTerms? nativeLegibility = null;
                    if (legibility)
                    {
                        ideal ??= IdealFont.Render(fontData, size, characters);
                        bool mono = aa == AntiAliasMode.None;
                        freeTypeLegibility ??= LegibilityScore.Measure(freeType.Glyphs, ideal, mono);
                        nativeLegibility = LegibilityScore.Measure(nativeFont.Glyphs, ideal, mono);
                    }

                    var result = new CellResult
                    {
                        Cell = cell,
                        Engine = engine,
                        FreeType = freeType,
                        Native = nativeFont,
                        Glyphs = glyphs,
                        FreeTypeQuality = QualityStats.Measure(freeType.Glyphs),
                        NativeQuality = QualityStats.Measure(nativeFont.Glyphs),
                        FreeTypeLegibility = freeTypeLegibility,
                        NativeLegibility = nativeLegibility,
                    };
                    results.Add(result);

                    bool wantPng = pngAll || pngFilters.Contains($"{font.Name}:{size}") || pngFilters.Contains(font.Name);
                    if (wantPng)
                    {
                        string strip = Path.Combine(outDir, $"strip-{cell.Key}.png");
                        StripRenderer.Write(strip, result);
                    }

                    if (wantPng && textPng)
                    {
                        string strip = Path.Combine(outDir, $"text-{cell.Key}.png");
                        TextStrip.Write(strip, [(freeType.Glyphs, freeType.LineHeight), (nativeFont.Glyphs, nativeFont.LineHeight)]);
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    errors.Add($"{cell.Key}: {e.GetType().Name}: {e.Message}");
                }
            }
        }
    }
}

totalWatch.Stop();

var table = BuildTable(results);
Console.WriteLine();
Console.Write(table);

if (errors.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Errors:");
    foreach (var e in errors)
        Console.WriteLine("  " + e);
}

var runOptions = new Dictionary<string, string>
{
    ["fonts"] = string.Join(",", fonts.Select(f => f.Name)),
    ["sizes"] = string.Join(",", sizes),
    ["antiAlias"] = string.Join(",", antiAlias.Select(Cell.AaName)),
    ["native"] = string.Join(";", nativeConfigs.Select(n => $"{n.Label}:aa={(n.AntiAlias is null ? "cell" : Cell.AaName(n.AntiAlias.Value))},hinting={(n.EnableHinting ? "on" : "off")}")),
    ["charset"] = charsetName,
    ["enforceBar"] = enforceBar.ToString(),
    ["legibility"] = legibility.ToString(),
    ["tuning"] = Sweep.Describe(HintTuning.Current),
    ["elapsedSeconds"] = totalWatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),
};

string jsonPath = Path.Combine(outDir, "parity.json");
JsonReport.Write(jsonPath, results, runOptions);
File.WriteAllText(Path.Combine(outDir, "summary.txt"), table + (errors.Count == 0 ? "" : "\nErrors:\n  " + string.Join("\n  ", errors) + "\n"));

int passed = results.Count(r => r.BarPassed);
Console.WriteLine();
Console.WriteLine($"{results.Count} cells, {passed} pass the parity bar, {results.Count - passed} fail; {errors.Count} errors; {totalWatch.Elapsed.TotalSeconds:0.0} s.");
Console.WriteLine($"JSON: {jsonPath}");

if (errors.Count > 0)
    return 2;
return enforceBar && passed < results.Count ? 1 : 0;

static string BuildTable(IReadOnlyList<CellResult> results)
{
    bool legibility = results.Any(r => r.NativeLegibility is not null);
    string[] headers =
    [
        "Font", "Size", "AA", "Native", "FT engine", "n", "line=", "adv%", "box=%", "box+-1%",
        "SSIM", "worst", "maxD", "meanD", "mono=%", "sharp FT/Nat", "solid FT/Nat", "stemVar FT/Nat",
        .. legibility ? new[] { "legib FT/Nat", "topo", "fidel", "conc", "separ", "align", "stems", "space" } : [],
        "bar",
    ];
    var rows = new List<string[]>();
    foreach (var r in results)
    {
        string[] legibilityColumns = [];
        if (legibility && r.FreeTypeLegibility is { } ft && r.NativeLegibility is { } nat)
        {
            legibilityColumns =
            [
                $"{ft.Score:0.000}/{nat.Score:0.000}",
                $"{ft.Topology:0.00}/{nat.Topology:0.00}",
                $"{ft.Fidelity:0.00}/{nat.Fidelity:0.00}",
                $"{ft.Concentration:0.00}/{nat.Concentration:0.00}",
                $"{ft.Separability:0.00}/{nat.Separability:0.00}",
                $"{ft.Alignment:0.00}/{nat.Alignment:0.00}",
                $"{ft.Stems:0.00}/{nat.Stems:0.00}",
                $"{ft.Spacing:0.00}/{nat.Spacing:0.00}",
            ];
        }
        else if (legibility)
        {
            legibilityColumns = ["-", "-", "-", "-", "-", "-", "-", "-"];
        }

        rows.Add(
        [
            r.Cell.Font.Name,
            r.Cell.Size.ToString(),
            Cell.AaName(r.Cell.AntiAlias),
            r.Cell.Native.Label,
            r.Engine.Name,
            r.GlyphCount.ToString(),
            r.LineMetricsEqual ? "yes" : $"no ({r.FreeType.LineHeight}/{r.FreeType.Base} vs {r.Native.LineHeight}/{r.Native.Base})",
            Pct(r.AdvanceMatchRatio),
            Pct(r.BoxIdenticalRatio),
            Pct(r.BoxWithin1Ratio),
            r.Mono ? "-" : r.MeanSsim.ToString("0.000"),
            r.Mono ? "-" : r.WorstSsim.ToString("0.000"),
            r.MaxAbsDiff.ToString(),
            r.MeanAbsDiff.ToString("0.0"),
            r.Mono ? Pct(r.MonoIdenticalRatio) : "-",
            $"{r.FreeTypeQuality.EdgeSharpness:0.0}/{r.NativeQuality.EdgeSharpness:0.0}",
            $"{r.FreeTypeQuality.SolidFraction:0.00}/{r.NativeQuality.SolidFraction:0.00}",
            $"{r.FreeTypeQuality.StemVarianceCoverage:0.000}/{r.NativeQuality.StemVarianceCoverage:0.000}",
            .. legibilityColumns,
            r.BarPassed ? "PASS" : "FAIL",
        ]);
    }

    var widths = headers.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(row => row[i].Length))).ToArray();
    var sb = new StringBuilder();
    sb.AppendLine(string.Join(" | ", headers.Select((h, i) => h.PadRight(widths[i]))));
    sb.AppendLine(string.Join("-+-", widths.Select(w => new string('-', w))));
    foreach (var row in rows)
        sb.AppendLine(string.Join(" | ", row.Select((c, i) => c.PadRight(widths[i]))));
    return sb.ToString();

    static string Pct(double v) => (v * 100).ToString("0.0");
}

static void PrintHelp()
{
    Console.WriteLine("""
        HintParity - Native vs FreeType output parity (Phase 188)

        Usage: dotnet run --project tests/hint-parity/HintParity -- [options]

        Options:
          --fonts <a,b,...>     Fixture names/stems, system font names, or paths.
                                Default: Roboto-Regular, NotoSans-Regular-Latin, SourceSerif4-Regular,
                                DancingScript-Variable, RobotoFlex-Latin, plus arial and georgia when present.
          --sizes <8,10,...>    Default: 8,10,12,14,16,24
          --aa <grayscale,none> Default: both
          --charset ascii|latin Default: ascii (latin = ASCII + Latin-1)
          --native <spec>       A Native configuration, repeatable; replaces the defaults
                                (default:aa=cell,hinting=on and light:aa=light,hinting=on).
                                spec = label:aa=cell|grayscale|light|none,hinting=on|off
                                (hintlevel=<level> is reserved for the HintLevel API and rejected for now)
          --out <dir>           Default: tests/hint-parity/output
          --png [Font:Size,...] Write x4 strips (FreeType / Native / diff) for every cell, or only the listed
                                font:size pairs (a bare font name selects all its sizes).
          --text-png            With --png: also write text-<cell>.png, sample lines set with each side's
                                advances (row 1 FreeType, row 2 Native), x4.
          --bar | --no-bar      Enforce the parity bar in the exit code (default: enforce).
          --legibility          Score both sides on the FreeType-independent legibility metric (adds the
                                legib FT/Nat column and its seven terms, and the detail to parity.json).
          --tuning <k=v,...>    Override the Native Normal hinter's constants for the run
                                (MinEdgeLength, MinLoneEdgeLength, MergeDistance, StandardStemTolerance,
                                PhaseShiftPenalty, MaxShift, MinCounter, XHeightRoundUp, OriginNudge,
                                SlopeToleranceX, SlopeToleranceY, LeftBearingSlack, TaperStartPpem, TaperEndPpem).

        Sweep mode (tunes the constants against the legibility score instead of running the matrix):
          --sweep               Coordinate descent over the constants, maximising the mean Native Normal
                                legibility score over --fonts x --sizes x --aa. Defaults: fonts Roboto-Regular,
                                NotoSans-Regular-Latin, SourceSerif4-Regular, DancingScript-Variable; sizes
                                8,10,12,14,16; both aa modes; output tests/hint-parity/output/sweep.
          --holdout <a,b>       Fonts scored before and after but not tuned on
                                (default SourceCodeVF-Upright, RobotoFlex-Latin).
          --sweep-rounds <n>    Passes over the parameter list (default 2).
          --sweep-params <a,b>  Only these constants (default: all but the taper window, which has
                                no effect at or under 16 px).
          --tuning <k=v,...>    The starting point.

        Exit codes: 0 all cells pass (or --no-bar), 1 a cell fails the bar, 2 an error.
        """);
}
