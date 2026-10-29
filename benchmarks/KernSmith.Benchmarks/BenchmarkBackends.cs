using KernSmith;
using KernSmith.Rasterizer;

/// <summary>
/// Backend lists for <c>[ParamsSource]</c>, filtered to the backends this build can create.
/// </summary>
internal static class BenchmarkBackends
{
    /// <summary>
    /// Every backend in the Native comparison. GDI and DirectWrite are only referenced by the
    /// <c>net10.0-windows</c> build, so other builds leave them out instead of failing to create them.
    /// </summary>
    public static IEnumerable<RasterizerBackend> Comparison { get; } = Available(
        RasterizerBackend.Native,
        RasterizerBackend.StbTrueType,
        RasterizerBackend.FreeType,
        RasterizerBackend.Gdi,
        RasterizerBackend.DirectWrite);

    /// <summary>The cross-platform backends with per-feature support worth comparing against Native.</summary>
    public static IEnumerable<RasterizerBackend> Portable { get; } = Available(
        RasterizerBackend.Native,
        RasterizerBackend.StbTrueType,
        RasterizerBackend.FreeType);

    private static RasterizerBackend[] Available(params RasterizerBackend[] candidates) =>
        candidates.Where(RasterizerFactory.IsRegistered).ToArray();
}
