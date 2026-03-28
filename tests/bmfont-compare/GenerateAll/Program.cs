using System.Diagnostics;
using System.Runtime.CompilerServices;
using KernSmith;
using KernSmith.Rasterizer;

// Force assembly load so module initializers run
RuntimeHelpers.RunClassConstructor(typeof(KernSmith.Rasterizers.Gdi.GdiRasterizer).TypeHandle);
RuntimeHelpers.RunClassConstructor(typeof(KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer).TypeHandle);

// Usage: GenerateAll <bmfc-dir> <output-dir>
//   bmfc-dir:   directory containing .bmfc files (default: gum-bmfont/)
//   output-dir: directory for generated output (default: current directory)
var bmfcDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "gum-bmfont"));

var outDir = args.Length > 1
    ? args[1]
    : ".";

if (!Directory.Exists(bmfcDir))
{
    Console.Error.WriteLine($"ERROR: bmfc directory not found: {bmfcDir}");
    return 1;
}

Directory.CreateDirectory(outDir);

var bmfcFiles = Directory.GetFiles(bmfcDir, "*.bmfc").OrderBy(f => f).ToArray();
Console.WriteLine($"Found {bmfcFiles.Length} .bmfc files in {bmfcDir}");
Console.WriteLine($"Output directory: {outDir}");

// Find bmfont64.exe for reference output
var bmfont64Path = FindBmFont64();
if (bmfont64Path != null)
    Console.WriteLine($"BMFont64: {bmfont64Path}");
else
    Console.WriteLine("BMFont64: not found (skipping bmfont backend)");

var backends = new (string Name, Func<IRasterizer> Factory)[]
{
    ("freetype", () => RasterizerFactory.Create(RasterizerBackend.FreeType)),
    ("gdi", () => new KernSmith.Rasterizers.Gdi.GdiRasterizer()),
    ("directwrite", () => new KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer()),
};

int totalSucceeded = 0;
int totalFailed = 0;

foreach (var bmfcPath in bmfcFiles)
{
    var configName = Path.GetFileNameWithoutExtension(bmfcPath);
    Console.WriteLine($"\n--- {configName} ---");

    // KernSmith backends
    foreach (var (backendName, factory) in backends)
    {
        Console.Write($"  {backendName} ... ");

        try
        {
            var result = BmFont.Builder()
                .FromConfig(bmfcPath)
                .WithRasterizer(factory())
                .Build();

            var baseName = $"{configName}-{backendName}";
            var fntPath = Path.Combine(outDir, $"{baseName}.fnt");
            File.WriteAllText(fntPath, result.FntText);

            var pngs = result.GetPngData();
            for (int i = 0; i < pngs.Length; i++)
            {
                var pngPath = Path.Combine(outDir, $"{baseName}_{i}.png");
                File.WriteAllBytes(pngPath, pngs[i]);
            }

            Console.WriteLine("OK");
            totalSucceeded++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
            totalFailed++;
        }
    }

    // BMFont64.exe reference output
    if (bmfont64Path != null)
    {
        Console.Write("  bmfont ... ");

        try
        {
            var bmfontOutPath = Path.Combine(outDir, $"{configName}-bmfont.fnt");
            var psi = new ProcessStartInfo
            {
                FileName = bmfont64Path,
                Arguments = $"-c \"{bmfcPath}\" -o \"{bmfontOutPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)!;
            proc.WaitForExit(30_000);

            if (proc.ExitCode == 0 && File.Exists(bmfontOutPath))
            {
                Console.WriteLine("OK");
                totalSucceeded++;
            }
            else
            {
                var stderr = proc.StandardError.ReadToEnd().Trim();
                Console.WriteLine($"FAILED: exit={proc.ExitCode} {stderr}");
                totalFailed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
            totalFailed++;
        }
    }
}

Console.WriteLine($"\nDone. {totalSucceeded} succeeded, {totalFailed} failed.");
return totalFailed > 0 ? 1 : 0;

static string? FindBmFont64()
{
    // Check known locations
    var candidates = new[]
    {
        @"c:\tools\bmfont64.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "bmfont64.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "bmfont64_1.14b_beta", "bmfont64.exe"),
    };

    foreach (var path in candidates)
    {
        if (File.Exists(path))
            return path;
    }

    // Check PATH
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "where",
            Arguments = "bmfont64.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit(5_000);
        if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
            return output.Split('\n')[0].Trim();
    }
    catch { }

    return null;
}
