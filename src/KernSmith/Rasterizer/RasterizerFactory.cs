using System.Collections.Concurrent;

namespace KernSmith.Rasterizer;

/// <summary>
/// Thread-safe factory for creating rasterizer instances by backend type.
/// FreeType is pre-registered.
/// </summary>
public static class RasterizerFactory
{
    private static readonly ConcurrentDictionary<RasterizerBackend, Func<IRasterizer>> Backends = new();

    static RasterizerFactory()
    {
        Backends[RasterizerBackend.FreeType] = () => new FreeTypeRasterizer();
    }

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
    /// <see cref="RasterizerBackend.Auto"/> resolves to FreeType.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the backend is not registered.</exception>
    public static IRasterizer Create(RasterizerBackend backend)
    {
        var resolved = backend == RasterizerBackend.Auto ? RasterizerBackend.FreeType : backend;

        if (Backends.TryGetValue(resolved, out var factory))
            return factory();

        throw new InvalidOperationException(
            $"Rasterizer backend '{backend}' is not registered. " +
            $"Available backends: {string.Join(", ", GetAvailableBackends())}. " +
            $"Call RasterizerFactory.Register to add a backend.");
    }

    /// <summary>
    /// Returns all registered backends plus <see cref="RasterizerBackend.Auto"/>.
    /// </summary>
    public static IReadOnlyList<RasterizerBackend> GetAvailableBackends()
    {
        var backends = new List<RasterizerBackend> { RasterizerBackend.Auto };
        backends.AddRange(Backends.Keys);
        return backends;
    }
}
