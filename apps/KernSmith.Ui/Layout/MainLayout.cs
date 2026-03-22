using Gum.DataTypes;
using Gum.Forms;
using Gum.Forms.Controls;
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

        var saveAsItem = new MenuItem();
        saveAsItem.Header = "Save As...";
        saveAsItem.Clicked += (_, _) => _viewModel.SaveAs();
        fileItem.Items!.Add(saveAsItem);

        var exitItem = new MenuItem();
        exitItem.Header = "Exit";
        exitItem.Clicked += (_, _) => _viewModel.Exit();
        fileItem.Items!.Add(exitItem);

        menu.Items!.Add(fileItem);

        // Tools menu
        var toolsItem = new MenuItem();
        toolsItem.Header = "Tools";

        var generateItem = new MenuItem();
        generateItem.Header = "Generate";
        generateItem.Clicked += async (_, _) => await _viewModel.GenerateAsync();
        toolsItem.Items!.Add(generateItem);

        menu.Items!.Add(toolsItem);

        // Help menu
        var helpItem = new MenuItem();
        helpItem.Header = "Help";

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
        var fontConfigPanel = new FontConfigPanel(_viewModel, _viewModel.FontConfig);
        fontConfigPanel.Width = 280;
        fontConfigPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        fontConfigPanel.Height = 0;
        body.AddChild(fontConfigPanel);

        // Splitter between left and center
        var leftSplitter = new Splitter();
        leftSplitter.Width = 5;
        leftSplitter.Dock(Gum.Wireframe.Dock.FillVertically);
        body.AddChild(leftSplitter);

        // Center column: preview (fills remaining)
        var previewPanel = new PreviewPanel(_viewModel.Preview, _graphicsDevice);
        previewPanel.WidthUnits = DimensionUnitType.Ratio;
        previewPanel.Width = 1;
        previewPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        previewPanel.Height = 0;
        body.AddChild(previewPanel);

        // Splitter between center and right
        var rightSplitter = new Splitter();
        rightSplitter.Width = 5;
        rightSplitter.Dock(Gum.Wireframe.Dock.FillVertically);
        body.AddChild(rightSplitter);

        // Right column: effects placeholder (fixed width)
        var effectsPanel = new EffectsPanel();
        effectsPanel.Width = 240;
        effectsPanel.HeightUnits = DimensionUnitType.RelativeToParent;
        effectsPanel.Height = 0;
        body.AddChild(effectsPanel);
    }

    private void CreateStatusBar()
    {
        var statusBar = new StatusBar(_viewModel.StatusBar);
        statusBar.Dock(Gum.Wireframe.Dock.Bottom);
        statusBar.Height = 24;
        this.AddChild(statusBar);
    }

    private void ShowAboutDialog()
    {
        var window = new Window();
        window.Anchor(Gum.Wireframe.Anchor.Center);
        window.Width = 300;
        window.Height = 200;
        FrameworkElement.ModalRoot.AddChild(window);

        var contentStack = new StackPanel();
        contentStack.Spacing = 8;
        contentStack.Y = 24;
        window.AddChild(contentStack);

        var titleLabel = new Label();
        titleLabel.Text = "KernSmith";
        contentStack.AddChild(titleLabel);

        var versionLabel = new Label();
        versionLabel.Text = "Version 1.0.0";
        contentStack.AddChild(versionLabel);

        var descLabel = new Label();
        descLabel.Text = "Bitmap Font Generator";
        contentStack.AddChild(descLabel);

        var okButton = new Button();
        okButton.Text = "OK";
        okButton.Anchor(Gum.Wireframe.Anchor.Bottom);
        okButton.Y = -10;
        okButton.Width = 80;
        window.AddChild(okButton.Visual);
        okButton.Click += (_, _) => window.RemoveFromRoot();
    }
}
