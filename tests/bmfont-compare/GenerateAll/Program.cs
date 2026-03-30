using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using KernSmith;
using KernSmith.Rasterizer;

// Force assembly load so module initializers run
RuntimeHelpers.RunClassConstructor(typeof(KernSmith.Rasterizers.Gdi.GdiRasterizer).TypeHandle);
RuntimeHelpers.RunClassConstructor(typeof(KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer).TypeHandle);

// Parse flags
bool compare = true;
string? configFilter = null;
var positionalArgs = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--compare" && i + 1 < args.Length)
    {
        compare = bool.Parse(args[++i]);
    }
    else if (args[i] == "--no-compare")
    {
        compare = false;
    }
    else if (args[i] == "--config" && i + 1 < args.Length)
    {
        configFilter = args[++i];
    }
    else
    {
        positionalArgs.Add(args[i]);
    }
}

// Usage: GenerateAll [--compare true/false] [--no-compare] [--config <name>] [bmfc-dir] [output-dir]
//   bmfc-dir:   directory containing .bmfc files (default: gum-bmfont/)
//   output-dir: directory for generated output (default: current directory)
var bmfcDir = positionalArgs.Count > 0
    ? positionalArgs[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "gum-bmfont"));

var outDir = positionalArgs.Count > 1
    ? positionalArgs[1]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "output"));

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
var generatedConfigs = new List<string>();

foreach (var bmfcPath in bmfcFiles)
{
    var configName = Path.GetFileNameWithoutExtension(bmfcPath);
    Console.WriteLine($"\n--- {configName} ---");
    generatedConfigs.Add(configName);

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

// --- Style variant generation for "plain" config ---
var plainBmfcPath = bmfcFiles.FirstOrDefault(f =>
    Path.GetFileNameWithoutExtension(f).Equals("plain", StringComparison.OrdinalIgnoreCase));

if (plainBmfcPath != null)
{
    // Create modified bmfc files with isBold=1 / isItalic=1 for real bold/italic
    var plainContent = File.ReadAllText(plainBmfcPath);
    var boldBmfc = plainContent.Replace("isBold=0", "isBold=1");
    var italicBmfc = plainContent.Replace("isItalic=0", "isItalic=1");
    var boldBmfcPath = Path.Combine(outDir, "plain-bold.bmfc");
    var italicBmfcPath = Path.Combine(outDir, "plain-italic.bmfc");
    File.WriteAllText(boldBmfcPath, boldBmfc);
    File.WriteAllText(italicBmfcPath, italicBmfc);

    // Real bold/italic: all backends (including BMFont64) using modified bmfc
    var realVariants = new (string Prefix, string BmfcPath)[]
    {
        ("plain-bold", boldBmfcPath),
        ("plain-italic", italicBmfcPath),
    };

    foreach (var (prefix, variantBmfcPath) in realVariants)
    {
        Console.WriteLine($"\n--- {prefix} ---");
        generatedConfigs.Add(prefix);

        foreach (var (backendName, factory) in backends)
        {
            Console.Write($"  {backendName} ... ");

            try
            {
                var result = BmFont.Builder()
                    .FromConfig(variantBmfcPath)
                    .WithRasterizer(factory())
                    .Build();

                var baseName = $"{prefix}-{backendName}";
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

        // BMFont64 reference output for real bold/italic
        if (bmfont64Path != null)
        {
            Console.Write("  bmfont ... ");

            try
            {
                var bmfontOutPath = Path.Combine(outDir, $"{prefix}-bmfont.fnt");
                var psi = new ProcessStartInfo
                {
                    FileName = bmfont64Path,
                    Arguments = $"-c \"{variantBmfcPath}\" -o \"{bmfontOutPath}\"",
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

    // Synthetic bold/italic: KernSmith backends only, using original plain.bmfc + WithBold/WithItalic
    var syntheticVariants = new (string Prefix, bool Bold, bool Italic)[]
    {
        ("plain-synbold", true, false),
        ("plain-synitalic", false, true),
    };

    foreach (var (prefix, bold, italic) in syntheticVariants)
    {
        Console.WriteLine($"\n--- {prefix} ---");
        generatedConfigs.Add(prefix);

        foreach (var (backendName, factory) in backends)
        {
            Console.Write($"  {backendName} ... ");

            try
            {
                var builder = BmFont.Builder()
                    .FromConfig(plainBmfcPath)
                    .WithRasterizer(factory());

                if (bold) builder.WithForceSyntheticBold(true);
                if (italic) builder.WithForceSyntheticItalic(true);

                var result = builder.Build();

                var baseName = $"{prefix}-{backendName}";
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

        // Copy BMFont64's real bold/italic output as reference for synthetic comparisons
        var realPrefix = bold ? "plain-bold" : "plain-italic";
        var bmfontFnt = Path.Combine(outDir, $"{realPrefix}-bmfont.fnt");
        var bmfontPng = Path.Combine(outDir, $"{realPrefix}-bmfont_0.png");
        if (File.Exists(bmfontFnt) && File.Exists(bmfontPng))
        {
            File.Copy(bmfontFnt, Path.Combine(outDir, $"{prefix}-bmfont.fnt"), true);
            File.Copy(bmfontPng, Path.Combine(outDir, $"{prefix}-bmfont_0.png"), true);
        }
    }
}
else
{
    Console.WriteLine("\nSkipping style variants: 'plain' config not found.");
}

Console.WriteLine($"\nGeneration done. {totalSucceeded} succeeded, {totalFailed} failed.");

// --- Comparison step ---
if (compare)
{
    Console.WriteLine("\n=== Generating comparison images ===");

    var allBackendNames = new[] { "freetype", "gdi", "directwrite", "bmfont" };
    var allBackendLabels = new[] { "FreeType", "GDI", "DW", "BMFont" };

    var synBackendNames = new[] { "freetype", "gdi", "directwrite", "bmfont" };
    var synBackendLabels = new[] { "FreeType", "GDI", "DW", "BMFont (real)" };

    var mixBoldBackendNames = new[] { "ft-norm", "ft-real", "ft-syn", "gdi-norm", "gdi-real", "gdi-syn", "dw-norm", "dw-real", "dw-syn", "bmf-norm", "bmf-style" };
    var mixBoldBackendLabels = new[] { "FT", "FT real", "FT syn", "GDI", "GDI real", "GDI syn", "DW", "DW real", "DW syn", "BMF", "BMF style" };

    // Fixed comparison outputs matching the original CompareGlyphs naming
    var comparisons = new (string Prefix, string OutputName, string[] Backends, string[] Labels, string Title)[]
    {
        ("fire", "comparison.png", allBackendNames, allBackendLabels, "Fire config — outlined font, all backends"),
        ("plain", "comparison2.png", allBackendNames, allBackendLabels, "Plain config — Georgia 56pt regular, all backends"),
        ("plain-bold", "comparison3.png", allBackendNames, allBackendLabels, "Real bold — Georgia Bold 56pt (native bold face)"),
        ("plain-italic", "comparison4.png", allBackendNames, allBackendLabels, "Real italic — Georgia Italic 56pt (native italic face)"),
        ("plain-synbold", "comparison5.png", synBackendNames, synBackendLabels, "Synthetic bold — Georgia 56pt regular + synthetic emboldening"),
        ("plain-synitalic", "comparison6.png", synBackendNames, synBackendLabels, "Synthetic italic — Georgia 56pt regular + synthetic oblique"),
        ("mix-bold", "comparison7.png", mixBoldBackendNames, mixBoldBackendLabels, "Bold — normal vs real face vs synthetic per backend"),
        ("mix-italic", "comparison8.png", mixBoldBackendNames, mixBoldBackendLabels, "Italic — normal vs real face vs synthetic per backend"),
    };

    // Create mix comparison files by copying/linking existing outputs
    var mixMappings = new (string MixPrefix, string Style, (string Slot, string SourcePrefix, string SourceBackend)[] Map)[]
    {
        ("mix-bold", "bold", new[]
        {
            ("ft-norm", "plain", "freetype"),
            ("ft-real", "plain-bold", "freetype"),
            ("ft-syn", "plain-synbold", "freetype"),
            ("gdi-norm", "plain", "gdi"),
            ("gdi-real", "plain-bold", "gdi"),
            ("gdi-syn", "plain-synbold", "gdi"),
            ("dw-norm", "plain", "directwrite"),
            ("dw-real", "plain-bold", "directwrite"),
            ("dw-syn", "plain-synbold", "directwrite"),
            ("bmf-norm", "plain", "bmfont"),
            ("bmf-style", "plain-bold", "bmfont"),
        }),
        ("mix-italic", "italic", new[]
        {
            ("ft-norm", "plain", "freetype"),
            ("ft-real", "plain-italic", "freetype"),
            ("ft-syn", "plain-synitalic", "freetype"),
            ("gdi-norm", "plain", "gdi"),
            ("gdi-real", "plain-italic", "gdi"),
            ("gdi-syn", "plain-synitalic", "gdi"),
            ("dw-norm", "plain", "directwrite"),
            ("dw-real", "plain-italic", "directwrite"),
            ("dw-syn", "plain-synitalic", "directwrite"),
            ("bmf-norm", "plain", "bmfont"),
            ("bmf-style", "plain-italic", "bmfont"),
        }),
    };

    foreach (var (mixPrefix, style, map) in mixMappings)
    {
        bool allFound = true;
        foreach (var (slot, sourcePrefix, sourceBackend) in map)
        {
            var srcFnt = Path.Combine(outDir, $"{sourcePrefix}-{sourceBackend}.fnt");
            var srcPng = Path.Combine(outDir, $"{sourcePrefix}-{sourceBackend}_0.png");
            var dstFnt = Path.Combine(outDir, $"{mixPrefix}-{slot}.fnt");
            var dstPng = Path.Combine(outDir, $"{mixPrefix}-{slot}_0.png");

            if (File.Exists(srcFnt) && File.Exists(srcPng))
            {
                File.Copy(srcFnt, dstFnt, true);
                File.Copy(srcPng, dstPng, true);
            }
            else
            {
                allFound = false;
            }
        }
        if (allFound) generatedConfigs.Add(mixPrefix);
    }

    // Also generate per-config comparisons for any remaining configs
    var fixedPrefixes = comparisons.Select(c => c.Prefix).ToHashSet(StringComparer.OrdinalIgnoreCase);

    var configsToCompare = configFilter != null
        ? generatedConfigs.Where(c => c.Equals(configFilter, StringComparison.OrdinalIgnoreCase)).ToList()
        : generatedConfigs;

    int comparedCount = 0;

    // Generate the fixed comparisons first
    foreach (var (prefix, outputName, bNames, bLabels, title) in comparisons)
    {
        if (configFilter != null && !prefix.Equals(configFilter, StringComparison.OrdinalIgnoreCase))
            continue;
        if (!generatedConfigs.Contains(prefix, StringComparer.OrdinalIgnoreCase))
            continue;

        Console.Write($"  {outputName} ({prefix}) ... ");
        try
        {
            GenerateComparison(outDir, prefix, outputName, bNames, bLabels, title);
            comparedCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
        }
    }

    // Generate per-config comparisons for configs not covered by the fixed set
    foreach (var configName in configsToCompare)
    {
        if (fixedPrefixes.Contains(configName)) continue;

        var outputName = $"comparison-{configName}.png";
        Console.Write($"  {configName} ... ");

        try
        {
            GenerateComparison(outDir, configName, outputName, allBackendNames, allBackendLabels, configName);
            comparedCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.Message}");
        }
    }

    Console.WriteLine($"\nComparison done. {comparedCount} images generated.");
}

return totalFailed > 0 ? 1 : 0;

// --- Comparison helpers ---

static void GenerateComparison(string basePath, string prefix, string outputName,
    string[] backendNames, string[] backendLabels, string title)
{
    var allChars = new Dictionary<string, Dictionary<int, CharInfo>>();
    var atlasImages = new Dictionary<string, Bitmap>();

    foreach (var backend in backendNames)
    {
        var fntPath = Path.Combine(basePath, $"{prefix}-{backend}.fnt");
        var pngPath = Path.Combine(basePath, $"{prefix}-{backend}_0.png");
        if (!File.Exists(fntPath) || !File.Exists(pngPath))
            continue;

        allChars[backend] = ParseFnt(fntPath);
        atlasImages[backend] = new Bitmap(pngPath);
    }

    if (atlasImages.Count == 0)
    {
        Console.WriteLine($"SKIPPED (no output files found)");
        return;
    }

    var activeBackends = backendNames.Where(b => atlasImages.ContainsKey(b)).ToArray();
    var activeLabels = backendNames
        .Select((b, i) => (b, backendLabels[i]))
        .Where(x => atlasImages.ContainsKey(x.b))
        .Select(x => x.Item2)
        .ToArray();

    var codepoints = Enumerable.Range(32, 95)
        .Where(cp => activeBackends.Any(b => allChars[b].TryGetValue(cp, out var c) && c.Width > 0 && c.Height > 0))
        .OrderBy(cp => cp)
        .ToList();

    if (codepoints.Count == 0)
    {
        Console.WriteLine($"SKIPPED (no visible glyphs)");
        foreach (var img in atlasImages.Values) img.Dispose();
        return;
    }

    const int labelColWidth = 100;
    const int glyphColWidth = 80;
    const int glyphColHeight = 70;
    const int titleHeight = 28;
    const int headerHeight = 30;
    const int padding = 4;

    int totalWidth = labelColWidth + glyphColWidth * activeBackends.Length + padding * (activeBackends.Length + 1);
    int totalHeight = titleHeight + headerHeight + codepoints.Count * glyphColHeight;

    using var output = new Bitmap(totalWidth, totalHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(output);
    g.Clear(Color.Transparent);

    using var font = new Font("Arial", 12);
    using var titleFont = new Font("Arial", 11, FontStyle.Italic);
    using var headerFont = new Font("Arial", 12, FontStyle.Bold);
    using var blackBrush = new SolidBrush(Color.Black);
    using var grayBrush = new SolidBrush(Color.FromArgb(80, 80, 80));
    using var redBrush = new SolidBrush(Color.FromArgb(255, 200, 200));

    // Title row describing what this comparison shows
    g.DrawString(title, titleFont, grayBrush, 4, 4);

    int colHeaderY = titleHeight + 4;
    g.DrawString("Char", headerFont, blackBrush, 4, colHeaderY);
    for (int i = 0; i < activeBackends.Length; i++)
        g.DrawString(activeLabels[i], headerFont, blackBrush, labelColWidth + i * glyphColWidth + padding, colHeaderY);

    for (int row = 0; row < codepoints.Count; row++)
    {
        int cp = codepoints[row];
        int y = titleHeight + headerHeight + row * glyphColHeight;
        char ch = (char)cp;
        string label = $"{(char.IsControl(ch) ? "?" : ch.ToString())} ({cp})";
        g.DrawString(label, font, blackBrush, 4, y + 4);

        int maxW = 0, maxH = 0;
        foreach (var backend in activeBackends)
        {
            if (allChars[backend].TryGetValue(cp, out var ci) && ci.Width > 0 && ci.Height > 0)
            {
                maxW = Math.Max(maxW, ci.Width);
                maxH = Math.Max(maxH, ci.Height);
            }
        }

        float rowScale = 1f;
        if (maxW > 0 && maxH > 0)
        {
            rowScale = Math.Min((float)(glyphColWidth - padding * 2) / maxW, (float)(glyphColHeight - 4) / maxH);
            if (rowScale > 1) rowScale = 1;
        }

        for (int col = 0; col < activeBackends.Length; col++)
        {
            int x = labelColWidth + col * glyphColWidth + padding;
            var backend = activeBackends[col];

            if (!allChars[backend].TryGetValue(cp, out var ci) || ci.Width == 0 || ci.Height == 0)
            {
                g.FillRectangle(redBrush, x, y, glyphColWidth - padding, glyphColHeight - 2);
                continue;
            }

            var atlas = atlasImages[backend];
            int sw = Math.Min(ci.Width, atlas.Width - ci.X);
            int sh = Math.Min(ci.Height, atlas.Height - ci.Y);
            if (sw <= 0 || sh <= 0) continue;

            var srcRect = new Rectangle(ci.X, ci.Y, sw, sh);
            int dw = (int)(sw * rowScale);
            int dh = (int)(sh * rowScale);
            int dy = y + 2 + (glyphColHeight - 4 - dh) / 2;
            g.DrawImage(atlas, new Rectangle(x, dy, dw, dh), srcRect, GraphicsUnit.Pixel);
        }
    }

    var outputPath = Path.Combine(basePath, outputName);
    output.Save(outputPath, ImageFormat.Png);
    Console.WriteLine($"OK ({codepoints.Count} chars, {totalWidth}x{totalHeight})");

    foreach (var img in atlasImages.Values) img.Dispose();
}

static Dictionary<int, CharInfo> ParseFnt(string path)
{
    var chars = new Dictionary<int, CharInfo>();
    foreach (var line in File.ReadLines(path))
    {
        if (!line.StartsWith("char ") || line.StartsWith("chars ")) continue;
        chars[GetInt(line, "id")] = new CharInfo(
            GetInt(line, "x"), GetInt(line, "y"),
            GetInt(line, "width"), GetInt(line, "height"));
    }
    return chars;
}

static int GetInt(string line, string key)
{
    var match = Regex.Match(line, $@"{key}=(-?\d+)");
    return match.Success ? int.Parse(match.Groups[1].Value) : 0;
}

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

record CharInfo(int X, int Y, int Width, int Height);
