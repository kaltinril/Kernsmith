using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace KernSmith.Rasterizer;

/// <summary>
/// Thread-safe factory for creating rasterizer instances by backend type.
/// Built-in backends are auto-discovered on first access via <see cref="Create"/>,
/// <see cref="GetAvailableBackends"/>, or <see cref="IsRegistered"/>.
/// Custom backends can register manually via <see cref="Register"/>.
/// </summary>
public static class RasterizerFactory
{
    private static readonly ConcurrentDictionary<RasterizerBackend, Func<IRasterizer>> Backends = new();

    private static readonly (RasterizerBackend Backend, string TypeName)[] KnownBackends =
    [
        (RasterizerBackend.FreeType, "KernSmith.Rasterizers.FreeType.FreeTypeRegistration, KernSmith.Rasterizers.FreeType"),
        (RasterizerBackend.StbTrueType, "KernSmith.Rasterizers.StbTrueType.StbTrueTypeRegistration, KernSmith.Rasterizers.StbTrueType"),
        (RasterizerBackend.Gdi, "KernSmith.Rasterizers.Gdi.GdiRegistration, KernSmith.Rasterizers.Gdi"),
        (RasterizerBackend.DirectWrite, "KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRegistration, KernSmith.Rasterizers.DirectWrite.TerraFX"),
    ];

    private static readonly object _discoveryLock = new();
    private static volatile bool _discoveryComplete;

    /// <summary>
    /// Registers a factory function for the specified backend.
    /// </summary>
    public static void Register(RasterizerBackend backend, Func<IRasterizer> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Backends[backend] = factory;
    }

    /// <summary>
    /// Creates a rasterizer instance for the specified backend.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the backend is not registered.</exception>
    public static IRasterizer Create(RasterizerBackend backend)
    {
        if (Backends.TryGetValue(backend, out var factory))
            return factory();

        DiscoverBackends();

        if (Backends.TryGetValue(backend, out factory))
            return factory();

        var available = GetAvailableBackends();
        var availableText = available.Count > 0
            ? $"Available backends: {string.Join(", ", available)}."
            : "No backends have been registered.";

        throw new InvalidOperationException(
            $"Rasterizer backend '{backend}' is not registered. " +
            $"{availableText} " +
            $"Install the corresponding NuGet package (e.g., KernSmith.Rasterizers.StbTrueType) and ensure it is referenced by your project.");
    }

    /// <summary>
    /// Returns all registered backends.
    /// </summary>
    public static IReadOnlyList<RasterizerBackend> GetAvailableBackends()
    {
        DiscoverBackends();
        return Backends.Keys.ToList();
    }

    /// <summary>Returns true if the specified backend has been registered.</summary>
    public static bool IsRegistered(RasterizerBackend backend)
    {
        if (Backends.ContainsKey(backend))
            return true;

        DiscoverBackends();
        return Backends.ContainsKey(backend);
    }

    /// <summary>
    /// Clears all registered backends and restores factory-default state.
    /// Intended for test isolation only.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (_discoveryLock)
        {
            Backends.Clear();
            _discoveryComplete = false;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2057",
        Justification = "Rasterizer backend assemblies are optional runtime dependencies loaded by name.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Registration types are in rasterizer backend assemblies that are not trimmed.")]
    private static void DiscoverBackends()
    {
        if (_discoveryComplete)
            return;

        lock (_discoveryLock)
        {
            if (_discoveryComplete)
                return;

            foreach (var (_, typeName) in KnownBackends)
            {
                try
                {
                    var type = Type.GetType(typeName);
                    if (type is null)
                        continue;

                    // Invoke the Register() method directly instead of RunModuleConstructor,
                    // because module constructors are one-shot and won't re-fire after
                    // ResetForTesting() clears the registrations.
                    var registerMethod = type.GetMethod("Register",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (registerMethod is not null)
                        registerMethod.Invoke(null, null);
                    else
                        RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
                }
                catch (Exception)
                {
                    // Silently skip backends that fail to load
                }
            }

            _discoveryComplete = true;
        }
    }
}
