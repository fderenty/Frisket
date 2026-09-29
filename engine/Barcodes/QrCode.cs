using System.Text.Json.Serialization;
namespace Frisket
{
    /// <summary>How much of a QR code can be lost and still read.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<QrErrorCorrection>))]
    public enum QrErrorCorrection
    {
        /// <summary>About 7%. The densest, for a clean printed page.</summary>
        Low,

        /// <summary>About 15%. The usual choice, and the default.</summary>
        Medium,

        /// <summary>About 25%.</summary>
        Quartile,

        /// <summary>About 30%. For a label that will be handled, folded or stamped over.</summary>
        High
    }

    /// <summary>
    /// A QR code, as a square of light and dark modules.
    ///
    /// Byte mode only, which encodes anything at all as UTF-8 and is what every reader
    /// supports. Numeric and alphanumeric modes would pack digits tighter, at the cost of
    /// a mode-splitting pass that pays only for very long numbers.
    ///
    /// The result is a grid, not a picture: the renderer draws it with the rectangles it
    /// already knows how to draw, so every backend gets QR codes without a new primitive.
    /// </summary>
    public static class QrCode
    {
        /// <summary>Total codewords per version, 1..40.</summary>
        private static readonly int[] TotalCodewords =
        {
            0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346,
            404, 466, 532, 581, 655, 733, 815, 901, 991, 1085,
            1156, 1258, 1364, 1474, 1588, 1706, 1828, 1921, 2051, 2185,
            2323, 2465, 2611, 2761, 2876, 3034, 3196, 3362, 3532, 3706
        };

        /// <summary>
        /// Error correction codewords per block, then the block structure, per version and
        /// level: { ecPerBlock, blocksInGroup1, blocksInGroup2 }. Data codewords per block
        /// follow from the total, so only the split has to be tabulated.
        /// </summary>
        private static readonly int[][,] Structure = BuildStructure();

        /// <summary>Centres of the alignment patterns, per version.</summary>
        private static readonly int[][] Alignment =
        {
            new int[0], new int[0],
            new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
            new[] { 6, 30, 54 }, new[] { 6, 32, 58 }, new[] { 6, 34, 62 },
            new[] { 6, 26, 46, 66 }, new[] { 6, 26, 48, 70 }, new[] { 6, 26, 50, 74 },
            new[] { 6, 30, 54, 78 }, new[] { 6, 30, 56, 82 }, new[] { 6, 30, 58, 86 },
            new[] { 6, 34, 62, 90 },
            new[] { 6, 28, 50, 72, 94 }, new[] { 6, 26, 50, 74, 98 }, new[] { 6, 30, 54, 78, 102 },
            new[] { 6, 28, 54, 80, 106 }, new[] { 6, 32, 58, 84, 110 }, new[] { 6, 30, 58, 86, 114 },
            new[] { 6, 34, 62, 90, 118 },
            new[] { 6, 26, 50, 74, 98, 122 }, new[] { 6, 30, 54, 78, 102, 126 },
            new[] { 6, 26, 52, 78, 104, 130 }, new[] { 6, 30, 56, 82, 108, 134 },
            new[] { 6, 34, 60, 86, 112, 138 }, new[] { 6, 30, 58, 86, 114, 142 },
            new[] { 6, 34, 62, 90, 118, 146 },
            new[] { 6, 30, 54, 78, 102, 126, 150 }, new[] { 6, 24, 50, 76, 102, 128, 154 },
            new[] { 6, 28, 54, 80, 106, 132, 158 }, new[] { 6, 32, 58, 84, 110, 136, 162 },
            new[] { 6, 26, 54, 82, 110, 138, 166 }, new[] { 6, 30, 58, 86, 114, 142, 170 }
        };

        /// <summary>
        /// Encodes <paramref name="text"/>. Returns a square grid, true where a module is
        /// dark, without the quiet zone - that is the caller's margin to leave.
        /// </summary>
        public static bool[,] Encode(string text, QrErrorCorrection level = QrErrorCorrection.Medium)
        {
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            int ec = (int)level;

            int version = SmallestVersion(payload.Length, ec);
            int size = 17 + version * 4;

            byte[] codewords = Interleave(DataBits(payload, version, ec), version, ec);

            var dark = new bool[size, size];
            var fixedModule = new bool[size, size];

            PlaceFunctionPatterns(dark, fixedModule, version);
            PlaceData(dark, fixedModule, codewords, size);

            // Eight masks exist because a QR full of large blank areas or of stripes reads
            // badly; the standard scores each and keeps the least offensive.
            int best = 0;
            int bestPenalty = int.MaxValue;
            bool[,] bestGrid = dark;

            for (int mask = 0; mask < 8; mask++)
            {
                bool[,] candidate = Apply(dark, fixedModule, mask, size);
                PlaceFormat(candidate, fixedModule, ec, mask, size);
                if (version >= 7)
                    PlaceVersion(candidate, version, size);

                int penalty = Penalty(candidate, size);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    best = mask;
                    bestGrid = candidate;
                }
            }

            _ = best;
            return bestGrid;
        }

        /// <summary>
        /// How many modules a version leaves for data once the function patterns are
        /// placed. The standard fixes this number, so it can be compared rather than
        /// trusted: reserving one module too many or too few shifts every bit after it,
        /// and the result is a code with flawless finders that decodes to nothing.
        /// </summary>
        internal static int FreeModules(int version)
        {
            int size = 17 + version * 4;
            var dark = new bool[size, size];
            var known = new bool[size, size];
            PlaceFunctionPatterns(dark, known, version);

            int free = 0;
            foreach (bool taken in known)
                if (!taken)
                    free++;

            return free;
        }

        // ---- capacity ---------------------------------------------------------------

        private static int DataCodewords(int version, int ec)
        {
            var s = Structure[ec];
            int blocks = s[version, 1] + s[version, 2];
            return TotalCodewords[version] - s[version, 0] * blocks;
        }

        private static int SmallestVersion(int bytes, int ec)
        {
            for (int version = 1; version <= 40; version++)
            {
                int header = 4 + (version <= 9 ? 8 : 16);        // mode indicator + length
                if (DataCodewords(version, ec) * 8 >= header + bytes * 8)
                    return version;
            }

            throw new ArgumentException(
                $"{bytes} bytes do not fit in a QR code at this error correction level. " +
                "Shorten the text, or lower the correction level.");
        }

        // ---- data -------------------------------------------------------------------

        private static byte[] DataBits(byte[] payload, int version, int ec)
        {
            int capacity = DataCodewords(version, ec);
            var bits = new BitBuffer();

            bits.Add(0b0100, 4);                                  // byte mode
            bits.Add(payload.Length, version <= 9 ? 8 : 16);
            foreach (byte b in payload)
                bits.Add(b, 8);

            // Terminator, then pad to a whole codeword, then the standard filler.
            bits.Add(0, Math.Min(4, capacity * 8 - bits.Length));
            while (bits.Length % 8 != 0)
                bits.Add(0, 1);

            byte[] data = bits.ToBytes();
            var padded = new byte[capacity];
            Array.Copy(data, padded, Math.Min(data.Length, capacity));

            for (int i = data.Length; i < capacity; i++)
                padded[i] = (i - data.Length) % 2 == 0 ? (byte)0xEC : (byte)0x11;

            return padded;
        }

        /// <summary>
        /// Splits the data into blocks, computes each block's error correction, and reads
        /// them back column-wise - which is what lets a burst of damage fall across many
        /// blocks instead of destroying one.
        /// </summary>
        private static byte[] Interleave(byte[] data, int version, int ec)
        {
            var s = Structure[ec];
            int ecPerBlock = s[version, 0];
            int blocks1 = s[version, 1];
            int blocks2 = s[version, 2];
            int blocks = blocks1 + blocks2;

            int shortLength = data.Length / blocks;
            var dataBlocks = new byte[blocks][];
            var ecBlocks = new byte[blocks][];

            int at = 0;
            for (int i = 0; i < blocks; i++)
            {
                int length = shortLength + (i < blocks1 ? 0 : 1);
                dataBlocks[i] = new byte[length];
                Array.Copy(data, at, dataBlocks[i], 0, length);
                at += length;
                ecBlocks[i] = ReedSolomon.Remainder(dataBlocks[i], ecPerBlock);
            }

            var result = new List<byte>(TotalCodewords[version]);

            for (int i = 0; i < shortLength + 1; i++)
                foreach (byte[] block in dataBlocks)
                    if (i < block.Length)
                        result.Add(block[i]);

            for (int i = 0; i < ecPerBlock; i++)
                foreach (byte[] block in ecBlocks)
                    result.Add(block[i]);

            return result.ToArray();
        }

        // ---- the grid ----------------------------------------------------------------

        private static void PlaceFunctionPatterns(bool[,] dark, bool[,] known, int version)
        {
            int size = dark.GetLength(0);

            void Finder(int x, int y)
            {
                for (int dy = -1; dy <= 7; dy++)
                {
                    for (int dx = -1; dx <= 7; dx++)
                    {
                        int px = x + dx, py = y + dy;
                        if (px < 0 || py < 0 || px >= size || py >= size)
                            continue;

                        int d = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                        dark[px, py] = d != 2 && d <= 3;
                        known[px, py] = true;
                    }
                }
            }

            Finder(0, 0);
            Finder(size - 7, 0);
            Finder(0, size - 7);

            for (int i = 8; i < size - 8; i++)
            {
                dark[i, 6] = dark[6, i] = i % 2 == 0;
                known[i, 6] = known[6, i] = true;
            }

            int[] centres = Alignment[version];
            int first = centres.Length > 0 ? centres[0] : 0;
            int last = centres.Length > 0 ? centres[^1] : 0;

            foreach (int cx in centres)
            {
                foreach (int cy in centres)
                {
                    // Only the three that would sit on a finder are left out. Skipping
                    // every centre already marked would drop the two that stand on the
                    // timing lines as well, which belong there - and their modules would
                    // then be handed to the data, shifting every bit that follows.
                    if ((cx == first && cy == first) || (cx == first && cy == last)
                        || (cx == last && cy == first))
                        continue;

                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            dark[cx + dx, cy + dy] = Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1;
                            known[cx + dx, cy + dy] = true;
                        }
                    }
                }
            }

            // The format areas, reserved now and filled once the mask is chosen. The
            // counts are not the same on each side and that matters: reserving one module
            // too many steals it from the data, and every bit after it lands one place
            // out. The code still shows perfect finders, timing and alignment - a reader
            // finds it and then decodes nothing.
            for (int i = 0; i <= 8; i++)
                known[i, 8] = known[8, i] = true;            // row 8 and column 8, near the corner

            for (int i = 0; i < 8; i++)
                known[size - 1 - i, 8] = true;               // row 8, last eight columns

            for (int i = 0; i < 7; i++)
                known[8, size - 1 - i] = true;               // column 8, last seven rows

            dark[8, size - 8] = true;                        // the module that is always dark
            known[8, size - 8] = true;

            if (version >= 7)
            {
                for (int i = 0; i < 18; i++)
                {
                    known[i / 3, size - 11 + i % 3] = true;
                    known[size - 11 + i % 3, i / 3] = true;
                }
            }
        }

        private static void PlaceData(bool[,] dark, bool[,] known, byte[] codewords, int size)
        {
            int bit = 0;
            bool Next() => bit < codewords.Length * 8
                        && (codewords[bit / 8] >> (7 - bit % 8) & 1) != 0;

            // Up the right-hand column pair, then down the next, skipping the timing column.
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;

                for (int step = 0; step < size; step++)
                {
                    // Which way this column pair runs. Taken from the column itself
                    // rather than counted from the edge: the pair that straddles the
                    // timing column is skipped, and counting would lose a turn there.
                    bool upwards = ((right + 1) & 2) == 0;
                    int y = upwards ? size - 1 - step : step;

                    for (int c = 0; c < 2; c++)
                    {
                        int x = right - c;
                        if (known[x, y])
                            continue;

                        dark[x, y] = Next();
                        bit++;
                    }
                }
            }
        }

        private static bool[,] Apply(bool[,] source, bool[,] known, int mask, int size)
        {
            var result = new bool[size, size];

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    result[x, y] = source[x, y];
                    if (known[x, y])
                        continue;

                    bool flip = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => (y / 2 + x / 3) % 2 == 0,
                        5 => x * y % 2 + x * y % 3 == 0,
                        6 => (x * y % 2 + x * y % 3) % 2 == 0,
                        _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                    };

                    if (flip)
                        result[x, y] = !result[x, y];
                }
            }

            return result;
        }

        /// <summary>
        /// The fifteen bits of level and mask, protected by a BCH code and XORed with a
        /// fixed pattern, most significant first. Exposed so a test can compare it with
        /// the published table: a matrix whose finders, timing and alignment are perfect
        /// still reads as nothing if these are wrong.
        /// </summary>
        internal static string FormatBits(QrErrorCorrection level, int mask)
        {
            int value = Format((int)level, mask);
            var text = new char[15];
            for (int i = 0; i < 15; i++)
                text[i] = (value >> (14 - i) & 1) != 0 ? '1' : '0';
            return new string(text);
        }

        private static int Format(int ec, int mask)
        {
            int[] indicator = { 1, 0, 3, 2 };                 // L, M, Q, H as the spec numbers them
            int format = indicator[ec] << 3 | mask;
            int bits = format << 10;

            for (int i = 4; i >= 0; i--)
                if ((bits >> (i + 10) & 1) != 0)
                    bits ^= 0b101_0011_0111 << i;

            return (format << 10 | bits) ^ 0b101_0100_0001_0010;
        }

        private static void PlaceFormat(bool[,] grid, bool[,] known, int ec, int mask, int size)
        {
            int value = Format(ec, mask);

            // Around the top-left finder: down column 8, then along row 8, skipping the
            // timing line. Written (column, row) - stated because the two copies read in
            // opposite directions and transposing one of them costs nothing structurally
            // while making the code undecodable.
            for (int i = 0; i <= 5; i++)
                grid[8, i] = (value >> i & 1) != 0;

            grid[8, 7] = (value >> 6 & 1) != 0;
            grid[8, 8] = (value >> 7 & 1) != 0;
            grid[7, 8] = (value >> 8 & 1) != 0;

            for (int i = 9; i < 15; i++)
                grid[14 - i, 8] = (value >> i & 1) != 0;

            // And the copy split between the other two corners.
            for (int i = 0; i < 8; i++)
                grid[size - 1 - i, 8] = (value >> i & 1) != 0;

            for (int i = 8; i < 15; i++)
                grid[8, size - 15 + i] = (value >> i & 1) != 0;

            _ = known;
        }

        private static void PlaceVersion(bool[,] grid, int version, int size)
        {
            int bits = version << 12;
            for (int i = 5; i >= 0; i--)
                if ((bits >> (i + 12) & 1) != 0)
                    bits ^= 0b1_1111_0010_0101 << i;

            int value = version << 12 | bits;

            for (int i = 0; i < 18; i++)
            {
                bool set = (value >> i & 1) != 0;
                grid[i / 3, size - 11 + i % 3] = set;
                grid[size - 11 + i % 3, i / 3] = set;
            }
        }

        // ---- mask scoring -------------------------------------------------------------

        private static int Penalty(bool[,] g, int size)
        {
            int score = 0;

            // Runs of five or more of one colour, in each direction.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < size; b++)
                    {
                        bool here = pass == 0 ? g[b, a] : g[a, b];
                        bool before = pass == 0 ? g[b - 1, a] : g[a, b - 1];

                        if (here == before) run++;
                        else { if (run >= 5) score += run - 2; run = 1; }
                    }
                    if (run >= 5) score += run - 2;
                }
            }

            // Two-by-two blocks of one colour.
            for (int x = 0; x < size - 1; x++)
                for (int y = 0; y < size - 1; y++)
                    if (g[x, y] == g[x + 1, y] && g[x, y] == g[x, y + 1] && g[x, y] == g[x + 1, y + 1])
                        score += 3;

            // The finder-like sequence, which a reader could mistake for a corner.
            bool[] pattern = { true, false, true, true, true, false, true, false, false, false, false };
            for (int a = 0; a < size; a++)
            {
                for (int b = 0; b + 11 <= size; b++)
                {
                    bool across = true, down = true;
                    for (int i = 0; i < 11; i++)
                    {
                        if (g[b + i, a] != pattern[i]) across = false;
                        if (g[a, b + i] != pattern[i]) down = false;
                    }
                    if (across) score += 40;
                    if (down) score += 40;
                }
            }

            // Imbalance between light and dark.
            int total = size * size, darkCount = 0;
            foreach (bool m in g) if (m) darkCount++;
            score += Math.Abs(darkCount * 20 / total - 10) * 10;

            return score;
        }

        // ---- tables -------------------------------------------------------------------

        /// <summary>
        /// { ec per block, blocks in group 1, blocks in group 2 } for versions 1..40, one
        /// table per correction level. Straight from the standard; there is no formula.
        /// </summary>
        private static int[][,] BuildStructure()
        {
            int[][] raw =
            {
                // L                M                Q                H
                new[] {  7,1,0,    10,1,0,   13,1,0,   17,1,0 },   // 1
                new[] { 10,1,0,    16,1,0,   22,1,0,   28,1,0 },
                new[] { 15,1,0,    26,1,0,   18,2,0,   22,2,0 },
                new[] { 20,1,0,    18,2,0,   26,2,0,   16,4,0 },
                new[] { 26,1,0,    24,2,0,   18,2,2,   22,2,2 },
                new[] { 18,2,0,    16,4,0,   24,4,0,   28,4,0 },
                new[] { 20,2,0,    18,4,0,   18,2,4,   26,4,1 },
                new[] { 24,2,0,    22,2,2,   22,4,2,   26,4,2 },
                new[] { 30,2,0,    22,3,2,   20,4,4,   24,4,4 },
                new[] { 18,2,2,    26,4,1,   24,6,2,   28,6,2 },   // 10
                new[] { 20,4,0,    30,1,4,   28,4,4,   24,3,8 },
                new[] { 24,2,2,    22,6,2,   26,4,6,   28,7,4 },
                new[] { 26,4,0,    22,8,1,   24,8,4,   22,12,4 },
                new[] { 30,3,1,    24,4,5,   20,11,5,  24,11,5 },
                new[] { 22,5,1,    24,5,5,   30,5,7,   24,11,7 },
                new[] { 24,5,1,    28,7,3,   24,15,2,  30,3,13 },
                new[] { 28,1,5,    28,10,1,  28,1,15,  28,2,17 },
                new[] { 30,5,1,    26,9,4,   28,17,1,  28,2,19 },
                new[] { 28,3,4,    26,3,11,  26,17,4,  26,9,16 },
                new[] { 28,3,5,    26,3,13,  30,15,5,  28,15,10 },  // 20
                new[] { 28,4,4,    26,17,0,  28,17,6,  30,19,6 },
                new[] { 28,2,7,    28,17,0,  30,7,16,  24,34,0 },
                new[] { 30,4,5,    28,4,14,  30,11,14, 30,16,14 },
                new[] { 30,6,4,    28,6,14,  30,11,16, 30,30,2 },
                new[] { 26,8,4,    28,8,13,  30,7,22,  30,22,13 },
                new[] { 28,10,2,   28,19,4,  28,28,6,  30,33,4 },
                new[] { 30,8,4,    28,22,3,  30,8,26,  30,12,28 },
                new[] { 30,3,10,   28,3,23,  30,4,31,  30,11,31 },
                new[] { 30,7,7,    28,21,7,  30,1,37,  30,19,26 },
                new[] { 30,5,10,   28,19,10, 30,15,25, 30,23,25 },  // 30
                new[] { 30,13,3,   28,2,29,  30,42,1,  30,23,28 },
                new[] { 30,17,0,   28,10,23, 30,10,35, 30,19,35 },
                new[] { 30,17,1,   28,14,21, 30,29,19, 30,11,46 },
                new[] { 30,13,6,   28,14,23, 30,44,7,  30,59,1 },
                new[] { 30,12,7,   28,12,26, 30,39,14, 30,22,41 },
                new[] { 30,6,14,   28,6,34,  30,46,10, 30,2,64 },
                new[] { 30,17,4,   28,29,14, 30,49,10, 30,24,46 },
                new[] { 30,4,18,   28,13,32, 30,48,14, 30,42,32 },
                new[] { 30,20,4,   28,40,7,  30,43,22, 30,10,67 },
                new[] { 30,19,6,   28,18,31, 30,34,34, 30,20,61 }   // 40
            };

            var tables = new int[4][,];
            for (int level = 0; level < 4; level++)
            {
                tables[level] = new int[41, 3];
                for (int version = 1; version <= 40; version++)
                    for (int field = 0; field < 3; field++)
                        tables[level][version, field] = raw[version - 1][level * 3 + field];
            }

            return tables;
        }

        private sealed class BitBuffer
        {
            private readonly List<bool> _bits = new();

            public int Length => _bits.Count;

            public void Add(int value, int bits)
            {
                for (int i = bits - 1; i >= 0; i--)
                    _bits.Add((value >> i & 1) != 0);
            }

            public byte[] ToBytes()
            {
                var bytes = new byte[(_bits.Count + 7) / 8];
                for (int i = 0; i < _bits.Count; i++)
                    if (_bits[i])
                        bytes[i / 8] |= (byte)(1 << (7 - i % 8));
                return bytes;
            }
        }
    }
}
