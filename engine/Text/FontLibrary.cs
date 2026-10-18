namespace Frisket
{
    /// <summary>
    /// The fonts a report may use, keyed by family, weight and slant.
    ///
    /// Fonts are registered explicitly rather than looked up in the system: a report is
    /// laid out against precise advance widths, so a font quietly swapped for another at
    /// render time changes every line break. On a server there may be no system font at
    /// all, which is exactly when a silent substitution would do the most damage.
    ///
    /// A library nobody fills draws with the typefaces bundled with Frisket
    /// (<see cref="BundledFonts"/>, Mulish by default), so a first render never fails for
    /// want of a font nobody said was needed. The moment a caller registers one of its own,
    /// those stand aside and only what was registered is used: the two are never mixed,
    /// because a family found in one set and not the other would change typeface without
    /// a word. Filling a library is not thread-safe; reading it is, including the first read
    /// of an empty one, which stands the bundled faces in under a lock.
    /// </summary>
    public sealed class FontLibrary
    {
        private readonly Dictionary<string, FontFile> _fonts = new(StringComparer.OrdinalIgnoreCase);

        // The bundled faces, read and parsed once per process however many libraries stand
        // them in: a FontFile does not change once it is read, so one copy serves them all.
        private static readonly Lazy<IReadOnlyList<FontFile>> BundledFaces = new(() =>
            BundledFonts.Files.Select(f => FontFile.Load(BundledFonts.Open(f), "Frisket.Fonts/" + f)).ToList());

        private readonly object _gate = new();
        private volatile bool _settled;          // filled by the caller, or standing in
        private bool _standingIn;                // the bundled faces are what it holds
        private bool _defaultFromBundle;         // and the default came with them
        private string? _defaultFamily;

        /// <summary>Family used when a text does not name one.</summary>
        public string? DefaultFamily
        {
            get { StandIn(); return _defaultFamily; }
            set => _defaultFamily = value;
        }

        private readonly List<FontClash> _clashes = new();

        public IEnumerable<FontFile> Fonts
        {
            get { StandIn(); return _fonts.Values; }
        }

        /// <summary>Whether what this library holds is the bundled set, standing in.</summary>
        internal bool UsesBundledFonts
        {
            get { StandIn(); return _standingIn; }
        }

        /// <summary>The families held, by the names they were registered under.</summary>
        internal IEnumerable<string> Families()
        {
            StandIn();
            return _fonts.Keys.Select(k => k[..k.LastIndexOf('|')])
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Order(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The bundled faces, put in a library nobody filled - at its first read, so a caller
        /// that registers its own before using it never pays for them.
        /// </summary>
        private void StandIn()
        {
            if (_settled)
                return;

            lock (_gate)
            {
                if (_settled)
                    return;

                bool callerChose = _defaultFamily != null;
                foreach (FontFile face in BundledFaces.Value)
                    Add(face, null, null, null);

                if (!callerChose)
                {
                    _defaultFamily = BundledFonts.DefaultFamily;
                    _defaultFromBundle = true;
                }

                _standingIn = true;
                _settled = true;
            }
        }

        /// <summary>
        /// The caller is filling this library itself: whatever stood in steps aside, so
        /// what it registers is all there is.
        /// </summary>
        private void Own()
        {
            lock (_gate)
            {
                if (_standingIn)
                {
                    _fonts.Clear();
                    _clashes.Clear();
                    if (_defaultFromBundle)
                        _defaultFamily = null;
                    _standingIn = false;
                    _defaultFromBundle = false;
                }

                _settled = true;
            }
        }

        /// <summary>
        /// Where two files claimed one slot. Empty in the ordinary case.
        ///
        /// The library can serve only one of them, and which one is settled by the order
        /// a directory happens to enumerate in - so the caller is told rather than left
        /// with a report drawn in a cut it never asked for. Recorded, not refused: a
        /// duplicate is not a reason to fail a whole directory, and the widths are the
        /// only thing that moves, so nothing else would ever give it away.
        /// </summary>
        public IReadOnlyList<FontClash> Clashes => _clashes;

        private static string Key(string family, bool bold, bool italic)
            => string.Concat(family, "|", bold ? "b" : "", italic ? "i" : "");

        public FontLibrary Register(FontFile font, string? family = null, bool? bold = null, bool? italic = null)
        {
            Own();
            Add(font, family, bold, italic);
            return this;
        }

        private void Add(FontFile font, string? family, bool? bold, bool? italic)
        {
            string name = family ?? font.Family;
            bool isBold = bold ?? font.Bold;
            bool isItalic = italic ?? font.Italic;
            string key = Key(name, isBold, isItalic);

            if (_fonts.TryGetValue(key, out FontFile? held) && !ReferenceEquals(held, font))
                _clashes.Add(new FontClash(name, isBold, isItalic, held, font));

            _fonts[key] = font;
            _defaultFamily ??= name;
        }

        public FontFile Register(string path, string? family = null, bool? bold = null, bool? italic = null)
        {
            FontFile font = FontFile.Load(path);
            Register(font, family, bold, italic);
            return font;
        }

        /// <summary>
        /// Registers a font from its bytes, for a caller that has no file: something
        /// posted over HTTP, held in a database, or embedded in another document.
        ///
        /// It throws where <see cref="RegisterDirectory"/> would shrug. A directory scan
        /// steps over a file that is not a font because it was never asked about that
        /// file in particular; this was asked about exactly one, chosen on purpose, and
        /// a silent skip would leave somebody looking at a list their font is not in
        /// with nothing to explain why.
        ///
        /// Parsing is the check. There is no cheaper one worth having: reading the
        /// header proves the first twelve bytes look like a font, while getting through
        /// <see cref="FontFile.Load(byte[])"/> proves the tables are there, the cmap can
        /// be read and the 'H' can be measured - which is what the engine will actually
        /// need. A second, weaker reader that answered first could only ever agree with
        /// this one or contradict it.
        /// </summary>
        public FontFile Register(byte[] data, string? family = null, bool? bold = null, bool? italic = null)
        {
            FontFile font = FontFile.Load(data);
            Register(font, family, bold, italic);
            return font;
        }

        /// <summary>
        /// The file a font directory names its default family in, at its root: the first
        /// line that is neither blank nor a <c>#</c> comment is the family.
        /// </summary>
        public const string DefaultFamilyFile = "default-family.txt";

        /// <summary>
        /// Registers every font file of a directory, taking family and style from each
        /// file's own name table. Returns how many were loaded.
        ///
        /// A directory may say which family text that names none should get, in
        /// <see cref="DefaultFamilyFile"/>; otherwise the first family registered is the
        /// default. Either way a default the caller had already set is kept. The first
        /// rule alone gave the typefaces this repository ships Elms Sans Black - a face
        /// with no bold cut, so bold text was drawn exactly like the rest - for no reason
        /// but that "Black" sorts before "Bold" and "Regular".
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The directory names a default family it does not hold. A declared default that
        /// is not there would otherwise fall back to the alphabet, which is the accident
        /// the declaration exists to prevent.
        /// </exception>
        public int RegisterDirectory(string directory, bool recursive = true)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException(directory);

            // Before the default is read: a library standing in holds the bundled default,
            // which is not one the caller set and must not stop this directory declaring its own.
            Own();

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            int loaded = 0;
            bool defaultWasSet = _defaultFamily != null;

            // In a fixed order, never the one the file system happens to give - see
            // InRegistrationOrder for why that decides which typeface a document gets.
            foreach (string path in InRegistrationOrder(
                         Directory.EnumerateFiles(directory, "*.*", option), directory))
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is not (".ttf" or ".otf" or ".ttc"))
                    continue;

                try
                {
                    Register(path);
                    loaded++;
                }
                catch (InvalidDataException)
                {
                    // A file that is not a font is not a reason to fail the whole load.
                }
            }

            string declaration = Path.Combine(directory, DefaultFamilyFile);
            if (File.Exists(declaration))
            {
                string? family = File.ReadLines(declaration)
                    .Select(line => line.Trim())
                    .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));

                if (family != null)
                {
                    if (Resolve(family, false, false) == null)
                        throw new InvalidDataException(
                            $"{DefaultFamilyFile} names '{family}', which this directory holds no font of");

                    if (!defaultWasSet)
                        _defaultFamily = family;
                }
            }

            return loaded;
        }

        /// <summary>
        /// The order a directory's files are registered in, which is not a detail.
        ///
        /// The first family registered becomes <see cref="DefaultFamily"/>, and a key
        /// claimed twice goes to the file registered last - so the typeface of every text
        /// that names none, and which file wins a clash, were whatever the file system
        /// happened to list first. NTFS lists alphabetically; ext4 lists by a hash of the
        /// name. The same template came out in Elms Sans Black on Windows and in Mulish on
        /// Linux, and nothing said so: every font was a legal answer.
        ///
        /// The key is the path relative to <paramref name="directory"/> with one separator,
        /// because '\' and '/' do not sort in the same place among the other characters,
        /// and two platforms would otherwise order the same tree differently.
        /// Case-insensitive first, which is the order NTFS already gives - so what Windows
        /// produced before is exactly what it produces now - then ordinal, so two names
        /// differing only in case, which Linux allows, still have an order.
        /// </summary>
        internal static IEnumerable<string> InRegistrationOrder(IEnumerable<string> paths, string directory)
            => paths
                .Select(path => (path, key: Path.GetRelativePath(directory, path).Replace('\\', '/')))
                .OrderBy(f => f.key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.key, StringComparer.Ordinal)
                .Select(f => f.path);

        /// <summary>
        /// Finds the closest cut *within the requested family*: the exact style first,
        /// then a single-axis match, then the regular. Null when the family itself is
        /// unknown - falling back to another family would silently relayout the report.
        ///
        /// An unnamed family resolves to <see cref="DefaultFamily"/>. To keep an existing
        /// report working under a new typeface, register the file under the old name:
        /// <c>library.Register(path, family: "Helvetica")</c>.
        /// </summary>
        public FontFile? Resolve(string family, bool bold, bool italic)
        {
            StandIn();
            if (string.IsNullOrWhiteSpace(family))
                family = _defaultFamily ?? string.Empty;

            if (_fonts.TryGetValue(Key(family, bold, italic), out FontFile? exact))
                return exact;

            // Synthesising bold or italic is the renderer's business, not ours; for
            // measuring, another cut of the right family is the honest approximation.
            if (bold && italic)
            {
                if (_fonts.TryGetValue(Key(family, true, false), out FontFile? boldOnly))
                    return boldOnly;
                if (_fonts.TryGetValue(Key(family, false, true), out FontFile? italicOnly))
                    return italicOnly;
            }

            if (_fonts.TryGetValue(Key(family, false, false), out FontFile? regular))
                return regular;

            // Some families ship no regular cut at all - a "Bold Italic" only, say. Taking
            // another cut of the family the report asked for is not the substitution this
            // guards against: it is the same typeface, drawn by the same hand.
            foreach (KeyValuePair<string, FontFile> entry in _fonts)
            {
                int bar = entry.Key.LastIndexOf('|');
                if (bar > 0 && entry.Key.AsSpan(0, bar).Equals(family, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }

            return null;
        }
    }

    /// <summary>
    /// Two font files that came out with the same family, weight and slant.
    ///
    /// One of them is unreachable: no template can name it, because there is nothing
    /// left to name it with. <see cref="Kept"/> is the one a report will get - the last
    /// registered, which is to say whichever the filesystem listed last.
    /// </summary>
    public sealed record FontClash(string Family, bool Bold, bool Italic, FontFile Shadowed, FontFile Kept)
    {
        public override string ToString()
            => $"{Family}{(Bold ? " Bold" : "")}{(Italic ? " Italic" : "")}: " +
               $"{Name(Shadowed)} is hidden by {Name(Kept)}";

        // The cut, when the two differ - "Light hidden by Regular" says it at a glance.
        // When they do not, which is the commonest way to get here, saying "Regular is
        // hidden by Regular" says nothing at all, and the path is the only thing left
        // that tells one file from the other.
        private static string Name(FontFile font)
            => font.Source == "<memory>"
                 ? $"'{font.Subfamily}'"
                 : $"'{font.Subfamily}' ({System.IO.Path.GetFileName(font.Source)})";
    }

    /// <summary>Thrown when a report asks for a font the library does not hold.</summary>
    public sealed class FontNotFoundException : Exception
    {
        public string Family { get; }

        public FontNotFoundException(string family)
            : base($"No font registered for family '{family}'. Register it in the FontLibrary: " +
                   "substituting silently would change every line break in the report.")
            => Family = family;

        /// <summary>The same refusal, saying what the library does hold - the first thing
        /// anyone asks next, and the answer is usually a spelling.</summary>
        public FontNotFoundException(string family, IEnumerable<string> held)
            : base($"No font registered for family '{family}'. " +
                   $"{(held.Any() ? "This library holds " + string.Join(", ", held) : "This library holds none")}. " +
                   "Register it in the FontLibrary: substituting silently would change every " +
                   "line break in the report.")
            => Family = family;
    }
}
