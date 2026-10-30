using System.Diagnostics;
using KernSmith.Output;
using Shouldly;
using static KernSmith.Tests.Font.Woff2TestSupport;

namespace KernSmith.Tests.Font;

/// <summary>
/// A WOFF2 font must render exactly like the TTF it was made from on <b>every</b> backend:
/// core decodes WOFF2 to sfnt before any rasterizer sees the bytes, so the only way a backend
/// could differ is if the decoded font differs. Mirrors <see cref="WoffAllBackendsTests"/>.
/// </summary>
[Collection("RasterizerFactory")]
public class Woff2AllBackendsTests
{
    private const int TestSize = 28;

    /// <summary>Plain Latin plus accented letters, which are composite glyphs in every fixture.</summary>
    private const string TestText = "AWTVaeogy.,!?0123ÀÉÑçöÅÿ";

    private static FontGeneratorOptions OptionsFor(RasterizerBackend backend) => new()
    {
        Size = TestSize,
        Characters = CharacterSet.FromChars(TestText),
        Backend = backend,
        Kerning = true,
    };

    public static TheoryData<RasterizerBackend, string> BackendFixtures()
    {
        var data = new TheoryData<RasterizerBackend, string>();
        RasterizerBackend[] backends =
        [
            RasterizerBackend.FreeType,
            RasterizerBackend.StbTrueType,
#if WINDOWS
            RasterizerBackend.Gdi,
#endif
#if DIRECTWRITE
            RasterizerBackend.DirectWrite,
#endif
        ];
        foreach (var backend in backends)
        {
            foreach (var fixture in FixtureNames)
                data.Add(backend, fixture);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BackendFixtures))]
    public void Generate_FromWoff2_MatchesTheSameFontAsTtf(RasterizerBackend backend, string fixture)
    {
        var fromTtf = BmFont.Generate(Ttf(fixture), OptionsFor(backend));
        var fromWoff2 = BmFont.Generate(Woff2(fixture), OptionsFor(backend));

        fromTtf.Model.Characters.Count.ShouldBeGreaterThan(TestText.Length - 2, $"{backend} should render the test text from the TTF");
        fromTtf.Pages[0].PixelData.Any(b => b != 0).ShouldBeTrue($"{backend} rendered a blank atlas from the TTF");

        fromWoff2.FntText.ShouldBe(fromTtf.FntText, $"{backend}/{fixture}: .fnt (metrics, kerning) should match the TTF");
        fromWoff2.Pages.Count.ShouldBe(fromTtf.Pages.Count);
        for (var i = 0; i < fromTtf.Pages.Count; i++)
        {
            fromWoff2.Pages[i].PixelData.ShouldBe(fromTtf.Pages[i].PixelData,
                $"{backend}/{fixture}: page {i} pixels should match the TTF");
        }
    }

    // ──────────────────────────────────────────────
    // Native backend (out of process, like WoffAllBackendsTests)
    // ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Cli_Generate_NativeBackend_FromWoff2_MatchesTtf(string fixture)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "kernsmith-woff2-native-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var outputs = new Dictionary<string, string>();
            foreach (var extension in new[] { "ttf", "woff2" })
            {
                var fontPath = Path.Combine(tempDir, $"{fixture}.{extension}");
                File.WriteAllBytes(fontPath, Fixture($"{fixture}.{extension}"));
                var outputBase = Path.Combine(tempDir, $"native-{extension}");

                var (exitCode, stdout, stderr) = RunCli(
                    "generate", "-f", fontPath, "-s", TestSize.ToString(), "-o", outputBase,
                    "--rasterizer", "native", "--chars", TestText);

                exitCode.ShouldBe(0, $"Native should accept {extension} input. stderr: {stderr}");
                stdout.ShouldContain("Done.");
                File.Exists(outputBase + "_0.png").ShouldBeTrue($"Native should write an atlas page from {extension}");
                outputs[extension] = outputBase;
            }

            File.ReadAllBytes(outputs["woff2"] + "_0.png").ShouldBe(File.ReadAllBytes(outputs["ttf"] + "_0.png"),
                "Native should render identical pixels from WOFF2 and from the source TTF");
            File.ReadAllText(outputs["woff2"] + ".fnt").Replace("native-woff2", "native-ttf")
                .ShouldBe(File.ReadAllText(outputs["ttf"] + ".fnt"), "Native .fnt should match");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    public static TheoryData<string> Fixtures() => new(FixtureNames);

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var tfm = Path.GetFileName(baseDir);
        var configuration = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var cliDllPath = Path.Combine(repoRoot, "tools", "KernSmith.Cli", "bin", configuration, tfm, "KernSmith.Cli.dll");

        if (!File.Exists(cliDllPath))
            throw new FileNotFoundException(
                $"The CLI was not built at '{cliDllPath}'. Build the solution for this " +
                "configuration first; this test runs the real CLI as a child process.",
                cliDllPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(cliDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return (process.ExitCode, stdout.Result, stderr.Result);
    }
}
