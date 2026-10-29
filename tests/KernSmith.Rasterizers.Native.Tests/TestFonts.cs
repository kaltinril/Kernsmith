namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Shared access to the test font fixtures.</summary>
internal static class TestFonts
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>Path to the primary test font (Roboto-Regular.ttf).</summary>
    public static string RobotoRegularPath => Path.Combine(FixturesDir, "Roboto-Regular.ttf");

    /// <summary>Path to the static CFF (PostScript-outline) fixture, Source Serif 4 Regular.</summary>
    public static string SourceSerifOtfPath => Path.Combine(FixturesDir, "SourceSerif4-Regular.otf");

    /// <summary>Path to the CFF2 variable fixture, Source Code Pro's upright variable font.</summary>
    public static string SourceCodeVariableOtfPath => Path.Combine(FixturesDir, "SourceCodeVF-Upright.otf");

    /// <summary>Reads the primary test font into memory.</summary>
    public static byte[] RobotoRegularBytes() => File.ReadAllBytes(RobotoRegularPath);

    /// <summary>Reads the static CFF fixture into memory.</summary>
    public static byte[] SourceSerifOtfBytes() => File.ReadAllBytes(SourceSerifOtfPath);

    /// <summary>Reads the CFF2 variable fixture into memory.</summary>
    public static byte[] SourceCodeVariableOtfBytes() => File.ReadAllBytes(SourceCodeVariableOtfPath);
}
