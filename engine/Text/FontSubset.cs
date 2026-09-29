namespace Frisket
{
    /// <summary>
    /// Rebuilds a TrueType file carrying only the glyphs a section actually drew.
    ///
    /// A whole face is most of an invoice: sixty kilobytes of font against twelve of
    /// section, for thirty-seven glyphs out of six hundred. Two things go, in order of
    /// how much they pay: the tables a PDF never consults - kerning, ligatures, glyph
    /// names - and then the outlines of everything unused.
    ///
    /// Glyph numbers are left exactly as they were. The subset keeps a full-length loca
    /// table in which unused entries have zero length, so every glyph id the rest of the
    /// writer already computed - the widths array, ToUnicode, CIDToGIDMap Identity -
    /// stays correct without a single change. Renumbering compactly would save a few
    /// hundred bytes more and put four mechanisms at risk at once.
    /// </summary>
    internal static class FontSubset
    {
        /// <summary>
        /// Tables worth keeping. Everything else is either derivable, or something a
        /// PDF viewer never reads because this engine positions the text itself.
        /// </summary>
        private static readonly string[] Keep =
            { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "cvt ", "fpgm", "prep" };

        /// <summary>
        /// A font holding <paramref name="glyphs"/> and nothing else, or the original
        /// bytes when it cannot safely be cut down - a face with PostScript outlines has
        /// no glyf table to trim, and a malformed one is better embedded whole than
        /// embedded wrong.
        /// </summary>
        public static byte[] Build(byte[] data, IEnumerable<ushort> glyphs)
        {
            try
            {
                return Subset(data, glyphs);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
            {
                return data;
            }
        }

        private static byte[] Subset(byte[] d, IEnumerable<ushort> glyphs)
        {
            Dictionary<string, (int Offset, int Length)> tables = ReadDirectory(d);
            if (!tables.ContainsKey("glyf") || !tables.ContainsKey("loca") || !tables.ContainsKey("head"))
                return d;

            int head = tables["head"].Offset;
            int indexToLoc = Int16(d, head + 50);
            int glyphCount = UInt16(d, tables["maxp"].Offset + 4);

            int[] loca = ReadLoca(d, tables["loca"], indexToLoc, glyphCount);

            // Glyph 0 is .notdef and must survive: it is what a reader draws for anything
            // it cannot find, and a font without it is rejected outright.
            var wanted = new SortedSet<ushort> { 0 };
            foreach (ushort g in glyphs)
                if (g < glyphCount)
                    wanted.Add(g);

            // A composite glyph - e acute, say - is only the instruction to place other
            // glyphs. Keeping it without its components draws nothing at all, and the
            // gap shows on accented text alone, which is how this is missed.
            Close(d, tables["glyf"].Offset, loca, wanted, glyphCount);

            // Outlines, in glyph order, each padded to a four-byte boundary.
            var glyf = new MemoryStream();
            var newLoca = new int[glyphCount + 1];

            for (int g = 0; g < glyphCount; g++)
            {
                newLoca[g] = (int)glyf.Length;

                if (wanted.Contains((ushort)g) && loca[g + 1] > loca[g])
                {
                    glyf.Write(d, tables["glyf"].Offset + loca[g], loca[g + 1] - loca[g]);
                    while (glyf.Length % 4 != 0)
                        glyf.WriteByte(0);
                }
            }
            newLoca[glyphCount] = (int)glyf.Length;

            // Long offsets throughout: the short form stores halved values and cannot
            // express an odd length, and the saving is a few hundred bytes at most.
            var locaBytes = new byte[(glyphCount + 1) * 4];
            for (int i = 0; i <= glyphCount; i++)
                WriteUInt32(locaBytes, i * 4, (uint)newLoca[i]);

            var built = new Dictionary<string, byte[]>();
            foreach (string tag in Keep)
            {
                if (!tables.TryGetValue(tag, out var slice))
                    continue;

                built[tag] = tag switch
                {
                    "glyf" => glyf.ToArray(),
                    "loca" => locaBytes,
                    _ => Slice(d, slice.Offset, slice.Length)
                };
            }

            // head records which loca form was used; it now says long whatever it said.
            WriteUInt16(built["head"], 50, 1);

            return Assemble(built);
        }

        /// <summary>Adds every glyph the wanted ones are built from, however deep.</summary>
        private static void Close(byte[] d, int glyf, int[] loca, SortedSet<ushort> wanted, int glyphCount)
        {
            var pending = new Queue<ushort>(wanted);

            while (pending.Count > 0)
            {
                ushort g = pending.Dequeue();
                if (g + 1 >= loca.Length || loca[g + 1] <= loca[g])
                    continue;

                int at = glyf + loca[g];
                if (Int16(d, at) >= 0)         // positive contour count: a simple outline
                    continue;

                int p = at + 10;
                while (true)
                {
                    int flags = UInt16(d, p);
                    ushort component = (ushort)UInt16(d, p + 2);

                    if (component < glyphCount && wanted.Add(component))
                        pending.Enqueue(component);

                    p += 4;
                    p += (flags & 1) != 0 ? 4 : 2;             // ARG_1_AND_2_ARE_WORDS
                    if ((flags & 8) != 0) p += 2;              // WE_HAVE_A_SCALE
                    else if ((flags & 0x40) != 0) p += 4;      // X_AND_Y_SCALE
                    else if ((flags & 0x80) != 0) p += 8;      // TWO_BY_TWO

                    if ((flags & 0x20) == 0)                   // MORE_COMPONENTS
                        break;
                }
            }
        }

        private static byte[] Assemble(Dictionary<string, byte[]> tables)
        {
            string[] tags = tables.Keys.OrderBy(t => t, StringComparer.Ordinal).ToArray();
            int directory = 12 + tags.Length * 16;
            int offset = directory;

            var file = new MemoryStream();
            var writer = new BinaryWriterBE(file);

            // sfnt header. The search fields are derived from the table count; readers
            // recompute them, but a wrong value trips the strict ones.
            int entrySelector = (int)Math.Floor(Math.Log2(tags.Length));
            int searchRange = 16 * (1 << entrySelector);

            writer.UInt32(0x00010000);
            writer.UInt16((ushort)tags.Length);
            writer.UInt16((ushort)searchRange);
            writer.UInt16((ushort)entrySelector);
            writer.UInt16((ushort)(tags.Length * 16 - searchRange));

            foreach (string tag in tags)
            {
                byte[] body = tables[tag];
                writer.Tag(tag);
                writer.UInt32(CheckSum(body));
                writer.UInt32((uint)offset);
                writer.UInt32((uint)body.Length);
                offset += (body.Length + 3) & ~3;
            }

            foreach (string tag in tags)
            {
                byte[] body = tables[tag];
                file.Write(body, 0, body.Length);
                while (file.Length % 4 != 0)
                    file.WriteByte(0);
            }

            byte[] result = file.ToArray();

            // head carries a checksum over the whole file, computed with its own field
            // zeroed. Left wrong, some readers reject the font outright.
            int headAt = 12;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == "head")
                {
                    headAt = (int)ReadUInt32(result, 12 + i * 16 + 8);
                    break;
                }
            }

            WriteUInt32(result, headAt + 8, 0);
            WriteUInt32(result, headAt + 8, unchecked(0xB1B0AFBAu - CheckSum(result)));
            return result;
        }

        // ---- bytes -----------------------------------------------------------------

        private static Dictionary<string, (int Offset, int Length)> ReadDirectory(byte[] d)
        {
            int start = d.Length >= 4 && d[0] == 't' && d[1] == 't' && d[2] == 'c' && d[3] == 'f'
                ? (int)ReadUInt32(d, 12)
                : 0;

            var tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            int count = UInt16(d, start + 4);

            for (int i = 0; i < count; i++)
            {
                int r = start + 12 + i * 16;
                string tag = new(new[] { (char)d[r], (char)d[r + 1], (char)d[r + 2], (char)d[r + 3] });
                tables[tag] = ((int)ReadUInt32(d, r + 8), (int)ReadUInt32(d, r + 12));
            }

            return tables;
        }

        private static int[] ReadLoca(byte[] d, (int Offset, int Length) loca, int format, int glyphCount)
        {
            var offsets = new int[glyphCount + 1];
            for (int i = 0; i <= glyphCount; i++)
            {
                offsets[i] = format == 0
                    ? UInt16(d, loca.Offset + i * 2) * 2
                    : (int)ReadUInt32(d, loca.Offset + i * 4);
            }
            return offsets;
        }

        private static uint CheckSum(byte[] table)
        {
            uint sum = 0;
            for (int i = 0; i + 3 < table.Length; i += 4)
                sum = unchecked(sum + ReadUInt32(table, i));

            // A trailing partial word counts as if padded with zeros.
            int rest = table.Length & ~3;
            if (rest < table.Length)
            {
                uint tail = 0;
                for (int i = 0; i < 4; i++)
                    tail = (tail << 8) | (rest + i < table.Length ? table[rest + i] : (byte)0);
                sum = unchecked(sum + tail);
            }

            return sum;
        }

        private static byte[] Slice(byte[] d, int at, int length)
        {
            var copy = new byte[length];
            Array.Copy(d, at, copy, 0, Math.Min(length, d.Length - at));
            return copy;
        }

        private static int UInt16(byte[] d, int at) => (d[at] << 8) | d[at + 1];
        private static int Int16(byte[] d, int at) => unchecked((short)UInt16(d, at));

        private static uint ReadUInt32(byte[] d, int at)
            => ((uint)d[at] << 24) | ((uint)d[at + 1] << 16) | ((uint)d[at + 2] << 8) | d[at + 3];

        private static void WriteUInt16(byte[] d, int at, ushort v)
        {
            d[at] = (byte)(v >> 8);
            d[at + 1] = (byte)v;
        }

        private static void WriteUInt32(byte[] d, int at, uint v)
        {
            d[at] = (byte)(v >> 24);
            d[at + 1] = (byte)(v >> 16);
            d[at + 2] = (byte)(v >> 8);
            d[at + 3] = (byte)v;
        }

        private sealed class BinaryWriterBE
        {
            private readonly Stream _to;
            public BinaryWriterBE(Stream to) => _to = to;

            public void UInt16(ushort v) { _to.WriteByte((byte)(v >> 8)); _to.WriteByte((byte)v); }

            public void UInt32(uint v)
            {
                _to.WriteByte((byte)(v >> 24)); _to.WriteByte((byte)(v >> 16));
                _to.WriteByte((byte)(v >> 8)); _to.WriteByte((byte)v);
            }

            public void Tag(string t)
            {
                foreach (char c in t)
                    _to.WriteByte((byte)c);
            }
        }
    }
}
