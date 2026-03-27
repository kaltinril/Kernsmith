using System.Runtime.InteropServices;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using static KernSmith.Rasterizers.Gdi.NativeMethods;

namespace KernSmith.Rasterizers.Gdi;

/// <summary>
/// Windows GDI-based rasterizer backend. Produces output compatible with BMFont's built-in rasterizer.
/// Windows-only; uses GetGlyphOutlineW with GGO_GRAY8_BITMAP for grayscale rendering.
/// </summary>
public sealed class GdiRasterizer : IRasterizer
{
    private static readonly IRasterizerCapabilities GdiCapabilitiesInstance = new GdiCapabilities();

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => GdiCapabilitiesInstance;

    private IntPtr _fontResourceHandle;
    private GCHandle _pinnedFontData;
    private string? _familyName;
    private bool _disposed;

    /// <summary>
    /// Loads a system-installed font by family name, letting GDI's font mapper resolve it.
    /// This matches BMFont's behavior for system fonts (e.g., "Arial", "Batang").
    /// </summary>
    public void LoadSystemFont(string familyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_familyName is not null)
            throw new InvalidOperationException("Font already loaded. Create a new GdiRasterizer instance.");

        ArgumentNullException.ThrowIfNull(familyName);

        if (string.IsNullOrWhiteSpace(familyName))
            throw new ArgumentException("Font family name cannot be empty.", nameof(familyName));

        _familyName = familyName;
    }

    /// <inheritdoc />
    public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_familyName is not null)
            throw new InvalidOperationException("Font already loaded. Create a new GdiRasterizer instance.");

        if (faceIndex != 0)
            throw new NotSupportedException("GDI rasterizer does not support font collection face selection. Use faceIndex 0 or use FreeType backend for TTC files.");

        var fontBytes = fontData.ToArray();
        _pinnedFontData = GCHandle.Alloc(fontBytes, GCHandleType.Pinned);

        try
        {
            uint numFonts = 0;
            _fontResourceHandle = AddFontMemResourceEx(
                _pinnedFontData.AddrOfPinnedObject(),
                (uint)fontBytes.Length,
                IntPtr.Zero,
                ref numFonts);

            if (_fontResourceHandle == IntPtr.Zero)
                throw new InvalidOperationException("AddFontMemResourceEx failed to register the font.");

            _familyName = ParseFamilyName(fontBytes);

            if (string.IsNullOrEmpty(_familyName))
                throw new InvalidOperationException("Could not read font family name from the TTF name table.");
        }
        catch
        {
            // Clean up on failure.
            if (_fontResourceHandle != IntPtr.Zero)
            {
                RemoveFontMemResourceEx(_fontResourceHandle);
                _fontResourceHandle = IntPtr.Zero;
            }

            if (_pinnedFontData.IsAllocated)
                _pinnedFontData.Free();

            throw;
        }
    }

    /// <inheritdoc />
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        int aa = Math.Max(1, options.SuperSample);

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new InvalidOperationException("CreateCompatibleDC failed.");

        try
        {
            SetMapMode(hdc, MM_TEXT);
            var hFont = CreateHFont(options, options.Size * aa);
            var oldFont = SelectObject(hdc, hFont);

            try
            {
                var result = RasterizeGlyphCore(hdc, codepoint, options, aa);
                return result;
            }
            finally
            {
                SelectObject(hdc, oldFont);
                DeleteObject(hFont);
            }
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        int aa = Math.Max(1, options.SuperSample);
        var results = new List<RasterizedGlyph>();

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new InvalidOperationException("CreateCompatibleDC failed.");

        try
        {
            SetMapMode(hdc, MM_TEXT);
            var hFont = CreateHFont(options, options.Size * aa);
            var oldFont = SelectObject(hdc, hFont);

            try
            {
                foreach (var codepoint in codepoints)
                {
                    var glyph = RasterizeGlyphCore(hdc, codepoint, options, aa);
                    if (glyph is not null)
                        results.Add(glyph);
                }
            }
            finally
            {
                SelectObject(hdc, oldFont);
                DeleteObject(hFont);
            }
        }
        finally
        {
            DeleteDC(hdc);
        }

        return results;
    }

    /// <inheritdoc />
    public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new InvalidOperationException("CreateCompatibleDC failed.");

        try
        {
            SetMapMode(hdc, MM_TEXT);
            var hFont = CreateHFont(options);
            var oldFont = SelectObject(hdc, hFont);

            try
            {
                var mat2 = MAT2.Identity;
                var size = GetGlyphOutlineW(
                    hdc,
                    (uint)codepoint,
                    GGO_METRICS,
                    out var gm,
                    0,
                    IntPtr.Zero,
                    ref mat2);

                if (size == GDI_ERROR)
                    return null;

                return new GlyphMetrics(
                    BearingX: gm.GmptGlyphOrigin.X,
                    BearingY: gm.GmptGlyphOrigin.Y,
                    Advance: gm.GmCellIncX,
                    Width: (int)gm.GmBlackBoxX,
                    Height: (int)gm.GmBlackBoxY);
            }
            finally
            {
                SelectObject(hdc, oldFont);
                DeleteObject(hFont);
            }
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    /// <summary>
    /// Returns font-level metrics (ascent, descent, line height) from GDI's TEXTMETRIC.
    /// When supersampling, creates the HFONT at size*aa and divides results by aa (ceiling),
    /// matching BMFont's approach.
    /// </summary>
    public RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new InvalidOperationException("CreateCompatibleDC failed.");

        try
        {
            SetMapMode(hdc, MM_TEXT);
            var hFont = CreateHFont(options);
            var oldFont = SelectObject(hdc, hFont);

            try
            {
                if (!GetTextMetricsW(hdc, out var tm))
                    return null;

                return new RasterizerFontMetrics
                {
                    Ascent = tm.TmAscent,
                    Descent = tm.TmDescent,
                    LineHeight = tm.TmHeight
                };
            }
            finally
            {
                SelectObject(hdc, oldFont);
                DeleteObject(hFont);
            }
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    /// <summary>
    /// Returns null to let the shared GPOS/kern parser handle kerning scaling.
    /// <para>
    /// BMFont's documented behavior is: try <c>GetKerningPairsW</c> first, then fall back to
    /// GPOS parsing with <c>otmrcFontBox</c>-based scaling. In practice, BMFont (32-bit) gets
    /// zero pairs from <c>GetKerningPairsW</c> for fonts like Bell MT whose kerning is in the
    /// GPOS table, and uses GPOS-scaled values. The 64-bit <c>GetKerningPairsW</c> returns
    /// legacy kern table data for those same fonts, producing completely different (wrong)
    /// amounts. Returning null here delegates to the shared GPOS parser, which produces
    /// values consistent with BMFont's output. For fonts where <c>GetKerningPairsW</c> and
    /// GPOS agree (Arial, Bahnschrift), the GPOS path produces equivalent results because
    /// their <c>unitsPerEm</c> matches <c>head.yMax - head.yMin</c>.
    /// </para>
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
    {
        return null;
    }

    /// <summary>Releases unmanaged font resources if Dispose was not called.</summary>
    ~GdiRasterizer()
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

        if (_fontResourceHandle != IntPtr.Zero)
        {
            RemoveFontMemResourceEx(_fontResourceHandle);
            _fontResourceHandle = IntPtr.Zero;
        }

        if (_pinnedFontData.IsAllocated)
            _pinnedFontData.Free();
    }

    // ── Private helpers ─────────────────────────────────────────────

    private void EnsureFontLoaded()
    {
        if (_familyName is null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont or LoadSystemFont first.");
    }

    private IntPtr CreateHFont(RasterOptions options, int? sizeOverride = null)
    {
        int size = sizeOverride ?? options.Size;
        var logFont = new LOGFONTW
        {
            LfHeight = (int)(-(long)size * options.Dpi / 72),
            LfWidth = 0,
            LfEscapement = 0,
            LfOrientation = 0,
            LfWeight = options.Bold ? FW_BOLD : FW_NORMAL,
            LfItalic = (byte)(options.Italic ? 1 : 0),
            LfUnderline = 0,
            LfStrikeOut = 0,
            LfCharSet = DEFAULT_CHARSET,
            LfOutPrecision = OUT_TT_PRECIS,
            LfClipPrecision = CLIP_DEFAULT_PRECIS,
            LfQuality = options.AntiAlias == AntiAliasMode.None
                ? NONANTIALIASED_QUALITY
                : ANTIALIASED_QUALITY,
            LfPitchAndFamily = DEFAULT_PITCH | FF_DONTCARE,
            LfFaceName = _familyName
        };

        var hFont = CreateFontIndirectW(ref logFont);
        if (hFont == IntPtr.Zero)
            throw new InvalidOperationException($"CreateFontIndirectW failed for font '{_familyName}'.");

        return hFont;
    }

    private static RasterizedGlyph? RasterizeGlyphCore(IntPtr hdc, int codepoint, RasterOptions options, int aa)
    {
        var mat2 = MAT2.Identity;

        // First call: get the required buffer size.
        var bufferSize = GetGlyphOutlineW(
            hdc,
            (uint)codepoint,
            GGO_GRAY8_BITMAP,
            out var gm,
            0,
            IntPtr.Zero,
            ref mat2);

        if (bufferSize == GDI_ERROR)
            return null;

        // Resolve the glyph index for this codepoint.
        var glyphIndex = GetGlyphIndex(hdc, codepoint);

        int bearingX = gm.GmptGlyphOrigin.X / aa;
        int bearingY = gm.GmptGlyphOrigin.Y / aa;
        int advance = gm.GmCellIncX / aa;

        int width = (int)gm.GmBlackBoxX;
        int height = (int)gm.GmBlackBoxY;

        // Zero-size glyph (e.g. space) — return metrics with empty bitmap.
        if (bufferSize == 0 || width == 0 || height == 0)
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

        // Second call: retrieve the bitmap data.
        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            var result = GetGlyphOutlineW(
                hdc,
                (uint)codepoint,
                GGO_GRAY8_BITMAP,
                out gm,
                bufferSize,
                buffer,
                ref mat2);

            if (result == GDI_ERROR)
                return null;

            // GGO_GRAY8_BITMAP rows are DWORD-aligned.
            int srcPitch = (width + 3) & ~3;
            if ((long)srcPitch * height > bufferSize)
                return null;

            // Output: tightly packed rows (1 byte per pixel, width bytes per row).
            long totalPixels = (long)width * height;
            if (totalPixels > int.MaxValue)
                return null;
            var bitmapData = new byte[totalPixels];

            unsafe
            {
                var src = (byte*)buffer;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // GGO_GRAY8_BITMAP values are 0-64, remap to 0-255.
                        byte value = src[y * srcPitch + x];
                        bitmapData[y * width + x] = (byte)Math.Min(value * 255 / 64, 255);
                    }
                }
            }

            // Downscale bitmap by averaging aa x aa blocks when supersampling.
            if (aa > 1)
            {
                int newWidth = width / aa;
                int newHeight = height / aa;
                var downscaled = new byte[newWidth * newHeight];
                int aaSq = aa * aa;

                for (int dy = 0; dy < newHeight; dy++)
                {
                    for (int dx = 0; dx < newWidth; dx++)
                    {
                        int sum = 0;
                        for (int sy = 0; sy < aa; sy++)
                            for (int sx = 0; sx < aa; sx++)
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
                    BearingX: bearingX,
                    BearingY: bearingY,
                    Advance: advance,
                    Width: width,
                    Height: height),
                Format = PixelFormat.Grayscale8
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Resolves the internal glyph index for a codepoint using GetGlyphIndicesW.
    /// Falls back to the codepoint itself if the call fails.
    /// </summary>
    private static int GetGlyphIndex(IntPtr hdc, int codepoint)
    {
        // GetGlyphIndicesW works with UTF-16 characters.
        var text = char.ConvertFromUtf32(codepoint);
        var indices = new ushort[text.Length];
        var result = GetGlyphIndicesW(hdc, text, text.Length, indices, GGI_MARK_NONEXISTING_GLYPHS);

        if (result > 0 && indices[0] != 0xFFFF)
            return indices[0];

        return codepoint;
    }

    /// <summary>
    /// Parses the font family name (nameID=1) from the TTF name table.
    /// Looks for platformID=3 (Windows), encodingID=1 (Unicode BMP), decoded as UTF-16BE.
    /// </summary>
    private static string? ParseFamilyName(byte[] fontData, int faceIndex = 0)
    {
        if (fontData.Length < 12)
            return null;

        // Check for TTC (TrueType Collection) format — starts with "ttcf".
        int fontOffset = 0;
        if (fontData.Length >= 16 &&
            fontData[0] == (byte)'t' && fontData[1] == (byte)'t' &&
            fontData[2] == (byte)'c' && fontData[3] == (byte)'f')
        {
            uint numFonts = ReadUInt32BE(fontData, 8);
            if (faceIndex < 0 || (uint)faceIndex >= numFonts)
                return null;
            int offsetArrayPos = 12 + faceIndex * 4;
            if (offsetArrayPos + 4 > fontData.Length)
                return null;
            fontOffset = (int)ReadUInt32BE(fontData, offsetArrayPos);
            if (fontOffset < 0 || fontOffset + 12 > fontData.Length)
                return null;
        }

        // Read the offset table to find the 'name' table.
        int numTables = ReadUInt16BE(fontData, fontOffset + 4);
        int nameTableOffset = -1;

        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = fontOffset + 12 + i * 16;
            if (entryOffset + 16 > fontData.Length)
                break;

            var tag = Encoding.ASCII.GetString(fontData, entryOffset, 4);
            if (tag == "name")
            {
                uint rawOffset = ReadUInt32BE(fontData, entryOffset + 8);
                if (rawOffset > fontData.Length)
                    return null;
                nameTableOffset = (int)rawOffset;
                break;
            }
        }

        if (nameTableOffset < 0 || nameTableOffset + 6 > fontData.Length)
            return null;

        int nameCount = ReadUInt16BE(fontData, nameTableOffset + 2);
        long storageOffsetLong = (long)nameTableOffset + ReadUInt16BE(fontData, nameTableOffset + 4);
        if (storageOffsetLong > fontData.Length)
            return null;
        int storageOffset = (int)storageOffsetLong;

        for (int i = 0; i < nameCount; i++)
        {
            int recordOffset = nameTableOffset + 6 + i * 12;
            if (recordOffset + 12 > fontData.Length)
                break;

            int platformId = ReadUInt16BE(fontData, recordOffset);
            int encodingId = ReadUInt16BE(fontData, recordOffset + 2);
            int nameId = ReadUInt16BE(fontData, recordOffset + 6);
            int length = ReadUInt16BE(fontData, recordOffset + 8);
            int stringOffset = ReadUInt16BE(fontData, recordOffset + 10);

            // nameID=1 is "Font Family", platformID=3 is Windows, encodingID=1 is Unicode BMP.
            if (nameId == 1 && platformId == 3 && encodingId == 1)
            {
                long dataOffsetLong = (long)storageOffset + stringOffset;
                if (dataOffsetLong < 0 || dataOffsetLong + length > fontData.Length)
                    continue;
                int dataOffset = (int)dataOffsetLong;

                return Encoding.BigEndianUnicode.GetString(fontData, dataOffset, length);
            }
        }

        return null;
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    // ── Capabilities ────────────────────────────────────────────────

    private sealed class GdiCapabilities : IRasterizerCapabilities
    {
        public bool SupportsColorFonts => false;
        public bool SupportsVariableFonts => false;
        public bool SupportsSdf => false;
        public bool SupportsOutlineStroke => false;
        public bool HandlesOwnSizing => true;
        public bool SupportsSystemFonts => true;

        public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
            [AntiAliasMode.None, AntiAliasMode.Grayscale];
    }
}
