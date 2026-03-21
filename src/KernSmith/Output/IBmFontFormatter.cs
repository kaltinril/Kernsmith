using KernSmith.Output.Model;

namespace KernSmith.Output;

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
