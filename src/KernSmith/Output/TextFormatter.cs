using System.Globalization;
using System.Text;
using KernSmith.Output.Model;

namespace KernSmith.Output;

/// <summary>
/// Formats a <see cref="BmFontModel"/> as BMFont text format.
/// </summary>
internal sealed class TextFormatter : IBmFontTextFormatter
{
    public string Format => "text";

    /// <summary>
    /// Every number is written in the invariant culture: a <c>.fnt</c> is read on other machines
    /// and by engines expecting ASCII '-' and '.', and the thread's culture may format negatives
    /// with U+2212 (sv-SE) or decimals with a comma (de-DE).
    /// </summary>
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public string FormatText(BmFontModel model)
    {
        // Roughly a line per char and kerning pair, plus the header blocks.
        var sb = new StringBuilder(512 + model.Characters.Count * 112 + model.KerningPairs.Count * 48);

        FormatInfo(sb, model.Info);
        FormatCommon(sb, model.Common);
        FormatPages(sb, model.Pages);
        FormatChars(sb, model.Characters);
        FormatKernings(sb, model.KerningPairs);
        FormatExtended(sb, model.Extended);

        return sb.ToString();
    }

    private static void FormatInfo(StringBuilder sb, InfoBlock info)
    {
        sb.Append("info");
        sb.Append(Invariant, $" face=\"{info.Face}\"");
        sb.Append(Invariant, $" size={(int)Math.Round(info.Size)}");
        sb.Append(Invariant, $" bold={BoolToInt(info.Bold)}");
        sb.Append(Invariant, $" italic={BoolToInt(info.Italic)}");
        sb.Append(Invariant, $" charset=\"{info.Charset}\"");
        sb.Append(Invariant, $" unicode={BoolToInt(info.Unicode)}");
        sb.Append(Invariant, $" stretchH={info.StretchH}");
        sb.Append(Invariant, $" smooth={BoolToInt(info.Smooth)}");
        sb.Append(Invariant, $" aa={info.Aa}");
        sb.Append(Invariant, $" padding={info.Padding.Up},{info.Padding.Right},{info.Padding.Down},{info.Padding.Left}");
        sb.Append(Invariant, $" spacing={info.Spacing.Horizontal},{info.Spacing.Vertical}");
        sb.Append(Invariant, $" outline={info.Outline}");
        sb.AppendLine();
    }

    private static void FormatCommon(StringBuilder sb, CommonBlock common)
    {
        sb.Append("common");
        sb.Append(Invariant, $" lineHeight={common.LineHeight}");
        sb.Append(Invariant, $" base={common.Base}");
        sb.Append(Invariant, $" scaleW={common.ScaleW}");
        sb.Append(Invariant, $" scaleH={common.ScaleH}");
        sb.Append(Invariant, $" pages={common.Pages}");
        sb.Append(Invariant, $" packed={BoolToInt(common.Packed)}");
        sb.Append(Invariant, $" alphaChnl={common.AlphaChnl}");
        sb.Append(Invariant, $" redChnl={common.RedChnl}");
        sb.Append(Invariant, $" greenChnl={common.GreenChnl}");
        sb.Append(Invariant, $" blueChnl={common.BlueChnl}");
        sb.AppendLine();
    }

    private static void FormatPages(StringBuilder sb, IReadOnlyList<PageEntry> pages)
    {
        foreach (var page in pages)
        {
            sb.Append("page");
            sb.Append(Invariant, $" id={page.Id}");
            sb.Append(Invariant, $" file=\"{page.File}\"");
            sb.AppendLine();
        }
    }

    private static void FormatChars(StringBuilder sb, IReadOnlyList<CharEntry> chars)
    {
        sb.Append(Invariant, $"chars count={chars.Count}").AppendLine();

        foreach (var ch in chars)
        {
            sb.Append(Invariant,
                $"char id={ch.Id} x={ch.X} y={ch.Y} width={ch.Width} height={ch.Height} xoffset={ch.XOffset} yoffset={ch.YOffset} xadvance={ch.XAdvance} page={ch.Page} chnl={ch.Channel}")
              .AppendLine();
        }
    }

    private static void FormatKernings(StringBuilder sb, IReadOnlyList<KerningEntry> kernings)
    {
        if (kernings.Count == 0)
            return;

        sb.Append(Invariant, $"kernings count={kernings.Count}").AppendLine();

        foreach (var kern in kernings)
            sb.Append(Invariant, $"kerning first={kern.First} second={kern.Second} amount={kern.Amount}").AppendLine();
    }

    private static void FormatExtended(StringBuilder sb, ExtendedMetadata? extended)
    {
        if (extended == null)
            return;

        sb.Append("kernsmith");
        sb.Append(Invariant, $" version=\"{extended.GeneratorVersion}\"");

        if (extended.SdfSpread.HasValue)
            sb.Append(Invariant, $" sdfSpread={extended.SdfSpread.Value}");
        if (extended.SdfMode.HasValue)
            sb.Append(Invariant, $" sdfMode={extended.SdfMode.Value.ToString().ToLowerInvariant()}");
        if (extended.OutlineThickness.HasValue)
            sb.Append(Invariant, $" outlineThickness={extended.OutlineThickness.Value}");
        if (extended.GradientTopColor != null)
            sb.Append(Invariant, $" gradientTopColor={extended.GradientTopColor}");
        if (extended.GradientBottomColor != null)
            sb.Append(Invariant, $" gradientBottomColor={extended.GradientBottomColor}");
        if (extended.ShadowOffsetX.HasValue)
            sb.Append(Invariant, $" shadowOffsetX={extended.ShadowOffsetX.Value}");
        if (extended.ShadowOffsetY.HasValue)
            sb.Append(Invariant, $" shadowOffsetY={extended.ShadowOffsetY.Value}");
        if (extended.ShadowColor != null)
            sb.Append(Invariant, $" shadowColor={extended.ShadowColor}");
        if (extended.SuperSampleLevel.HasValue)
            sb.Append(Invariant, $" superSampleLevel={extended.SuperSampleLevel.Value}");
        if (extended.AdvanceAdjustY.HasValue)
            sb.Append(Invariant, $" advanceAdjustY={extended.AdvanceAdjustY.Value}");
        if (extended.ColorFont is true)
            sb.Append(" colorFont=1");
        if (extended.VariationAxes is { Count: > 0 })
        {
            foreach (var (tag, value) in extended.VariationAxes)
                sb.Append(Invariant, $" axis_{tag}={value}");
        }
        if (extended.VariantOf != null)
            sb.Append(Invariant, $" variantOf=\"{extended.VariantOf}\"");
        if (extended.Variants is { Count: > 0 })
            sb.Append(Invariant, $" variants=\"{string.Join(",", extended.Variants)}\"");

        sb.AppendLine();
    }

    private static int BoolToInt(bool value) => value ? 1 : 0;
}
