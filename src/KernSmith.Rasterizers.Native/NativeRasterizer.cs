using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Fully custom, pure C# rasterizer backend owned entirely by KernSmith. Zero external
/// dependencies. Cross-platform and WASM/AOT-friendly.
/// </summary>
/// <remarks>
/// This is the Phase 161 scaffold: it loads and validates a font and parses the core
/// tables (<c>head</c>, <c>hhea</c>, <c>hmtx</c>, <c>OS/2</c>, <c>cmap</c>), but glyph
/// outline decoding and rasterization arrive in later phases (162+). Rendering methods
/// throw <see cref="NotImplementedException"/> until then.
///
/// Instances are NOT thread-safe (Phase 160, D9). Create one instance per thread for
/// parallel rasterization.
/// </remarks>
public sealed class NativeRasterizer : IRasterizer
{
    private static readonly IRasterizerCapabilities CapabilitiesInstance = new NativeCapabilities();

    private NativeFontFace? _face;
    private bool _disposed;

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => CapabilitiesInstance;

    /// <inheritdoc />
    public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_face is not null)
            throw new InvalidOperationException("Font already loaded. Create a new NativeRasterizer instance.");

        _face = NativeFontFace.Load(fontData, faceIndex);
    }

    /// <summary>
    /// Not supported. The native rasterizer cannot load system fonts by name.
    /// </summary>
    public void LoadSystemFont(string familyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException(
            "Native rasterizer does not support loading system fonts by name. Use LoadFont with font bytes instead.");
    }

    /// <inheritdoc />
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        throw new NotImplementedException(
            "Native rasterizer glyph rendering is implemented in a later phase (162+).");
    }

    /// <inheritdoc />
    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        throw new NotImplementedException(
            "Native rasterizer glyph rendering is implemented in a later phase (162+).");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _face = null;
    }

    private void EnsureFontLoaded()
    {
        if (_face is null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");
    }

    /// <summary>The glyph index a codepoint maps to, or 0 when unmapped. Internal until rendering lands.</summary>
    internal int GetGlyphIndex(int codepoint)
    {
        EnsureFontLoaded();
        return _face!.GetGlyphIndex(codepoint);
    }

    /// <summary>The parsed font face. Internal access for tests and later-phase decoders.</summary>
    internal NativeFontFace? Face => _face;
}
