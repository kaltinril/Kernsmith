using Microsoft.Xna.Framework;
using MonoGameGum;
using Gum.Forms;
using KernSmith.Ui.Layout;
using KernSmith.Ui.ViewModels;
using KernSmith.Ui.Services;

namespace KernSmith.Ui;

public class KernSmithGame : Game
{
    private GraphicsDeviceManager _graphics;
    private MainLayout? _mainLayout;
    private MainViewModel? _mainViewModel;

    public KernSmithGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = 1280;
        _graphics.PreferredBackBufferHeight = 720;
        Window.Title = "KernSmith";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        GumService.Default.Initialize(this, DefaultVisualsVersion.V3);

        var fileDialogService = new FileDialogService();
        var fontDiscoveryService = new FontDiscoveryService();
        var generationService = new GenerationService();

        _mainViewModel = new MainViewModel(fileDialogService, fontDiscoveryService, generationService, this);
        _mainLayout = new MainLayout(_mainViewModel, GraphicsDevice);
        _mainLayout.AddToRoot();

        // Load system fonts in background
        Task.Run(() =>
        {
            var fonts = fontDiscoveryService.GetSystemFonts();
            _mainViewModel.FontConfig.SystemFonts = fonts;
        });

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        GumService.Default.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));
        GumService.Default.Draw();
        base.Draw(gameTime);
    }
}
