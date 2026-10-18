namespace test
{
    /// <summary>
    /// The scaffolding every other file leans on: the assertion, the three paths, the small
    /// item builders the fixtures write with, and the caption block a sample says what it
    /// demonstrates through.
    ///
    /// <b>There are no tests here, and that is deliberate.</b> A new test goes in the file
    /// whose name names its subject, and a subject with no file yet gets one - every file in
    /// this directory is `public partial class EngineTests`, so placement costs nothing and
    /// buys everything. The whole suite lived in two files called AcmeInvoice.cs and
    /// Program.cs until 24 August 2026, and the person who could not find the backdrop test
    /// was the one who had asked for the backdrop a day earlier.
    ///
    /// One class, so one xUnit collection, run in series: the tests write into the same
    /// directories under tests/output/ and would fight over them in parallel.
    /// </summary>
    public partial class EngineTests
    {
        internal const string DmsFont = "Elms Sans";

        const string InvoiceFont = "Elms Sans";

        /// <summary>
        /// Millimetres, compared to a tolerance. The first two arguments name the test and
        /// the property; the runner already knows the test, but the property is what makes
        /// a failure readable, so both are kept and the pair reads the same at every call
        /// site as it did before the move to xUnit.
        /// </summary>
        static void AssertEqual(string testName, string property, double expected, double actual, double tolerance)
        {
            double diff = Math.Abs(expected - actual);
            Assert.True(diff <= tolerance,
                $"{property}: {actual:F2} (expected {expected:F2}, diff {diff:F2} > {tolerance:F2})");
        }

        static void AssertEqual<T>(string testName, string property, T expected, T actual, double tolerance) where T : IComparable
        {
            Assert.True(EqualityComparer<T>.Default.Equals(expected, actual),
                $"{property}: {actual} (expected {expected})");
        }

        /// <summary>
        /// The repository root, found by walking up from the test binary rather than from
        /// the working directory. Started from the solution folder the tests wrote to one
        /// tests/output, started from test/ they wrote to another, and an afternoon went on
        /// comparing a file from one tree against a file from the other.
        /// </summary>
        static string RepositoryRoot()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "engine", "Fonts")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }

            throw new DirectoryNotFoundException("engine/Fonts not found above the test binary.");
        }

        /// <summary>
        /// Where every test writes: one tree, whatever the run started from. Created on
        /// the way out, so no caller has to remember to.
        /// </summary>
        static string OutputDirectory(params string[] parts)
        {
            string dir = Path.Combine(RepositoryRoot(), "tests/output");
            foreach (string part in parts)
                dir = Path.Combine(dir, part);

            Directory.CreateDirectory(dir);
            return dir;
        }

        static string FontDirectory() => Path.Combine(RepositoryRoot(), "engine", "Fonts");

        /// <summary>
        /// Writes a PDF a test leaves behind, and refuses one dated by the clock.
        ///
        /// What lands under tests/output is tracked, and a document dated "now" differs on
        /// every run - its timestamp, and the /ID that follows from it. Twelve did, so no
        /// run ever left the tree clean and a real change had to be found among twelve
        /// that were not one. <see cref="Report.GeneratedAt"/> fixes the date; this asks
        /// that it was fixed, so the next sample to forget is named by the run rather than
        /// by a diff nobody reads. A file with no date at all - a hostile fixture built by
        /// hand - is the same on every run, and passes.
        /// </summary>
        /// <summary>The date a sample says it was made, for one that has no reason to choose its own.</summary>
        static readonly DateTimeOffset SampleDate = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

        static void WriteSamplePdf(string path, byte[] pdf)
        {
            var dated = System.Text.RegularExpressions.Regex.Match(
                System.Text.Encoding.Latin1.GetString(pdf), @"/CreationDate \(D:(\d{14})");
            if (dated.Success)
            {
                DateTime written = DateTime.ParseExact(dated.Groups[1].Value, "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(Math.Abs((written - DateTime.UtcNow).TotalHours) > 36,
                    $"{Path.GetFileName(path)} is dated {written:yyyy-MM-dd HH:mm}, which is now: set "
                    + "Report.GeneratedAt, or the file changes on every run.");
            }

            File.WriteAllBytes(path, pdf);
        }

        static void SaveReport(Report report, string filename)
        {
            try
            {
                var outputDir = OutputDirectory();
                Directory.CreateDirectory(outputDir);
                
                var json = JsonSerializer.Serialize(report, SourceGenerationContext.Default.Report);
                var filePath = Path.Combine(outputDir, filename);
                File.WriteAllText(filePath, json);
                Console.WriteLine($"  💾 Saved report to: {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Failed to save report: {ex.Message}");
            }
        }

        // The printed original uses a narrow grotesque; Elms Sans is what the
        // repository ships. Swap the family here to change the whole section -
        // and only here, because every test reads this constant rather than
        // naming a face. It did not, until the typeface triage of 1 August 2026
        // dropped the old default and eighteen literals had to be found by hand.
        private static TextItem T(string text, double rem = 1, bool bold = false,
                                  HorizontalAlignment align = HorizontalAlignment.Left,
                                  Color? colour = null)
            => new()
            {
                Text = text,
                // No family here: the document's DefaultFont settles it once for all.
                Font = new() { Size = rem, Bold = bold, HorizontalAlignment = align, Color = colour }
            };

        /// <summary>A text pinned at an absolute spot inside its band.</summary>
        private static TextItem At(double left, double top, string text, double rem = 1,
                                   bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            TextItem item = T(text, rem, bold, align);
            item.Style.Location.Left = (left, Units.Millimeter);
            item.Style.Location.Top = (top, Units.Millimeter);
            return item;
        }

        private static LineItem Rule(double width = 0.2, DashStyle dash = DashStyle.Solid)
            => new()
            {
                Pen = new() { Width = width, DashStyle = dash, Color = new(60, 60, 60) },
                Style = { Location = { Dock = Dock.Left | Dock.Right } }
            };

        static TextItem Label(string text, double rem = 1, bool bold = false,
                              HorizontalAlignment align = HorizontalAlignment.Left)
            => new()
            {
                Text = text,
                Font = new() { Family = InvoiceFont, Size = rem, SizeUnit = FontSizeUnit.Rem,
                               Bold = bold, HorizontalAlignment = align }
            };

        static TextItem Block(string text, double heightMm)
            => new()
            {
                Text = text,
                Style = new() { Location = new() { Height = (heightMm, Units.Millimeter) } }
            };

        static TextItem Cell(string text) => new() { Text = text };

        /// <summary>Builds a report with a 20mm header and a 20mm footer, so the body band is 20..277.</summary>
        static Report BandedReport(string title)
        {
            var r = new Report
            {
                Title = title,
                Dimension = new() { Width = 210, Height = 297 },
                Header = new(),
                Footer = new(),
                Body = new()
            };
            r.Header!.Style.Location.Height = (20, Units.Millimeter);
            r.Footer!.Style.Location.Height = (20, Units.Millimeter);
            return r;
        }

        static IEnumerable<BaseItem> Descend(ContainerItem container)
            => container.Childs.Concat(container.Childs.OfType<ContainerItem>().SelectMany(Descend));

        static int CountOn(Page page, BaseItem source)
            => page.Items.Count(p => ReferenceEquals(p.Source, source));

        /// <summary>
        /// A value as text, deeply enough for a comparison to mean something: a style
        /// object compared by reference differs for every clone and would say nothing.
        ///
        /// Here rather than beside either caller because there were two of it within an
        /// hour - one written for a field's properties, one for every item's - and the
        /// second would not compile beside the first. Section 1, in the tests.
        /// </summary>
        static string Describe(object? value) => value switch
        {
            null => "(null)",
            string text => text,
            TextStyle f => System.Text.Json.JsonSerializer.Serialize(f),
            PenStyle p => System.Text.Json.JsonSerializer.Serialize(p),
            BrushStyle b => System.Text.Json.JsonSerializer.Serialize(b),
            Style s => System.Text.Json.JsonSerializer.Serialize(s),
            ImageSource i => Convert.ToBase64String(i.Data) + i.MediaType,
            System.Collections.IEnumerable list => string.Join("|", list.Cast<object?>().Select(Describe)),
            _ => value.ToString() ?? ""
        };

        static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        /// <summary>
        /// Says, at the head of a document, what it demonstrates and what a reader is
        /// meant to look for.
        ///
        /// It belongs in the file rather than in the test's console output, because the
        /// file is what someone opens six months later. A page of columns is a page of
        /// columns; nothing on it says which of the several things it does is the one
        /// being shown, and a sample nobody can read is a sample nobody checks.
        ///
        /// The block goes first whatever was added before it, so it can be called at any
        /// point in a build - the invoice-based samples are already assembled by the time
        /// a test gets to say what it is doing with them.
        ///
        /// Every piece asks to stay with what follows. A heading stranded at the foot of
        /// a page would be this document demonstrating a fault by accident, in the middle
        /// of demonstrating something else.
        /// </summary>
        /// <returns>
        /// What it added, so a test measuring marks on the page can leave it out. Without
        /// that, a check counting five pieces of a styled line counts the heading and the
        /// caption too, and the number it compares against stops meaning anything.
        /// </returns>
        private static BaseItem[] Demonstrates(ContainerItem body, string title, string lookFor)
        {
            var heading = new TextItem
            {
                Text = title,
                Font = new TextStyle { Size = 1.6, Bold = true },
                Role = PdfRole.Heading1
            };
            heading.Style.KeepWithNext = true;

            var rule = new LineItem
            {
                Pen = new PenStyle { Width = 0.2, Color = new Color(150, 150, 150) }
            };
            rule.Style.Location.Dock = Dock.Left | Dock.Right;
            rule.Style.Margin.Top = 1;
            rule.Style.Margin.Bottom = 1.5;
            rule.Style.KeepWithNext = true;

            var caption = new TextItem
            {
                Text = lookFor,
                Font = new TextStyle { Size = 0.95, Italic = true, Color = new Color(85, 85, 85) }
            };
            caption.Style.Margin.Bottom = 3;
            caption.Style.KeepWithNext = true;

            BaseItem[] block = { heading, rule, caption };

            BaseItem[] already = body.Childs.ToArray();
            foreach (BaseItem child in already)
                body.Remove(child);

            foreach (BaseItem piece in block)
                body.Add(piece);
            foreach (BaseItem child in already)
                body.Add(child);

            return block;
        }
    }
}
