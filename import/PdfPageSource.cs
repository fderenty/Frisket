using System.Globalization;
using System.Text;

namespace Frisket.Import
{
    /// <summary>
    /// Reads one page out of another PDF so that it can be copied into a document this
    /// engine is writing.
    ///
    /// <para>This is the whole of the public surface. Everything behind it - the
    /// cross-reference table, object streams, the page tree - exists to answer one
    /// question: which bytes are this page, and which objects do they reach?</para>
    ///
    /// <example>
    /// <code>
    /// IImportedPage page = PdfPageSource.Read(File.ReadAllBytes("terms.pdf"), 0);
    /// </code>
    /// </example>
    /// </summary>
    public static class PdfPageSource
    {
        /// <summary>Millimetres in a PostScript point, which is what a PDF measures in.</summary>
        private const double ToMillimetres = 25.4 / 72.0;

        /// <summary>
        /// How many objects one page may drag in with it.
        ///
        /// A page's resources reach fonts, images, other forms, and whatever those reach
        /// in turn - a graph, and one written by somebody else. Sound files have no such
        /// bound and a hostile one is a file that describes more objects than it holds.
        /// </summary>
        private const int MaxObjectsPerPage = 200_000;

        /// <summary>
        /// The page at <paramref name="index"/>, counting from zero.
        ///
        /// Throws <see cref="InvalidDataException"/> for anything it cannot read, which
        /// includes an encrypted file: every string and stream in one is enciphered, so a
        /// page copied out would travel as bytes that mean nothing in their new home and
        /// would look, going past, exactly like a page that copied correctly.
        /// </summary>
        public static IImportedPage Read(byte[] pdf, int index = 0)
        {
            ArgumentNullException.ThrowIfNull(pdf);

            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index), "Pages are counted from zero.");

            PdfFile file = PdfFile.Open(pdf);
            PdfDictionary page = file.Page(index);

            return new Imported(file, page);
        }

        private sealed class Imported : IImportedPage
        {
            private readonly PdfFile _file;
            private readonly PdfDictionary _page;
            private readonly double[] _box;
            private readonly int _rotation;

            public Imported(PdfFile file, PdfDictionary page)
            {
                _file = file;
                _page = page;
                _box = Box(file, page);

                // Quarter turns, and a file may say -90 or 450 for the same thing.
                int declared = Filters.Whole(file.Resolve(page["Rotate"]), 0);
                _rotation = ((declared / 90 % 4) + 4) % 4;
            }

            private double BoxWidth => _box[2] - _box[0];
            private double BoxHeight => _box[3] - _box[1];

            // A quarter turn swaps them, which is the whole reason /Rotate is read at all:
            // a landscape page stored upright and rotated is stored with a portrait box,
            // and a reader that ignores the key imports it on its side.
            public double Width => (_rotation % 2 == 0 ? BoxWidth : BoxHeight) * ToMillimetres;
            public double Height => (_rotation % 2 == 0 ? BoxHeight : BoxWidth) * ToMillimetres;

            private static double[] Box(PdfFile file, PdfDictionary page)
            {
                double[] fallback = [0, 0, 612, 792];

                if (file.Resolve(page["MediaBox"]) is not PdfArray box || box.Items.Count < 4)
                    return fallback;

                var numbers = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    if (file.Resolve(box.Items[i]) is not PdfNumber n || double.IsNaN(n.Value)
                        || double.IsInfinity(n.Value))
                        return fallback;

                    numbers[i] = n.Value;
                }

                // Written either way round by producers, and the standard says a reader
                // must cope. Normalised here so that everything downstream can assume
                // left < right and bottom < top.
                double[] ordered =
                [
                    Math.Min(numbers[0], numbers[2]), Math.Min(numbers[1], numbers[3]),
                    Math.Max(numbers[0], numbers[2]), Math.Max(numbers[1], numbers[3])
                ];

                return ordered[2] - ordered[0] <= 0 || ordered[3] - ordered[1] <= 0
                    ? fallback
                    : ordered;
            }

            public int WriteInto(IPdfSink sink)
            {
                ArgumentNullException.ThrowIfNull(sink);

                (byte[] content, string filter) = Content();
                PdfObject? resources = _page["Resources"];

                // Every object the page reaches, gathered before a single number is
                // handed out: a copied graph has references pointing in every direction,
                // so nothing can be written until all of them are known.
                Dictionary<int, PdfObject> closure = Closure(resources);

                var map = new Dictionary<int, int>();
                foreach (int number in closure.Keys)
                    map[number] = sink.Reserve();

                int Renumber(int number) => map.GetValueOrDefault(number, 0);

                foreach ((int number, PdfObject value) in closure)
                {
                    if (value is PdfStream stream)
                        sink.SetStream(map[number], stream.DictionaryText(Renumber), stream.Raw);
                    else
                    {
                        var sb = new StringBuilder();
                        value.Write(sb, Renumber);
                        sink.Set(map[number], sb.ToString());
                    }
                }

                var dictionary = new StringBuilder("/Type /XObject /Subtype /Form /FormType 1 ");
                dictionary.Append("/BBox [0 0 ")
                          .Append(Number(_rotation % 2 == 0 ? BoxWidth : BoxHeight)).Append(' ')
                          .Append(Number(_rotation % 2 == 0 ? BoxHeight : BoxWidth)).Append("] ");
                dictionary.Append("/Matrix [").Append(Matrix()).Append("] ");

                if (resources != null)
                {
                    var sb = new StringBuilder();
                    resources.Write(sb, Renumber);
                    dictionary.Append("/Resources ").Append(sb).Append(' ');
                }

                if (filter.Length > 0)
                    dictionary.Append(filter).Append(' ');

                int form = sink.Reserve();
                sink.SetStream(form, dictionary.ToString(), content);
                return form;
            }

            /// <summary>
            /// The transform from the page's own coordinates into the form's.
            ///
            /// Two things at once: the media box is moved so its corner sits at the
            /// origin - it is very often not at zero, and a page whose box starts at
            /// (0, 842) draws entirely off the sheet without this - and the quarter turn
            /// <c>/Rotate</c> asks for is applied.
            /// </summary>
            private string Matrix()
            {
                double x0 = _box[0], y0 = _box[1];
                double w = BoxWidth, h = BoxHeight;

                // Derived rather than remembered, because a quarter turn is the kind of
                // thing that is wrong in a way nobody notices until a page arrives upside
                // down. A point (px, py) of the page becomes (x, y) of the form, and the
                // six numbers are read straight off those two lines:
                //
                //     x = a.px + c.py + e        [a b c d e f]
                //     y = b.px + d.py + f
                //
                // No turn:  x = px - x0,        y = py - y0.
                // Clockwise: the page's own left edge becomes the form's bottom, so
                //            x = py - y0,        y = w - (px - x0).
                // Half:      x = w - (px - x0),  y = h - (py - y0).
                // The other way:
                //            x = h - (py - y0),  y = px - x0.
                return _rotation switch
                {
                    1 => $"0 -1 1 0 {Number(-y0)} {Number(w + x0)}",
                    2 => $"-1 0 0 -1 {Number(w + x0)} {Number(h + y0)}",
                    3 => $"0 1 -1 0 {Number(h + y0)} {Number(-x0)}",
                    _ => $"1 0 0 1 {Number(-x0)} {Number(-y0)}"
                };
            }

            private static string Number(double value) =>
                value == Math.Floor(value) && Math.Abs(value) < 1e9
                    ? ((long)value).ToString(CultureInfo.InvariantCulture)
                    : value.ToString("0.####", CultureInfo.InvariantCulture);

            /// <summary>
            /// The page's marking instructions.
            ///
            /// One stream is copied across exactly as it lies, filter and all - decoding
            /// it to encode it again is work that can only lose. Several are concatenated,
            /// which is what <c>/Contents</c> being an array means, and that needs them
            /// decoded first: a producer splits a page's content wherever it likes,
            /// including in the middle of an operator.
            /// </summary>
            private (byte[] Bytes, string Filter) Content()
            {
                PdfObject? contents = _file.Resolve(_page["Contents"]);

                if (contents is PdfStream single)
                {
                    var sb = new StringBuilder();
                    if (single["Filter"] is { } filter)
                    {
                        sb.Append("/Filter ");
                        filter.Write(sb, n => 0);
                        if (single["DecodeParms"] is { } parms)
                        {
                            sb.Append(" /DecodeParms ");
                            parms.Write(sb, n => 0);
                        }
                    }

                    // A filter naming another object could not be copied without copying
                    // that too, and content filters do not do that in practice. Decoded
                    // rather than guessed at.
                    if (sb.ToString().Contains(" 0 R", StringComparison.Ordinal))
                        return (Filters.Decode(single, _file.Resolve), "");

                    return (single.Raw, sb.ToString());
                }

                if (contents is not PdfArray many)
                    return ([], "");

                var joined = new MemoryStream();
                foreach (PdfObject part in many.Items)
                {
                    if (_file.Resolve(part) is not PdfStream stream)
                        continue;

                    byte[] bytes = Filters.Decode(stream, _file.Resolve);
                    joined.Write(bytes, 0, bytes.Length);

                    // A newline between the parts, because the split may fall inside a
                    // token: "10 2" and "0 Td" joined without one is "10 20 Td".
                    joined.WriteByte((byte)'\n');
                }

                return (joined.ToArray(), "");
            }

            private Dictionary<int, PdfObject> Closure(PdfObject? from)
            {
                var found = new Dictionary<int, PdfObject>();
                if (from == null)
                    return found;

                var queue = new Queue<int>();
                foreach (int number in from.References())
                    queue.Enqueue(number);

                while (queue.Count > 0)
                {
                    int number = queue.Dequeue();
                    if (found.ContainsKey(number))
                        continue;

                    if (found.Count >= MaxObjectsPerPage)
                        throw new InvalidDataException(
                            "This page reaches more objects than a page has any business reaching.");

                    PdfObject? value = _file.Get(number);
                    if (value == null)
                    {
                        // A reference to something that is not there. Written as null so
                        // the file stays well formed: a dangling reference is legal PDF
                        // and reads as null anyway, and refusing the whole page over one
                        // would refuse documents every reader opens.
                        found[number] = PdfNull.Instance;
                        continue;
                    }

                    // The page's own dictionary is not resources and must not be dragged
                    // in: a font's parent chain or an annotation would otherwise pull the
                    // whole document across behind it.
                    if (value is PdfDictionary dictionary
                        && (_file.Resolve(dictionary["Type"]) as PdfName)?.Value is "Page" or "Pages")
                    {
                        found[number] = PdfNull.Instance;
                        continue;
                    }

                    found[number] = value;

                    foreach (int next in value.References())
                        queue.Enqueue(next);
                }

                return found;
            }
        }
    }
}
