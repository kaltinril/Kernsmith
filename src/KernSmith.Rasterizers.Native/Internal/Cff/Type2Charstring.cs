using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>The outline and width a charstring encodes, in charstring units.</summary>
/// <param name="Commands">Cubic pen commands, each contour closed.</param>
/// <param name="Width">The advance the charstring encodes (CFF) — <c>defaultWidthX</c> when it encodes none, 0 for CFF2.</param>
/// <param name="StemCount">Hint stems declared, implicit <c>vstem</c>s included.</param>
internal sealed record CharstringOutline(OutlineCommand[] Commands, double Width, int StemCount);

/// <summary>
/// Type 2 (Adobe TN#5177) and CFF2 charstring interpreter: runs a glyph's charstring and emits its
/// outline as cubic pen commands. Hints are parsed for their side effects (stem count, mask bytes,
/// the width operand) and otherwise ignored.
/// </summary>
/// <remarks>
/// Hostile input is bounded rather than trusted: the operand stack (48 entries for CFF, 513 for
/// CFF2), subroutine nesting (10 levels), the total work per glyph, the number of emitted commands
/// and the coordinate range are all capped, and every violation throws
/// <see cref="FontFormatException"/>. The work cap is what stops a subroutine call tree that is
/// shallow enough to pass the nesting limit but fans out exponentially.
/// </remarks>
internal static class Type2Charstring
{
    /// <summary>Operators plus operands a single glyph may execute, subroutines and seac components included.</summary>
    internal const int MaxOperations = 1 << 20;

    /// <summary>Pen commands a single glyph may emit.</summary>
    internal const int MaxCommands = 1 << 16;

    /// <summary>
    /// Largest coordinate magnitude, in charstring units, a glyph may reach. FreeType holds CFF
    /// coordinates in 16.16 fixed point, so nothing renderable exceeds ±32768; the cap is twice
    /// that and exists so a runaway outline cannot demand an enormous bitmap.
    /// </summary>
    internal const double MaxCoordinate = 65536;

    private const int MaxSubroutineDepth = 10;
    private const int TransientArraySize = 32;

    /// <summary>Runs a charstring.</summary>
    /// <exception cref="FontFormatException">If the charstring is malformed or exceeds a limit.</exception>
    public static CharstringOutline Interpret(ReadOnlyMemory<byte> charstring, CharstringContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var commands = new List<OutlineCommand>();
        var run = new Run(context, commands, new Budget(), allowSeac: true, offsetX: 0, offsetY: 0);
        run.Execute(charstring.Span, depth: 0);
        run.Finish();

        return new CharstringOutline([.. commands], run.Width, run.StemCount);
    }

    /// <summary>Work shared by a glyph and its seac components.</summary>
    private sealed class Budget
    {
        public int Operations;
    }

    /// <summary>One charstring execution: its stack, hint state, pen and output.</summary>
    private sealed class Run
    {
        private readonly CharstringContext _context;
        private readonly List<OutlineCommand> _commands;
        private readonly Budget _budget;
        private readonly bool _allowSeac;
        private readonly double _offsetX;
        private readonly double _offsetY;
        private readonly double[] _stack;
        private readonly double[] _transient = new double[TransientArraySize];

        private int _count;
        private bool _widthParsed;
        private int _vsIndex;
        private bool _ended;

        private double _x;
        private double _y;
        private bool _contourOpen;
        private double _startX;
        private double _startY;

        public Run(CharstringContext context, List<OutlineCommand> commands, Budget budget, bool allowSeac, double offsetX, double offsetY)
        {
            _context = context;
            _commands = commands;
            _budget = budget;
            _allowSeac = allowSeac;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _stack = new double[context.IsCff2 ? 513 : 48];
            _vsIndex = context.VsIndex;
            Width = context.IsCff2 ? 0 : context.DefaultWidthX;
        }

        public double Width { get; private set; }

        public int StemCount { get; private set; }

        /// <summary>Closes the last contour; a CFF2 charstring (no endchar) ends at its last byte.</summary>
        public void Finish() => ClosePath();

        /// <summary>Executes charstring or subroutine bytes.</summary>
        /// <returns>True once <c>endchar</c> has run, which ends the whole glyph.</returns>
        public bool Execute(ReadOnlySpan<byte> code, int depth)
        {
            int i = 0;
            while (i < code.Length)
            {
                if (++_budget.Operations > MaxOperations)
                    throw new FontFormatException($"Charstring exceeds {MaxOperations} operations.");

                byte b0 = code[i];
                if (b0 >= 32 || b0 == 28)
                {
                    Push(ReadNumber(code, ref i));
                    continue;
                }

                i++;
                switch (b0)
                {
                    case 1: // hstem
                    case 3: // vstem
                    case 18: // hstemhm
                    case 23: // vstemhm
                        ParseWidth(_count % 2 == 1);
                        StemCount += _count / 2;
                        Clear();
                        break;

                    case 19: // hintmask
                    case 20: // cntrmask
                        // Operands left here are an implicit vstem (hm) list.
                        ParseWidth(_count % 2 == 1);
                        StemCount += _count / 2;
                        Clear();
                        int maskBytes = (StemCount + 7) / 8;
                        if (maskBytes > code.Length - i)
                            throw new FontFormatException("Charstring ends inside a hint mask.");
                        i += maskBytes;
                        break;

                    case 21: // rmoveto
                        ParseWidth(_count > 2);
                        Need(2);
                        MoveTo(_x + _stack[0], _y + _stack[1]);
                        Clear();
                        break;

                    case 22: // hmoveto
                        ParseWidth(_count > 1);
                        Need(1);
                        MoveTo(_x + _stack[0], _y);
                        Clear();
                        break;

                    case 4: // vmoveto
                        ParseWidth(_count > 1);
                        Need(1);
                        MoveTo(_x, _y + _stack[0]);
                        Clear();
                        break;

                    case 5: // rlineto
                        Need(2);
                        for (int a = 0; a + 1 < _count; a += 2)
                            LineTo(_x + _stack[a], _y + _stack[a + 1]);
                        Clear();
                        break;

                    case 6: // hlineto
                    case 7: // vlineto
                        Need(1);
                        bool horizontal = b0 == 6;
                        for (int a = 0; a < _count; a++, horizontal = !horizontal)
                        {
                            if (horizontal)
                                LineTo(_x + _stack[a], _y);
                            else
                                LineTo(_x, _y + _stack[a]);
                        }

                        Clear();
                        break;

                    case 8: // rrcurveto
                        Need(6);
                        for (int a = 0; a + 5 < _count; a += 6)
                            CurveBy(_stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], _stack[a + 4], _stack[a + 5]);
                        Clear();
                        break;

                    case 27: // hhcurveto: dy1? {dxa dxb dyb dxc}+
                        {
                            Need(4);
                            int a = 0;
                            double dy1 = 0;
                            if (_count % 2 == 1)
                                dy1 = _stack[a++];
                            for (; a + 3 < _count; a += 4, dy1 = 0)
                                CurveBy(_stack[a], dy1, _stack[a + 1], _stack[a + 2], _stack[a + 3], 0);
                            Clear();
                            break;
                        }

                    case 26: // vvcurveto: dx1? {dya dxb dyb dyc}+
                        {
                            Need(4);
                            int a = 0;
                            double dx1 = 0;
                            if (_count % 2 == 1)
                                dx1 = _stack[a++];
                            for (; a + 3 < _count; a += 4, dx1 = 0)
                                CurveBy(dx1, _stack[a], _stack[a + 1], _stack[a + 2], 0, _stack[a + 3]);
                            Clear();
                            break;
                        }

                    case 31: // hvcurveto
                    case 30: // vhcurveto
                        Need(4);
                        AlternatingCurves(startHorizontal: b0 == 31);
                        Clear();
                        break;

                    case 24: // rcurveline: {curve}+ line
                        {
                            Need(8);
                            int a = 0;
                            for (; a + 5 < _count - 2; a += 6)
                                CurveBy(_stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], _stack[a + 4], _stack[a + 5]);
                            LineTo(_x + _stack[a], _y + _stack[a + 1]);
                            Clear();
                            break;
                        }

                    case 25: // rlinecurve: {line}+ curve
                        {
                            Need(8);
                            int a = 0;
                            for (; a + 1 < _count - 6; a += 2)
                                LineTo(_x + _stack[a], _y + _stack[a + 1]);
                            CurveBy(_stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], _stack[a + 4], _stack[a + 5]);
                            Clear();
                            break;
                        }

                    case 10: // callsubr
                    case 29: // callgsubr
                        {
                            var subrs = b0 == 10 ? _context.LocalSubrs : _context.GlobalSubrs;
                            int index = PopInteger() + subrs.Bias;
                            if ((uint)index >= (uint)subrs.Count)
                                throw new FontFormatException(
                                    $"Charstring calls {(b0 == 10 ? "local" : "global")} subroutine {index}, outside 0..{subrs.Count - 1}.");

                            if (depth + 1 > MaxSubroutineDepth)
                                throw new FontFormatException($"Charstring subroutines nest deeper than {MaxSubroutineDepth} levels.");

                            if (Execute(subrs[index].Span, depth + 1))
                                return true;
                            break;
                        }

                    case 11: // return
                        return false;

                    case 14: // endchar
                        EndChar();
                        return true;

                    case 15: // vsindex (CFF2)
                        RequireCff2(b0);
                        _vsIndex = PopInteger();
                        CffFont.RegionCount(_context.RegionCounts, _vsIndex);
                        Clear();
                        break;

                    case 16: // blend (CFF2)
                        RequireCff2(b0);
                        Blend();
                        break;

                    case 12:
                        if (i >= code.Length)
                            throw new FontFormatException("Charstring ends inside a two-byte operator.");
                        Escape(code[i++]);
                        break;

                    default:
                        throw new FontFormatException($"Charstring uses reserved operator {b0}.");
                }

                if (_ended)
                    return true;
            }

            return false;
        }

        private void Escape(byte op)
        {
            switch (op)
            {
                case 0: // dotsection (deprecated, no effect)
                    Clear();
                    break;

                case 35: // flex: two curves from 12 operands, then fd (flex depth) ignored
                    Need(13);
                    CurveBy(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                    CurveBy(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
                    Clear();
                    break;

                case 34: // hflex: dx1 dx2 dy2 dx3 dx4 dx5 dx6
                    Need(7);
                    CurveBy(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                    CurveBy(_stack[4], 0, _stack[5], -_stack[2], _stack[6], 0);
                    Clear();
                    break;

                case 36: // hflex1: dx1 dy1 dx2 dy2 dx3 dx4 dx5 dy5 dx6
                    {
                        Need(9);
                        double dy6 = -(_stack[1] + _stack[3] + _stack[7]);
                        CurveBy(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                        CurveBy(_stack[5], 0, _stack[6], _stack[7], _stack[8], dy6);
                        Clear();
                        break;
                    }

                case 37: // flex1: dx1 dy1 … dx5 dy5 d6
                    {
                        Need(11);
                        double dx = 0, dy = 0;
                        for (int a = 0; a < 10; a += 2)
                        {
                            dx += _stack[a];
                            dy += _stack[a + 1];
                        }

                        // The last point moves along the dominant axis and returns to the start on the other.
                        (double dx6, double dy6) = Math.Abs(dx) > Math.Abs(dy) ? (_stack[10], -dy) : (-dx, _stack[10]);
                        CurveBy(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                        CurveBy(_stack[6], _stack[7], _stack[8], _stack[9], dx6, dy6);
                        Clear();
                        break;
                    }

                case 3: Binary((a, b) => !a.Equals(0d) && !b.Equals(0d) ? 1 : 0); break; // and
                case 4: Binary((a, b) => !a.Equals(0d) || !b.Equals(0d) ? 1 : 0); break; // or
                case 5: Unary(a => a.Equals(0d) ? 1 : 0); break; // not
                case 9: Unary(Math.Abs); break; // abs
                case 10: Binary((a, b) => a + b); break; // add
                case 11: Binary((a, b) => a - b); break; // sub
                case 12: // div
                    Binary((a, b) => b.Equals(0d) ? throw new FontFormatException("Charstring divides by zero.") : a / b);
                    break;
                case 14: Unary(a => -a); break; // neg
                case 15: Binary((a, b) => a.Equals(b) && !double.IsNaN(a) ? 1 : 0); break; // eq (NaN is never equal, as with ==)
                case 18: // drop
                    Pop();
                    break;
                case 20: // put: val i
                    {
                        int slot = TransientSlot(PopInteger());
                        _transient[slot] = Pop();
                        break;
                    }

                case 21: // get: i
                    Push(_transient[TransientSlot(PopInteger())]);
                    break;
                case 22: // ifelse: s1 s2 v1 v2
                    {
                        double v2 = Pop(), v1 = Pop(), s2 = Pop(), s1 = Pop();
                        Push(v1 <= v2 ? s1 : s2);
                        break;
                    }

                case 24: Binary((a, b) => a * b); break; // mul
                case 26: // sqrt
                    Unary(a => a < 0 ? throw new FontFormatException("Charstring takes the square root of a negative number.") : Math.Sqrt(a));
                    break;
                case 27: // dup
                    {
                        double top = Pop();
                        Push(top);
                        Push(top);
                        break;
                    }

                case 28: // exch
                    {
                        double b = Pop(), a = Pop();
                        Push(b);
                        Push(a);
                        break;
                    }

                case 29: // index
                    {
                        int n = PopInteger();
                        if (n < 0)
                            n = 0;
                        if (n >= _count)
                            throw new FontFormatException("Charstring index reaches below the stack.");
                        Push(_stack[_count - 1 - n]);
                        break;
                    }

                case 30: // roll: N J
                    {
                        int j = PopInteger();
                        int n = PopInteger();
                        if (n < 0 || n > _count)
                            throw new FontFormatException("Charstring roll count is outside the stack.");
                        if (n > 0)
                            Roll(n, j);
                        break;
                    }

                case 23: // random: deprecated, and nondeterministic output is no use for an atlas
                    throw new FontFormatException("Charstring uses the unsupported 'random' operator.");

                default:
                    throw new FontFormatException($"Charstring uses reserved operator 12 {op}.");
            }
        }

        private void EndChar()
        {
            if (_context.IsCff2)
                throw new FontFormatException("CFF2 charstrings have no endchar operator.");

            ParseWidth(_count == 1 || _count == 5);
            if (_count == 4)
                Seac(_stack[0], _stack[1], _stack[2], _stack[3]);

            Clear();
            ClosePath();
            _ended = true;
        }

        /// <summary>
        /// The deprecated <c>seac</c> form of endchar: draws the base character, then the accent
        /// offset by (adx, ady). Both are Standard Encoding codes.
        /// </summary>
        private void Seac(double adx, double ady, double baseCode, double accentCode)
        {
            if (!_allowSeac || _context.SeacComponent is null)
                throw new FontFormatException("Charstring uses seac where accent composition is unavailable.");

            ClosePath();
            DrawComponent(baseCode, 0, 0);
            DrawComponent(accentCode, adx, ady);
        }

        private void DrawComponent(double code, double dx, double dy)
        {
            int standardCode = Integer(code);
            var charstring = _context.SeacComponent!(standardCode)
                ?? throw new FontFormatException($"seac names Standard Encoding code {standardCode}, which the font lacks.");

            var component = new Run(_context, _commands, _budget, allowSeac: false, _offsetX + dx, _offsetY + dy);
            component.Execute(charstring.Span, depth: 0);
            component.Finish();
        }

        /// <summary>The CFF2 blend operator: keeps n default values, applying any region scalars to their deltas.</summary>
        private void Blend()
        {
            int n = PopInteger();
            int k = CffFont.RegionCount(_context.RegionCounts, _vsIndex);
            long operands = (long)n * (k + 1);
            if (n < 0 || operands > _count)
                throw new FontFormatException($"CFF2 blend of {n} values with {k} regions needs more operands than the stack holds.");

            int defaults = _count - (int)operands;
            double[]? scalars = _context.BlendScalars?.Invoke(_vsIndex);
            if (scalars is not null)
            {
                if (scalars.Length != k)
                    throw new FontFormatException($"CFF2 blend has {scalars.Length} region scalars for {k} regions.");

                for (int v = 0; v < n; v++)
                {
                    int deltas = defaults + n + (v * k);
                    for (int r = 0; r < k; r++)
                        _stack[defaults + v] += _stack[deltas + r] * scalars[r];
                }
            }

            _count = defaults + n;
        }

        /// <summary>hvcurveto / vhcurveto: curves alternating between horizontal and vertical tangents.</summary>
        private void AlternatingCurves(bool startHorizontal)
        {
            bool horizontal = startHorizontal;
            for (int a = 0; _count - a >= 4; a += 4, horizontal = !horizontal)
            {
                // The final curve takes a fifth operand for the otherwise-zero end delta.
                double extra = _count - a == 5 ? _stack[a + 4] : 0;
                if (horizontal)
                    CurveBy(_stack[a], 0, _stack[a + 1], _stack[a + 2], extra, _stack[a + 3]);
                else
                    CurveBy(0, _stack[a], _stack[a + 1], _stack[a + 2], _stack[a + 3], extra);
            }
        }

        /// <summary>
        /// Takes the width operand off the bottom of the stack the first time a stack-clearing
        /// operator that can carry one runs. CFF2 has no width.
        /// </summary>
        private void ParseWidth(bool hasWidth)
        {
            if (_widthParsed || _context.IsCff2)
                return;

            _widthParsed = true;
            if (!hasWidth)
                return;

            Width = _context.NominalWidthX + _stack[0];
            Array.Copy(_stack, 1, _stack, 0, _count - 1);
            _count--;
        }

        private void RequireCff2(byte op)
        {
            if (!_context.IsCff2)
                throw new FontFormatException($"Charstring uses reserved operator {op}.");
        }

        // ---- Path ------------------------------------------------------------------------------

        private void MoveTo(double x, double y)
        {
            ClosePath();
            _x = x;
            _y = y;
        }

        private void LineTo(double x, double y)
        {
            OpenContour();
            _x = x;
            _y = y;
            Emit(OutlineCommand.Line(X(x), Y(y)));
        }

        private void CurveBy(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            OpenContour();
            double x1 = _x + dx1, y1 = _y + dy1;
            double x2 = x1 + dx2, y2 = y1 + dy2;
            _x = x2 + dx3;
            _y = y2 + dy3;
            Emit(OutlineCommand.Cubic(X(x1), Y(y1), X(x2), Y(y2), X(_x), Y(_y)));
        }

        /// <summary>
        /// Opens a contour at the current point on the first drawing operator after a moveto, so a
        /// moveto that draws nothing leaves no stray point to widen the glyph's bounds.
        /// </summary>
        private void OpenContour()
        {
            if (_contourOpen)
                return;

            _contourOpen = true;
            _startX = _x;
            _startY = _y;
            Emit(OutlineCommand.Move(X(_x), Y(_y)));
        }

        private void ClosePath()
        {
            if (!_contourOpen)
                return;

            _contourOpen = false;
            Emit(OutlineCommand.CloseAt(X(_startX), Y(_startY)));
        }

        private float X(double x) => Coordinate(x + _offsetX);

        private float Y(double y) => Coordinate(y + _offsetY);

        private static float Coordinate(double value)
        {
            if (!(Math.Abs(value) <= MaxCoordinate))
                throw new FontFormatException($"Charstring coordinate {value} is outside ±{MaxCoordinate}.");

            return (float)value;
        }

        private void Emit(OutlineCommand command)
        {
            if (_commands.Count >= MaxCommands)
                throw new FontFormatException($"Charstring emits more than {MaxCommands} path commands.");

            _commands.Add(command);
        }

        // ---- Stack -----------------------------------------------------------------------------

        private void Push(double value)
        {
            if (_count >= _stack.Length)
                throw new FontFormatException($"Charstring operand stack exceeds {_stack.Length} entries.");

            _stack[_count++] = value;
        }

        private double Pop()
        {
            if (_count == 0)
                throw new FontFormatException("Charstring operand stack underflow.");

            return _stack[--_count];
        }

        private int PopInteger() => Integer(Pop());

        private static int Integer(double value)
        {
            if (!double.IsInteger(value) || value < int.MinValue || value > int.MaxValue)
                throw new FontFormatException($"Charstring operand {value} is not an integer.");

            return (int)value;
        }

        private void Need(int count)
        {
            if (_count < count)
                throw new FontFormatException($"Charstring operator needs {count} operands but the stack holds {_count}.");
        }

        private void Clear() => _count = 0;

        private void Unary(Func<double, double> op) => Push(op(Pop()));

        private void Binary(Func<double, double, double> op)
        {
            double b = Pop(), a = Pop();
            Push(op(a, b));
        }

        private static int TransientSlot(int index) =>
            (uint)index < TransientArraySize
                ? index
                : throw new FontFormatException($"Charstring transient array index {index} is outside 0..{TransientArraySize - 1}.");

        /// <summary>Rolls the top <paramref name="n"/> elements by <paramref name="j"/> positions towards the top.</summary>
        private void Roll(int n, int j)
        {
            int shift = ((j % n) + n) % n;
            if (shift == 0)
                return;

            int start = _count - n;
            var window = _stack.AsSpan(start, n);
            Span<double> copy = stackalloc double[n];
            window.CopyTo(copy);
            for (int e = 0; e < n; e++)
                window[(e + shift) % n] = copy[e];
        }

        /// <summary>Reads a charstring number: 1–3 byte integers, int16 (28) or 16.16 fixed (255).</summary>
        private static double ReadNumber(ReadOnlySpan<byte> code, ref int i)
        {
            byte b0 = code[i];

            if (b0 <= 246)
            {
                if (b0 == 28)
                {
                    Require(code, i, 3);
                    double value = (short)((code[i + 1] << 8) | code[i + 2]);
                    i += 3;
                    return value;
                }

                i++;
                return b0 - 139;
            }

            if (b0 == 255)
            {
                Require(code, i, 5);
                int raw = (code[i + 1] << 24) | (code[i + 2] << 16) | (code[i + 3] << 8) | code[i + 4];
                i += 5;
                return raw / 65536.0;
            }

            Require(code, i, 2);
            int b1 = code[i + 1];
            i += 2;
            return b0 <= 250
                ? ((b0 - 247) * 256) + b1 + 108
                : -((b0 - 251) * 256) - b1 - 108;
        }

        private static void Require(ReadOnlySpan<byte> code, int at, int length)
        {
            if (length > code.Length - at)
                throw new FontFormatException("Charstring ends inside a number.");
        }
    }
}
