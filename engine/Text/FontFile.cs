namespace Frisket
{
    /// <summary>
    /// Metrics read straight out of a TrueType or OpenType file.
    ///
    /// Only the metric tables are parsed - head, hhea, hmtx, cmap, OS/2, name - which
    /// are identical in both formats; the outlines differ (glyf against CFF) and are of
    /// no use for measuring. That keeps the engine free of any font library.
    ///
    /// Every ratio is expressed as a fraction of the em, so a size in millimeters is a
    /// single multiplication away.
    /// </summary>
    public sealed class FontFile
    {
        private readonly ushort[] _advances;        // in font units, per glyph
        private readonly Dictionary<int, ushort> _cmap;
        private readonly ushort _defaultAdvance;

        /// <summary>The file's bytes, kept so a backend can embed the font it measured with.</summary>
        public byte[] Data { get; }

        public string Family { get; }
        public string Subfamily { get; }

        /// <summary>Where it was read from, or "&lt;memory&gt;". Kept because two files can
        /// come out with the same family and cut, and then the path is the only thing
        /// that tells them apart.</summary>
        public string Source { get; }
        public bool Bold { get; }
        public bool Italic { get; }

        /// <summary>
        /// The <c>fsType</c> bits from OS/2 - what the foundry says may be done with
        /// this file. Read and reported, never enforced.
        ///
        /// Enforcing it is not this library's call to make, and the clearest reason is
        /// the simplest: these bits say what the *foundry* permits, and the caller may
        /// well be the foundry. Refusing a font on the strength of them would be telling
        /// someone they may not use a typeface they drew themselves.
        ///
        /// More generally, whose font it is and what the licence behind it says is known
        /// to the caller and not here; a renderer refusing on two bits is guessing at a
        /// contract it has never seen. What the project owes is not to *ship* a font it
        /// has no right to, which is a different promise and one it keeps elsewhere.
        ///
        /// So it is surfaced instead: a caller who has to answer for embedding can read
        /// these and decide, and a caller who does not is not stopped. Zero means the
        /// font declares no restriction at all, which is much the commonest answer.
        /// </summary>
        public int EmbeddingRights { get; }

        /// <summary>Bit 1: the foundry says this font may not be embedded at all.</summary>
        public bool EmbeddingRestricted => (EmbeddingRights & 0x0002) != 0;

        /// <summary>
        /// Bit 8: the font may be embedded whole but not subsetted - which this engine
        /// always does, so for such a font the two are the same refusal.
        /// </summary>
        public bool SubsettingForbidden => (EmbeddingRights & 0x0100) != 0;

        public int UnitsPerEm { get; }

        /// <summary>Ascender as a fraction of the em.</summary>
        public double Ascender { get; }

        /// <summary>Descender as a fraction of the em, positive downwards.</summary>
        public double Descender { get; }

        /// <summary>Baseline-to-baseline distance as a fraction of the em.</summary>
        public double LineHeight { get; }

        /// <summary>Height of a capital as a fraction of the em. This is what "3mm of text" pins.</summary>
        public double CapHeight { get; }

        /// <summary>Height of a lowercase x as a fraction of the em.</summary>
        public double XHeight { get; }

        /// <summary>
        /// True when the cap height could not be measured from an outline - a CFF font,
        /// where the declaration has to be taken on trust, or no H at all.
        /// </summary>
        public bool CapHeightIsEstimated { get; }

        /// <summary>Glyph bounding box of the whole font, as fractions of the em.</summary>
        public double XMin { get; private set; }
        public double YMin { get; private set; }
        public double XMax { get; private set; }
        public double YMax { get; private set; }

        /// <summary>
        /// Glyph index of a code point, 0 - .notdef - when the font has nothing for it.
        ///
        /// Unicode has a dozen spaces and most fonts carry only the plain one, so a
        /// missing space falls back to it rather than to a blank box. That is what the
        /// character means anyway, and it keeps the measurement honest.
        /// </summary>
        public ushort GlyphOf(int codePoint)
        {
            if (_cmap.TryGetValue(codePoint, out ushort glyph))
                return glyph;

            if (codePoint <= 0xFFFF && char.IsWhiteSpace((char)codePoint)
                && _cmap.TryGetValue(' ', out ushort space))
                return space;

            return 0;
        }

        /// <summary>Advance of a glyph, as a fraction of the em.</summary>
        public double AdvanceOfGlyph(ushort glyph)
            => (double)(glyph < _advances.Length ? _advances[glyph] : _defaultAdvance) / UnitsPerEm;

        private FontFile(byte[] data, string origin, string family, string subfamily, bool bold, bool italic, int unitsPerEm,
                         double ascender, double descender, double lineHeight,
                         double capHeight, double xHeight, bool capEstimated,
                         int embeddingRights, ushort[] advances, Dictionary<int, ushort> cmap)
        {
            Data = data;
            Source = origin;
            Family = family;
            Subfamily = subfamily;
            Bold = bold;
            Italic = italic;
            UnitsPerEm = unitsPerEm;
            Ascender = ascender;
            Descender = descender;
            LineHeight = lineHeight;
            CapHeight = capHeight;
            XHeight = xHeight;
            CapHeightIsEstimated = capEstimated;
            EmbeddingRights = embeddingRights;
            _advances = advances;
            _cmap = cmap;
            _defaultAdvance = advances.Length > 0 ? advances[^1] : (ushort)unitsPerEm;
        }

        public static FontFile Load(string path) => Parse(File.ReadAllBytes(path), path);

        public static FontFile Load(byte[] data) => Parse(data, "<memory>");

        /// <summary>Advance width of a code point, as a fraction of the em.</summary>
        public double AdvanceOf(int codePoint)
        {
            ushort glyph = _cmap.TryGetValue(codePoint, out ushort g) ? g : (ushort)0;
            ushort units = glyph < _advances.Length ? _advances[glyph] : _defaultAdvance;
            return (double)units / UnitsPerEm;
        }

        public bool HasGlyph(int codePoint) => _cmap.ContainsKey(codePoint);

        public override string ToString()
            => $"{Family} {Subfamily} ({UnitsPerEm}upem, cap {CapHeight:0.###}{(CapHeightIsEstimated ? " est." : "")})";

        // ------------------------------------------------------------------

        private static FontFile Parse(byte[] data, string origin)
        {
            var r = new BigEndianReader(data);

            uint version = r.UInt32(0);
            int tableStart = 12;

            // A .ttc collection starts with 'ttcf'; take its first font.
            if (version == 0x74746366)
            {
                uint first = r.UInt32(12);
                version = r.UInt32((int)first);
                tableStart = (int)first + 12;
            }

            if (version != 0x00010000 && version != 0x4F54544F && version != 0x74727565)
                throw new InvalidDataException($"{origin}: not a TrueType or OpenType font (0x{version:X8}).");

            int numTables = r.UInt16(tableStart - 8);
            var tables = new Dictionary<string, (int Offset, int Length)>(numTables);

            for (int i = 0; i < numTables; i++)
            {
                int rec = tableStart + i * 16;
                string tag = r.Tag(rec);
                tables[tag] = ((int)r.UInt32(rec + 8), (int)r.UInt32(rec + 12));
            }

            if (!tables.TryGetValue("head", out var head))
                throw new InvalidDataException($"{origin}: no head table.");
            if (!tables.TryGetValue("hhea", out var hhea))
                throw new InvalidDataException($"{origin}: no hhea table.");
            if (!tables.TryGetValue("hmtx", out var hmtx))
                throw new InvalidDataException($"{origin}: no hmtx table.");

            int unitsPerEm = r.UInt16(head.Offset + 18);
            if (unitsPerEm == 0)
                unitsPerEm = 1000;

            int macStyle = r.UInt16(head.Offset + 44);
            int indexToLocFormat = r.Int16(head.Offset + 50);

            double ascender = (double)r.Int16(hhea.Offset + 4) / unitsPerEm;
            double descender = -(double)r.Int16(hhea.Offset + 6) / unitsPerEm;
            double lineGap = (double)r.Int16(hhea.Offset + 8) / unitsPerEm;
            int numberOfHMetrics = r.UInt16(hhea.Offset + 34);

            int glyphCount = tables.TryGetValue("maxp", out var maxp) ? r.UInt16(maxp.Offset + 4) : numberOfHMetrics;
            var advances = ReadAdvances(r, hmtx.Offset, numberOfHMetrics, glyphCount);

            Dictionary<int, ushort> cmap = tables.TryGetValue("cmap", out var cm)
                ? ReadCmap(r, cm.Offset)
                : new Dictionary<int, ushort>();

            // OS/2 version 2 and later declare a cap height and an x-height.
            double declaredCap = 0;
            double declaredX = 0;
            int embeddingRights = 0;
            bool bold = (macStyle & 1) != 0;
            bool italic = (macStyle & 2) != 0;

            if (tables.TryGetValue("OS/2", out var os2) && os2.Length >= 78)
            {
                int os2Version = r.UInt16(os2.Offset);
                embeddingRights = r.UInt16(os2.Offset + 8);      // fsType
                ushort selection = r.UInt16(os2.Offset + 62);
                bold |= (selection & 0x20) != 0;
                italic |= (selection & 0x01) != 0;

                if (os2Version >= 2 && os2.Length >= 90)
                {
                    declaredX = (double)r.Int16(os2.Offset + 86) / unitsPerEm;
                    declaredCap = (double)r.Int16(os2.Offset + 88) / unitsPerEm;
                }
            }

            // The glyph wins over the declaration. Fonts get this wrong: one of the faces
            // shipped here declares 0.940 while its H actually measures 0.732, so trusting
            // the number printed capitals a fifth too small. What the reader sees is the
            // outline, so that is what a size in millimetres has to be pinned to.
            double measuredCap = GlyphTop(r, tables, cmap, indexToLocFormat, 'H', unitsPerEm);
            double measuredX = GlyphTop(r, tables, cmap, indexToLocFormat, 'x', unitsPerEm);

            // PostScript outlines have no bounding box to read, so the charstring itself
            // is walked. Worth the trouble: one of the faces here declares 0.640 and draws
            // 0.730, and a face nobody can measure is a face that quietly sets its own size.
            if (measuredCap <= 0 && tables.TryGetValue("CFF ", out var cff))
            {
                cmap.TryGetValue('H', out ushort capGlyph);
                cmap.TryGetValue('x', out ushort xGlyph);
                measuredCap = CffOutlines.GlyphTop(data, cff.Offset, capGlyph, unitsPerEm);
                measuredX = CffOutlines.GlyphTop(data, cff.Offset, xGlyph, unitsPerEm);
            }

            double capHeight = measuredCap > 0 ? measuredCap : declaredCap;
            double xHeight = measuredX > 0 ? measuredX : declaredX;

            // Only a face whose outline could not be read at all falls back on its word.
            bool capEstimated = measuredCap <= 0;

            if (capHeight <= 0)
            {
                capHeight = 0.70;
                capEstimated = true;
            }

            if (xHeight <= 0)
                xHeight = capHeight * 0.72;

            (string family, string subfamily) = tables.TryGetValue("name", out var name)
                ? ReadNames(r, name.Offset)
                : ("Unknown", "Regular");

            (family, bold, italic) = ApplySubfamily(family, subfamily, bold, italic);

            double lineHeight = ascender + descender + lineGap;
            if (lineHeight <= 0)
                lineHeight = 1.2;

            var font = new FontFile(data, origin, family, subfamily, bold, italic, unitsPerEm,
                                    ascender, descender, lineHeight, capHeight, xHeight, capEstimated,
                                    embeddingRights, advances, cmap);

            font.XMin = (double)r.Int16(head.Offset + 36) / unitsPerEm;
            font.YMin = (double)r.Int16(head.Offset + 38) / unitsPerEm;
            font.XMax = (double)r.Int16(head.Offset + 40) / unitsPerEm;
            font.YMax = (double)r.Int16(head.Offset + 42) / unitsPerEm;
            return font;
        }

        /// <summary>
        /// How many records of <paramref name="size"/> bytes still fit after
        /// <paramref name="at"/>. The ceiling for any count the file declares about
        /// itself, and the reason a malformed font costs a malformed font's worth of
        /// work rather than whatever it asked for.
        /// </summary>
        private static int Room(BigEndianReader r, int at, int size)
            => at < 0 || at >= r.Length ? 0 : (r.Length - at) / size;

        private static ushort[] ReadAdvances(BigEndianReader r, int offset, int numberOfHMetrics, int glyphCount)
        {
            int count = Math.Max(1, Math.Max(numberOfHMetrics, glyphCount));
            var advances = new ushort[count];
            ushort last = 0;

            for (int i = 0; i < count; i++)
            {
                if (i < numberOfHMetrics)
                {
                    last = r.UInt16(offset + i * 4);
                }
                advances[i] = last;
            }

            return advances;
        }

        /// <summary>Reads the top of a glyph's bounding box, used when OS/2 has no cap height.</summary>
        private static double GlyphTop(BigEndianReader r, Dictionary<string, (int Offset, int Length)> tables,
                                       Dictionary<int, ushort> cmap, int locFormat, char c, int unitsPerEm)
        {
            if (!cmap.TryGetValue(c, out ushort glyph) || glyph == 0)
                return 0;
            if (!tables.TryGetValue("loca", out var loca) || !tables.TryGetValue("glyf", out var glyf))
                return 0;   // CFF outlines: no glyf table to look into

            int start, end;
            if (locFormat == 0)
            {
                if (loca.Offset + glyph * 2 + 4 > r.Length)
                    return 0;
                start = r.UInt16(loca.Offset + glyph * 2) * 2;
                end = r.UInt16(loca.Offset + glyph * 2 + 2) * 2;
            }
            else
            {
                if (loca.Offset + glyph * 4 + 8 > r.Length)
                    return 0;
                start = (int)r.UInt32(loca.Offset + glyph * 4);
                end = (int)r.UInt32(loca.Offset + glyph * 4 + 4);
            }

            if (end <= start || glyf.Offset + start + 10 > r.Length)
                return 0;

            return (double)r.Int16(glyf.Offset + start + 8) / unitsPerEm;   // yMax
        }

        /// <summary>
        /// Reads the character map.
        ///
        /// Every count here comes out of the font, which is a file somebody else wrote.
        /// The reader below is deliberately forgiving - past the end it returns zero
        /// rather than throwing - and that generosity is what makes an unbounded count
        /// dangerous rather than merely wrong: the loop never runs out of bytes to read,
        /// so it runs exactly as many times as the file asked for. A format 12 subtable
        /// declaring two billion groups is two hundred bytes on disk and seconds of
        /// work, and it is accepted at the end of them.
        ///
        /// So each count is held to what the file could actually contain. The bound is
        /// arithmetic on the length, never a number picked to look generous.
        /// </summary>
        private static Dictionary<int, ushort> ReadCmap(BigEndianReader r, int offset)
        {
            // Eight bytes per record, and they have to be inside the file.
            int tableCount = Math.Min(r.UInt16(offset + 2), Room(r, offset + 4, 8));
            int best = -1;
            int bestScore = -1;

            for (int i = 0; i < tableCount; i++)
            {
                int rec = offset + 4 + i * 8;
                int platform = r.UInt16(rec);
                int encoding = r.UInt16(rec + 2);
                int sub = offset + (int)r.UInt32(rec + 4);

                // Prefer full Unicode, then BMP Unicode, then anything.
                int score = (platform, encoding) switch
                {
                    (3, 10) => 4,
                    (0, 4) or (0, 6) => 4,
                    (3, 1) => 3,
                    (0, _) => 2,
                    (3, 0) => 1,
                    _ => 0
                };

                if (score > bestScore)
                {
                    bestScore = score;
                    best = sub;
                }
            }

            var map = new Dictionary<int, ushort>(256);
            if (best < 0)
                return map;

            int format = r.UInt16(best);
            switch (format)
            {
                case 0:
                    for (int c = 0; c < 256; c++)
                        map[c] = r.Byte(best + 6 + c);
                    break;

                case 4:
                {
                    // Four parallel arrays of segCount shorts, plus the pad - eight bytes
                    // a segment at least, so that is the ceiling.
                    int segCount = Math.Min(r.UInt16(best + 6) / 2, Room(r, best + 14, 8));
                    int ends = best + 14;
                    int starts = ends + segCount * 2 + 2;
                    int deltas = starts + segCount * 2;
                    int ranges = deltas + segCount * 2;

                    for (int s = 0; s < segCount; s++)
                    {
                        int endCode = r.UInt16(ends + s * 2);
                        int startCode = r.UInt16(starts + s * 2);
                        int delta = r.Int16(deltas + s * 2);
                        int rangeOffset = r.UInt16(ranges + s * 2);

                        if (startCode > endCode || endCode == 0xFFFF && startCode == 0xFFFF)
                            continue;

                        for (int c = startCode; c <= endCode; c++)
                        {
                            ushort glyph;
                            if (rangeOffset == 0)
                            {
                                glyph = (ushort)((c + delta) & 0xFFFF);
                            }
                            else
                            {
                                int at = ranges + s * 2 + rangeOffset + (c - startCode) * 2;
                                if (at + 1 >= r.Length)
                                    continue;
                                glyph = r.UInt16(at);
                                if (glyph != 0)
                                    glyph = (ushort)((glyph + delta) & 0xFFFF);
                            }

                            if (glyph != 0)
                                map[c] = glyph;
                        }
                    }
                    break;
                }

                case 6:
                {
                    int first = r.UInt16(best + 6);
                    int count = Math.Min(r.UInt16(best + 8), Room(r, best + 10, 2));
                    for (int i = 0; i < count; i++)
                        map[first + i] = r.UInt16(best + 10 + i * 2);
                    break;
                }

                case 12:
                {
                    // Twelve bytes a group. This is the one that bought seven tenths of a
                    // second for two hundred and twelve bytes.
                    uint declared = r.UInt32(best + 12);
                    int groups = (int)Math.Min(declared, (uint)Room(r, best + 16, 12));
                    for (int g = 0; g < groups; g++)
                    {
                        int rec = best + 16 + g * 12;
                        uint startChar = r.UInt32(rec);
                        uint endChar = r.UInt32(rec + 4);
                        uint startGlyph = r.UInt32(rec + 8);

                        // Guard against absurd ranges in damaged fonts.
                        if (endChar < startChar || endChar - startChar > 0x10000)
                            continue;

                        for (uint c = startChar; c <= endChar; c++)
                            map[(int)c] = (ushort)(startGlyph + (c - startChar));
                    }
                    break;
                }
            }

            return map;
        }

        /// <summary>
        /// Folds the subfamily into the family name, or into the two style flags.
        ///
        /// A library keyed on family, bold and italic has nowhere to put a weight, so a
        /// cut that is neither bold nor italic - Light, Medium, SemiBold - has to become
        /// a family of its own or it lands on the regular and one of the two disappears.
        /// Glametrix does exactly that: three files, all calling themselves "Glametrix",
        /// distinguished only by Regular / Bold / Light.
        ///
        /// This is not a convention being invented. Elms Sans already ships that way -
        /// "Elms Sans Light" in the family record, "Elms Sans" in the typographic one -
        /// so all this does is make the archive that does not follow it look like the one
        /// that does.
        ///
        /// The list is of what counts as a *flag*, never of what counts as a weight.
        /// Whitelisting the weights would leave a hole for the next unusual word - a
        /// Caption or a Poster cut - which would then shadow the regular in silence. The
        /// other way round has no hole: anything unrecognised becomes part of the name.
        ///
        /// Whole words, too. The rule this replaces asked whether the subfamily
        /// *contained* "bold", which makes "SemiBold" a bold and drops it on top of the
        /// real one. Nothing in the library here triggers that, which is precisely why it
        /// would have gone on not being noticed.
        ///
        /// The name table is read rather than OS/2's usWeightClass, and deliberately:
        /// Glametrix's regular declares 500, which is Medium. It is the same lesson as
        /// the cap height - what a font says about itself is a claim, and here the
        /// designer's own name for the cut is the better one.
        /// </summary>
        private static (string Family, bool Bold, bool Italic) ApplySubfamily(
            string family, string subfamily, bool bold, bool italic)
        {
            var rest = new List<string>();

            foreach (string word in subfamily.Split(
                         new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (word.ToLowerInvariant())
                {
                    case "italic":
                    case "oblique":
                        italic = true;
                        break;
                    case "bold":
                        bold = true;
                        break;
                    case "regular":
                    case "normal":
                    case "book":
                    case "roman":
                        break;
                    default:
                        rest.Add(word);
                        break;
                }
            }

            return rest.Count == 0
                ? (family, bold, italic)
                : (family + " " + string.Join(" ", rest), bold, italic);
        }

        private static (string Family, string Subfamily) ReadNames(BigEndianReader r, int offset)
        {
            int count = r.UInt16(offset + 2);
            int storage = offset + r.UInt16(offset + 4);
            string family = "Unknown";
            string subfamily = "Regular";

            for (int i = 0; i < count; i++)
            {
                int rec = offset + 6 + i * 12;
                int platform = r.UInt16(rec);
                int encoding = r.UInt16(rec + 2);
                int nameId = r.UInt16(rec + 6);
                int length = r.UInt16(rec + 8);
                int at = storage + r.UInt16(rec + 10);

                if (nameId != 1 && nameId != 2)
                    continue;
                if (at + length > r.Length)
                    continue;

                bool utf16 = platform == 3 || (platform == 0) || encoding == 1 && platform == 0;
                string value = utf16
                    ? System.Text.Encoding.BigEndianUnicode.GetString(r.Slice(at, length))
                    : System.Text.Encoding.ASCII.GetString(r.Slice(at, length));

                value = value.Trim('\0', ' ');
                if (value.Length == 0)
                    continue;

                if (nameId == 1)
                    family = value;
                else
                    subfamily = value;
            }

            return (family, subfamily);
        }

        /// <summary>
        /// Font files are big-endian; nothing in .NET reads them that way for free.
        ///
        /// Past either end it returns zero rather than throwing, so a truncated or
        /// scrambled font is refused for what it says rather than for where it stopped.
        ///
        /// <b>Either</b> end. Every guard here once tested only the top -
        /// <c>at + 1 &lt; Length</c> - which is perfectly true of a negative index, and a
        /// negative index is not exotic: a table offset is a <c>uint</c> in the file and
        /// an <c>int</c> here, so anything past 0x7FFFFFFF arrives below zero. Reading
        /// then threw where nothing was catching, and a fuzzer found it in a few
        /// thousand tries after this file had been read twice and called safe.
        /// </summary>
        private readonly struct BigEndianReader
        {
            private readonly byte[] _data;

            public BigEndianReader(byte[] data) => _data = data;

            public int Length => _data.Length;

            /// <summary>Whether <paramref name="count"/> bytes from here are inside the file.</summary>
            private bool Has(int at, int count) => at >= 0 && at <= _data.Length - count;

            public byte Byte(int at) => Has(at, 1) ? _data[at] : (byte)0;

            public ushort UInt16(int at)
                => Has(at, 2) ? (ushort)((_data[at] << 8) | _data[at + 1]) : (ushort)0;

            public short Int16(int at) => unchecked((short)UInt16(at));

            public uint UInt32(int at)
                => Has(at, 4)
                    ? ((uint)_data[at] << 24) | ((uint)_data[at + 1] << 16) | ((uint)_data[at + 2] << 8) | _data[at + 3]
                    : 0u;

            public string Tag(int at)
                => Has(at, 4)
                    ? new string(new[] { (char)_data[at], (char)_data[at + 1], (char)_data[at + 2], (char)_data[at + 3] })
                    : string.Empty;

            public byte[] Slice(int at, int length)
            {
                if (length <= 0 || at < 0 || at >= _data.Length)
                    return Array.Empty<byte>();

                var copy = new byte[length];
                Array.Copy(_data, at, copy, 0, Math.Min(length, _data.Length - at));
                return copy;
            }
        }
    }
}
