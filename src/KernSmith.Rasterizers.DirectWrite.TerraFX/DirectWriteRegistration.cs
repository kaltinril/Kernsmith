using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.DirectWrite.TerraFX;

internal static class DirectWriteRegistration
{
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-registration
    [ModuleInitializer]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.DirectWrite, () => new DirectWriteRasterizer());
    }
#pragma warning restore CA2255
}
