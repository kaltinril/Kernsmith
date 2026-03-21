# Phase 68 — Platform, Performance & Accessibility

> **Status**: Planning
> **Created**: 2026-03-21
> **Goal**: Ensure the UI performs well with large fonts, works correctly on all platforms (Windows, macOS, Linux), meets accessibility standards where possible within MonoGame's constraints, and handles edge cases gracefully.

---

## Framework Context

KernSmith UI uses **MonoGame (DesktopGL) + GUM UI (code-only) + MonoGame.Extended**. This has significant implications for this phase:

- **Single-threaded GPU**: All `Texture2D`, `RenderTarget2D`, `SpriteBatch`, and `GraphicsDevice` operations MUST run on the main/graphics thread. Font generation (CPU-bound) runs on background threads via `Task.Run()`, but results must be marshaled back.
- **No built-in UI automation**: MonoGame has no screen reader support, no `AutomationProperties`, no accessibility tree. Accessibility is limited to keyboard navigation, visual contrast, and scaling.
- **No native file dialogs**: MonoGame provides no file dialog API. Use a cross-platform library (e.g., `NativeFileDialogSharp` or platform-invoke) for open/save dialogs.
- **Manual layout**: GUM handles layout, but there is no data-binding or `ObservableCollection`. State changes must be pushed to GUM elements explicitly.
- **DesktopGL cross-platform**: OpenGL backend runs on Windows, macOS, and Linux without platform-specific rendering code.

---

## Wave 1: Performance Optimization

**Goal**: Eliminate frame drops during generation, handle large character sets without memory exhaustion, and render atlas previews efficiently at any zoom level.

### Background Generation & Thread Marshaling

Font generation is CPU-intensive and must never run on the graphics thread. However, `Texture2D` creation requires the `GraphicsDevice`, which is only safe on the main thread. A thread-safe queue bridges the gap.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 1.1 | Thread-safe result queue | Create `ConcurrentQueue<Action> _mainThreadQueue` in the `Game` subclass. In `Update()`, dequeue and execute all pending actions each frame. This is the sole mechanism for background threads to touch GPU resources or GUM elements. | M |
| 1.2 | Wrap `BmFont.Builder()` in `Task.Run()` | `GenerationService.GenerateAsync()` offloads the entire pipeline to `Task.Run()`. Returns `Task<BmFontResult>`. Never call `BmFont.Builder().Build()` on the graphics thread. When complete, enqueue a callback onto `_mainThreadQueue` to create `Texture2D` from the result. | M |
| 1.3 | Texture2D creation on main thread | After generation completes on the background thread, enqueue a lambda that calls `new Texture2D(GraphicsDevice, width, height)` and `texture.SetData(pixelData)`. Store the resulting `Texture2D` in the preview state. Never call `GraphicsDevice` methods from a background thread. **Note:** `AtlasPage.PixelData` contains raw pixel data (not encoded PNG) and can be used with `SetData()` directly. When using `BmFontResult.GetPngData()`, the returned bytes are PNG-encoded and must be decoded first — use `Texture2D.FromStream()` with a `MemoryStream`, or a decoder like StbImageSharp. Prefer `AtlasPage.PixelData` to avoid the decode step on the main thread. | M |
| 1.4 | Add `CancellationToken` to generation | Pass `CancellationTokenSource.Token` into `GenerateAsync()`. Wire to a "Cancel" button in the status bar. When cancelled, `OperationCanceledException` is caught and status set to "Generation cancelled". CancellationToken is used to discard stale results after `Build()` completes — not for mid-generation cancellation (see Phase 55). The token is checked before marshaling the result to the main thread. | M |
| 1.5 | Progress reporting placeholder | Core library has no progress reporting (see Phase 55). UI can only report coarse status: "Generating..." and "Done". This task is a placeholder pending Phase 55 API addition. | S |
| 1.6 | Debounce rapid setting changes | When the user drags a slider (font size, padding, spacing), debounce so auto-regeneration only fires after 300ms of inactivity. Use a manual `CancellationTokenSource` reset pattern: each change cancels the previous pending generation and starts a new delay timer. Prevents queueing dozens of generation tasks during a single slider drag. | M |
| 1.7 | Generate button vs auto-generate toggle | Add a "Generate" button (always visible) and an "Auto-generate on change" checkbox (GUM `CheckBoxRuntime`). When auto-generate is on, any config change triggers debounced generation. When off, only the button triggers it. Default: auto-generate on. | S |
| 1.8 | Prevent concurrent generation | If generation is already running and a new request comes in, cancel the in-flight generation before starting the new one. Use a cancel-and-restart pattern with `CancellationTokenSource`. Never run two generation tasks in parallel. | S |
| 1.9 | Frame time monitor | In `DEBUG` builds, track `gameTime.ElapsedGameTime` in `Update()`. If any frame exceeds 50ms (below 20 FPS), log a warning: "Frame took {elapsed}ms — possible main thread blockage". This catches accidental GPU-thread blocking during development. | S |
| 1.10 | Generation timing in status bar | Measure wall-clock time of generation using `Stopwatch`. Display in the status bar GUM text element as "Generated in 0.42s". Update via `_mainThreadQueue` when generation completes. | S |
| 1.11 | Disable controls during generation | While generation is active, set `IsEnabled = false` on the Generate button, font file picker, and character set GUM elements. Enable the Cancel button. Use an `IsGenerating` flag checked by the UI update logic. | S |
| 1.12 | Error handling for generation failures | Wrap `GenerateAsync` in try/catch on the background thread. Catch `OperationCanceledException` (enqueue status "Cancelled"), `OutOfMemoryException` (enqueue error dialog: "Atlas too large, reduce character set or texture size"), and general `Exception` (enqueue error dialog with message). All error UI updates go through `_mainThreadQueue`. | M |

### Large Character Set Handling

CJK fonts can contain 20,000+ glyphs. The character grid and generation pipeline must handle this without freezing or crashing.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 1.13 | Virtual character grid | GUM cannot efficiently display 20K+ elements. Implement a virtual scrolling container: calculate the total scroll height from character count and cell size, track scroll offset, and only create/update GUM elements for the visible rows. Reuse a fixed pool of cell elements (e.g., 200 cells for a 10x20 visible grid) and rebind them as the user scrolls. | L |
| 1.14 | Cell pool recycling | Maintain a `List<CharacterCellRuntime>` pool sized to `visibleRows * columnsPerRow + buffer`. On scroll, reassign each cell's character, codepoint text, and selection state rather than creating/destroying GUM elements. This keeps GUM element count constant regardless of character set size. | M |
| 1.15 | Scroll input handling | Handle `MouseState.ScrollWheelValue` delta in `Update()` to scroll the virtual character grid. Clamp scroll offset to `[0, totalHeight - visibleHeight]`. Smooth scrolling: lerp toward the target offset each frame for fluid motion. Also support click-and-drag on a scrollbar track element. | M |
| 1.16 | Paginated character display | For character sets exceeding 5,000 glyphs, optionally display in pages of 500. Add "Page X of Y" indicator and Previous/Next buttons (GUM `ButtonRuntime`). Alternative to full virtualization for simpler implementation. | M |
| 1.17 | Lazy character enumeration | When loading a font, enumerate glyphs on a background thread via `Task.Run()`. Populate the character list in batches of 200, enqueueing UI updates onto `_mainThreadQueue`. Show "Loading characters... (4,200 / 21,000)" progress in a GUM text element. | M |
| 1.18 | Character search and filter | Add a GUM `TextBoxRuntime` above the character grid. Filter by character name, Unicode codepoint (e.g., "U+4E00"), or block name (e.g., "CJK Unified"). Filtering rebuilds the virtual grid's backing list and resets scroll to top. Debounce input by 200ms. | M |
| 1.19 | Character range presets | Quick-select buttons: "ASCII" (U+0020-007E), "Latin Extended" (U+0020-024F), "Cyrillic" (U+0400-04FF), "CJK Common" (top 3,000 most-used CJK characters), "All Glyphs". Clicking a preset populates the character set and refreshes the virtual grid. | S |
| 1.20 | Glyph count display | Show "X characters selected / Y available in font" in a GUM text element in the character grid header. Update in real-time as characters are toggled. | S |
| 1.21 | Select all / Deselect all | Buttons to select or deselect all visible (filtered) characters. Must complete in <100ms even for 20K characters: batch-update the selection array and refresh only visible cells. Do not trigger per-item callbacks during bulk operations. | S |
| 1.22 | Memory usage indicator | Display estimated memory usage in the status bar: "~45 MB" calculated from `(charCount * avgGlyphSize * bitsPerPixel)`. Warn (amber text) if estimated atlas memory exceeds 512 MB. | S |

### Atlas Rendering Optimization

The atlas preview must render smoothly during pan/zoom without re-decoding the source data. All rendering uses `SpriteBatch` and `Texture2D`.

| # | Task | Details | Effort |
|---|------|---------|--------|
| 1.23 | Cache atlas as Texture2D | When generation completes, create a `Texture2D` for each atlas page from the raw pixel data on the main thread (via `_mainThreadQueue`). Store in `PreviewState.CachedPages` as `List<Texture2D>`. Reuse on pan/zoom. Only recreate after a new generation. | M |
| 1.24 | SpriteBatch rendering with transform | Render the cached `Texture2D` using `SpriteBatch.Draw()` with a transformation `Matrix` for zoom and pan. Compute the matrix from current zoom level and pan offset. Use `SpriteBatch.Begin(transformMatrix: viewMatrix)`. Let the GPU handle scaling rather than creating rescaled textures in software. | M |
| 1.25 | Multi-page atlas tab strip | When the atlas has multiple pages, render a tab strip (GUM `ButtonRuntime` per page) above the preview. Each tab shows "Page 1 (512x512)". Clicking a tab switches which `Texture2D` is drawn. Only the active page's texture needs to be in `Draw()`. | S |
| 1.26 | Zoom level presets | Buttons: "Fit", "1:1 (100%)", "200%", "400%". "Fit" scales the atlas to fill the preview panel. "1:1" renders at native pixel resolution. Current zoom percentage shown in a GUM text element (e.g., "150%"). | S |
| 1.27 | Smooth pan and zoom | Mouse wheel zooms toward cursor position (not center). Calculate the world-space point under the cursor before and after zoom, then adjust the pan offset to keep that point stationary. Click-and-drag pans the atlas by updating the offset from `MouseState` delta. Clamp zoom range to 10%-3200%. | M |
| 1.28 | Glyph hover tooltip | When the mouse hovers over a glyph in the atlas preview, hit-test against the glyph rectangles from `BmFontResult` (transformed by the inverse view matrix). Show a GUM tooltip element: "A (U+0041) | 24x28 px | Page 1". Position the tooltip near the cursor. Hide when the mouse moves away. | M |
| 1.29 | Dispose previous Texture2D | When a new generation completes, call `texture.Dispose()` on the previous `Texture2D` objects before creating new ones. This frees GPU memory immediately. Set old references to `null`. Failing to dispose causes GPU memory leaks that eventually crash the application. | S |
| 1.30 | Large atlas warning | If atlas dimensions exceed 4096x4096, show a warning: "Atlas is very large (8192x8192). Preview may be slow. Consider reducing character set or increasing packing density." Some GPUs have maximum texture size limits (check `GraphicsDevice.Adapter` capabilities). | S |
| 1.31 | RenderTarget2D for preview isolation | Render the atlas preview into a `RenderTarget2D` sized to the preview panel, then draw that render target to the screen. This clips the atlas to the preview region and avoids SpriteBatch state conflicts with GUM rendering. Recreate the `RenderTarget2D` if the panel resizes. | M |

### Startup Performance

| # | Task | Details | Effort |
|---|------|---------|--------|
| 1.32 | System font enumeration on background thread | `FontDiscoveryService.EnumerateSystemFontsAsync()` runs via `Task.Run()`. The font dropdown GUM element shows "Loading fonts..." placeholder until complete. Enqueue the populated font list onto `_mainThreadQueue` when finished. Cache the result for the session. | M |
| 1.33 | Lazy-load effects panel | The effects panel GUM elements are not instantiated until the user first navigates to the Effects tab. Use a flag to defer creation. This reduces initial GUM element count and speeds up first frame. | S |
| 1.34 | Startup time measurement | Log elapsed time from `Game.Initialize()` to first `Draw()` call. Target: <1.5 seconds cold start on SSD. Log to debug output: "KernSmith UI started in 1.23s". Use `Stopwatch` started in the `Game` constructor. | S |
| 1.35 | Loading screen for slow startups | If initialization takes >2 seconds (e.g., first-launch JIT, slow font enumeration), show a simple loading screen: clear to the brand color and draw "KernSmith — Loading..." with `SpriteFont`. Replace with the full GUM UI once ready. | S |
| 1.36 | Font cache persistence | Save the system font list to a JSON file in `{AppData}/KernSmith/font-cache.json`. On next launch, load from cache immediately (on the main thread since JSON parsing is fast), then refresh in background. Cache includes font family name, style, and file path. Invalidate if file modification dates change. | M |

---

## Wave 2: Cross-Platform Testing & Fixes

**Goal**: The application runs correctly on Windows 10/11, macOS 12+, and common Linux distributions (Ubuntu 22.04+, Fedora 38+) without platform-specific bugs. MonoGame DesktopGL uses OpenGL on all platforms.

### Windows-Specific

| # | Task | Details | Effort |
|---|------|---------|--------|
| 2.1 | Native file dialogs | MonoGame has no built-in file dialogs. Use `NativeFileDialogSharp` (NuGet) or P/Invoke `GetOpenFileName` / `GetSaveFileName` from `comdlg32.dll`. Run the dialog call on the main thread (Win32 dialogs require the STA thread). Set file type filters: `*.ttf`, `*.otf`, `*.woff`, `*.woff2` for font open; `*.fnt`, `*.xml`, `*.json`, `*.txt` for save. | M |
| 2.2 | System font discovery paths | Enumerate fonts from `C:\Windows\Fonts` and `%LOCALAPPDATA%\Microsoft\Windows\Fonts` (per-user fonts on Windows 10 1809+). Use `Environment.GetFolderPath(Environment.SpecialFolder.Fonts)` as the primary path. Also scan the registry key `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts` for the canonical font list. | M |
| 2.3 | HiDPI / display scaling | MonoGame DesktopGL on Windows does not automatically handle DPI scaling. The window may appear small on high-DPI displays. Options: (1) set the process as DPI-aware via app manifest and scale the `PreferredBackBufferWidth/Height` by the DPI scale factor, or (2) let Windows scale the window (blurry). Prefer option 1. Detect DPI via `User32.GetDpiForWindow()` P/Invoke. | M |
| 2.4 | Per-monitor DPI awareness | When the window is dragged between monitors with different DPI, detect the change via SDL2's `SDL_DISPLAYEVENT_CHANGED` event (the correct approach for MonoGame DesktopGL, which uses SDL2 — not Win32 `WM_DPICHANGED`). Resize the back buffer accordingly. GUM layout must be recalculated after DPI changes. | M |
| 2.5 | Taskbar icon | Set the window icon via `GameWindow.SetIconFromContent()` or by P/Invoking `SendMessage` with `WM_SETICON`. Provide a multi-resolution `.ico` file containing 16x16, 32x32, 48x48, and 256x256 sizes. Verify the icon appears in the taskbar and Alt+Tab switcher. | S |
| 2.6 | Windows dark/light theme detection | Detect Windows theme by reading the registry key `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`. Value 0 = dark, 1 = light. Apply the corresponding GUM color scheme. Optionally poll on a timer to detect live changes. | S |
| 2.7 | Windows Defender / SmartScreen | Self-contained single-file executables may trigger SmartScreen warnings. Document that code-signing (Authenticode) is required for distribution. Note the publisher name needed for the certificate. | S |
| 2.8 | Windows ARM64 support | Verify the application runs on Windows on ARM (Surface Pro X, etc.) via x64 emulation or native ARM64 build. FreeTypeSharp must have ARM64 native binaries. MonoGame DesktopGL uses OpenGL which runs under translation on ARM64 Windows. Test with `RuntimeInformation.ProcessArchitecture`. | M |
| 2.9 | Long path support | Ensure file save paths exceeding 260 characters work. .NET 10 enables long paths by default. Test saving to a deeply nested directory. Use `Path.GetFullPath()` and avoid `MAX_PATH`-limited APIs. | S |
| 2.10 | Drag-and-drop font files | MonoGame DesktopGL on Windows supports `GameWindow.FileDrop` event (SDL2-based). Handle the event to detect dropped `.ttf` / `.otf` files. Filter to supported extensions and call the font loading pipeline. The `FileDrop` event fires on the main thread. | M |

### macOS-Specific

| # | Task | Details | Effort |
|---|------|---------|--------|
| 2.11 | Menu bar integration | MonoGame does not create a native macOS menu bar. The application runs as an SDL2 window without system menu integration. Options: (1) accept no menu bar and use in-app menus only, or (2) use P/Invoke with `AppKit` via `ObjCRuntime` to create a minimal native menu (About, Quit). Document the trade-off. Recommend option 1 with keyboard shortcuts. | M |
| 2.12 | Keyboard shortcut remapping | Map `Ctrl` shortcuts to `Cmd` on macOS. MonoGame reports `Cmd` as `Keys.LeftWindows` or `Keys.RightWindows` on macOS. Detect macOS via `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)` and remap accordingly. Key mappings: `Cmd+O` (Open), `Cmd+S` (Save), `Cmd+Shift+S` (Save As), `Cmd+Q` (Quit). | M |
| 2.13 | System font discovery paths | Enumerate fonts from: `/System/Library/Fonts` (system fonts), `/Library/Fonts` (all-users fonts), `~/Library/Fonts` (user fonts). Use `Directory.GetFiles(path, "*.ttf", SearchOption.AllDirectories)` and similarly for `*.otf`, `*.ttc`. Note: `/System/Library/Fonts` may require SIP-aware access on macOS 11+. | M |
| 2.14 | Retina display support | macOS Retina displays report 2x scaling. MonoGame DesktopGL on macOS requires `SDL_WINDOW_ALLOW_HIGHDPI` (set via SDL2 hints before window creation) and the back buffer must be sized to the physical resolution. Check drawable size vs window size (via `SDL_GL_GetDrawableSize` vs `SDL_GetWindowSize`) to determine the actual scale factor. `PreferHalfPixelOffset` controls texel-to-pixel alignment for SpriteBatch and is unrelated to Retina/HiDPI support. Ensure atlas preview renders at native resolution (not blurry). | M |
| 2.15 | App bundle structure | The macOS build must produce a `.app` bundle: `KernSmith.app/Contents/MacOS/KernSmith`, `KernSmith.app/Contents/Resources/kernsmith.icns`, `KernSmith.app/Contents/Info.plist`. Use `dotnet publish -r osx-x64 --self-contained` and post-process into `.app` via a build script. MonoGame DesktopGL requires the SDL2 and OpenAL dylibs in the bundle. | M |
| 2.16 | Code signing and notarization | macOS requires code signing for Gatekeeper. Document the process: obtain Apple Developer ID certificate, sign with `codesign --deep --force --verify --verbose`, notarize with `xcrun notarytool submit`. Without signing, users must right-click and "Open" to bypass Gatekeeper. | M |
| 2.17 | DMG packaging | Create a `.dmg` installer with a background image, drag-to-Applications icon, and symlink to `/Applications`. Use `create-dmg` or `hdiutil` script. Include a license agreement panel. | S |
| 2.18 | macOS dark mode detection | Detect macOS appearance by reading `defaults read -g AppleInterfaceStyle` (returns "Dark" or fails). Apply the corresponding GUM color scheme. Alternatively use `ObjCRuntime` to query `NSApp.effectiveAppearance`. | S |
| 2.19 | File dialogs on macOS | Use `NativeFileDialogSharp` which wraps the native Cocoa `NSOpenPanel` / `NSSavePanel` on macOS. Verify the dialog respects macOS conventions (sidebar, tags, format dropdown). Test that the dialog returns correct paths. | S |
| 2.20 | Apple Silicon (ARM64) support | Build and test on macOS with Apple Silicon (M1/M2/M3). Publish with `dotnet publish -r osx-arm64`. MonoGame DesktopGL runs on Apple Silicon via Rosetta 2 (x64) or natively (ARM64). FreeTypeSharp must include `libfreetype.dylib` for ARM64. Prefer native ARM64 for performance. | M |

### Linux-Specific

| # | Task | Details | Effort |
|---|------|---------|--------|
| 2.21 | System font discovery paths | Enumerate fonts from: `/usr/share/fonts`, `/usr/local/share/fonts`, `~/.fonts`, `~/.local/share/fonts`. Also parse `fontconfig` output via `fc-list --format="%{file}\n"` for the most accurate font list. Handle symbolic links. | M |
| 2.22 | X11 and Wayland compatibility | MonoGame DesktopGL uses SDL2, which supports both X11 and Wayland. SDL2 defaults to X11 but can use Wayland with `SDL_VIDEODRIVER=wayland`. Test on both. Known issues: Wayland may have different window positioning behavior, some compositors may not support window resizing correctly. Test on GNOME (Wayland default) and KDE (X11 default). | M |
| 2.23 | File dialog on Linux | `NativeFileDialogSharp` uses GTK3 file dialogs on Linux or the xdg-desktop-portal. If neither is available (minimal desktop environments), implement a basic in-app file browser using GUM elements as fallback. Test on Ubuntu GNOME, KDE Plasma, and a minimal WM (i3/Sway). | M |
| 2.24 | Theme compatibility | MonoGame renders everything via OpenGL, so desktop themes do not affect rendering. The GUM UI has its own color scheme independent of GTK/Qt themes. However, file dialogs (via NativeFileDialogSharp) inherit the desktop theme. No action needed beyond testing that file dialogs look acceptable. | S |
| 2.25 | AppImage packaging | Create an AppImage using `appimagetool`. Include the self-contained .NET runtime, FreeType native libraries (`libfreetype.so.6`), SDL2 libraries, and a `.desktop` file. The AppImage should be a single executable file that runs on any x64 Linux distro without installation. Set `AppRun` to launch the .NET entry point. Bundle `libSDL2.so` to avoid version conflicts with system SDL2. | M |
| 2.26 | Flatpak packaging | Create a Flatpak manifest (`org.kernsmith.KernSmith.yml`). Use the `org.freedesktop.Platform` runtime. Include FreeType and SDL2 as dependencies. Publish to Flathub (future). Flatpak provides sandboxing, so test file access via portals. | M |
| 2.27 | .desktop file | Create `kernsmith.desktop` with `[Desktop Entry]` section: `Name=KernSmith`, `Exec=kernsmith %f`, `Icon=kernsmith`, `Type=Application`, `Categories=Graphics;Font;`, `MimeType=application/x-font-ttf;application/x-font-otf;`. Install to `~/.local/share/applications/`. | S |
| 2.28 | Linux HiDPI support | MonoGame DesktopGL on Linux uses SDL2 which reads `SDL_HINT_VIDEO_HIGHDPI_DISABLED` and `GDK_SCALE` environment variables. Detect the display scale factor and multiply `PreferredBackBufferWidth/Height` accordingly. GUM layout must account for the scale factor. Test at 1x, 1.5x (fractional scaling on GNOME), and 2x. | M |
| 2.29 | Missing native library handling | If `libfreetype.so.6` or `libSDL2-2.0.so.0` is missing at runtime, show a clear error: "FreeType library not found. Install it with: `sudo apt install libfreetype6` (Debian/Ubuntu) or `sudo dnf install freetype` (Fedora)." Detect at startup via `NativeLibrary.TryLoad()` before MonoGame initialization. | S |
| 2.30 | Drag-and-drop on Linux | SDL2 supports drag-and-drop on Linux via `SDL_DROPFILE` events. MonoGame exposes this through `GameWindow.FileDrop`. Test from Nautilus (GNOME), Dolphin (KDE), and Thunar (XFCE). Wayland drag-and-drop support depends on the compositor and SDL2 version. | S |

### Cross-Platform Path Handling

| # | Task | Details | Effort |
|---|------|---------|--------|
| 2.31 | Use `Path.Combine()` everywhere | Audit all file path construction. Never concatenate with `+` and `"/"` or `"\\"`. Use `Path.Combine()`, `Path.GetDirectoryName()`, `Path.GetFileName()`. | S |
| 2.32 | Case-sensitive file system handling | Linux file systems are case-sensitive. Ensure output file names (`font_0.png`, `font.fnt`) use consistent casing. When checking if a file exists, use exact casing. Never assume `Font.ttf` and `font.ttf` are the same file. | S |
| 2.33 | Home directory resolution | Use `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` instead of hardcoding `~`. For app data: `Environment.SpecialFolder.ApplicationData` (Windows: `%APPDATA%`, macOS: `~/Library/Application Support`, Linux: `~/.config`). | S |
| 2.34 | Path separator display | When showing file paths in the UI (status bar, recent files), display with the platform's native separator. Use `Path.DirectorySeparatorChar`. Truncate long paths with "..." in the middle: `C:\Users\...\output\font.fnt`. | S |
| 2.35 | Recent files persistence | Store recent file paths in app settings (JSON). On load, verify each path still exists before displaying. Remove stale entries. Limit to 10 recent files. Store as absolute paths. | S |

---

## Wave 3: Accessibility

**Goal**: Provide the best accessibility possible within MonoGame's constraints. MonoGame has NO built-in screen reader support, no UI automation tree, and no platform accessibility APIs. This is a fundamental limitation of the framework. Focus on keyboard accessibility, visual accessibility, and honest documentation of limitations.

### Accessibility Limitations (Document Clearly)

| # | Task | Details | Effort |
|---|------|---------|--------|
| 3.1 | Document screen reader limitations | Create a user-facing accessibility statement: "KernSmith uses MonoGame for rendering, which does not integrate with platform screen readers (NVDA, VoiceOver, Orca). We focus on keyboard navigation, visual contrast, and scaling. Users requiring screen reader support should use the KernSmith CLI tool, which produces text output compatible with any terminal screen reader." | S |
| 3.2 | CLI as accessible alternative | Ensure the `KernSmith.Cli` tool provides equivalent functionality for users who cannot use the GUI. Document all CLI flags and their GUI equivalents. The CLI runs in a terminal which screen readers can read natively. | S |
| 3.3 | Investigate IAccessible/ATK bridge (research only) | Research whether it is feasible to expose MonoGame's SDL2 window handle and inject `IAccessible` (Windows) or ATK (Linux) automation data. This would be a large custom implementation. Document findings and effort estimate. Do not implement in this phase. | M |

### Keyboard Navigation

| # | Task | Details | Effort |
|---|------|---------|--------|
| 3.4 | GUM focus management system | Implement a focus manager that tracks which GUM element is currently focused. Maintain a flat or tree-structured list of focusable elements. Draw a visible focus rectangle (2px accent-colored border) around the focused element. GUM does not provide this natively; implement it as a custom system. | L |
| 3.5 | Tab order through all panels | Define a logical tab order: Font file picker -> Font family dropdown -> Font size input -> Style checkboxes -> Character set -> Generate button -> Atlas preview -> Effects. `Tab` moves forward, `Shift+Tab` moves backward. Implement in the focus manager by maintaining an ordered list of focusable GUM elements. | M |
| 3.6 | Focus indicators | Every focusable GUM element must have a visible focus indicator: a 2px solid border in the accent color drawn around or inside the element bounds. The indicator must be visible in both light and dark color schemes. Draw the focus rect in the `Draw()` method based on the focus manager's current target. | M |
| 3.7 | Arrow key navigation in grids | In the character grid, arrow keys move selection between cells. The focus manager must understand 2D grid layout: `Left`/`Right` move within a row, `Up`/`Down` move between rows. Wrap at row boundaries (Right on last cell in row goes to first cell in next row). | M |
| 3.8 | Keyboard-operable character grid | `Space` toggles the focused character on/off. `Ctrl+A` selects all visible characters. `Home` jumps to first character, `End` to last. `Page Up` / `Page Down` scroll by one visible page. Each navigation updates the visible scroll position to keep the focused cell in view. | M |
| 3.9 | Keyboard-operable controls | GUM `TextBoxRuntime` already handles keyboard input. For custom controls: dropdowns open with `Enter`/`Space` and navigate with arrows, sliders adjust with `Left`/`Right` arrows (small step) and `Page Up`/`Page Down` (large step), checkboxes toggle with `Space`. | M |
| 3.10 | Keyboard-operable gradient editor | `Tab` into the gradient editor. `Left` / `Right` arrow selects color stops. `Delete` removes the selected stop. `Enter` opens the color picker for the selected stop. `Space` adds a new stop at the current position. | M |
| 3.11 | Keyboard shortcuts | Global shortcuts (when not in a text input): `Ctrl+O` / `Cmd+O` (Open Font), `Ctrl+S` / `Cmd+S` (Save), `Ctrl+Shift+S` / `Cmd+Shift+S` (Save As), `F5` (Generate), `Escape` (Cancel generation), `Ctrl+Z` / `Cmd+Z` (Undo), `Ctrl+Y` / `Cmd+Y` (Redo). Check `KeyboardState` in `Update()`. Display shortcuts in tooltip text on buttons. | M |
| 3.12 | Atlas preview keyboard navigation | `+` / `-` zoom in/out. `0` reset to fit. Arrow keys pan the atlas. The preview panel must be focusable and only consume arrow keys when focused (not when a text input has focus). | S |
| 3.13 | Modal dialog keyboard handling | `Enter` activates the primary button ("OK", "Save"). `Escape` activates the cancel button. `Tab` cycles through dialog controls. Focus is trapped within the dialog (the focus manager skips elements behind the modal overlay). | S |
| 3.14 | Keyboard shortcut help | `F1` opens a keyboard shortcut reference panel listing all available shortcuts. Render as a GUM panel overlay with two columns: shortcut and action description. | S |
| 3.15 | Focus management after generation | After generation completes, move focus to the atlas preview panel so the user can immediately explore the result. After an error, move focus to the error message / dismiss button. | S |

### Visual Accessibility

| # | Task | Details | Effort |
|---|------|---------|--------|
| 3.16 | Color contrast ratios | Audit all GUM text colors against WCAG 2.1 AA requirements: 4.5:1 for normal text, 3:1 for large text (18px+ or 14px+ bold). Use a contrast checker tool. Fix any failing colors in both light and dark color schemes. Key areas: status bar text, panel headers, placeholder text, disabled text. | M |
| 3.17 | Non-color information | Never rely solely on color to convey information. Error states: add an icon texture (red circle with X) in addition to red text. Selected characters: add a checkmark overlay in addition to a highlight color. Active tab: add an underline in addition to a different background color. | S |
| 3.18 | UI scaling | Implement a "UI Scale" setting (100%, 125%, 150%, 200%). Multiply all GUM layout coordinates, font sizes, and element dimensions by the scale factor. Store the multiplier and apply it during layout. This compensates for MonoGame's lack of automatic system font size scaling. | M |
| 3.19 | High contrast mode | Add a "High Contrast" option in settings that switches the GUM color scheme to black background with white text, bold borders, and high-saturation accent colors. Define the color scheme as a named set of `Color` constants swapped at runtime. | M |
| 3.20 | Minimum click target size | All interactive elements must be at least 32x32 pixels (44x44 recommended). This applies to: character grid cells, gradient stop handles, tab buttons, toolbar icons. Add padding if the visual element is smaller. Enforce during GUM element sizing. | S |
| 3.21 | Reduced motion support | Add a "Reduce animations" toggle in settings. When enabled: disable smooth pan/zoom (snap instantly), disable smooth scrolling (jump to target), disable any fade transitions. Use `if (!ReduceAnimations)` checks around lerp/animation logic. | S |
| 3.22 | Icon button tooltips | All icon-only buttons must show a text tooltip on hover. Implement tooltips as a GUM text element positioned near the cursor that appears after a 500ms hover delay. Include the action name and keyboard shortcut: "Generate (F5)". | S |
| 3.23 | Error identification | Form validation errors must be: (1) described in text next to the control, (2) shown with an error icon, (3) visible without scrolling. Example: font size field shows "Must be between 8 and 144" in red text below the field. | S |
| 3.24 | Accessibility settings panel | Add an Accessibility section in the settings: "High contrast mode" toggle, "Reduce animations" toggle, "UI scale" dropdown (100%-200%), "Show keyboard shortcuts on buttons" toggle. All settings take effect immediately without restart. | S |

---

## Wave 4: Error Handling & Recovery

**Goal**: The application handles all foreseeable error conditions gracefully, never crashes silently, and recovers from unexpected shutdowns without data loss.

### Graceful Error Handling

| # | Task | Details | Effort |
|---|------|---------|--------|
| 4.1 | Font file validation errors | When loading a font file, catch and display specific errors: file not found, file corrupted (invalid TTF header), unsupported format (WOFF2 without decompressor), file locked by another process. Show the error in a GUM text element below the file picker with a red warning icon texture. | M |
| 4.2 | FreeType rasterization errors | Catch `FreeTypeException` and translate to user-friendly messages: "FreeType error 0x06: Invalid_Argument" becomes "The selected font does not contain the requested glyph style. Try a different style variant." Map common FreeType error codes to plain-English explanations. | M |
| 4.3 | Atlas packing failures | When the atlas cannot fit all glyphs in the maximum texture size: show a dialog with specific guidance: "95 glyphs cannot fit in a 256x256 atlas. Suggested actions: increase texture size to 512x512, reduce font size from 72 to 48, or reduce character set." Provide GUM buttons for each suggestion that apply the fix with one click. | M |
| 4.4 | File save permission errors | Catch `UnauthorizedAccessException` and `IOException` during save. Show: "Cannot save to {path}. The file may be read-only or the directory may not exist." Offer "Save to a different location" button that opens the Save As dialog. | S |
| 4.5 | Out of memory handling | Register `AppDomain.CurrentDomain.UnhandledException` for `OutOfMemoryException`. Before it occurs, monitor `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`. If available memory drops below 100 MB during generation, cancel and warn: "Low memory. Reduce the character set or font size." | M |
| 4.6 | Disk space check before save | Before writing output files, check available disk space with `DriveInfo`. If estimated output size exceeds available space, warn: "Not enough disk space. Need ~15 MB, but only 8 MB available on drive C:." | S |
| 4.7 | GPU texture size limit check | Before creating a `Texture2D`, check `GraphicsDevice.Adapter.MaxTextureSize` (or query via `GL.GetInteger`). If the atlas dimensions exceed the GPU's maximum texture size, show: "Your GPU supports textures up to {max}x{max}. The atlas ({w}x{h}) is too large. Reduce texture size in settings." This prevents a crash from invalid `Texture2D` creation. | M |
| 4.8 | Concurrent file access | If the output `.fnt` file is locked by another application (e.g., a game engine has it open), catch `IOException` and show: "Cannot write {filename}. The file is in use by another application. Close the other application and try again." Add a "Retry" button. | S |
| 4.9 | Invalid configuration detection | Before generation, validate all settings. Errors: font size 0 or negative, padding larger than glyph size, texture size below 16px, empty character set. Show errors inline with the relevant GUM control in red text. Disable the Generate button until all errors are resolved. | M |
| 4.10 | Unsupported font features | When a font uses features not supported by the current KernSmith version (e.g., variable fonts with unsupported axes, color font formats not yet implemented): show a warning (not error) and proceed with fallback behavior. "This font uses COLR v1 color data, which is not yet supported. Generating with monochrome glyphs." | S |

### Crash Recovery

| # | Task | Details | Effort |
|---|------|---------|--------|
| 4.11 | Auto-save project state | Every 2 minutes (tracked via `gameTime.TotalGameTime` in `Update()`), serialize the current configuration state to JSON and save to `{AppData}/KernSmith/autosave/autosave.json`. Include: font file path, font size, character set, texture size, padding, effects configuration, output format. Use `System.Text.Json` with indented formatting. Write on a background thread to avoid frame drops. | M |
| 4.12 | Auto-save on generation | After each successful generation, auto-save the project state immediately (in addition to the timed auto-save). Enqueue the save onto a background thread via `Task.Run()`. | S |
| 4.13 | Crash detection | On startup, check for the existence of `{AppData}/KernSmith/autosave/lock.pid`. If it exists and the PID is not running, the previous session crashed. Create the lock file on startup, delete on clean shutdown in `Game.OnExiting()`. | S |
| 4.14 | Recovery dialog | When a crash is detected on startup, show a GUM dialog overlay: "KernSmith did not shut down properly. Would you like to restore your previous session?" Buttons: "Restore" (loads auto-saved state), "Start Fresh" (ignores auto-save), "Show File" (opens the autosave directory in the system file manager). | S |
| 4.15 | Global exception handler | Register `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`. The `UnhandledException` handler should focus on logging crash data to `{AppData}/KernSmith/logs/crash-{timestamp}.log` and writing to stderr — the CLR is in a terminal state at this point, so showing GUM dialogs is not reliable. For recoverable errors, wrap `Game.Run()` in a top-level try/catch that can display an error dialog and offer restart. | M |
| 4.16 | Safe file writing | When saving output files, write to a temporary file first (`{name}.tmp`), then rename to the final name. This prevents corrupted output if the app crashes mid-write. Use `File.Move(tempPath, finalPath, overwrite: true)` for atomic rename. | S |
| 4.17 | Backup previous output | Before overwriting existing output files, rename the old files with a `.bak` extension: `font.fnt` becomes `font.fnt.bak`. Keep only the most recent backup. This protects against accidental overwrites. | S |
| 4.18 | MonoGame crash recovery | MonoGame can crash on `GraphicsDevice` errors (e.g., GPU driver crash, context lost). Catch `NoSuitableGraphicsDeviceException` and OpenGL errors at the top level. Log the error and attempt to restart with reduced graphics settings (smaller back buffer, no MSAA). | M |

### Logging

| # | Task | Details | Effort |
|---|------|---------|--------|
| 4.19 | Application log file | Write logs to `{AppData}/KernSmith/logs/kernsmith-{date}.log`. Use `Microsoft.Extensions.Logging` with a file provider. Rotate logs: keep the last 7 days, delete older files. Maximum file size: 10 MB per day (truncate oldest entries). | M |
| 4.20 | Generation logging | Log every generation: timestamp, font file, font size, character count, texture size, packing algorithm, output format, elapsed time, success/failure, error message (if any). Format as structured log entries. | S |
| 4.21 | User action logging | Log significant user actions at `Information` level: font loaded, settings changed, generation started, generation completed, file saved, file opened. Do not log sensitive data (file contents, font data). | S |
| 4.22 | Debug logging | At `Debug` level, log: FreeType version, native library paths, system font count, GPU info (`GraphicsDevice.Adapter.Description`), OpenGL version, memory usage at key points, atlas packer iterations, display scaling factor, SDL2 video driver in use. Only written to file when log level is set to Debug. | S |
| 4.23 | Log viewer in UI | Add a "View Logs" option in the Help menu. Opens a GUM panel overlay showing the current log file contents in a scrollable text area. Include filter buttons: All, Errors, Warnings, Info. Include "Copy All" and "Open Log Folder" buttons. | M |
| 4.24 | "Copy log to clipboard" for bug reports | In the Help menu, add "Copy Diagnostic Log". Copies the last 100 log entries to the system clipboard (via SDL2 clipboard API), with system info header (OS version, .NET version, KernSmith version, MonoGame version, GPU info, display scaling). User can paste into a bug report. | S |
| 4.25 | Log level setting | In settings, add "Log level" dropdown: Error (default for release), Warning, Information, Debug. Changing the level takes effect immediately without restart. Debug level writes to file only to avoid performance impact. | S |
| 4.26 | Performance logging | Log generation phase timings: "Rasterized 95 glyphs in 120ms", "Packed atlas (512x512) in 45ms", "Encoded PNG (1 page) in 30ms", "Total: 195ms". Log memory usage delta: "Generation used 12.4 MB peak". Log frame time statistics: average, P95, P99 over the last 60 seconds. | S |

---

## Wave 5: Installer & Distribution

**Goal**: Users can install, update, and uninstall KernSmith on all three platforms with platform-appropriate mechanisms.

### Windows Distribution

| # | Task | Details | Effort |
|---|------|---------|--------|
| 5.1 | Self-contained single-file executable | Publish with `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`. Output: a single `KernSmith.exe` (~80 MB) that includes the .NET runtime, FreeTypeSharp native DLLs, MonoGame/SDL2 native DLLs, and all managed assemblies. | M |
| 5.2 | MSIX installer | Create an MSIX package using the Windows Application Packaging Project or `MakeAppx.exe`. Benefits: clean install/uninstall, automatic updates via App Installer. Include: app manifest, visual assets (icons at 44x44, 71x71, 150x150, 310x310), file associations. | L |
| 5.3 | MSI installer (alternative) | Create an MSI using WiX Toolset v5. Components: main executable, FreeType and SDL2 native DLLs, Start Menu shortcut, optional desktop shortcut, file association for `.bmfc`. Custom actions: install/uninstall .NET runtime if not present. Include license agreement screen. | L |
| 5.4 | File association for `.bmfc` | Register `.bmfc` extension (KernSmith project file) in the registry: `HKCR\.bmfc` -> `KernSmith.ProjectFile`. Set default icon and "Open with KernSmith" verb. When double-clicked, launch KernSmith with the file path as argument. | S |
| 5.5 | Start Menu and taskbar integration | Create Start Menu shortcut: `%ProgramData%\Microsoft\Windows\Start Menu\Programs\KernSmith.lnk`. Pin-to-taskbar-capable. Include app ID for proper taskbar grouping: `KernSmith.Desktop`. | S |
| 5.6 | Context menu integration (optional) | Add "Generate Bitmap Font with KernSmith" to the right-click context menu for `.ttf`, `.otf`, and `.woff` files. Register via `HKCR\SystemFileAssociations\.ttf\shell\KernSmith`. Opens KernSmith with the font pre-loaded. | S |
| 5.7 | Uninstaller | MSI/MSIX uninstaller removes: application files, Start Menu shortcut, file associations, registry entries. Leaves user data in `%APPDATA%\KernSmith\` (settings, logs, auto-save). Prompt: "Do you want to remove saved settings and logs?" | S |
| 5.8 | Portable mode (xcopy deployment) | Support a `KernSmith.portable` sentinel file next to the executable. When present, store all settings, logs, and auto-save in a `data/` subdirectory next to the executable instead of `%APPDATA%`. No installation or registry modifications needed. | M |
| 5.9 | Authenticode code signing | Sign `KernSmith.exe` and the MSI/MSIX installer with an Authenticode certificate. Eliminates SmartScreen warnings. Use `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256`. Sign during CI/CD build pipeline. | M |
| 5.10 | Windows version compatibility | Target Windows 10 1809 (build 17763) and later. Test on Windows 10 21H2 and Windows 11 23H2. Verify .NET 10 runtime requirements. Document minimum OS version in installer prerequisites. MonoGame DesktopGL requires OpenGL 3.0+ (virtually all Windows GPUs since 2008). | S |

### macOS Distribution

| # | Task | Details | Effort |
|---|------|---------|--------|
| 5.11 | .app bundle creation | Post-publish script creates: `KernSmith.app/Contents/MacOS/KernSmith` (executable), `KernSmith.app/Contents/Resources/kernsmith.icns` (icon), `KernSmith.app/Contents/Info.plist` (metadata), `KernSmith.app/Contents/Frameworks/` (SDL2.framework, native libs). Set `LSMinimumSystemVersion` to `12.0`. | M |
| 5.12 | Info.plist configuration | Keys: `CFBundleIdentifier` = `com.kernsmith.desktop`, `CFBundleName` = `KernSmith`, `CFBundleVersion` = build number, `CFBundleShortVersionString` = semver, `CFBundleDocumentTypes` = `[{extensions: ["bmfc"], name: "KernSmith Project", role: "Editor"}]`, `NSHighResolutionCapable` = true. | S |
| 5.13 | DMG creation with background | Use `create-dmg` tool: `create-dmg --volname "KernSmith" --background bg.png --window-size 600 400 --icon KernSmith.app 150 200 --app-drop-link 450 200 KernSmith.dmg KernSmith.app`. Include a simple drag-to-install background image. | S |
| 5.14 | Code signing | Sign with: `codesign --deep --force --options runtime --sign "Developer ID Application: ..." KernSmith.app`. Enable hardened runtime (`--options runtime`). Sign embedded frameworks (SDL2) and native libraries individually. Verify with `codesign --verify --deep --strict`. | M |
| 5.15 | Notarization | Submit to Apple: `xcrun notarytool submit KernSmith.dmg --apple-id ... --team-id ... --password ...`. Wait for approval. Staple the notarization ticket: `xcrun stapler staple KernSmith.dmg`. Without notarization, macOS 10.15+ blocks the app entirely. | M |

### Linux Distribution

| # | Task | Details | Effort |
|---|------|---------|--------|
| 5.16 | AppImage creation | Use `appimagetool` to package: self-contained .NET publish output, SDL2 and FreeType shared libraries, `AppRun` script, `kernsmith.desktop`, `kernsmith.png` (icon, 256x256). Set executable permission. Test on Ubuntu 22.04, Fedora 38, and Arch Linux. AppImage should work without any installed dependencies. Bundle `libSDL2-2.0.so.0` to avoid system version conflicts. | M |
| 5.17 | Flatpak manifest | Create `org.kernsmith.KernSmith.yml`: runtime `org.freedesktop.Platform//23.08`, sdk `org.freedesktop.Sdk//23.08`, modules for .NET SDK (build only), FreeType, and SDL2, `finish-args: [--socket=x11, --socket=wayland, --share=ipc, --filesystem=home, --device=dri]`. The `--device=dri` permission is required for OpenGL acceleration. | M |
| 5.18 | .deb package (Debian/Ubuntu) | Create `.deb` using `dpkg-deb`. Install to `/opt/kernsmith/`. Dependencies: `libfreetype6, libsdl2-2.0-0`. Create symlink `/usr/local/bin/kernsmith`. Install `.desktop` file to `/usr/share/applications/`. Install icon to `/usr/share/icons/hicolor/256x256/apps/`. | M |
| 5.19 | .rpm package (Fedora/RHEL) | Create `.rpm` using `rpmbuild`. Same structure as `.deb`. Dependencies: `freetype, SDL2`. Spec file follows Fedora packaging guidelines. | M |
| 5.20 | Snap package (optional) | Create `snapcraft.yaml` using `base: core22`. `confinement: strict`. Plugs: `desktop`, `home`, `x11`, `wayland`, `opengl`. Build with `snapcraft`. Publish to Snap Store (future). The `opengl` plug is required for GPU access. | S |

### Auto-Update Mechanism

| # | Task | Details | Effort |
|---|------|---------|--------|
| 5.21 | Version check on startup | On launch (after the first frame is drawn), check `https://api.kernsmith.dev/v1/version` (or GitHub releases API) on a background thread. Compare with `Assembly.GetExecutingAssembly().GetName().Version`. If newer, show a non-intrusive notification bar (GUM element) at the top of the window: "KernSmith v2.1.0 is available. [Download] [Dismiss] [Don't remind me]". | M |
| 5.22 | Update check setting | In settings, add "Check for updates" dropdown: "On startup" (default), "Weekly", "Never". If "Never", skip the check entirely. Respect the user's choice. Store the last check timestamp and skip if within the interval. | S |
| 5.23 | Download update | "Download" button opens the default browser to the download page via `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`. Do not auto-download or auto-install; let the user control the process. | S |
| 5.24 | Changelog display | When an update is available, show a "What's new" section in the notification bar or a GUM dialog. Fetch from the releases API on a background thread. Display as a list of changes in a scrollable text area. | S |
| 5.25 | Offline behavior | If the version check fails (no network, DNS failure, timeout), silently ignore. Never block startup or show an error for a failed update check. Log the failure at `Debug` level. Timeout: 5 seconds for the HTTP request. | S |

---

## Wave 6: Telemetry & Diagnostics (Optional, Opt-in)

**Goal**: Provide opt-in usage analytics to guide development priorities, and diagnostic reporting to help users submit useful bug reports. All telemetry is optional, clearly disclosed, and contains no PII.

### Opt-in Usage Analytics

| # | Task | Details | Effort |
|---|------|---------|--------|
| 6.1 | First-launch consent dialog | On first launch, show a GUM dialog overlay: "KernSmith can collect anonymous usage data to help improve the application. No personal information or font data is collected. You can change this at any time in Settings." Buttons: "Yes, send anonymous data", "No, thanks". Default: opt-out. Store choice in settings JSON. | S |
| 6.2 | Telemetry data model | Define the event schema: `{ event: string, timestamp: ISO8601, sessionId: GUID, appVersion: string, os: string, properties: {} }`. Events: `app_started`, `font_loaded`, `generation_completed`, `generation_failed`, `feature_used`. Properties vary by event. No PII: no file paths, no font names, no IP addresses. | M |
| 6.3 | Event collection | Track: which features are used (SDF, effects, channel packing, multi-page atlas), common font sizes (bucket: small 8-16, medium 17-32, large 33-72, xlarge 73+), character set sizes (bucket: small <100, medium 100-500, large 500-5000, xlarge 5000+), output format preferences (PNG vs TGA vs DDS), error types and frequencies. | M |
| 6.4 | Local event buffer | Queue events locally in memory (`ConcurrentQueue<TelemetryEvent>`). Flush to the telemetry endpoint every 5 minutes or when the buffer reaches 50 events. If the endpoint is unreachable, discard events (do not persist unsent events). Flush on a background thread via `Task.Run()`. | S |
| 6.5 | Telemetry endpoint | POST events to `https://telemetry.kernsmith.dev/v1/events` (or use a lightweight service like Plausible, PostHog self-hosted, or Application Insights). Use `HttpClient` with a 5-second timeout. Fire-and-forget on a background thread; never block the game loop. | S |
| 6.6 | Disable telemetry completely | In settings, add "Send anonymous usage data" toggle. When off, no events are collected, buffered, or sent. The telemetry module is effectively dead code. Also respect a `KERNSMITH_NO_TELEMETRY=1` environment variable for CI/automation environments. | S |
| 6.7 | View collected data | In settings, add "View collected data" link that shows the current event buffer in a read-only GUM text area. Users can see exactly what would be sent. Transparency builds trust. | S |
| 6.8 | Session-level analytics only | Do not track individual user behavior over time. Each app session gets a random GUID. There is no persistent user ID. Sessions cannot be linked across launches. This is by design for privacy. | S |
| 6.9 | No font data transmission | Explicitly never include: font file contents, font file paths, font family names, generated atlas images, glyph data, file system paths, user name, machine name, IP address (the telemetry endpoint must not log IPs). Document this in the privacy policy. | S |
| 6.10 | Telemetry audit trail | Log all telemetry events sent at `Debug` level. Include the full event payload. This allows developers to verify that no PII is leaking. Run this audit during code review of any telemetry changes. | S |

### Diagnostic Report

| # | Task | Details | Effort |
|---|------|---------|--------|
| 6.11 | System info collection | Collect: OS name and version (`RuntimeInformation.OSDescription`), .NET runtime version (`Environment.Version`), MonoGame version (`typeof(Game).Assembly.GetName().Version`), KernSmith library version, GPU adapter (`GraphicsDevice.Adapter.Description`), OpenGL version, display scaling factor, available memory (`GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`), CPU architecture (`RuntimeInformation.ProcessArchitecture`), FreeType version (from native library), SDL2 video driver. | S |
| 6.12 | Diagnostic report format | Generate a plain-text report: header with system info (including GPU and OpenGL version), separator, last 50 log entries, separator, current configuration (anonymized: font size but not font path, texture size, character count but not which characters). Format for easy pasting into a GitHub issue. | M |
| 6.13 | "Copy diagnostic report" button | In the Help menu, add "Copy Diagnostic Report to Clipboard". Copy to clipboard via `SDL2.SDL.SDL_SetClipboardText()`. Show a GUM toast notification: "Diagnostic report copied to clipboard." Toast auto-dismisses after 3 seconds. | S |
| 6.14 | "Save diagnostic report" option | Alternative to clipboard: save to a `.txt` file via Save dialog. Default filename: `kernsmith-diagnostic-{timestamp}.txt`. Useful when clipboard is not available or the report is large. | S |
| 6.15 | Anonymize paths in report | Replace file paths with placeholders: `C:\Users\Jeremy\Documents\fonts\Roboto.ttf` becomes `<user>\Documents\fonts\Roboto.ttf`. Replace the user-specific portion with `<user>`. On Linux: `/home/jeremy/...` becomes `<user>/...`. | S |
| 6.16 | Include recent error details | If the app encountered an error in the current session, include the full exception details in the diagnostic report: type, message, stack trace. Truncate stack traces longer than 100 lines. | S |
| 6.17 | Report generation performance | Include the last 5 generation timing results: font, size, character count, texture size, total time, per-phase times. This helps diagnose performance complaints. Anonymize font names. | S |
| 6.18 | Link to issue tracker | At the bottom of the diagnostic report, include: "Submit this report at: https://github.com/kernsmith/kernsmith/issues/new". Make it easy for users to file bugs. | S |

---

## Core Library Notes (Document, Don't Fix)

These items document the current state of the KernSmith core library as it relates to UI integration. They are not implementation tasks for this phase; they are observations to record and potentially address in future library phases (see Phase 55 for planned core API additions including `CancellationToken` and `IProgress<T>` support).

| # | Item | Current State | Impact on UI | Future Action |
|---|------|---------------|--------------|---------------|
| N.1 | CancellationToken support | `BmFont.Builder().Build()` does not accept a `CancellationToken`. | Cannot cancel mid-generation. The UI can only discard stale results after `Build()` completes. | Phase 55: Add `CancellationToken` parameter to `Build()` and thread it through rasterization and packing loops. |
| N.2 | Progress reporting | No progress callback mechanism exists in the builder pipeline. | UI can only report "Generating..." and "Done". Cannot show per-glyph or percentage progress. | Phase 55: Add `IProgress<T>` parameter to `Build()` with phase and percentage reporting. |
| N.3 | Memory usage for large character sets | CJK generation (20K+ glyphs at 48px) can allocate 500MB+ for glyph bitmaps before packing. | Risk of `OutOfMemoryException` on machines with limited RAM. | Consider streaming rasterization: rasterize and pack in chunks rather than all-at-once. |
| N.4 | Platform-specific FreeType behavior | FreeType hinting renders slightly differently on Windows (ClearType-style) vs macOS (no hinting) vs Linux (autohinter). | Fonts may look different on different platforms with identical settings. Not a bug, but users may report it. | Document platform rendering differences. Consider adding a "hinting mode" option (none, light, full). |
| N.5 | System font enumeration performance | `ISystemFontProvider` scans font directories synchronously. On systems with 1,000+ fonts, this can take 2-5 seconds. | Blocks the game loop if called on the main thread. Already addressed by Wave 1 (background enumeration). | Consider caching or lazy enumeration in the library itself. |
| N.6 | Thread safety of `BmFont.Builder()` | The builder is not designed for concurrent use. A single builder instance should not be shared across threads. | UI must create a new builder for each generation request. Not a problem if following the Task.Run pattern. | Document thread safety guarantees in the API. |
| N.7 | FreeType native library loading | FreeTypeSharp loads native libraries from the NuGet package's `runtimes/` folder. On Linux AppImage, the library path may not resolve correctly. | May need to set `LD_LIBRARY_PATH` or use `NativeLibrary.SetDllImportResolver()`. | Test native library loading in all distribution formats (AppImage, Flatpak, deb, rpm). |
| N.8 | Texture2D creation from byte arrays | `BmFontResult` provides both `AtlasPage.PixelData` (raw RGBA) and `GetPngData()` (encoded PNG). The UI should prefer `AtlasPage.PixelData` for `Texture2D.SetData()` to avoid a decode step. If using `GetPngData()`, decode via `Texture2D.FromStream()` or StbImageSharp. | Using raw pixel data avoids a decode step on the main thread. | No action needed — raw pixel data is already available via `AtlasPage.PixelData`. |

---

## Success Criteria

| Criterion | Measurement | Target |
|-----------|-------------|--------|
| Generation never blocks the graphics thread | Frame time monitor in DEBUG builds | No frame >50ms during generation |
| Thread marshaling works correctly | Texture2D creation after background generation | No `InvalidOperationException` from cross-thread GraphicsDevice access |
| Large character sets work | Generate CJK font with 10K+ glyphs | Completes without crash, <30 seconds, <1 GB memory |
| Virtual character grid performs | Display 20K characters | Smooth scrolling at 60 FPS with <200 GUM elements |
| Windows compatibility | Tested on Windows 10 21H2, Windows 11 23H2 | All features functional, HiDPI correct, OpenGL 3.0+ verified |
| macOS compatibility | Tested on macOS 12+ (Intel and Apple Silicon) | Retina rendering, file dialogs, keyboard remapping all work |
| Linux compatibility | Tested on Ubuntu 22.04 (X11 + Wayland), Fedora 38 | File dialogs, font enumeration, OpenGL rendering all work |
| Keyboard accessibility | Complete workflow with keyboard only | Can load font, configure, generate, save — no mouse required |
| Screen reader limitation documented | Accessibility statement published | CLI alternative documented for screen reader users |
| Error handling | Trigger each error scenario | Clear message shown, no crash, no data loss |
| GPU error handling | Exceed max texture size, lose GL context | Graceful error message, no crash |
| Auto-save / crash recovery | Kill process during generation | Next launch offers to restore settings |
| Installer (Windows) | MSI or MSIX installer | Installs, creates shortcuts, uninstalls cleanly |
| Installer (macOS) | Signed and notarized .app in DMG | Installs without Gatekeeper warnings |
| Installer (Linux) | AppImage with bundled SDL2 + FreeType | Runs on Ubuntu 22.04 without installed dependencies |
| Startup time | Cold start measurement | <2 seconds to interactive on SSD |
| WCAG 2.1 AA compliance (visual) | Contrast ratio audit | All text meets 4.5:1 ratio |

---

## Dependencies

| Dependency | Required By | Notes |
|------------|-------------|-------|
| Phase 60 (UI MVP) | All waves | Core UI must exist before optimizing it |
| Phase 61-67 (UI features) | Waves 1-3 | Character grid, effects panel, and atlas preview must exist before adding accessibility/performance |
| MonoGame (DesktopGL) | All waves | Cross-platform OpenGL rendering; requires OpenGL 3.0+ |
| GUM UI (code-only) | Waves 1, 3 | Layout, controls, focus management |
| MonoGame.Extended | Waves 1-2 | Utility types, input handling |
| NativeFileDialogSharp | Wave 2 | Cross-platform native file open/save dialogs |
| FreeTypeSharp 3.1.0 | Wave 2 | Native binaries for all platforms (win-x64, osx-x64, osx-arm64, linux-x64) |
| SDL2 | Wave 2 | Bundled with MonoGame DesktopGL; clipboard, drag-and-drop, window management |
| WiX Toolset v5 | Wave 5 (5.3) | MSI installer creation (Windows only) |
| `create-dmg` | Wave 5 (5.13) | DMG creation (macOS only, build machine) |
| `appimagetool` | Wave 5 (5.16) | AppImage creation (Linux only, build machine) |
| Apple Developer ID certificate | Wave 5 (5.14-5.15) | Code signing and notarization (macOS) |
| Authenticode certificate | Wave 5 (5.9) | Code signing (Windows) |

---

## Estimated Effort

| Wave | Task Count | Effort |
|------|------------|--------|
| Wave 1: Performance Optimization | 36 tasks | X-Large |
| Wave 2: Cross-Platform Testing & Fixes | 35 tasks | Large |
| Wave 3: Accessibility | 24 tasks | Large |
| Wave 4: Error Handling & Recovery | 26 tasks | Large |
| Wave 5: Installer & Distribution | 25 tasks | Large |
| Wave 6: Telemetry & Diagnostics (Optional) | 18 tasks | Medium |
| **Total** | **164 tasks** | **X-Large** |
