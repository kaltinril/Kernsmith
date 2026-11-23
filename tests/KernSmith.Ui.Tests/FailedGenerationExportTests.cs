using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using KernSmith.Output;
using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// After a failed generation nothing valid exists for the current settings, so Export must not
/// write the previous successful font, and the preview and status bar must not keep showing it.
/// </summary>
public sealed class FailedGenerationExportTests
{
    private static readonly string RobotoPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    private const string PackingFillError = "Channel packing cannot be combined with a fill color";

    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly MainViewModel _vm;

    public FailedGenerationExportTests()
    {
        // The view model only uses the game's main-thread queue. A real Game opens a window, so
        // this one is never constructed (or finalized), and the test pumps the queue itself.
        var game = (KernSmithGame)RuntimeHelpers.GetUninitializedObject(typeof(KernSmithGame));
        GC.SuppressFinalize(game);
        typeof(KernSmithGame)
            .GetField("_mainThreadActions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, _mainThread);

        _vm = new MainViewModel(new FontDiscoveryService(), new GenerationService(),
            new ProjectService(), new SessionService(), game);
        _vm.FontConfig.LoadFromFile(RobotoPath);
        _vm.CharacterGrid.AddFromText("ABg");
    }

    private void PumpMainThread()
    {
        while (_mainThread.TryDequeue(out var action))
            action();
    }

    private async Task GenerateAsync()
    {
        await _vm.GenerateAsync();
        PumpMainThread();
    }

    private async Task GenerateThenBreakSettingsAsync()
    {
        await GenerateAsync();
        _vm.Preview.HasResult.ShouldBeTrue();

        _vm.Effects.ChannelPackingEnabled = true;
        _vm.Effects.FillColor = "#FF8800";
        await GenerateAsync();
        _vm.StatusBar.StatusText.ShouldContain(PackingFillError);
    }

    [Fact]
    public async Task Export_WithASuccessfulResult_OffersIt()
    {
        await GenerateAsync();

        (await _vm.PrepareExportAsync()).ShouldNotBeNull();
    }

    [Fact]
    public async Task Export_AfterAFailedGeneration_DoesNotOfferThePreviousFont()
    {
        await GenerateThenBreakSettingsAsync();

        var exported = await _vm.PrepareExportAsync();
        PumpMainThread();

        exported.ShouldBeNull("the previous font does not match the settings that were rejected");
    }

    [Fact]
    public async Task Export_AfterAFailedGeneration_SaysWhy()
    {
        await GenerateThenBreakSettingsAsync();

        await _vm.PrepareExportAsync();
        PumpMainThread();

        _vm.StatusBar.StatusText.ShouldStartWith("Error: Cannot export: ");
        _vm.StatusBar.StatusText.ShouldContain(PackingFillError);
        _vm.StatusBar.IsGenerating.ShouldBeFalse();
    }

    [Fact]
    public async Task FailedGeneration_ClearsThePreviewAndResultInfo()
    {
        await GenerateThenBreakSettingsAsync();

        _vm.Preview.HasResult.ShouldBeFalse();
        _vm.Preview.Model.ShouldBeNull();
        _vm.Preview.Pages.ShouldBeEmpty();
        _vm.StatusBar.AtlasDimensions.ShouldBeEmpty();
        _vm.StatusBar.GlyphCount.ShouldBe(0);
        _vm.StatusBar.GenerationTime.ShouldBeEmpty();
        _vm.StatusBar.GlyphInfoText.ShouldBeEmpty();
    }

    [Fact]
    public async Task Export_AfterAValidationFailure_DoesNotOfferThePreviousFont()
    {
        await GenerateAsync();

        _vm.CharacterGrid.Clear();
        await GenerateAsync();
        var exported = await _vm.PrepareExportAsync();
        PumpMainThread();

        exported.ShouldBeNull();
        _vm.Preview.HasResult.ShouldBeFalse();
        _vm.StatusBar.StatusText.ShouldContain("No characters selected");
    }

    [Fact]
    public async Task Export_AfterRecovering_OffersTheNewResult()
    {
        await GenerateThenBreakSettingsAsync();

        _vm.Effects.FillColor = "#FFFFFF";
        await GenerateAsync();
        var exported = await _vm.PrepareExportAsync();

        exported.ShouldNotBeNull();
        exported.Model.Common.Packed.ShouldBeTrue("the recovered result uses the current settings");
        _vm.Preview.HasResult.ShouldBeTrue();
        _vm.StatusBar.StatusText.ShouldStartWith("Generation complete");
    }
}
