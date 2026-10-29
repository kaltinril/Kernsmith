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

        // The CLI is built in the same configuration as this test assembly, so derive it from
        // our own output path. Hardcoding "Debug" made every test here fail under
        // `dotnet test --configuration Release`, which is exactly what publish.yml runs.
        var configuration = Path.GetFileName(Path.GetDirectoryName(baseDir)!);

        CliDllPath = Path.Combine(repoRoot, "tools", "KernSmith.Cli", "bin", configuration, tfm, "KernSmith.Cli.dll");
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
        // Without this the whole suite fails with an opaque "dotnet exec" error and no clue
        // that the CLI simply was not built for this configuration/TFM.
        if (!File.Exists(CliDllPath))
            throw new FileNotFoundException(
                $"The CLI was not built at '{CliDllPath}'. Build the solution for this " +
                "configuration first; these tests run the real CLI as a child process.",
                CliDllPath);

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

    [Fact]
    public void Generate_BackendRejectsFont_PrintsOneLineErrorWithBackendName()
    {
        // A CBDT-only color font has no outlines for Native to compute a distance field from, so
        // an SDF request without color is refused. That is an expected user error, not a crash.
        var emojiPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoColorEmoji.ttf");

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", emojiPath, "-s", "32", "--rasterizer", "native", "--sdf",
            "-o", Path.Combine(_tempDir, "emoji"), "--no-color");

        // Assert
        exitCode.ShouldBe(4, $"stderr: {stderr}");
        stderr.ShouldNotContain("Unhandled exception", Case.Sensitive);
        stderr.Trim().ShouldNotContain('\n');
        stderr.ShouldContain("native", Case.Sensitive);
        stderr.ShouldContain("color bitmaps", Case.Sensitive);
    }

    [Fact]
    public void Generate_NativeColorBitmapFont_WritesAColorAtlas()
    {
        var emojiPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoColorEmoji.ttf");
        var output = Path.Combine(_tempDir, "emoji-color");

        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", emojiPath, "-s", "32", "--rasterizer", "native", "--color-font",
            "--chars", "\U0001F600❤", "-o", output, "--no-color");

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.Exists(output + ".fnt").ShouldBeTrue();
        File.Exists(output + "_0.png").ShouldBeTrue();
    }

    [Theory]
    [InlineData("native", "99")]
    [InlineData("native", "-1")]
    [InlineData("freetype", "99")]
    public void Generate_ColorPaletteTheFontLacks_IsAGenerationError(string rasterizer, string palette)
    {
        // Whether a palette index exists depends on the font's CPAL table, so a bad one is the
        // backend rejecting an option for this font -- exit 4 like every other such rejection.
        var colorFont = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Color", "BungeeColor-Regular.ttf");

        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", colorFont, "-s", "32", "--rasterizer", rasterizer, "--color-font",
            "--color-palette", palette, "-o", Path.Combine(_tempDir, "palette"), "--no-color");

        exitCode.ShouldBe(4, $"stderr: {stderr}");
        stderr.ShouldNotContain("Unhandled exception", Case.Sensitive);
        stderr.ShouldContain($"Generation error ({rasterizer} rasterizer)", Case.Sensitive);
        stderr.ShouldContain("palette", Case.Sensitive);
    }

    [Fact]
    public void GenerateHelp_ColorFont_NamesEveryColorFormat()
    {
        var (exitCode, stdout, _) = RunCli("generate", "--help");

        exitCode.ShouldBe(0);
        stdout.ShouldContain("COLR/CPAL layers, CBDT/sbix bitmaps", Case.Sensitive);
        stdout.ShouldNotContain("(COLR/CPAL)", Case.Sensitive);
    }

    [Fact]
    public void Generate_BackendDeclinesOption_PrintsOneLineErrorWithBackendName()
    {
        // StbTrueType throws NotSupportedException for a color-font request.

        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "--rasterizer", "stbtruetype", "--color-font",
            "-o", Path.Combine(_tempDir, "stbcolor"), "--no-color");

        // Assert
        exitCode.ShouldBe(4, $"stderr: {stderr}");
        stderr.ShouldNotContain("Unhandled exception", Case.Sensitive);
        stderr.Trim().ShouldNotContain('\n');
        stderr.ShouldContain("stbtruetype", Case.Sensitive);
        stderr.ShouldContain("does not support color font rendering", Case.Sensitive);
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

    // -- WOFF2 input --

    private static readonly string Woff2FontPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.woff2");

    [Fact]
    public void Generate_Woff2Font_MatchesTtfOutput()
    {
        var ttfDir = Directory.CreateDirectory(Path.Combine(_tempDir, "ttf")).FullName;
        var woff2Dir = Directory.CreateDirectory(Path.Combine(_tempDir, "woff2")).FullName;

        var ttf = RunCli("generate", "-f", FontPath, "-s", "24", "-o", Path.Combine(ttfDir, "out"));
        var woff2 = RunCli("generate", "-f", Woff2FontPath, "-s", "24", "-o", Path.Combine(woff2Dir, "out"));

        ttf.ExitCode.ShouldBe(0, $"stderr: {ttf.StdErr}");
        woff2.ExitCode.ShouldBe(0, $"stderr: {woff2.StdErr}");
        File.ReadAllText(Path.Combine(woff2Dir, "out.fnt"))
            .ShouldBe(File.ReadAllText(Path.Combine(ttfDir, "out.fnt")));
        File.ReadAllBytes(Path.Combine(woff2Dir, "out_0.png"))
            .ShouldBe(File.ReadAllBytes(Path.Combine(ttfDir, "out_0.png")));
    }

    [Fact]
    public void Info_Woff2Font_ReportsWoff2Format()
    {
        var (exitCode, stdout, stderr) = RunCli("info", Woff2FontPath);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("WOFF2", Case.Sensitive);
        stdout.ShouldContain("Roboto", Case.Sensitive);
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

    // -- Distance-field mode --

    [Theory]
    [InlineData("msdf", "alphaChnl=4")]
    [InlineData("mtsdf", "alphaChnl=0")]
    public void Generate_SdfModeOnNative_WritesTheMultiChannelFont(string mode, string alphaChannel)
    {
        var outputBase = Path.Combine(_tempDir, mode);

        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "--chars", "AB", "-o", outputBase,
            "--rasterizer", "native", "--sdf-mode", mode);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.ShouldContain(alphaChannel, Case.Sensitive);
        fnt.ShouldContain($"sdfMode={mode}", Case.Sensitive);
    }

    [Fact]
    public void Generate_SdfModeMsdf_OnABackendWithoutMsdf_Fails()
    {
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "--chars", "AB", "-o", Path.Combine(_tempDir, "ft"),
            "--sdf-mode", "msdf");

        exitCode.ShouldNotBe(0);
        (stdout + stderr).ShouldContain("MSDF", Case.Sensitive);
    }

    [Fact]
    public void Generate_UnknownSdfMode_IsRejected()
    {
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath, "-s", "32", "--sdf-mode", "bogus");

        exitCode.ShouldNotBe(0);
        stderr.ShouldContain("--sdf-mode", Case.Sensitive);
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

    // -- Rasterizer option --

    [Fact]
    public void ListRasterizers_IncludesNativeBackend()
    {
        // Act
        var (exitCode, stdout, stderr) = RunCli("list-rasterizers");

        // Assert -- the CLI references KernSmith.Rasterizers.Native, so auto-discovery
        // must find it and report it as available rather than "(not available)".
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("native");
        stdout.ShouldNotContain("native          (not available)");
    }

    [Fact]
    public void ListRasterizers_NamesTheVectorOutlineCapability()
    {
        // SupportsOutlineStroke means the backend draws OutlineMode.Vector outlines itself; every
        // backend gets bitmap outlines from core, so a bare "Outline" label would mislead.
        var (exitCode, stdout, stderr) = RunCli("list-rasterizers");

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        var lines = stdout.Split('\n').Select(line => line.TrimEnd()).ToArray();
        lines.Single(line => line.TrimStart().StartsWith("native ", StringComparison.Ordinal))
            .ShouldContain("Vector Outline", Case.Sensitive);
        lines.ShouldAllBe(line => !line.Contains(", Outline,") && !line.EndsWith(", Outline"));
    }

    [Fact]
    public void Generate_NativeRasterizer_ProducesFntAndPng()
    {
        // Arrange
        var outputBase = Path.Combine(_tempDir, "native-out");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-o", outputBase,
            "--rasterizer", "native");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");
        File.Exists(outputBase + ".fnt").ShouldBeTrue();

        var pngPath = Path.Combine(_tempDir, "native-out_0.png");
        File.Exists(pngPath).ShouldBeTrue();
    }

    [Theory]
    [InlineData("SourceSerif4-Regular.otf")]
    [InlineData("SourceCodeVF-Upright.otf")]
    public void Generate_NativeRasterizer_FromOtf_ProducesInkedGlyphs(string fixture)
    {
        // PostScript-outline (CFF / CFF2) fonts through the real CLI. The .fnt must describe real
        // glyph cells — a backend that loaded the font but drew nothing would still exit 0.
        var outputBase = Path.Combine(_tempDir, "native-otf");
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);

        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", fontPath, "-s", "32", "-o", outputBase, "--rasterizer", "native");

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");

        var model = KernSmith.Output.BmFontReader.ReadText(File.ReadAllText(outputBase + ".fnt"));
        var upperA = model.Characters.First(c => c.Id == 'A');
        upperA.Width.ShouldBeGreaterThan(10);
        upperA.Height.ShouldBeGreaterThan(10);
        File.Exists(Path.Combine(_tempDir, "native-otf_0.png")).ShouldBeTrue();
    }

    // -- Named variation instances --

    private static readonly string RobotoFlexPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "RobotoFlex-Variable.ttf");

    private string RenderRobotoFlexHash(string name, params string[] extra)
    {
        var outputBase = Path.Combine(_tempDir, name);
        var (exitCode, _, stderr) = RunCli(
            ["generate", "-f", RobotoFlexPath, "-s", "32", "--chars", "AGQ", "-o", outputBase,
             "--rasterizer", "native", .. extra]);
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(Path.Combine(_tempDir, name + "_0.png"))));
    }

    [Fact]
    public void Generate_NamedInstance_RendersThatInstance()
    {
        // Roboto Flex's "Black" instance is wght=900 with every other axis at its default, so it
        // must render exactly like --axis wght=900, and differently from the default instance.
        var instance = RenderRobotoFlexHash("instance", "--instance", "Black");
        var axis = RenderRobotoFlexHash("axis", "--axis", "wght=900");
        var regular = RenderRobotoFlexHash("regular");

        instance.ShouldBe(axis);
        instance.ShouldNotBe(regular);
    }

    [Fact]
    public void Generate_NamedInstance_ExplicitAxisWins()
    {
        RenderRobotoFlexHash("black-thin", "--instance", "Black", "--axis", "wght=100")
            .ShouldBe(RenderRobotoFlexHash("thin", "--instance", "Thin"));
    }

    [Fact]
    public void Generate_UnknownNamedInstance_FailsListingTheNames()
    {
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", RobotoFlexPath, "-s", "32", "--chars", "A", "-o", Path.Combine(_tempDir, "x"),
            "--rasterizer", "native", "--instance", "Bogus");

        exitCode.ShouldBe(1);
        stderr.ShouldContain("Bogus", Case.Sensitive);
        stderr.ShouldContain("Black", Case.Sensitive);
    }

    [Fact]
    public void Generate_UnknownRasterizer_ListsNativeAmongValidValues()
    {
        // Act
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "--rasterizer", "bogus");

        // Assert
        exitCode.ShouldBe(1);
        stderr.ShouldContain("Unknown rasterizer backend: 'bogus'");
        stderr.ShouldContain("native");
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

    // -- --outline-shape --

    [Fact]
    public void Generate_InvalidOutlineShape_ReturnsError()
    {
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "--outline", "2", "--outline-shape", "hexagon");

        exitCode.ShouldBe(1);
        stderr.ShouldContain("Unknown outline shape: hexagon");
    }

    [Theory]
    [InlineData("square")]
    [InlineData("plus")]
    public void Init_OutlineShape_WritesOutlineShapeToBmfc(string shape)
    {
        var configPath = Path.Combine(_tempDir, "shape.bmfc");

        var (exitCode, _, stderr) = RunCli(
            "init", "-f", FontPath, "-s", "24", "-c", "A", "--outline", "2", "--outline-shape", shape, "-o", configPath);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.ReadAllText(configPath).ShouldContain($"outlineShape={shape}", Case.Sensitive);
    }

    [Fact]
    public void Generate_OutlineShapeFlag_ReachesGeneratorAndSavedConfig()
    {
        var savedConfig = Path.Combine(_tempDir, "saved.bmfc");

        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-c", "A", "--outline", "2", "--outline-shape", "plus",
            "-o", Path.Combine(_tempDir, "out"), "--save-config", savedConfig);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.ReadAllText(savedConfig).ShouldContain("outlineShape=plus", Case.Sensitive);
    }

    [Fact]
    public void Generate_OutlineShapeFlag_ChangesOutputPixels()
    {
        var roundOut = Path.Combine(_tempDir, "round");
        var squareOut = Path.Combine(_tempDir, "square");

        RunCli("generate", "-f", FontPath, "-s", "24", "-c", "A", "--mono", "--outline", "3",
            "-o", roundOut).ExitCode.ShouldBe(0);
        RunCli("generate", "-f", FontPath, "-s", "24", "-c", "A", "--mono", "--outline", "3",
            "--outline-shape", "square", "-o", squareOut).ExitCode.ShouldBe(0);

        File.ReadAllBytes(squareOut + "_0.png").ShouldNotBe(File.ReadAllBytes(roundOut + "_0.png"));
    }

    [Fact]
    public void Generate_ConfigOutlineShape_KeptWhenFlagAbsent_OverriddenWhenFlagGiven()
    {
        var configPath = Path.Combine(_tempDir, "base.bmfc");
        RunCli("init", "-f", FontPath, "-s", "24", "-c", "A", "--outline", "2", "--outline-shape", "square",
            "-o", configPath).ExitCode.ShouldBe(0);

        var keptConfig = Path.Combine(_tempDir, "kept.bmfc");
        var (keptExit, _, keptErr) = RunCli(
            "generate", "--config", configPath, "-f", FontPath, "-o", Path.Combine(_tempDir, "kept"), "--save-config", keptConfig);
        keptExit.ShouldBe(0, $"stderr: {keptErr}");
        File.ReadAllText(keptConfig).ShouldContain("outlineShape=square", Case.Sensitive);

        var plusConfig = Path.Combine(_tempDir, "plus.bmfc");
        var (plusExit, _, plusErr) = RunCli(
            "generate", "--config", configPath, "-f", FontPath, "--outline-shape", "plus",
            "-o", Path.Combine(_tempDir, "plus"), "--save-config", plusConfig);
        plusExit.ShouldBe(0, $"stderr: {plusErr}");
        File.ReadAllText(plusConfig).ShouldContain("outlineShape=plus", Case.Sensitive);

        var roundConfig = Path.Combine(_tempDir, "round.bmfc");
        var (roundExit, _, roundErr) = RunCli(
            "generate", "--config", configPath, "-f", FontPath, "--outline-shape", "round",
            "-o", Path.Combine(_tempDir, "round"), "--save-config", roundConfig);
        roundExit.ShouldBe(0, $"stderr: {roundErr}");
        File.ReadAllText(roundConfig).ShouldNotContain("outlineShape");
    }

    // -- LCD options --

    [Fact]
    public void Generate_LcdFlags_ReachTheSavedConfig()
    {
        var savedConfig = Path.Combine(_tempDir, "lcd.bmfc");

        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "-c", "AB", "--rasterizer", "native", "--aa", "lcd",
            "--lcd-filter", "smooth", "--lcd-order", "vbgr", "-o", Path.Combine(_tempDir, "lcd"), "--save-config", savedConfig);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        var saved = File.ReadAllText(savedConfig);
        saved.ShouldContain("lcdFilter=smooth", Case.Sensitive);
        saved.ShouldContain("lcdOrder=vbgr", Case.Sensitive);
    }

    [Fact]
    public void Generate_ConfigLcdKeys_KeptWhenFlagsAbsent_OverriddenWhenFlagsGiven()
    {
        var configPath = Path.Combine(_tempDir, "lcd-base.bmfc");
        RunCli("init", "-f", FontPath, "-s", "24", "-c", "A", "--aa", "lcd", "--lcd-filter", "none", "--lcd-order", "bgr",
            "-o", configPath).ExitCode.ShouldBe(0);
        var initial = File.ReadAllText(configPath);
        initial.ShouldContain("lcdFilter=none", Case.Sensitive);
        initial.ShouldContain("lcdOrder=bgr", Case.Sensitive);

        var keptConfig = Path.Combine(_tempDir, "lcd-kept.bmfc");
        var (keptExit, _, keptErr) = RunCli(
            "generate", "--config", configPath, "-f", FontPath, "-o", Path.Combine(_tempDir, "lcd-kept"), "--save-config", keptConfig);
        keptExit.ShouldBe(0, $"stderr: {keptErr}");
        var kept = File.ReadAllText(keptConfig);
        kept.ShouldContain("lcdFilter=none", Case.Sensitive);
        kept.ShouldContain("lcdOrder=bgr", Case.Sensitive);

        var defaultConfig = Path.Combine(_tempDir, "lcd-default.bmfc");
        var (defaultExit, _, defaultErr) = RunCli(
            "generate", "--config", configPath, "-f", FontPath, "--lcd-filter", "default", "--lcd-order", "rgb",
            "-o", Path.Combine(_tempDir, "lcd-default"), "--save-config", defaultConfig);
        defaultExit.ShouldBe(0, $"stderr: {defaultErr}");
        var overridden = File.ReadAllText(defaultConfig);
        overridden.ShouldNotContain("lcdFilter", Case.Sensitive);
        overridden.ShouldNotContain("lcdOrder", Case.Sensitive);
    }

    [Theory]
    [InlineData("--lcd-filter")]
    [InlineData("--lcd-order")]
    public void Generate_UnknownLcdOptionValue_IsRejected(string flag)
    {
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath, "-s", "32", flag, "bogus");

        exitCode.ShouldNotBe(0);
        stderr.ShouldContain($"Unknown {flag}: bogus", Case.Sensitive);
    }

    // -- --outline-mode, --width-percent, --italic-angle (Phase 168) --

    [Fact]
    public void Generate_InvalidOutlineMode_ReturnsError()
    {
        var (exitCode, _, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "24", "--outline", "2", "--outline-mode", "hologram");

        exitCode.ShouldBe(1);
        stderr.ShouldContain("Unknown outline mode: hologram", Case.Sensitive);
    }

    [Fact]
    public void Generate_OutlineModeVector_OnNative_ChangesPixelsAndIsSaved()
    {
        var effectOut = Path.Combine(_tempDir, "effect");
        var vectorOut = Path.Combine(_tempDir, "vector");
        var savedConfig = Path.Combine(_tempDir, "vector.bmfc");

        RunCli("generate", "-f", FontPath, "-s", "24", "-c", "@", "--outline", "2", "--rasterizer", "native",
            "-o", effectOut).ExitCode.ShouldBe(0);
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath, "-s", "24", "-c", "@", "--outline", "2",
            "--rasterizer", "native", "--outline-mode", "vector", "-o", vectorOut, "--save-config", savedConfig);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        File.ReadAllBytes(vectorOut + "_0.png").ShouldNotBe(File.ReadAllBytes(effectOut + "_0.png"));
        File.ReadAllText(savedConfig).ShouldContain("outlineMode=vector", Case.Sensitive);
    }

    [Fact]
    public void Generate_WidthPercentAndItalicAngle_OnNative_AreSavedAndKeptFromConfig()
    {
        var savedConfig = Path.Combine(_tempDir, "shaped.bmfc");
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath, "-s", "24", "-c", "Al", "--italic",
            "--rasterizer", "native", "--width-percent", "80", "--italic-angle", "20",
            "-o", Path.Combine(_tempDir, "shaped"), "--save-config", savedConfig);

        exitCode.ShouldBe(0, $"stderr: {stderr}");
        var text = File.ReadAllText(savedConfig);
        text.ShouldContain("widthPercent=80", Case.Sensitive);
        text.ShouldContain("italicAngle=20", Case.Sensitive);

        var resaved = Path.Combine(_tempDir, "resaved.bmfc");
        var (keptExit, _, keptErr) = RunCli("generate", "--config", savedConfig, "-f", FontPath,
            "-o", Path.Combine(_tempDir, "kept"), "--save-config", resaved);
        keptExit.ShouldBe(0, $"stderr: {keptErr}");
        File.ReadAllText(resaved).ShouldContain("widthPercent=80", Case.Sensitive);
        File.ReadAllText(resaved).ShouldContain("italicAngle=20", Case.Sensitive);
    }

    [Fact]
    public void Generate_WidthPercent_OnABackendWithoutIt_FailsWithTheReason()
    {
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath, "-s", "24", "-c", "A",
            "--rasterizer", "freetype", "--width-percent", "80", "-o", Path.Combine(_tempDir, "ft"));

        exitCode.ShouldNotBe(0);
        stderr.ShouldContain("WidthPercent = 80", Case.Sensitive);
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

    // -- Shadow variant (phase-182, issue #175) --

    [Fact]
    public void Generate_ShadowVariant_WritesPrimaryAndShadowFntSharingOnePng()
    {
        // Arrange
        var outputBase = Path.Join(_tempDir, "shadow-test");

        // Act
        var (exitCode, stdout, stderr) = RunCli(
            "generate", "-f", FontPath, "-s", "32", "-o", outputBase, "--shadow-variant");

        // Assert
        exitCode.ShouldBe(0, $"stderr: {stderr}");
        stdout.ShouldContain("Done.");

        File.Exists(outputBase + ".fnt").ShouldBeTrue("should produce the primary .fnt file");
        File.Exists(outputBase + "-shadow.fnt").ShouldBeTrue("should produce the shadow variant's .fnt file");

        // Both .fnt files must reference the same shared PNG -- no duplicate image written.
        var pngFiles = Directory.GetFiles(_tempDir, "*.png");
        pngFiles.Length.ShouldBe(1, "primary and shadow variant should share one PNG, not duplicate it");

        var primaryFnt = File.ReadAllText(outputBase + ".fnt");
        var shadowFnt = File.ReadAllText(outputBase + "-shadow.fnt");
        var primaryPageLine = primaryFnt.Split('\n').Single(l => l.StartsWith("page "));
        var shadowPageLine = shadowFnt.Split('\n').Single(l => l.StartsWith("page "));
        shadowPageLine.ShouldBe(primaryPageLine);
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
