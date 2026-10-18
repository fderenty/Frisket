using Frisket;

namespace Frisket.Cli
{
    /// <summary>
    /// The command line, read once into something the commands can ask questions of.
    ///
    /// Long and short forms both, and repeatable where repeating is the natural thing
    /// to want - several data files, several font directories. Anything unknown is an
    /// error rather than a silent no-op: a misspelt flag that changes nothing is the
    /// kind of thing somebody chases for an hour.
    /// </summary>
    internal sealed class Options
    {
        private readonly Dictionary<string, string> _single = new(StringComparer.Ordinal);
        private readonly List<string> _data = new();
        private readonly List<string> _fonts = new();
        private readonly List<string> _fontFiles = new();
        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        public static Options Parse(IEnumerable<string> args)
        {
            var options = new Options();
            var queue = new Queue<string>(args);

            while (queue.Count > 0)
            {
                string arg = queue.Dequeue();

                string Value(string name) => queue.Count > 0 && !queue.Peek().StartsWith('-')
                    ? queue.Dequeue()
                    : throw new CliError($"--{name} needs a value");

                switch (arg)
                {
                    case "-t" or "--template": options._single["template"] = Value("template"); break;
                    case "-o" or "--out": options._single["out"] = Value("out"); break;
                    case "-f" or "--format": options._single["format"] = Value("format"); break;
                    case "-d" or "--data": options._data.Add(Value("data")); break;
                    case "--fonts": options._fonts.Add(Value("fonts")); break;
                    case "--font": options._fontFiles.Add(Value("font")); break;
                    case "--default-font": options._single["default-font"] = Value("default-font"); break;
                    case "--conformance": options._single["conformance"] = Value("conformance"); break;
                    case "--numbers": options._single["numbers"] = Value("numbers"); break;
                    case "--split-on": options._single["split-on"] = Value("split-on"); break;
                    case "--name": options._single["name"] = Value("name"); break;
                    case "--max-documents": options._single["max-documents"] = Value("max-documents"); break;

                    case "--tagged" or "--flat" or "--no-subset" or "--bare" or "--accessible":
                        options._flags.Add(arg.TrimStart('-'));
                        break;

                    default:
                        throw new CliError($"unknown option '{arg}'");
                }
            }

            return options;
        }

        /// <summary>An option that may be absent, without a default standing in for it.</summary>
        public string? Maybe(string name) => _single.TryGetValue(name, out string? value) ? value : null;

        public string Require(string name) => _single.TryGetValue(name, out string? value)
            ? value
            : throw new CliError($"--{name} is required");

        public string Output => _single.GetValueOrDefault("out", "out");
        public string? DefaultFamily => _single.GetValueOrDefault("default-font");
        // Invariant by default, to match the engine itself: a narrow-space thousands
        // separator is a French typographic convention, not a neutral one, and a
        // library meant to be used from anywhere should not assume it.
        public string Numbers => _single.GetValueOrDefault("numbers", "invariant");

        public bool Tagged => _flags.Contains("tagged");
        public bool Flat => _flags.Contains("flat");

        /// <summary>
        /// Whether the output has pages. Set by asking for the mail format, since a
        /// message has none - it is not a flag of its own, because the format already
        /// says it and two ways to say one thing is one way to disagree.
        /// </summary>
        public bool Paged => !Formats.Contains("mail");
        public bool NoSubset => _flags.Contains("no-subset");
        public bool Bare => _flags.Contains("bare");
        public bool Accessible => _flags.Contains("accessible");

        public IEnumerable<string> Formats
            => _single.GetValueOrDefault("format", "pdf")
                      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(f => f.ToLowerInvariant());

        public PdfConformance Conformance => _single.GetValueOrDefault("conformance", "none") switch
        {
            "none" => PdfConformance.None,
            "pdfa3b" or "pdf/a-3b" or "3b" => PdfConformance.PdfA3b,
            var other => throw new CliError($"unknown conformance '{other}'. Try none or pdfa3b.")
        };

        /// <summary>
        /// Every data file named, with a directory standing for the *.json inside it -
        /// which is what turns a folder of records into a folder of sections.
        /// </summary>
        public List<string> DataFiles()
        {
            var files = new List<string>();

            foreach (string entry in _data)
            {
                if (Directory.Exists(entry))
                    files.AddRange(Directory.GetFiles(entry, "*.json").OrderBy(f => f, StringComparer.Ordinal));
                else if (File.Exists(entry))
                    files.Add(entry);
                else
                    throw new CliError($"no data at '{entry}'");
            }

            return files;
        }

        /// <summary>
        /// Font files named one by one, with --font. Separate from --fonts because the
        /// two want opposite treatment: a directory scan steps over what is not a font,
        /// and a file somebody named must be complained about instead.
        /// </summary>
        public IReadOnlyList<string> FontFiles() => _fontFiles;

        /// <summary>The directories named with --fonts, and nothing else.</summary>
        public IReadOnlyList<string> FontDirectories() => _fonts;

        /// <summary>
        /// Whether any font was named at all. When none was, the engine draws with the
        /// typefaces bundled with Frisket - which is what replaced walking up from the
        /// executable looking for an engine/Fonts directory: true inside the repository and
        /// false everywhere a CLI is actually installed, so the one way of running it that
        /// mattered was the one that failed.
        /// </summary>
        public bool NamesFonts => _fonts.Count > 0 || _fontFiles.Count > 0;
    }
}
