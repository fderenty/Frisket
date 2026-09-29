using System.Text;

namespace Frisket
{
    /// <summary>
    /// Builds a minimal but valid ICC v2 matrix/TRC display profile for sRGB.
    ///
    /// PDF/A demands an OutputIntent carrying an embedded profile, and shipping a binary
    /// profile file alongside the library would be the dependency this engine has spent
    /// its whole life avoiding. So the profile is written out here: a header, a tag table
    /// and the nine tags a matrix/TRC RGB profile needs. The primaries are the sRGB ones
    /// already adapted to the D50 white point the specification requires of the
    /// connection space.
    ///
    /// It is not byte-identical to the profile Adobe ships - it describes the same
    /// colours through a plain 2.2 gamma rather than sRGB's piecewise curve, which is a
    /// hair different in the deepest shadows and of no consequence for a section made
    /// of black text on white paper.
    /// </summary>
    internal static class IccProfile
    {
        public static byte[] CreateSRgb()
        {
            var tags = new List<(string Signature, byte[] Data)>
            {
                ("desc", TextDescription("Frisket sRGB")),
                ("cprt", Text("Public domain")),
                ("wtpt", Xyz(0.9642, 1.0000, 0.8249)),        // D50, the profile connection space
                ("rXYZ", Xyz(0.4360, 0.2225, 0.0139)),        // sRGB primaries, Bradford-adapted to D50
                ("gXYZ", Xyz(0.3851, 0.7169, 0.0971)),
                ("bXYZ", Xyz(0.1431, 0.0606, 0.7141)),
                ("rTRC", Gamma(2.2)),
                ("gTRC", Gamma(2.2)),
                ("bTRC", Gamma(2.2))
            };

            int tableSize = 4 + tags.Count * 12;
            int offset = Align(128 + tableSize);

            var body = new MemoryStream();
            var table = new MemoryStream();
            WriteUInt32(table, (uint)tags.Count);

            foreach ((string signature, byte[] data) in tags)
            {
                WriteSignature(table, signature);
                WriteUInt32(table, (uint)offset);
                WriteUInt32(table, (uint)data.Length);

                body.Write(data);
                int padded = Align(data.Length) - data.Length;
                for (int i = 0; i < padded; i++)
                    body.WriteByte(0);

                offset += Align(data.Length);
            }

            int total = offset;
            var profile = new MemoryStream(total);
            WriteHeader(profile, total);
            profile.Write(table.ToArray());

            // Pad out to where the first tag was promised to start.
            while (profile.Length < Align(128 + tableSize))
                profile.WriteByte(0);

            profile.Write(body.ToArray());
            return profile.ToArray();
        }

        private static void WriteHeader(Stream s, int size)
        {
            WriteUInt32(s, (uint)size);
            WriteSignature(s, "ADBE");          // preferred CMM
            WriteUInt32(s, 0x02400000);         // version 2.4
            WriteSignature(s, "mntr");          // display device
            WriteSignature(s, "RGB ");
            WriteSignature(s, "XYZ ");          // profile connection space

            // Creation date, in the profile's own six-field form.
            foreach (ushort part in new ushort[] { 2026, 1, 1, 0, 0, 0 })
                WriteUInt16(s, part);

            WriteSignature(s, "acsp");
            WriteUInt32(s, 0);                  // platform
            WriteUInt32(s, 0);                  // flags: not embedded, use anywhere
            WriteUInt32(s, 0);                  // manufacturer
            WriteUInt32(s, 0);                  // model
            WriteUInt32(s, 0);                  // attributes
            WriteUInt32(s, 0);
            WriteUInt32(s, 0);                  // rendering intent: perceptual

            WriteS15Fixed16(s, 0.9642);         // the illuminant is always D50 here
            WriteS15Fixed16(s, 1.0000);
            WriteS15Fixed16(s, 0.8249);

            WriteUInt32(s, 0);                  // creator
            for (int i = 0; i < 16 + 28; i++)   // profile id, then reserved
                s.WriteByte(0);
        }

        /// <summary>The v2 description tag, which carries the same name three ways over.</summary>
        private static byte[] TextDescription(string name)
        {
            var s = new MemoryStream();
            WriteSignature(s, "desc");
            WriteUInt32(s, 0);

            byte[] ascii = Encoding.ASCII.GetBytes(name + "\0");
            WriteUInt32(s, (uint)ascii.Length);
            s.Write(ascii);

            WriteUInt32(s, 0);                  // unicode language code
            WriteUInt32(s, 0);                  // unicode count
            WriteUInt16(s, 0);                  // script code
            s.WriteByte(0);                     // macintosh description length
            for (int i = 0; i < 67; i++)        // the fixed-size macintosh field
                s.WriteByte(0);

            return s.ToArray();
        }

        private static byte[] Text(string value)
        {
            var s = new MemoryStream();
            WriteSignature(s, "text");
            WriteUInt32(s, 0);
            s.Write(Encoding.ASCII.GetBytes(value + "\0"));
            return s.ToArray();
        }

        private static byte[] Xyz(double x, double y, double z)
        {
            var s = new MemoryStream();
            WriteSignature(s, "XYZ ");
            WriteUInt32(s, 0);
            WriteS15Fixed16(s, x);
            WriteS15Fixed16(s, y);
            WriteS15Fixed16(s, z);
            return s.ToArray();
        }

        /// <summary>A curve of one entry is read as a plain gamma value.</summary>
        private static byte[] Gamma(double gamma)
        {
            var s = new MemoryStream();
            WriteSignature(s, "curv");
            WriteUInt32(s, 0);
            WriteUInt32(s, 1);
            WriteUInt16(s, (ushort)Math.Round(gamma * 256));   // u8Fixed8
            return s.ToArray();
        }

        private static int Align(int value) => (value + 3) & ~3;

        private static void WriteSignature(Stream s, string signature)
        {
            for (int i = 0; i < 4; i++)
                s.WriteByte((byte)signature[i]);
        }

        private static void WriteUInt32(Stream s, uint value)
        {
            s.WriteByte((byte)(value >> 24));
            s.WriteByte((byte)(value >> 16));
            s.WriteByte((byte)(value >> 8));
            s.WriteByte((byte)value);
        }

        private static void WriteUInt16(Stream s, ushort value)
        {
            s.WriteByte((byte)(value >> 8));
            s.WriteByte((byte)value);
        }

        private static void WriteS15Fixed16(Stream s, double value)
            => WriteUInt32(s, unchecked((uint)(int)Math.Round(value * 65536)));
    }
}
