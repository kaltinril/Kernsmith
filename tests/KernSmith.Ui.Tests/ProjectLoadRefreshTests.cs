using System.Buffers.Binary;
using System.Globalization;
using Gum.Forms.Controls;
using KernSmith.Ui.Layout;
using KernSmith.Ui.Services;
using KernSmith.Ui.ViewModels;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// Load Project must leave every control of the font-config and effects panels showing the
/// loaded value, and the controls must still write user edits back to the view models.
/// Each project is saved from a view model holding a non-default value for every field the
/// project file stores, then loaded into a UI built from the real panels.
/// </summary>
[Collection(HeadlessGum.Name)]
public sealed class ProjectLoadRefreshTests : IDisposable
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly string _dir = Directory.CreateTempSubdirectory("kernsmith-ui-load-refresh-tests").FullName;

    static ProjectLoadRefreshTests() => HeadlessGum.EnsureInitialized();

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    /// <summary>The app's two settings panels over one <see cref="MainViewModel"/>, as MainLayout builds them.</summary>
    private sealed class Ui
    {
        public MainViewModel Vm { get; } = NewMainViewModel();
        public FontConfigPanel FontPanel { get; }
        public EffectsPanel EffectsPanel { get; }

        public Ui()
        {
            FontPanel = new FontConfigPanel(Vm, Vm.FontConfig, Vm.AtlasConfig);
            EffectsPanel = new EffectsPanel(Vm.Effects, null!);
        }

        public FrameworkElement Control(string name)
        {
            var visual = FontPanel.Visual.GetGraphicalUiElementByName(name)
                ?? EffectsPanel.Visual.GetGraphicalUiElementByName(name)
                ?? throw new ShouldAssertException($"No control named '{name}'.");
            return ((global::Gum.Wireframe.InteractiveGue)visual).FormsControlAsObject as FrameworkElement
                ?? throw new ShouldAssertException($"'{name}' is not a Forms control.");
        }

        public string Shown(string name) => Control(name) switch
        {
            TextBox box => box.Text ?? "",
            CheckBox check => check.IsChecked?.ToString() ?? "null",
            RadioButton radio => radio.IsChecked?.ToString() ?? "null",
            ComboBox combo => combo.SelectedObject?.ToString() ?? "(none)",
            Slider slider => slider.Value.ToString(CultureInfo.InvariantCulture),
            var other => throw new ShouldAssertException($"'{name}' is a {other.GetType().Name}."),
        };
    }

    // No KernSmithGame: it only marshals generation results to the UI thread, and nothing here generates.
    private static MainViewModel NewMainViewModel() =>
        new(new FontDiscoveryService(), new GenerationService(), new ProjectService(), new SessionService(), null!);

    private string Save(MainViewModel source, string name)
    {
        var path = Path.Combine(_dir, name);
        new ProjectService().SaveProject(path, source.FontConfig, source.AtlasConfig, source.Effects, source.CharacterGrid);
        return path;
    }

    /// <summary>A control, the view-model value it shows, and the text it should show for the loaded project.</summary>
    private sealed record Row(string Control, Func<MainViewModel, object?> Property, string Shown);

    /// <summary>
    /// Loads <paramref name="source"/>'s saved project into a fresh UI whose user had switched to
    /// System Font mode, checks the project's values reached the view models, then lists every
    /// control that does not show its value.
    /// </summary>
    private List<string> LoadAndListStaleControls(MainViewModel source, IEnumerable<Row> rows)
    {
        var path = Save(source, "project.bmfc");
        var ui = new Ui();
        ((RadioButton)ui.Control("SystemFontRadio")).IsChecked = true;

        ui.Vm.LoadProjectFromPath(path);

        var stale = new List<string>();
        foreach (var row in rows)
        {
            row.Property(ui.Vm).ShouldBe(row.Property(source), $"precondition: the project carries {row.Control}");
            var shown = ui.Shown(row.Control);
            if (shown != row.Shown)
                stale.Add($"{row.Control}: shows '{shown}', loaded '{row.Shown}'");
        }
        return stale;
    }

    private static string Text(float value) => value.ToString("0.0###");

    [Fact]
    public void LoadProject_EveryControlShowsTheLoadedValue()
    {
        var source = NewMainViewModel();
        source.FontConfig.LoadFromFile(Path.Combine(FixtureDir, "RobotoFlex-Variable.ttf"));
        source.FontConfig.SelectedBackend = RasterizerBackend.Native;
        source.FontConfig.FontSize = 48;

        var a = source.AtlasConfig;
        a.AutofitTexture = false;
        a.MaxWidth = 512;
        a.MaxHeight = 256;
        a.PaddingUp = 2; a.PaddingRight = 3; a.PaddingDown = 4; a.PaddingLeft = 5;
        a.SpacingH = 6; a.SpacingV = 7;
        a.IncludeKerning = false;
        a.DescriptorFormat = OutputFormat.Xml;

        var e = source.Effects;
        e.FillColor = "#112233";
        e.AdvanceAdjustX = 1.5f;
        e.Gamma = 2.2f;
        e.Bold = true;
        e.Italic = true;
        e.ForceSyntheticBold = true;
        e.ForceSyntheticItalic = true;
        e.AntiAlias = AntiAliasMode.Lcd;
        e.LcdFilter = LcdFilter.Smooth;
        e.LcdSubpixelOrder = LcdSubpixelOrder.Bgr;
        e.WidthPercent = 150;
        e.ItalicAngle = 20f;
        e.Hinting = false;
        e.SuperSampleLevel = 2;
        e.OutlineEnabled = true;
        e.OutlineWidth = 3;
        e.OutlineColor = "#445566";
        e.OutlineShape = OutlineShape.Square;
        e.OutlineMode = OutlineMode.Vector;
        e.ShadowEnabled = true;
        e.ShadowOffsetX = -3;
        e.ShadowOffsetY = 4;
        e.ShadowBlur = 2;
        e.ShadowBlurKernelSize = 3;
        e.ShadowBlurPasses = 2;
        e.ShadowColor = "#778899";
        e.ShadowOpacity = 50;
        e.HardShadow = true;
        e.GradientEnabled = true;
        e.GradientStartColor = "#AA0000";
        e.GradientEndColor = "#00BB00";
        e.GradientAngle = 45;
        e.GradientOffset = 0.25f;
        e.GradientScale = 1.5f;
        e.GradientCyclic = true;
        e.ChannelPackingEnabled = true;
        e.SdfSpread = 12f;
        e.SdfScale = 2;
        e.VariationAxisValues["wght"] = 700f;

        Row R<T>(string control, Func<MainViewModel, T> property, string shown) => new(control, vm => property(vm), shown);

        var stale = LoadAndListStaleControls(source,
        [
            R("BrowseFileRadio", vm => vm.FontConfig.FontSourceKind, "True"),
            R(nameof(FontConfigViewModel.FontSize), vm => vm.FontConfig.FontSize, "48"),
            R(nameof(FontConfigViewModel.SelectedBackend), vm => vm.FontConfig.SelectedBackend, "Native"),
            R(nameof(AtlasConfigViewModel.AutofitTexture), vm => vm.AtlasConfig.AutofitTexture, "True"),
            R(nameof(AtlasConfigViewModel.MaxWidth), vm => vm.AtlasConfig.MaxWidth, "512"),
            R(nameof(AtlasConfigViewModel.MaxHeight), vm => vm.AtlasConfig.MaxHeight, "256"),
            R(nameof(AtlasConfigViewModel.PaddingUp), vm => vm.AtlasConfig.PaddingUp, "2"),
            R(nameof(AtlasConfigViewModel.PaddingRight), vm => vm.AtlasConfig.PaddingRight, "3"),
            R(nameof(AtlasConfigViewModel.PaddingDown), vm => vm.AtlasConfig.PaddingDown, "4"),
            R(nameof(AtlasConfigViewModel.PaddingLeft), vm => vm.AtlasConfig.PaddingLeft, "5"),
            R(nameof(AtlasConfigViewModel.SpacingH), vm => vm.AtlasConfig.SpacingH, "6"),
            R(nameof(AtlasConfigViewModel.SpacingV), vm => vm.AtlasConfig.SpacingV, "7"),
            R(nameof(AtlasConfigViewModel.IncludeKerning), vm => vm.AtlasConfig.IncludeKerning, "False"),
            R(nameof(AtlasConfigViewModel.DescriptorFormat), vm => vm.AtlasConfig.DescriptorFormat, "XML"),
            R(nameof(EffectsViewModel.FillColor), vm => vm.Effects.FillColor, "#112233"),
            R(nameof(EffectsViewModel.AdvanceAdjustX), vm => vm.Effects.AdvanceAdjustX, Text(1.5f)),
            R(nameof(EffectsViewModel.Gamma), vm => vm.Effects.Gamma, "22"), // slider steps of 0.1
            R(nameof(EffectsViewModel.Bold), vm => vm.Effects.Bold, "True"),
            R(nameof(EffectsViewModel.Italic), vm => vm.Effects.Italic, "True"),
            R(nameof(EffectsViewModel.ForceSyntheticBold), vm => vm.Effects.ForceSyntheticBold, "True"),
            R(nameof(EffectsViewModel.ForceSyntheticItalic), vm => vm.Effects.ForceSyntheticItalic, "True"),
            R(nameof(EffectsViewModel.AntiAlias), vm => vm.Effects.AntiAlias, "LCD"),
            R(nameof(EffectsViewModel.LcdFilter), vm => vm.Effects.LcdFilter, "Smooth"),
            R(nameof(EffectsViewModel.LcdSubpixelOrder), vm => vm.Effects.LcdSubpixelOrder, "BGR"),
            R(nameof(EffectsViewModel.WidthPercent), vm => vm.Effects.WidthPercent, "150"),
            R(nameof(EffectsViewModel.ItalicAngle), vm => vm.Effects.ItalicAngle, "20"),
            R(nameof(EffectsViewModel.Hinting), vm => vm.Effects.Hinting, "False"),
            R(nameof(EffectsViewModel.SuperSampleLevel), vm => vm.Effects.SuperSampleLevel, "2x"),
            R(nameof(EffectsViewModel.OutlineEnabled), vm => vm.Effects.OutlineEnabled, "True"),
            R(nameof(EffectsViewModel.OutlineWidth), vm => vm.Effects.OutlineWidth, "3"),
            R(nameof(EffectsViewModel.OutlineColor), vm => vm.Effects.OutlineColor, "#445566"),
            R(nameof(EffectsViewModel.OutlineShape), vm => vm.Effects.OutlineShape, "Square"),
            R(nameof(EffectsViewModel.OutlineMode), vm => vm.Effects.OutlineMode, "Vector"),
            R(nameof(EffectsViewModel.ShadowEnabled), vm => vm.Effects.ShadowEnabled, "True"),
            R(nameof(EffectsViewModel.ShadowOffsetX), vm => vm.Effects.ShadowOffsetX, "-3"),
            R(nameof(EffectsViewModel.ShadowOffsetY), vm => vm.Effects.ShadowOffsetY, "4"),
            R(nameof(EffectsViewModel.ShadowBlur), vm => vm.Effects.ShadowBlur, "2"),
            R(nameof(EffectsViewModel.ShadowBlurKernelSize), vm => vm.Effects.ShadowBlurKernelSize, "3"),
            R(nameof(EffectsViewModel.ShadowBlurPasses), vm => vm.Effects.ShadowBlurPasses, "2"),
            R(nameof(EffectsViewModel.ShadowColor), vm => vm.Effects.ShadowColor, "#778899"),
            R(nameof(EffectsViewModel.ShadowOpacity), vm => vm.Effects.ShadowOpacity, "50"),
            R(nameof(EffectsViewModel.HardShadow), vm => vm.Effects.HardShadow, "True"),
            R(nameof(EffectsViewModel.GradientEnabled), vm => vm.Effects.GradientEnabled, "True"),
            R(nameof(EffectsViewModel.GradientStartColor), vm => vm.Effects.GradientStartColor, "#AA0000"),
            R(nameof(EffectsViewModel.GradientEndColor), vm => vm.Effects.GradientEndColor, "#00BB00"),
            R(nameof(EffectsViewModel.GradientAngle), vm => vm.Effects.GradientAngle, "45"),
            R(nameof(EffectsViewModel.GradientOffset), vm => vm.Effects.GradientOffset, Text(0.25f)),
            R(nameof(EffectsViewModel.GradientScale), vm => vm.Effects.GradientScale, Text(1.5f)),
            R(nameof(EffectsViewModel.GradientCyclic), vm => vm.Effects.GradientCyclic, "True"),
            R(nameof(EffectsViewModel.ChannelPackingEnabled), vm => vm.Effects.ChannelPackingEnabled, "True"),
            R(nameof(EffectsViewModel.SdfMode), vm => vm.Effects.SdfMode, "None"),
            R(nameof(EffectsViewModel.SdfSpread), vm => vm.Effects.SdfSpread, "12"),
            R(nameof(EffectsViewModel.SdfScale), vm => vm.Effects.SdfScale, "2"),
            R(nameof(EffectsViewModel.ColorFontEnabled), vm => vm.Effects.ColorFontEnabled, "False"),
            R(nameof(EffectsViewModel.ColorPaletteIndex), vm => vm.Effects.ColorPaletteIndex, "0"),
            R("VariationAxis-wght", vm => vm.Effects.VariationAxisValues["wght"], "700"),
        ]);

        stale.ShouldBeEmpty();
    }

    [Fact]
    public void LoadProject_ColorFontDistanceFieldAndPaletteControlsShowTheLoadedValues()
    {
        var source = NewMainViewModel();
        source.FontConfig.LoadFromFile(Path.Combine(FixtureDir, "BungeeColor-Regular.ttf"));
        source.FontConfig.SelectedBackend = RasterizerBackend.Native;
        source.Effects.HasColorGlyphs = source.FontConfig.HasColorGlyphs;
        source.Effects.ColorFontEnabled = true;
        source.Effects.ColorPaletteIndex = 2;
        source.Effects.SdfMode = SdfMode.Sdf;

        Row R<T>(string control, Func<MainViewModel, T> property, string shown) => new(control, vm => property(vm), shown);

        LoadAndListStaleControls(source,
        [
            R(nameof(EffectsViewModel.ColorFontEnabled), vm => vm.Effects.ColorFontEnabled, "True"),
            R(nameof(EffectsViewModel.ColorPaletteIndex), vm => vm.Effects.ColorPaletteIndex, "2"),
            R(nameof(EffectsViewModel.SdfMode), vm => vm.Effects.SdfMode, "SDF"),
        ]).ShouldBeEmpty();
    }

    private string WriteTwoFaceCollection()
    {
        var roboto = File.ReadAllBytes(Path.Combine(FixtureDir, "Roboto-Regular.ttf"));
        var collection = Path.Combine(_dir, "two-faces.ttc");
        File.WriteAllBytes(collection, BuildTtc(roboto, roboto));
        return collection;
    }

    [Fact]
    public void OpeningAFontCollection_ListsItsFaces()
    {
        // The face combo was filled from FaceCount before LoadFromFile had set it, so selecting
        // face 0 of an empty list threw and the font failed to load.
        var ui = new Ui();

        ui.Vm.LoadFontFromPath(WriteTwoFaceCollection());

        ui.Vm.FontConfig.IsFontLoaded.ShouldBeTrue(ui.Vm.StatusBar.StatusText);
        var faces = (ComboBox)ui.Control(nameof(FontConfigViewModel.FaceIndex));
        faces.Items!.Cast<object>().ShouldBe(["Face 0", "Face 1"]);
        faces.SelectedObject.ShouldBe("Face 0");
    }

    [Fact]
    public void LoadProject_FaceComboShowsTheLoadedFace()
    {
        var collection = WriteTwoFaceCollection();

        var source = NewMainViewModel();
        source.FontConfig.LoadFromFile(collection);
        source.FontConfig.ReloadWithFaceIndex(1);

        LoadAndListStaleControls(source,
        [
            new(nameof(FontConfigViewModel.FaceIndex), vm => vm.FontConfig.FaceIndex, "Face 1"),
        ]).ShouldBeEmpty();
    }

    /// <summary>A user edit to a control, and the view-model value it must produce.</summary>
    private sealed record Edit(string Control, Action<FrameworkElement> Apply, Func<MainViewModel, object?> Property, object? Expected);

    [Fact]
    public void AfterLoad_EditingEachKindOfControlStillUpdatesTheViewModel()
    {
        var source = NewMainViewModel();
        source.FontConfig.LoadFromFile(Path.Combine(FixtureDir, "RobotoFlex-Variable.ttf"));
        source.FontConfig.SelectedBackend = RasterizerBackend.Native;
        source.Effects.OutlineEnabled = true;
        source.Effects.GradientEnabled = true;
        var ui = new Ui();
        ui.Vm.LoadProjectFromPath(Save(source, "project.bmfc"));

        Edit[] edits =
        [
            new(nameof(EffectsViewModel.Bold), c => ((CheckBox)c).IsChecked = true, vm => vm.Effects.Bold, true),
            new(nameof(EffectsViewModel.Hinting), c => ((CheckBox)c).IsChecked = false, vm => vm.Effects.Hinting, false),
            new(nameof(EffectsViewModel.GradientEnabled), c => ((CheckBox)c).IsChecked = false, vm => vm.Effects.GradientEnabled, false),
            new(nameof(EffectsViewModel.HardShadow), c => ((CheckBox)c).IsChecked = true, vm => vm.Effects.HardShadow, true),
            new(nameof(EffectsViewModel.OutlineWidth), c => ((Slider)c).Value = 7, vm => vm.Effects.OutlineWidth, 7),
            new(nameof(EffectsViewModel.ShadowOffsetX), c => ((Slider)c).Value = -5, vm => vm.Effects.ShadowOffsetX, -5),
            new(nameof(EffectsViewModel.Gamma), c => ((Slider)c).Value = 25, vm => vm.Effects.Gamma, 2.5f),
            new(nameof(EffectsViewModel.SdfSpread), c => ((Slider)c).Value = 20, vm => vm.Effects.SdfSpread, 20f),
            new(nameof(EffectsViewModel.SdfScale), c => ((Slider)c).Value = 3, vm => vm.Effects.SdfScale, 3),
            new(nameof(EffectsViewModel.FillColor), c => ((TextBox)c).Text = "#ABCDEF", vm => vm.Effects.FillColor, "#ABCDEF"),
            new(nameof(EffectsViewModel.GradientStartColor), c => ((TextBox)c).Text = "#010203", vm => vm.Effects.GradientStartColor, "#010203"),
            new(nameof(EffectsViewModel.AdvanceAdjustX), c => ((TextBox)c).Text = Text(-2f), vm => vm.Effects.AdvanceAdjustX, -2f),
            new(nameof(EffectsViewModel.SuperSampleLevel), c => ((ComboBox)c).SelectedIndex = 2, vm => vm.Effects.SuperSampleLevel, 4),
            new(nameof(EffectsViewModel.ChannelPackingEnabled), c => ((CheckBox)c).IsChecked = true, vm => vm.Effects.ChannelPackingEnabled, true),
            new("VariationAxis-wght", c => ((Slider)c).Value = 300, vm => vm.Effects.VariationAxisValues["wght"], 300f),
        ];

        foreach (var edit in edits)
        {
            var before = ui.Shown(edit.Control);
            edit.Apply(ui.Control(edit.Control));
            edit.Property(ui.Vm).ShouldBe(edit.Expected, $"{edit.Control} edited from '{before}'");
        }

        // The edits are not rewritten by the view model echoing them back.
        ui.Shown(nameof(EffectsViewModel.Gamma)).ShouldBe("25");
        ui.Shown(nameof(EffectsViewModel.FillColor)).ShouldBe("#ABCDEF");
    }

    [Fact]
    public void TypingAPartialColor_LeavesTheBoxAndViewModelAlone()
    {
        var ui = new Ui();
        var box = (TextBox)ui.Control(nameof(EffectsViewModel.OutlineColor));

        box.Text = "#12";

        box.Text.ShouldBe("#12");
        ui.Vm.Effects.OutlineColor.ShouldBe("#000000");
    }

    /// <summary>Concatenates fonts into a TrueType Collection (version 1 header, tables not shared).</summary>
    private static byte[] BuildTtc(params byte[][] fonts)
    {
        var headerSize = 12 + 4 * fonts.Length;
        var directorySizes = fonts.Select(f => 12 + 16 * BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(4))).ToArray();
        using var output = new MemoryStream();
        output.Write(new byte[headerSize + directorySizes.Sum()]);
        var buffer = output.GetBuffer();
        "ttcf"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8), (uint)fonts.Length);

        var directoryOffset = headerSize;
        var records = new List<(int RecordPosition, byte[] Data)>();
        for (var f = 0; f < fonts.Length; f++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12 + 4 * f), (uint)directoryOffset);
            fonts[f].AsSpan(0, directorySizes[f]).CopyTo(buffer.AsSpan(directoryOffset));
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(fonts[f].AsSpan(4));
            for (var t = 0; t < tableCount; t++)
            {
                var record = fonts[f].AsSpan(12 + 16 * t);
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                records.Add((directoryOffset + 12 + 16 * t, fonts[f].AsSpan(offset, length).ToArray()));
            }
            directoryOffset += directorySizes[f];
        }

        foreach (var (recordPosition, data) in records)
        {
            while (output.Length % 4 != 0)
                output.WriteByte(0);
            var offset = (int)output.Length;
            output.Write(data);
            buffer = output.GetBuffer();
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(recordPosition + 8), (uint)offset);
        }

        while (output.Length % 4 != 0)
            output.WriteByte(0);
        return output.ToArray();
    }
}
