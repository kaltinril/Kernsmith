namespace KernSmith.Cli.Utilities;

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

    /// <summary>
    /// Enables or disables verbose output mode.
    /// </summary>
    /// <param name="verbose"><c>true</c> to enable verbose messages.</param>
    public static void SetVerbose(bool verbose) => _verbose = verbose;

    /// <summary>
    /// Enables or disables quiet mode, which suppresses all output except errors.
    /// </summary>
    /// <param name="quiet"><c>true</c> to suppress non-error output.</param>
    public static void SetQuiet(bool quiet) => _quiet = quiet;

    /// <summary>
    /// Enables or disables colored output. When disabled, ANSI escape codes are omitted.
    /// </summary>
    /// <param name="noColor"><c>true</c> to disable colored output.</param>
    public static void SetNoColor(bool noColor) => _noColor = noColor;

    /// <summary>
    /// Gets whether quiet mode is active.
    /// </summary>
    public static bool IsQuiet => _quiet;

    /// <summary>
    /// Gets whether verbose mode is active.
    /// </summary>
    public static bool IsVerbose => _verbose;

    /// <summary>
    /// Resets all flags to their defaults. Useful between commands or tests.
    /// </summary>
    public static void Reset()
    {
        _verbose = false;
        _quiet = false;
        _noColor = Environment.GetEnvironmentVariable("NO_COLOR") != null
                   || Console.IsOutputRedirected;
    }

    /// <summary>
    /// Writes an error message to <see cref="Console.Error"/> with red coloring when enabled.
    /// Always shown regardless of quiet mode.
    /// </summary>
    /// <param name="message">The error message to display.</param>
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

    /// <summary>
    /// Writes a warning message to <see cref="Console.Error"/> with yellow coloring when enabled.
    /// Suppressed in quiet mode.
    /// </summary>
    /// <param name="message">The warning message to display.</param>
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

    /// <summary>
    /// Writes a success message to <see cref="Console.Out"/> with green coloring when enabled.
    /// Suppressed in quiet mode.
    /// </summary>
    /// <param name="message">The success message to display.</param>
    public static void WriteSuccess(string message)
    {
        if (_quiet) return;
        if (_noColor)
            Console.WriteLine(message);
        else
            Console.WriteLine($"\x1b[32m{message}\x1b[0m");
    }

    /// <summary>
    /// Writes a plain message to <see cref="Console.Out"/>. Suppressed in quiet mode.
    /// </summary>
    /// <param name="message">The message to display.</param>
    public static void WriteStdout(string message)
    {
        if (_quiet) return;
        Console.WriteLine(message);
    }

    /// <summary>
    /// Writes a progress message to <see cref="Console.Error"/>. Suppressed in quiet mode.
    /// </summary>
    /// <param name="message">The progress message to display.</param>
    public static void WriteProgress(string message)
    {
        if (_quiet) return;
        Console.Error.WriteLine(message);
    }

    /// <summary>
    /// Writes a diagnostic message to <see cref="Console.Error"/>. Only shown when verbose mode is active.
    /// </summary>
    /// <param name="message">The verbose message to display.</param>
    public static void WriteVerbose(string message)
    {
        if (!_verbose) return;
        Console.Error.WriteLine(message);
    }

    /// <summary>
    /// Writes a labeled key-value pair to <see cref="Console.Out"/> with fixed-width alignment.
    /// </summary>
    /// <param name="label">The left-aligned label.</param>
    /// <param name="value">The value to display next to the label.</param>
    public static void WriteInfo(string label, string value)
    {
        Console.WriteLine($"  {label,-18}{value}");
    }

    /// <summary>
    /// Writes a line to <see cref="Console.Out"/>. Not affected by quiet or verbose flags.
    /// </summary>
    /// <param name="message">The message to display, or empty for a blank line.</param>
    public static void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }
}
