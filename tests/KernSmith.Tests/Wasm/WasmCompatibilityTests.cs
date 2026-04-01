using System.Reflection;
using System.Runtime.InteropServices;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Wasm;

public class WasmCompatibilityTests
{
    [Fact]
    public void CoreAssembly_HasNoPInvoke()
    {
        var assembly = typeof(BmFont).Assembly;
        var pinvokeMethods = FindPInvokeMethods(assembly);
        pinvokeMethods.ShouldBeEmpty(
            $"Core KernSmith assembly contains P/Invoke: {FormatMethods(pinvokeMethods)}");
    }

    [Fact]
    public void StbTrueTypeAssembly_HasNoPInvoke()
    {
        var assembly = typeof(StbTrueTypeRasterizer).Assembly;
        var pinvokeMethods = FindPInvokeMethods(assembly);
        pinvokeMethods.ShouldBeEmpty(
            $"StbTrueType assembly contains P/Invoke: {FormatMethods(pinvokeMethods)}");
    }

    [Fact]
    public void StbTrueTypeSharpAssembly_HasNoPInvoke()
    {
        var assembly = typeof(StbTrueTypeSharp.StbTrueType).Assembly;
        var pinvokeMethods = FindPInvokeMethods(assembly);
        pinvokeMethods.ShouldBeEmpty(
            $"StbTrueTypeSharp assembly contains P/Invoke: {FormatMethods(pinvokeMethods)}");
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceFreeTypeSharp()
    {
        var references = typeof(BmFont).Assembly.GetReferencedAssemblies();
        references.ShouldNotContain(
            r => r.Name!.Contains("FreeType", StringComparison.OrdinalIgnoreCase),
            "Core assembly should not reference FreeTypeSharp");
    }

    private static List<MethodInfo> FindPInvokeMethods(Assembly assembly)
    {
        var results = new List<MethodInfo>();
        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<DllImportAttribute>() is not null)
                    results.Add(method);
                else if (method.GetCustomAttributes().Any(a => a.GetType().Name == "LibraryImportAttribute"))
                    results.Add(method);
            }
        }
        return results;
    }

    private static string FormatMethods(List<MethodInfo> methods) =>
        string.Join(", ", methods.Select(m => $"{m.DeclaringType?.Name}.{m.Name}"));
}
