using System.IO.Compression;

namespace Frisket.Import
{
    /// <summary>
    /// The one filter this assembly has to understand, and the predictors that come with
    /// it.
    ///
    /// One, because a page is <b>copied</b>: its content and its resources travel across
    /// still encoded, whatever they are encoded with. Flate is decoded here only where
    /// the file's own structure is written in it - cross-reference streams and object
    /// streams - because those have to be read to find anything at all.
    ///
    /// So a page full of JPEG images, JBIG2 masks and CCITT faxes imports perfectly well
    /// without a line of code for any of them.
    /// </summary>
    internal static class Filters
    {
        /// <summary>
        /// Undoes Flate, and the predictor if the stream declares one.
        ///
        /// Refuses rather than guesses: everything that calls this is reading the file's
        /// own scaffolding, and scaffolding that cannot be decoded is a file that cannot
        /// be read - which is a different thing from a page whose picture is in a format
        /// nobody here knows.
        /// </summary>
        public static byte[] Decode(PdfStream stream, Func<PdfObject?, PdfObject?> resolve)
        {
            PdfObject? filter = resolve(stream["Filter"]);
            string name = filter switch
            {
                PdfName one => one.Value,
                PdfArray many when many.Items.Count == 1
                    => (resolve(many.Items[0]) as PdfName)?.Value ?? "",
                PdfArray many when many.Items.Count == 0 => "",
                null => "",
                _ => "?"
            };

            byte[] bytes = name switch
            {
                "" => stream.Raw,
                "FlateDecode" or "Fl" => Inflate(stream.Raw),
                _ => throw new InvalidDataException(
                        $"A structural stream is filtered with {name}, which this reader does not decode.")
            };

            return Unpredict(bytes, resolve(stream["DecodeParms"]) as PdfDictionary, resolve);
        }

        /// <summary>
        /// Inflates, trying the zlib wrapper first and then a bare deflate stream.
        ///
        /// Both are met. The standard says zlib; producers that write the raw stream do
        /// exist, and a reader that only knew the well-formed one would refuse files
        /// every other reader opens.
        /// </summary>
        public static byte[] Inflate(byte[] data)
        {
            try
            {
                using var source = new MemoryStream(data, writable: false);
                using var zlib = new ZLibStream(source, CompressionMode.Decompress);
                return Drain(zlib);
            }
            catch (InvalidDataException)
            {
                using var source = new MemoryStream(data, writable: false);
                using var raw = new DeflateStream(source, CompressionMode.Decompress);
                return Drain(raw);
            }
        }

        /// <summary>
        /// How much a stream is allowed to become.
        ///
        /// A compressed stream declares nothing about its decompressed size, so this is
        /// the only bound available - and without one, forty kilobytes of zeros is four
        /// gigabytes of memory and a reader that never returns. Section 8c, met in a new
        /// place: the guard is not the count the file gives, because it gives none.
        /// </summary>
        private const long MaxInflated = 256L * 1024 * 1024;

        private static byte[] Drain(Stream stream)
        {
            var output = new MemoryStream();
            var buffer = new byte[64 * 1024];

            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                if (output.Length + read > MaxInflated)
                    throw new InvalidDataException("A stream decompressed to more than this reader will hold.");

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }

        /// <summary>
        /// Undoes the row filter a predictor applied. PNG's, which is what cross-reference
        /// streams use, and TIFF's, which is what almost nothing does.
        /// </summary>
        private static byte[] Unpredict(byte[] data, PdfDictionary? parms,
                                        Func<PdfObject?, PdfObject?> resolve)
        {
            if (parms == null)
                return data;

            int predictor = Whole(resolve(parms["Predictor"]), 1);
            if (predictor <= 1)
                return data;

            int colours = Whole(resolve(parms["Colors"]), 1);
            int bits = Whole(resolve(parms["BitsPerComponent"]), 8);
            int columns = Whole(resolve(parms["Columns"]), 1);

            if (colours <= 0 || bits <= 0 || columns <= 0)
                throw new InvalidDataException("A predictor declares a row of nothing.");

            int sample = Math.Max(1, colours * bits / 8);
            long wide = ((long)columns * colours * bits + 7) / 8;

            // Arithmetic on the length of what was actually decompressed, never on the
            // numbers alone: a row width taken at its word is how a thirty-byte file asks
            // for two gigabytes.
            if (wide <= 0 || wide > data.Length)
                throw new InvalidDataException("A predictor declares rows wider than the stream.");

            int width = (int)wide;

            if (predictor == 2)
                return bits == 8 ? UnpredictTiff(data, width, sample) : data;

            int rows = data.Length / (width + 1);
            var output = new byte[(long)rows * width <= int.MaxValue ? rows * width : 0];
            if (output.Length == 0 && rows > 0)
                throw new InvalidDataException("A predicted stream is larger than this reader will hold.");

            var previous = new byte[width];

            for (int row = 0; row < rows; row++)
            {
                int at = row * (width + 1);
                int tag = data[at];
                int from = at + 1;
                int to = row * width;

                for (int i = 0; i < width; i++)
                {
                    int raw = data[from + i];
                    int left = i >= sample ? output[to + i - sample] : 0;
                    int up = previous[i];
                    int upLeft = i >= sample ? previous[i - sample] : 0;

                    output[to + i] = (byte)(tag switch
                    {
                        0 => raw,
                        1 => raw + left,
                        2 => raw + up,
                        3 => raw + ((left + up) / 2),
                        4 => raw + Paeth(left, up, upLeft),
                        _ => raw
                    });
                }

                Buffer.BlockCopy(output, to, previous, 0, width);
            }

            return output;
        }

        private static byte[] UnpredictTiff(byte[] data, int width, int sample)
        {
            int rows = data.Length / width;
            var output = (byte[])data.Clone();

            for (int row = 0; row < rows; row++)
            {
                int at = row * width;
                for (int i = sample; i < width; i++)
                    output[at + i] = (byte)(output[at + i] + output[at + i - sample]);
            }

            return output;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int da = Math.Abs(p - a), db = Math.Abs(p - b), dc = Math.Abs(p - c);
            return da <= db && da <= dc ? a : db <= dc ? b : c;
        }

        internal static int Whole(PdfObject? value, int fallback) =>
            value is PdfNumber n && n.Value >= int.MinValue && n.Value <= int.MaxValue
                ? (int)n.Value
                : fallback;
    }
}
