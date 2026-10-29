using KernSmith.Font;
using KernSmith.Fonts.Web;
using Shouldly;

namespace KernSmith.Tests.Fonts.Web;

/// <summary>
/// Live network tests against the real Bunny Fonts and Google Fonts CDNs. Skipped by default
/// so the suite stays deterministic and offline-friendly; remove the <c>Skip</c> to run them
/// manually (requires internet access).
/// </summary>
public class WebFontSourceIntegrationTests
{
    [Fact(Skip = "Live network test — run manually with internet access.")]
    public async Task GetFontAsync_FetchesRobotoFromBunnyFonts_ReturnsDecodableWoff2()
    {
        using var source = new WebFontSource(WebFontProvider.BunnyFonts);

        var bytes = await source.GetFontAsync("Roboto", weight: 400, subset: "latin");

        // Bunny offers WOFF2 and WOFF; WOFF2 ("wOF2") is preferred.
        WoffDecompressor.IsWoff2(bytes).ShouldBeTrue();
        AssertDecodesToTrueType(bytes);
    }

    [Fact(Skip = "Live network test — run manually with internet access.")]
    public async Task GetFontAsync_FetchesRobotoFromGoogleFonts_ReturnsDecodableWoff2()
    {
        using var source = new WebFontSource(WebFontProvider.GoogleFonts);

        var bytes = await source.GetFontAsync("Roboto", weight: 400, subset: "latin");

        // Google serves only WOFF2 to the modern-browser User-Agent WebFontSource sends.
        WoffDecompressor.IsWoff2(bytes).ShouldBeTrue();
        AssertDecodesToTrueType(bytes);
    }

    private static void AssertDecodesToTrueType(byte[] bytes)
    {
        var sfnt = WoffDecompressor.Decompress(bytes);
        sfnt.Length.ShouldBeGreaterThan(12);
        // TrueType sfnt version 0x00010000.
        sfnt[..4].ShouldBe(new byte[] { 0x00, 0x01, 0x00, 0x00 });
    }
}
