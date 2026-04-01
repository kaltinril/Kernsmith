using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.StbTrueType;

internal static class StbTrueTypeRegistration
{
#pragma warning disable CA2255 // ModuleInitializer is intentional for auto-registration
    [ModuleInitializer]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(StbTrueTypeRasterizer))]
    internal static void Register()
    {
        RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
    }
#pragma warning restore CA2255
}
