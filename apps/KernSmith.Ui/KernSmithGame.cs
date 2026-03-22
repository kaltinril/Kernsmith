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
    private SessionService? _sessionService;

    public KernSmithGame()
    {
        _graphics = new GraphicsDeviceManager(this);

        // Load session state early so window size is applied before first frame
        _sessionService = new SessionService();
        _sessionService.Load();

        _graphics.PreferredBackBufferWidth = _sessionService.State.WindowWidth;
        _graphics.PreferredBackBufferHeight = _sessionService.State.WindowHeight;
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
        var projectService = new ProjectService();

        _mainViewModel = new MainViewModel(
            fileDialogService, fontDiscoveryService, generationService,
            projectService, _sessionService!, this);
        _mainLayout = new MainLayout(_mainViewModel, GraphicsDevice);
        _mainLayout.AddToRoot();

        // Drag-and-drop font and project loading
        Window.FileDrop += (_, args) =>
        {
            if (args.Files is { Length: > 0 })
            {
                var path = args.Files[0];
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".bmfc")
                    _mainViewModel!.LoadProjectFromPath(path);
                else if (ext is ".ttf" or ".otf" or ".woff" or ".ttc")
                    _mainViewModel!.LoadFontFromPath(path);
            }
        };

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

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (_sessionService != null)
        {
            _sessionService.State.WindowWidth = _graphics.PreferredBackBufferWidth;
            _sessionService.State.WindowHeight = _graphics.PreferredBackBufferHeight;
            _sessionService.Save();
        }

        base.OnExiting(sender, args);
    }
}
