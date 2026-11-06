using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Frisket
{
    /// <summary>
    /// The bare bones of the PDF container: numbered objects, streams, a cross-reference
    /// table and a trailer. Enough to write a section, and nothing more - a report needs
    /// text, lines, rectangles and images, not a page description language.
    ///
    /// Deflate comes from System.IO.Compression, which is part of the framework, so this
    /// keeps the library free of dependencies like everything else here. A caller who wants
    /// a smaller encoder brings one (<see cref="Compressor"/>); the engine never references it.
    /// </summary>
    internal sealed class PdfWriter
    {
        private readonly List<byte[]?> _objects = new();

        /// <summary>The objects that are streams, which an object stream may not hold.</summary>
        private readonly HashSet<int> _streams = new();

        /// <summary>
        /// The streams still to be deflated, by object number, with their dictionaries.
        ///
        /// Deflated when the file is built, all together, rather than one by one as they
        /// arrive. That is what lets them go through several threads at once, and it makes the
        /// compressor the one set when the file is built: a stream given while drawing, through
        /// <see cref="IPdfSink"/>, would otherwise be deflated by whatever was set at that
        /// moment. The price is that every stream's bytes are held until then.
        /// </summary>
        private readonly Dictionary<int, (string Dictionary, byte[] Data, string? DecodeParms)> _pending = new();

        /// <summary>
        /// What deflates the streams, when the caller brought something: see
        /// <see cref="PdfBackend.Compressor"/>. Null is the framework's zlib at its smallest,
        /// which is what this has always written.
        /// </summary>
        public ZlibCompressor? Compressor { get; set; }

        /// <summary>How many streams are deflated at once: see <see cref="PdfBackend.CompressionThreads"/>.</summary>
        public int Threads { get; set; } = 1;

        /// <summary>
        /// How many objects one object stream holds. Enough for the dictionaries of a page to
        /// deflate together, few enough that a reader after one object inflates little else.
        /// </summary>
        private const int PerObjectStream = 100;

        /// <summary>Reserves an object number to be filled in later, for forward references.</summary>
        public int Reserve()
        {
            _objects.Add(null);
            return _objects.Count;
        }

        public void Set(int number, string body)
        {
            _objects[number - 1] = Latin1(body);
            _streams.Remove(number);
            _pending.Remove(number);
        }

        public int Add(string body)
        {
            int number = Reserve();
            Set(number, body);
            return number;
        }

        /// <summary>Adds a stream object. <paramref name="dictionary"/> excludes the length.</summary>
        public int AddStream(string dictionary, byte[] data, bool compress = true)
        {
            int number = Reserve();
            SetStream(number, dictionary, data, compress);
            return number;
        }

        /// <summary>
        /// Adds a stream that is always deflated, with what its filter needs to read it -
        /// a predictor, for a picture's rows. Always, whatever its length: parameters for a
        /// filter that is not there would have the rows' filter bytes drawn as pixels.
        ///
        /// Written after the filter, which is where a picture that crosses as it came has them,
        /// so the two say the same thing in the same words and a comparison of two files sees
        /// one dictionary.
        /// </summary>
        public int AddStream(string dictionary, byte[] data, string decodeParms)
        {
            int number = Reserve();
            _objects[number - 1] = null;
            _pending[number] = (dictionary, data, decodeParms);
            _streams.Add(number);
            return number;
        }

        /// <summary>
        /// Fills a stream object whose number was reserved earlier.
        ///
        /// Separate from <see cref="AddStream(string, byte[], bool)"/> because an object graph copied out of
        /// another file - a page being imported - has references pointing in every
        /// direction, so every number has to be known before any body can be written.
        /// A method that reserves its own number cannot serve that.
        /// </summary>
        public void SetStream(int number, string dictionary, byte[] data, bool compress = true)
        {
            if (WorthDeflating(compress, data))
            {
                _objects[number - 1] = null;
                _pending[number] = (dictionary, data, null);
            }
            else
            {
                _objects[number - 1] = StreamBody(dictionary, data, deflated: false);
                _pending.Remove(number);
            }

            _streams.Add(number);
        }

        /// <summary>A stream short enough is stored as it is: deflating it would save nothing.</summary>
        private static bool WorthDeflating(bool compress, byte[] data) => compress && data.Length > 128;

        /// <summary>A stream object's body: its dictionary, then its bytes, as they will be in the file.</summary>
        private static byte[] StreamBody(string dictionary, byte[] payload, bool deflated, string? decodeParms = null)
        {
            string filter = !deflated ? string.Empty
                : decodeParms == null ? "/Filter /FlateDecode "
                : "/Filter /FlateDecode " + decodeParms + " ";

            var head = Latin1($"<< {dictionary} {filter}/Length {payload.Length} >>\nstream\n");
            var tail = Latin1("\nendstream");

            var body = new byte[head.Length + payload.Length + tail.Length];
            Buffer.BlockCopy(head, 0, body, 0, head.Length);
            Buffer.BlockCopy(payload, 0, body, head.Length, payload.Length);
            Buffer.BlockCopy(tail, 0, body, head.Length + payload.Length, tail.Length);
            return body;
        }

        /// <summary>Adds a stream whose bytes are already compressed, or must not be touched.</summary>
        public int AddRawStream(string dictionary, byte[] data) => AddStream(dictionary, data, compress: false);

        /// <summary>
        /// The file. <paramref name="packed"/> puts every object that is not a stream into
        /// deflated object streams and writes the cross-reference table as a stream as well
        /// (ISO 32000-1 7.5.7, 7.5.8); otherwise each object is written as text where it
        /// stands, and the table as a table.
        /// </summary>
        public byte[] Build(int catalog, string producer, DateTimeOffset now,
                            string? title = null, string? author = null, byte[]? documentId = null,
                            string? subject = null, string? keywords = null, bool packed = false)
        {
            // PDF/A wants the info dictionary and the XMP packet to agree, so whatever
            // goes in one goes in the other.
            var info = new StringBuilder("<< /Producer (").Append(Escape(producer)).Append(") /CreationDate (")
                       .Append(Timestamp(now)).Append(") /ModDate (").Append(Timestamp(now)).Append(')');
            if (!string.IsNullOrEmpty(title))
                info.Append(" /Title ").Append(Text(title));
            if (!string.IsNullOrEmpty(author))
                info.Append(" /Author ").Append(Text(author));
            if (!string.IsNullOrEmpty(subject))
                info.Append(" /Subject ").Append(Text(subject));
            if (!string.IsNullOrEmpty(keywords))
                info.Append(" /Keywords ").Append(Text(keywords));
            info.Append(" >>");

            int infoObject = Add(info.ToString());

            // Every stream waiting to be deflated, before anything is laid out: the offsets
            // depend on their lengths.
            int[] waiting = _pending.Keys.Order().ToArray();
            byte[][] deflated = DeflateAll(waiting.Select(n => _pending[n].Data).ToArray());
            for (int i = 0; i < waiting.Length; i++)
            {
                var (dictionary, _, decodeParms) = _pending[waiting[i]];
                _objects[waiting[i] - 1] = StreamBody(dictionary, deflated[i], deflated: true, decodeParms);
            }
            _pending.Clear();

            if (packed)
                return Packed(catalog, infoObject, documentId);

            using var stream = new MemoryStream();
            Write(stream, "%PDF-1.7\n");
            stream.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });   // binary marker

            var offsets = new long[_objects.Count + 1];

            for (int i = 0; i < _objects.Count; i++)
            {
                byte[] body = _objects[i]
                              ?? throw new InvalidOperationException($"PDF object {i + 1} was reserved but never set.");

                offsets[i + 1] = stream.Position;
                Write(stream, $"{i + 1} 0 obj\n");
                stream.Write(body);
                Write(stream, "\nendobj\n");
            }

            long xref = stream.Position;
            Write(stream, $"xref\n0 {_objects.Count + 1}\n");
            Write(stream, "0000000000 65535 f \n");
            for (int i = 1; i <= _objects.Count; i++)
                Write(stream, offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");

            var trailer = new StringBuilder("trailer\n<< /Size ").Append(_objects.Count + 1)
                          .Append(" /Root ").Append(catalog).Append(" 0 R /Info ").Append(infoObject).Append(" 0 R");

            if (documentId != null)
            {
                // An archivable PDF must carry a file identifier. Both halves match here:
                // the section has never been revised.
                string hex = Convert.ToHexString(documentId);
                trailer.Append(" /ID [<").Append(hex).Append("> <").Append(hex).Append(">]");
            }

            trailer.Append(" >>\n");
            Write(stream, trailer.ToString());
            Write(stream, $"startxref\n{xref}\n%%EOF\n");

            return stream.ToArray();
        }

        /// <summary>
        /// The file with its dictionaries deflated together.
        ///
        /// Everything that is not a stream - the catalog, the pages, the fonts' dictionaries,
        /// every element of a tagged file's structure tree - goes into object streams of
        /// <see cref="PerObjectStream"/>, and the cross-reference table becomes a stream of
        /// binary entries. A tagged document is mostly small dictionaries and a table of
        /// twenty bytes an object, which is where the saving is: the conformance document went
        /// from 160 KB to 107. PDF/A-1 forbade both, being PDF 1.4; PDF/A-2 and -3
        /// allow them and PDF/UA-1 says nothing against them. The file is never hybrid - a
        /// table beside the stream for older readers - which PDF/A-2 does forbid.
        ///
        /// Streams stay where they are, as text with their dictionary in front: an object
        /// stream may not hold one (7.5.7).
        /// </summary>
        private byte[] Packed(int catalog, int info, byte[]? documentId)
        {
            // Which object stream holds each packed object, and at which index. The object
            // streams and the table are numbered after the writer's own objects and never
            // join them, so the writer is left as it was found.
            var held = new Dictionary<int, (int Container, int Index)>();
            var made = new List<(string Dictionary, byte[] Content)>();
            int objects = _objects.Count;
            var loose = Enumerable.Range(1, objects).Where(n => !_streams.Contains(n)).ToList();

            for (int start = 0; start < loose.Count; start += PerObjectStream)
            {
                List<int> group = loose.GetRange(start, Math.Min(PerObjectStream, loose.Count - start));
                var index = new StringBuilder();
                var bodies = new MemoryStream();

                foreach (int number in group)
                {
                    byte[] body = _objects[number - 1]
                                  ?? throw new InvalidOperationException($"PDF object {number} was reserved but never set.");
                    index.Append(number).Append(' ').Append(bodies.Length).Append(' ');
                    bodies.Write(body);
                    bodies.WriteByte((byte)'\n');
                }

                byte[] head = Latin1(index.ToString());
                byte[] content = new byte[head.Length + bodies.Length];
                head.CopyTo(content, 0);
                bodies.ToArray().CopyTo(content, head.Length);

                made.Add(($"/Type /ObjStm /N {group.Count} /First {head.Length}", content));
                for (int i = 0; i < group.Count; i++)
                    held[group[i]] = (objects + made.Count, i);
            }

            // Deflated together, as the streams were, and then each in its own place.
            int[] worth = Enumerable.Range(0, made.Count).Where(i => WorthDeflating(true, made[i].Content)).ToArray();
            byte[][] squeezed = DeflateAll(worth.Select(i => made[i].Content).ToArray());
            var containers = made.Select(m => StreamBody(m.Dictionary, m.Content, deflated: false)).ToList();
            for (int i = 0; i < worth.Length; i++)
                containers[worth[i]] = StreamBody(made[worth[i]].Dictionary, squeezed[i], deflated: true);

            int xrefNumber = objects + containers.Count + 1;
            int size = xrefNumber + 1;

            using var stream = new MemoryStream();
            Write(stream, "%PDF-1.7\n");
            stream.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });   // binary marker

            var offsets = new long[size];
            for (int number = 1; number < xrefNumber; number++)
            {
                if (held.ContainsKey(number))
                    continue;

                offsets[number] = stream.Position;
                Write(stream, $"{number} 0 obj\n");
                stream.Write(number > objects
                    ? containers[number - objects - 1]
                    : _objects[number - 1] ?? throw new InvalidOperationException($"PDF object {number} was reserved but never set."));
                Write(stream, "\nendobj\n");
            }

            long xrefAt = stream.Position;
            offsets[xrefNumber] = xrefAt;

            // One byte for the kind of entry, as few as hold the largest offset or object
            // number for the second field, two for the third.
            int width = 1;
            while ((Math.Max(xrefAt, size) >> (8 * width)) > 0)
                width++;

            int entry = 1 + width + 2;
            var entries = new byte[size * entry];
            for (int number = 0; number < size; number++)
            {
                (int kind, long second, int third) = number == 0 ? (0, 0L, 0xFFFF)
                    : held.TryGetValue(number, out var place) ? (2, place.Container, place.Index)
                    : (1, offsets[number], 0);

                int at = number * entry;
                entries[at] = (byte)kind;
                for (int b = 0; b < width; b++)
                    entries[at + 1 + b] = (byte)(second >> (8 * (width - 1 - b)));
                entries[at + 1 + width] = (byte)(third >> 8);
                entries[at + 2 + width] = (byte)third;
            }

            byte[] table = Deflated(entries);
            var dictionary = new StringBuilder("<< /Type /XRef /Size ").Append(size)
                .Append(" /W [1 ").Append(width).Append(" 2] /Root ").Append(catalog).Append(" 0 R /Info ")
                .Append(info).Append(" 0 R");

            if (documentId != null)
            {
                string hex = Convert.ToHexString(documentId);
                dictionary.Append(" /ID [<").Append(hex).Append("> <").Append(hex).Append(">]");
            }

            dictionary.Append(" /Filter /FlateDecode /Length ").Append(table.Length).Append(" >>\nstream\n");

            Write(stream, $"{xrefNumber} 0 obj\n");
            Write(stream, dictionary.ToString());
            stream.Write(table);
            Write(stream, "\nendstream\nendobj\n");
            Write(stream, $"startxref\n{xrefAt}\n%%EOF\n");

            return stream.ToArray();
        }

        // ------------------------------------------------------------------

        private static void Write(Stream stream, string text)
        {
            byte[] bytes = Latin1(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// PDF syntax is byte oriented: everything outside a stream is written one byte
        /// per character, and real text travels as hex-encoded glyph ids instead.
        /// </summary>
        public static byte[] Latin1(string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
                bytes[i] = text[i] < 256 ? (byte)text[i] : (byte)'?';
            return bytes;
        }

        public static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var deflate = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                deflate.Write(data, 0, data.Length);
            return output.ToArray();
        }

        /// <summary>
        /// Several streams deflated, each answer in the place of its question.
        ///
        /// On more than one thread when <see cref="Threads"/> allows it, and the file is the
        /// same either way: each stream is deflated on its own, and the file is laid out in
        /// object order afterwards. The largest go first, handed out one at a time, so a font
        /// or an image that takes most of the work is not the last one started.
        /// </summary>
        private byte[][] DeflateAll(byte[][] inputs)
        {
            var outputs = new byte[inputs.Length][];

            if (Threads == 1 || inputs.Length < 2)
            {
                for (int i = 0; i < inputs.Length; i++)
                    outputs[i] = Deflated(inputs[i]);
                return outputs;
            }

            int[] largestFirst = Enumerable.Range(0, inputs.Length).OrderByDescending(i => inputs[i].Length).ToArray();
            try
            {
                Parallel.ForEach(
                    System.Collections.Concurrent.Partitioner.Create(largestFirst,
                        System.Collections.Concurrent.EnumerablePartitionerOptions.NoBuffering),
                    new ParallelOptions { MaxDegreeOfParallelism = Threads },
                    i => outputs[i] = Deflated(inputs[i]));
            }
            catch (AggregateException failed)
            {
                // What a stream threw, as it would have been thrown on one thread: a
                // cancellation stays a cancellation, and a refusal keeps its own words.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed.InnerExceptions[0]).Throw();
            }

            return outputs;
        }

        /// <summary>
        /// One stream deflated: by the framework's zlib, or by the caller's compressor once its
        /// answer has been shown to be one.
        ///
        /// Shown, because nothing else would: a reader handed a stream that is not zlib draws a
        /// blank page or a missing font, and says nothing of why. The commonest way to get there
        /// is the plausible one - asking for raw deflate because the filter is called
        /// FlateDecode - and inflating the answer costs little beside an encoder slow enough to
        /// be worth bringing.
        /// </summary>
        private byte[] Deflated(byte[] data)
        {
            if (Compressor is not { } compressor)
                return Deflate(data);

            byte[] made = compressor(data);
            if (NotZlibOf(made, data) is { } why)
            {
                throw new InvalidOperationException(
                    $"PdfBackend.Compressor handed back {made?.Length ?? 0} bytes that are not a zlib stream of the " +
                    $"{data.Length} it was given: {why}. /FlateDecode reads zlib (RFC 1950) - a two-byte header, the " +
                    "deflate data, then the Adler-32 of what was compressed. With ZopfliSharp that is " +
                    "ZopfliFormat.Zlib; ZopfliFormat.Deflate leaves the header and the checksum out.");
            }

            return made;
        }

        /// <summary>What is wrong with <paramref name="made"/> as a zlib stream of <paramref name="data"/>, or null.</summary>
        private static string? NotZlibOf(byte[]? made, byte[] data)
        {
            if (made is not { Length: >= 7 })
                return "too short to hold a header, any data and a checksum";

            int cmf = made[0], flg = made[1];
            if ((cmf & 0x0F) != 8 || cmf >> 4 > 7 || ((cmf << 8) | flg) % 31 != 0)
                return "it does not start with a zlib header";
            if ((flg & 0x20) != 0)
                return "it asks for a preset dictionary, which a PDF has no way to give";

            uint adler = (uint)(made[^4] << 24 | made[^3] << 16 | made[^2] << 8 | made[^1]);
            if (adler != Adler32(data))
                return "its checksum is not that of the data";

            try
            {
                using var inflate = new ZLibStream(new MemoryStream(made), CompressionMode.Decompress);
                var back = new byte[data.Length + 1];
                int filled = 0, got;
                while (filled < back.Length && (got = inflate.Read(back, filled, back.Length - filled)) > 0)
                    filled += got;

                if (filled != data.Length || !back.AsSpan(0, filled).SequenceEqual(data))
                    return "it inflates to something else";
            }
            // InvalidDataException for data that is not deflate, and ZLibException - an
            // IOException - for an error code zlib returns instead, such as asking for a
            // dictionary; neither names the stream, and this does.
            catch (Exception broken) when (broken is InvalidDataException or IOException)
            {
                return "it does not inflate (" + broken.Message + ")";
            }

            return null;
        }

        /// <summary>The checksum a zlib stream ends with (RFC 1950 8.2).</summary>
        private static uint Adler32(byte[] data)
        {
            const uint Base = 65521;
            uint a = 1, b = 0;
            int at = 0;

            // 5552 is the most bytes that can be summed before b can overflow 32 bits.
            while (at < data.Length)
            {
                int end = Math.Min(at + 5552, data.Length);
                for (; at < end; at++)
                {
                    a += data[at];
                    b += a;
                }
                a %= Base;
                b %= Base;
            }

            return b << 16 | a;
        }

        /// <summary>
        /// A complete PDF text string, delimiters included: a literal when every
        /// character survives PDFDocEncoding, otherwise UTF-16 in hexadecimal.
        ///
        /// A literal carries one byte per character, so anything past U+00FF cannot go
        /// in one - and the narrow no-break space this engine groups French numbers with
        /// is U+202F. Written as a literal it became a question mark, silently, in a
        /// title, a bookmark or the text a screen reader announces.
        /// </summary>
        public static string Text(string? value)
        {
            string text = value ?? string.Empty;

            bool plain = true;
            foreach (char c in text)
            {
                if (c > 0xFF || c < 0x20)
                {
                    plain = false;
                    break;
                }
            }

            if (plain)
                return "(" + Escape(text) + ")";

            // UTF-16 big-endian behind a byte order mark, which is how a PDF says the
            // string is not PDFDocEncoding.
            var sb = new StringBuilder(text.Length * 4 + 8).Append("<FEFF");
            foreach (char c in text)
                sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));

            return sb.Append('>').ToString();
        }

        /// <summary>
        /// Escapes what a literal string cannot hold as it stands. Only for text already
        /// known to fit PDFDocEncoding; <see cref="Text"/> decides that.
        /// </summary>
        public static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                if (c is '(' or ')' or '\\')
                    sb.Append('\\');
                sb.Append(c < 256 ? c : '?');
            }
            return sb.ToString();
        }

        private static string Timestamp(DateTimeOffset when)
        {
            TimeSpan offset = when.Offset;
            char sign = offset < TimeSpan.Zero ? '-' : '+';
            return string.Format(CultureInfo.InvariantCulture, "D:{0:yyyyMMddHHmmss}{1}{2:00}'{3:00}'",
                                 when.DateTime, sign, Math.Abs(offset.Hours), Math.Abs(offset.Minutes));
        }

        public static string N(double value)
            => double.IsNaN(value) || double.IsInfinity(value)
                ? "0"
                : value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
