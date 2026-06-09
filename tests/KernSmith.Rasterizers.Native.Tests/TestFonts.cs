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
}
