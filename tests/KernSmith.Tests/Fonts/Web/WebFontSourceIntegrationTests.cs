using KernSmith.Font;
using KernSmith.Fonts.Web;
using Shouldly;

namespace KernSmith.Tests.Fonts.Web;

/// <summary>
/// Live network tests against the real Bunny Fonts CDN. Skipped by default so the
/// suite stays deterministic and offline-friendly; remove the <c>Skip</c> to run them
/// manually (requires internet access).
/// </summary>
public class WebFontSourceIntegrationTests
{
    [Fact(Skip = "Live network test — run manually with internet access.")]
    public async Task GetFontAsync_FetchesRobotoFromBunnyFonts_ReturnsValidWoff()
    {
        using var source = new WebFontSource(WebFontProvider.BunnyFonts);

        var bytes = await source.GetFontAsync("Roboto", weight: 400, subset: "latin");

        bytes.ShouldNotBeEmpty();
        // WOFF files begin with the "wOFF" signature (0x774F4646).
        WoffDecompressor.IsWoff(bytes).ShouldBeTrue();
    }
}
