using Gum.DataTypes;
using Gum.Forms;
using Gum.Forms.Controls;
using KernSmith.Ui.Styling;
using KernSmith.Ui.ViewModels;
using Microsoft.Xna.Framework.Graphics;
using MonoGameGum;
using MonoGameGum.GueDeriving;

namespace KernSmith.Ui.Layout;

public class MainLayout : ContainerRuntime
{
    private readonly MainViewModel _viewModel;
    private readonly GraphicsDevice _graphicsDevice;

    public MainLayout(MainViewModel viewModel, GraphicsDevice graphicsDevice)
    {
        _viewModel = viewModel;
        _graphicsDevice = graphicsDevice;

        this.Dock(Gum.Wireframe.Dock.Fill);

        CreateMenu();
        CreateBody();
        CreateStatusBar();
    }

    private void CreateMenu()
    {
        var menu = new Menu();

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

        var resetItem = new MenuItem();
        resetItem.Header = "Reset Layout";
        resetItem.Clicked += (_, _) => _viewModel.ResetLayout();
        viewItem.Items!.Add(resetItem);

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
        var body = new StackPanel();
        body.Orientation = Orientation.Horizontal;
        body.Y = 40; // below menu
        body.WidthUnits = DimensionUnitType.RelativeToParent;
        body.Width = 0;
        body.HeightUnits = DimensionUnitType.RelativeToParent;
        body.Height = -64; // minus menu (~40) and status bar (~24)
        this.AddChild(body);

        // Left column: font config (fixed width)
        var fontConfigPanel = new FontConfigPanel(_viewModel, _viewModel.FontConfig, _viewModel.AtlasConfig);
        fontConfigPanel.Width = 280;
        fontConfigPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        fontConfigPanel.Height = 0;
        AddPanelBackground(fontConfigPanel, Theme.Panel);
        body.AddChild(fontConfigPanel);

        // Splitter between left and center
        var leftSplitter = new Splitter();
        leftSplitter.Width = 5;
        leftSplitter.Dock(Gum.Wireframe.Dock.FillVertically);
        AddSplitterBackground(leftSplitter);
        body.AddChild(leftSplitter);

        // Center column: preview (fills remaining)
        var previewPanel = new PreviewPanel(_viewModel.Preview, _viewModel.CharacterGrid, _graphicsDevice);
        previewPanel.WidthUnits = DimensionUnitType.Ratio;
        previewPanel.Width = 1;
        previewPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        previewPanel.Height = 0;
        body.AddChild(previewPanel);

        // Splitter between center and right
        var rightSplitter = new Splitter();
        rightSplitter.Width = 5;
        rightSplitter.Dock(Gum.Wireframe.Dock.FillVertically);
        AddSplitterBackground(rightSplitter);
        body.AddChild(rightSplitter);

        // Right column: effects (fixed width)
        var effectsPanel = new EffectsPanel(_viewModel.Effects, _viewModel.AtlasConfig);
        effectsPanel.Width = 280;
        effectsPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        effectsPanel.Height = 0;
        AddPanelBackground(effectsPanel, Theme.Panel);
        body.AddChild(effectsPanel);
    }

    private void CreateStatusBar()
    {
        var statusBar = new StatusBar(_viewModel.StatusBar);
        statusBar.Dock(Gum.Wireframe.Dock.Bottom);
        statusBar.Height = 24;
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

    private static void AddSplitterBackground(Splitter splitter)
    {
        var bg = new ColoredRectangleRuntime();
        bg.Color = Theme.PanelBorder;
        bg.Dock(Gum.Wireframe.Dock.Fill);
        splitter.Visual.Children.Insert(0, bg);
    }

    private static void AddPanelBackground(Panel panel, Microsoft.Xna.Framework.Color color)
    {
        var bg = new ColoredRectangleRuntime();
        bg.Color = color;
        bg.Dock(Gum.Wireframe.Dock.Fill);
        // Insert as first child so it renders behind other content
        panel.Visual.Children.Insert(0, bg);
    }

    private void ShowAboutDialog()
    {
        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
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

        var version = typeof(MainLayout).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
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
        okButton.Anchor(Gum.Wireframe.Anchor.Bottom);
        okButton.Y = -10;
        okButton.Width = 80;
        window.AddChild(okButton.Visual);
        okButton.Click += (_, _) => window.RemoveFromRoot();
    }
}
