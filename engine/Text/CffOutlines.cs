namespace Frisket
{
    /// <summary>
    /// Just enough of the CFF table to find out how tall a glyph really is.
    ///
    /// OpenType fonts with PostScript outlines carry no <c>glyf</c> table, so the height
    /// of a capital cannot be read off a bounding box the way it can in a TrueType face.
    /// For a while that left the declared cap height in OS/2 as the only source, taken on
    /// trust - and one of the faces shipped here declares 0.640 where its H measures 0.730,
    /// which sizes every capital 14% short. So the charstring is walked instead.
    ///
    /// Only the vertical extent is tracked: this is not a rasteriser, and x never matters
    /// for the one question being asked.
    /// </summary>
    internal static class CffOutlines
    {
        /// <summary>
        /// The top of a glyph's outline, in em units, or 0 when it cannot be determined.
        /// </summary>
        public static double GlyphTop(byte[] data, int cffOffset, ushort glyph, int unitsPerEm)
        {
            if (glyph == 0 || cffOffset <= 0 || cffOffset + 4 > data.Length)
                return 0;

            try
            {
                int p = cffOffset + data[cffOffset + 2];        // past the header
                p = SkipIndex(data, p);                          // Name
                (int, int)[] tops = ReadIndex(data, p, out p);   // Top DICT
                p = SkipIndex(data, p);                          // String
                (int, int)[] globalSubrs = ReadIndex(data, p, out _);

                if (tops.Length == 0)
                    return 0;

                Dictionary<int, List<double>> top = ReadDict(data, tops[0].Item1, tops[0].Item2);
                if (!top.TryGetValue(17, out List<double>? charStringsOp) || charStringsOp.Count == 0)
                    return 0;

                (int, int)[] charStrings = ReadIndex(data, cffOffset + (int)charStringsOp[0], out _);
                if (glyph >= charStrings.Length)
                    return 0;

                // Local subroutines live behind the Private DICT, whose own location the
                // Top DICT gives as a (size, offset) pair.
                (int, int)[] localSubrs = Array.Empty<(int, int)>();
                if (top.TryGetValue(18, out List<double>? priv) && priv.Count >= 2)
                {
                    int size = (int)priv[0], at = cffOffset + (int)priv[1];
                    Dictionary<int, List<double>> privateDict = ReadDict(data, at, at + size);
                    if (privateDict.TryGetValue(19, out List<double>? subrs) && subrs.Count > 0)
                        localSubrs = ReadIndex(data, at + (int)subrs[0], out _);
                }

                var state = new Walk(data, globalSubrs, localSubrs);
                state.Run(charStrings[glyph].Item1, charStrings[glyph].Item2, 0);

                return state.Found ? state.YMax / unitsPerEm : 0;
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                // A malformed charstring is a reason to fall back on the declaration,
                // not to refuse the font.
                return 0;
            }
        }

        // ---- INDEX ----------------------------------------------------------------

        private static (int Start, int End)[] ReadIndex(byte[] d, int at, out int next)
        {
            int count = (d[at] << 8) | d[at + 1];
            if (count == 0)
            {
                next = at + 2;
                return Array.Empty<(int, int)>();
            }

            int offSize = d[at + 2];
            int table = at + 3;
            int data = table + (count + 1) * offSize - 1;

            var items = new (int, int)[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = (data + Offset(d, table + i * offSize, offSize),
                            data + Offset(d, table + (i + 1) * offSize, offSize));
            }

            next = data + Offset(d, table + count * offSize, offSize);
            return items;
        }

        private static int SkipIndex(byte[] d, int at)
        {
            ReadIndex(d, at, out int next);
            return next;
        }

        private static int Offset(byte[] d, int at, int size)
        {
            int v = 0;
            for (int i = 0; i < size; i++)
                v = (v << 8) | d[at + i];
            return v;
        }

        // ---- DICT -----------------------------------------------------------------

        private static Dictionary<int, List<double>> ReadDict(byte[] d, int start, int end)
        {
            var result = new Dictionary<int, List<double>>();
            var operands = new List<double>();
            int i = start;

            while (i < end)
            {
                int b = d[i];

                if (b <= 21)
                {
                    int op = b;
                    i++;
                    if (b == 12) { op = 1200 + d[i]; i++; }
                    result[op] = operands;
                    operands = new List<double>();
                }
                else if (b == 28) { operands.Add(unchecked((short)((d[i + 1] << 8) | d[i + 2]))); i += 3; }
                else if (b == 29)
                {
                    operands.Add((d[i + 1] << 24) | (d[i + 2] << 16) | (d[i + 3] << 8) | d[i + 4]);
                    i += 5;
                }
                else if (b == 30) { operands.Add(ReadReal(d, ref i, end)); }
                else if (b <= 246) { operands.Add(b - 139); i++; }
                else if (b <= 250) { operands.Add((b - 247) * 256 + d[i + 1] + 108); i += 2; }
                else if (b <= 254) { operands.Add(-(b - 251) * 256 - d[i + 1] - 108); i += 2; }
                else i++;
            }

            return result;
        }

        /// <summary>A real number, written one decimal digit per nibble.</summary>
        private static double ReadReal(byte[] d, ref int i, int end)
        {
            var text = new System.Text.StringBuilder();
            i++;

            while (i < end)
            {
                byte b = d[i++];
                bool done = false;

                foreach (int nibble in stackalloc[] { b >> 4, b & 15 })
                {
                    if (nibble <= 9) text.Append((char)('0' + nibble));
                    else if (nibble == 10) text.Append('.');
                    else if (nibble == 11) text.Append('E');
                    else if (nibble == 12) text.Append("E-");
                    else if (nibble == 14) text.Append('-');
                    else if (nibble == 15) { done = true; break; }
                }

                if (done) break;
            }

            return double.TryParse(text.ToString(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        // ---- Type 2 charstring ----------------------------------------------------

        /// <summary>
        /// Walks a charstring keeping only the pen's y and the highest point it reached.
        /// Control points count: a curve can bulge above both its ends, and the overshoot
        /// on a round capital like O is exactly what a cap height has to include.
        /// </summary>
        private sealed class Walk
        {
            private readonly byte[] _d;
            private readonly (int Start, int End)[] _global;
            private readonly (int Start, int End)[] _local;
            private readonly List<double> _stack = new();
            private double _y;
            private int _stems;

            public double YMax { get; private set; } = double.MinValue;
            public bool Found => YMax > double.MinValue;

            public Walk(byte[] data, (int, int)[] global, (int, int)[] local)
                => (_d, _global, _local) = (data, global, local);

            private void Move(double dy)
            {
                _y += dy;
                if (_y > YMax) YMax = _y;
            }

            private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

            public void Run(int start, int end, int depth)
            {
                if (depth > 10)
                    return;   // charstrings may not recurse this deep; a malformed one might

                int i = start;

                while (i < end)
                {
                    int b = _d[i];

                    if (b >= 32 || b == 28)
                    {
                        if (b == 28) { _stack.Add(unchecked((short)((_d[i + 1] << 8) | _d[i + 2]))); i += 3; }
                        else if (b <= 246) { _stack.Add(b - 139); i++; }
                        else if (b <= 250) { _stack.Add((b - 247) * 256 + _d[i + 1] + 108); i += 2; }
                        else if (b <= 254) { _stack.Add(-(b - 251) * 256 - _d[i + 1] - 108); i += 2; }
                        else
                        {
                            int fixed16 = (_d[i + 1] << 24) | (_d[i + 2] << 16) | (_d[i + 3] << 8) | _d[i + 4];
                            _stack.Add(fixed16 / 65536.0);
                            i += 5;
                        }
                        continue;
                    }

                    i++;

                    switch (b)
                    {
                        case 1: case 3: case 18: case 23:           // h/vstem, h/vstemhm
                            _stems += _stack.Count / 2;
                            _stack.Clear();
                            break;

                        case 19: case 20:                            // hintmask, cntrmask
                            _stems += _stack.Count / 2;
                            _stack.Clear();
                            i += (_stems + 7) / 8;
                            break;

                        case 21:                                     // rmoveto
                            if (_stack.Count >= 2) Move(_stack[^1]);
                            _stack.Clear();
                            break;

                        case 22:                                     // hmoveto
                            Move(0);
                            _stack.Clear();
                            break;

                        case 4:                                      // vmoveto
                            if (_stack.Count >= 1) Move(_stack[^1]);
                            _stack.Clear();
                            break;

                        case 5:                                      // rlineto
                            for (int k = 0; k + 1 < _stack.Count; k += 2) Move(_stack[k + 1]);
                            _stack.Clear();
                            break;

                        case 6: case 7:                              // hlineto, vlineto
                        {
                            bool horizontal = b == 6;
                            foreach (double v in _stack)
                            {
                                if (!horizontal) Move(v);
                                horizontal = !horizontal;
                            }
                            _stack.Clear();
                            break;
                        }

                        case 8:                                      // rrcurveto
                            for (int k = 0; k + 5 < _stack.Count; k += 6)
                            {
                                Move(_stack[k + 1]); Move(_stack[k + 3]); Move(_stack[k + 5]);
                            }
                            _stack.Clear();
                            break;

                        case 24:                                     // rcurveline
                        {
                            int k = 0;
                            while (_stack.Count - k > 7)
                            {
                                Move(_stack[k + 1]); Move(_stack[k + 3]); Move(_stack[k + 5]);
                                k += 6;
                            }
                            if (k + 1 < _stack.Count) Move(_stack[k + 1]);
                            _stack.Clear();
                            break;
                        }

                        case 25:                                     // rlinecurve
                        {
                            int k = 0;
                            while (_stack.Count - k > 6) { Move(_stack[k + 1]); k += 2; }
                            if (k + 5 < _stack.Count)
                            {
                                Move(_stack[k + 1]); Move(_stack[k + 3]); Move(_stack[k + 5]);
                            }
                            _stack.Clear();
                            break;
                        }

                        case 26: case 27:                            // vvcurveto, hhcurveto
                        {
                            int k = (_stack.Count % 4) != 0 ? 1 : 0;
                            while (k + 3 < _stack.Count)
                            {
                                if (b == 26) { Move(_stack[k]); Move(_stack[k + 2]); Move(_stack[k + 3]); }
                                else Move(_stack[k + 2]);
                                k += 4;
                            }
                            _stack.Clear();
                            break;
                        }

                        case 30: case 31:                            // vhcurveto, hvcurveto
                        {
                            bool horizontal = b == 31;
                            int k = 0;
                            while (k + 3 < _stack.Count)
                            {
                                bool last = _stack.Count - k == 5;
                                if (horizontal)
                                {
                                    Move(_stack[k + 2]); Move(_stack[k + 3]);
                                }
                                else
                                {
                                    Move(_stack[k]); Move(_stack[k + 2]);
                                    if (last) Move(_stack[k + 4]);
                                }
                                horizontal = !horizontal;
                                k += 4;
                            }
                            _stack.Clear();
                            break;
                        }

                        case 10:                                     // callsubr
                            CallSubr(_local, depth);
                            break;

                        case 29:                                     // callgsubr
                            CallSubr(_global, depth);
                            break;

                        case 11:                                     // return
                            return;

                        case 14:                                     // endchar
                            return;

                        default:
                            _stack.Clear();
                            break;
                    }
                }
            }

            private void CallSubr((int Start, int End)[] subrs, int depth)
            {
                if (_stack.Count == 0)
                    return;

                int n = (int)_stack[^1] + Bias(subrs.Length);
                _stack.RemoveAt(_stack.Count - 1);

                if (n >= 0 && n < subrs.Length)
                    Run(subrs[n].Start, subrs[n].End, depth + 1);
            }
        }
    }
}
