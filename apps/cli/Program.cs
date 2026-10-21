using System.Globalization;
using System.Text.Json.Nodes;
using Frisket;

namespace Frisket.Cli
{
    /// <summary>
    /// Applies a template to a set of data and writes what comes out.
    ///
    /// The engine keeps the template and the data apart on purpose; this is the
    /// smallest thing that puts them back together from a shell, so a template can be
    /// looked at without a test being written for it, and a batch of records can be
    /// turned into a batch of sections without any code at all.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (CliError e)
            {
                Console.Error.WriteLine("frisket: " + e.Message);
                return 2;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("frisket: " + e.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Usage();
                return 0;
            }

            string verb = args[0];
            Options options = Options.Parse(args.Skip(1));

            return verb switch
            {
                "render" => Render(options),
                "check" => Check(options),
                "fonts" => Fonts(options),
                _ => throw new CliError($"unknown command '{verb}'. Try 'render', 'check' or 'fonts'.")
            };
        }

        // ------------------------------------------------------------------

        private static int Render(Options options)
        {
            string template = options.Require("template");
            Report loaded = LoadTemplate(template);

            // Every data file gives one document. With none, the template is rendered as
            // it stands - which is how a template is looked at while it is being drawn.
            List<string> data = options.DataFiles();
            if (data.Count == 0)
                data.Add("");

            FontLibrary library = Library(options);
            Directory.CreateDirectory(options.Output);

            string? splitOn = options.Maybe("split-on");

            foreach (string file in data)
            {
                string fallback = file.Length == 0
                    ? Path.GetFileNameWithoutExtension(template)
                    : Path.GetFileNameWithoutExtension(file);

                if (splitOn is not null)
                {
                    if (file.Length == 0)
                        throw new CliError("--split-on needs data to split. Pass --data.");

                    Split(template, file, splitOn, library, options, fallback);
                    continue;
                }

                Report report = file.Length == 0
                    ? loaded
                    : ReportBinder.Bind(LoadTemplate(template),
                                        File.ReadAllText(file),
                                        new BindingOptions { Numbers = Numbers(options) });

                foreach (string what in options.Formats)
                    Write(what, report, library, options, fallback);
            }

            return 0;
        }

        /// <summary>
        /// One document per record, from one data file.
        ///
        /// The record is not lifted out and made the root; the named array is cut down to
        /// the one element and everything else in the file is left where it is. So a
        /// template written to repeat over recipients produces every letter in one
        /// document without this option, and one document per letter with it - the same
        /// template, and the mode chosen here rather than by writing a second one.
        ///
        /// It also means the shared half of the data keeps working for nothing: a token
        /// resolves through the record first and falls back to the file's root, which is
        /// what already lets a table row read a currency off the invoice around it.
        /// </summary>
        private static void Split(string template, string file, string path,
                                  FontLibrary library, Options options, string fallback)
        {
            JsonNode? parsed = JsonNode.Parse(File.ReadAllText(file));
            if (parsed is not JsonObject root)
                throw new CliError($"'{file}' is not a JSON object.");

            if (Walk(root, path) is not JsonArray records)
            {
                throw new CliError(
                    $"--split-on '{path}' is not an array in '{file}'. "
                    + "A path that names nothing is a typo, and a typo that rendered one "
                    + "document instead of a hundred would look like a quiet success.");
            }

            // Counted before a single document is bound, because counting is free and
            // binding is not. A cap belongs to whoever is exposed to the outside; the
            // engine has no business holding one, since a batch of ten thousand invoices
            // is perfectly legitimate.
            if (options.Maybe("max-documents") is { } capText)
            {
                if (!int.TryParse(capText, out int cap) || cap < 0)
                    throw new CliError($"--max-documents wants a whole number, not '{capText}'.");

                if (records.Count > cap)
                    throw new CliError($"{records.Count} records, where {cap} were allowed for.");
            }

            // An empty list is an answer, not a failure: nobody to write to today.
            string? pattern = options.Maybe("name");
            var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] is not JsonObject record)
                    throw new CliError($"--split-on '{path}': record {i + 1} is not an object.");

                // The array reduced to this one element, in a copy - the original is left
                // alone so each pass starts from the same file.
                JsonObject one = root.DeepClone().AsObject();
                Replace(one, path, new JsonArray(record.DeepClone()));

                Report report = ReportBinder.Bind(LoadTemplate(template), one.ToJsonString(),
                                                  new BindingOptions { Numbers = Numbers(options) });

                string name = Unique(pattern is null
                    ? $"{fallback}-{i + 1}"
                    : Fill(pattern, record, i + 1), taken);

                foreach (string what in options.Formats)
                    Write(what, report, library, options, name);
            }
        }

        /// <summary>Follows a dotted path through an object.</summary>
        private static JsonNode? Walk(JsonObject root, string path)
        {
            JsonNode? at = root;
            foreach (string step in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (at is not JsonObject here || !here.TryGetPropertyValue(step, out JsonNode? next))
                    return null;
                at = next;
            }
            return at;
        }

        /// <summary>Puts a value at a dotted path that is already known to exist.</summary>
        private static void Replace(JsonObject root, string path, JsonNode value)
        {
            string[] steps = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            JsonObject here = root;

            for (int i = 0; i < steps.Length - 1; i++)
                here = (JsonObject)here[steps[i]]!;

            here[steps[^1]] = value;
        }

        /// <summary>
        /// The output's name, from the record. <c>{Nom}</c> and <c>{Client.Ref}</c> are
        /// read the way a template reads them; <c>{#}</c> is the record's position.
        ///
        /// Every value is reduced to what a file name may hold. A name comes out of data
        /// somebody else wrote, and a value carrying a slash or a pair of dots would be
        /// choosing which directory the document lands in - which is the same fault as a
        /// sub-template naming any file it likes, one storey down.
        /// </summary>
        private static string Fill(string pattern, JsonObject record, int position)
        {
            var name = new System.Text.StringBuilder();
            int at = 0;

            while (at < pattern.Length)
            {
                int open = pattern.IndexOf('{', at);
                int close = open < 0 ? -1 : pattern.IndexOf('}', open);

                if (open < 0 || close < 0)
                {
                    name.Append(Safe(pattern[at..]));
                    break;
                }

                name.Append(Safe(pattern[at..open]));
                string token = pattern[(open + 1)..close];

                name.Append(token == "#"
                    ? position.ToString(CultureInfo.InvariantCulture)
                    : Safe(Walk(record, token)?.ToString() ?? ""));

                at = close + 1;
            }

            string made = name.ToString().Trim();
            return made.Length == 0 ? position.ToString(CultureInfo.InvariantCulture) : made;
        }

        /// <summary>What is left of a value once it cannot name a place.</summary>
        private static string Safe(string value)
        {
            var kept = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
            {
                kept.Append(c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
                            || char.IsControl(c) || c == '.' && kept.Length > 0 && kept[^1] == '.'
                            ? '-' : c);
            }
            return kept.ToString();
        }

        /// <summary>
        /// A second record answering to the same name gets a number rather than the
        /// first one's document. Two people called Dupont is ordinary; one of their
        /// letters silently replacing the other is not.
        /// </summary>
        private static string Unique(string name, Dictionary<string, int> taken)
        {
            if (taken.TryGetValue(name, out int seen))
            {
                taken[name] = seen + 1;
                return $"{name}-{seen + 1}";
            }

            taken[name] = 1;
            return name;
        }

        private static void Write(string what, Report report, FontLibrary library,
                                  Options options, string name)
        {
            switch (what)
            {
                case "pdf":
                {
                    var pdf = new PdfBackend(library)
                    {
                        Conformance = options.Conformance,
                        Tagged = options.Tagged,
                        Subset = !options.NoSubset,
                        Language = report.Language,
                        Interactive = !options.Flat
                    };
                    ReportRenderer.Render(report, pdf);

                    string path = Path.Combine(options.Output, name + ".pdf");
                    File.WriteAllBytes(path, pdf.ToArray());
                    Say(path);
                    break;
                }

                case "html":
                {
                    var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
                    ReportRenderer.Render(report, svg);

                    string path = Path.Combine(options.Output, name + ".html");
                    File.WriteAllText(path, svg.ToHtml(options.Bare
                        ? SvgBackend.HtmlChrome.Bare
                        : SvgBackend.HtmlChrome.Preview));
                    Say(path);
                    break;
                }

                // Not a backend, and it cannot be one: everything reaching a backend
                // has already been laid out on a page, and a message has no pages. It
                // reads the bound model directly - see FlowingHtml.
                case "mail":
                {
                    string path = Path.Combine(options.Output, name + ".mail.html");
                    File.WriteAllText(path, FlowingHtml.Write(report));
                    Say(path);
                    break;
                }

                case "svg":
                {
                    var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
                    ReportRenderer.Render(report, svg);

                    for (int i = 0; i < svg.Pages.Count; i++)
                    {
                        string path = Path.Combine(options.Output, $"{name}{i + 1}.svg");
                        File.WriteAllText(path, svg.Pages[i]);
                        Say(path);
                    }
                    break;
                }

                default:
                    throw new CliError($"unknown format '{what}'. Try pdf, html, mail or svg.");
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Reads the template and says what is wrong with it, without producing
        /// anything. Same rules as the designer shows, and the same as the engine will
        /// answer to when the document is finally checked by an outside tool.
        /// </summary>
        private static int Check(Options options)
        {
            string path = options.Require("template");
            if (!File.Exists(path))
                throw new CliError($"no template at '{path}'");

            // What the text says that the model will not read, before anything else: a
            // property misspelt - which the reader skips, so the document comes out without
            // it and nothing says so - or a text it refuses outright, which has nothing more
            // to say once refused.
            var issues = new List<TemplateIssue>(TemplateJson.Check(File.ReadAllText(path)));
            if (issues.Any(i => i.Rule == "read.invalid"))
                return Tell(issues, options);

            Report report = LoadTemplate(path);

            // Capable is always on, and is not what --accessible turns on: the two answer
            // different questions. --accessible asks whether the template is fit for a
            // reader; this asks whether the output being aimed at can carry what the
            // template already says, which is worth knowing either way.
            TemplateProfile profile = TemplateProfile.Capable
                                    | (options.Accessible ? TemplateProfile.All : TemplateProfile.Sound);

            // The same two flags that decide what `render` writes, read here to say what
            // that output would lose. They default the way rendering defaults, not the way
            // that would keep this quiet: `frisket render` writes an untagged file unless
            // told otherwise, so a template declaring a language really does lose it, and
            // saying so is the whole point.
            var target = new RenderTarget(Tagged: options.Tagged, Interactive: !options.Flat,
                                          Paged: options.Paged);
            issues.AddRange(TemplateValidator.Check(report, profile, target));

            // Every token and every path into the data that the host's data will not answer,
            // when the host says what it holds - the catalogue the content editor is handed.
            if (options.Maybe("fields") is { } catalogue)
            {
                if (!File.Exists(catalogue))
                    throw new CliError($"no field catalogue at '{catalogue}'");
                issues.AddRange(TemplateFields.Check(report, File.ReadAllText(catalogue)));
            }

            return Tell(issues, options);
        }

        /// <summary>
        /// The findings, for a person or - with --json - for a program, and the exit code
        /// a build reads either way.
        /// </summary>
        private static int Tell(IReadOnlyList<TemplateIssue> issues, Options options)
        {
            int errors = issues.Count(i => i.Level == IssueLevel.Error);

            if (options.Json)
            {
                // As UTF-8 bytes, straight onto standard output. Through Console it went by the
                // console's code page, which on Windows turns "›" into ">" - redirected to a
                // file or not - and would do the same to an accented label: harmless on a
                // screen, wrong in what a program reads. The console's own code page is left
                // alone, since changing it outlives this process in the window it ran in.
                using Stream stdout = Console.OpenStandardOutput();
                stdout.Write(System.Text.Encoding.UTF8.GetBytes(TemplateIssue.ToJson(issues) + "\n"));
                return errors > 0 ? 3 : 0;
            }

            foreach (TemplateIssue issue in issues)
            {
                Console.WriteLine($"{issue.Level.ToString().ToLowerInvariant(),-7} {issue.Rule,-22} {issue.Where}");
                Console.WriteLine($"        {issue.Message}");
            }

            Console.WriteLine(issues.Count == 0
                ? "nothing to report"
                : $"{errors} error(s), {issues.Count - errors} warning(s)");

            // An error is worth a non-zero exit: this belongs in a build as much as in a
            // shell, and a build only notices what changes its exit code.
            return errors > 0 ? 3 : 0;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// What the library holds, and what the engine measured in it. A template names
        /// families by name, so a list of the names that will actually resolve saves the
        /// usual round of a section silently rendered in the fallback face.
        ///
        /// The cap height is shown because it is measured from the outline rather than
        /// read from the font's own tables, which are wrong often enough not to trust.
        /// </summary>
        private static int Fonts(Options options)
        {
            FontLibrary library = Library(options);

            foreach (FontFile font in library.Fonts
                                             .OrderBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
                                             .ThenBy(f => f.Bold).ThenBy(f => f.Italic))
            {
                string face = (font.Bold ? " bold" : "") + (font.Italic ? " italic" : "");
                Console.WriteLine($"{font.Family + face,-34} cap {font.CapHeight:0.000}"
                                  + (font.CapHeightIsEstimated ? "  (estimated)" : ""));
            }

            // Where these came from and which one unnamed text gets - on standard error, so
            // the list itself stays one face per line for whoever reads it with a script.
            Console.Error.WriteLine();
            Console.Error.WriteLine((options.NamesFonts ? "From the fonts named." : "Bundled with Frisket.")
                                    + $" Text that names no family: {library.DefaultFamily}.");

            // A file that never made it into the list above, because another claimed the
            // same family, weight and slant. Said out loud here because nothing else
            // would ever say it: the document still comes out, in a cut nobody asked
            // for, and only the line breaks are different.
            if (library.Clashes.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Two files claimed one name, so one of them cannot be reached:");
                foreach (FontClash clash in library.Clashes)
                    Console.Error.WriteLine($"  {clash}");
            }

            return 0;
        }

        private static Report LoadTemplate(string path)
        {
            if (!File.Exists(path))
                throw new CliError($"no template at '{path}'");

            // Resolves every SubTemplateItem the file contains, each relative to where it
            // is - which is the whole reason to load from a path rather than from text.
            Report report = Report.FromFile(path);

            // And every page it imports out of another PDF, for the same reason and with
            // the same boundary: the folder the template sits in, and no higher.
            //
            // Handed a reader rather than reaching for one - the engine has no parser and
            // is not to grow one. This is where a program that has a file system supplies
            // what a library deliberately does not carry.
            Frisket.ImportedPages.Resolve(report, new Frisket.Import.PdfPageImporter(),
                Path.GetDirectoryName(Path.GetFullPath(path)) ?? "");

            return report;
        }

        private static FontLibrary Library(Options options)
        {
            var library = new FontLibrary();

            // Nothing named: the library is left empty, and an empty library draws with the
            // typefaces bundled with Frisket. Named: only what was named, and a name that
            // finds nothing is an error rather than a quiet fall back to those.
            if (!options.NamesFonts)
            {
                if (options.DefaultFamily is { Length: > 0 } wanted)
                    library.DefaultFamily = wanted;
                return library;
            }

            int found = 0;

            // A file named one by one is refused out loud where a directory scan would
            // step over it: the scan was never asked about that file in particular, and
            // this was asked about it and nothing else. Registering is the check - there
            // is no cheaper one worth having, since getting through it is what proves the
            // tables are there and the 'H' can be measured.
            foreach (string file in options.FontFiles())
            {
                if (!File.Exists(file))
                    throw new CliError($"no font file at '{file}'");

                try
                {
                    library.Register(file);
                    found++;
                }
                catch (InvalidDataException why)
                {
                    throw new CliError($"'{file}' is not a font this engine can read: {why.Message}");
                }
            }

            foreach (string dir in options.FontDirectories())
            {
                if (!Directory.Exists(dir))
                    throw new CliError($"no font directory at '{dir}'");
                found += library.RegisterDirectory(dir);
            }

            if (found == 0)
                throw new CliError("no fonts found. Point --fonts at a directory holding some.");

            if (options.DefaultFamily is { Length: > 0 })
                library.DefaultFamily = options.DefaultFamily;

            return library;
        }

        private static System.Globalization.NumberFormatInfo Numbers(Options options)
            => options.Numbers switch
            {
                "french" => NumberFormats.French,
                "invariant" => NumberFormats.Invariant,
                var other => throw new CliError($"unknown number format '{other}'. Try french or invariant.")
            };

        private static void Say(string path) => Console.WriteLine(path);

        private static void Usage()
        {
            Console.WriteLine("""
            frisket - applies a template to data and writes the document

              frisket render -t <template.json> [options]
              frisket check  -t <template.json> [--accessible] [--tagged] [--flat] [--fields <catalogue.json>] [--json]
              frisket fonts  [--fonts <dir>]

            Choosing what goes in

              -t, --template <file>   the template. Required.
              -d, --data <file|dir>   a data file, or a directory of them. Repeatable.
                                      With none, the template is rendered as it stands.
              -o, --out <dir>         where to write. Default: out

            Mail merge

              --split-on <path>       one document per record of that array, instead of
                                      one document holding all of them. The array is cut
                                      down to one element and the rest of the file is
                                      left alone, so the same template serves both: what
                                      is shared stays readable, since a token falls back
                                      to the file's root.
              --name <pattern>        what to call each one: --name "{Nom}-{Client.Ref}"
                                      {#} is the record's position. Anything that could
                                      name a directory is reduced to a dash, and a
                                      repeated name gets a number rather than replacing
                                      the document already written.
                                      Default: the data file's name and a number.
              --max-documents <n>     refuse before binding anything if there are more
                                      records than that. A cap belongs to whoever is
                                      exposed to the outside, which is why the engine
                                      does not hold one.
              -f, --format <list>     pdf, html, mail, svg, or several: --format pdf,html
                                      Default: pdf

                                      html is the pages themselves, drawn as SVG with the
                                      fonts embedded - open it and see exactly what the
                                      printer will do.

                                      mail is the other thing entirely: HTML that reflows,
                                      for a message rather than a page. Tables for layout,
                                      styles inline, no embedded fonts, no form controls.
                                      Run `frisket check --flat` first to be told what a
                                      template loses on the way there.

            Fonts

                  --fonts <dir>       a directory of font files. Repeatable.
                                      Default: the typefaces bundled with Frisket -
                                      `frisket fonts` lists them. Naming any font,
                                      here or with --font, means only those are used.
                  --default-font <family>
                                      what to use where a template names no family
                                      at all. A family it does name and that is
                                      missing is an error, never a substitution:
                                      another face would relay out every line.
                                      Default: Mulish, with the bundled typefaces; with
                                      --fonts, the family the directory names in
                                      default-family.txt, or else the first it holds.

            The PDF

                  --conformance <none|pdfa3b>   what the file claims to be. Default: none
                  --tagged                      write the structure PDF/UA needs
                  --flat                        draw form fields instead of making them fill-in

                                                Both are read by `check` as well, where they
                                                describe the output you are aiming at: it then
                                                reports what the template would lose being
                                                written that way, which nothing else can see -
                                                the page looks the same either way.
                  --no-subset                   embed whole fonts rather than the glyphs used

            The HTML

                  --bare                pages only, no grey surround - for an iframe

            Data

                  --numbers <french|invariant>  how numbers are written. Default: invariant

            Examples

              frisket render -t note.json -d data/ -o out --format pdf,html
              frisket check  -t note.json --accessible
              frisket check  -t note.json --flat        # what flattening would cost
              frisket check  -t note.json --format mail # what a message cannot carry
            """);
        }
    }

    internal sealed class CliError(string message) : Exception(message);
}
