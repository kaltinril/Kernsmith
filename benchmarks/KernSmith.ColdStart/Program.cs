using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using KernSmith;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native;
using KernSmith.Rasterizers.StbTrueType;
#if WINDOWS
using KernSmith.Rasterizers.DirectWrite.TerraFX;
using KernSmith.Rasterizers.Gdi;
#endif

// Times the 1st, 2nd ... Nth BmFont.Generate call in a fresh process, so JIT warm-up shows up
// in the first calls instead of being hidden by BenchmarkDotNet's warm-up iterations.
// Usage: KernSmith.ColdStart [--backend native] [--font <path>] [--size 32] [--charset ascii]
//        [--repeat 50] [--out <report.json>] [--mode <label>] [--registration auto|explicit]

long mainEntered = Stopwatch.GetTimestamp();
double? processStartToMainMs = ProcessStartToNowMs();
var jitAtMain = JitSnapshot.Take();

Dictionary<string, string> settings;
try
{
    settings = ParseArgs(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var report = new ColdStartReport
{
    Mode = settings.GetValueOrDefault("mode", "unlabelled"),
    Backend = settings.GetValueOrDefault("backend", "native"),
    Registration = settings.GetValueOrDefault("registration", RuntimeFeature.IsDynamicCodeSupported ? "auto" : "explicit"),
    Charset = settings.GetValueOrDefault("charset", "ascii"),
    Framework = RuntimeInformation.FrameworkDescription,
    RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
    DynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported,
    ProcessStartToMainMs = processStartToMainMs,
    JitAtMain = jitAtMain,
};

try
{
    report.Size = float.Parse(settings.GetValueOrDefault("size", "32"), System.Globalization.CultureInfo.InvariantCulture);
    report.Repeat = int.Parse(settings.GetValueOrDefault("repeat", "50"), System.Globalization.CultureInfo.InvariantCulture);
    report.Font = settings.TryGetValue("font", out var fontPath) ? Path.GetFullPath(fontPath) : FindDefaultFont();

    if (!Enum.TryParse<RasterizerBackend>(report.Backend, ignoreCase: true, out var backend))
        throw new ArgumentException($"Unknown backend '{report.Backend}'. Expected one of: {string.Join(", ", Enum.GetNames<RasterizerBackend>())}.");
    report.Backend = backend.ToString();

    if (report.Registration == "explicit")
        RegisterBackends();
    else if (report.Registration != "auto")
        throw new ArgumentException($"Unknown registration '{report.Registration}'. Expected auto or explicit.");

    var characters = ParseCharset(report.Charset);

    long readStart = Stopwatch.GetTimestamp();
    byte[] fontData = File.ReadAllBytes(report.Font);
    report.FontReadMs = Stopwatch.GetElapsedTime(readStart).TotalMilliseconds;

    for (int call = 0; call < report.Repeat; call++)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        long callStart = Stopwatch.GetTimestamp();

        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = report.Size,
            Characters = characters,
            Backend = backend,
        });
        string fntText = result.FntText;
        byte[][] pngPages = result.GetPngData();

        report.CallsMs.Add(Stopwatch.GetElapsedTime(callStart).TotalMilliseconds);

        if (call == 0)
        {
            report.MainToFirstResultMs = Stopwatch.GetElapsedTime(mainEntered).TotalMilliseconds;
            report.FirstCallAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
            report.JitAfterFirstCall = JitSnapshot.Take();
            report.Glyphs = result.Model.Characters.Count;
            report.Pages = pngPages.Length;
            report.OutputBytes = fntText.Length + pngPages.Sum(page => (long)page.Length);
        }
    }

    report.Ok = true;
}
catch (Exception ex)
{
    report.Ok = false;
    report.Error = $"{ex.GetType().FullName}: {ex.Message}";
}

report.JitAtEnd = JitSnapshot.Take();
report.MainToEndMs = Stopwatch.GetElapsedTime(mainEntered).TotalMilliseconds;

string json = JsonSerializer.Serialize(report, ColdStartJsonContext.Default.ColdStartReport);
if (settings.TryGetValue("out", out var outPath))
    File.WriteAllText(outPath, json);
else
    Console.WriteLine(json);

if (!report.Ok)
    Console.Error.WriteLine(report.Error);

return report.Ok ? 0 : 1;

static double? ProcessStartToNowMs()
{
    try
    {
        using var process = Process.GetCurrentProcess();
        return (DateTime.Now - process.StartTime).TotalMilliseconds;
    }
    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

// Auto-discovery finds backends by reflection, which trimming/NativeAOT removes; this is the
// documented explicit registration those builds need.
static void RegisterBackends()
{
    RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());
    RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
    RasterizerFactory.Register(RasterizerBackend.FreeType, () => new FreeTypeRasterizer());
#if WINDOWS
    RasterizerFactory.Register(RasterizerBackend.Gdi, () => new GdiRasterizer());
    RasterizerFactory.Register(RasterizerBackend.DirectWrite, () => new DirectWriteRasterizer());
#endif
}

static CharacterSet ParseCharset(string value) => value.ToLowerInvariant() switch
{
    "ascii" => CharacterSet.Ascii,
    "extended" => CharacterSet.ExtendedAscii,
    "latin" => CharacterSet.Latin,
    _ => CharacterSet.FromChars(value),
};

static string FindDefaultFont()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "KernSmith.Tests", "Fixtures", "Roboto-Regular.ttf");
            if (File.Exists(candidate))
                return candidate;
        }
    }

    throw new FileNotFoundException("Roboto-Regular.ttf fixture not found; pass --font <path>.");
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    string[] known = ["backend", "font", "size", "charset", "repeat", "out", "mode", "registration"];
    var values = new Dictionary<string, string>();
    for (int i = 0; i < args.Length; i++)
    {
        string name = args[i].StartsWith("--", StringComparison.Ordinal) ? args[i][2..] : "";
        if (!known.Contains(name) || i + 1 >= args.Length)
            throw new ArgumentException($"Bad argument '{args[i]}'. Options: {string.Join(" ", known.Select(k => $"--{k} <value>"))}");
        values[name] = args[++i];
    }

    return values;
}
