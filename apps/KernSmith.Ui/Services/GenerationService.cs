using KernSmith.Ui.Models;
using KernSmith.Output;

namespace KernSmith.Ui.Services;

public class GenerationService
{
    public async Task<BmFontResult> GenerateAsync(GenerationRequest request)
    {
        return await Task.Run(() =>
        {
            var builder = BmFont.Builder()
                .WithSize(request.FontSize)
                .WithCharacters(request.Characters);

            switch (request.SourceKind)
            {
                case FontSourceKind.File when request.FontFilePath != null:
                    builder.WithFont(request.FontFilePath);
                    break;
                case FontSourceKind.System when request.SystemFontFamily != null:
                    builder.WithSystemFont(request.SystemFontFamily);
                    break;
                default:
                    builder.WithFont(request.FontData!);
                    break;
            }

            return builder.Build();
        });
    }
}
