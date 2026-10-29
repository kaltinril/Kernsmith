using KernSmith;

// Usage: dotnet run --project samples/KernSmith.Samples.Minimal [-- --rasterizer <backend>] [--font <path>]
// Backends: stbtruetype (default) or native (experimental, referenced from source).
var backend = RasterizerBackend.StbTrueType;
string? fontPath = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--rasterizer")
        backend = Enum.Parse<RasterizerBackend>(args[i + 1], ignoreCase: true);
    else if (args[i] == "--font")
        fontPath = Path.GetFullPath(args[i + 1]);
}

using var game = new KernSmith.Samples.Minimal.Game1(backend, fontPath);
game.Run();
