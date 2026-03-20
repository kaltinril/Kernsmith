namespace Bmfontier.Cli.Utilities;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int FileNotFound = 2;
    public const int FontParseError = 3;
    public const int GenerationError = 4;
    public const int OutputWriteError = 5;
    public const int ConfigParseError = 10;
}
