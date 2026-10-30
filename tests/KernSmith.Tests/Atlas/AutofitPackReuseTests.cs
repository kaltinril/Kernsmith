using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// With <see cref="FontGeneratorOptions.AutofitTexture"/>, the one-page verification pack and
/// the real pack run at the same size whenever the estimate fits. The built-in packers are
/// deterministic, so the verification's result is the real one; a caller's own packer is
/// still asked twice, since nothing says it is deterministic.
/// </summary>
public sealed class AutofitPackReuseTests
{
    private static readonly byte[] Font =
        File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private sealed class CountingPacker(IAtlasPacker inner) : IAtlasPacker
    {
        public List<(int Width, int Height)> Calls { get; } = new();

        public PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight)
        {
            Calls.Add((maxWidth, maxHeight));
            return inner.Pack(glyphs, maxWidth, maxHeight);
        }
    }

    [Theory]
    [InlineData(PackingAlgorithm.MaxRects, true, 32, 0, false)]
    [InlineData(PackingAlgorithm.MaxRects, false, 32, 0, false)]
    [InlineData(PackingAlgorithm.Skyline, true, 32, 0, false)]
    [InlineData(PackingAlgorithm.MaxRects, true, 48, 64, true)] // 64 px wide is too narrow: bumped
    [InlineData(PackingAlgorithm.Skyline, true, 48, 64, true)]
    public void Autofit_BuiltInPacker_MatchesTheSamePackerAskedTwice(
        PackingAlgorithm algorithm, bool powerOfTwo, int size, int fixedWidth, bool bumped)
    {
        FontGeneratorOptions Options() => new()
        {
            Size = size,
            Characters = CharacterSet.ExtendedAscii,
            Backend = RasterizerBackend.StbTrueType,
            AutofitTexture = true,
            PowerOfTwo = powerOfTwo,
            PackingAlgorithm = algorithm,
            SizeConstraints = fixedWidth > 0 ? new AtlasSizeConstraints { FixedWidth = fixedWidth } : null,
        };

        var builtIn = BmFont.Generate(Font, Options());

        IAtlasPacker inner = algorithm == PackingAlgorithm.Skyline ? new SkylinePacker() : new MaxRectsPacker();
        var counting = new CountingPacker(inner);
        var custom = Options();
        custom.Packer = counting;
        var askedTwice = BmFont.Generate(Font, custom);

        counting.Calls.Count.ShouldBe(2, "a caller's packer runs the verification pack and the real pack");
        (counting.Calls[0] != counting.Calls[1]).ShouldBe(bumped, "the case must exercise the path it is named for");
        builtIn.Model.Common.ShouldBe(askedTwice.Model.Common);
        builtIn.Model.Characters.ShouldBe(askedTwice.Model.Characters);
        builtIn.Pages.Count.ShouldBe(askedTwice.Pages.Count);
        for (var i = 0; i < builtIn.Pages.Count; i++)
            builtIn.Pages[i].PixelData.ShouldBe(askedTwice.Pages[i].PixelData);
    }
}
