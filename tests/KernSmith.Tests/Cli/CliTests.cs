using System.Diagnostics;
using Shouldly;

namespace KernSmith.Tests.Cli;

public class CliTests : IDisposable
{
    private static readonly string CliDllPath;

    private static readonly string FontPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    private readonly string _tempDir;

    static CliTests()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var tfm = Path.GetFileName(baseDir);
        CliDllPath = Path.Combine(repoRoot, "tools", "KernSmith.Cli", "bin", "Debug", tfm, "KernSmith.Cli.dll");
    }

    public CliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kernsmith-cli-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(CliDllPath);

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI process timed out after 30s");
        }

        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    // -- Help and usage --

    [Fact]
    public void NoArguments_ShowsHelp_ExitCode0()
    {
        // Act
        var (exitCode, stdout, _) = RunCli();

        // Assert
        exitCode.ShouldBe(0);
        stdout.ShouldContain("Usage:");
        stdout.ShouldContain("generate");
        stdout.ShouldContain("list-fonts");
    }

    [Fact]
    public void HelpFlag_ShowsHelp_ExitCode0()
    {
        // Act
        var (exitCode, stdout, _) = RunCli("--help");

        // Assert
        exitCode.ShouldBe(0);
        stdout.ShouldContain("Usage:");
        stdout.ShouldContain("--font");
        stdout.ShouldContain("--size");
        stdout.ShouldContain("--format");
    }

    // -- Error handling --

    [Fact]
    public void UnknownCommand_ReturnsExitCode1()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("bogus");

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("Unknown command: bogus");
    }

    [Fact]
    public void Generate_MissingFont_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "-s", "32");

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("--font is required");
    }

    [Fact]
    public void Generate_MissingSize_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath);

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("--size is required");
    }

    [Fact]
    public void Generate_NonexistentFontFile_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "-f", "/no/such/font.ttf", "-s", "32");

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("Font file not found");
    }

    [Fact]
    public void Generate_UnknownOption_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "--bogus-flag");

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("Unknown option: --bogus-flag");
    }

    // -- Basic generation (text format, default) --

    [Fact]
    public void Generate_DefaultOptions_ProducesFntAndPng()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "test-output");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");

        File.Exists(outputBase + ".fnt").ShouldBeTrue("should produce a .fnt file");

        // PNG pages are named {outputBaseName}_{pageIndex}.png in the output directory
        var pngPath = Path.Combine(_tempDir, "test-output_0.png");
        File.Exists(pngPath).ShouldBeTrue("should produce at least one .png page");

        var fntContent = File.ReadAllText(outputBase + ".fnt");
        fntContent.ShouldContain("info face=\"Roboto\"");
        fntContent.ShouldContain("common ");
        fntContent.ShouldContain("char id=");
    }

    // -- Output formats --

    [Fact]
    public void Generate_TextFormat_ProducesValidTextFnt()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "text-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-o", outputBase, "--format", "text");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.ShouldStartWith("info ");
        fnt.ShouldContain("page id=");
    }

    [Fact]
    public void Generate_XmlFormat_ProducesValidXmlFnt()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "xml-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-o", outputBase, "--format", "xml");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.ShouldContain("<font>");
        fnt.ShouldContain("face=\"Roboto\"");
    }

    [Fact]
    public void Generate_BinaryFormat_ProducesValidBinaryFnt()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "bin-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-o", outputBase, "--format", "binary");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var bytes = File.ReadAllBytes(outputBase + ".fnt");
        // BMF header + version 3
        bytes[0].ShouldBe((byte)66);
        bytes[1].ShouldBe((byte)77);
        bytes[2].ShouldBe((byte)70);
        bytes[3].ShouldBe((byte)3);
    }

    // -- Size option --

    [Fact]
    public void Generate_DifferentSizes_ProduceDifferentLineHeights()
    {
        // Arrange
        var smallOutput = Path.Combine(_tempDir, "small");
        var largeOutput = Path.Combine(_tempDir, "large");

        // Act
        var (exitCode1, _, stderr1) = RunCli(
            "generate", "-f", FontPath, "-s", "12", "-o", smallOutput);
        var (exitCode2, _, stderr2) = RunCli(
            "generate", "-f", FontPath, "-s", "48", "-o", largeOutput);

        // Assert
        exitCode1.ShouldBe(0, $"stderr: {stderr1}");
        exitCode2.ShouldBe(0, $"stderr: {stderr2}");

        var smallFnt = File.ReadAllText(smallOutput + ".fnt");
        var largeFnt = File.ReadAllText(largeOutput + ".fnt");

        // Extract lineHeight from "common lineHeight=<N>"
        var smallLineHeight = ExtractIntAttribute(smallFnt, "lineHeight");
        var largeLineHeight = ExtractIntAttribute(largeFnt, "lineHeight");

        largeLineHeight.ShouldBeGreaterThan(smallLineHeight,
            "48px font should have larger line height than 12px font");
    }

    // -- Charset option --

    [Fact]
    public void Generate_CustomCharset_ProducesOnlyRequestedChars()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "charset-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase, "-c", "ABC");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.ShouldContain("chars count=3");
        fnt.ShouldContain("char id=65 ");  // A
        fnt.ShouldContain("char id=66 ");  // B
        fnt.ShouldContain("char id=67 ");  // C
    }

    // -- Padding option --

    [Fact]
    public void Generate_WithPadding_IncludesPaddingInOutput()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "pad-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase,
            "--padding", "4", "-c", "A");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        // The info block should reflect the padding values
        fnt.ShouldContain("padding=4,4,4,4");
    }

    // -- Spacing option --

    [Fact]
    public void Generate_WithSpacing_IncludesSpacingInOutput()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "spacing-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase,
            "--spacing", "3", "-c", "A");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.ShouldContain("spacing=3,3");
    }

    // -- Packer option --

    [Fact]
    public void Generate_SkylinePacker_Succeeds()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "skyline-out");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-o", outputBase,
            "--packer", "skyline");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");
        File.Exists(outputBase + ".fnt").ShouldBeTrue();

        var pngPath = Path.Combine(_tempDir, "skyline-out_0.png");
        File.Exists(pngPath).ShouldBeTrue();
    }

    // -- Short flag aliases --

    [Fact]
    public void Generate_ShortFlags_WorkIdenticallyToLongFlags()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "short-out");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-o", outputBase, "-c", "Hello");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");
        File.Exists(outputBase + ".fnt").ShouldBeTrue();

        var fnt = File.ReadAllText(outputBase + ".fnt");
        // "Hello" has 4 unique chars: H, e, l, o
        fnt.ShouldContain("chars count=4");
    }

    // -- Max texture size --

    [Fact]
    public void Generate_MaxTextureSize_RespectsLimit()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "maxtex-out");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase,
            "--max-texture-size", "256");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        var scaleW = ExtractIntAttribute(fnt, "scaleW");
        var scaleH = ExtractIntAttribute(fnt, "scaleH");

        scaleW.ShouldBeLessThanOrEqualTo(256);
        scaleH.ShouldBeLessThanOrEqualTo(256);
    }

    // -- Progress output --

    [Fact]
    public void Generate_PrintsProgressMessages()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "progress-out");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Loading font:");
        stdout.ShouldContain("Rasterizing");
        stdout.ShouldContain("Packing into atlas");
        stdout.ShouldContain("Writing output to");
        stdout.ShouldContain("Done.");
    }

    // -- Output PNG validation --

    [Fact]
    public void Generate_OutputPng_HasValidPngHeader()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "png-check");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");

        var pngPath = Path.Combine(_tempDir, "png-check_0.png");
        var pngBytes = File.ReadAllBytes(pngPath);
        pngBytes.Length.ShouldBeGreaterThan(8);
        // PNG magic bytes
        pngBytes[0].ShouldBe((byte)137);
        pngBytes[1].ShouldBe((byte)80);  // P
        pngBytes[2].ShouldBe((byte)78);  // N
        pngBytes[3].ShouldBe((byte)71);  // G
    }

    // -- Phase 84: .hiero / .bmfc config format support --

    [Fact]
    public void Init_HieroExtension_WritesHieroFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "myfont.hiero");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configPath);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configPath).ShouldBeTrue("init should honor the .hiero extension");
        stdout.ShouldContain(configPath);

        // .hiero files are Java-properties style with font.* keys
        var content = File.ReadAllText(configPath);
        content.ShouldContain("font.size=32");
    }

    [Fact]
    public void Init_NoExtension_DefaultsToBmfc()
    {
        // Arrange
        var configBase = Path.Combine(_tempDir, "myfont");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configBase);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configBase + ".bmfc").ShouldBeTrue("no extension should default to .bmfc");
        File.Exists(configBase + ".hiero").ShouldBeFalse();

        // .bmfc files are BMFont-style with a configuration block
        var content = File.ReadAllText(configBase + ".bmfc");
        content.ShouldContain("fileVersion=1");
    }

    [Fact]
    public void Init_CfgExtension_RespectedNotSuffixedWithBmfc()
    {
        // A path WITH an explicit extension must be respected as-is: init must NOT
        // append .bmfc (so x.cfg stays x.cfg, never becomes x.cfg.bmfc).
        var configPath = Path.Combine(_tempDir, "x.cfg");

        var (exitCode, stdout, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configPath);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configPath).ShouldBeTrue("x.cfg should be respected verbatim");
        File.Exists(configPath + ".bmfc").ShouldBeFalse("init must NOT append .bmfc to a path that already has an extension");
        stdout.ShouldContain(configPath);

        // A non-.hiero extension is written as BMFont content.
        File.ReadAllText(configPath).ShouldContain("fileVersion=1");
    }

    [Fact]
    public void Init_ArbitraryExtension_RespectedWithBmfcContent()
    {
        // Any explicit non-.hiero extension is respected and written as BMFont format.
        var configPath = Path.Combine(_tempDir, "a.abc");

        var (exitCode, _, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configPath);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configPath).ShouldBeTrue("a.abc should be respected verbatim");
        File.Exists(configPath + ".bmfc").ShouldBeFalse("init must NOT append .bmfc to a.abc");
        File.ReadAllText(configPath).ShouldContain("fileVersion=1");
    }

    [Fact]
    public void Init_BmfcExtension_WritesBmfcFile()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "myfont.bmfc");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configPath);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configPath).ShouldBeTrue();
        File.ReadAllText(configPath).ShouldContain("fileVersion=1");
    }

    [Fact]
    public void Init_HieroExtensionWithTrailingSpace_WritesHieroContent()
    {
        // L4: a -o value with a trailing space ("name.hiero ") is trimmed by init, and the
        // extension is also trimmed when selecting the writer, so the file is Hiero content.
        // The on-disk name is the trimmed "name.hiero".
        var configPath = Path.Combine(_tempDir, "trailing.hiero");

        var (exitCode, _, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configPath + " ");

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configPath).ShouldBeTrue("init trims the trailing space from the output path");
        var content = File.ReadAllText(configPath);
        // Hiero-style keys, not BMFont's fileVersion block.
        content.ShouldContain("font.size=32");
        content.ShouldNotContain("fileVersion=1");
    }

    [Fact]
    public void Init_TrailingDotNoExtension_ProducesSingleBmfcExtension()
    {
        // nit: "myfont." has no real extension, so init appends .bmfc. The lone trailing dot
        // must be trimmed first so the result is "myfont.bmfc", never "myfont..bmfc".
        var configBase = Path.Combine(_tempDir, "myfont.");

        var (exitCode, _, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "32", "-c", "A-Z", "-o", configBase);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        var expected = Path.Combine(_tempDir, "myfont.bmfc");
        File.Exists(expected).ShouldBeTrue("a trailing dot must collapse to a single .bmfc extension");
        File.Exists(Path.Combine(_tempDir, "myfont..bmfc")).ShouldBeFalse("must NOT produce a double-dot myfont..bmfc");
        File.ReadAllText(expected).ShouldContain("fileVersion=1");
    }

    [Fact]
    public void Generate_SaveConfigHiero_WritesHieroFile()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "save-hiero-out");
        var configPath = Path.Combine(_tempDir, "exported.hiero");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "48", "-c", "A-Z",
            "-o", outputBase, "--save-config", configPath);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(configPath).ShouldBeTrue("--save-config should write a .hiero file");
        File.ReadAllText(configPath).ShouldContain("font.size=48");
    }

    [Fact]
    public void Generate_ConfigHiero_AutoDetectsAndGenerates()
    {
        // Arrange: first create a .hiero config via --save-config, then load it.
        var firstOut = Path.Combine(_tempDir, "first-out");
        var configPath = Path.Combine(_tempDir, "game.hiero");
        var (saveExit, _, saveErr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-c", "ABC",
            "-o", firstOut, "--save-config", configPath);
        saveExit.ShouldBe(0, $"save stderr: {saveErr}");
        File.Exists(configPath).ShouldBeTrue();

        var outputBase = Path.Combine(_tempDir, "from-hiero");

        // Act: load the .hiero config (format auto-detected from its CONTENT, not the extension)
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "--config", configPath, "-f", FontPath, "-o", outputBase);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");
        File.Exists(outputBase + ".fnt").ShouldBeTrue();
    }

    [Fact]
    public void Batch_MixedBmfcAndHieroGlob_ProcessesBoth()
    {
        // Arrange: create one .bmfc and one .hiero config, each with a distinct output.
        var bmfcOut = Path.Combine(_tempDir, "batch-bmfc");
        var hieroOut = Path.Combine(_tempDir, "batch-hiero");
        var bmfcConfig = Path.Combine(_tempDir, "a.bmfc");
        var hieroConfig = Path.Combine(_tempDir, "b.hiero");

        RunCli("init", "-f", FontPath, "-s", "24", "-c", "AB", "-o", bmfcOut).ExitCode.ShouldBe(0);
        RunCli("init", "-f", FontPath, "-s", "24", "-c", "CD", "-o", hieroOut + ".hiero").ExitCode.ShouldBe(0);

        // init writes config alongside; move the generated configs to predictable glob names
        File.Move(bmfcOut + ".bmfc", bmfcConfig);
        File.Move(hieroOut + ".hiero", hieroConfig);

        // Act: glob both extensions
        var (exitCode, stdout, stderr) = RunCli(
            "batch",
            Path.Combine(_tempDir, "*.bmfc"),
            Path.Combine(_tempDir, "*.hiero"));

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Processing 2 job(s)");
        stdout.ShouldContain("2 succeeded, 0 failed");
    }

    [Fact]
    public void Batch_NoConfigs_ReportsBmfcOrHieroMessage()
    {
        // Act: glob that matches nothing
        var (exitCode, _, stderr) = RunCli(
            "batch", Path.Combine(_tempDir, "*.nope"));

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("No .bmfc or .hiero config files specified");
    }

    // -- benchmark-fonts --

    [Fact]
    public void BenchmarkFonts_NoFilter_RunsColdAndWarmPasses()
    {
        // Act
        var (exitCode, stdout, stderr) = RunCli("benchmark-fonts");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Benchmarking font resolution");
        stdout.ShouldContain("Cold pass:");
        stdout.ShouldContain("Warm pass");
    }

    [Fact]
    public void BenchmarkFonts_Filter_NarrowsResults()
    {
        // Arrange — first get the unfiltered family list/count via JSON so this test
        // doesn't hardcode a specific font name that may not exist on every machine.
        var (unfilteredExit, unfilteredStdout, unfilteredStderr) = RunCli("benchmark-fonts", "--json");
        unfilteredExit.ShouldBe(0, $"stderr: {unfilteredStderr}");

        using var unfilteredDoc = System.Text.Json.JsonDocument.Parse(unfilteredStdout);
        var totalFamilies = unfilteredDoc.RootElement.GetProperty("families").GetInt32();
        totalFamilies.ShouldBeGreaterThan(0, "a real machine should have at least one installed font family");

        var firstFamily = unfilteredDoc.RootElement
            .GetProperty("coldPass").GetProperty("results")[0]
            .GetProperty("family").GetString();

        // Act — filter down to just that one family name.
        var (exitCode, stdout, stderr) = RunCli("benchmark-fonts", "--json", "--filter", firstFamily!);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        using var filteredDoc = System.Text.Json.JsonDocument.Parse(stdout);
        var filteredFamilies = filteredDoc.RootElement.GetProperty("families").GetInt32();
        filteredFamilies.ShouldBeGreaterThan(0);
        filteredFamilies.ShouldBeLessThanOrEqualTo(totalFamilies);
    }

    [Fact]
    public void BenchmarkFonts_Json_ProducesParseableJsonWithExpectedShape()
    {
        // Act
        var (exitCode, stdout, stderr) = RunCli("benchmark-fonts", "--json");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        using var doc = System.Text.Json.JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        root.GetProperty("families").GetInt32().ShouldBeGreaterThan(0);

        foreach (var passName in new[] { "coldPass", "warmPass" })
        {
            var pass = root.GetProperty(passName);
            var results = pass.GetProperty("results");
            results.GetArrayLength().ShouldBeGreaterThan(0);

            var firstResult = results[0];
            firstResult.GetProperty("family").GetString().ShouldNotBeNullOrEmpty();
            firstResult.TryGetProperty("ms", out _).ShouldBeTrue();

            pass.TryGetProperty("min", out _).ShouldBeTrue();
            pass.TryGetProperty("mean", out _).ShouldBeTrue();
            pass.TryGetProperty("max", out _).ShouldBeTrue();
            pass.TryGetProperty("totalMs", out _).ShouldBeTrue();
        }
    }

    [Fact]
    public void BenchmarkFonts_Help_ShowsHelp_ExitCode0()
    {
        // Act
        var (exitCode, stdout, _) = RunCli("benchmark-fonts", "--help");

        // Assert
        exitCode.ShouldBe(0);
        stdout.ShouldContain("Usage: kernsmith benchmark-fonts");
        stdout.ShouldContain("--filter");
        stdout.ShouldContain("--json");
    }

    [Fact]
    public void BenchmarkFonts_FilterMatchesNothing_ExitsCleanlyWithoutThrowing()
    {
        // Arrange — a pattern guaranteed not to match any real installed font family.
        var noSuchFamily = "KernSmithZzzNoSuchFontFamily_" + Guid.NewGuid().ToString("N")[..8];

        // Act
        var (exitCode, stdout, stderr) = RunCli("benchmark-fonts", "--filter", noSuchFamily);

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("No font families");
    }

    // -- Helper methods --

    private static int ExtractIntAttribute(string fntContent, string attributeName)
    {
        // Matches patterns like "lineHeight=38" or "scaleW=256"
        var pattern = $"{attributeName}=";
        var idx = fntContent.IndexOf(pattern, StringComparison.Ordinal);
        idx.ShouldBeGreaterThanOrEqualTo(0, $"fnt content should contain '{pattern}'");

        var start = idx + pattern.Length;
        var end = start;
        while (end < fntContent.Length && char.IsDigit(fntContent[end]))
            end++;

        return int.Parse(fntContent[start..end]);
    }
}
