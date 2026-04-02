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
using Microsoft.Xna.Framework.Graphics;

namespace KernSmith.Ui;

/// <summary>
/// MonoGame <see cref="Game"/> subclass and application entry point. Initializes GUM with a dark theme,
/// creates all services and the root <see cref="MainViewModel"/>, handles keyboard shortcuts
/// (Ctrl+O/S/G, zoom), drag-and-drop font/project loading, UI scaling, and the game loop.
/// </summary>
public class KernSmithGame : Game
{
    private const int MinWindowWidth = 800;
    private const int MinWindowHeight = 500;

    private const float MinUiScale = 0.5f;
    private const float MaxUiScale = 2.0f;
    private const float UiScaleStep = 0.125f;

    private GraphicsDeviceManager _graphics;
    private MainLayout? _mainLayout;
    private MainViewModel? _mainViewModel;
    private SessionService? _sessionService;
    private KeyboardState _previousKeyboardState;
    private float _uiScale = 1.0f;
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

        // Handle window resize — update GUM canvas and enforce minimum size
        Window.ClientSizeChanged += HandleClientSizeChanged;
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

        // Dark palette — accent blue for interactive elements only, quiet chrome
        darkStyle.Colors.Primary = new Color(0, 122, 204);        // blue for buttons/active controls
        darkStyle.Colors.Accent = new Color(0, 122, 204);         // selection highlights
        darkStyle.Colors.InputBackground = new Color(50, 50, 54); // input fields (darker than old)
        darkStyle.Colors.SurfaceVariant = new Color(42, 42, 46);  // scrollbar tracks
        darkStyle.Colors.TextPrimary = new Color(200, 200, 200);  // main text
        darkStyle.Colors.TextMuted = new Color(128, 128, 128);    // placeholder/muted
        darkStyle.Colors.IconDefault = new Color(170, 170, 170);  // icons
        darkStyle.Colors.Black = new Color(30, 30, 30);           // deep background
        darkStyle.Colors.DarkGray = new Color(37, 37, 38);        // panel fills (match Theme.Panel)
        darkStyle.Colors.Gray = new Color(55, 55, 58);            // borders/dividers (subtler)
        darkStyle.Colors.LightGray = new Color(140, 140, 140);    // secondary text
        darkStyle.Colors.White = new Color(220, 220, 220);        // bright text/icons
        darkStyle.Colors.Success = new Color(78, 201, 176);       // success green
        darkStyle.Colors.Warning = new Color(220, 170, 50);       // warning amber
        darkStyle.Colors.Danger = new Color(244, 71, 71);         // error red

        Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle = darkStyle;

        var fontDiscoveryService = new FontDiscoveryService();
        var generationService = new GenerationService();
        var projectService = new ProjectService();

        _mainViewModel = new MainViewModel(
            fontDiscoveryService, generationService,
            projectService, _sessionService!, this);
        _mainLayout = new MainLayout(_mainViewModel, GraphicsDevice);
        _mainLayout.AddToRoot();

        // Wire UI scale controls from preview tab bar
        if (_mainLayout.Preview != null)
        {
            _mainLayout.Preview.UiScaleUpRequested += () => SetUiScale(_uiScale + UiScaleStep);
            _mainLayout.Preview.UiScaleDownRequested += () => SetUiScale(_uiScale - UiScaleStep);
            _mainLayout.Preview.UiScaleResetRequested += () => SetUiScale(1.0f);
        }

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

        // Load system fonts in background, marshal result to UI thread
        Task.Run(() =>
        {
            var fonts = fontDiscoveryService.GetSystemFonts();
            RunOnMainThread(() => _mainViewModel.FontConfig.SystemFonts = fonts);
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
                SetUiScale(_uiScale + UiScaleStep);
            if (ctrlHeld && (IsKeyPressed(Keys.OemMinus, kbState) || IsKeyPressed(Keys.Subtract, kbState)))
                SetUiScale(_uiScale - UiScaleStep);
            if (ctrlHeld && IsKeyPressed(Keys.D0, kbState))
                SetUiScale(1.0f);
        }

        _previousKeyboardState = kbState;

        // Sync window title from view model
        Window.Title = _mainViewModel?.WindowTitle ?? "KernSmith v1.0.0";

        GumService.Default.Update(gameTime);
        TooltipService.Update();
        _mainLayout?.Preview?.UpdateInput();
        base.Update(gameTime);
    }

    private void SetUiScale(float scale)
    {
        _uiScale = Math.Clamp(scale, MinUiScale, MaxUiScale);
        ApplyUiScale();
        _mainLayout?.Preview?.UpdateUiScaleDisplay(_uiScale);
        if (_mainViewModel != null)
            _mainViewModel.StatusBar.StatusText = $"UI Scale: {(int)(_uiScale * 100)}%";
    }

    private void ApplyUiScale()
    {
        var gumUI = GumService.Default;
        gumUI.Renderer.Camera.Zoom = _uiScale;
        gumUI.CanvasWidth = _graphics.GraphicsDevice.Viewport.Width / _uiScale;
        gumUI.CanvasHeight = _graphics.GraphicsDevice.Viewport.Height / _uiScale;
        gumUI.Root.UpdateLayout();
    }

    private bool IsKeyPressed(Keys key, KeyboardState current)
    {
        return current.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
        GumService.Default.Draw();
        base.Draw(gameTime);
    }

    private void HandleClientSizeChanged(object? sender, EventArgs e)
    {
        try
        {
            var width = Window.ClientBounds.Width;
            var height = Window.ClientBounds.Height;

            if (width <= 0 || height <= 0)
                return;

            // Enforce minimum window size
            if (width < MinWindowWidth || height < MinWindowHeight)
            {
                _graphics.PreferredBackBufferWidth = Math.Max(MinWindowWidth, width);
                _graphics.PreferredBackBufferHeight = Math.Max(MinWindowHeight, height);
                _graphics.ApplyChanges();
            }

            // Update GUM canvas to match new window size, accounting for UI scale
            ApplyUiScale();
        }
        catch (Exception)
        {
            // Swallow resize errors
        }
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
