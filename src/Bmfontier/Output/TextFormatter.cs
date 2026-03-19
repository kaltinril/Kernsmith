using System.Text;
using Bmfontier.Output.Model;

namespace Bmfontier.Output;

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

        return sb.ToString();
    }

    private static void FormatInfo(StringBuilder sb, InfoBlock info)
    {
        sb.Append("info");
        sb.Append($" face=\"{info.Face}\"");
        sb.Append($" size={info.Size}");
        sb.Append($" bold={BoolToInt(info.Bold)}");
        sb.Append($" italic={BoolToInt(info.Italic)}");
        sb.Append($" charset=\"{info.Charset}\"");
        sb.Append($" unicode={BoolToInt(info.Unicode)}");
        sb.Append($" stretchH={info.StretchH}");
        sb.Append($" smooth={BoolToInt(info.Smooth)}");
        sb.Append($" aa={info.Aa}");
        sb.Append($" padding={info.Padding.Up},{info.Padding.Right},{info.Padding.Down},{info.Padding.Left}");
        sb.Append($" spacing={info.Spacing.Horizontal},{info.Spacing.Vertical}");
        sb.Append(" outline=0");
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
        sb.AppendLine($"chars count={chars.Count}");

        foreach (var ch in chars)
        {
            sb.Append("char");
            sb.Append($" id={ch.Id}");
            sb.Append($" x={ch.X}");
            sb.Append($" y={ch.Y}");
            sb.Append($" width={ch.Width}");
            sb.Append($" height={ch.Height}");
            sb.Append($" xoffset={ch.XOffset}");
            sb.Append($" yoffset={ch.YOffset}");
            sb.Append($" xadvance={ch.XAdvance}");
            sb.Append($" page={ch.Page}");
            sb.Append($" chnl={ch.Channel}");
            sb.AppendLine();
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

    private static int BoolToInt(bool value) => value ? 1 : 0;
}
