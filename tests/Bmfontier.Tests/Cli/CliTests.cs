using System.Diagnostics;
using FluentAssertions;

namespace Bmfontier.Tests.Cli;

public class CliTests : IDisposable
{
    private static readonly string ProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "Bmfontier.Cli", "Bmfontier.Cli.csproj"));

    private static readonly string FontPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    private readonly string _tempDir;

    public CliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bmfontier-cli-tests-" + Guid.NewGuid().ToString("N")[..8]);
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

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(ProjectPath);
        psi.ArgumentList.Add("--");

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(60));

        return (process.ExitCode, stdout, stderr);
    }

    // -- Help and usage --

    [Fact]
    public void NoArguments_ShowsHelp_ExitCode0()
    {
        // Act
        var (exitCode, stdout, _) = RunCli();

        // Assert
        exitCode.Should().Be(0);
        stdout.Should().Contain("Usage:");
        stdout.Should().Contain("generate");
        stdout.Should().Contain("list-fonts");
    }

    [Fact]
    public void HelpFlag_ShowsHelp_ExitCode0()
    {
        // Act
        var (exitCode, stdout, _) = RunCli("--help");

        // Assert
        exitCode.Should().Be(0);
        stdout.Should().Contain("Usage:");
        stdout.Should().Contain("--font");
        stdout.Should().Contain("--size");
        stdout.Should().Contain("--format");
    }

    // -- Error handling --

    [Fact]
    public void UnknownCommand_ReturnsExitCode1()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("bogus");

        // Assert
        exitCode.Should().Be(1);
        stderr.Should().Contain("Unknown command: bogus");
    }

    [Fact]
    public void Generate_MissingFont_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "-s", "32");

        // Assert
        exitCode.Should().Be(1);
        stderr.Should().Contain("--font is required");
    }

    [Fact]
    public void Generate_MissingSize_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "-f", FontPath);

        // Assert
        exitCode.Should().Be(1);
        stderr.Should().Contain("--size is required");
    }

    [Fact]
    public void Generate_NonexistentFontFile_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "-f", "/no/such/font.ttf", "-s", "32");

        // Assert
        exitCode.Should().Be(1);
        stderr.Should().Contain("Font file not found");
    }

    [Fact]
    public void Generate_UnknownOption_ReturnsError()
    {
        // Act
        var (exitCode, _, stderr) = RunCli("generate", "--bogus-flag");

        // Assert
        exitCode.Should().Be(1);
        stderr.Should().Contain("Unknown option: --bogus-flag");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");
        stdout.Should().Contain("Done.");

        File.Exists(outputBase + ".fnt").Should().BeTrue("should produce a .fnt file");

        // PNG pages are named {outputBaseName}_{pageIndex}.png in the output directory
        var pngPath = Path.Combine(_tempDir, "test-output_0.png");
        File.Exists(pngPath).Should().BeTrue("should produce at least one .png page");

        var fntContent = File.ReadAllText(outputBase + ".fnt");
        fntContent.Should().Contain("info face=\"Roboto\"");
        fntContent.Should().Contain("common ");
        fntContent.Should().Contain("char id=");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.Should().StartWith("info ");
        fnt.Should().Contain("page id=");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.Should().Contain("<font>");
        fnt.Should().Contain("face=\"Roboto\"");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var bytes = File.ReadAllBytes(outputBase + ".fnt");
        // BMF header + version 3
        bytes[0].Should().Be(66, "first byte should be 'B'");
        bytes[1].Should().Be(77, "second byte should be 'M'");
        bytes[2].Should().Be(70, "third byte should be 'F'");
        bytes[3].Should().Be(3, "fourth byte should be version 3");
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
        exitCode1.Should().Be(0, $"stderr: {stderr1}");
        exitCode2.Should().Be(0, $"stderr: {stderr2}");

        var smallFnt = File.ReadAllText(smallOutput + ".fnt");
        var largeFnt = File.ReadAllText(largeOutput + ".fnt");

        // Extract lineHeight from "common lineHeight=<N>"
        var smallLineHeight = ExtractIntAttribute(smallFnt, "lineHeight");
        var largeLineHeight = ExtractIntAttribute(largeFnt, "lineHeight");

        largeLineHeight.Should().BeGreaterThan(smallLineHeight,
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.Should().Contain("chars count=3");
        fnt.Should().Contain("char id=65 ");  // A
        fnt.Should().Contain("char id=66 ");  // B
        fnt.Should().Contain("char id=67 ");  // C
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        // The info block should reflect the padding values
        fnt.Should().Contain("padding=4,4,4,4");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        fnt.Should().Contain("spacing=3,3");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");
        stdout.Should().Contain("Done.");
        File.Exists(outputBase + ".fnt").Should().BeTrue();

        var pngPath = Path.Combine(_tempDir, "skyline-out_0.png");
        File.Exists(pngPath).Should().BeTrue();
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
        exitCode.Should().Be(0, $"stderr: {stderr}");
        stdout.Should().Contain("Done.");
        File.Exists(outputBase + ".fnt").Should().BeTrue();

        var fnt = File.ReadAllText(outputBase + ".fnt");
        // "Hello" has 4 unique chars: H, e, l, o
        fnt.Should().Contain("chars count=4");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var fnt = File.ReadAllText(outputBase + ".fnt");
        var scaleW = ExtractIntAttribute(fnt, "scaleW");
        var scaleH = ExtractIntAttribute(fnt, "scaleH");

        scaleW.Should().BeLessThanOrEqualTo(256);
        scaleH.Should().BeLessThanOrEqualTo(256);
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
        exitCode.Should().Be(0, $"stderr: {stderr}");
        stdout.Should().Contain("Loading font:");
        stdout.Should().Contain("Rasterizing");
        stdout.Should().Contain("Packing into atlas");
        stdout.Should().Contain("Writing output to");
        stdout.Should().Contain("Done.");
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
        exitCode.Should().Be(0, $"stderr: {stderr}");

        var pngPath = Path.Combine(_tempDir, "png-check_0.png");
        var pngBytes = File.ReadAllBytes(pngPath);
        pngBytes.Length.Should().BeGreaterThan(8);
        // PNG magic bytes
        pngBytes[0].Should().Be(137);
        pngBytes[1].Should().Be(80);  // P
        pngBytes[2].Should().Be(78);  // N
        pngBytes[3].Should().Be(71);  // G
    }

    // -- Helper methods --

    private static int ExtractIntAttribute(string fntContent, string attributeName)
    {
        // Matches patterns like "lineHeight=38" or "scaleW=256"
        var pattern = $"{attributeName}=";
        var idx = fntContent.IndexOf(pattern, StringComparison.Ordinal);
        idx.Should().BeGreaterThanOrEqualTo(0, $"fnt content should contain '{pattern}'");

        var start = idx + pattern.Length;
        var end = start;
        while (end < fntContent.Length && char.IsDigit(fntContent[end]))
            end++;

        return int.Parse(fntContent[start..end]);
    }
}
