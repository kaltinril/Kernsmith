using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Gdi;

internal static class GdiRegistration
{
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-registration
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.Gdi, () => new GdiRasterizer());
    }
#pragma warning restore CA2255
}
