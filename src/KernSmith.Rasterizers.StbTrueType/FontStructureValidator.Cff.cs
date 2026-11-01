namespace KernSmith.Rasterizers.StbTrueType;

internal sealed partial class FontStructureValidator
{
    /// <summary>Operators plus operands one glyph may execute, subroutines included (Native's <c>Type2Charstring.MaxOperations</c>).</summary>
    internal const int MaxCharstringOperations = 1 << 20;

    /// <summary>Largest charstring coordinate magnitude (Native's <c>Type2Charstring.MaxCoordinate</c>).</summary>
    internal const float MaxCharstringCoordinate = 65536;

    /// <summary>Type 2 subroutine nesting limit, which stb_truetype also enforces.</summary>
    private const int MaxSubroutineDepth = 10;

    /// <summary>Type 2 argument stack depth for CFF (stb_truetype's stack size).</summary>
    private const int MaxCharstringStack = 48;

    /// <summary>
    /// A window into the CFF data, mirroring stb_truetype's <c>stbtt__buf</c>: reads past the end
    /// return 0 and seeks outside it clamp to the end. Every window here lies inside the
    /// <c>CFF </c> table (stb's own top-level window is 512 MB, which is why the structure is
    /// checked against the real table length first).
    /// </summary>
    private struct CffBuf
    {
        public int Start;
        public int Size;
        public int Cursor;
    }

    private int _cffOffset;
    private int _cffLength;
    private CffBuf _charStrings;
    private CffBuf _globalSubrs;
    private CffBuf _localSubrs;
    private CffBuf[]? _fontDictSubrs;
    private int _fdSelect = -1;

    /// <summary>
    /// Checks the CFF header and the Name, Top DICT, String and Global Subr INDEXes, then every
    /// offset stb_truetype takes from the Top DICT — CharStrings, Private DICT and its Subrs,
    /// and for a CID-keyed font FDArray (each Font DICT's Private DICT and Subrs) and FDSelect —
    /// against the real table length.
    /// </summary>
    private void ValidateCff(TableRange cff)
    {
        _cffOffset = cff.Offset;
        _cffLength = cff.Length;
        if (_cffLength < 4)
            throw Fail("CFF ", _cffOffset, $"table is {_cffLength} bytes, shorter than the 4-byte header.");
        if (_data[_cffOffset] != 1)
            throw Fail("CFF ", _cffOffset, $"major version {_data[_cffOffset]} is not 1.");
        int headerSize = _data[_cffOffset + 2];
        if (headerSize < 4)
            throw Fail("CFF ", _cffOffset + 2, $"header size {headerSize} is smaller than the 4-byte header.");

        var name = ReadIndex(headerSize, "Name");
        var topDicts = ReadIndex(name.Start - _cffOffset + name.Size, "Top DICT");
        if (IndexCount(topDicts) == 0)
            throw Fail("CFF ", topDicts.Start, "Top DICT INDEX is empty.");
        var strings = ReadIndex(topDicts.Start - _cffOffset + topDicts.Size, "String");
        _globalSubrs = ReadIndex(strings.Start - _cffOffset + strings.Size, "Global Subr");

        var topDict = IndexGet(topDicts, 0);
        uint charStrings = DictInt(topDict, 17, 0);
        uint charstringType = DictInt(topDict, 0x100 | 6, 2);
        uint fdArray = DictInt(topDict, 0x100 | 36, 0);
        uint fdSelect = DictInt(topDict, 0x100 | 37, 0);
        _localSubrs = ReadSubrs(topDict, "Top DICT");

        if (charstringType != 2)
            throw Fail("CFF ", topDict.Start, $"CharstringType {charstringType} is not 2.");
        if (charStrings == 0)
            throw Fail("CFF ", topDict.Start, "Top DICT has no CharStrings offset.");

        _charStrings = ReadIndex((int)charStrings, "CharStrings");
        int glyphCount = IndexCount(_charStrings);
        if (glyphCount != NumGlyphs)
            throw Fail("CFF ", _charStrings.Start, $"CharStrings count {glyphCount} differs from maxp numGlyphs {NumGlyphs}.");

        if (fdArray != 0)
        {
            if (fdSelect == 0)
                throw Fail("CFF ", topDict.Start, "Top DICT has an FDArray but no FDSelect.");
            var fontDicts = ReadIndex((int)fdArray, "FDArray");
            int fdCount = IndexCount(fontDicts);
            _fontDictSubrs = new CffBuf[fdCount];
            for (int fd = 0; fd < fdCount; fd++)
                _fontDictSubrs[fd] = ReadSubrs(IndexGet(fontDicts, fd), $"Font DICT {fd}");
            ValidateFdSelect((int)fdSelect, fdCount);
        }
    }

    /// <summary>
    /// Locates and checks an INDEX at <paramref name="pos"/> (relative to the table): count,
    /// offSize 1-4, an offset array starting at 1 and ascending, and data inside the table.
    /// Returns the window stb_truetype's <c>stbtt__cff_get_index</c> would produce.
    /// </summary>
    private CffBuf ReadIndex(int pos, string what)
    {
        int at = _cffOffset + Math.Max(pos, 0);
        if (pos < 0 || pos > _cffLength - 2)
            throw Fail("CFF ", at, $"{what} INDEX at offset {pos} lies outside the table ({_cffLength} bytes).");

        int count = U16(at);
        if (count == 0)
            return new CffBuf { Start = at, Size = 2 };
        if (pos + 3 > _cffLength)
            throw Fail("CFF ", at, $"{what} INDEX header runs past the table.");

        int offSize = _data[at + 2];
        if (offSize is < 1 or > 4)
            throw Fail("CFF ", at + 2, $"{what} INDEX offSize {offSize} is not 1-4.");

        long offsetsEnd = pos + 3L + ((count + 1L) * offSize);
        if (offsetsEnd > _cffLength)
            throw Fail("CFF ", at, $"{what} INDEX offset array runs past the table.");

        long previous = 0;
        for (int i = 0; i <= count; i++)
        {
            long offset = ReadOffset(at + 3 + (i * offSize), offSize);
            if (i == 0 && offset != 1)
                throw Fail("CFF ", at + 3, $"{what} INDEX first offset is {offset}, not 1.");
            if (offset < previous)
                throw Fail("CFF ", at + 3 + (i * offSize), $"{what} INDEX offsets are not ascending at entry {i}.");
            previous = offset;
        }

        long end = offsetsEnd + previous - 1;
        if (end > _cffLength)
            throw Fail("CFF ", at, $"{what} INDEX data runs past the table (ends at {end} of {_cffLength}).");
        return new CffBuf { Start = at, Size = (int)(end - pos) };
    }

    private long ReadOffset(int pos, int offSize)
    {
        long v = 0;
        for (int i = 0; i < offSize; i++)
            v = (v << 8) | _data[pos + i];
        return v;
    }

    /// <summary>
    /// A DICT's Private DICT (operator 18: size, offset) and the Subrs INDEX it points to
    /// (operator 19, relative to the Private DICT), as <c>stbtt__get_subrs</c> reads them.
    /// </summary>
    private CffBuf ReadSubrs(CffBuf dict, string owner)
    {
        Span<uint> privateDict = [0, 0];
        DictInts(dict, 18, privateDict);
        if (privateDict[0] == 0 || privateDict[1] == 0)
            return default;

        int size = (int)privateDict[0];
        int offset = (int)privateDict[1];
        if (offset < 0 || size < 0 || offset > _cffLength || size > _cffLength - offset)
            throw Fail("CFF ", dict.Start,
                $"{owner} Private DICT ({privateDict[0]} bytes at offset {privateDict[1]}) lies outside the table.");

        var pdict = new CffBuf { Start = _cffOffset + offset, Size = size };
        uint subrs = DictInt(pdict, 19, 0);
        if (subrs == 0)
            return default;
        int subrsAt = (int)(privateDict[1] + subrs);
        if (subrsAt < 0 || subrsAt > _cffLength)
            throw Fail("CFF ", pdict.Start, $"{owner} Subrs offset {subrs} lies outside the table.");
        return ReadIndex(subrsAt, $"{owner} Subrs");
    }

    /// <summary>
    /// FDSelect format 0 (one Font DICT index per glyph) or 3 (ranges from glyph 0, ascending,
    /// closed by a sentinel equal to numGlyphs); every Font DICT index must name an FDArray entry.
    /// </summary>
    private void ValidateFdSelect(int pos, int fdCount)
    {
        int at = _cffOffset + Math.Max(pos, 0);
        if (pos < 0 || pos >= _cffLength)
            throw Fail("CFF ", at, $"FDSelect at offset {pos} lies outside the table.");
        int format = _data[at];
        if (format == 0)
        {
            if (pos + 1L + NumGlyphs > _cffLength)
                throw Fail("CFF ", at, "FDSelect format 0 runs past the table.");
            for (int g = 0; g < NumGlyphs; g++)
                CheckFd(_data[at + 1 + g], at + 1 + g);
        }
        else if (format == 3)
        {
            if (pos + 5L > _cffLength)
                throw Fail("CFF ", at, "FDSelect format 3 header runs past the table.");
            int ranges = U16(at + 1);
            if (ranges == 0)
                throw Fail("CFF ", at + 1, "FDSelect format 3 has no ranges.");
            if (pos + 3L + (3L * ranges) + 2 > _cffLength)
                throw Fail("CFF ", at, "FDSelect format 3 ranges run past the table.");
            if (U16(at + 3) != 0)
                throw Fail("CFF ", at + 3, "FDSelect format 3 first range does not start at glyph 0.");
            int previous = -1;
            for (int r = 0; r < ranges; r++)
            {
                int range = at + 3 + (3 * r);
                int first = U16(range);
                if (first <= previous)
                    throw Fail("CFF ", range, $"FDSelect format 3 range {r} is out of order.");
                previous = first;
                CheckFd(_data[range + 2], range + 2);
            }

            int sentinel = U16(at + 3 + (3 * ranges));
            if (sentinel != NumGlyphs || sentinel <= previous)
                throw Fail("CFF ", at + 3 + (3 * ranges), $"FDSelect format 3 sentinel {sentinel} is not numGlyphs {NumGlyphs}.");
        }
        else
        {
            throw Fail("CFF ", at, $"FDSelect format {format} is not 0 or 3.");
        }

        _fdSelect = at;

        void CheckFd(int fd, int position)
        {
            if (fd >= fdCount)
                throw Fail("CFF ", position, $"FDSelect maps glyphs to Font DICT {fd}, but FDArray holds {fdCount}.");
        }
    }

    /// <summary>The Font DICT a glyph uses, read as <c>stbtt__cid_get_glyph_subrs</c> does (FDSelect already checked).</summary>
    private int FontDictOf(int glyph)
    {
        if (_data[_fdSelect] == 0)
            return _data[_fdSelect + 1 + glyph];

        int ranges = U16(_fdSelect + 1);
        int start = U16(_fdSelect + 3);
        for (int r = 0; r < ranges; r++)
        {
            int range = _fdSelect + 3 + (3 * r);
            int end = U16(range + 3);
            if (glyph >= start && glyph < end)
                return _data[range + 2];
            start = end;
        }

        return 0;
    }

    // ------------------------------------------------------------------ charstrings

    /// <summary>
    /// Walks a glyph's Type 2 charstring the way <c>stbtt__run_charstring</c> does — the same
    /// operand decoding, hint-mask skipping, stack clearing and subroutine selection — and
    /// rejects what would leave it reading outside a subroutine INDEX or working without bound:
    /// a call to a subroutine the INDEX lacks or that is empty, nesting past 10, a 49th stack
    /// entry, a reserved operator, a return outside a subroutine, running off the end without
    /// endchar, or the operation, path-command and coordinate limits. Where stb_truetype stops
    /// on its own and draws nothing (too few operands, an arithmetic operator it does not
    /// implement), the walk stops and accepts.
    /// </summary>
    private void CheckCharstring(int glyph)
    {
        var b = IndexGet(_charStrings, glyph);
        int charstringStart = b.Start;
        var subrs = _localSubrs;
        bool haveSubrs = false;
        Span<float> s = stackalloc float[MaxCharstringStack];
        Span<CffBuf> callers = stackalloc CffBuf[MaxSubroutineDepth];
        int sp = 0, depth = 0, maskBits = 0, operations = 0;
        bool inHeader = true;
        var pen = new Pen(glyph, charstringStart);

        while (b.Cursor < b.Size)
        {
            if (++operations > MaxCharstringOperations)
                throw Fail("CFF ", charstringStart, $"glyph {glyph} charstring exceeds {MaxCharstringOperations} operations.");

            int i = 0;
            bool clearStack = true;
            int b0 = Get8(ref b);
            switch (b0)
            {
                case 0x13: // hintmask
                case 0x14: // cntrmask
                    if (inHeader)
                        maskBits += sp / 2;
                    inHeader = false;
                    Skip(ref b, (maskBits + 7) / 8);
                    break;

                case 0x01: // hstem
                case 0x03: // vstem
                case 0x12: // hstemhm
                case 0x17: // vstemhm
                    maskBits += sp / 2;
                    break;

                case 0x15: // rmoveto
                    inHeader = false;
                    if (sp < 2)
                        return;
                    pen.MoveTo(s[sp - 2], s[sp - 1]);
                    break;

                case 0x04: // vmoveto
                    inHeader = false;
                    if (sp < 1)
                        return;
                    pen.MoveTo(0, s[sp - 1]);
                    break;

                case 0x16: // hmoveto
                    inHeader = false;
                    if (sp < 1)
                        return;
                    pen.MoveTo(s[sp - 1], 0);
                    break;

                case 0x05: // rlineto
                    if (sp < 2)
                        return;
                    for (; i + 1 < sp; i += 2)
                        pen.LineTo(s[i], s[i + 1]);
                    break;

                case 0x07: // vlineto
                case 0x06: // hlineto
                    {
                        if (sp < 1)
                            return;
                        bool vertical = b0 == 0x07;
                        for (; i < sp; i++, vertical = !vertical)
                        {
                            if (vertical)
                                pen.LineTo(0, s[i]);
                            else
                                pen.LineTo(s[i], 0);
                        }

                        break;
                    }

                case 0x1F: // hvcurveto
                case 0x1E: // vhcurveto
                    {
                        if (sp < 4)
                            return;
                        bool horizontalFirst = b0 == 0x1F;
                        for (; i + 3 < sp; i += 4, horizontalFirst = !horizontalFirst)
                        {
                            float last = sp - i == 5 ? s[i + 4] : 0.0f;
                            if (horizontalFirst)
                                pen.CurveTo(s[i], 0, s[i + 1], s[i + 2], last, s[i + 3]);
                            else
                                pen.CurveTo(0, s[i], s[i + 1], s[i + 2], s[i + 3], last);
                        }

                        break;
                    }

                case 0x08: // rrcurveto
                    if (sp < 6)
                        return;
                    for (; i + 5 < sp; i += 6)
                        pen.CurveTo(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                    break;

                case 0x18: // rcurveline
                    if (sp < 8)
                        return;
                    for (; i + 5 < sp - 2; i += 6)
                        pen.CurveTo(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                    if (i + 1 >= sp)
                        return;
                    pen.LineTo(s[i], s[i + 1]);
                    break;

                case 0x19: // rlinecurve
                    if (sp < 8)
                        return;
                    for (; i + 1 < sp - 6; i += 2)
                        pen.LineTo(s[i], s[i + 1]);
                    if (i + 5 >= sp)
                        return;
                    pen.CurveTo(s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                    break;

                case 0x1A: // vvcurveto
                case 0x1B: // hhcurveto
                    {
                        if (sp < 4)
                            return;
                        float f = 0;
                        if ((sp & 1) != 0)
                            f = s[i++];
                        for (; i + 3 < sp; i += 4)
                        {
                            if (b0 == 0x1B)
                                pen.CurveTo(s[i], f, s[i + 1], s[i + 2], s[i + 3], 0);
                            else
                                pen.CurveTo(f, s[i], s[i + 1], s[i + 2], 0, s[i + 3]);
                            f = 0;
                        }

                        break;
                    }

                case 0x0A: // callsubr
                case 0x1D: // callgsubr
                    {
                        if (b0 == 0x0A && !haveSubrs)
                        {
                            if (_fontDictSubrs is not null)
                                subrs = _fontDictSubrs[FontDictOf(glyph)];
                            haveSubrs = true;
                        }

                        if (sp < 1)
                            return;
                        int v = (int)s[--sp];
                        if (depth >= MaxSubroutineDepth)
                            throw Fail("CFF ", charstringStart, $"glyph {glyph} subroutine nesting deeper than {MaxSubroutineDepth} levels.");
                        callers[depth++] = b;
                        var index = b0 == 0x0A ? subrs : _globalSubrs;
                        b = GetSubr(index, v, out bool found);
                        if (!found)
                            throw Fail("CFF ", charstringStart,
                                $"glyph {glyph} calls {(b0 == 0x0A ? "local" : "global")} subroutine {v} outside its INDEX.");
                        if (b.Size == 0)
                            throw Fail("CFF ", charstringStart, $"glyph {glyph} calls an empty subroutine ({v}).");
                        b.Cursor = 0;
                        clearStack = false;
                        break;
                    }

                case 0x0B: // return
                    if (depth <= 0)
                        throw Fail("CFF ", charstringStart, $"glyph {glyph} charstring has a return outside a subroutine.");
                    b = callers[--depth];
                    clearStack = false;
                    break;

                case 0x0E: // endchar
                    pen.Close();
                    return;

                case 0x0C: // escape
                    {
                        int b1 = Get8(ref b);
                        switch (b1)
                        {
                            case 0x22: // hflex
                                if (sp < 7)
                                    return;
                                pen.CurveTo(s[0], 0, s[1], s[2], s[3], 0);
                                pen.CurveTo(s[4], 0, s[5], -s[2], s[6], 0);
                                break;
                            case 0x23: // flex
                                if (sp < 13)
                                    return;
                                pen.CurveTo(s[0], s[1], s[2], s[3], s[4], s[5]);
                                pen.CurveTo(s[6], s[7], s[8], s[9], s[10], s[11]);
                                break;
                            case 0x24: // hflex1
                                if (sp < 9)
                                    return;
                                pen.CurveTo(s[0], s[1], s[2], s[3], s[4], 0);
                                pen.CurveTo(s[5], 0, s[6], s[7], s[8], -(s[1] + s[3] + s[7]));
                                break;
                            case 0x25: // flex1
                                {
                                    if (sp < 11)
                                        return;
                                    float dx = s[0] + s[2] + s[4] + s[6] + s[8];
                                    float dy = s[1] + s[3] + s[5] + s[7] + s[9];
                                    float dx6 = s[10], dy6 = s[10];
                                    if (Math.Abs(dx) > Math.Abs(dy))
                                        dy6 = -dy;
                                    else
                                        dx6 = -dx;
                                    pen.CurveTo(s[0], s[1], s[2], s[3], s[4], s[5]);
                                    pen.CurveTo(s[6], s[7], s[8], s[9], dx6, dy6);
                                    break;
                                }

                            default:
                                return; // an operator stb_truetype does not implement: it draws nothing
                        }

                        break;
                    }

                default:
                    {
                        if (b0 != 255 && b0 != 28 && b0 < 32)
                            throw Fail("CFF ", charstringStart, $"glyph {glyph} charstring uses reserved operator {b0}.");
                        float f;
                        if (b0 == 255)
                        {
                            f = (float)(int)Get(ref b, 4) / 0x10000;
                        }
                        else
                        {
                            Skip(ref b, -1);
                            f = (short)CffInt(ref b);
                        }

                        if (sp >= MaxCharstringStack)
                            throw Fail("CFF ", charstringStart, $"glyph {glyph} charstring stack holds more than {MaxCharstringStack} operands.");
                        s[sp++] = f;
                        clearStack = false;
                        break;
                    }
            }

            if (clearStack)
                sp = 0;
        }

        throw Fail("CFF ", charstringStart, $"glyph {glyph} charstring ends without endchar.");
    }

    /// <summary>
    /// The pen of stb_truetype's <c>stbtt__csctx</c>, tracked in the same single-precision
    /// arithmetic: counts the path commands the glyph emits and bounds its coordinates.
    /// </summary>
    private struct Pen(int glyph, int at)
    {
        private float _x, _y, _firstX, _firstY;
        private int _commands;

        public void MoveTo(float dx, float dy)
        {
            Close();
            _firstX = _x = _x + dx;
            _firstY = _y = _y + dy;
            Emit(_x, _y);
        }

        public void LineTo(float dx, float dy)
        {
            _x += dx;
            _y += dy;
            Emit(_x, _y);
        }

        public void CurveTo(float dx1, float dy1, float dx2, float dy2, float dx3, float dy3)
        {
            float cx1 = _x + dx1;
            float cy1 = _y + dy1;
            float cx2 = cx1 + dx2;
            float cy2 = cy1 + dy2;
            _x = cx2 + dx3;
            _y = cy2 + dy3;
            CheckCoordinate(cx1, cy1);
            CheckCoordinate(cx2, cy2);
            Emit(_x, _y);
        }

        public void Close()
        {
            if (!_firstX.Equals(_x) || !_firstY.Equals(_y))
                Emit(_firstX, _firstY);
        }

        private void Emit(float x, float y)
        {
            if (++_commands > MaxGlyphPoints)
                throw Fail("CFF ", at, $"glyph {glyph} charstring emits more than {MaxGlyphPoints} path commands.");
            CheckCoordinate(x, y);
        }

        private readonly void CheckCoordinate(float x, float y)
        {
            if (!(Math.Abs(x) <= MaxCharstringCoordinate && Math.Abs(y) <= MaxCharstringCoordinate))
                throw Fail("CFF ", at, $"glyph {glyph} charstring coordinate ({x}, {y}) is beyond ±{MaxCharstringCoordinate}.");
        }
    }

    // ------------------------------------------------------------------ stbtt__buf mirror

    private byte Get8(ref CffBuf b) => b.Cursor >= b.Size ? (byte)0 : _data[b.Start + b.Cursor++];

    private byte Peek8(ref CffBuf b) => b.Cursor >= b.Size ? (byte)0 : _data[b.Start + b.Cursor];

    private uint Get(ref CffBuf b, int n)
    {
        uint v = 0;
        for (int i = 0; i < n; i++)
            v = (v << 8) | Get8(ref b);
        return v;
    }

    private static void Seek(ref CffBuf b, int o) => b.Cursor = o > b.Size || o < 0 ? b.Size : o;

    private static void Skip(ref CffBuf b, int o) => Seek(ref b, b.Cursor + o);

    private static CffBuf Range(CffBuf b, int o, int s) =>
        o < 0 || s < 0 || o > b.Size || s > b.Size - o ? default : new CffBuf { Start = b.Start + o, Size = s };

    private int IndexCount(CffBuf b)
    {
        Seek(ref b, 0);
        return (int)Get(ref b, 2);
    }

    private CffBuf IndexGet(CffBuf b, int i)
    {
        Seek(ref b, 0);
        int count = (int)Get(ref b, 2);
        int offSize = Get8(ref b);
        Skip(ref b, i * offSize);
        int start = (int)Get(ref b, offSize);
        int end = (int)Get(ref b, offSize);
        return Range(b, 2 + ((count + 1) * offSize) + start, end - start);
    }

    private CffBuf GetSubr(CffBuf index, int n, out bool found)
    {
        int count = IndexCount(index);
        int bias = count >= 33900 ? 32768 : count >= 1240 ? 1131 : 107;
        n += bias;
        found = n >= 0 && n < count;
        return found ? IndexGet(index, n) : default;
    }

    private uint CffInt(ref CffBuf b)
    {
        int b0 = Get8(ref b);
        if (b0 is >= 32 and <= 246)
            return (uint)(b0 - 139);
        if (b0 is >= 247 and <= 250)
            return (uint)(((b0 - 247) * 256) + Get8(ref b) + 108);
        if (b0 is >= 251 and <= 254)
            return (uint)((-(b0 - 251) * 256) - Get8(ref b) - 108);
        if (b0 == 28)
            return Get(ref b, 2);
        if (b0 == 29)
            return Get(ref b, 4);
        return 0;
    }

    private void SkipOperand(ref CffBuf b)
    {
        int b0 = Peek8(ref b);
        if (b0 == 30)
        {
            Skip(ref b, 1);
            while (b.Cursor < b.Size)
            {
                int v = Get8(ref b);
                if ((v & 0xF) == 0xF || v >> 4 == 0xF)
                    break;
            }
        }
        else
        {
            CffInt(ref b);
        }
    }

    /// <summary><c>stbtt__dict_get</c>: the operands of the first entry with <paramref name="key"/>.</summary>
    private CffBuf DictGet(CffBuf b, int key)
    {
        Seek(ref b, 0);
        while (b.Cursor < b.Size)
        {
            int start = b.Cursor;
            while (Peek8(ref b) >= 28)
                SkipOperand(ref b);
            int end = b.Cursor;
            int op = Get8(ref b);
            if (op == 12)
                op = Get8(ref b) | 0x100;
            if (op == key)
                return Range(b, start, end - start);
        }

        return Range(b, 0, 0);
    }

    /// <summary><c>stbtt__dict_get_ints</c>: fills <paramref name="values"/> from the entry's operands, leaving the rest unchanged.</summary>
    private void DictInts(CffBuf dict, int key, Span<uint> values)
    {
        var operands = DictGet(dict, key);
        for (int i = 0; i < values.Length && operands.Cursor < operands.Size; i++)
            values[i] = CffInt(ref operands);
    }

    private uint DictInt(CffBuf dict, int key, uint fallback)
    {
        Span<uint> value = [fallback];
        DictInts(dict, key, value);
        return value[0];
    }
}
