using KernSmith.Ui.Models;
using KernSmith.Ui.Services;
using Shouldly;

namespace KernSmith.Ui.Tests;

public sealed class GenerationErrorTests
{
    private static readonly string RobotoPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    [Theory]
    [InlineData(PackingAlgorithm.MaxRects)]
    [InlineData(PackingAlgorithm.Skyline)]
    public async Task GenerateAsync_GlyphLargerThanThePage_ThrowsTheTypeTheTooLargeDialogCatches(PackingAlgorithm packer)
    {
        // MainViewModel.GenerateAsync shows its "too large" dialog only for AtlasPackingException.
        var request = new GenerationRequest
        {
            FontData = File.ReadAllBytes(RobotoPath),
            SourceKind = FontSourceKind.File,
            FontSize = 64,
            Characters = CharacterSet.FromChars("W"),
            MaxWidth = 16,
            MaxHeight = 16,
            AutofitTexture = false,
            PackingAlgorithmIndex = (int)packer,
        };

        await Should.ThrowAsync<AtlasPackingException>(() => new GenerationService().GenerateAsync(request));
    }
}
