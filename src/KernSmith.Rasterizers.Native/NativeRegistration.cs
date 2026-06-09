using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

internal static class NativeRegistration
{
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-registration
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());
    }
#pragma warning restore CA2255
}
