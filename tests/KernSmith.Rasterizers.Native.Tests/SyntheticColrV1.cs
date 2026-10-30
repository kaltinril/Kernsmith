using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Builds <c>COLR</c> version 1 tables byte by byte from a small paint-graph description, so each
/// Paint format, composite mode and malformed case can be tested at known pixels.
/// </summary>
/// <remarks>
/// Paints are serialized into a heap that follows the lists, parent before children (Offset24
/// fields are unsigned, so children must come later). Writing the same <see cref="Paint"/>
/// instance twice reuses its bytes, which is how tests build shared (DAG) sub-graphs.
/// </remarks>
internal static class SyntheticColrV1
{
    /// <summary>The palette entry that means "the text foreground color".</summary>
    public const int Foreground = 0xFFFF;

    public enum Extend { Pad = 0, Repeat = 1, Reflect = 2 }

    public abstract record Paint;

    /// <summary>PaintColrLayers over new LayerList entries, bottom first.</summary>
    public sealed record Layers(params Paint[] Children) : Paint;

    /// <summary>PaintColrLayers with a raw (possibly invalid) LayerList slice.</summary>
    public sealed record RawLayers(int FirstLayer, int Count) : Paint;

    /// <summary>PaintSolid (2), or PaintVarSolid (3) when a variation index is given.</summary>
    public sealed record Solid(int PaletteIndex, double Alpha = 1, uint? Var = null) : Paint;

    public sealed record Stop(double Offset, int PaletteIndex, double Alpha = 1, uint VarBase = 0);

    public sealed record Line(Extend Extend, params Stop[] Stops);

    /// <summary>PaintLinearGradient (4) / PaintVarLinearGradient (5).</summary>
    public sealed record Linear(Line Line, int X0, int Y0, int X1, int Y1, int X2, int Y2, uint? Var = null) : Paint;

    /// <summary>PaintRadialGradient (6) / PaintVarRadialGradient (7).</summary>
    public sealed record Radial(Line Line, int X0, int Y0, int R0, int X1, int Y1, int R1, uint? Var = null) : Paint;

    /// <summary>PaintSweepGradient (8) / (9). Angles are the stored F2DOT14 values (degrees = (v + 1) * 180).</summary>
    public sealed record Sweep(Line Line, int Cx, int Cy, double StartAngle, double EndAngle, uint? Var = null) : Paint;

    /// <summary>PaintGlyph (10).</summary>
    public sealed record Glyph(int GlyphId, Paint Child) : Paint;

    /// <summary>PaintColrGlyph (11).</summary>
    public sealed record ColrGlyph(int GlyphId) : Paint;

    /// <summary>PaintTransform (12) / PaintVarTransform (13).</summary>
    public sealed record Transform(double Xx, double Yx, double Xy, double Yy, double Dx, double Dy, Paint Child, uint? Var = null) : Paint;

    /// <summary>PaintTranslate (14) / (15).</summary>
    public sealed record Translate(int Dx, int Dy, Paint Child, uint? Var = null) : Paint;

    /// <summary>PaintScale (16/17), or around a center (18/19).</summary>
    public sealed record Scale(double Sx, double Sy, Paint Child, (int X, int Y)? Center = null, uint? Var = null) : Paint;

    /// <summary>PaintScaleUniform (20/21), or around a center (22/23).</summary>
    public sealed record ScaleUniform(double S, Paint Child, (int X, int Y)? Center = null, uint? Var = null) : Paint;

    /// <summary>PaintRotate (24/25), or around a center (26/27). Angle in half-turns (1.0 = 180°).</summary>
    public sealed record Rotate(double Angle, Paint Child, (int X, int Y)? Center = null, uint? Var = null) : Paint;

    /// <summary>PaintSkew (28/29), or around a center (30/31). Angles in half-turns.</summary>
    public sealed record Skew(double XAngle, double YAngle, Paint Child, (int X, int Y)? Center = null, uint? Var = null) : Paint;

    /// <summary>PaintComposite (32).</summary>
    public sealed record Composite(int Mode, Paint Source, Paint Backdrop) : Paint;

    /// <summary>Arbitrary bytes standing in for a paint table (an unknown format, say).</summary>
    public sealed record Raw(byte[] Bytes) : Paint;

    /// <summary>A clip record: format 1, or format 2 when a variation index is given.</summary>
    public sealed record Clip(int StartGlyph, int EndGlyph, int XMin, int YMin, int XMax, int YMax, uint? Var = null);

    /// <summary>
    /// Variation data: an ItemVariationStore with one subtable whose rows are the given deltas
    /// (one column per region), plus an optional DeltaSetIndexMap.
    /// </summary>
    /// <param name="AxisCount">The font's fvar axis count.</param>
    /// <param name="Regions">Per region, per axis: (start, peak, end).</param>
    /// <param name="Rows">Delta rows (item index = row), one int16 per region.</param>
    /// <param name="IndexMap">When non-null, a DeltaSetIndexMap whose entry i is (0, IndexMap[i]).</param>
    public sealed record Variations(int AxisCount, (double Start, double Peak, double End)[][] Regions, short[][] Rows, int[]? IndexMap = null);

    /// <summary>Builds a version 1 <c>COLR</c> table.</summary>
    public static byte[] Colr(
        (int Glyph, Paint Root)[] baseGlyphs,
        Clip[]? clips = null,
        Variations? variations = null,
        (int BaseGlyph, (int Glyph, int Palette)[] Layers)[]? v0 = null)
    {
        var writer = new HeapWriter();
        var roots = baseGlyphs.OrderBy(b => b.Glyph).Select(b => (b.Glyph, Offset: writer.Write(b.Root))).ToArray();

        clips ??= [];
        v0 ??= [];
        var v0Sorted = v0.OrderBy(b => b.BaseGlyph).ToArray();
        int v0LayerCount = v0Sorted.Sum(b => b.Layers.Length);

        const int HeaderSize = 34;
        int v0BaseOffset = HeaderSize;
        int v0LayerOffset = v0BaseOffset + (v0Sorted.Length * 6);
        int baseListOffset = v0LayerOffset + (v0LayerCount * 4);
        int layerListOffset = baseListOffset + 4 + (roots.Length * 6);
        int clipListOffset = layerListOffset + 4 + (writer.LayerList.Count * 4);
        int clipBoxesOffset = clipListOffset + 5 + (clips.Length * 7);
        int heapOffset = clipBoxesOffset + (clips.Length * 13);
        int varMapOffset = heapOffset + writer.Heap.Count;
        byte[] varMap = variations?.IndexMap is { } map ? IndexMap(map) : [];
        int varStoreOffset = varMapOffset + varMap.Length;
        byte[] varStore = variations is null ? [] : VarStore(variations);

        var table = new byte[varStoreOffset + varStore.Length];
        var span = table.AsSpan();
        U16(span, 0, 1);
        U16(span, 2, v0Sorted.Length);
        U32(span, 4, v0Sorted.Length == 0 ? 0u : (uint)v0BaseOffset);
        U32(span, 8, v0LayerCount == 0 ? 0u : (uint)v0LayerOffset);
        U16(span, 12, v0LayerCount);
        U32(span, 14, (uint)baseListOffset);
        U32(span, 18, (uint)layerListOffset);
        U32(span, 22, clips.Length == 0 ? 0u : (uint)clipListOffset);
        U32(span, 26, varMap.Length == 0 ? 0u : (uint)varMapOffset);
        U32(span, 30, varStore.Length == 0 ? 0u : (uint)varStoreOffset);

        int layer = 0;
        for (int i = 0; i < v0Sorted.Length; i++)
        {
            U16(span, v0BaseOffset + (i * 6), v0Sorted[i].BaseGlyph);
            U16(span, v0BaseOffset + (i * 6) + 2, layer);
            U16(span, v0BaseOffset + (i * 6) + 4, v0Sorted[i].Layers.Length);
            foreach (var (glyph, palette) in v0Sorted[i].Layers)
            {
                U16(span, v0LayerOffset + (layer * 4), glyph);
                U16(span, v0LayerOffset + (layer * 4) + 2, palette);
                layer++;
            }
        }

        U32(span, baseListOffset, (uint)roots.Length);
        for (int i = 0; i < roots.Length; i++)
        {
            U16(span, baseListOffset + 4 + (i * 6), roots[i].Glyph);
            U32(span, baseListOffset + 4 + (i * 6) + 2, (uint)(heapOffset + roots[i].Offset - baseListOffset));
        }

        U32(span, layerListOffset, (uint)writer.LayerList.Count);
        for (int i = 0; i < writer.LayerList.Count; i++)
            U32(span, layerListOffset + 4 + (i * 4), (uint)(heapOffset + writer.LayerList[i] - layerListOffset));

        if (clips.Length > 0)
        {
            span[clipListOffset] = 1;
            U32(span, clipListOffset + 1, (uint)clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                int record = clipListOffset + 5 + (i * 7);
                int box = clipBoxesOffset + (i * 13);
                U16(span, record, clip.StartGlyph);
                U16(span, record + 2, clip.EndGlyph);
                U24(span, record + 4, box - clipListOffset);
                span[box] = (byte)(clip.Var is null ? 1 : 2);
                I16(span, box + 1, clip.XMin);
                I16(span, box + 3, clip.YMin);
                I16(span, box + 5, clip.XMax);
                I16(span, box + 7, clip.YMax);
                if (clip.Var is { } v)
                    U32(span, box + 9, v);
            }
        }

        writer.Heap.ToArray().CopyTo(span[heapOffset..]);
        varMap.CopyTo(span[varMapOffset..]);
        varStore.CopyTo(span[varStoreOffset..]);
        return table;
    }

    /// <summary>A one-glyph table: <paramref name="glyph"/> painted by <paramref name="root"/>.</summary>
    public static byte[] Colr(int glyph, Paint root, Clip? clip = null, Variations? variations = null) =>
        Colr([(glyph, root)], clip is null ? null : [clip], variations);

    /// <summary>Encodes a value as F2DOT14.</summary>
    public static short F2Dot14(double value) => checked((short)Math.Round(value * 16384));

    private static byte[] IndexMap(int[] inner)
    {
        // Format 0, entryFormat 0x3F: 4-byte entries with a 16-bit inner index.
        var bytes = new byte[4 + (inner.Length * 4)];
        bytes[0] = 0;
        bytes[1] = 0x3F;
        U16(bytes, 2, inner.Length);
        for (int i = 0; i < inner.Length; i++)
            U32(bytes, 4 + (i * 4), (uint)inner[i]);
        return bytes;
    }

    private static byte[] VarStore(Variations v)
    {
        int regionCount = v.Regions.Length;
        int regionListOffset = 12;
        int regionListSize = 4 + (regionCount * v.AxisCount * 6);
        int dataOffset = regionListOffset + regionListSize;
        int dataSize = 6 + (regionCount * 2) + (v.Rows.Length * regionCount * 2);
        var bytes = new byte[dataOffset + dataSize];

        U16(bytes, 0, 1);
        U32(bytes, 2, (uint)regionListOffset);
        U16(bytes, 6, 1);
        U32(bytes, 8, (uint)dataOffset);

        U16(bytes, regionListOffset, v.AxisCount);
        U16(bytes, regionListOffset + 2, regionCount);
        int at = regionListOffset + 4;
        foreach (var region in v.Regions)
        {
            for (int axis = 0; axis < v.AxisCount; axis++)
            {
                var (start, peak, end) = axis < region.Length ? region[axis] : (0, 0, 0);
                I16(bytes, at, F2Dot14(start));
                I16(bytes, at + 2, F2Dot14(peak));
                I16(bytes, at + 4, F2Dot14(end));
                at += 6;
            }
        }

        U16(bytes, dataOffset, v.Rows.Length);
        U16(bytes, dataOffset + 2, regionCount); // every column is a word (int16) delta
        U16(bytes, dataOffset + 4, regionCount);
        at = dataOffset + 6;
        for (int r = 0; r < regionCount; r++, at += 2)
            U16(bytes, at, r);
        foreach (var row in v.Rows)
        {
            for (int r = 0; r < regionCount; r++, at += 2)
                I16(bytes, at, r < row.Length ? row[r] : 0);
        }

        return bytes;
    }

    private sealed class HeapWriter
    {
        private readonly Dictionary<Paint, int> _written = new(ReferenceEqualityComparer.Instance);

        public List<byte> Heap { get; } = [];

        public List<int> LayerList { get; } = [];

        public int Write(Paint paint)
        {
            if (_written.TryGetValue(paint, out int existing))
                return existing;

            int at = Heap.Count;
            _written[paint] = at;
            switch (paint)
            {
                case Layers layers:
                    {
                        Reserve(6);
                        Heap[at] = 1;
                        var children = layers.Children.Select(Write).ToArray();
                        int first = LayerList.Count;
                        LayerList.AddRange(children);
                        Heap[at + 1] = (byte)children.Length;
                        Put32(at + 2, (uint)first);
                        break;
                    }

                case RawLayers raw:
                    Reserve(6);
                    Heap[at] = 1;
                    Heap[at + 1] = (byte)raw.Count;
                    Put32(at + 2, (uint)raw.FirstLayer);
                    break;

                case Solid solid:
                    Reserve(solid.Var is null ? 5 : 9);
                    Heap[at] = (byte)(solid.Var is null ? 2 : 3);
                    Put16(at + 1, solid.PaletteIndex);
                    Put16(at + 3, F2Dot14(solid.Alpha));
                    if (solid.Var is { } sv)
                        Put32(at + 5, sv);
                    break;

                case Linear linear:
                    Reserve(linear.Var is null ? 16 : 20);
                    Heap[at] = (byte)(linear.Var is null ? 4 : 5);
                    PutFields(at + 4, linear.X0, linear.Y0, linear.X1, linear.Y1, linear.X2, linear.Y2);
                    if (linear.Var is { } lv)
                        Put32(at + 16, lv);
                    Put24(at + 1, WriteLine(linear.Line, linear.Var is not null) - at);
                    break;

                case Radial radial:
                    Reserve(radial.Var is null ? 16 : 20);
                    Heap[at] = (byte)(radial.Var is null ? 6 : 7);
                    PutFields(at + 4, radial.X0, radial.Y0, radial.R0, radial.X1, radial.Y1, radial.R1);
                    if (radial.Var is { } rv)
                        Put32(at + 16, rv);
                    Put24(at + 1, WriteLine(radial.Line, radial.Var is not null) - at);
                    break;

                case Sweep sweep:
                    Reserve(sweep.Var is null ? 12 : 16);
                    Heap[at] = (byte)(sweep.Var is null ? 8 : 9);
                    PutFields(at + 4, sweep.Cx, sweep.Cy, F2Dot14(sweep.StartAngle), F2Dot14(sweep.EndAngle));
                    if (sweep.Var is { } swv)
                        Put32(at + 12, swv);
                    Put24(at + 1, WriteLine(sweep.Line, sweep.Var is not null) - at);
                    break;

                case Glyph glyph:
                    Reserve(6);
                    Heap[at] = 10;
                    Put16(at + 4, glyph.GlyphId);
                    Put24(at + 1, Write(glyph.Child) - at);
                    break;

                case ColrGlyph colrGlyph:
                    Reserve(3);
                    Heap[at] = 11;
                    Put16(at + 1, colrGlyph.GlyphId);
                    break;

                case Transform t:
                    {
                        Reserve(7);
                        Heap[at] = (byte)(t.Var is null ? 12 : 13);
                        int affine = Heap.Count;
                        Reserve(t.Var is null ? 24 : 28);
                        double[] values = [t.Xx, t.Yx, t.Xy, t.Yy, t.Dx, t.Dy];
                        for (int i = 0; i < 6; i++)
                            Put32(affine + (i * 4), unchecked((uint)checked((int)Math.Round(values[i] * 65536))));
                        if (t.Var is { } tv)
                            Put32(affine + 24, tv);
                        Put24(at + 4, affine - at);
                        Put24(at + 1, Write(t.Child) - at);
                        break;
                    }

                case Translate t:
                    WriteTransform(at, 14, t.Child, t.Var, [t.Dx, t.Dy]);
                    break;

                case Scale s:
                    WriteTransform(at, s.Center is null ? 16 : 18, s.Child, s.Var,
                        s.Center is { } sc ? [F2Dot14(s.Sx), F2Dot14(s.Sy), sc.X, sc.Y] : [F2Dot14(s.Sx), F2Dot14(s.Sy)]);
                    break;

                case ScaleUniform s:
                    WriteTransform(at, s.Center is null ? 20 : 22, s.Child, s.Var,
                        s.Center is { } uc ? [F2Dot14(s.S), uc.X, uc.Y] : [F2Dot14(s.S)]);
                    break;

                case Rotate r:
                    WriteTransform(at, r.Center is null ? 24 : 26, r.Child, r.Var,
                        r.Center is { } rc ? [F2Dot14(r.Angle), rc.X, rc.Y] : [F2Dot14(r.Angle)]);
                    break;

                case Skew k:
                    WriteTransform(at, k.Center is null ? 28 : 30, k.Child, k.Var,
                        k.Center is { } kc ? [F2Dot14(k.XAngle), F2Dot14(k.YAngle), kc.X, kc.Y] : [F2Dot14(k.XAngle), F2Dot14(k.YAngle)]);
                    break;

                case Composite c:
                    Reserve(8);
                    Heap[at] = 32;
                    Heap[at + 4] = (byte)c.Mode;
                    Put24(at + 1, Write(c.Source) - at);
                    Put24(at + 5, Write(c.Backdrop) - at);
                    break;

                case Raw raw:
                    Heap.AddRange(raw.Bytes);
                    break;

                default:
                    throw new ArgumentException($"Unknown paint {paint}.", nameof(paint));
            }

            return at;
        }

        /// <summary>Formats 14–31: format, Offset24 child, int16 fields, then uint32 varIndexBase for the Var form (format + 1).</summary>
        private void WriteTransform(int at, int format, Paint child, uint? var, int[] fields)
        {
            Reserve(4 + (fields.Length * 2) + (var is null ? 0 : 4));
            Heap[at] = (byte)(var is null ? format : format + 1);
            PutFields(at + 4, fields);
            if (var is { } v)
                Put32(at + 4 + (fields.Length * 2), v);
            Put24(at + 1, Write(child) - at);
        }

        private int WriteLine(Line line, bool variable)
        {
            int at = Heap.Count;
            int stopSize = variable ? 10 : 6;
            Reserve(3 + (line.Stops.Length * stopSize));
            Heap[at] = (byte)line.Extend;
            Put16(at + 1, line.Stops.Length);
            for (int i = 0; i < line.Stops.Length; i++)
            {
                var stop = line.Stops[i];
                int s = at + 3 + (i * stopSize);
                Put16(s, F2Dot14(stop.Offset));
                Put16(s + 2, stop.PaletteIndex);
                Put16(s + 4, F2Dot14(stop.Alpha));
                if (variable)
                    Put32(s + 6, stop.VarBase);
            }

            return at;
        }

        private void Reserve(int count)
        {
            for (int i = 0; i < count; i++)
                Heap.Add(0);
        }

        private void PutFields(int at, params int[] values)
        {
            for (int i = 0; i < values.Length; i++)
                Put16(at + (i * 2), values[i]);
        }

        private void Put16(int at, int value)
        {
            Heap[at] = (byte)(value >> 8);
            Heap[at + 1] = (byte)value;
        }

        private void Put24(int at, int value)
        {
            if (value is < 0 or > 0xFFFFFF)
                throw new InvalidOperationException($"Offset24 {value} out of range.");
            Heap[at] = (byte)(value >> 16);
            Heap[at + 1] = (byte)(value >> 8);
            Heap[at + 2] = (byte)value;
        }

        private void Put32(int at, uint value)
        {
            Heap[at] = (byte)(value >> 24);
            Heap[at + 1] = (byte)(value >> 16);
            Heap[at + 2] = (byte)(value >> 8);
            Heap[at + 3] = (byte)value;
        }
    }

    private static void U16(Span<byte> span, int at, int value) => BinaryPrimitives.WriteUInt16BigEndian(span[at..], (ushort)value);

    private static void I16(Span<byte> span, int at, int value) => BinaryPrimitives.WriteInt16BigEndian(span[at..], checked((short)value));

    private static void U24(Span<byte> span, int at, int value)
    {
        span[at] = (byte)(value >> 16);
        span[at + 1] = (byte)(value >> 8);
        span[at + 2] = (byte)value;
    }

    private static void U32(Span<byte> span, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(span[at..], value);
}
