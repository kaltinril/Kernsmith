using System.Text;
using KernSmith.Output;
using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// Export As must write the <c>.fnt</c> in the descriptor format selected in the UI
/// (<see cref="AtlasConfigViewModel.DescriptorFormat"/>), not always as text (issue #320).
/// </summary>
public sealed class ExportFormatTests : IDisposable
{
    private static readonly string RobotoPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-export-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private async Task<byte[]> ExportAs(OutputFormat format)
    {
        var atlasConfig = new AtlasConfigViewModel { DescriptorFormat = format };
        var result = await new GenerationService().GenerateAsync(new GenerationRequest
        {
            FontData = File.ReadAllBytes(RobotoPath),
            SourceKind = FontSourceKind.File,
            FontSize = 32,
            Characters = CharacterSet.FromChars("MW"),
            Backend = RasterizerBackend.Native,
        });

        var path = Path.Combine(_dir, "exported.fnt");
        MainViewModel.ExportResult(result, path, atlasConfig).ShouldBe(Path.Combine(_dir, "exported"));
        return File.ReadAllBytes(path);
    }

    [Fact]
    public async Task ExportResult_TextSelected_WritesTextDescriptor()
    {
        var fnt = await ExportAs(OutputFormat.Text);

        Encoding.UTF8.GetString(fnt).ShouldStartWith("info ");
    }

    [Fact]
    public async Task ExportResult_XmlSelected_WritesXmlDescriptor()
    {
        var fnt = await ExportAs(OutputFormat.Xml);

        var text = Encoding.UTF8.GetString(fnt).TrimStart('﻿');
        text.ShouldStartWith("<?xml");
        text.ShouldContain("<font>");
    }

    [Fact]
    public async Task ExportResult_BinarySelected_WritesBinaryDescriptor()
    {
        var fnt = await ExportAs(OutputFormat.Binary);

        // BMFont binary header: "BMF" followed by format version 3.
        fnt.Take(4).ShouldBe(new byte[] { (byte)'B', (byte)'M', (byte)'F', 3 });
    }
}
