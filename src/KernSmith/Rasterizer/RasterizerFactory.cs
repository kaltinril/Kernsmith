using System.Collections.Concurrent;

namespace KernSmith.Rasterizer;

/// <summary>
/// Thread-safe factory for creating rasterizer instances by backend type.
/// Backends register themselves via <see cref="Register"/> (typically from a [ModuleInitializer]).
/// </summary>
public static class RasterizerFactory
{
    private static readonly ConcurrentDictionary<RasterizerBackend, Func<IRasterizer>> Backends = new();

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
        return Backends.Keys.ToList();
    }

    /// <summary>Returns true if the specified backend has been registered.</summary>
    public static bool IsRegistered(RasterizerBackend backend) => Backends.ContainsKey(backend);

    /// <summary>
    /// Clears all registered backends and restores factory-default state.
    /// Intended for test isolation only.
    /// </summary>
    internal static void ResetForTesting()
    {
        Backends.Clear();
    }
}
