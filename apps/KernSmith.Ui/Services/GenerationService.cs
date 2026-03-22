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
                .WithCharacters(request.Characters)
                .WithMaxTextureSize(request.MaxWidth, request.MaxHeight)
                .WithPowerOfTwo(request.PowerOfTwo)
                .WithAutofitTexture(request.AutofitTexture)
                .WithPadding(request.PaddingUp, request.PaddingRight, request.PaddingDown, request.PaddingLeft)
                .WithSpacing(request.SpacingH, request.SpacingV)
                .WithKerning(request.IncludeKerning)
                .WithBold(request.Bold)
                .WithItalic(request.Italic)
                .WithAntiAlias(request.AntiAlias ? AntiAliasMode.Grayscale : AntiAliasMode.None)
                .WithHinting(request.Hinting)
                .WithSuperSampling(request.SuperSampleLevel)
                .WithSdf(request.SdfEnabled)
                .WithColorFont(request.ColorFontEnabled);

            if (request.OutlineEnabled)
                builder.WithOutline(request.OutlineWidth, request.OutlineColorR, request.OutlineColorG, request.OutlineColorB);

            if (request.ShadowEnabled)
                builder.WithShadow(request.ShadowOffsetX, request.ShadowOffsetY, request.ShadowBlur,
                    (request.ShadowColorR, request.ShadowColorG, request.ShadowColorB),
                    request.ShadowOpacity / 100f);

            if (request.GradientEnabled)
                builder.WithGradient(
                    (request.GradientStartR, request.GradientStartG, request.GradientStartB),
                    (request.GradientEndR, request.GradientEndG, request.GradientEndB),
                    request.GradientAngle);

            if (request.ChannelPackingEnabled)
                builder.WithChannelPacking();

            builder.WithPackingAlgorithm((PackingAlgorithm)request.PackingAlgorithmIndex);

            if (request.FaceIndex > 0)
                builder.WithFaceIndex(request.FaceIndex);

            if (request.VariationAxisValues is { Count: > 0 })
            {
                foreach (var (tag, value) in request.VariationAxisValues)
                    builder.WithVariationAxis(tag, value);
            }

            if (!string.IsNullOrEmpty(request.FallbackCharacter))
                builder.WithFallbackCharacter(request.FallbackCharacter[0]);

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
