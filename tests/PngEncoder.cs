using System.IO.Compression;

namespace test
{
    /// <summary>
    /// Writes 8-bit RGB PNGs, which is all the sample needs to produce a logo.
    ///
    /// This lives in the tests on purpose: the engine reads images but never makes them,
    /// and giving it an encoder would be a dependency in spirit if not in name.
    /// The output is deliberately the one shape <c>PngImage</c> can hand straight to a
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
