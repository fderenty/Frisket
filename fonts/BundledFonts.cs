using System.Reflection;

namespace Frisket
{
    /// <summary>
    /// The typefaces bundled with Frisket: what a <c>FontLibrary</c> nobody filled draws
    /// with. Mulish is the default; Elms Sans, Noto Sans Display and Glametrix are there to
    /// be named.
    ///
    /// Every face is under the SIL Open Font License 1.1 and carries its licence text with
    /// it - <see cref="Licences"/> - because a font bundled with software travels with its
    /// copyright and its licence (OFL FAQ 1.20). Embedding a face in a document, whole or
    /// as a subset, is what the licence allows without conditions (OFL FAQ 1.12), which is
    /// all the engine ever does with them.
    /// </summary>
    public static class BundledFonts
    {
        private const string Prefix = "fonts/";
        private static readonly Assembly Here = typeof(BundledFonts).Assembly;

        private static readonly Lazy<IReadOnlyList<string>> files = new(() => Named(n => n.EndsWith(".ttf") || n.EndsWith(".otf")));
        private static readonly Lazy<IReadOnlyList<string>> licences = new(() => Named(n => n.EndsWith(".txt") && n.Contains('/')));
        private static readonly Lazy<string> defaultFamily = new(ReadDefault);

        /// <summary>
        /// Every font file the package carries, as <c>family/File.ttf</c>, in a fixed order -
        /// the order a library registers them in, which decides nothing here since the
        /// default is declared rather than taken from whichever comes first.
        /// </summary>
        public static IReadOnlyList<string> Files => files.Value;

        /// <summary>The licence texts, one per family, as <c>family/OFL.txt</c>.</summary>
        public static IReadOnlyList<string> Licences => licences.Value;

        /// <summary>
        /// The family text that names none is drawn in, as the font directory the package is
        /// built from declares it (<c>default-family.txt</c>).
        /// </summary>
        public static string DefaultFamily => defaultFamily.Value;

        /// <summary>
        /// The bytes of one of <see cref="Files"/> or <see cref="Licences"/>, a fresh copy
        /// each time: a caller writing into them must not change the face every other
        /// library is drawing with.
        /// </summary>
        public static byte[] Open(string name)
        {
            using Stream stream = Here.GetManifestResourceStream(Prefix + name)
                ?? throw new FileNotFoundException($"Frisket.Fonts carries no '{name}'", name);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        private static IReadOnlyList<string> Named(Func<string, bool> wanted)
            => Here.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(n => n.Substring(Prefix.Length))
                .Where(wanted)
                .Order(StringComparer.Ordinal)
                .ToList();

        private static string ReadDefault()
        {
            // Less a byte-order mark, which File.ReadLines drops on the engine's side and
            // GetString does not - the comment line would then read as a family name.
            string text = System.Text.Encoding.UTF8.GetString(Open("default-family.txt")).TrimStart((char)0xFEFF);
            return text.Split('\n')
                       .Select(line => line.Trim())
                       .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'))
                   ?? throw new InvalidDataException("default-family.txt names no family");
        }
    }
}
