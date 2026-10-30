namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>One palette color, straight (not premultiplied) sRGB with alpha.</summary>
internal readonly record struct PaletteColor(byte R, byte G, byte B, byte A);

/// <summary>
/// The parsed <c>CPAL</c> (color palette) table: one or more palettes of the same number of
/// colors, referenced by <c>COLR</c> layers by entry index.
/// </summary>
/// <remarks>
/// Versions 0 and 1 are read. Version 1's palette type flags are kept (they choose the default
/// foreground color); its palette and entry label name IDs only name things for a UI and are
/// not read. Every palette's run of color records is bounds-checked at parse time, so lookups
/// never index outside the table.
/// </remarks>
internal sealed class CpalTable
{
    /// <summary>Palette type flag: suitable on a light background such as white.</summary>
    public const uint UsableWithLightBackground = 0x0001;

    /// <summary>Palette type flag: suitable on a dark background such as black.</summary>
    public const uint UsableWithDarkBackground = 0x0002;

    private const int ColorRecordSize = 4;

    private readonly PaletteColor[] _colors;
    private readonly ushort[] _firstColorIndex;
    private readonly uint[]? _paletteTypes;

    private CpalTable(int version, int entryCount, PaletteColor[] colors, ushort[] firstColorIndex, uint[]? paletteTypes)
    {
        Version = version;
        EntryCount = entryCount;
        _colors = colors;
        _firstColorIndex = firstColorIndex;
        _paletteTypes = paletteTypes;
    }

    /// <summary>The table version, 0 or 1.</summary>
    public int Version { get; }

    /// <summary>The number of palettes; at least one.</summary>
    public int PaletteCount => _firstColorIndex.Length;

    /// <summary>The number of colors in every palette.</summary>
    public int EntryCount { get; }

    /// <summary>Returns entry <paramref name="entry"/> of palette <paramref name="palette"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If either index is outside the table.</exception>
    public PaletteColor GetColor(int palette, int entry)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)palette, (uint)PaletteCount, nameof(palette));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)entry, (uint)EntryCount, nameof(entry));
        return _colors[_firstColorIndex[palette] + entry];
    }

    /// <summary>
    /// The palette's type flags (<see cref="UsableWithLightBackground"/>,
    /// <see cref="UsableWithDarkBackground"/>); 0 when the table carries none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the palette index is outside the table.</exception>
    public uint GetPaletteType(int palette)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)palette, (uint)PaletteCount, nameof(palette));
        return _paletteTypes?[palette] ?? 0;
    }

    /// <summary>Parses the <c>CPAL</c> table.</summary>
    /// <param name="data">The raw <c>CPAL</c> bytes.</param>
    /// <exception cref="FontFormatException">
    /// If the version is unknown, there are no palettes or entries, or any palette's colors or
    /// the palette type array fall outside the table.
    /// </exception>
    public static CpalTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        int version = reader.ReadUInt16();
        if (version > 1)
            throw new FontFormatException("CPAL", 0, $"unsupported version {version}.");

        int entryCount = reader.ReadUInt16();
        int paletteCount = reader.ReadUInt16();
        int colorRecordCount = reader.ReadUInt16();
        uint colorRecordsOffset = reader.ReadUInt32();

        if (paletteCount == 0 || entryCount == 0)
            throw new FontFormatException("CPAL", 2, "the table must hold at least one palette with at least one entry.");

        var firstColorIndex = new ushort[paletteCount];
        for (int p = 0; p < paletteCount; p++)
        {
            firstColorIndex[p] = reader.ReadUInt16();
            if (firstColorIndex[p] + entryCount > colorRecordCount)
                throw new FontFormatException("CPAL", reader.Position - 2,
                    $"palette {p} starts at color record {firstColorIndex[p]} and needs {entryCount}, but there are only {colorRecordCount}.");
        }

        uint paletteTypesOffset = version >= 1 ? reader.ReadUInt32() : 0;

        int recordsStart = CheckedRange(data.Length, colorRecordsOffset, (long)colorRecordCount * ColorRecordSize, "color records");
        var colors = new PaletteColor[colorRecordCount];
        for (int i = 0; i < colorRecordCount; i++)
        {
            int at = recordsStart + (i * ColorRecordSize);
            // Stored blue, green, red, alpha.
            colors[i] = new PaletteColor(R: data[at + 2], G: data[at + 1], B: data[at], A: data[at + 3]);
        }

        uint[]? paletteTypes = null;
        if (paletteTypesOffset != 0)
        {
            var types = new FontReader(data);
            types.Seek(CheckedRange(data.Length, paletteTypesOffset, (long)paletteCount * 4, "palette types"));
            paletteTypes = new uint[paletteCount];
            for (int p = 0; p < paletteCount; p++)
                paletteTypes[p] = types.ReadUInt32();
        }

        return new CpalTable(version, entryCount, colors, firstColorIndex, paletteTypes);
    }

    /// <summary>Checks that <paramref name="length"/> bytes at <paramref name="offset"/> lie inside the table.</summary>
    private static int CheckedRange(int tableLength, uint offset, long length, string what)
    {
        if (offset + length > tableLength)
            throw new FontFormatException("CPAL", (int)Math.Min(offset, int.MaxValue),
                $"the {what} ({length} bytes at offset {offset}) run past the end of the {tableLength}-byte table.");
        return (int)offset;
    }
}
