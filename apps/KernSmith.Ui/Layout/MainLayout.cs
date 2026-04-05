using global::Gum.DataTypes;
using global::Gum.Forms;
using global::Gum.Forms.Controls;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework.Graphics;
using global::Gum.Wireframe;
using MonoGameGum;
using MonoGameGum.GueDeriving;
using global::Gum.Forms.DefaultVisuals.V3;

namespace KernSmith.Ui.Layout;

/// <summary>
/// Root GUM container that builds the application's three-column layout: font config (left),
/// preview with tab switching (center), and effects/atlas config (right), separated by
/// draggable splitters. Also creates the menu bar and status bar.
/// </summary>
public class MainLayout : ContainerRuntime
{
    private const float DefaultPanelWidth = 280;

    private readonly MainViewModel _viewModel;
    private readonly GraphicsDevice _graphicsDevice;
    private FontConfigPanel? _fontConfigPanel;
    private EffectsPanel? _effectsPanel;
    public PreviewPanel? Preview { get; private set; }

    public MainLayout(MainViewModel viewModel, GraphicsDevice graphicsDevice)
    {
        _viewModel = viewModel;
        _graphicsDevice = graphicsDevice;

        this.Dock(global::Gum.Wireframe.Dock.Fill);

        CreateMenu();
        CreateBody();
        CreateStatusBar();
    }

    private void CreateMenu()
    {
        var menu = new Menu();
        var visual = (MenuVisual)menu.Visual;
        visual.Height = 30;
        visual.HeightUnits = DimensionUnitType.Absolute;
        // separate out the menu items a little:
        visual.InnerPanelInstance.StackSpacing = 10;

        // File menu
        var fileItem = new MenuItem();
        fileItem.Header = "File";

        var openFontItem = new MenuItem();
        openFontItem.Header = "Open Font...";
        openFontItem.Clicked += (_, _) => _viewModel.OpenFont();
        fileItem.Items!.Add(openFontItem);

        var loadProjectItem = new MenuItem();
        loadProjectItem.Header = "Load Project...";
        loadProjectItem.Clicked += (_, _) => _viewModel.LoadProject();
        fileItem.Items!.Add(loadProjectItem);

        // Recent Fonts submenu
        var recentFontsItem = new MenuItem();
        recentFontsItem.Header = "Recent Fonts";
        PopulateRecentFonts(recentFontsItem);
        fileItem.Items!.Add(recentFontsItem);

        var saveProjectItem = new MenuItem();
        saveProjectItem.Header = "Save Project";
        saveProjectItem.Clicked += (_, _) => _viewModel.SaveProject();
        fileItem.Items!.Add(saveProjectItem);

        var saveAsItem = new MenuItem();
        saveAsItem.Header = "Export As...";
        saveAsItem.Clicked += (_, _) => _viewModel.SaveAs();
        fileItem.Items!.Add(saveAsItem);

        var exitItem = new MenuItem();
        exitItem.Header = "Exit";
        exitItem.Clicked += (_, _) => _viewModel.Exit();
        fileItem.Items!.Add(exitItem);

        menu.Items!.Add(fileItem);

        // View menu
        var viewItem = new MenuItem();
        viewItem.Header = "View";

        var resetLayoutItem = new MenuItem();
        resetLayoutItem.Header = "Reset Layout";
        resetLayoutItem.Clicked += (_, _) => ResetLayout();
        viewItem.Items!.Add(resetLayoutItem);

        menu.Items!.Add(viewItem);

        // Help menu
        var helpItem = new MenuItem();
        helpItem.Header = "Help";

        var shortcutsItem = new MenuItem();
        shortcutsItem.Header = "Keyboard Shortcuts";
        shortcutsItem.Clicked += (_, _) => KeyboardShortcutsDialog.Show();
        helpItem.Items!.Add(shortcutsItem);

        var inspectorItem = new MenuItem();
        inspectorItem.Header = "Font Inspector...";
        inspectorItem.Clicked += (_, _) =>
        {
            if (_viewModel.FontConfig.IsFontLoaded)
            {
                var dialog = new FontInspectorDialog(_viewModel.FontConfig);
                dialog.Show();
            }
            else
            {
                _viewModel.StatusBar.StatusText = "Load a font first to use Font Inspector";
            }
        };
        helpItem.Items!.Add(inspectorItem);

        var aboutItem = new MenuItem();
        aboutItem.Header = "About";
        aboutItem.Clicked += (_, _) => ShowAboutDialog();
        helpItem.Items!.Add(aboutItem);

        menu.Items!.Add(helpItem);

        this.AddChild(menu);
    }

    private void CreateBody()
    {
        // Horizontal StackPanel for 3-column layout with splitters
        // Positioned below menu (Y=30) and above status bar (Height = -54 = 30 menu + 24 status)
        var body = new StackPanel();
        body.Name = "Body";
        body.Orientation = Orientation.Horizontal;
        body.Y = 30;
        body.WidthUnits = DimensionUnitType.RelativeToParent;
        body.Width = 0;
        body.HeightUnits = DimensionUnitType.RelativeToParent;
        body.Height = -54;
        body.Visual.ClipsChildren = true;
        this.AddChild(body);

        // Left column: font config (fixed width)
        _fontConfigPanel = new FontConfigPanel(_viewModel, _viewModel.FontConfig, _viewModel.AtlasConfig);
        var fontConfigPanel = _fontConfigPanel;
        fontConfigPanel.Name = "FontConfigPanel";
        fontConfigPanel.Width = DefaultPanelWidth;
        fontConfigPanel.Visual.MinWidth = 210;
        fontConfigPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        fontConfigPanel.Height = 0;
        AddPanelBackground(fontConfigPanel, Theme.Panel);
        body.AddChild(fontConfigPanel);

        // Splitter between left and center
        var leftSplitter = new Splitter();
        leftSplitter.Name = "LeftSplitter";
        leftSplitter.Width = 5;
        leftSplitter.Dock(global::Gum.Wireframe.Dock.FillVertically);
        AddSplitterBackground(leftSplitter);
        body.AddChild(leftSplitter);

        // Double-click splitter to reset left panel to default width
        if (leftSplitter.Visual is InteractiveGue leftInteractive)
            leftInteractive.DoubleClick += (_, _) => fontConfigPanel.Width = DefaultPanelWidth;

        // Center column: preview (fills remaining)
        Preview = new PreviewPanel(_viewModel.Preview, _viewModel.CharacterGrid, _graphicsDevice);
        var previewPanel = Preview;
        previewPanel.Name = "PreviewPanel";
        previewPanel.WidthUnits = DimensionUnitType.Ratio;
        previewPanel.Width = 1;
        previewPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        previewPanel.Height = 0;
        body.AddChild(previewPanel);

        // Splitter between center and right
        var rightSplitter = new Splitter();
        rightSplitter.Name = "RightSplitter";
        rightSplitter.Width = 5;
        rightSplitter.Dock(global::Gum.Wireframe.Dock.FillVertically);
        AddSplitterBackground(rightSplitter);
        body.AddChild(rightSplitter);

        // Right column: effects (fixed width)
        _effectsPanel = new EffectsPanel(_viewModel.Effects, _graphicsDevice);
        var effectsPanel = _effectsPanel;
        effectsPanel.Name = "EffectsPanel";
        effectsPanel.Width = DefaultPanelWidth;
        effectsPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        effectsPanel.Height = 0;
        AddPanelBackground(effectsPanel, Theme.Panel);
        body.AddChild(effectsPanel);

        // Double-click splitter to reset right panel to default width
        if (rightSplitter.Visual is InteractiveGue rightInteractive)
            rightInteractive.DoubleClick += (_, _) => effectsPanel.Width = DefaultPanelWidth;
    }

    private void CreateStatusBar()
    {
        var statusBar = new StatusBar(_viewModel.StatusBar);
        // Pin to bottom of screen with explicit positioning
        statusBar.Visual.YUnits = global::Gum.Converters.GeneralUnitType.PixelsFromLarge;
        statusBar.Visual.YOrigin = RenderingLibrary.Graphics.VerticalAlignment.Bottom;
        statusBar.Visual.Y = 0;
        statusBar.Height = 24;
        statusBar.Visual.HeightUnits = DimensionUnitType.Absolute;
        statusBar.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        statusBar.Visual.Width = 0;
        this.AddChild(statusBar);
    }

    private void PopulateRecentFonts(MenuItem parent)
    {
        var recentFonts = _viewModel.SessionService.State.RecentFonts;
        if (recentFonts.Count == 0)
        {
            var emptyItem = new MenuItem();
            emptyItem.Header = "(none)";
            parent.Items!.Add(emptyItem);
            return;
        }

        foreach (var fontPath in recentFonts)
        {
            var item = new MenuItem();
            item.Header = Path.GetFileName(fontPath);
            var capturedPath = fontPath;
            item.Clicked += (_, _) => _viewModel.LoadFontFromPath(capturedPath);
            parent.Items!.Add(item);
        }
    }

    /// <summary>
    /// Resets both side panels to their default width.
    /// </summary>
    public void ResetLayout()
    {
        if (_fontConfigPanel != null)
            _fontConfigPanel.Width = DefaultPanelWidth;
        if (_effectsPanel != null)
            _effectsPanel.Width = DefaultPanelWidth;
    }

    private static void AddSplitterBackground(Splitter splitter)
    {
        if (splitter.Visual is global::Gum.Forms.DefaultVisuals.V3.SplitterVisual sv)
        {
            sv.Background.ApplyState(global::Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle.NineSlice.Solid);
            sv.BackgroundColor = Theme.PanelBorder;
        }
    }

    private static void AddPanelBackground(Panel panel, Microsoft.Xna.Framework.Color color)
    {
        var bg = new ColoredRectangleRuntime();
        bg.Color = color;
        bg.Dock(global::Gum.Wireframe.Dock.Fill);
        // Insert as first child so it renders behind other content
        panel.Visual.Children.Insert(0, bg);
    }

    private void ShowAboutDialog()
    {
        var window = new Window();
        window.Anchor(global::Gum.Wireframe.Anchor.Center);
        window.Width = 340;
        window.Height = 260;
        FrameworkElement.ModalRoot.AddChild(window);

        var contentStack = new StackPanel();
        contentStack.Spacing = 8;
        contentStack.Y = 24;
        window.AddChild(contentStack);

        var titleLabel = new Label();
        titleLabel.Text = "KernSmith";
        contentStack.AddChild(titleLabel);

        var version = typeof(MainLayout).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var versionLabel = new Label();
        versionLabel.Text = $"Version {version}";
        contentStack.AddChild(versionLabel);

        var descLabel = new Label();
        descLabel.Text = "Cross-platform bitmap font generator";
        contentStack.AddChild(descLabel);

        var creditsLabel = new Label();
        creditsLabel.Text = "Built with MonoGame + GUM UI";
        contentStack.AddChild(creditsLabel);

        var urlLabel = new Label();
        urlLabel.Text = "https://github.com/kaltinril/kernsmith";
        contentStack.AddChild(urlLabel);

        var okButton = new Button();
        okButton.Text = "OK";
        okButton.Anchor(global::Gum.Wireframe.Anchor.Bottom);
        okButton.Y = -10;
        okButton.Width = 80;
        window.AddChild(okButton.Visual);
        okButton.Click += (_, _) => window.RemoveFromRoot();
    }
}
