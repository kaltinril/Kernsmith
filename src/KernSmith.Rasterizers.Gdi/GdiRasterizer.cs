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
                var result = RasterizeGlyphCore(hdc, codepoint, options);
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

        var results = new List<RasterizedGlyph>();

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
                foreach (var codepoint in codepoints)
                {
                    var glyph = RasterizeGlyphCore(hdc, codepoint, options);
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
    /// Returns kerning pairs from GDI, already scaled to the requested pixel size.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
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
                var count = GetKerningPairsW(hdc, 0, null);
                if (count == 0)
                    return null;

                var pairs = new KERNINGPAIR[count];
                GetKerningPairsW(hdc, count, pairs);

                var result = new ScaledKerningPair[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = new ScaledKerningPair(
                        pairs[i].WFirst,
                        pairs[i].WSecond,
                        pairs[i].IKernAmount);
                }

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
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");
    }

    private IntPtr CreateHFont(RasterOptions options)
    {
        var logFont = new LOGFONTW
        {
            LfHeight = (int)(-(long)options.Size * options.Dpi / 72),
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

    private static RasterizedGlyph? RasterizeGlyphCore(IntPtr hdc, int codepoint, RasterOptions options)
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
                    BearingX: gm.GmptGlyphOrigin.X,
                    BearingY: gm.GmptGlyphOrigin.Y,
                    Advance: gm.GmCellIncX,
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

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = glyphIndex,
                BitmapData = bitmapData,
                Width = width,
                Height = height,
                Pitch = width,
                Metrics = new GlyphMetrics(
                    BearingX: gm.GmptGlyphOrigin.X,
                    BearingY: gm.GmptGlyphOrigin.Y,
                    Advance: gm.GmCellIncX,
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

        public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
            [AntiAliasMode.None, AntiAliasMode.Grayscale];
    }
}
