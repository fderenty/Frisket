using System.Globalization;

namespace Frisket.Import
{
    /// <summary>
    /// A PDF opened for reading: where its objects are, and how to fetch one.
    ///
    /// A PDF is read from its end. The last line but one says where the cross-reference
    /// section is; that section says where every object is, and names the section before
    /// it, and so on back through however many times the file has been saved. Two shapes
    /// of section exist - the table, written in plain text since 1993, and the stream,
    /// which is the same thing packed into binary and Flate-compressed - and a file may
    /// use both at once.
    ///
    /// <para><b>And the whole of it may be wrong.</b> An offset in a cross-reference table
    /// is a number in a file somebody else wrote; a document assembled by concatenation,
    /// truncated by a mail gateway or produced by a careless tool has offsets that point
    /// at nothing. Every reader in the world therefore has a repair path, and so does this
    /// one: when an object is not where the table says, the file is swept for what it
    /// actually contains. That is not indulgence - a reader that refused those refuses
    /// files every other reader opens.</para>
    /// </summary>
    internal sealed class PdfFile
    {
        /// <summary>
        /// How many objects a file may declare. A cross-reference stream states its own
        /// entry count, which is exactly the shape of number section 8c is about: bounded
        /// here by arithmetic on the length of the file, never taken at its word.
        /// </summary>
        private const int MaxObjects = 8_000_000;

        private readonly PdfSyntax _syntax;
        private readonly Dictionary<int, long> _offsets = new();
        private readonly Dictionary<int, (int Container, int Index)> _inStreams = new();
        private readonly Dictionary<int, PdfObject> _cache = new();
        private readonly HashSet<int> _fetching = new();
        private bool _swept;

        public PdfDictionary Trailer { get; private set; } = new(new Dictionary<string, PdfObject>());

        private PdfFile(byte[] data) => _syntax = new PdfSyntax(data);

        public static PdfFile Open(byte[] data)
        {
            if (data.Length < 8)
                throw new InvalidDataException("Too short to be a PDF.");

            var file = new PdfFile(data);
            file.ReadCrossReferences();

            // Refused outright rather than half-read. Every string and every stream in an
            // encrypted file is enciphered, so a page copied out of one would travel as
            // bytes that mean nothing in their new home - and would look, on the way past,
            // exactly like a page that copied correctly.
            if (file.Trailer["Encrypt"] != null)
                throw new InvalidDataException("This PDF is encrypted, and this reader does not decrypt.");

            return file;
        }

        /// <summary>The object a reference names, or the object itself when it is not one.</summary>
        public PdfObject? Resolve(PdfObject? value) =>
            value is PdfReference reference ? Get(reference.Number) : value;

        /// <summary>
        /// One object, by number.
        ///
        /// The guard is a cycle: an object whose length is an indirect reference to
        /// itself is a file that describes a loop, and following it is a stack overflow
        /// rather than an exception - out of reach of any catch, which is the same
        /// lesson the handle register in section 8b was written for.
        /// </summary>
        public PdfObject? Get(int number)
        {
            if (number <= 0)
                return null;

            if (_cache.TryGetValue(number, out PdfObject? known))
                return known;

            if (!_fetching.Add(number))
                return null;

            try
            {
                PdfObject? found = Fetch(number);

                // Not where it was said to be. Sweep the file once for what it really
                // holds and ask again - a repair, and it happens at most once per file.
                if (found == null && !_swept)
                {
                    Sweep();
                    found = Fetch(number);
                }

                if (found != null)
                    _cache[number] = found;

                return found;
            }
            finally
            {
                _fetching.Remove(number);
            }
        }

        private PdfObject? Fetch(int number)
        {
            if (_offsets.TryGetValue(number, out long offset))
            {
                PdfObject? direct = ReadAt(offset, number);
                if (direct != null)
                    return direct;
            }

            if (_inStreams.TryGetValue(number, out (int Container, int Index) where))
                return FromObjectStream(where.Container, where.Index, number);

            return null;
        }

        /// <summary>Reads "n g obj ... endobj" at an offset, checking it really is that object.</summary>
        private PdfObject? ReadAt(long offset, int expected)
        {
            if (offset <= 0 || offset >= _syntax.Length)
                return null;

            _syntax.At = (int)offset;
            _syntax.SkipSpace();

            string number = _syntax.ReadToken();
            string generation = _syntax.ReadToken();
            string keyword = _syntax.ReadToken();

            if (keyword != "obj"
                || !int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int actual)
                || actual != expected)
            {
                _ = generation;
                return null;
            }

            return _syntax.ReadObject(0, n => Get(n));
        }

        private PdfObject? FromObjectStream(int container, int index, int expected)
        {
            if (Get(container) is not PdfStream stream)
                return null;

            byte[] bytes = Filters.Decode(stream, Resolve);
            int count = Filters.Whole(Resolve(stream["N"]), 0);
            int first = Filters.Whole(Resolve(stream["First"]), 0);

            if (count <= 0 || first < 0 || first > bytes.Length || index < 0 || index >= count)
                return null;

            // The pairs at the head are (object number, offset from /First), and both are
            // numbers from the file. Read them all, then trust none of them further than
            // the length of what was actually decompressed.
            var head = new PdfSyntax(bytes);
            int at = -1;

            for (int i = 0; i < count; i++)
            {
                head.SkipSpace();
                if (head.At >= first) break;

                string n = head.ReadToken();
                string o = head.ReadToken();

                if (i != index) continue;

                if (!int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    || number != expected
                    || !int.TryParse(o, NumberStyles.Integer, CultureInfo.InvariantCulture, out at))
                    return null;

                break;
            }

            if (at < 0 || first + (long)at >= bytes.Length)
                return null;

            var body = new PdfSyntax(bytes) { At = first + at };
            return body.ReadObject(0, n => Get(n));
        }

        // ------------------------------------------------------------------ the tables

        private void ReadCrossReferences()
        {
            int marker = _syntax.LastIndexOf("startxref", _syntax.Length);
            if (marker < 0)
            {
                Sweep();
                ReadTrailerBySweeping();
                return;
            }

            _syntax.At = marker + "startxref".Length;
            string where = _syntax.ReadToken();

            if (!long.TryParse(where, NumberStyles.Integer, CultureInfo.InvariantCulture, out long at))
            {
                Sweep();
                ReadTrailerBySweeping();
                return;
            }

            var seen = new HashSet<long>();
            var trailers = new List<PdfDictionary>();

            // Newest first, and an entry already known is never replaced: that is how an
            // incremental save works, and reading the chain the other way round would
            // hand back the version of every object as it stood before the last edit.
            while (at > 0 && at < _syntax.Length && seen.Add(at))
            {
                PdfDictionary? section = ReadSection(at);
                if (section == null) break;

                trailers.Add(section);

                // A hybrid file: a plain table for old readers, and a stream beside it
                // carrying the objects the table cannot describe. Both are needed.
                if (section["XRefStm"] is PdfNumber hybrid && seen.Add((long)hybrid.Value))
                    ReadSection((long)hybrid.Value);

                at = section["Prev"] is PdfNumber previous ? (long)previous.Value : 0;
            }

            var merged = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
            foreach (PdfDictionary trailer in trailers)
                foreach ((string key, PdfObject value) in trailer.Entries)
                    merged.TryAdd(key, value);

            Trailer = new PdfDictionary(merged);

            if (Trailer["Root"] == null)
            {
                Sweep();
                ReadTrailerBySweeping();
            }
        }

        private PdfDictionary? ReadSection(long at)
        {
            if (at <= 0 || at >= _syntax.Length)
                return null;

            _syntax.At = (int)at;
            _syntax.SkipSpace();

            return _syntax.Looking("xref") ? ReadTable() : ReadStream(at);
        }

        private PdfDictionary? ReadTable()
        {
            _syntax.At += "xref".Length;

            while (true)
            {
                _syntax.SkipSpace();
                if (_syntax.Looking("trailer"))
                {
                    _syntax.At += "trailer".Length;
                    return _syntax.ReadObject(0, n => Get(n)) as PdfDictionary;
                }

                int save = _syntax.At;
                string startText = _syntax.ReadToken();
                string countText = _syntax.ReadToken();

                if (!int.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
                    || !int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                    || start < 0 || count < 0)
                {
                    _syntax.At = save;
                    return null;
                }

                // Twenty bytes an entry, said by the standard, so what the file can hold
                // is the only bound worth having on a count it declares itself.
                long room = (_syntax.Length - _syntax.At) / 18;
                if (count > room) count = (int)Math.Max(0, room);
                if (count > MaxObjects) count = MaxObjects;

                for (int i = 0; i < count; i++)
                {
                    _syntax.SkipSpace();
                    string offset = _syntax.ReadToken();
                    string generation = _syntax.ReadToken();
                    string kind = _syntax.ReadToken();
                    _ = generation;

                    if (kind != "n") continue;
                    if (!long.TryParse(offset, NumberStyles.Integer, CultureInfo.InvariantCulture, out long where))
                        continue;

                    _offsets.TryAdd(start + i, where);
                }
            }
        }

        private PdfDictionary? ReadStream(long at)
        {
            _syntax.At = (int)at;
            _syntax.SkipSpace();

            _syntax.ReadToken();                       // number
            _syntax.ReadToken();                       // generation
            if (_syntax.ReadToken() != "obj")
                return null;

            if (_syntax.ReadObject(0, n => Get(n)) is not PdfStream stream)
                return null;

            byte[] bytes = Filters.Decode(stream, Resolve);

            if (Resolve(stream["W"]) is not PdfArray widths || widths.Items.Count < 3)
                return null;

            int[] w = widths.Items.Select(i => Filters.Whole(Resolve(i), 0)).ToArray();
            int row = w.Sum();
            if (row <= 0 || row > 32)
                return null;

            var ranges = new List<(int Start, int Count)>();
            if (Resolve(stream["Index"]) is PdfArray index && index.Items.Count >= 2)
            {
                for (int i = 0; i + 1 < index.Items.Count; i += 2)
                    ranges.Add((Filters.Whole(Resolve(index.Items[i]), 0),
                                Filters.Whole(Resolve(index.Items[i + 1]), 0)));
            }
            else
            {
                ranges.Add((0, Filters.Whole(Resolve(stream["Size"]), 0)));
            }

            int cursor = 0;
            foreach ((int start, int count) in ranges)
            {
                if (start < 0 || count <= 0) continue;

                // Bounded by what really came out of the stream, not by what it says.
                int room = (bytes.Length - cursor) / row;
                int take = Math.Min(count, Math.Max(0, room));

                for (int i = 0; i < take; i++, cursor += row)
                {
                    long kind = w[0] == 0 ? 1 : Read(bytes, cursor, w[0]);
                    long first = Read(bytes, cursor + w[0], w[1]);
                    long second = Read(bytes, cursor + w[0] + w[1], w[2]);

                    int number = start + i;
                    if (kind == 1) _offsets.TryAdd(number, first);
                    else if (kind == 2) _inStreams.TryAdd(number, ((int)first, (int)second));
                }
            }

            return new PdfDictionary(stream.Entries);
        }

        private static long Read(byte[] bytes, int at, int width)
        {
            long value = 0;
            for (int i = 0; i < width; i++)
            {
                int j = at + i;
                value = (value << 8) | (long)(j >= 0 && j < bytes.Length ? bytes[j] : (byte)0);
            }
            return value;
        }

        // ------------------------------------------------------------------ the repair

        /// <summary>
        /// Reads the file for what it actually contains, rather than for what its
        /// cross-reference table says it does.
        ///
        /// Every "n g obj" in the file, latest occurrence winning - which is what an
        /// incrementally saved document means by putting the same object down twice.
        /// </summary>
        private void Sweep()
        {
            _swept = true;
            byte[] data = _syntax.Bytes;

            for (int i = 0; i + 3 < data.Length; i++)
            {
                if (data[i] != 'o' || data[i + 1] != 'b' || data[i + 2] != 'j')
                    continue;

                if (PdfSyntax.IsRegular(data[i + 3]))
                    continue;

                // Walk back over "n g " to the number, since that is where the object
                // begins and where an offset would have pointed.
                int at = i - 1;
                while (at >= 0 && PdfSyntax.IsSpace(data[at])) at--;
                int generationEnd = at + 1;
                while (at >= 0 && data[at] >= '0' && data[at] <= '9') at--;
                int generationStart = at + 1;
                if (generationStart == generationEnd) continue;

                while (at >= 0 && PdfSyntax.IsSpace(data[at])) at--;
                int numberEnd = at + 1;
                while (at >= 0 && data[at] >= '0' && data[at] <= '9') at--;
                int numberStart = at + 1;
                if (numberStart == numberEnd) continue;
                if (at >= 0 && PdfSyntax.IsRegular(data[at])) continue;

                if (int.TryParse(_syntax.Latin1(numberStart, numberEnd - numberStart),
                                 NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    _offsets[number] = numberStart;
                    _cache.Remove(number);
                }
            }
        }

        private void ReadTrailerBySweeping()
        {
            // The catalogue names itself, so a file with no usable trailer still has one
            // object saying /Type /Catalog. Every reader falls back to this, and a
            // document rebuilt this way opens where a strict one refuses.
            var merged = new Dictionary<string, PdfObject>(Trailer.Entries, StringComparer.Ordinal);

            int trailer = _syntax.LastIndexOf("trailer", _syntax.Length);
            while (trailer >= 0 && !merged.ContainsKey("Root"))
            {
                _syntax.At = trailer + "trailer".Length;
                if (_syntax.ReadObject(0, n => Get(n)) is PdfDictionary found && found["Root"] != null)
                {
                    foreach ((string key, PdfObject value) in found.Entries)
                        merged.TryAdd(key, value);
                    break;
                }

                trailer = _syntax.LastIndexOf("trailer", trailer - 1);
            }

            if (!merged.ContainsKey("Root"))
            {
                foreach (int number in _offsets.Keys.OrderBy(n => n))
                {
                    if (Get(number) is PdfDictionary dictionary
                        && (dictionary["Type"] as PdfName)?.Value == "Catalog")
                    {
                        merged["Root"] = new PdfReference(number);
                        break;
                    }
                }
            }

            Trailer = new PdfDictionary(merged);
        }

        // ------------------------------------------------------------------- the pages

        /// <summary>
        /// The page at <paramref name="index"/>, counting from zero, with everything it
        /// inherits already folded in.
        ///
        /// <c>/Resources</c>, <c>/MediaBox</c>, <c>/CropBox</c> and <c>/Rotate</c> are
        /// inheritable: a page that states none of them takes its parent's, and a great
        /// many producers put all four on the tree and none on the leaves. Reading only
        /// the leaf gives a page with no size and no fonts, which draws nothing and looks
        /// like an empty document rather than like a reader that did not look up.
        /// </summary>
        public PdfDictionary Page(int index)
        {
            if (Resolve(Trailer["Root"]) is not PdfDictionary root)
                throw new InvalidDataException("This PDF names no catalogue, so it has no pages to read.");

            if (Resolve(root["Pages"]) is not PdfDictionary tree)
                throw new InvalidDataException("This PDF's catalogue names no page tree.");

            var inherited = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
            PdfDictionary? found = Walk(tree, inherited, ref index, new HashSet<PdfDictionary>(), 0);

            if (found == null)
                throw new InvalidDataException("This PDF has no page at that number.");

            var entries = new Dictionary<string, PdfObject>(found.Entries, StringComparer.Ordinal);
            foreach ((string key, PdfObject value) in inherited)
                entries.TryAdd(key, value);

            return new PdfDictionary(entries);
        }

        private static readonly string[] Inheritable = ["Resources", "MediaBox", "CropBox", "Rotate"];

        private PdfDictionary? Walk(PdfDictionary node, Dictionary<string, PdfObject> inherited,
                                    ref int index, HashSet<PdfDictionary> seen, int depth)
        {
            if (depth > PdfSyntax.MaxDepth || !seen.Add(node))
                return null;

            string type = (Resolve(node["Type"]) as PdfName)?.Value ?? "";

            if (type == "Page" || (type != "Pages" && node["Kids"] == null))
            {
                if (index > 0) { index--; return null; }
                return node;
            }

            // Carried down a copy, not the caller's: a sibling that states its own size
            // must not hand it to the branch after it.
            var below = new Dictionary<string, PdfObject>(inherited, StringComparer.Ordinal);
            foreach (string key in Inheritable)
                if (node[key] is { } value)
                    below[key] = value;

            if (Resolve(node["Kids"]) is not PdfArray kids)
                return null;

            foreach (PdfObject kid in kids.Items)
            {
                if (Resolve(kid) is not PdfDictionary child)
                    continue;

                PdfDictionary? found = Walk(child, below, ref index, seen, depth + 1);
                if (found == null)
                    continue;

                foreach ((string key, PdfObject value) in below)
                    inherited.TryAdd(key, value);

                return found;
            }

            return null;
        }
    }
}
