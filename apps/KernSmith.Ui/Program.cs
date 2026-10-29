using KernSmith.Ui;

using KernSmith.Ui.Services;

// --ui-rasterizer <backend>: render the app's own Gum text with that backend (see README).
using var game = new KernSmithGame(UiFontRasterizer.Parse(args));
game.Run();
