using KernSmith.Ui;

#if WINDOWS
// Force backend assembly loading so [ModuleInitializer] registers them with RasterizerFactory
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
    typeof(KernSmith.Rasterizers.Gdi.GdiRasterizer).Module.ModuleHandle);
System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
    typeof(KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer).Module.ModuleHandle);
#endif

using var game = new KernSmithGame();
game.Run();
