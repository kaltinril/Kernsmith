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
        inner.Y = Theme.ControlSpacing;
        inner.ChildrenLayout = global::Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.StackSpacing = Theme.SectionSpacing;
        scrollViewer.InnerPanel.Children.Add(inner);

        var stack = inner;

        // --- FONT FILE section (expander) ---
        var fontFileExpander = new Expander();
        fontFileExpander.Header = "FONT FILE";
        fontFileExpander.IsExpanded = true;
        stack.Children.Add(fontFileExpander.Visual);
        {
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

            var sourceLabel = new Label();
            sourceLabel.Text = "";
            sourceLabel.IsVisible = false;
            fontFileExpander.AddContent(sourceLabel);

            // Only show source label for file-loaded fonts (system font is visible in dropdown)
            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.FontSourceKind) ||
                    e.PropertyName == nameof(FontConfigViewModel.FontSourceDescription))
                {
                    var isFile = _fontConfig.FontSourceKind == Models.FontSourceKind.File;
                    sourceLabel.IsVisible = isFile;
                    if (isFile)
                        sourceLabel.Text = _fontConfig.FontSourceDescription;
                }
            };

            // --- TTC Face Selection (hidden unless a .ttc font collection is loaded) ---
            var faceSelectionRow = new StackPanel();
            faceSelectionRow.Orientation = Orientation.Horizontal;
            faceSelectionRow.Spacing = Theme.ControlSpacing;
            faceSelectionRow.IsVisible = false;
            fontFileExpander.AddContent(faceSelectionRow);

            var faceLabel = new Label();
            faceLabel.Text = "Face:";
            faceLabel.Width = 50;
            faceSelectionRow.AddChild(faceLabel);

            var faceCombo = new ComboBox();
            faceCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
            faceCombo.Width = 80;
            faceSelectionRow.AddChild(faceCombo);

            var faceCountLabel = new Label();
            faceCountLabel.Text = "";
            faceSelectionRow.AddChild(faceCountLabel);

            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.IsFontCollection))
                {
                    faceSelectionRow.IsVisible = _fontConfig.IsFontCollection;
                    if (_fontConfig.IsFontCollection)
                    {
                        faceCombo.Items.Clear();
                        for (int i = 0; i < _fontConfig.FaceCount; i++)
                            faceCombo.Items.Add($"Face {i}");
                        faceCombo.SelectedIndex = 0;
                        faceCountLabel.Text = $"of {_fontConfig.FaceCount}";
                    }
                }
            };

            faceCombo.SelectionChanged += (_, _) =>
            {
                if (faceCombo.SelectedIndex >= 0 && _fontConfig.IsFontCollection)
                {
                    _fontConfig.ReloadWithFaceIndex(faceCombo.SelectedIndex);
                }
            };

            var familyLabel = new Label();
            familyLabel.Text = "System Font:";
            fontFileExpander.AddContent(familyLabel);
            TooltipService.SetTooltip(familyLabel, "Pick an installed system font. Requires a backend that supports system fonts (GDI or DirectWrite).");

            var familyCombo = new ComboBox();
            familyCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
            familyCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
            familyCombo.Visual.Width = 0;
            fontFileExpander.AddContent(familyCombo);

            // Wire system font combo
            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.SystemFonts) && _fontConfig.SystemFonts != null)
                {
                    familyCombo.Items = _fontConfig.SystemFonts.Select(g => (object)g.FamilyName).ToList();
                }

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

                    // Auto-load Regular, falling back to first available
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

            // --- Glyph count via PropertyGridVisual ---
            var fontInfoGrid = new PropertyGridVisual();

            var glyphCount = new Label();
            glyphCount.Text = "0";
            glyphCount.SetBinding(nameof(Label.Text), nameof(FontConfigViewModel.NumGlyphs));
            glyphCount.Visual.BindingContext = _fontConfig;
            fontInfoGrid.AddRow("Glyphs:", glyphCount);

            fontFileExpander.AddContent(fontInfoGrid);

            // --- Conditional font info: color glyphs ---
            var colorGlyphLabel = new Label();
            colorGlyphLabel.Text = "Has color glyphs";
            colorGlyphLabel.IsVisible = false;
            fontFileExpander.AddContent(colorGlyphLabel);

            // --- Conditional font info: variable font axes ---
            var axesLabel = new Label();
            axesLabel.Text = "";
            axesLabel.IsVisible = false;
            fontFileExpander.AddContent(axesLabel);

            _fontConfig.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FontConfigViewModel.HasColorGlyphs))
                    colorGlyphLabel.IsVisible = _fontConfig.HasColorGlyphs;

                if (e.PropertyName is nameof(FontConfigViewModel.HasVariationAxes)
                    or nameof(FontConfigViewModel.VariationAxesSummary))
                {
                    axesLabel.IsVisible = _fontConfig.HasVariationAxes;
                    if (_fontConfig.HasVariationAxes)
                        axesLabel.Text = $"Variable axes: {_fontConfig.VariationAxesSummary}";
                }
            };
        }

        // --- SIZE section (expander) ---
        var sizeExpander = new Expander();
        sizeExpander.Header = "SIZE";
        sizeExpander.IsExpanded = true;
        stack.Children.Add(sizeExpander.Visual);
        {
            var sizeGrid = new PropertyGridVisual();

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
        var atlasExpander = new Expander();
        atlasExpander.Header = "ATLAS";
        atlasExpander.IsExpanded = true;
        stack.Children.Add(atlasExpander.Visual);
        BuildAtlasSection(atlasExpander);

        // --- OUTPUT section (expander) ---
        var outputExpander = new Expander();
        outputExpander.Header = "OUTPUT";
        outputExpander.IsExpanded = true;
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

        var forceSizeCheck = new CheckBox();
        forceSizeCheck.Text = "Force Size";
        forceSizeCheck.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        forceSizeCheck.Visual.Width = 0;
        forceSizeCheck.IsChecked = !_atlasConfig.AutofitTexture;
        TooltipService.SetTooltip(forceSizeCheck, "Use exact atlas dimensions instead of auto-fitting");
        expander.AddContent(forceSizeCheck);

        var maxSizeGrid = new Grid();
        maxSizeGrid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        maxSizeGrid.Visual.Width = 0;
        maxSizeGrid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        maxSizeGrid.Visual.Height = 0;
        maxSizeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        maxSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        maxSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        maxSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        maxSizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        expander.AddContent(maxSizeGrid);

        var sizeLabel = new Label();
        sizeLabel.Text = "Size:";
        maxSizeGrid.AddChild(sizeLabel, row: 0, column: 0);
        TooltipService.SetTooltip(sizeLabel, "Atlas texture size in pixels");

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
        maxSizeGrid.AddChild(maxWidthCombo, row: 0, column: 1);

        var xLabel = new Label();
        xLabel.Text = "x";
        maxSizeGrid.AddChild(xLabel, row: 0, column: 2);

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
        maxSizeGrid.AddChild(maxHeightCombo, row: 0, column: 3);

        maxSizeGrid.Visual.Visible = !_atlasConfig.AutofitTexture;

        forceSizeCheck.Checked += (_, _) =>
        {
            if (!updatingFromVm)
            {
                _atlasConfig.AutofitTexture = false;
                maxSizeGrid.Visual.Visible = true;
            }
        };
        forceSizeCheck.Unchecked += (_, _) =>
        {
            if (!updatingFromVm)
            {
                _atlasConfig.AutofitTexture = true;
                maxSizeGrid.Visual.Visible = false;
            }
        };

        var atlasGrid = new PropertyGridVisual();

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

        var padHeader = new TextRuntime();
        padHeader.Text = "Padding";
        padHeader.WidthUnits = DimensionUnitType.RelativeToParent;
        padHeader.Width = 0;
        padHeader.HeightUnits = DimensionUnitType.Absolute;
        padHeader.Height = 20;
        padContainer.Children.Add(padHeader);

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

        var spaceHeader = new TextRuntime();
        spaceHeader.Text = "Spacing";
        spaceHeader.WidthUnits = DimensionUnitType.RelativeToParent;
        spaceHeader.Width = 0;
        spaceHeader.HeightUnits = DimensionUnitType.Absolute;
        spaceHeader.Height = 20;
        spaceContainer.Children.Add(spaceHeader);

        var spacingGrid = new Grid();
        spacingGrid.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        spacingGrid.Visual.Width = 0;
        spacingGrid.Visual.HeightUnits = DimensionUnitType.RelativeToChildren;
        spacingGrid.Visual.Height = 0;
        spacingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        spacingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        spacingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        spacingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        spaceContainer.Children.Add(spacingGrid.Visual);

        var spcHLabel = new Label(); spcHLabel.Text = "H:";
        spacingGrid.AddChild(spcHLabel, row: 0, column: 0);
        var spacingHBox = CreateSmallIntBox(_atlasConfig.SpacingH, v => { if (!updatingFromVm) _atlasConfig.SpacingH = Math.Clamp(v, 0, 32); });
        spacingGrid.AddChild(spacingHBox, row: 0, column: 1);
        var spcVLabel = new Label(); spcVLabel.Text = "V:";
        spacingGrid.AddChild(spcVLabel, row: 1, column: 0);
        var spacingVBox = CreateSmallIntBox(_atlasConfig.SpacingV, v => { if (!updatingFromVm) _atlasConfig.SpacingV = Math.Clamp(v, 0, 32); });
        spacingGrid.AddChild(spacingVBox, row: 1, column: 1);

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
                        maxSizeGrid.Visual.Visible = !_atlasConfig.AutofitTexture;
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
        var formatLabel = new Label();
        formatLabel.Text = "Descriptor Format:";
        expander.AddContent(formatLabel);
        TooltipService.SetTooltip(formatLabel, "Output format: Text, XML, or Binary");

        var formatGroup = new StackPanel();
        formatGroup.Spacing = Theme.ControlSpacing;
        expander.AddContent(formatGroup);

        var formatRadios = new List<(RadioButton rb, OutputFormat fmt)>();
        var formats = new[] { ("Text", OutputFormat.Text), ("XML", OutputFormat.Xml), ("Binary", OutputFormat.Binary) };
        foreach (var (name, format) in formats)
        {
            var rb = new RadioButton();
            rb.Text = name;
            rb.Width = 200;
            if (format == _atlasConfig.DescriptorFormat) rb.IsChecked = true;
            var capturedFormat = format;
            rb.Checked += (_, _) => _atlasConfig.DescriptorFormat = capturedFormat;
            formatRadios.Add((rb, format));
            formatGroup.AddChild(rb);
        }

        var kerningCb = new CheckBox();
        kerningCb.Text = "Include Kerning";
        kerningCb.Width = 200;
        kerningCb.IsChecked = _atlasConfig.IncludeKerning;
        TooltipService.SetTooltip(kerningCb, "Include kerning pairs for better spacing");
        kerningCb.Checked += (_, _) => _atlasConfig.IncludeKerning = true;
        kerningCb.Unchecked += (_, _) => _atlasConfig.IncludeKerning = false;
        expander.AddContent(kerningCb);

        _atlasConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AtlasConfigViewModel.DescriptorFormat))
                foreach (var (rb, fmt) in formatRadios)
                    rb.IsChecked = fmt == _atlasConfig.DescriptorFormat;
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
