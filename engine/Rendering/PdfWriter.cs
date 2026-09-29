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
    /// keeps the library free of dependencies like everything else here.
    /// </summary>
    internal sealed class PdfWriter
    {
        private readonly List<byte[]?> _objects = new();

        /// <summary>Reserves an object number to be filled in later, for forward references.</summary>
        public int Reserve()
        {
            _objects.Add(null);
            return _objects.Count;
        }

        public void Set(int number, string body) => _objects[number - 1] = Latin1(body);

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
        /// Fills a stream object whose number was reserved earlier.
        ///
        /// Separate from <see cref="AddStream"/> because an object graph copied out of
        /// another file - a page being imported - has references pointing in every
        /// direction, so every number has to be known before any body can be written.
        /// A method that reserves its own number cannot serve that.
        /// </summary>
        public void SetStream(int number, string dictionary, byte[] data, bool compress = true)
        {
            byte[] payload = data;
            string filter = string.Empty;

            if (compress && data.Length > 128)
            {
                payload = Deflate(data);
                filter = "/Filter /FlateDecode ";
            }

            var head = Latin1($"<< {dictionary} {filter}/Length {payload.Length} >>\nstream\n");
            var tail = Latin1("\nendstream");

            var body = new byte[head.Length + payload.Length + tail.Length];
            Buffer.BlockCopy(head, 0, body, 0, head.Length);
            Buffer.BlockCopy(payload, 0, body, head.Length, payload.Length);
            Buffer.BlockCopy(tail, 0, body, head.Length + payload.Length, tail.Length);

            _objects[number - 1] = body;
        }

        /// <summary>Adds a stream whose bytes are already compressed, or must not be touched.</summary>
        public int AddRawStream(string dictionary, byte[] data) => AddStream(dictionary, data, compress: false);

        public byte[] Build(int catalog, string producer, DateTimeOffset now,
                            string? title = null, string? author = null, byte[]? documentId = null,
                            string? subject = null, string? keywords = null)
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
