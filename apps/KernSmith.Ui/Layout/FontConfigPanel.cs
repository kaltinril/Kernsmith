using System.Linq;
using Gum.DataTypes;
using Gum.Forms.Controls;
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
        var scrollViewer = new ScrollViewer();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        scrollViewer.Dock(Gum.Wireframe.Dock.Fill);
        this.AddChild(scrollViewer);

        // Inner container with padding from panel edges
        var inner = new ContainerRuntime();
        inner.WidthUnits = DimensionUnitType.RelativeToParent;
        inner.HeightUnits = DimensionUnitType.RelativeToChildren;
        inner.Width = -16; // 8px padding each side
        inner.Height = 0;
        inner.X = 8;
        inner.Y = 4;
        inner.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        inner.StackSpacing = 6;
        scrollViewer.InnerPanel.Children.Add(inner);
        var stack = inner;

        // --- FONT SOURCE section ---
        AddSectionHeader(stack, "FONT FILE");

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
        stack.Children.Add(browseBtn.Visual);
        TooltipService.SetTooltip(browseBtn, "Browse for a font file");

        var sourceLabel = new Label();
        sourceLabel.Text = "";
        sourceLabel.IsVisible = false;
        stack.Children.Add(sourceLabel.Visual);

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
        faceSelectionRow.Spacing = 4;
        faceSelectionRow.IsVisible = false;
        stack.Children.Add(faceSelectionRow.Visual);

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

        AddDivider(stack);

        var familyLabel = new Label();
        familyLabel.Text = "System Font:";
        stack.Children.Add(familyLabel.Visual);
        TooltipService.SetTooltip(familyLabel, "Pick an installed system font. Requires a backend that supports system fonts (GDI or DirectWrite).");

        var familyCombo = new ComboBox();
        familyCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        familyCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        familyCombo.Visual.Width = 0;
        stack.Children.Add(familyCombo.Visual);

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

        // --- Glyph count (only useful non-redundant info) ---
        var glyphRow = new StackPanel();
        glyphRow.Orientation = Orientation.Horizontal;
        glyphRow.Spacing = 4;
        stack.Children.Add(glyphRow.Visual);

        var glyphLbl = new Label();
        glyphLbl.Text = "Glyphs in font:";
        glyphRow.AddChild(glyphLbl);

        var glyphCount = new Label();
        glyphCount.Text = "0";
        glyphCount.SetBinding(nameof(Label.Text), nameof(FontConfigViewModel.NumGlyphs));
        glyphCount.Visual.BindingContext = _fontConfig;
        glyphRow.AddChild(glyphCount);

        // --- Conditional font info: color glyphs ---
        var colorGlyphLabel = new Label();
        colorGlyphLabel.Text = "Has color glyphs";
        colorGlyphLabel.IsVisible = false;
        stack.Children.Add(colorGlyphLabel.Visual);

        // --- Conditional font info: variable font axes ---
        var axesLabel = new Label();
        axesLabel.Text = "";
        axesLabel.IsVisible = false;
        stack.Children.Add(axesLabel.Visual);

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

        AddDivider(stack);

        // --- SIZE section ---
        AddSectionHeader(stack, "SIZE");

        var sizeRow = new StackPanel();
        sizeRow.Orientation = Orientation.Horizontal;
        sizeRow.Spacing = 4;
        stack.Children.Add(sizeRow.Visual);

        var sizeLabel = new Label();
        sizeLabel.Text = "Font Size:";
        sizeRow.AddChild(sizeLabel);
        TooltipService.SetTooltip(sizeLabel, "Font size in points (4-500)");

        var sizeTextBox = new TextBox();
        sizeTextBox.Width = 42;
        sizeTextBox.Text = "32";
        sizeTextBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(sizeTextBox.Text, out var size))
                _fontConfig.FontSize = Math.Clamp(size, 4, 500);
        };
        sizeRow.AddChild(sizeTextBox);

        var ptLabel = new Label();
        ptLabel.Text = "pt";
        sizeRow.AddChild(ptLabel);

        // --- RASTERIZER section ---
        var rasterizerLabel = new Label();
        rasterizerLabel.Text = "Rasterizer:";
        stack.Children.Add(rasterizerLabel.Visual);
        TooltipService.SetTooltip(rasterizerLabel, "Glyph rasterizer backend. FreeType: cross-platform default. GDI: Windows-only, matches BMFont output. DirectWrite: Windows-only, modern rendering with color/variable font support.");

        _rasterizerCombo = new ComboBox();
        _rasterizerCombo.ListBox.InnerPanel.UseFixedStackChildrenSize = true;
        _rasterizerCombo.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        _rasterizerCombo.Visual.Width = 0;
        foreach (var backend in _fontConfig.AvailableBackends)
            _rasterizerCombo.Items.Add(backend.ToString());
        _rasterizerCombo.SelectedIndex = _fontConfig.AvailableBackends.ToList().IndexOf(_fontConfig.SelectedBackend);
        if (_rasterizerCombo.SelectedIndex < 0) _rasterizerCombo.SelectedIndex = 0;
        _rasterizerCombo.SelectionChanged += (_, _) => OnRasterizerComboSelectionChanged();
        stack.Children.Add(_rasterizerCombo.Visual);

        AddDivider(stack);

        // --- GENERATE button (primary action, visually distinct) ---
        var generateBtnSpacer = new ColoredRectangleRuntime();
        generateBtnSpacer.Width = 0;
        generateBtnSpacer.WidthUnits = DimensionUnitType.RelativeToParent;
        generateBtnSpacer.Height = 4;
        generateBtnSpacer.Color = Microsoft.Xna.Framework.Color.Transparent;
        stack.Children.Add(generateBtnSpacer);

        var generateBtn = new Button();
        generateBtn.Text = "Generate";
        generateBtn.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        generateBtn.Visual.Width = 0;
        generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        generateBtn.Click += async (_, _) => await _mainViewModel.GenerateAsync();
        stack.Children.Add(generateBtn.Visual);
        TooltipService.SetTooltip(generateBtn, "Generate bitmap font from current settings");

        // Enable/disable Generate button based on font loaded state
        _fontConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontConfigViewModel.IsFontLoaded))
                generateBtn.IsEnabled = _fontConfig.IsFontLoaded;
        };

        // Auto-regenerate toggle
        var autoRegenCb = new CheckBox();
        autoRegenCb.Text = "Auto-regenerate";
        autoRegenCb.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        autoRegenCb.Visual.Width = 0;
        autoRegenCb.Checked += (_, _) => _mainViewModel.AutoRegenerate = true;
        autoRegenCb.Unchecked += (_, _) => _mainViewModel.AutoRegenerate = false;
        stack.Children.Add(autoRegenCb.Visual);
        TooltipService.SetTooltip(autoRegenCb, "Auto-regenerate on settings change");

        // --- ATLAS section ---
        AddDivider(stack);
        AddSectionHeader(stack, "ATLAS");
        BuildAtlasSection(stack);

        // --- OUTPUT section ---
        AddDivider(stack);
        AddSectionHeader(stack, "OUTPUT");
        BuildOutputSection(stack);
    }

    private void OnRasterizerComboSelectionChanged()
    {
        if (_rasterizerCombo == null) return;
        var idx = _rasterizerCombo.SelectedIndex;
        if (idx >= 0 && idx < _fontConfig.AvailableBackends.Count)
            _fontConfig.SelectedBackend = _fontConfig.AvailableBackends[idx];
    }

    private static void AddSectionHeader(Gum.Wireframe.GraphicalUiElement parent, string text)
    {
        var container = new ContainerRuntime();
        container.Width = 0;
        container.WidthUnits = DimensionUnitType.RelativeToParent;
        container.Height = 22;
        container.HeightUnits = DimensionUnitType.Absolute;
        parent.Children.Add(container);

        var bg = new ColoredRectangleRuntime();
        bg.Width = 0;
        bg.WidthUnits = DimensionUnitType.RelativeToParent;
        bg.Height = 0;
        bg.HeightUnits = DimensionUnitType.RelativeToParent;
        bg.Color = new Microsoft.Xna.Framework.Color(50, 50, 55);
        container.Children.Add(bg);

        var header = new TextRuntime();
        header.Text = text;
        header.Color = Theme.Accent;
        header.X = 6;
        header.Y = 2;
        container.Children.Add(header);
    }

    private static void AddDivider(Gum.Wireframe.GraphicalUiElement parent)
    {
        var divider = new ColoredRectangleRuntime();
        divider.Width = 0;
        divider.WidthUnits = DimensionUnitType.RelativeToParent;
        divider.Height = 1;
        divider.Color = Theme.PanelBorder;
        parent.Children.Add(divider);
    }

    private void BuildAtlasSection(Gum.Wireframe.GraphicalUiElement stack)
    {
        bool updatingFromVm = false;

        var sizes = new[] { 128, 256, 512, 1024, 2048, 4096, 8192 };

        var forceSizeCheck = new CheckBox();
        forceSizeCheck.Text = "Force Size";
        forceSizeCheck.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        forceSizeCheck.Visual.Width = 0;
        forceSizeCheck.IsChecked = !_atlasConfig.AutofitTexture;
        TooltipService.SetTooltip(forceSizeCheck, "Use exact atlas dimensions instead of auto-fitting");
        stack.Children.Add(forceSizeCheck.Visual);

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
        stack.Children.Add(maxSizeGrid.Visual);

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

        var packAlgoLabel = new Label();
        packAlgoLabel.Text = "Packing Algorithm:";
        stack.Children.Add(packAlgoLabel.Visual);
        TooltipService.SetTooltip(packAlgoLabel, "Glyph packing algorithm for the atlas");

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
        stack.Children.Add(packAlgoCombo.Visual);

        // --- Padding and Spacing side by side ---
        var padSpaceRow = new ContainerRuntime();
        padSpaceRow.WidthUnits = DimensionUnitType.RelativeToParent;
        padSpaceRow.Width = 0;
        padSpaceRow.HeightUnits = DimensionUnitType.RelativeToChildren;
        padSpaceRow.Height = 0;
        padSpaceRow.ChildrenLayout = Gum.Managers.ChildrenLayout.LeftToRightStack;
        padSpaceRow.StackSpacing = 8;
        stack.Children.Add(padSpaceRow);

        // --- Padding (cross layout using 3 columns) ---
        var padContainer = new ContainerRuntime();
        padContainer.WidthUnits = DimensionUnitType.RelativeToChildren;
        padContainer.Width = 0;
        padContainer.HeightUnits = DimensionUnitType.RelativeToChildren;
        padContainer.Height = 0;
        padContainer.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        padContainer.StackSpacing = 2;
        padSpaceRow.Children.Add(padContainer);

        AddLabeledDivider(padContainer, "Padding");

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
        spaceContainer.ChildrenLayout = Gum.Managers.ChildrenLayout.TopToBottomStack;
        spaceContainer.StackSpacing = 2;
        padSpaceRow.Children.Add(spaceContainer);

        AddLabeledDivider(spaceContainer, "Spacing");

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

    private void BuildOutputSection(Gum.Wireframe.GraphicalUiElement stack)
    {
        var formatLabel = new Label();
        formatLabel.Text = "Descriptor Format:";
        stack.Children.Add(formatLabel.Visual);
        TooltipService.SetTooltip(formatLabel, "Output format: Text, XML, or Binary");

        var formatGroup = new StackPanel();
        formatGroup.Spacing = 2;
        stack.Children.Add(formatGroup.Visual);

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
        stack.Children.Add(kerningCb.Visual);

        _atlasConfig.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AtlasConfigViewModel.DescriptorFormat))
                foreach (var (rb, fmt) in formatRadios)
                    rb.IsChecked = fmt == _atlasConfig.DescriptorFormat;
            if (e.PropertyName == nameof(AtlasConfigViewModel.IncludeKerning))
                kerningCb.IsChecked = _atlasConfig.IncludeKerning;
        };
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

    private static void AddLabeledDivider(Gum.Wireframe.GraphicalUiElement parent, string label)
    {
        var text = new TextRuntime();
        text.Text = label;
        text.Color = Theme.Accent;
        parent.Children.Add(text);
    }
}
