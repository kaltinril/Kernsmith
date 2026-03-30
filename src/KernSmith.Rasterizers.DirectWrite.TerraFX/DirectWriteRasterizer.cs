using System.Runtime.InteropServices;
using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace KernSmith.Rasterizers.DirectWrite.TerraFX;

/// <summary>
/// DirectWrite-based rasterizer backend using TerraFX.Interop.Windows.
/// Windows-only. Supports color fonts and variable fonts.
/// Uses IDWriteGlyphRunAnalysis for glyph bitmap rasterization.
/// </summary>
public sealed unsafe class DirectWriteRasterizer : IRasterizer
{
    private static readonly IRasterizerCapabilities DirectWriteCapabilitiesInstance = new DirectWriteCapabilities();

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => DirectWriteCapabilitiesInstance;

    private ComPtr<IDWriteFactory5> _factory;
    private ComPtr<IDWriteFontFace> _fontFace;
    private ComPtr<IDWriteInMemoryFontFileLoader> _inMemoryLoader;
    private GCHandle _pinnedFontData;
    private string? _familyName;
    private bool _disposed;
    private int _colorPaletteIndex;
    private Dictionary<string, float>? _variationAxes;

    // Font face creation parameters (needed to create simulation variants).
    private ComPtr<IDWriteFontFile> _fontFile;
    private DWRITE_FONT_FACE_TYPE _faceType;
    private uint _faceIndex;

    // Cached font faces per simulation flag combo (up to 4 variants).
    private readonly Dictionary<DWRITE_FONT_SIMULATIONS, ComPtr<IDWriteFontFace>> _simulatedFaces = new();

    /// <inheritdoc />
    public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_familyName is not null)
            throw new InvalidOperationException("Font already loaded. Create a new DirectWriteRasterizer instance.");

        var fontBytes = fontData.ToArray();
        _pinnedFontData = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);

        try
        {
            // Create IDWriteFactory5 (needed for in-memory font file loader).
            IDWriteFactory5* factory5;
            HRESULT hr = DWriteCreateFactory(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_ISOLATED,
                __uuidof<IDWriteFactory5>(),
                (IUnknown**)&factory5);
            ThrowIfFailed(hr, "DWriteCreateFactory (IDWriteFactory5)");
            _factory = new ComPtr<IDWriteFactory5>(factory5);

            // Create in-memory font file loader.
            IDWriteInMemoryFontFileLoader* inMemoryLoader;
            hr = factory5->CreateInMemoryFontFileLoader(&inMemoryLoader);
            ThrowIfFailed(hr, "CreateInMemoryFontFileLoader");
            _inMemoryLoader = new ComPtr<IDWriteInMemoryFontFileLoader>(inMemoryLoader);

            // Register the loader with the factory.
            hr = factory5->RegisterFontFileLoader((IDWriteFontFileLoader*)inMemoryLoader);
            ThrowIfFailed(hr, "RegisterFontFileLoader");

            // Create font file reference from in-memory data.
            IDWriteFontFile* fontFile;
            hr = inMemoryLoader->CreateInMemoryFontFileReference(
                (IDWriteFactory*)factory5,
                (void*)_pinnedFontData.AddrOfPinnedObject(),
                (uint)fontBytes.Length,
                null, // ownerObject - we manage lifetime via GCHandle
                &fontFile);
            ThrowIfFailed(hr, "CreateInMemoryFontFileReference");

            // Determine if font file is supported.
            BOOL isSupported;
            DWRITE_FONT_FILE_TYPE fileType;
            DWRITE_FONT_FACE_TYPE faceType;
            uint numberOfFaces;
            hr = fontFile->Analyze(&isSupported, &fileType, &faceType, &numberOfFaces);
            ThrowIfFailed(hr, "Analyze");

            if (!isSupported)
                throw new InvalidOperationException("Font file format is not supported by DirectWrite.");

            if (faceIndex < 0 || (uint)faceIndex >= numberOfFaces)
                throw new ArgumentOutOfRangeException(nameof(faceIndex),
                    $"Face index {faceIndex} is out of range. Font has {numberOfFaces} face(s).");

            // Keep font file alive for creating simulation variants later.
            _fontFile = new ComPtr<IDWriteFontFile>(fontFile);
            _faceType = faceType;
            _faceIndex = (uint)faceIndex;

            // Create the default (no-simulation) font face.
            IDWriteFontFace* fontFace;
            IDWriteFontFile* pFontFile = fontFile;
            hr = factory5->CreateFontFace(
                faceType,
                1, // numberOfFiles
                &pFontFile,
                (uint)faceIndex,
                DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE,
                &fontFace);
            ThrowIfFailed(hr, "CreateFontFace");
            _fontFace = new ComPtr<IDWriteFontFace>(fontFace);

            _familyName = "InMemoryFont";
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    /// <summary>
    /// Loads a system-installed font by family name.
    /// </summary>
    public void LoadSystemFont(string familyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_familyName is not null)
            throw new InvalidOperationException("Font already loaded. Create a new DirectWriteRasterizer instance.");

        ArgumentNullException.ThrowIfNull(familyName);

        if (string.IsNullOrWhiteSpace(familyName))
            throw new ArgumentException("Font family name cannot be empty.", nameof(familyName));

        try
        {
            // Create factory.
            IDWriteFactory5* factory5;
            HRESULT hr = DWriteCreateFactory(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                __uuidof<IDWriteFactory5>(),
                (IUnknown**)&factory5);
            ThrowIfFailed(hr, "DWriteCreateFactory");
            _factory = new ComPtr<IDWriteFactory5>(factory5);

            // Get system font collection.
            IDWriteFontCollection* fontCollection;
            hr = factory5->GetSystemFontCollection((IDWriteFontCollection**)&fontCollection, false);
            ThrowIfFailed(hr, "GetSystemFontCollection");

            try
            {
                // Find the font family.
                uint familyIndex;
                BOOL exists;
                fixed (char* pFamilyName = familyName)
                {
                    hr = fontCollection->FindFamilyName(pFamilyName, &familyIndex, &exists);
                }
                ThrowIfFailed(hr, "FindFamilyName");

                if (!exists)
                    throw new InvalidOperationException($"Font family '{familyName}' was not found in the system font collection.");

                // Get the font family.
                IDWriteFontFamily* fontFamily;
                hr = fontCollection->GetFontFamily(familyIndex, &fontFamily);
                ThrowIfFailed(hr, "GetFontFamily");

                try
                {
                    // Get the first font (normal weight/style/stretch).
                    IDWriteFont* font;
                    hr = fontFamily->GetFirstMatchingFont(
                        DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                        DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                        DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                        &font);
                    ThrowIfFailed(hr, "GetFirstMatchingFont");

                    try
                    {
                        // Create font face.
                        IDWriteFontFace* fontFace;
                        hr = font->CreateFontFace(&fontFace);
                        ThrowIfFailed(hr, "CreateFontFace");
                        _fontFace = new ComPtr<IDWriteFontFace>(fontFace);

                        // Extract the font file and face metadata so GetFontFaceForOptions
                        // can recreate the face with bold/italic simulations.
                        _faceType = fontFace->GetType();
                        _faceIndex = fontFace->GetIndex();

                        uint fileCount = 0;
                        hr = fontFace->GetFiles(&fileCount, null);
                        ThrowIfFailed(hr, "GetFiles (count)");
                        if (fileCount > 0)
                        {
                            IDWriteFontFile* fontFile;
                            hr = fontFace->GetFiles(&fileCount, &fontFile);
                            ThrowIfFailed(hr, "GetFiles");
                            _fontFile = new ComPtr<IDWriteFontFile>(fontFile);
                        }
                    }
                    finally
                    {
                        font->Release();
                    }
                }
                finally
                {
                    fontFamily->Release();
                }
            }
            finally
            {
                fontCollection->Release();
            }

            _familyName = familyName;

        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    /// <inheritdoc />
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        return RasterizeGlyphCore(codepoint, options);
    }

    /// <inheritdoc />
    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        var results = new List<RasterizedGlyph>();
        foreach (var codepoint in codepoints)
        {
            var glyph = RasterizeGlyphCore(codepoint, options);
            if (glyph is not null)
                results.Add(glyph);
        }

        return results;
    }

    /// <inheritdoc />
    public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        int aa = Math.Max(1, options.SuperSample);
        float fontSize = ComputeFontEmSize(options, aa);

        IDWriteFontFace* fontFace = GetFontFaceForOptions(options);

        ushort glyphIndex = MapCodepointToGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        DWRITE_FONT_METRICS fontMetrics;
        fontFace->GetMetrics(&fontMetrics);

        DWRITE_GLYPH_METRICS glyphMetrics;
        BOOL isSideways = false;
        HRESULT hr = fontFace->GetDesignGlyphMetrics(&glyphIndex, 1, &glyphMetrics, isSideways);
        if (hr.FAILED)
            return null;

        float scale = fontSize / fontMetrics.designUnitsPerEm;

        int bearingX = (int)Math.Round(glyphMetrics.leftSideBearing * scale) / aa;
        int bearingY = (int)Math.Round((fontMetrics.ascent) * scale) / aa;
        int advance = (int)Math.Round(glyphMetrics.advanceWidth * scale) / aa;
        int width = (int)Math.Round((glyphMetrics.advanceWidth - glyphMetrics.leftSideBearing - glyphMetrics.rightSideBearing) * scale) / aa;
        int height = (int)Math.Round((glyphMetrics.advanceHeight - glyphMetrics.topSideBearing - glyphMetrics.bottomSideBearing) * scale) / aa;

        return new GlyphMetrics(
            BearingX: bearingX,
            BearingY: bearingY,
            Advance: advance,
            Width: width,
            Height: height);
    }

    /// <summary>
    /// Returns null to use the shared OS/2 table metrics path. DirectWrite's own
    /// DWRITE_FONT_METRICS uses hhea typographic values (not OS/2 WinAscent/WinDescent),
    /// which produce incorrect lineHeight for many fonts. The shared path uses OS/2
    /// values with Math.Ceiling, which matches BMFont64 to ±1 pixel.
    /// </summary>
    public RasterizerFontMetrics? GetFontMetrics(RasterOptions options) => null;

    /// <summary>
    /// Returns null to delegate to the shared GPOS/kern table parser.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
    {
        return null;
    }

    /// <summary>
    /// Applies variable font axis values. Recreates the font face if needed.
    /// </summary>
    public void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _variationAxes = new Dictionary<string, float>(userAxes);
    }

    /// <summary>
    /// Stores the color palette index for future color font rendering.
    /// </summary>
    public void SelectColorPalette(int paletteIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _colorPaletteIndex = paletteIndex;
    }

    /// <summary>Releases unmanaged COM resources if Dispose was not called.</summary>
    ~DirectWriteRasterizer()
    {
        Dispose(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
    }

    // ── Private helpers ─────────────────────────────────────────────

    private void EnsureFontLoaded()
    {
        if (_familyName is null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont or LoadSystemFont first.");
    }

    /// <summary>
    /// Gets the appropriate font face for the given bold/italic options.
    /// Returns the base font face when no simulations are needed, or a cached
    /// simulated variant when bold/italic is requested.
    /// For system fonts (where we don't have the font file), simulations are
    /// not supported and the base font face is always returned.
    /// </summary>
    private IDWriteFontFace* GetFontFaceForOptions(RasterOptions options)
    {
        var simulations = DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE;
        if (options.Bold)
            simulations |= DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_BOLD;
        if (options.Italic)
            simulations |= DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_OBLIQUE;

        // No simulations needed — use the default font face.
        if (simulations == DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE)
            return _fontFace.Get;

        // If we don't have the font file (shouldn't happen), fall back to base face.
        if (_fontFile.Get == null)
            return _fontFace.Get;

        // Check cache.
        if (_simulatedFaces.TryGetValue(simulations, out var cached))
            return cached.Get;

        // Create a new font face with the requested simulations.
        IDWriteFontFace* simulatedFace;
        IDWriteFontFile* pFontFile = _fontFile.Get;
        HRESULT hr = ((IDWriteFactory*)_factory.Get)->CreateFontFace(
            _faceType,
            1,
            &pFontFile,
            _faceIndex,
            simulations,
            &simulatedFace);
        ThrowIfFailed(hr, $"CreateFontFace (simulations={simulations})");

        var comPtr = new ComPtr<IDWriteFontFace>(simulatedFace);
        _simulatedFaces[simulations] = comPtr;
        return comPtr.Get;
    }

    /// <summary>
    /// Computes the DirectWrite em size from the RasterOptions.
    /// DirectWrite font size is in DIPs (device-independent pixels).
    /// Points to pixels: emSize = pointSize * dpi / 72.
    /// </summary>
    private static float ComputeFontEmSize(RasterOptions options, int aa)
    {
        return options.Size * aa * options.Dpi / 72.0f;
    }

    private ushort MapCodepointToGlyphIndex(int codepoint)
    {
        uint cp = (uint)codepoint;
        ushort glyphIndex;
        HRESULT hr = _fontFace.Get->GetGlyphIndices(&cp, 1, &glyphIndex);
        if (hr.FAILED)
            return 0;
        return glyphIndex;
    }

    private RasterizedGlyph? RasterizeGlyphCore(int codepoint, RasterOptions options)
    {
        int aa = Math.Max(1, options.SuperSample);
        float fontSize = ComputeFontEmSize(options, aa);
        IDWriteFontFace* fontFace = GetFontFaceForOptions(options);

        ushort glyphIndex = MapCodepointToGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        // Get design metrics for positioning.
        DWRITE_FONT_METRICS fontMetrics;
        fontFace->GetMetrics(&fontMetrics);

        DWRITE_GLYPH_METRICS designMetrics;
        BOOL isSideways = false;
        HRESULT hr = fontFace->GetDesignGlyphMetrics(&glyphIndex, 1, &designMetrics, isSideways);
        if (hr.FAILED)
            return null;

        float scale = fontSize / fontMetrics.designUnitsPerEm;

        int bearingX = (int)Math.Round(designMetrics.leftSideBearing * scale) / aa;
        int bearingY = (int)Math.Round(fontMetrics.ascent * scale) / aa;
        int advance = (int)Math.Round(designMetrics.advanceWidth * scale) / aa;

        // Build DWRITE_GLYPH_RUN.
        float glyphAdvance = designMetrics.advanceWidth * scale;
        DWRITE_GLYPH_OFFSET glyphOffset = default;

        DWRITE_GLYPH_RUN glyphRun;
        glyphRun.fontFace = fontFace;
        glyphRun.fontEmSize = fontSize;
        glyphRun.glyphCount = 1;
        glyphRun.glyphIndices = &glyphIndex;
        glyphRun.glyphAdvances = &glyphAdvance;
        glyphRun.glyphOffsets = &glyphOffset;
        glyphRun.isSideways = 0;
        glyphRun.bidiLevel = 0;

        // Determine texture type based on anti-aliasing mode.
        DWRITE_TEXTURE_TYPE textureType = options.AntiAlias == AntiAliasMode.None
            ? DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_ALIASED_1x1
            : DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1;

        DWRITE_RENDERING_MODE renderingMode = options.AntiAlias == AntiAliasMode.None
            ? DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_ALIASED
            : (options.EnableHinting
                ? DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_NATURAL_SYMMETRIC
                : DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_NATURAL);

        // Create glyph run analysis.
        DWRITE_MATRIX transform = default;
        transform.m11 = 1.0f;
        transform.m22 = 1.0f;

        IDWriteGlyphRunAnalysis* analysis;
        hr = ((IDWriteFactory*)_factory.Get)->CreateGlyphRunAnalysis(
            &glyphRun,
            1.0f, // pixelsPerDip
            &transform,
            renderingMode,
            DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
            0.0f, // baselineOriginX
            0.0f, // baselineOriginY
            &analysis);
        if (hr.FAILED)
            return null;

        try
        {
            // Get the bounding box of the alpha texture.
            RECT textureBounds;
            hr = analysis->GetAlphaTextureBounds(textureType, &textureBounds);
            if (hr.FAILED)
                return null;

            int width = textureBounds.right - textureBounds.left;
            int height = textureBounds.bottom - textureBounds.top;

            // Zero-size glyph (e.g., space).
            if (width <= 0 || height <= 0)
            {
                return new RasterizedGlyph
                {
                    Codepoint = codepoint,
                    GlyphIndex = glyphIndex,
                    BitmapData = Array.Empty<byte>(),
                    Width = 0,
                    Height = 0,
                    Pitch = 0,
                    Metrics = new GlyphMetrics(
                        BearingX: bearingX,
                        BearingY: bearingY,
                        Advance: advance,
                        Width: 0,
                        Height: 0),
                    Format = PixelFormat.Grayscale8
                };
            }

            // Allocate buffer and get alpha texture.
            int bytesPerPixel = textureType == DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1 ? 3 : 1;
            int bufferSize = width * height * bytesPerPixel;
            var alphaBuffer = new byte[bufferSize];

            fixed (byte* pBuffer = alphaBuffer)
            {
                hr = analysis->CreateAlphaTexture(textureType, &textureBounds, pBuffer, (uint)bufferSize);
                if (hr.FAILED)
                    return null;
            }

            // Convert to grayscale (1 byte per pixel).
            byte[] bitmapData;
            if (bytesPerPixel == 3)
            {
                // ClearType 3x1: convert RGB subpixel to grayscale by averaging.
                bitmapData = new byte[width * height];
                for (int i = 0; i < width * height; i++)
                {
                    int r = alphaBuffer[i * 3];
                    int g = alphaBuffer[i * 3 + 1];
                    int b = alphaBuffer[i * 3 + 2];
                    bitmapData[i] = (byte)((r + g + b) / 3);
                }
            }
            else
            {
                bitmapData = alphaBuffer;
            }

            // Update metrics from the actual rendered bounds.
            // textureBounds is relative to the baseline origin (0,0).
            int renderedBearingX = textureBounds.left / aa;
            int renderedBearingY = -textureBounds.top / aa;

            // Downscale bitmap by averaging aa x aa blocks when supersampling.
            if (aa > 1)
            {
                int newWidth = width / aa;
                int newHeight = height / aa;
                if (newWidth == 0) newWidth = 1;
                if (newHeight == 0) newHeight = 1;

                var downscaled = new byte[newWidth * newHeight];
                int aaSq = aa * aa;

                for (int dy = 0; dy < newHeight; dy++)
                {
                    for (int dx = 0; dx < newWidth; dx++)
                    {
                        int sum = 0;
                        for (int sy = 0; sy < aa && (dy * aa + sy) < height; sy++)
                            for (int sx = 0; sx < aa && (dx * aa + sx) < width; sx++)
                                sum += bitmapData[(dy * aa + sy) * width + (dx * aa + sx)];
                        downscaled[dy * newWidth + dx] = (byte)(sum / aaSq);
                    }
                }

                bitmapData = downscaled;
                width = newWidth;
                height = newHeight;
            }

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = bitmapData,
                Width = width,
                Height = height,
                Pitch = width,
                Metrics = new GlyphMetrics(
                    BearingX: renderedBearingX,
                    BearingY: renderedBearingY,
                    Advance: advance,
                    Width: width,
                    Height: height),
                Format = PixelFormat.Grayscale8
            };
        }
        finally
        {
            analysis->Release();
        }
    }

    private void Cleanup()
    {
        // Release cached simulation font face variants.
        foreach (var kvp in _simulatedFaces)
        {
            var face = kvp.Value;
            if (!face.IsNull)
                face.Release();
        }
        _simulatedFaces.Clear();

        if (!_fontFace.IsNull)
            _fontFace.Release();

        if (!_fontFile.IsNull)
            _fontFile.Release();

        if (!_inMemoryLoader.IsNull && !_factory.IsNull)
        {
            ((IDWriteFactory*)_factory.Get)->UnregisterFontFileLoader((IDWriteFontFileLoader*)_inMemoryLoader.Get);
            _inMemoryLoader.Release();
        }

        if (!_factory.IsNull)
            _factory.Release();

        if (_pinnedFontData.IsAllocated)
            _pinnedFontData.Free();
    }

    private static void ThrowIfFailed(HRESULT hr, string operation)
    {
        if (hr.FAILED)
            throw new InvalidOperationException($"DirectWrite operation '{operation}' failed with HRESULT 0x{hr.Value:X8}.");
    }
}
