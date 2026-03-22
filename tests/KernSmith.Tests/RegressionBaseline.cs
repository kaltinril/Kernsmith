using System.Security.Cryptography;
using Xunit;

namespace KernSmith.Tests;

/// <summary>
/// Generates baseline font outputs for regression testing.
/// Run once BEFORE making changes, then run the comparison tests AFTER.
/// </summary>
public class RegressionBaseline
{
    private static readonly string FontPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    private static readonly Lazy<byte[]> FontData = new(() => File.ReadAllBytes(FontPath));

    private static readonly string BaselineDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "output", "baseline-regression");

    private record BaselineConfig(string Name, Action<BmFontBuilder> Configure);

    private static readonly BaselineConfig[] Configs =
    [
        // Basic configurations
        new("ascii-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii)),
        new("ascii-16", b => b.WithFont(FontData.Value).WithSize(16).WithCharacters(CharacterSet.Ascii)),
        new("ascii-64", b => b.WithFont(FontData.Value).WithSize(64).WithCharacters(CharacterSet.Ascii)),
        new("extended-ascii-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.ExtendedAscii)),
        new("latin-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Latin)),

        // Styling
        new("bold-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithBold()),
        new("italic-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithItalic()),
        new("bold-italic-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithBold().WithItalic()),

        // Anti-aliasing modes
        new("aa-none-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithAntiAlias(AntiAliasMode.None)),
        new("aa-light-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithAntiAlias(AntiAliasMode.Light)),

        // Effects
        new("outline-2-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithOutline(2)),
        new("outline-3-red-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithOutline(3, 255, 0, 0)),
        new("shadow-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithShadow(2, 2, 1)),
        new("gradient-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithGradient((255, 215, 0), (220, 20, 60), 90f)),
        new("all-effects-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii)
            .WithOutline(2, 0, 0, 0).WithShadow(2, 2, 1).WithGradient((255, 215, 0), (220, 20, 60), 90f).WithPadding(2)),

        // Atlas configuration
        new("texture-512-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithMaxTextureSize(512)),
        new("texture-2048-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithMaxTextureSize(2048)),
        new("padding-4-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithPadding(4)),
        new("spacing-3-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithSpacing(3)),
        new("skyline-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithPackingAlgorithm(PackingAlgorithm.Skyline)),
        new("autofit-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithAutofitTexture()),
        new("no-pot-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithPowerOfTwo(false).WithAutofitTexture()),

        // Advanced rendering
        new("sdf-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithSdf()),
        new("supersample-2-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithSuperSampling(2)),
        new("no-hinting-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithHinting(false)),
        new("height-150-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithHeightPercent(150)),
        new("equalize-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithEqualizeCellHeights()),
        new("force-offsets-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithForceOffsetsToZero()),
        new("match-char-height-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithMatchCharHeight()),

        // Kerning
        new("no-kerning-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithKerning(false)),

        // Output formats (same generation, different serialization)
        new("xml-output-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii)),

        // Custom characters
        new("custom-chars-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.FromChars("ABCDEFGabcdefg0123456789!@#$%"))),
        new("digits-only-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.FromChars("0123456789"))),

        // DPI
        new("dpi-96-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithDpi(96)),
        new("dpi-144-32", b => b.WithFont(FontData.Value).WithSize(32).WithCharacters(CharacterSet.Ascii).WithDpi(144)),
    ];

    [Fact]
    public void GenerateAllBaselines()
    {
        Directory.CreateDirectory(BaselineDir);
        var results = new List<string>();

        foreach (var config in Configs)
        {
            var builder = BmFont.Builder();
            config.Configure(builder);
            var result = builder.Build();

            // Save .fnt text
            var fntPath = Path.Combine(BaselineDir, $"{config.Name}.fnt");
            File.WriteAllText(fntPath, result.FntText);

            // Save XML variant for the xml test
            if (config.Name == "xml-output-32")
            {
                var xmlPath = Path.Combine(BaselineDir, $"{config.Name}.xml");
                File.WriteAllText(xmlPath, result.FntXml);
            }

            // Save PNG hash (not the full PNG — just the hash for comparison)
            var pngData = result.GetPngData();
            var pngHashes = new List<string>();
            for (int i = 0; i < pngData.Length; i++)
            {
                var hash = Convert.ToHexString(SHA256.HashData(pngData[i]));
                pngHashes.Add($"page{i}:{hash}");
            }

            // Save atlas page metadata
            var pageInfo = result.Pages.Select(p =>
                $"page{p.PageIndex}:{p.Width}x{p.Height}:{p.Format}:{p.PixelData.Length}bytes").ToList();

            // Save summary
            var summary = new List<string>
            {
                $"config={config.Name}",
                $"glyphs={result.Model.Characters.Count}",
                $"kerning_pairs={result.Model.KerningPairs.Count}",
                $"pages={result.Model.Pages.Count}",
                $"line_height={result.Model.Common.LineHeight}",
                $"base={result.Model.Common.Base}",
                $"scale_w={result.Model.Common.ScaleW}",
                $"scale_h={result.Model.Common.ScaleH}",
                $"face={result.Model.Info.Face}",
                $"size={result.Model.Info.Size}",
                $"bold={result.Model.Info.Bold}",
                $"italic={result.Model.Info.Italic}",
                $"failed_codepoints={result.FailedCodepoints.Count}",
            };
            foreach (var ph in pngHashes) summary.Add($"png_hash_{ph}");
            foreach (var pi in pageInfo) summary.Add($"page_info_{pi}");

            // Save per-character metrics for exact regression
            foreach (var ch in result.Model.Characters.OrderBy(c => c.Id))
            {
                summary.Add($"char:{ch.Id}:x={ch.X},y={ch.Y},w={ch.Width},h={ch.Height},xoff={ch.XOffset},yoff={ch.YOffset},xadv={ch.XAdvance},page={ch.Page},chn={ch.Channel}");
            }

            foreach (var kp in result.Model.KerningPairs.OrderBy(k => k.First).ThenBy(k => k.Second))
            {
                summary.Add($"kern:{kp.First},{kp.Second}={kp.Amount}");
            }

            var summaryPath = Path.Combine(BaselineDir, $"{config.Name}.baseline");
            File.WriteAllLines(summaryPath, summary);
            results.Add($"OK: {config.Name} ({result.Model.Characters.Count} glyphs, {result.Model.Pages.Count} pages)");
        }

        // Write master manifest
        File.WriteAllLines(Path.Combine(BaselineDir, "MANIFEST.txt"), results);
        Assert.True(results.Count == Configs.Length, $"Generated {results.Count}/{Configs.Length} baselines");
    }

    [Fact]
    public void CompareAgainstBaselines()
    {
        var manifestPath = Path.Combine(BaselineDir, "MANIFEST.txt");
        Assert.True(File.Exists(manifestPath), "No baselines found. Run GenerateAllBaselines first.");

        var failures = new List<string>();

        foreach (var config in Configs)
        {
            var baselinePath = Path.Combine(BaselineDir, $"{config.Name}.baseline");
            if (!File.Exists(baselinePath))
            {
                failures.Add($"MISSING: {config.Name} baseline file not found");
                continue;
            }

            var baselineLines = File.ReadAllLines(baselinePath);

            var builder = BmFont.Builder();
            config.Configure(builder);
            var result = builder.Build();

            // Regenerate summary with same format
            var pngData = result.GetPngData();
            var pngHashes = new List<string>();
            for (int i = 0; i < pngData.Length; i++)
            {
                var hash = Convert.ToHexString(SHA256.HashData(pngData[i]));
                pngHashes.Add($"page{i}:{hash}");
            }

            var pageInfo = result.Pages.Select(p =>
                $"page{p.PageIndex}:{p.Width}x{p.Height}:{p.Format}:{p.PixelData.Length}bytes").ToList();

            var current = new List<string>
            {
                $"config={config.Name}",
                $"glyphs={result.Model.Characters.Count}",
                $"kerning_pairs={result.Model.KerningPairs.Count}",
                $"pages={result.Model.Pages.Count}",
                $"line_height={result.Model.Common.LineHeight}",
                $"base={result.Model.Common.Base}",
                $"scale_w={result.Model.Common.ScaleW}",
                $"scale_h={result.Model.Common.ScaleH}",
                $"face={result.Model.Info.Face}",
                $"size={result.Model.Info.Size}",
                $"bold={result.Model.Info.Bold}",
                $"italic={result.Model.Info.Italic}",
                $"failed_codepoints={result.FailedCodepoints.Count}",
            };
            foreach (var ph in pngHashes) current.Add($"png_hash_{ph}");
            foreach (var pi in pageInfo) current.Add($"page_info_{pi}");

            foreach (var ch in result.Model.Characters.OrderBy(c => c.Id))
            {
                current.Add($"char:{ch.Id}:x={ch.X},y={ch.Y},w={ch.Width},h={ch.Height},xoff={ch.XOffset},yoff={ch.YOffset},xadv={ch.XAdvance},page={ch.Page},chn={ch.Channel}");
            }

            foreach (var kp in result.Model.KerningPairs.OrderBy(k => k.First).ThenBy(k => k.Second))
            {
                current.Add($"kern:{kp.First},{kp.Second}={kp.Amount}");
            }

            // Compare ordered lines
            if (baselineLines.Length != current.Count)
            {
                failures.Add($"DIFF: {config.Name} — line count {baselineLines.Length} vs {current.Count}");
            }
            var diffCount = 0;
            for (int i = 0; i < Math.Min(baselineLines.Length, current.Count); i++)
            {
                if (baselineLines[i] != current[i])
                {
                    if (diffCount < 10)
                    {
                        failures.Add($"DIFF: {config.Name} line {i + 1}");
                        failures.Add($"  - BASELINE: {baselineLines[i]}");
                        failures.Add($"  + CURRENT:  {current[i]}");
                    }
                    diffCount++;
                }
            }
            if (diffCount > 10)
            {
                failures.Add($"  ... and {diffCount - 10} more differences in {config.Name}");
            }

            // Compare .fnt text files
            var fntPath = Path.Combine(BaselineDir, $"{config.Name}.fnt");
            if (File.Exists(fntPath))
            {
                var baselineFnt = File.ReadAllText(fntPath);
                var currentFnt = result.FntText;
                if (baselineFnt != currentFnt)
                {
                    failures.Add($"DIFF: {config.Name} .fnt text differs");
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"Regression detected:\n{string.Join('\n', failures)}");
        }
    }
}
