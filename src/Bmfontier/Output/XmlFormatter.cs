using Bmfontier.Output.Model;

namespace Bmfontier.Output;

internal class XmlFormatter : IBmFontTextFormatter
{
    public string Format => "xml";

    public string FormatText(BmFontModel model)
        => throw new NotSupportedException("XML format is not yet implemented.");
}
