using System.Text;
using System.Xml;
using Bmfontier.Output.Model;

namespace Bmfontier.Output;

/// <summary>
/// Formats a <see cref="BmFontModel"/> as BMFont XML format.
/// </summary>
internal sealed class XmlFormatter : IBmFontTextFormatter
{
    public string Format => "xml";

    public string FormatText(BmFontModel model)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            NewLineOnAttributes = false,
        };

        using var writer = XmlWriter.Create(sb, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("font");

        WriteInfo(writer, model.Info);
        WriteCommon(writer, model.Common);
        WritePages(writer, model.Pages);
        WriteChars(writer, model.Characters);
        WriteKernings(writer, model.KerningPairs);

        writer.WriteEndElement(); // font
        writer.WriteEndDocument();
        writer.Flush();

        return sb.ToString();
    }

    private static void WriteInfo(XmlWriter writer, InfoBlock info)
    {
        writer.WriteStartElement("info");
        writer.WriteAttributeString("face", info.Face);
        writer.WriteAttributeString("size", info.Size.ToString());
        writer.WriteAttributeString("bold", BoolToString(info.Bold));
        writer.WriteAttributeString("italic", BoolToString(info.Italic));
        writer.WriteAttributeString("charset", info.Charset);
        writer.WriteAttributeString("unicode", BoolToString(info.Unicode));
        writer.WriteAttributeString("stretchH", info.StretchH.ToString());
        writer.WriteAttributeString("smooth", BoolToString(info.Smooth));
        writer.WriteAttributeString("aa", info.Aa.ToString());
        writer.WriteAttributeString("padding", $"{info.Padding.Up},{info.Padding.Right},{info.Padding.Down},{info.Padding.Left}");
        writer.WriteAttributeString("spacing", $"{info.Spacing.Horizontal},{info.Spacing.Vertical}");
        writer.WriteAttributeString("outline", "0");
        writer.WriteEndElement();
    }

    private static void WriteCommon(XmlWriter writer, CommonBlock common)
    {
        writer.WriteStartElement("common");
        writer.WriteAttributeString("lineHeight", common.LineHeight.ToString());
        writer.WriteAttributeString("base", common.Base.ToString());
        writer.WriteAttributeString("scaleW", common.ScaleW.ToString());
        writer.WriteAttributeString("scaleH", common.ScaleH.ToString());
        writer.WriteAttributeString("pages", common.Pages.ToString());
        writer.WriteAttributeString("packed", BoolToString(common.Packed));
        writer.WriteAttributeString("alphaChnl", common.AlphaChnl.ToString());
        writer.WriteAttributeString("redChnl", common.RedChnl.ToString());
        writer.WriteAttributeString("greenChnl", common.GreenChnl.ToString());
        writer.WriteAttributeString("blueChnl", common.BlueChnl.ToString());
        writer.WriteEndElement();
    }

    private static void WritePages(XmlWriter writer, IReadOnlyList<PageEntry> pages)
    {
        writer.WriteStartElement("pages");

        foreach (var page in pages)
        {
            writer.WriteStartElement("page");
            writer.WriteAttributeString("id", page.Id.ToString());
            writer.WriteAttributeString("file", page.File);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteChars(XmlWriter writer, IReadOnlyList<CharEntry> chars)
    {
        writer.WriteStartElement("chars");
        writer.WriteAttributeString("count", chars.Count.ToString());

        foreach (var ch in chars)
        {
            writer.WriteStartElement("char");
            writer.WriteAttributeString("id", ch.Id.ToString());
            writer.WriteAttributeString("x", ch.X.ToString());
            writer.WriteAttributeString("y", ch.Y.ToString());
            writer.WriteAttributeString("width", ch.Width.ToString());
            writer.WriteAttributeString("height", ch.Height.ToString());
            writer.WriteAttributeString("xoffset", ch.XOffset.ToString());
            writer.WriteAttributeString("yoffset", ch.YOffset.ToString());
            writer.WriteAttributeString("xadvance", ch.XAdvance.ToString());
            writer.WriteAttributeString("page", ch.Page.ToString());
            writer.WriteAttributeString("chnl", ch.Channel.ToString());
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteKernings(XmlWriter writer, IReadOnlyList<KerningEntry> kernings)
    {
        if (kernings.Count == 0)
            return;

        writer.WriteStartElement("kernings");
        writer.WriteAttributeString("count", kernings.Count.ToString());

        foreach (var kern in kernings)
        {
            writer.WriteStartElement("kerning");
            writer.WriteAttributeString("first", kern.First.ToString());
            writer.WriteAttributeString("second", kern.Second.ToString());
            writer.WriteAttributeString("amount", kern.Amount.ToString());
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string BoolToString(bool value) => value ? "1" : "0";
}
