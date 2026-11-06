namespace Frisket
{
    /// <summary>
    /// Just enough of the PNG container to hand a PDF its pixels.
    ///
    /// Nothing is decoded: PNG's per-row filtering is exactly PDF's Predictor 15, and
    /// both use Deflate, so the compressed data crosses over untouched. That keeps the
    /// engine free of an image library, and costs nothing in quality since no pixel is
    /// ever recompressed. A caller who brings a better compressor has the rows inflated and
    /// deflated again (<see cref="Rows"/>), which changes the size and never a pixel.
    ///
    /// An alpha channel is the exception, since PDF keeps it apart in a soft mask: such an
    /// image is decoded once and split, by <see cref="Planes"/>. It needs nothing beyond the
    /// framework's zlib, which writing a PDF already leans on. Interlaced images and palettes
    /// stay out, for want of anybody who has needed them.
    /// </summary>
    internal readonly struct PngImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public int BitDepth { get; init; }
        public int Colors { get; init; }
        public string ColorSpace { get; init; }

        /// <summary>The concatenated IDAT payload, still Deflate-compressed.</summary>
        public byte[] Data { get; init; }

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// The most decoded bytes one image may come to: a little over a hundred megapixels
        /// with their alpha. Past that the memory is the hazard, whoever's fault it is.
        /// </summary>
        private const long MostDecoded = 512L * 1024 * 1024;

        /// <summary>
        /// How far Deflate can shrink anything, by its own format. An image claiming to hold
        /// more than this over its data is lying about its size, and believing the size is
        /// exactly what section 8c says a parser has no right to do.
        /// </summary>
        private const long MostInflation = 1032;

        /// <summary>The second plane exists: gray or RGB with alpha, which is split on the way in.</summary>
        public bool HasAlpha { get; init; }

        public static bool TryRead(byte[] data, out PngImage image) => TryRead(data, out image, out _);

        /// <summary>Reads the header. <paramref name="reason"/> says what stopped it, in a sentence someone can act on.</summary>
        public static bool TryRead(byte[] data, out PngImage image, out string reason)
        {
            image = default;
            reason = "This is not a PNG that could be read.";

            if (data.Length < 8 + 25)
                return false;
            for (int i = 0; i < Signature.Length; i++)
            {
                if (data[i] != Signature[i])
                    return false;
            }

            int width = 0, height = 0, bitDepth = 0, colourType = -1;
            var payload = new List<byte[]>();

            for (int at = 8; at + 8 <= data.Length;)
            {
                int length = ReadInt(data, at);

                // Subtraction, never addition. Written as at + 12 + length this
                // overflows for a length near int.MaxValue, comes out negative, and
                // sails past a test meant to stop exactly that - after which
                // new byte[length] asks for two gigabytes on behalf of a thirty-byte
                // file. Found by a fuzzer, as an OutOfMemory that nothing was catching.
                if (length < 0 || length > data.Length - at - 12)
                    return false;

                string type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);
                int body = at + 8;

                switch (type)
                {
                    case "IHDR":
                        // A header is thirteen bytes. The guard above only proved the
                        // chunk fits the file, so a chunk claiming to be shorter than a
                        // header still had its thirteen bytes read - off the end, when it
                        // sat near it.
                        if (length < 13)
                            return false;

                        width = ReadInt(data, body);
                        height = ReadInt(data, body + 4);
                        bitDepth = data[body + 8];
                        colourType = data[body + 9];
                        if (data[body + 10] != 0)
                            return false;
                        if (data[body + 12] != 0)
                        {
                            reason = "This PNG is interlaced, which is not taken: save it without interlacing.";
                            return false;
                        }
                        break;

                    case "IDAT":
                        var chunk = new byte[length];
                        Array.Copy(data, body, chunk, 0, length);
                        payload.Add(chunk);
                        break;

                    case "IEND":
                        at = data.Length;
                        continue;
                }

                at += 12 + length;
            }

            if (width <= 0 || height <= 0 || payload.Count == 0)
                return false;

            // Gray and RGB pass through untouched; with alpha they are split, later.
            (int colors, string space) = colourType switch
            {
                0 or 4 => (1, "/DeviceGray"),
                2 or 6 => (3, "/DeviceRGB"),
                _ => (0, string.Empty)
            };

            if (colors == 0)
            {
                reason = "This PNG has a palette, which is not taken: save it as RGB, with or without alpha.";
                return false;
            }

            if (bitDepth != 8)
            {
                reason = $"This PNG has {bitDepth} bits to a channel, and 8 are taken.";
                return false;
            }

            int total = payload.Sum(p => p.Length);
            var joined = new byte[total];
            int offset = 0;
            foreach (byte[] chunk in payload)
            {
                Buffer.BlockCopy(chunk, 0, joined, offset, chunk.Length);
                offset += chunk.Length;
            }

            bool alpha = colourType is 4 or 6;
            if (alpha)
            {
                long decoded = height * (1L + width * (colors + 1L));
                if (decoded > MostDecoded)
                {
                    reason = $"This PNG is {width} by {height} with alpha, too large to take apart.";
                    return false;
                }

                if (decoded > total * MostInflation)
                {
                    reason = "This PNG claims to be far larger than its data could hold.";
                    return false;
                }
            }

            image = new PngImage
            {
                Width = width,
                Height = height,
                BitDepth = bitDepth,
                Colors = colors,
                ColorSpace = space,
                Data = joined,
                HasAlpha = alpha
            };
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// The colours and the alpha apart, uncompressed. The alpha is null when every pixel is
        /// opaque, which is what a canvas exports more often than not: an image with nothing to
        /// mask should not carry a mask. Spoiled data throws
        /// <see cref="InvalidDataException"/>, like everything else a bad file does here.
        /// </summary>
        public (byte[] Colour, byte[]? Alpha) Planes()
        {
            int channels = Colors + 1;
            int stride = Width * channels;
            byte[] raw = Inflated(Height * (1L + stride));

            Unfilter(raw, stride, channels);

            var colour = new byte[Height * (long)(Width * Colors)];
            var alpha = new byte[Height * (long)Width];
            bool opaque = true;
            int c = 0, a = 0;

            for (int y = 0; y < Height; y++)
            {
                int at = y * (stride + 1) + 1;
                for (int x = 0; x < Width; x++, at += channels)
                {
                    for (int k = 0; k < Colors; k++)
                        colour[c++] = raw[at + k];

                    byte value = raw[at + Colors];
                    alpha[a++] = value;
                    opaque &= value == 255;
                }
            }

            return (colour, opaque ? null : alpha);
        }

        /// <summary>
        /// An opaque image's rows inflated, each still behind its filter byte - which is what
        /// PDF's Predictor 15 reads - so a caller's compressor can deflate them again
        /// (<see cref="PdfBackend.Compressor"/>). No pixel changes: the filters stay as the
        /// file chose them, and only the deflating is done over.
        ///
        /// Null when the image is larger than this inflates, or does not inflate to what its
        /// header says. Its data then crosses as it came, as it always has: a compressor makes
        /// a file smaller, and must not make a picture that was taken one that is refused.
        /// </summary>
        public byte[]? Rows()
        {
            long size = Height * (1L + (long)Width * Colors);
            if (HasAlpha || size > MostDecoded || size > Data.LongLength * MostInflation)
                return null;

            // InvalidDataException for data that is not deflate, ZLibException - an IOException -
            // for an error code zlib returns instead.
            try
            {
                return Inflated(size);
            }
            catch (Exception spoiled) when (spoiled is InvalidDataException or IOException)
            {
                return null;
            }
        }

        /// <summary>The IDAT data inflated to exactly <paramref name="size"/> bytes, or <see cref="InvalidDataException"/>.</summary>
        private byte[] Inflated(long size)
        {
            var raw = new byte[size];

            using var zlib = new System.IO.Compression.ZLibStream(
                new MemoryStream(Data), System.IO.Compression.CompressionMode.Decompress);

            int filled = 0;
            while (filled < raw.Length)
            {
                int got = zlib.Read(raw, filled, raw.Length - filled);
                if (got == 0)
                    throw new InvalidDataException("This PNG holds fewer pixels than it says.");
                filled += got;
            }

            return raw;
        }

        /// <summary>Undoes PNG's five row filters, in place.</summary>
        private void Unfilter(byte[] raw, int stride, int channels)
        {
            for (int y = 0; y < Height; y++)
            {
                int row = y * (stride + 1);
                byte filter = raw[row];
                int at = row + 1;
                int above = at - (stride + 1);          // only read when y > 0

                for (int i = 0; i < stride; i++)
                {
                    int left = i >= channels ? raw[at + i - channels] : 0;
                    int up = y > 0 ? raw[above + i] : 0;
                    int corner = y > 0 && i >= channels ? raw[above + i - channels] : 0;

                    raw[at + i] += filter switch
                    {
                        0 => (byte)0,
                        1 => (byte)left,
                        2 => (byte)up,
                        3 => (byte)((left + up) / 2),
                        4 => Paeth(left, up, corner),
                        _ => throw new InvalidDataException($"This PNG has a row filter numbered {filter}.")
                    };
                }
            }
        }

        private static byte Paeth(int left, int up, int corner)
        {
            int estimate = left + up - corner;
            int toLeft = Math.Abs(estimate - left);
            int toUp = Math.Abs(estimate - up);
            int toCorner = Math.Abs(estimate - corner);

            return (byte)(toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner);
        }

        private static int ReadInt(byte[] data, int at)
            => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
    }
}
