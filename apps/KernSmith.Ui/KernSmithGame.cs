using System.Collections.Concurrent;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoGameGum;
using Gum.Forms;
using KernSmith.Ui.Layout;
using KernSmith.Ui.ViewModels;
using KernSmith.Ui.Services;
using KernSmith.Ui.Styling;

namespace KernSmith.Ui;

public class KernSmithGame : Game
{
    private const int MinWindowWidth = 800;
    private const int MinWindowHeight = 500;

    private GraphicsDeviceManager _graphics;
    private MainLayout? _mainLayout;
    private MainViewModel? _mainViewModel;
    private SessionService? _sessionService;
    private KeyboardState _previousKeyboardState;
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    public KernSmithGame()
    {
        _graphics = new GraphicsDeviceManager(this);

        // Load session state early so window size is applied before first frame
        _sessionService = new SessionService();
        _sessionService.Load();

        _graphics.PreferredBackBufferWidth = Math.Max(MinWindowWidth, _sessionService.State.WindowWidth);
        _graphics.PreferredBackBufferHeight = Math.Max(MinWindowHeight, _sessionService.State.WindowHeight);
        Window.Title = "KernSmith v1.0.0";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;

        // Enforce minimum window size on resize
        Window.ClientSizeChanged += (_, _) =>
        {
            try
            {
                var width = Window.ClientBounds.Width;
                var height = Window.ClientBounds.Height;

                // Guard against zero or negative dimensions during rapid resize or minimize
                if (width <= 0 || height <= 0)
                    return;

                if (width < MinWindowWidth || height < MinWindowHeight)
                {
                    _graphics.PreferredBackBufferWidth = Math.Max(MinWindowWidth, width);
                    _graphics.PreferredBackBufferHeight = Math.Max(MinWindowHeight, height);
                    _graphics.ApplyChanges();
                }
            }
            catch (Exception)
            {
                // Swallow resize errors — the window will settle on the next frame
            }
        };
    }

    /// <summary>
    /// Enqueues an action to run on the main/game thread during the next Update cycle.
    /// Use this to marshal GPU operations (e.g., Texture2D creation) from background threads.
    /// </summary>
    public void RunOnMainThread(Action action) => _mainThreadActions.Enqueue(action);

    protected override void Initialize()
    {
        GumService.Default.Initialize(this, DefaultVisualsVersion.V3);

        // Build a dark theme using the existing sprite sheet, then set as active
        var defaultSpriteSheet = Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle.SpriteSheet;
        var darkStyle = new Gum.Forms.DefaultVisuals.V3.Styling(defaultSpriteSheet, useDefaults: true);

        // VS Code / dark IDE inspired palette
        darkStyle.Colors.Primary = new Color(0, 122, 204);       // blue buttons/accents
        darkStyle.Colors.Accent = new Color(0, 122, 204);        // selection highlights
        darkStyle.Colors.InputBackground = new Color(60, 60, 60); // input fields
        darkStyle.Colors.SurfaceVariant = new Color(50, 50, 50);  // scrollbar tracks
        darkStyle.Colors.TextPrimary = new Color(212, 212, 212);  // main text
        darkStyle.Colors.TextMuted = new Color(128, 128, 128);    // placeholder/muted
        darkStyle.Colors.IconDefault = new Color(200, 200, 200);  // icons
        darkStyle.Colors.Black = new Color(30, 30, 30);           // deep background
        darkStyle.Colors.DarkGray = new Color(45, 45, 48);        // panel fills
        darkStyle.Colors.Gray = new Color(70, 70, 74);            // borders/dividers
        darkStyle.Colors.LightGray = new Color(150, 150, 150);    // secondary text
        darkStyle.Colors.White = new Color(230, 230, 230);        // bright text/icons
        darkStyle.Colors.Success = new Color(78, 201, 176);       // success green
        darkStyle.Colors.Warning = new Color(220, 170, 50);       // warning amber
        darkStyle.Colors.Danger = new Color(244, 71, 71);         // error red

        Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle = darkStyle;

        var fileDialogService = new FileDialogService();
        var fontDiscoveryService = new FontDiscoveryService();
        var generationService = new GenerationService();
        var projectService = new ProjectService();

        _mainViewModel = new MainViewModel(
            fileDialogService, fontDiscoveryService, generationService,
            projectService, _sessionService!, this);
        _mainLayout = new MainLayout(_mainViewModel, GraphicsDevice);
        _mainLayout.AddToRoot();

        TooltipService.Initialize();

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
        // Drain main-thread action queue (for GPU operations marshaled from background threads)
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _mainViewModel?.StatusBar.SetError($"Main thread action failed: {ex.Message}");
            }
        }

        var kbState = Keyboard.GetState();
        var ctrlHeld = kbState.IsKeyDown(Keys.LeftControl) || kbState.IsKeyDown(Keys.RightControl);
        var shiftHeld = kbState.IsKeyDown(Keys.LeftShift) || kbState.IsKeyDown(Keys.RightShift);

        // Only process keyboard shortcuts when no text input has focus
        if (Gum.Wireframe.InteractiveGue.CurrentInputReceiver == null)
        {
            if (ctrlHeld && IsKeyPressed(Keys.O, kbState))
                _mainViewModel?.OpenFont();
            if (ctrlHeld && shiftHeld && IsKeyPressed(Keys.S, kbState))
                _mainViewModel?.SaveAs();
            else if (ctrlHeld && IsKeyPressed(Keys.S, kbState))
                _mainViewModel?.SaveProject();
            if (ctrlHeld && IsKeyPressed(Keys.G, kbState))
                Task.Run(() => _mainViewModel?.GenerateAsync());
            if (ctrlHeld && (IsKeyPressed(Keys.OemPlus, kbState) || IsKeyPressed(Keys.Add, kbState)))
                _mainLayout?.Preview?.ZoomIn();
            if (ctrlHeld && (IsKeyPressed(Keys.OemMinus, kbState) || IsKeyPressed(Keys.Subtract, kbState)))
                _mainLayout?.Preview?.ZoomOut();
        }

        _previousKeyboardState = kbState;

        // Sync window title from view model
        Window.Title = _mainViewModel?.WindowTitle ?? "KernSmith v1.0.0";

        GumService.Default.Update(gameTime);
        TooltipService.Update();
        _mainLayout?.Preview?.UpdateInput();
        base.Update(gameTime);
    }

    private bool IsKeyPressed(Keys key, KeyboardState current)
    {
        return current.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);
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
