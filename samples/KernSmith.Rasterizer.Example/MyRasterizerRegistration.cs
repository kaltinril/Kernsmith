using System.Runtime.CompilerServices;
using KernSmith;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.MyRasterizer;

/// <summary>
/// Auto-registers this backend with <see cref="RasterizerFactory"/> when the assembly loads.
/// No manual registration code is needed — just reference this assembly.
/// </summary>
internal static class MyRasterizerRegistration
{
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-registration
    [ModuleInitializer]
    internal static void Register()
    {
        // Use a unique numeric value (100+) to avoid collisions with built-in backends.
        // Built-in values: FreeType=0, Gdi=1, DirectWrite=2
        RasterizerFactory.Register((RasterizerBackend)100, () => new MyRasterizer());
    }
#pragma warning restore CA2255
}
