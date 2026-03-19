using Bmfontier.Output.Model;

namespace Bmfontier.Output;

public interface IBmFontFormatter
{
    string Format { get; }
}

public interface IBmFontTextFormatter : IBmFontFormatter
{
    string FormatText(BmFontModel model);
}

public interface IBmFontBinaryFormatter : IBmFontFormatter
{
    byte[] FormatBinary(BmFontModel model);
}
