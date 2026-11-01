using System.IO.Compression;

namespace test
{
    /// <summary>
    /// Writes 8-bit RGB PNGs, which is all the sample needs to produce a logo.
    ///
    /// This lives in the tests on purpose: the engine reads images but never makes them,
    /// and giving it an encoder would be a dependency in spirit if not in name.
    /// <see cref="Rgb"/> is deliberately the one shape <c>PngImage</c> can hand straight to a
    /// PDF - no interlacing, no palette, no alpha - so the sample exercises that path.
    /// </summary>
    internal static class PngEncoder
    {
        public static byte[] Rgb(int width, int height, Func<int, int, (byte R, byte G, byte B)> shade)
        {
            // One filter byte per row, then the pixels. Filter 0 means "no prediction",
            // which keeps the writer trivial and costs a little size.
            var raw = new byte[height * (1 + width * 3)];
            int at = 0;

            for (int y = 0; y < height; y++)
            {
                raw[at++] = 0;
                for (int x = 0; x < width; x++)
                {
                    (byte r, byte g, byte b) = shade(x, y);
                    raw[at++] = r;
                    raw[at++] = g;
                    raw[at++] = b;
                }
            }

            using var output = new MemoryStream();
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            var header = new byte[13];
            WriteInt(header, 0, width);
            WriteInt(header, 4, height);
            header[8] = 8;      // bit depth
            header[9] = 2;      // colour type 2: truecolour
            header[10] = 0;     // deflate
            header[11] = 0;     // adaptive filtering
            header[12] = 0;     // not interlaced
            Chunk(output, "IHDR", header);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            Chunk(output, "IDAT", compressed.ToArray());

            Chunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        /// <summary>
        /// RGBA, or gray with alpha, with every row filter PNG has, in turn. An image written with
        /// filter 0 alone would pass a decoder whose Paeth was wrong.
        /// </summary>
        public static byte[] WithAlpha(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> shade,
                                       bool gray = false, bool badFilter = false)
        {
            int channels = gray ? 2 : 4;
            int stride = width * channels;
            var pixels = new byte[height, stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    (byte r, byte g, byte b, byte a) = shade(x, y);
                    byte[] values = gray ? new[] { r, a } : new[] { r, g, b, a };
                    for (int k = 0; k < channels; k++)
                        pixels[y, x * channels + k] = values[k];
                }
            }

            var raw = new byte[height * (1 + stride)];
            int at = 0;

            for (int y = 0; y < height; y++)
            {
                int filter = y % 5;
                raw[at++] = badFilter && y == height / 2 ? (byte)9 : (byte)filter;

                for (int i = 0; i < stride; i++)
                {
                    int left = i >= channels ? pixels[y, i - channels] : 0;
                    int up = y > 0 ? pixels[y - 1, i] : 0;
                    int corner = y > 0 && i >= channels ? pixels[y - 1, i - channels] : 0;

                    int estimate = left + up - corner;
                    int toLeft = Math.Abs(estimate - left), toUp = Math.Abs(estimate - up), toCorner = Math.Abs(estimate - corner);
                    int paeth = toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;

                    int predicted = filter switch
                    {
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => paeth,
                        _ => 0
                    };

                    raw[at++] = (byte)(pixels[y, i] - predicted);
                }
            }

            return Assemble(width, height, gray ? (byte)4 : (byte)6, 0, raw);
        }

        /// <summary>How many of the five row filters an image with alpha uses, read back from its own bytes.</summary>
        public static int FiltersUsed(byte[] png)
        {
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int channels = png[25] == 4 ? 2 : 4;

            int length = (png[33] << 24) | (png[34] << 16) | (png[35] << 8) | png[36];
            using var inflate = new ZLibStream(new MemoryStream(png, 41, length), CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            byte[] raw = output.ToArray();

            var used = new HashSet<byte>();
            for (int row = 0; row < raw.Length; row += 1 + width * channels)
                used.Add(raw[row]);
            return used.Count;
        }

        /// <summary>The same file claiming another colour type or interlacing, with a checksum to match.</summary>
        public static byte[] WithHeader(byte[] png, byte? colourType = null, byte? interlace = null, byte? depth = null)
        {
            byte[] copy = (byte[])png.Clone();
            if (colourType is { } type) copy[25] = type;
            if (depth is { } bits) copy[24] = bits;
            if (interlace is { } way) copy[28] = way;

            byte[] body = new byte[17];
            Buffer.BlockCopy(copy, 12, body, 0, 17);
            uint crc = Crc32(body);
            WriteInt(copy, 29, unchecked((int)crc));
            return copy;
        }

        private static byte[] Assemble(int width, int height, byte colourType, byte interlace, byte[] raw)
        {
            using var output = new MemoryStream();
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            var header = new byte[13];
            WriteInt(header, 0, width);
            WriteInt(header, 4, height);
            header[8] = 8;
            header[9] = colourType;
            header[12] = interlace;
            Chunk(output, "IHDR", header);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            Chunk(output, "IDAT", compressed.ToArray());

            Chunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            stream.Write(length);

            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++)
                body[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, body, 4, data.Length);
            stream.Write(body);

            var crc = new byte[4];
            WriteInt(crc, 0, unchecked((int)Crc32(body)));
            stream.Write(crc);
        }

        private static void WriteInt(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
