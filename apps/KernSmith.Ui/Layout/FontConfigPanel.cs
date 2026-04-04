using System.Linq;
using global::Gum.DataTypes;
using global::Gum.Forms.Controls;
using Gum.Themes.Editor;
using KernSmith.Ui.Models;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using MonoGameGum.GueDeriving;
using NativeFileDialogNET;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Left-side panel containing font file browser, system font dropdown,
/// TTC face selector, font metadata display, font size input, generate button, and
/// auto-regenerate toggle.
/// </summary>
public class FontConfigPanel : Panel
{
    private readonly MainViewModel _mainViewModel;
    private readonly FontConfigViewModel _fontConfig;
    private readonly AtlasConfigViewModel _atlasConfig;
    private ComboBox? _rasterizerCombo;

    public FontConfigPanel(MainViewModel mainViewModel, FontConfigViewModel fontConfig, AtlasConfigViewModel atlasConfig)
    {
        _mainViewModel = mainViewModel;
        _fontConfig = fontConfig;
        _atlasConfig = atlasConfig;

        BuildContent();
    }

    private void BuildContent()
    {
        // Root wrapper: vertical stack inside the panel so scroll area and bottom bar share space
        var root = new ContainerRuntime();
        root.WidthUnits = DimensionUnitType.RelativeToParent;
        root.Width = 0;
        root.HeightUnits = DimensionUnitType.RelativeToParent;
        root.Height = 0;
        root.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        this.Visual.Children.Add(root);

        // Scroll area wrapper (ratio height = fills remaining space above bottom bar)
        var scrollArea = new ContainerRuntime();
        scrollArea.WidthUnits = DimensionUnitType.RelativeToParent;
        scrollArea.Width = 0;
        scrollArea.HeightUnits = DimensionUnitType.Ratio;
        scrollArea.Height = 1;
        scrollArea.ClipsChildren = true;
        root.Children.Add(scrollArea);

        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        scrollViewer.Visual.Width = 0;
        scrollViewer.Visual.HeightUnits = DimensionUnitType.RelativeToParent;
        scrollViewer.Visual.Height = 0;
        // Make ScrollViewer borderless: solid background matching panel,
        // and inject margin overrides into states so Gum's state system applies them
        if (scrollViewer.Visual is global::Gum.Forms.DefaultVisuals.V3.ScrollViewerVisual scrollVisual)
        {
            scrollVisual.Background.ApplyState(global::Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle.NineSlice.Solid);
            scrollVisual.BackgroundColor = Theme.Panel;
            StripScrollViewerMargins(scrollVisual);
        }
        scrollArea.Children.Add(scrollViewer.Visual);

        var inner = new ContainerRuntime();
        inner.WidthUnits = DimensionUnitType.RelativeToParent;
        inner.HeightUnits = DimensionUnitType.RelativeToChildren;
        inner.Width = 0;
        inner.Height = 0;
        inner.X = 0;
        inner.Y = 0;
        inner.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.StackSpacing = Theme.SectionSpacing;
        scrollViewer.InnerPanel.Children.Add(inner);

        var stack = inner;

        // --- FONT FILE section (expander) ---
        var fontFileExpander = UiFactory.CreateExpander("Font File");
        stack.Children.Add(fontFileExpander.Visual);
        {
            // --- Font source radio buttons ---
            var sourceRow = new StackPanel();
            sourceRow.Orientation = Orientation.Horizontal;
            sourceRow.Spacing = Theme.SectionSpacing;
            sourceRow.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
            sourceRow.Visual.Height = Theme.SectionSpacing;
            fontFileExpander.AddContent(sourceRow);

            var browseRadio = new RadioButton();
            browseRadio.Text = "Browse File";
            browseRadio.IsChecked = true;
            browseRadio.Visual.YOrigin = global::RenderingLibrary.Graphics.VerticalAlignment.Center;
            browseRadio.Visual.Y = 0;
            browseRadio.Visual.YUnits = global::Gum.Converters.GeneralUnitType.PixelsFromMiddle;
            sourceRow.AddChild(browseRadio);

            var systemRadio = new RadioButton();
            systemRadio.Text = "System Font";
            systemRadio.Visual.YOrigin = global::RenderingLibrary.Graphics.VerticalAlignment.Center;
            systemRadio.Visual.Y = 0;
            systemRadio.Visual.YUnits = global::Gum.Converters.GeneralUnitType.PixelsFromMiddle;
            sourceRow.AddChild(systemRadio);

            // --- Browse button (visible in Browse File mode) ---
            var browseBtn = new Button();
            browseBtn.Text = "Browse for Font...";
            browseBtn.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
            browseBtn.Visual.Width = 0;
            browseBtn.Click += (_, _) =>
            {
                using var dialog = new NativeFileDialog()
                    .SelectFile()
                    .AddFilter("Font Files", "ttf,otf,woff,ttc")
                    .AddFilter("All Files", "*");
                var result = dialog.Open(out string? path);
                if (result == DialogResult.Okay && path != null)
                    _mainViewModel.LoadFontFromPath(path);
            };
            fontFileExpander.AddContent(browseBtn);
            TooltipService.SetTooltip(browseBtn, "Browse for a font file");

            // --- Single grid for all font info rows ---
            var fontGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
            fontFileExpander.AddContent(fontGrid);

            // Browse File rows
            var sourceLabel = new Label();
            sourceLabel.Text = "";
            var sourceFileRow = fontGrid.AddRow("Source:", sourceLabel);
            sourceFileRow.Visible = false;

            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.FontSourceKind) ||
                    e.PropertyName == nameof(FontConfigViewModel.FontSourceDescription))
                {
                    var isFile = _fontConfig.FontSourceKind == Models.FontSourceKind.File;
                    sourceFileRow.Visible = isFile;
                    if (isFile)
                        sourceLabel.Text = _fontConfig.FontSourceDescription;
                }
            };

            var faceCombo = new ComboBox();
            faceCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
            faceCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
            faceCombo.Visual.Width = 0;
            var faceRow = fontGrid.AddRow("Face:", faceCombo);
            faceRow.Visible = false;

            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.IsFontCollection))
                {
                    faceRow.Visible = _fontConfig.IsFontCollection;
                    if (_fontConfig.IsFontCollection)
                    {
                        faceCombo.Items.Clear();
                        for (int i = 0; i < _fontConfig.FaceCount; i++)
                            faceCombo.Items.Add($"Face {i}");
                        faceCombo.SelectedIndex = 0;
                    }
                }
            };

            faceCombo.SelectionChanged += (_, _) =>
            {
                if (faceCombo.SelectedIndex >= 0 && _fontConfig.IsFontCollection)
                    _fontConfig.ReloadWithFaceIndex(faceCombo.SelectedIndex);
            };

            // System Font row
            var familyCombo = new ComboBox();
            familyCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
            ((global::Gum.Forms.DefaultVisuals.V3.ListBoxVisual)familyCombo.ListBox.Visual).MakeHeightFixedSize();
            familyCombo.ListBox.Visual.Height = 300;
            familyCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
            familyCombo.Visual.Width = 0;
            var fontRow = fontGrid.AddRow("Font:", familyCombo);
            fontRow.Visible = false;
            TooltipService.SetTooltip(familyCombo, "Pick an installed system font. Requires a backend that supports system fonts (GDI or DirectWrite).");

            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.SystemFonts) && _fontConfig.SystemFonts != null)
                    familyCombo.Items = _fontConfig.SystemFonts.Select(g => (object)g.FamilyName).ToList();
            };

            familyCombo.SelectionChanged += (_, _) =>
            {
                if (familyCombo.SelectedIndex >= 0 && _fontConfig.SystemFonts != null)
                {
                    var group = _fontConfig.SystemFonts[familyCombo.SelectedIndex];
                    _fontConfig.SelectedFontFamily = group.FamilyName;
                    _fontConfig.CurrentFontGroup = group;
                    _fontConfig.LoadedAsBold = false;
                    _fontConfig.LoadedAsItalic = false;

                    if (group.Styles.Count > 0)
                    {
                        var font = group.Styles.FirstOrDefault(s =>
                            s.StyleName.Equals("Regular", StringComparison.OrdinalIgnoreCase))
                            ?? group.Styles[0];
                        _fontConfig.SelectedSystemFont = font;
                        try { _fontConfig.LoadFromSystem(font); }
                        catch (Exception) { }
                    }
                }
            };

            // --- Radio toggle: swap row visibility ---
            browseRadio.Checked += (_, _) =>
            {
                browseBtn.IsVisible = true;
                fontRow.Visible = false;
            };
            systemRadio.Checked += (_, _) =>
            {
                browseBtn.IsVisible = false;
                fontRow.Visible = true;
            };

            // --- Always-visible font info rows ---
            var glyphCount = new Label();
            glyphCount.Text = "0";
            glyphCount.SetBinding(nameof(Label.Text), nameof(FontConfigViewModel.NumGlyphs));
            glyphCount.Visual.BindingContext = _fontConfig;
            fontGrid.AddRow("Glyphs:", glyphCount);

            var colorGlyphLabel = new Label();
            colorGlyphLabel.Text = "Yes";
            var colorRow = fontGrid.AddRow("Color:", colorGlyphLabel);
            colorRow.Visible = false;

            var axesLabel = new Label();
            axesLabel.Text = "";
            var axesRow = fontGrid.AddRow("Axes:", axesLabel);
            axesRow.Visible = false;

            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.HasColorGlyphs))
                    colorRow.Visible = _fontConfig.HasColorGlyphs;

                if (e.PropertyName is nameof(FontConfigViewModel.HasVariationAxes)
                    or nameof(FontConfigViewModel.VariationAxesSummary))
                {
                    axesRow.Visible = _fontConfig.HasVariationAxes;
                    if (_fontConfig.HasVariationAxes)
                        axesLabel.Text = _fontConfig.VariationAxesSummary;
                }
            };
        }

        // --- SIZE section (expander) ---
        var sizeExpander = UiFactory.CreateExpander("Size");
        stack.Children.Add(sizeExpander.Visual);
        {
            var sizeGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };

            var sizeTextBox = new TextBox();
            sizeTextBox.Width = 42;
            sizeTextBox.Text = "32";
            sizeTextBox.TextChanged += (_, _) =>
            {
                if (int.TryParse(sizeTextBox.Text, out var size))
                    _fontConfig.FontSize = Math.Clamp(size, 4, 500);
            };
            sizeGrid.AddRow("Size (pt):", sizeTextBox);
            TooltipService.SetTooltip(sizeTextBox, "Font size in points (4-500)");

            _rasterizerCombo = new ComboBox();
            _rasterizerCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
            _rasterizerCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
            _rasterizerCombo.Visual.Width = 0;
            foreach (var backend in _fontConfig.AvailableBackends)
                _rasterizerCombo.Items.Add(backend.ToString());
            _rasterizerCombo.SelectedIndex = _fontConfig.AvailableBackends.ToList().IndexOf(_fontConfig.SelectedBackend);
            if (_rasterizerCombo.SelectedIndex < 0) _rasterizerCombo.SelectedIndex = 0;
            _rasterizerCombo.SelectionChanged += (_, _) => OnRasterizerComboSelectionChanged();
            sizeGrid.AddRow("Rasterizer:", _rasterizerCombo);
            TooltipService.SetTooltip(_rasterizerCombo, "Glyph rasterizer backend. FreeType: cross-platform default. GDI: Windows-only, matches BMFont output. DirectWrite: Windows-only, modern rendering with color/variable font support.");

            sizeExpander.AddContent(sizeGrid);
        }

        // --- ATLAS section (expander) ---
        var atlasExpander = UiFactory.CreateExpander("Atlas");
        stack.Children.Add(atlasExpander.Visual);
        BuildAtlasSection(atlasExpander);

        // --- OUTPUT section (expander) ---
        var outputExpander = UiFactory.CreateExpander("Output");
        stack.Children.Add(outputExpander.Visual);
        BuildOutputSection(outputExpander);

        // --- Fixed bottom bar: Generate button + Auto-regenerate ---
        BuildGenerateBar(root);
    }

    private void BuildGenerateBar(ContainerRuntime parent)
    {
        var bottomBar = new ContainerRuntime();
        bottomBar.HeightUnits = DimensionUnitType.RelativeToChildren;
        bottomBar.Height = 0;
        bottomBar.WidthUnits = DimensionUnitType.RelativeToParent;
        bottomBar.Width = 0;
        bottomBar.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        bottomBar.StackSpacing = Theme.ControlSpacing;
        parent.Children.Add(bottomBar);

        // Separator line at top of bar
        var separator = new ColoredRectangleRuntime();
        separator.Width = 0;
        separator.WidthUnits = DimensionUnitType.RelativeToParent;
        separator.Height = 1;
        separator.Color = Theme.PanelBorder;
        bottomBar.Children.Add(separator);

        // Inner padding container
        var barInner = new ContainerRuntime();
        barInner.WidthUnits = DimensionUnitType.RelativeToParent;
        barInner.Width = -(Theme.PanelPadding * 2);
        barInner.X = Theme.PanelPadding;
        barInner.HeightUnits = DimensionUnitType.RelativeToChildren;
        barInner.Height = 0;
        barInner.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        barInner.StackSpacing = Theme.ControlSpacing;
        bottomBar.Children.Add(barInner);

        var generateBtn = new Button();
        generateBtn.Text = "Generate";
        generateBtn.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        generateBtn.Visual.Width = 0;
        generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        generateBtn.Click += async (_, _) => await _mainViewModel.GenerateAsync();
        barInner.Children.Add(generateBtn.Visual);
        TooltipService.SetTooltip(generateBtn, "Generate bitmap font from current settings");

        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.IsFontLoaded))
                generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        };

        var autoRegenCb = new CheckBox();
        autoRegenCb.Text = "Auto-regenerate";
        autoRegenCb.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        autoRegenCb.Visual.Width = 0;
        autoRegenCb.Checked += (_, _) => _mainViewModel.AutoRegenerate = true;
        autoRegenCb.Unchecked += (_, _) => _mainViewModel.AutoRegenerate = false;
        barInner.Children.Add(autoRegenCb.Visual);
        TooltipService.SetTooltip(autoRegenCb, "Auto-regenerate on settings change");
    }

    private void OnRasterizerComboSelectionChanged()
    {
        if (_rasterizerCombo == null) return;
        var idx = _rasterizerCombo.SelectedIndex;
        if (idx >= 0 && idx < _fontConfig.AvailableBackends.Count)
            _fontConfig.SelectedBackend = _fontConfig.AvailableBackends[idx];
    }

    private void BuildAtlasSection(Expander expander)
    {
        bool updatingFromVm = false;

        var sizes = new[] { 128, 256, 512, 1024, 2048, 4096, 8192 };

        var atlasChecksGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(atlasChecksGrid);

        var forceSizeCheck = new CheckBox();
        forceSizeCheck.Text = "";
        forceSizeCheck.IsChecked = !_atlasConfig.AutofitTexture;
        TooltipService.SetTooltip(forceSizeCheck, "Use exact atlas dimensions instead of auto-fitting");
        atlasChecksGrid.AddRow("Force Size:", forceSizeCheck);

        var maxSizeGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(maxSizeGrid);

        // Size row: [combo] x [combo] as a horizontal stack in the right column
        var sizeControlRow = new StackPanel();
        sizeControlRow.Orientation = Orientation.Horizontal;
        sizeControlRow.Spacing = Theme.ControlSpacing;

        var maxWidthCombo = new ComboBox();
        maxWidthCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        maxWidthCombo.Width = 80;
        foreach (var s in sizes) maxWidthCombo.Items.Add(s.ToString());
        maxWidthCombo.SelectedIndex = Array.IndexOf(sizes, _atlasConfig.MaxWidth);
        if (maxWidthCombo.SelectedIndex < 0) maxWidthCombo.SelectedIndex = 3; // 1024
        maxWidthCombo.SelectionChanged += (_, _) =>
        {
            if (!updatingFromVm && maxWidthCombo.SelectedIndex >= 0)
                _atlasConfig.MaxWidth = sizes[maxWidthCombo.SelectedIndex];
        };
        sizeControlRow.AddChild(maxWidthCombo);

        var xLabel = new Label();
        xLabel.Text = "x";
        sizeControlRow.AddChild(xLabel);

        var maxHeightCombo = new ComboBox();
        maxHeightCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        maxHeightCombo.Width = 80;
        foreach (var s in sizes) maxHeightCombo.Items.Add(s.ToString());
        maxHeightCombo.SelectedIndex = Array.IndexOf(sizes, _atlasConfig.MaxHeight);
        if (maxHeightCombo.SelectedIndex < 0) maxHeightCombo.SelectedIndex = 3;
        maxHeightCombo.SelectionChanged += (_, _) =>
        {
            if (!updatingFromVm && maxHeightCombo.SelectedIndex >= 0)
                _atlasConfig.MaxHeight = sizes[maxHeightCombo.SelectedIndex];
        };
        sizeControlRow.AddChild(maxHeightCombo);

        var maxSizeRow = maxSizeGrid.AddRow("Size:", sizeControlRow);
        TooltipService.SetTooltip(sizeControlRow, "Atlas texture size in pixels");

        maxSizeRow.Visible = !_atlasConfig.AutofitTexture;

        forceSizeCheck.Checked += (_, _) =>
        {
            if (!updatingFromVm)
            {
                _atlasConfig.AutofitTexture = false;
                maxSizeRow.Visible = true;
            }
        };
        forceSizeCheck.Unchecked += (_, _) =>
        {
            if (!updatingFromVm)
            {
                _atlasConfig.AutofitTexture = true;
                maxSizeRow.Visible = false;
            }
        };

        var atlasGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };

        var packAlgoCombo = new ComboBox();
        packAlgoCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        packAlgoCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        packAlgoCombo.Visual.Width = 0;
        packAlgoCombo.Items.Add("MaxRects");
        packAlgoCombo.Items.Add("Skyline");
        packAlgoCombo.SelectedIndex = _atlasConfig.PackingAlgorithmIndex;
        packAlgoCombo.SelectionChanged += (_, _) =>
        {
            if (!updatingFromVm && packAlgoCombo.SelectedIndex >= 0)
                _atlasConfig.PackingAlgorithmIndex = packAlgoCombo.SelectedIndex;
        };
        atlasGrid.AddRow("Packing:", packAlgoCombo);
        TooltipService.SetTooltip(packAlgoCombo, "Glyph packing algorithm for the atlas");

        expander.AddContent(atlasGrid);

        // --- Padding and Spacing side by side ---
        var padSpaceRow = new ContainerRuntime();
        padSpaceRow.WidthUnits = DimensionUnitType.RelativeToParent;
        padSpaceRow.Width = 0;
        padSpaceRow.HeightUnits = DimensionUnitType.RelativeToChildren;
        padSpaceRow.Height = 0;
        padSpaceRow.ChildrenLayout = global::Gum.Managers.ChildrenLayout.LeftToRightStack;
        padSpaceRow.StackSpacing = 8;
        expander.AddContent(padSpaceRow);

        // --- Padding (cross layout using 3 columns) ---
        var padContainer = new ContainerRuntime();
        padContainer.WidthUnits = DimensionUnitType.RelativeToChildren;
        padContainer.Width = 0;
        padContainer.HeightUnits = DimensionUnitType.RelativeToChildren;
        padContainer.Height = 0;
        padContainer.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        padContainer.StackSpacing = 2;
        padSpaceRow.Children.Add(padContainer);

        var padHeader = new Label();
        padHeader.Text = "Padding";
        padContainer.Children.Add(padHeader.Visual);

        var padCross = new StackPanel();
        padCross.Orientation = Orientation.Horizontal;
        padCross.Spacing = 2;
        padCross.Visual.WidthUnits = DimensionUnitType.RelativeToChildren;
        padCross.Visual.Width = 0;
        padContainer.Children.Add(padCross.Visual);

        // Left column: empty, Left input, empty
        var padColLeft = new StackPanel();
        padColLeft.Spacing = 2;
        var padLeftSpacer = new Label(); padLeftSpacer.Text = ""; padLeftSpacer.Height = 24;
        padColLeft.AddChild(padLeftSpacer);
        var padLeftBox = CreateSmallIntBox(_atlasConfig.PaddingLeft, v => { if (!updatingFromVm) _atlasConfig.PaddingLeft = Math.Clamp(v, 0, 32); });
        padColLeft.AddChild(padLeftBox);
        var padLeftSpacer2 = new Label(); padLeftSpacer2.Text = ""; padLeftSpacer2.Height = 24;
        padColLeft.AddChild(padLeftSpacer2);
        padCross.AddChild(padColLeft);

        // Center column: Up input, "pad" label, Down input
        var padColCenter = new StackPanel();
        padColCenter.Spacing = 2;
        var padUpBox = CreateSmallIntBox(_atlasConfig.PaddingUp, v => { if (!updatingFromVm) _atlasConfig.PaddingUp = Math.Clamp(v, 0, 32); });
        padColCenter.AddChild(padUpBox);
        var padCenterLabel = new Label(); padCenterLabel.Text = "Aa";
        padColCenter.AddChild(padCenterLabel);
        var padDownBox = CreateSmallIntBox(_atlasConfig.PaddingDown, v => { if (!updatingFromVm) _atlasConfig.PaddingDown = Math.Clamp(v, 0, 32); });
        padColCenter.AddChild(padDownBox);
        padCross.AddChild(padColCenter);

        // Right column: empty, Right input, empty
        var padColRight = new StackPanel();
        padColRight.Spacing = 2;
        var padRightSpacer = new Label(); padRightSpacer.Text = ""; padRightSpacer.Height = 24;
        padColRight.AddChild(padRightSpacer);
        var padRightBox = CreateSmallIntBox(_atlasConfig.PaddingRight, v => { if (!updatingFromVm) _atlasConfig.PaddingRight = Math.Clamp(v, 0, 32); });
        padColRight.AddChild(padRightBox);
        var padRightSpacer2 = new Label(); padRightSpacer2.Text = ""; padRightSpacer2.Height = 24;
        padColRight.AddChild(padRightSpacer2);
        padCross.AddChild(padColRight);

        // --- Spacing (simple H x V) ---
        var spaceContainer = new ContainerRuntime();
        spaceContainer.WidthUnits = DimensionUnitType.RelativeToChildren;
        spaceContainer.Width = 0;
        spaceContainer.HeightUnits = DimensionUnitType.RelativeToChildren;
        spaceContainer.Height = 0;
        spaceContainer.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        spaceContainer.StackSpacing = 2;
        padSpaceRow.Children.Add(spaceContainer);

        var spaceHeader = new Label();
        spaceHeader.Text = "Spacing";
        spaceContainer.Children.Add(spaceHeader.Visual);

        var hRow = new StackPanel();
        hRow.Orientation = Orientation.Horizontal;
        hRow.Spacing = Theme.ControlSpacing;
        spaceContainer.Children.Add(hRow.Visual);

        var spcHLabel = new Label(); spcHLabel.Text = "H:";
        spcHLabel.Visual.YOrigin = global::RenderingLibrary.Graphics.VerticalAlignment.Center;
        spcHLabel.Visual.Y = 0;
        spcHLabel.Visual.YUnits = global::Gum.Converters.GeneralUnitType.PixelsFromMiddle;
        hRow.AddChild(spcHLabel);
        var spacingHBox = CreateSmallIntBox(_atlasConfig.SpacingH, v => { if (!updatingFromVm) _atlasConfig.SpacingH = Math.Clamp(v, 0, 32); });
        hRow.AddChild(spacingHBox);

        var vRow = new StackPanel();
        vRow.Orientation = Orientation.Horizontal;
        vRow.Spacing = Theme.ControlSpacing;
        spaceContainer.Children.Add(vRow.Visual);

        var spcVLabel = new Label(); spcVLabel.Text = "V:";
        spcVLabel.Visual.YOrigin = global::RenderingLibrary.Graphics.VerticalAlignment.Center;
        spcVLabel.Visual.Y = 0;
        spcVLabel.Visual.YUnits = global::Gum.Converters.GeneralUnitType.PixelsFromMiddle;
        vRow.AddChild(spcVLabel);
        var spacingVBox = CreateSmallIntBox(_atlasConfig.SpacingV, v => { if (!updatingFromVm) _atlasConfig.SpacingV = Math.Clamp(v, 0, 32); });
        vRow.AddChild(spacingVBox);

        // Sync UI from ViewModel when preset is applied
        _atlasConfig.PropertyChanged += (_, e) =>
        {
            updatingFromVm = true;
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(AtlasConfigViewModel.MaxWidth):
                        var wi = Array.IndexOf(sizes, _atlasConfig.MaxWidth);
                        if (wi >= 0) maxWidthCombo.SelectedIndex = wi;
                        break;
                    case nameof(AtlasConfigViewModel.MaxHeight):
                        var hi = Array.IndexOf(sizes, _atlasConfig.MaxHeight);
                        if (hi >= 0) maxHeightCombo.SelectedIndex = hi;
                        break;
                    case nameof(AtlasConfigViewModel.AutofitTexture):
                        forceSizeCheck.IsChecked = !_atlasConfig.AutofitTexture;
                        maxSizeRow.Visible = !_atlasConfig.AutofitTexture;
                        break;
                    case nameof(AtlasConfigViewModel.PackingAlgorithmIndex): packAlgoCombo.SelectedIndex = _atlasConfig.PackingAlgorithmIndex; break;
                    case nameof(AtlasConfigViewModel.PaddingUp): padUpBox.Text = _atlasConfig.PaddingUp.ToString(); break;
                    case nameof(AtlasConfigViewModel.PaddingRight): padRightBox.Text = _atlasConfig.PaddingRight.ToString(); break;
                    case nameof(AtlasConfigViewModel.PaddingDown): padDownBox.Text = _atlasConfig.PaddingDown.ToString(); break;
                    case nameof(AtlasConfigViewModel.PaddingLeft): padLeftBox.Text = _atlasConfig.PaddingLeft.ToString(); break;
                    case nameof(AtlasConfigViewModel.SpacingH): spacingHBox.Text = _atlasConfig.SpacingH.ToString(); break;
                    case nameof(AtlasConfigViewModel.SpacingV): spacingVBox.Text = _atlasConfig.SpacingV.ToString(); break;
                }
            }
            finally { updatingFromVm = false; }
        };
    }

    private void BuildOutputSection(Expander expander)
    {
        var outputGrid = new PropertyGridVisual { AlternatingRowColorsEnabled = false };
        expander.AddContent(outputGrid);

        var formats = new[] { ("Text", OutputFormat.Text), ("XML", OutputFormat.Xml), ("Binary", OutputFormat.Binary) };
        var formatCombo = new ComboBox();
        formatCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        formatCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        formatCombo.Visual.Width = 0;
        foreach (var (name, _) in formats) formatCombo.Items.Add(name);
        formatCombo.SelectedIndex = Array.FindIndex(formats, f => f.Item2 == _atlasConfig.DescriptorFormat);
        if (formatCombo.SelectedIndex < 0) formatCombo.SelectedIndex = 0;
        formatCombo.SelectionChanged += (_, _) =>
        {
            if (formatCombo.SelectedIndex >= 0)
                _atlasConfig.DescriptorFormat = formats[formatCombo.SelectedIndex].Item2;
        };
        outputGrid.AddRow("Format:", formatCombo);
        TooltipService.SetTooltip(formatCombo, "Output format: Text, XML, or Binary");

        var kerningCb = new CheckBox();
        kerningCb.Text = "";
        kerningCb.IsChecked = _atlasConfig.IncludeKerning;
        TooltipService.SetTooltip(kerningCb, "Include kerning pairs for better spacing");
        kerningCb.Checked += (_, _) => _atlasConfig.IncludeKerning = true;
        kerningCb.Unchecked += (_, _) => _atlasConfig.IncludeKerning = false;
        outputGrid.AddRow("Kerning:", kerningCb);

        _atlasConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AtlasConfigViewModel.DescriptorFormat))
            {
                var idx = Array.FindIndex(formats, f => f.Item2 == _atlasConfig.DescriptorFormat);
                if (idx >= 0) formatCombo.SelectedIndex = idx;
            }
            if (e.PropertyName == nameof(AtlasConfigViewModel.IncludeKerning))
                kerningCb.IsChecked = _atlasConfig.IncludeKerning;
        };
    }

    /// <summary>
    /// Removes the 2px border margins from the ScrollViewer's clip container
    /// by injecting zero-margin variables into all states so the state system
    /// applies them rather than resetting to defaults.
    /// </summary>
    internal static void StripScrollViewerMargins(global::Gum.Forms.DefaultVisuals.V3.ScrollViewerVisual visual)
    {
        // Set the values directly
        visual.ClipContainerInstance.X = 0;
        visual.ClipContainerInstance.Width = 0;
        visual.ClipContainerInstance.Y = 0;
        visual.ClipContainerInstance.Height = 0;

        // Also inject into all states so state application doesn't reset them
        var marginVars = new (string Name, object Value)[]
        {
            ("ClipContainerInstance.X", 0f),
            ("ClipContainerInstance.Y", 0f),
            ("ClipContainerInstance.Width", 0f),
            ("ClipContainerInstance.Height", 0f),
        };

        // Add to the Enabled and Focused states
        foreach (var state in new[] { visual.States.Enabled, visual.States.Focused })
        {
            foreach (var (name, value) in marginVars)
            {
                state.Variables.Add(new global::Gum.DataTypes.Variables.VariableSave
                {
                    Name = name,
                    Value = value
                });
            }
        }
    }

    private static TextBox CreateSmallIntBox(int initialValue, Action<int> onChanged)
    {
        var box = new TextBox();
        box.Width = 36;
        box.Height = 24;
        box.Text = initialValue.ToString();
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var val))
                onChanged(val);
        };
        return box;
    }

}
