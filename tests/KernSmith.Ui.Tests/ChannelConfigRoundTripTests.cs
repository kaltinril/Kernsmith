using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The UI must not silently discard a per-channel <see cref="ChannelConfig"/> loaded from a
/// project file. The UI has no per-channel content editor, so the requirement is preservation:
/// what was loaded must survive a save, and must reach generation unchanged.
/// </summary>
public sealed class ChannelConfigRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    /// <summary>White-on-alpha routing: glyph in alpha, RGB forced to one.</summary>
    private string WriteConfig(string name, string extraKeys = "")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path,
            "fileVersion=1\n"
            + "fontName=Arial\n"
            + "fontSize=32\n"
            + "chars=32-126\n"
            + "alphaChnl=0\nredChnl=4\ngreenChnl=4\nblueChnl=4\n"
            + extraKeys);
        return path;
    }

    private static (FontConfigViewModel, AtlasConfigViewModel, EffectsViewModel, CharacterGridViewModel) NewViewModels()
        => (new FontConfigViewModel(), new AtlasConfigViewModel(), new EffectsViewModel(), new CharacterGridViewModel());

    [Fact]
    public void LoadThenSave_PreservesChannelConfig()
    {
        // The user-facing bug: open a project with channel routing, save it, routing is gone.
        var service = new ProjectService();
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        var source = WriteConfig("source.bmfc");

        service.LoadProject(source, fontConfig, atlasConfig, effects, characterGrid);

        var resaved = Path.Combine(_dir, "resaved.bmfc");
        service.SaveProject(resaved, fontConfig, atlasConfig, effects, characterGrid);

        var reloaded = BmfcConfigReader.Read(resaved).Options;
        reloaded.Channels.ShouldNotBeNull();
        reloaded.Channels!.Alpha.ShouldBe(ChannelContent.Glyph);
        reloaded.Channels!.Red.ShouldBe(ChannelContent.One);
        reloaded.Channels!.Green.ShouldBe(ChannelContent.One);
        reloaded.Channels!.Blue.ShouldBe(ChannelContent.One);
    }

    [Fact]
    public void LoadProject_ChannelContent_DoesNotEnableChannelPacking()
    {
        // Channel *content* (alphaChnl=...) and channel *packing* (fourChnlPacked=1) are
        // different features. Loading the former must not tick the latter's checkbox.
        var service = new ProjectService();
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        var source = WriteConfig("content-only.bmfc", "fourChnlPacked=0\n");

        service.LoadProject(source, fontConfig, atlasConfig, effects, characterGrid);

        effects.ChannelPackingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void LoadProject_ChannelPacking_EnablesChannelPacking()
    {
        // The converse: fourChnlPacked=1 is what should tick it, and it was never read.
        var service = new ProjectService();
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        var path = Path.Combine(_dir, "packed.bmfc");
        File.WriteAllText(path,
            "fileVersion=1\nfontName=Arial\nfontSize=32\nchars=32-126\nfourChnlPacked=1\n");

        service.LoadProject(path, fontConfig, atlasConfig, effects, characterGrid);

        effects.ChannelPackingEnabled.ShouldBeTrue();
    }

    [Fact]
    public void LoadThenSave_WithoutChannelConfig_WritesNoChannelRouting()
    {
        // Guards the default path: a project with no channel routing must not gain any.
        var service = new ProjectService();
        var (fontConfig, atlasConfig, effects, characterGrid) = NewViewModels();
        var path = Path.Combine(_dir, "plain.bmfc");
        File.WriteAllText(path, "fileVersion=1\nfontName=Arial\nfontSize=32\nchars=32-126\n");

        service.LoadProject(path, fontConfig, atlasConfig, effects, characterGrid);

        var resaved = Path.Combine(_dir, "plain-resaved.bmfc");
        service.SaveProject(resaved, fontConfig, atlasConfig, effects, characterGrid);

        BmfcConfigReader.Read(resaved).Options.Channels.ShouldBeNull();
    }
}
