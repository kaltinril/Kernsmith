using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.FreeType;

internal static class FreeTypeRegistration
{
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-registration
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.FreeType, () => new FreeTypeRasterizer());
    }
#pragma warning restore CA2255
}
