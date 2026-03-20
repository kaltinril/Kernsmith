using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bmfontier.Output.Model;

namespace Bmfontier.Output;

/// <summary>
/// Reads BMFont descriptor files in text, XML, or binary format into a <see cref="BmFontModel"/>.
/// </summary>
public static class BmFontReader
{
    /// <summary>
    /// Parses a BMFont text-format descriptor string into a <see cref="BmFontModel"/>.
    /// </summary>
    public static BmFontModel ReadText(string fntContent)
    {
        // Strip UTF-8 BOM if present (defense-in-depth for files written with BOM).
        if (fntContent.Length > 0 && fntContent[0] == '\uFEFF')
            fntContent = fntContent[1..];

        InfoBlock? info = null;
        CommonBlock? common = null;
        var pages = new List<PageEntry>();
        var chars = new List<CharEntry>();
        var kernings = new List<KerningEntry>();
        ExtendedMetadata? extended = null;

        foreach (var line in fntContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var spaceIndex = trimmed.IndexOf(' ');
            var tag = spaceIndex >= 0 ? trimmed[..spaceIndex] : trimmed;
            var rest = spaceIndex >= 0 ? trimmed[(spaceIndex + 1)..] : "";
            var kvp = ParseKeyValuePairs(rest);

            switch (tag)
            {
                case "info":
                    info = ParseInfoFromKvp(kvp);
                    break;
                case "common":
                    common = ParseCommonFromKvp(kvp);
                    break;
                case "page":
                    pages.Add(new PageEntry(
                        GetInt(kvp, "id"),
                        GetString(kvp, "file")));
                    break;
                case "char":
                    chars.Add(new CharEntry(
                        GetInt(kvp, "id"),
                        GetInt(kvp, "x"),
                        GetInt(kvp, "y"),
                        GetInt(kvp, "width"),
                        GetInt(kvp, "height"),
                        GetInt(kvp, "xoffset"),
                        GetInt(kvp, "yoffset"),
                        GetInt(kvp, "xadvance"),
                        GetInt(kvp, "page"),
                        GetInt(kvp, "chnl", 15)));
                    break;
                case "kerning":
                    kernings.Add(new KerningEntry(
                        GetInt(kvp, "first"),
                        GetInt(kvp, "second"),
                        GetInt(kvp, "amount")));
                    break;
                case "bmfontier":
                    extended = ParseExtendedFromKvp(kvp);
                    break;
                // "chars" and "kernings" lines just have count, skip them
            }
        }

        if (info == null)
            throw new FormatException("BMFont text format: missing 'info' line.");
        if (common == null)
            throw new FormatException("BMFont text format: missing 'common' line.");

        return new BmFontModel
        {
            Info = info,
            Common = common,
            Pages = pages,
            Characters = chars,
            KerningPairs = kernings,
            Extended = extended,
        };
    }

    /// <summary>
    /// Parses a BMFont XML-format descriptor string into a <see cref="BmFontModel"/>.
    /// </summary>
    public static BmFontModel ReadXml(string fntContent)
    {
        var doc = XDocument.Parse(fntContent);
        var root = doc.Root ?? throw new FormatException("BMFont XML: missing root element.");

        // The root should be <font>
        var fontEl = root.Name.LocalName == "font" ? root : throw new FormatException("BMFont XML: root element must be <font>.");

        var infoEl = fontEl.Element("info") ?? throw new FormatException("BMFont XML: missing <info> element.");
        var commonEl = fontEl.Element("common") ?? throw new FormatException("BMFont XML: missing <common> element.");
        var pagesEl = fontEl.Element("pages");
        var charsEl = fontEl.Element("chars");
        var kerningsEl = fontEl.Element("kernings");
        var bmfontierEl = fontEl.Element("bmfontier");

        var info = ParseInfoFromXml(infoEl);
        var common = ParseCommonFromXml(commonEl);

        var pages = new List<PageEntry>();
        if (pagesEl != null)
        {
            foreach (var pageEl in pagesEl.Elements("page"))
            {
                pages.Add(new PageEntry(
                    XmlAttrInt(pageEl, "id"),
                    XmlAttrStr(pageEl, "file")));
            }
        }

        var chars = new List<CharEntry>();
        if (charsEl != null)
        {
            foreach (var charEl in charsEl.Elements("char"))
            {
                chars.Add(new CharEntry(
                    XmlAttrInt(charEl, "id"),
                    XmlAttrInt(charEl, "x"),
                    XmlAttrInt(charEl, "y"),
                    XmlAttrInt(charEl, "width"),
                    XmlAttrInt(charEl, "height"),
                    XmlAttrInt(charEl, "xoffset"),
                    XmlAttrInt(charEl, "yoffset"),
                    XmlAttrInt(charEl, "xadvance"),
                    XmlAttrInt(charEl, "page"),
                    XmlAttrInt(charEl, "chnl", 15)));
            }
        }

        var kernings = new List<KerningEntry>();
        if (kerningsEl != null)
        {
            foreach (var kernEl in kerningsEl.Elements("kerning"))
            {
                kernings.Add(new KerningEntry(
                    XmlAttrInt(kernEl, "first"),
                    XmlAttrInt(kernEl, "second"),
                    XmlAttrInt(kernEl, "amount")));
            }
        }

        var extended = bmfontierEl != null ? ParseExtendedFromXml(bmfontierEl) : null;

        return new BmFontModel
        {
            Info = info,
            Common = common,
            Pages = pages,
            Characters = chars,
            KerningPairs = kernings,
            Extended = extended,
        };
    }

    /// <summary>
    /// Parses a BMFont binary-format descriptor into a <see cref="BmFontModel"/>.
    /// </summary>
    public static BmFontModel ReadBinary(byte[] fntData)
    {
        if (fntData.Length < 4)
            throw new FormatException("BMFont binary: data too short.");
        if (fntData[0] != 66 || fntData[1] != 77 || fntData[2] != 70)
            throw new FormatException("BMFont binary: invalid header (expected 'BMF').");
        if (fntData[3] != 3)
            throw new FormatException($"BMFont binary: unsupported version {fntData[3]} (expected 3).");

        InfoBlock? info = null;
        CommonBlock? common = null;
        var pages = new List<PageEntry>();
        var chars = new List<CharEntry>();
        var kernings = new List<KerningEntry>();
        ExtendedMetadata? extended = null;

        var offset = 4;
        while (offset < fntData.Length)
        {
            if (offset + 5 > fntData.Length)
                break;

            var blockType = fntData[offset];
            var blockSize = BitConverter.ToInt32(fntData, offset + 1);
            offset += 5;

            if (offset + blockSize > fntData.Length)
                throw new FormatException($"BMFont binary: block type {blockType} exceeds data length.");

            var blockData = new ReadOnlySpan<byte>(fntData, offset, blockSize);

            switch (blockType)
            {
                case 1:
                    info = ParseInfoBlockBinary(blockData);
                    break;
                case 2:
                    common = ParseCommonBlockBinary(blockData);
                    break;
                case 3:
                    pages = ParsePagesBlockBinary(blockData, common?.Pages ?? 1);
                    break;
                case 4:
                    chars = ParseCharsBlockBinary(blockData);
                    break;
                case 5:
                    kernings = ParseKerningsBlockBinary(blockData);
                    break;
                case 6:
                    extended = ParseExtendedBlockBinary(blockData);
                    break;
            }

            offset += blockSize;
        }

        if (info == null)
            throw new FormatException("BMFont binary: missing info block.");
        if (common == null)
            throw new FormatException("BMFont binary: missing common block.");

        return new BmFontModel
        {
            Info = info,
            Common = common,
            Pages = pages,
            Characters = chars,
            KerningPairs = kernings,
            Extended = extended,
        };
    }

    /// <summary>
    /// Auto-detects the format of the given BMFont data and parses it into a <see cref="BmFontModel"/>.
    /// </summary>
    public static BmFontModel Read(byte[] fntData)
    {
        if (fntData.Length >= 3 && fntData[0] == 66 && fntData[1] == 77 && fntData[2] == 70)
            return ReadBinary(fntData);

        var text = Encoding.UTF8.GetString(fntData);
        var trimmed = text.TrimStart();

        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<font", StringComparison.OrdinalIgnoreCase))
            return ReadXml(text);

        return ReadText(text);
    }

    #region Text format helpers

    private static readonly Regex KeyValueRegex = new(
        @"(\w+)=(""[^""]*""|\S+)",
        RegexOptions.Compiled);

    private static Dictionary<string, string> ParseKeyValuePairs(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in KeyValueRegex.Matches(text))
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            // Strip surrounding quotes if present
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            result[key] = value;
        }
        return result;
    }

    private static int GetInt(Dictionary<string, string> kvp, string key, int defaultValue = 0)
    {
        if (kvp.TryGetValue(key, out var val) && int.TryParse(val, out var result))
            return result;
        return defaultValue;
    }

    private static string GetString(Dictionary<string, string> kvp, string key, string defaultValue = "")
    {
        return kvp.TryGetValue(key, out var val) ? val : defaultValue;
    }

    private static bool GetBool(Dictionary<string, string> kvp, string key, bool defaultValue = false)
    {
        if (kvp.TryGetValue(key, out var val))
            return val == "1";
        return defaultValue;
    }

    private static Padding ParsePadding(string value)
    {
        var parts = value.Split(',');
        if (parts.Length == 4 &&
            int.TryParse(parts[0], out var up) &&
            int.TryParse(parts[1], out var right) &&
            int.TryParse(parts[2], out var down) &&
            int.TryParse(parts[3], out var left))
        {
            return new Padding(up, right, down, left);
        }
        return Padding.Zero;
    }

    private static Spacing ParseSpacing(string value)
    {
        var parts = value.Split(',');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var h) &&
            int.TryParse(parts[1], out var v))
        {
            return new Spacing(h, v);
        }
        return new Spacing(0, 0);
    }

    private static InfoBlock ParseInfoFromKvp(Dictionary<string, string> kvp)
    {
        var padding = kvp.TryGetValue("padding", out var padStr)
            ? ParsePadding(padStr)
            : Padding.Zero;

        var spacing = kvp.TryGetValue("spacing", out var spcStr)
            ? ParseSpacing(spcStr)
            : new Spacing(0, 0);

        return new InfoBlock(
            Face: GetString(kvp, "face"),
            Size: GetInt(kvp, "size"),
            Bold: GetBool(kvp, "bold"),
            Italic: GetBool(kvp, "italic"),
            Unicode: GetBool(kvp, "unicode"),
            Smooth: GetBool(kvp, "smooth"),
            FixedHeight: false,
            StretchH: GetInt(kvp, "stretchH", 100),
            Charset: GetString(kvp, "charset"),
            Aa: GetInt(kvp, "aa", 1),
            Padding: padding,
            Spacing: spacing);
    }

    private static CommonBlock ParseCommonFromKvp(Dictionary<string, string> kvp)
    {
        return new CommonBlock(
            LineHeight: GetInt(kvp, "lineHeight"),
            Base: GetInt(kvp, "base"),
            ScaleW: GetInt(kvp, "scaleW"),
            ScaleH: GetInt(kvp, "scaleH"),
            Pages: GetInt(kvp, "pages", 1),
            Packed: GetBool(kvp, "packed"),
            AlphaChnl: GetInt(kvp, "alphaChnl"),
            RedChnl: GetInt(kvp, "redChnl"),
            GreenChnl: GetInt(kvp, "greenChnl"),
            BlueChnl: GetInt(kvp, "blueChnl"));
    }

    #endregion

    #region XML format helpers

    private static int XmlAttrInt(XElement el, string name, int defaultValue = 0)
    {
        var attr = el.Attribute(name);
        if (attr != null && int.TryParse(attr.Value, out var result))
            return result;
        return defaultValue;
    }

    private static string XmlAttrStr(XElement el, string name, string defaultValue = "")
    {
        return el.Attribute(name)?.Value ?? defaultValue;
    }

    private static bool XmlAttrBool(XElement el, string name, bool defaultValue = false)
    {
        var attr = el.Attribute(name);
        if (attr != null)
            return attr.Value == "1";
        return defaultValue;
    }

    private static InfoBlock ParseInfoFromXml(XElement el)
    {
        var padding = el.Attribute("padding") != null
            ? ParsePadding(el.Attribute("padding")!.Value)
            : Padding.Zero;

        var spacing = el.Attribute("spacing") != null
            ? ParseSpacing(el.Attribute("spacing")!.Value)
            : new Spacing(0, 0);

        return new InfoBlock(
            Face: XmlAttrStr(el, "face"),
            Size: XmlAttrInt(el, "size"),
            Bold: XmlAttrBool(el, "bold"),
            Italic: XmlAttrBool(el, "italic"),
            Unicode: XmlAttrBool(el, "unicode"),
            Smooth: XmlAttrBool(el, "smooth"),
            FixedHeight: false,
            StretchH: XmlAttrInt(el, "stretchH", 100),
            Charset: XmlAttrStr(el, "charset"),
            Aa: XmlAttrInt(el, "aa", 1),
            Padding: padding,
            Spacing: spacing);
    }

    private static CommonBlock ParseCommonFromXml(XElement el)
    {
        return new CommonBlock(
            LineHeight: XmlAttrInt(el, "lineHeight"),
            Base: XmlAttrInt(el, "base"),
            ScaleW: XmlAttrInt(el, "scaleW"),
            ScaleH: XmlAttrInt(el, "scaleH"),
            Pages: XmlAttrInt(el, "pages", 1),
            Packed: XmlAttrBool(el, "packed"),
            AlphaChnl: XmlAttrInt(el, "alphaChnl"),
            RedChnl: XmlAttrInt(el, "redChnl"),
            GreenChnl: XmlAttrInt(el, "greenChnl"),
            BlueChnl: XmlAttrInt(el, "blueChnl"));
    }

    #endregion

    #region Binary format helpers

    private static InfoBlock ParseInfoBlockBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14)
            throw new FormatException("BMFont binary: info block too short.");

        var fontSize = BitConverter.ToInt16(data);
        var bitField = data[2];

        var smooth = (bitField & (1 << 7)) != 0;
        var unicode = (bitField & (1 << 6)) != 0;
        var italic = (bitField & (1 << 5)) != 0;
        var bold = (bitField & (1 << 4)) != 0;
        var fixedHeight = (bitField & (1 << 3)) != 0;

        // data[3] = charSet
        var stretchH = BitConverter.ToUInt16(data[4..]);
        var aa = data[6];
        var paddingUp = data[7];
        var paddingRight = data[8];
        var paddingDown = data[9];
        var paddingLeft = data[10];
        var spacingH = data[11];
        var spacingV = data[12];
        // data[13] = outline

        // Font name: null-terminated string starting at offset 14
        var nameEnd = 14;
        while (nameEnd < data.Length && data[nameEnd] != 0)
            nameEnd++;
        var face = Encoding.UTF8.GetString(data[14..nameEnd]);

        return new InfoBlock(
            Face: face,
            Size: fontSize,
            Bold: bold,
            Italic: italic,
            Unicode: unicode,
            Smooth: smooth,
            FixedHeight: fixedHeight,
            StretchH: stretchH,
            Charset: "",
            Aa: aa,
            Padding: new Padding(paddingUp, paddingRight, paddingDown, paddingLeft),
            Spacing: new Spacing(spacingH, spacingV));
    }

    private static CommonBlock ParseCommonBlockBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length < 15)
            throw new FormatException("BMFont binary: common block too short.");

        var lineHeight = BitConverter.ToUInt16(data);
        var baseLine = BitConverter.ToUInt16(data[2..]);
        var scaleW = BitConverter.ToUInt16(data[4..]);
        var scaleH = BitConverter.ToUInt16(data[6..]);
        var pageCount = BitConverter.ToUInt16(data[8..]);
        var bitField = data[10];
        var packed = (bitField & (1 << 7)) != 0;
        var alphaChnl = data[11];
        var redChnl = data[12];
        var greenChnl = data[13];
        var blueChnl = data[14];

        return new CommonBlock(
            LineHeight: lineHeight,
            Base: baseLine,
            ScaleW: scaleW,
            ScaleH: scaleH,
            Pages: pageCount,
            Packed: packed,
            AlphaChnl: alphaChnl,
            RedChnl: redChnl,
            GreenChnl: greenChnl,
            BlueChnl: blueChnl);
    }

    private static List<PageEntry> ParsePagesBlockBinary(ReadOnlySpan<byte> data, int pageCount)
    {
        var pages = new List<PageEntry>();
        if (data.Length == 0 || pageCount == 0)
            return pages;

        // Page names are null-terminated strings, all padded to the same length.
        // Calculate string length from block size and page count.
        var stringLen = data.Length / pageCount;
        if (stringLen == 0)
            return pages;

        for (var i = 0; i < pageCount; i++)
        {
            var start = i * stringLen;
            if (start >= data.Length)
                break;

            var end = start;
            var limit = Math.Min(start + stringLen, data.Length);
            while (end < limit && data[end] != 0)
                end++;

            var name = Encoding.UTF8.GetString(data[start..end]);
            pages.Add(new PageEntry(i, name));
        }

        return pages;
    }

    private static List<CharEntry> ParseCharsBlockBinary(ReadOnlySpan<byte> data)
    {
        const int entrySize = 20;
        var count = data.Length / entrySize;
        var chars = new List<CharEntry>(count);

        for (var i = 0; i < count; i++)
        {
            var entry = data[(i * entrySize)..];
            chars.Add(new CharEntry(
                Id: (int)BitConverter.ToUInt32(entry),
                X: BitConverter.ToUInt16(entry[4..]),
                Y: BitConverter.ToUInt16(entry[6..]),
                Width: BitConverter.ToUInt16(entry[8..]),
                Height: BitConverter.ToUInt16(entry[10..]),
                XOffset: BitConverter.ToInt16(entry[12..]),
                YOffset: BitConverter.ToInt16(entry[14..]),
                XAdvance: BitConverter.ToInt16(entry[16..]),
                Page: entry[18],
                Channel: entry[19]));
        }

        return chars;
    }

    private static List<KerningEntry> ParseKerningsBlockBinary(ReadOnlySpan<byte> data)
    {
        const int entrySize = 10;
        var count = data.Length / entrySize;
        var kernings = new List<KerningEntry>(count);

        for (var i = 0; i < count; i++)
        {
            var entry = data[(i * entrySize)..];
            kernings.Add(new KerningEntry(
                First: (int)BitConverter.ToUInt32(entry),
                Second: (int)BitConverter.ToUInt32(entry[4..]),
                Amount: BitConverter.ToInt16(entry[8..])));
        }

        return kernings;
    }

    #endregion

    #region Extended metadata helpers

    private static ExtendedMetadata ParseExtendedFromKvp(Dictionary<string, string> kvp)
    {
        Dictionary<string, float>? axes = null;
        foreach (var key in kvp.Keys)
        {
            if (key.StartsWith("axis_", StringComparison.OrdinalIgnoreCase))
            {
                axes ??= new Dictionary<string, float>();
                var tag = key[5..];
                if (float.TryParse(kvp[key], CultureInfo.InvariantCulture, out var val))
                    axes[tag] = val;
            }
        }

        return new ExtendedMetadata
        {
            GeneratorVersion = GetString(kvp, "version"),
            SdfSpread = kvp.ContainsKey("sdfSpread") ? GetInt(kvp, "sdfSpread") : null,
            OutlineThickness = kvp.TryGetValue("outlineThickness", out var ot) && float.TryParse(ot, CultureInfo.InvariantCulture, out var otv) ? otv : null,
            GradientTopColor = kvp.TryGetValue("gradientTopColor", out var gtc) ? gtc : null,
            GradientBottomColor = kvp.TryGetValue("gradientBottomColor", out var gbc) ? gbc : null,
            ShadowOffsetX = kvp.ContainsKey("shadowOffsetX") ? GetInt(kvp, "shadowOffsetX") : null,
            ShadowOffsetY = kvp.ContainsKey("shadowOffsetY") ? GetInt(kvp, "shadowOffsetY") : null,
            ShadowColor = kvp.TryGetValue("shadowColor", out var sc) ? sc : null,
            SuperSampleLevel = kvp.ContainsKey("superSampleLevel") ? GetInt(kvp, "superSampleLevel") : null,
            ColorFont = GetBool(kvp, "colorFont") ? true : null,
            VariationAxes = axes
        };
    }

    private static ExtendedMetadata ParseExtendedFromXml(XElement el)
    {
        Dictionary<string, float>? axes = null;
        foreach (var attr in el.Attributes())
        {
            if (attr.Name.LocalName.StartsWith("axis_", StringComparison.OrdinalIgnoreCase))
            {
                axes ??= new Dictionary<string, float>();
                var tag = attr.Name.LocalName[5..];
                if (float.TryParse(attr.Value, CultureInfo.InvariantCulture, out var val))
                    axes[tag] = val;
            }
        }

        return new ExtendedMetadata
        {
            GeneratorVersion = XmlAttrStr(el, "version"),
            SdfSpread = el.Attribute("sdfSpread") != null ? XmlAttrInt(el, "sdfSpread") : null,
            OutlineThickness = el.Attribute("outlineThickness") != null && float.TryParse(el.Attribute("outlineThickness")!.Value, CultureInfo.InvariantCulture, out var otv) ? otv : null,
            GradientTopColor = el.Attribute("gradientTopColor")?.Value,
            GradientBottomColor = el.Attribute("gradientBottomColor")?.Value,
            ShadowOffsetX = el.Attribute("shadowOffsetX") != null ? XmlAttrInt(el, "shadowOffsetX") : null,
            ShadowOffsetY = el.Attribute("shadowOffsetY") != null ? XmlAttrInt(el, "shadowOffsetY") : null,
            ShadowColor = el.Attribute("shadowColor")?.Value,
            SuperSampleLevel = el.Attribute("superSampleLevel") != null ? XmlAttrInt(el, "superSampleLevel") : null,
            ColorFont = XmlAttrBool(el, "colorFont") ? true : null,
            VariationAxes = axes
        };
    }

    private static ExtendedMetadata? ParseExtendedBlockBinary(ReadOnlySpan<byte> data)
    {
        // Find the null terminator to get the JSON string length
        var end = data.IndexOf((byte)0);
        var jsonSpan = end >= 0 ? data[..end] : data;
        var json = Encoding.UTF8.GetString(jsonSpan);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Dictionary<string, float>? axes = null;
        if (root.TryGetProperty("variationAxes", out var axesEl) && axesEl.ValueKind == JsonValueKind.Object)
        {
            axes = new Dictionary<string, float>();
            foreach (var prop in axesEl.EnumerateObject())
            {
                if (prop.Value.TryGetSingle(out var val))
                    axes[prop.Name] = val;
            }
        }

        return new ExtendedMetadata
        {
            GeneratorVersion = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            SdfSpread = root.TryGetProperty("sdfSpread", out var ss) ? ss.GetInt32() : null,
            OutlineThickness = root.TryGetProperty("outlineThickness", out var otProp) ? otProp.GetSingle() : null,
            GradientTopColor = root.TryGetProperty("gradientTopColor", out var gtc) ? gtc.GetString() : null,
            GradientBottomColor = root.TryGetProperty("gradientBottomColor", out var gbc) ? gbc.GetString() : null,
            ShadowOffsetX = root.TryGetProperty("shadowOffsetX", out var sx) ? sx.GetInt32() : null,
            ShadowOffsetY = root.TryGetProperty("shadowOffsetY", out var sy) ? sy.GetInt32() : null,
            ShadowColor = root.TryGetProperty("shadowColor", out var scProp) ? scProp.GetString() : null,
            SuperSampleLevel = root.TryGetProperty("superSampleLevel", out var sl) ? sl.GetInt32() : null,
            ColorFont = root.TryGetProperty("colorFont", out var cf) && cf.GetBoolean() ? true : null,
            VariationAxes = axes
        };
    }

    #endregion
}
