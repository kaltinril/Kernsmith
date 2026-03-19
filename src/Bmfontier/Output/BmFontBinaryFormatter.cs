using Bmfontier.Output.Model;

namespace Bmfontier.Output;

internal class BmFontBinaryFormatter : IBmFontBinaryFormatter
{
    public string Format => "binary";

    public byte[] FormatBinary(BmFontModel model)
        => throw new NotSupportedException("Binary format is not yet implemented.");
}
