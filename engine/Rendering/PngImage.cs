namespace Frisket
{
    /// <summary>
    /// Just enough of the PNG container to hand a PDF its pixels.
    ///
    /// Nothing is decoded: PNG's per-row filtering is exactly PDF's Predictor 15, and
    /// both use Deflate, so the compressed data crosses over untouched. That keeps the
    /// engine free of an image library, and costs nothing in quality since no pixel is
    /// ever recompressed.
    ///
    /// Interlaced images, palettes and alpha channels are out: they would need real
    /// decoding, and an alpha channel needs splitting into a separate soft mask.
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

        public static bool TryRead(byte[] data, out PngImage image)
        {
            image = default;

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
                        if (data[body + 10] != 0 || data[body + 12] != 0)
                            return false;                       // compression or interlacing we cannot pass through
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

            // Grayscale and truecolour only: a palette or an alpha channel would have to
            // be taken apart, which means decoding.
            (int colors, string space) = colourType switch
            {
                0 => (1, "/DeviceGray"),
                2 => (3, "/DeviceRGB"),
                _ => (0, string.Empty)
            };

            if (colors == 0 || bitDepth != 8)
                return false;

            int total = payload.Sum(p => p.Length);
            var joined = new byte[total];
            int offset = 0;
            foreach (byte[] chunk in payload)
            {
                Buffer.BlockCopy(chunk, 0, joined, offset, chunk.Length);
                offset += chunk.Length;
            }

            image = new PngImage
            {
                Width = width,
                Height = height,
                BitDepth = bitDepth,
                Colors = colors,
                ColorSpace = space,
                Data = joined
            };
            return true;
        }

        private static int ReadInt(byte[] data, int at)
            => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
    }
}
