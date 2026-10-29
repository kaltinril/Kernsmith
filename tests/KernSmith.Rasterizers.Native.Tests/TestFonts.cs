namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Shared access to the test font fixtures.</summary>
internal static class TestFonts
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>Path to the primary test font (Roboto-Regular.ttf).</summary>
    public static string RobotoRegularPath => Path.Combine(FixturesDir, "Roboto-Regular.ttf");

    /// <summary>Reads the primary test font into memory.</summary>
    public static byte[] RobotoRegularBytes() => File.ReadAllBytes(RobotoRegularPath);

    /// <summary>Path to the variable test font (RobotoFlex-Variable.ttf: fvar, avar, gvar, HVAR, MVAR).</summary>
    public static string RobotoFlexPath => Path.Combine(FixturesDir, "RobotoFlex-Variable.ttf");

    /// <summary>Reads the RobotoFlex variable font into memory.</summary>
    public static byte[] RobotoFlexBytes() => File.ReadAllBytes(RobotoFlexPath);

    /// <summary>Path to the single-axis variable test font (DancingScript-Variable.ttf, wght 400-700).</summary>
    public static string DancingScriptPath => Path.Combine(FixturesDir, "DancingScript-Variable.ttf");

    /// <summary>Reads the DancingScript variable font into memory.</summary>
    public static byte[] DancingScriptBytes() => File.ReadAllBytes(DancingScriptPath);
}
