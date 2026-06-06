using KernSmith.Ui.Models;
using KernSmith.Output;

namespace KernSmith.Ui.Services;

/// <summary>
/// Translates a <see cref="GenerationRequest"/> into a <see cref="BmFont.Builder"/> call chain
/// and runs bitmap font generation on a background thread.
/// </summary>
public class GenerationService
{
    /// <summary>
    /// Builds and executes a bitmap font generation from the given request on a background thread.
    /// </summary>
    public async Task<BmFontResult> GenerateAsync(GenerationRequest request)
    {
        return await Task.Run(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[GEN] Source={request.SourceKind} Path={request.FontFilePath} Bold={request.Bold} ForceSynBold={request.ForceSyntheticBold} Italic={request.Italic} ForceSynItalic={request.ForceSyntheticItalic}");

            // Build options directly (rather than via the fluent builder) so that the Phase 100
            // options without dedicated builder methods (FillColor, AdvanceAdjustX, Gamma, SdfSpread,
            // gradient Offset/Scale/Cyclic, shadow BlurKernelSize/BlurPasses) can be threaded through.
            // Each assignment mirrors the previous BmFontBuilder call to preserve identical output.
            var options = new FontGeneratorOptions
            {
                Size = request.FontSize,
                Characters = request.Characters,
                MaxTextureWidth = request.MaxWidth,
                MaxTextureHeight = request.MaxHeight,
                PowerOfTwo = request.PowerOfTwo,
                AutofitTexture = request.AutofitTexture,
                Padding = new Padding(request.PaddingUp, request.PaddingRight, request.PaddingDown, request.PaddingLeft),
                Spacing = new Spacing(request.SpacingH, request.SpacingV),
                Kerning = request.IncludeKerning,
                Bold = request.Bold,
                Italic = request.Italic,
                AntiAlias = request.AntiAlias ? AntiAliasMode.Grayscale : AntiAliasMode.None,
                EnableHinting = request.Hinting,
                SuperSampleLevel = request.SuperSampleLevel,
                Sdf = request.SdfEnabled,
                SdfSpread = request.SdfSpread,
                ColorFont = request.ColorFontEnabled,
                Backend = request.Backend,
                PackingAlgorithm = (PackingAlgorithm)request.PackingAlgorithmIndex,
                // Phase 100: base fill color, advance adjust, and gamma (defaults are no-ops).
                FillColorR = request.FillColorR,
                FillColorG = request.FillColorG,
                FillColorB = request.FillColorB,
                FillColorA = request.FillColorA,
                AdvanceAdjustX = request.AdvanceAdjustX,
                Gamma = request.Gamma,
            };

            if (request.OutlineEnabled)
            {
                options.Outline = request.OutlineWidth;
                options.OutlineR = request.OutlineColorR;
                options.OutlineG = request.OutlineColorG;
                options.OutlineB = request.OutlineColorB;
            }

            if (request.ShadowEnabled)
            {
                options.ShadowOffsetX = request.ShadowOffsetX;
                options.ShadowOffsetY = request.ShadowOffsetY;
                options.ShadowBlur = request.ShadowBlur;
                options.ShadowBlurKernelSize = request.ShadowBlurKernelSize;
                options.ShadowBlurPasses = request.ShadowBlurPasses;
                options.ShadowR = request.ShadowColorR;
                options.ShadowG = request.ShadowColorG;
                options.ShadowB = request.ShadowColorB;
                options.ShadowOpacity = request.ShadowOpacity / 100f;
                options.HardShadow = request.HardShadow;
            }

            if (request.GradientEnabled)
            {
                options.GradientStartR = request.GradientStartR;
                options.GradientStartG = request.GradientStartG;
                options.GradientStartB = request.GradientStartB;
                options.GradientEndR = request.GradientEndR;
                options.GradientEndG = request.GradientEndG;
                options.GradientEndB = request.GradientEndB;
                options.GradientAngle = request.GradientAngle;
                options.GradientOffset = request.GradientOffset;
                options.GradientScale = request.GradientScale;
                options.GradientCyclic = request.GradientCyclic;
            }

            if (request.ChannelPackingEnabled)
                options.ChannelPacking = true;

            if (request.ForceSyntheticBold)
            {
                options.Bold = true;
                options.ForceSyntheticBold = true;
            }
            if (request.ForceSyntheticItalic)
            {
                options.Italic = true;
                options.ForceSyntheticItalic = true;
            }

            if (request.FaceIndex > 0)
                options.FaceIndex = request.FaceIndex;

            if (request.VariationAxisValues is { Count: > 0 })
            {
                options.VariationAxes = new Dictionary<string, float>(request.VariationAxisValues);
            }

            if (!string.IsNullOrEmpty(request.FallbackCharacter))
                options.FallbackCharacter = request.FallbackCharacter[0];

            return request.SourceKind switch
            {
                FontSourceKind.File when request.FontFilePath != null
                    => BmFont.Generate(request.FontFilePath, options),
                FontSourceKind.System when request.SystemFontFamily != null
                    => BmFont.GenerateFromSystem(request.SystemFontFamily, options),
                _ => BmFont.Generate(request.FontData!, options),
            };
        });
    }
}
