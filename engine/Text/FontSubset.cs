namespace Frisket
{
    /// <summary>
    /// What a face becomes inside a PDF: the bytes, and what a reader has to be told about
    /// them.
    /// </summary>
    internal sealed class FontProgram
    {
        public required byte[] Data { get; init; }

        /// <summary>
        /// PostScript outlines: an OpenType file under /FontFile3, read by a CIDFontType0.
        /// ISO 32000-1 keeps /FontFile2 for TrueType outlines alone.
        /// </summary>
        public bool PostScript { get; init; }

        /// <summary>Only the glyphs the document drew, so the name carries a subset tag.</summary>
        public bool Partial { get; init; }

        /// <summary>
        /// The new id of every glyph of the file, 0 for one left out - or null when the
        /// ids are the file's own. The codes on the page stay the file's ids either way;
        /// this is what the CIDToGIDMap stream is written from.
        /// </summary>
        public ushort[]? Renumbered { get; init; }
    }

    /// <summary>
    /// Rebuilds a face carrying only what a section drew, in the shape ISO 32000-1 allows.
    ///
    /// A whole face is most of an invoice: sixty kilobytes of font against twelve of
    /// section, for thirty-seven glyphs out of six hundred. Three things go, in order of how
    /// much they pay: the tables a PDF never consults - kerning, ligatures, glyph names -
    /// the outlines of everything unused, and the numbering of the glyphs that are left.
    ///
    /// Renumbering is what a large face pays for. A full-length loca and hmtx hold an entry
    /// for every glyph the face has, drawn or not: Arial has 4,652, and its regular and
    /// bold cost 15 KB of numbering for seventy characters. The codes on the page keep the
    /// file's ids and a CIDToGIDMap stream turns them into the new ones, so the widths
    /// array, ToUnicode and every page stay exactly as they were written; the map is a few
    /// hundred bytes. A comment here used to say renumbering would save "a few hundred
    /// bytes" and put four mechanisms at risk - true of the faces of the time, and of
    /// neither half once a system font came in.
    ///
    /// What must not go is set by ISO 32000-1 9.9: head, hhea, loca, maxp, cvt, prep, glyf,
    /// hmtx and fpgm "shall always be present if present in the original". PDF/A-3 and
    /// PDF/UA-1 both embed "as defined in" that clause, and veraPDF does not check it - so
    /// a face without its cvt, fpgm and prep validates and does not conform. Those three are
    /// a face's hinting tables, which is why <see cref="Prepare"/> can leave out the hinting
    /// each glyph carries and never theirs.
    /// </summary>
    internal static class FontSubset
    {
        /// <summary>
        /// The tables a subset keeps: the nine of ISO 32000-1 9.9, and nothing else. A cmap
        /// in particular "shall not be present" with a CIDFont, whose codes reach the
        /// glyphs another way.
        /// </summary>
        private static readonly string[] Keep =
            { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "cvt ", "fpgm", "prep" };

        /// <summary>
        /// What an OpenType face with PostScript outlines keeps: the tables the format
        /// requires. Kerning and the layout tables go - this engine positions every glyph
        /// itself - and with them more than half of a face like Glametrix, whose kern
        /// table alone is 102 KB. The glyphs all stay: cutting a CFF table means rewriting
        /// its indexes and subroutines, which this writer does not do.
        /// </summary>
        private static readonly string[] KeepPostScript =
            { "CFF ", "OS/2", "cmap", "head", "hhea", "hmtx", "maxp", "name", "post" };

        /// <summary>
        /// The face as a PDF should carry it.
        ///
        /// <paramref name="subset"/> off embeds every glyph and every table, less the two a
        /// PDF must not or cannot hold: a TrueType cmap, which ISO 32000-1 forbids with a
        /// CIDFont, and a digital signature, which signs bytes that are no longer the file.
        /// <paramref name="hinting"/> off removes the instructions inside each glyph of a
        /// TrueType subset; see <see cref="PdfBackend.Hinting"/>.
        ///
        /// A face that cannot be read safely is embedded as it came: better whole than wrong.
        /// </summary>
        public static FontProgram Prepare(byte[] data, IEnumerable<ushort> glyphs, bool subset, bool hinting)
        {
            Dictionary<string, (int Offset, int Length)> tables;
            try
            {
                tables = ReadDirectory(data);
            }
            catch (Exception e) when (Refused(e))
            {
                return new FontProgram { Data = data };
            }

            if (tables.ContainsKey("CFF "))
            {
                return new FontProgram
                {
                    Data = subset
                        ? Rebuild(data, tables, tag => KeepPostScript.Contains(tag))
                        : Rebuild(data, tables, tag => tag != "DSIG"),
                    PostScript = true
                };
            }

            // Neither outline this writer knows - CFF2, bitmaps alone: as it came.
            if (!tables.ContainsKey("glyf"))
                return new FontProgram { Data = data };

            if (subset)
            {
                try
                {
                    return Cut(data, tables, glyphs, hinting);
                }
                catch (Exception e) when (Refused(e))
                {
                    // Embedded whole below, numbering untouched.
                }
            }

            return new FontProgram { Data = Rebuild(data, tables, tag => tag is not ("cmap" or "DSIG")) };
        }

        /// <summary>
        /// Whether the face has PostScript outlines keyed by CID, which <see cref="Prepare"/>
        /// cannot embed: its codes would have to be CIDs read from its charset.
        /// </summary>
        public static bool IsCidKeyed(byte[] data)
        {
            try
            {
                return ReadDirectory(data).TryGetValue("CFF ", out var cff)
                       && CffOutlines.IsCidKeyed(data, cff.Offset);
            }
            catch (Exception e) when (Refused(e))
            {
                return false;
            }
        }

        private static bool Refused(Exception e)
            => e is IndexOutOfRangeException or ArgumentException or InvalidDataException;

        /// <summary>The same face with only the tables <paramref name="keep"/> accepts, or the bytes as they came.</summary>
        private static byte[] Rebuild(byte[] d, Dictionary<string, (int Offset, int Length)> tables, Func<string, bool> keep)
        {
            try
            {
                return Assemble(tables.Where(t => keep(t.Key))
                                      .ToDictionary(t => t.Key, t => Slice(d, t.Value.Offset, t.Value.Length)));
            }
            catch (Exception e) when (Refused(e))
            {
                return d;
            }
        }

        private static FontProgram Cut(byte[] d, Dictionary<string, (int Offset, int Length)> tables,
                                       IEnumerable<ushort> glyphs, bool hinting)
        {
            foreach (string required in new[] { "head", "hhea", "hmtx", "loca", "maxp" })
                if (!tables.ContainsKey(required))
                    throw new InvalidDataException($"no {required} table");

            int head = tables["head"].Offset;
            int hhea = tables["hhea"].Offset;
            int hmtx = tables["hmtx"].Offset;
            int indexToLoc = Int16(d, head + 50);
            int glyphCount = UInt16(d, tables["maxp"].Offset + 4);
            int longMetrics = UInt16(d, hhea + 34);
            if (longMetrics == 0)
                throw new InvalidDataException("no horizontal metrics");

            int[] loca = ReadLoca(d, tables["loca"], indexToLoc, glyphCount);
            int glyfAt = tables["glyf"].Offset;

            // Glyph 0 is .notdef and must survive, as glyph 0: it is what a reader draws for
            // anything it cannot find, and PDF/A forbids drawing it - so no code the page
            // uses may ever be mapped onto it.
            var wanted = new SortedSet<ushort> { 0 };
            foreach (ushort g in glyphs)
                if (g < glyphCount)
                    wanted.Add(g);

            // A composite glyph - e acute, say - is only the instruction to place other
            // glyphs. Keeping it without its components draws nothing at all, and the
            // gap shows on accented text alone, which is how this is missed.
            Close(d, glyfAt, loca, wanted, glyphCount);

            // In the file's order, so the new ids follow the old ones and glyph 0 stays 0.
            ushort[] kept = wanted.ToArray();
            var renumbered = new ushort[glyphCount];
            for (int i = 0; i < kept.Length; i++)
                renumbered[kept[i]] = (ushort)i;

            var glyf = new MemoryStream();
            var newLoca = new int[kept.Length + 1];
            var metrics = new byte[kept.Length * 4];

            for (int i = 0; i < kept.Length; i++)
            {
                int g = kept[i];
                newLoca[i] = (int)glyf.Length;

                if (loca[g + 1] > loca[g])
                {
                    byte[] outline = Rewrite(Slice(d, glyfAt + loca[g], loca[g + 1] - loca[g]),
                                             renumbered, glyphCount, hinting);
                    glyf.Write(outline, 0, outline.Length);
                    while (glyf.Length % 4 != 0)
                        glyf.WriteByte(0);
                }

                // hmtx holds an advance and a side bearing for the first glyphs, and only a
                // side bearing for the rest, which share the last advance. A subset writes
                // both for every glyph: there are few, and one shape is one less to get wrong.
                int advance = UInt16(d, hmtx + Math.Min(g, longMetrics - 1) * 4);
                int bearing = g < longMetrics
                    ? Int16(d, hmtx + g * 4 + 2)
                    : Int16(d, hmtx + longMetrics * 4 + (g - longMetrics) * 2);
                WriteUInt16(metrics, i * 4, (ushort)advance);
                WriteUInt16(metrics, i * 4 + 2, unchecked((ushort)bearing));
            }
            newLoca[kept.Length] = (int)glyf.Length;

            // Long offsets throughout: the short form stores halved values and cannot
            // express an odd length, and the saving is a few hundred bytes at most.
            var locaBytes = new byte[(kept.Length + 1) * 4];
            for (int i = 0; i <= kept.Length; i++)
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
                    "hmtx" => metrics,
                    _ => Slice(d, slice.Offset, slice.Length)
                };
            }

            // The counts that follow from the cut. head records which loca form was used;
            // it now says long whatever it said. The rest of maxp is maxima, which a smaller
            // set of glyphs still respects.
            WriteUInt16(built["head"], 50, 1);
            WriteUInt16(built["hhea"], 34, (ushort)kept.Length);
            WriteUInt16(built["maxp"], 4, (ushort)kept.Length);

            return new FontProgram { Data = Assemble(built), Partial = true, Renumbered = renumbered };
        }

        /// <summary>
        /// One outline as the subset writes it: a composite's components point at their new
        /// ids, and without <paramref name="hinting"/> the glyph's own instructions are gone.
        /// The points and contours are never touched.
        /// </summary>
        private static byte[] Rewrite(byte[] outline, ushort[] renumbered, int glyphCount, bool hinting)
        {
            int contours = Int16(outline, 0);

            if (contours >= 0)
            {
                if (hinting)
                    return outline;

                int at = 10 + 2 * contours;
                int length = UInt16(outline, at);
                if (at + 2 + length > outline.Length)
                    throw new InvalidDataException("instructions run past the glyph");

                var plain = new byte[outline.Length - length];
                Array.Copy(outline, 0, plain, 0, at);                      // header and contour ends
                Array.Copy(outline, at + 2 + length, plain, at + 2, outline.Length - at - 2 - length);
                return plain;                                              // a length of zero, then the points
            }

            var copy = (byte[])outline.Clone();
            int p = 10;
            while (true)
            {
                int flags = UInt16(copy, p);
                int component = UInt16(copy, p + 2);
                if (component >= glyphCount)
                    throw new InvalidDataException("a component that is not in the font");

                WriteUInt16(copy, p + 2, renumbered[component]);
                if (!hinting)
                    WriteUInt16(copy, p, (ushort)(flags & ~0x100));      // WE_HAVE_INSTRUCTIONS

                p += 4;
                p += (flags & 1) != 0 ? 4 : 2;             // ARG_1_AND_2_ARE_WORDS
                if ((flags & 8) != 0) p += 2;              // WE_HAVE_A_SCALE
                else if ((flags & 0x40) != 0) p += 4;      // X_AND_Y_SCALE
                else if ((flags & 0x80) != 0) p += 8;      // TWO_BY_TWO

                if ((flags & 0x20) == 0)                   // MORE_COMPONENTS
                    break;
            }

            // The composite's own instructions follow its last component.
            return hinting ? copy : copy[..p];
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
            // recompute them, but a wrong value trips the strict ones. An OpenType file with
            // PostScript outlines says so in its first four bytes.
            int entrySelector = (int)Math.Floor(Math.Log2(tags.Length));
            int searchRange = 16 * (1 << entrySelector);

            writer.UInt32(tables.ContainsKey("CFF ") ? 0x4F54544Fu : 0x00010000u);
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
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] != "head" || tables["head"].Length < 12)
                    continue;

                int headAt = (int)ReadUInt32(result, 12 + i * 16 + 8);
                WriteUInt32(result, headAt + 8, 0);
                WriteUInt32(result, headAt + 8, unchecked(0xB1B0AFBAu - CheckSum(result)));
            }

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

        /// <summary>
        /// A table's bytes. Its offset and length come from the file, so they are held to
        /// the file before anything is allocated: a length near int.MaxValue would
        /// otherwise ask for two gigabytes on behalf of a thirty-kilobyte font (section 8c).
        /// </summary>
        private static byte[] Slice(byte[] d, int at, int length)
        {
            if (at < 0 || length < 0 || at > d.Length - length)
                throw new InvalidDataException("a table that does not fit the file");

            var copy = new byte[length];
            Array.Copy(d, at, copy, 0, length);
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
