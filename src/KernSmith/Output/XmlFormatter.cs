using System.Globalization;
using System.Text;
using System.Xml;
using KernSmith.Output.Model;

namespace KernSmith.Output;

/// <summary>
/// Formats a <see cref="BmFontModel"/> as BMFont XML format.
/// </summary>
internal sealed class XmlFormatter : IBmFontTextFormatter
{
    public string Format => "xml";

    public string FormatText(BmFontModel model)
    {
        // FileWriter saves this text as UTF-8, so the declaration must say utf-8; a writer over a
        // plain StringBuilder would declare utf-16.
        using var sw = new Utf8StringWriter();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            NewLineOnAttributes = false,
        };

        using var writer = XmlWriter.Create(sw, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("font");

        WriteInfo(writer, model.Info);
        WriteCommon(writer, model.Common);
        WritePages(writer, model.Pages);
        WriteChars(writer, model.Characters);
        WriteKernings(writer, model.KerningPairs);
        WriteExtended(writer, model.Extended);

        writer.WriteEndElement(); // font
        writer.WriteEndDocument();
        writer.Flush();

        return sw.ToString();
    }

    private sealed class Utf8StringWriter() : StringWriter(CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => new UTF8Encoding(false);
    }

    private static void WriteInfo(XmlWriter writer, InfoBlock info)
    {
        writer.WriteStartElement("info");
        writer.WriteAttributeString("face", info.Face);
        writer.WriteAttributeString("size", ((int)Math.Round(info.Size)).ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("bold", BoolToString(info.Bold));
        writer.WriteAttributeString("italic", BoolToString(info.Italic));
        writer.WriteAttributeString("charset", info.Charset);
        writer.WriteAttributeString("unicode", BoolToString(info.Unicode));
        writer.WriteAttributeString("stretchH", info.StretchH.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("smooth", BoolToString(info.Smooth));
        writer.WriteAttributeString("aa", info.Aa.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("padding", string.Create(CultureInfo.InvariantCulture, $"{info.Padding.Up},{info.Padding.Right},{info.Padding.Down},{info.Padding.Left}"));
        writer.WriteAttributeString("spacing", string.Create(CultureInfo.InvariantCulture, $"{info.Spacing.Horizontal},{info.Spacing.Vertical}"));
        writer.WriteAttributeString("outline", info.Outline.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static void WriteCommon(XmlWriter writer, CommonBlock common)
    {
        writer.WriteStartElement("common");
        writer.WriteAttributeString("lineHeight", common.LineHeight.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("base", common.Base.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("scaleW", common.ScaleW.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("scaleH", common.ScaleH.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("pages", common.Pages.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("packed", BoolToString(common.Packed));
        writer.WriteAttributeString("alphaChnl", common.AlphaChnl.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("redChnl", common.RedChnl.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("greenChnl", common.GreenChnl.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("blueChnl", common.BlueChnl.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
    }

    private static void WritePages(XmlWriter writer, IReadOnlyList<PageEntry> pages)
    {
        writer.WriteStartElement("pages");

        foreach (var page in pages)
        {
            writer.WriteStartElement("page");
            writer.WriteAttributeString("id", page.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("file", page.File);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteChars(XmlWriter writer, IReadOnlyList<CharEntry> chars)
    {
        writer.WriteStartElement("chars");
        writer.WriteAttributeString("count", chars.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var ch in chars)
        {
            writer.WriteStartElement("char");
            writer.WriteAttributeString("id", ch.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("x", ch.X.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("y", ch.Y.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", ch.Width.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("height", ch.Height.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("xoffset", ch.XOffset.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("yoffset", ch.YOffset.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("xadvance", ch.XAdvance.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("page", ch.Page.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("chnl", ch.Channel.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteKernings(XmlWriter writer, IReadOnlyList<KerningEntry> kernings)
    {
        if (kernings.Count == 0)
            return;

        writer.WriteStartElement("kernings");
        writer.WriteAttributeString("count", kernings.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var kern in kernings)
        {
            writer.WriteStartElement("kerning");
            writer.WriteAttributeString("first", kern.First.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("second", kern.Second.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("amount", kern.Amount.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteExtended(XmlWriter writer, ExtendedMetadata? extended)
    {
        if (extended == null)
            return;

        writer.WriteStartElement("kernsmith");
        writer.WriteAttributeString("version", extended.GeneratorVersion);

        if (extended.SdfSpread.HasValue)
            writer.WriteAttributeString("sdfSpread", extended.SdfSpread.Value.ToString(CultureInfo.InvariantCulture));
        if (extended.SdfMode.HasValue)
            writer.WriteAttributeString("sdfMode", extended.SdfMode.Value.ToString().ToLowerInvariant());
        if (extended.OutlineThickness.HasValue)
            writer.WriteAttributeString("outlineThickness", extended.OutlineThickness.Value.ToString(CultureInfo.InvariantCulture));
        if (extended.GradientTopColor != null)
            writer.WriteAttributeString("gradientTopColor", extended.GradientTopColor);
        if (extended.GradientBottomColor != null)
            writer.WriteAttributeString("gradientBottomColor", extended.GradientBottomColor);
        if (extended.ShadowOffsetX.HasValue)
            writer.WriteAttributeString("shadowOffsetX", extended.ShadowOffsetX.Value.ToString(CultureInfo.InvariantCulture));
        if (extended.ShadowOffsetY.HasValue)
            writer.WriteAttributeString("shadowOffsetY", extended.ShadowOffsetY.Value.ToString(CultureInfo.InvariantCulture));
        if (extended.ShadowColor != null)
            writer.WriteAttributeString("shadowColor", extended.ShadowColor);
        if (extended.SuperSampleLevel.HasValue)
            writer.WriteAttributeString("superSampleLevel", extended.SuperSampleLevel.Value.ToString(CultureInfo.InvariantCulture));
        if (extended.AdvanceAdjustY.HasValue)
            writer.WriteAttributeString("advanceAdjustY", extended.AdvanceAdjustY.Value.ToString(CultureInfo.InvariantCulture));
        if (extended.ColorFont is true)
            writer.WriteAttributeString("colorFont", "1");
        if (extended.VariationAxes is { Count: > 0 })
        {
            foreach (var (tag, value) in extended.VariationAxes)
                writer.WriteAttributeString($"axis_{tag}", value.ToString(CultureInfo.InvariantCulture));
        }
        if (extended.VariantOf != null)
            writer.WriteAttributeString("variantOf", extended.VariantOf);
        if (extended.Variants is { Count: > 0 })
            writer.WriteAttributeString("variants", string.Join(",", extended.Variants));

        writer.WriteEndElement();
    }

    private static string BoolToString(bool value) => value ? "1" : "0";
}
