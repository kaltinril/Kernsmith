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

    /// <summary>Bungee Color: <c>COLR</c> version 0 layers over a nine-palette <c>CPAL</c> version 0.</summary>
    public static byte[] BungeeColorBytes() => File.ReadAllBytes(Path.Combine(FixturesDir, "Color", "BungeeColor-Regular.ttf"));

    /// <summary>Reem Kufi Ink: <c>COLR</c> version 1 paint graphs only, no version 0 layer records.</summary>
    public static byte[] ReemKufiInkBytes() => File.ReadAllBytes(Path.Combine(FixturesDir, "Color", "ReemKufiInk-Regular.ttf"));

    /// <summary>Noto Color Emoji: <c>CBDT</c>/<c>CBLC</c> bitmap glyphs only, no outlines.</summary>
    public static byte[] NotoColorEmojiBytes() => File.ReadAllBytes(Path.Combine(FixturesDir, "NotoColorEmoji.ttf"));
}
