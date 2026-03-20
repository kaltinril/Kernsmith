namespace Bmfontier.Cli.Utilities;

/// <summary>
/// Helpers for formatted console output with color support.
/// </summary>
internal static class ConsoleOutput
{
    private static bool _verbose;
    private static bool _quiet;
    private static bool _noColor;

    static ConsoleOutput()
    {
        _noColor = Environment.GetEnvironmentVariable("NO_COLOR") != null
                   || Console.IsOutputRedirected;
    }

    public static void SetVerbose(bool verbose) => _verbose = verbose;
    public static void SetQuiet(bool quiet) => _quiet = quiet;
    public static void SetNoColor(bool noColor) => _noColor = noColor;
    public static bool IsQuiet => _quiet;
    public static bool IsVerbose => _verbose;

    public static void WriteError(string message)
    {
        if (_noColor)
            Console.Error.WriteLine($"Error: {message}");
        else
        {
            Console.Error.Write("\x1b[31mError: ");
            Console.Error.Write(message);
            Console.Error.WriteLine("\x1b[0m");
        }
    }

    public static void WriteWarning(string message)
    {
        if (_quiet) return;
        if (_noColor)
            Console.Error.WriteLine($"Warning: {message}");
        else
        {
            Console.Error.Write("\x1b[33mWarning: ");
            Console.Error.Write(message);
            Console.Error.WriteLine("\x1b[0m");
        }
    }

    public static void WriteSuccess(string message)
    {
        if (_quiet) return;
        if (_noColor)
            Console.WriteLine(message);
        else
            Console.WriteLine($"\x1b[32m{message}\x1b[0m");
    }

    public static void WriteStdout(string message)
    {
        if (_quiet) return;
        Console.WriteLine(message);
    }

    public static void WriteProgress(string message)
    {
        if (_quiet) return;
        Console.Error.WriteLine(message);
    }

    public static void WriteVerbose(string message)
    {
        if (!_verbose) return;
        Console.Error.WriteLine(message);
    }

    public static void WriteInfo(string label, string value)
    {
        Console.WriteLine($"  {label,-18}{value}");
    }

    public static void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }
}
