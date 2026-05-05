using System.Text;
using KernSmith.Output.Model;

namespace KernSmith.Output;

/// <summary>
/// Formats a <see cref="BmFontModel"/> as BMFont text format.
/// </summary>
internal sealed class TextFormatter : IBmFontTextFormatter
{
    public string Format => "text";

    public string FormatText(BmFontModel model)
    {
        var sb = new StringBuilder();

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
        sb.Append($" face=\"{info.Face}\"");
        sb.Append($" size={(int)Math.Round(info.Size)}");
        sb.Append($" bold={BoolToInt(info.Bold)}");
        sb.Append($" italic={BoolToInt(info.Italic)}");
        sb.Append($" charset=\"{info.Charset}\"");
        sb.Append($" unicode={BoolToInt(info.Unicode)}");
        sb.Append($" stretchH={info.StretchH}");
        sb.Append($" smooth={BoolToInt(info.Smooth)}");
        sb.Append($" aa={info.Aa}");
        sb.Append($" padding={info.Padding.Up},{info.Padding.Right},{info.Padding.Down},{info.Padding.Left}");
        sb.Append($" spacing={info.Spacing.Horizontal},{info.Spacing.Vertical}");
        sb.Append($" outline={info.Outline}");
        sb.AppendLine();
    }

    private static void FormatCommon(StringBuilder sb, CommonBlock common)
    {
        sb.Append("common");
        sb.Append($" lineHeight={common.LineHeight}");
        sb.Append($" base={common.Base}");
        sb.Append($" scaleW={common.ScaleW}");
        sb.Append($" scaleH={common.ScaleH}");
        sb.Append($" pages={common.Pages}");
        sb.Append($" packed={BoolToInt(common.Packed)}");
        sb.Append($" alphaChnl={common.AlphaChnl}");
        sb.Append($" redChnl={common.RedChnl}");
        sb.Append($" greenChnl={common.GreenChnl}");
        sb.Append($" blueChnl={common.BlueChnl}");
        sb.AppendLine();
    }

    private static void FormatPages(StringBuilder sb, IReadOnlyList<PageEntry> pages)
    {
        foreach (var page in pages)
        {
            sb.Append("page");
            sb.Append($" id={page.Id}");
            sb.Append($" file=\"{page.File}\"");
            sb.AppendLine();
        }
    }

    private static void FormatChars(StringBuilder sb, IReadOnlyList<CharEntry> chars)
    {
        sb.Append("chars count=").Append(chars.Count).AppendLine();

        foreach (var ch in chars)
        {
            sb.Append("char")
              .Append(" id=").Append(ch.Id)
              .Append(" x=").Append(ch.X)
              .Append(" y=").Append(ch.Y)
              .Append(" width=").Append(ch.Width)
              .Append(" height=").Append(ch.Height)
              .Append(" xoffset=").Append(ch.XOffset)
              .Append(" yoffset=").Append(ch.YOffset)
              .Append(" xadvance=").Append(ch.XAdvance)
              .Append(" page=").Append(ch.Page)
              .Append(" chnl=").Append(ch.Channel)
              .AppendLine();
        }
    }

    private static void FormatKernings(StringBuilder sb, IReadOnlyList<KerningEntry> kernings)
    {
        if (kernings.Count == 0)
            return;

        sb.AppendLine($"kernings count={kernings.Count}");

        foreach (var kern in kernings)
        {
            sb.Append("kerning");
            sb.Append($" first={kern.First}");
            sb.Append($" second={kern.Second}");
            sb.Append($" amount={kern.Amount}");
            sb.AppendLine();
        }
    }

    private static void FormatExtended(StringBuilder sb, ExtendedMetadata? extended)
    {
        if (extended == null)
            return;

        sb.Append("kernsmith");
        sb.Append($" version=\"{extended.GeneratorVersion}\"");

        if (extended.SdfSpread.HasValue)
            sb.Append($" sdfSpread={extended.SdfSpread.Value}");
        if (extended.OutlineThickness.HasValue)
            sb.Append($" outlineThickness={extended.OutlineThickness.Value}");
        if (extended.GradientTopColor != null)
            sb.Append($" gradientTopColor={extended.GradientTopColor}");
        if (extended.GradientBottomColor != null)
            sb.Append($" gradientBottomColor={extended.GradientBottomColor}");
        if (extended.ShadowOffsetX.HasValue)
            sb.Append($" shadowOffsetX={extended.ShadowOffsetX.Value}");
        if (extended.ShadowOffsetY.HasValue)
            sb.Append($" shadowOffsetY={extended.ShadowOffsetY.Value}");
        if (extended.ShadowColor != null)
            sb.Append($" shadowColor={extended.ShadowColor}");
        if (extended.SuperSampleLevel.HasValue)
            sb.Append($" superSampleLevel={extended.SuperSampleLevel.Value}");
        if (extended.ColorFont is true)
            sb.Append(" colorFont=1");
        if (extended.VariationAxes is { Count: > 0 })
        {
            foreach (var (tag, value) in extended.VariationAxes)
                sb.Append($" axis_{tag}={value}");
        }

        sb.AppendLine();
    }

    private static int BoolToInt(bool value) => value ? 1 : 0;
}
