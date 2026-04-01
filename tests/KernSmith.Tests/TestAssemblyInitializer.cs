using System.Runtime.CompilerServices;
using KernSmith.Rasterizers.FreeType;

namespace KernSmith.Tests;

/// <summary>
/// Ensures rasterizer plugin assemblies are loaded before any test runs.
/// Plugin assemblies use [ModuleInitializer] to auto-register their backends,
/// but the runtime only loads an assembly when a type from it is first accessed.
/// This module initializer forces the FreeType plugin assembly to load so its
/// registration fires before any test that depends on RasterizerFactory.
/// </summary>
internal static class TestAssemblyInitializer
{
#pragma warning disable CA2255 // ModuleInitializer is intentional
    [ModuleInitializer]
    internal static void Initialize()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(FreeTypeRasterizer).Module.ModuleHandle);
    }
#pragma warning restore CA2255
}
