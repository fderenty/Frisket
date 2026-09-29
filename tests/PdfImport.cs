using System.IO.Compression;
using Frisket.Import;

namespace test
{
    public partial class EngineTests
    {
        /// <summary>
        /// A sink that keeps what was written, so a test can ask whether the pieces of a
        /// copied page reach each other.
        ///
        /// The question this exists for is section 2's: a form that names object 41 in a
        /// file holding no object 41 is not a broken-looking file. It opens, it draws
        /// most of itself, and the missing piece is a font that quietly became a default
        /// one - which nothing but following the references catches.
        /// </summary>
        private sealed class Collected : IPdfSink
        {
            private readonly Dictionary<int, string> _bodies = new();
            private readonly Dictionary<int, byte[]> _streams = new();
            private int _next;

            public int Count => _bodies.Count;
            public IReadOnlyDictionary<int, string> Bodies => _bodies;

            public byte[] StreamOf(int number) => _streams.GetValueOrDefault(number, []);

            public int Reserve()
            {
                _next++;
                _bodies[_next] = "";
                return _next;
            }

            public void Set(int number, string body) => _bodies[number] = body;

            public void SetStream(int number, string dictionary, byte[] data, bool compress = false)
            {
                _bodies[number] = "<< " + dictionary + " >> stream";
                _streams[number] = data;
            }

            /// <summary>Every object number named by anything written.</summary>
            public List<int> Named() =>
                _bodies.Values
                    .SelectMany(b => System.Text.RegularExpressions.Regex.Matches(b, @"(\d+) 0 R"))
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .Distinct()
                    .ToList();
        }

        /// <summary>
        /// Reading a page out of another PDF, which is a copy and not a rendering.
        ///
        /// The corpus is our own output, which is the one advantage this project has over
        /// a reader written on its own: we know exactly what the file says, so "did it
        /// read correctly" is answerable rather than a matter of opening it and squinting.
        ///
        /// <b>Which page came back is asked by its size</b>, not by its content. A page
        /// tree walked wrongly hands back the first page every time, and on a document
        /// whose pages are alike that is indistinguishable from a correct read - so the
        /// three pages here are three different sheets. Reading the text back was tried
        /// first and abandoned: a PDF draws glyph indices rather than letters, the map
        /// that turns one into the other is compressed whatever the writer is told, and a
        /// check that has to decode two things to ask one question is a check with two
        /// ways to be wrong.
        ///
        /// What our own writer cannot supply is a file written the modern way - a
        /// cross-reference stream, and objects packed into an object stream - because it
        /// emits a plain table, as it has since the first version. Those two paths are
        /// most of the structural work in the reader, so the second half of this test
        /// builds such a file by hand. Section 8c from the other side: the numbers a
        /// parser trusts are where it breaks, and a corpus that never carries them proves
        /// nothing about them.
        /// </summary>
        [Fact]
        public void Test90_ReadingAPageOutOfAnotherPdf()
        {
            const string testName = "Test 90";
            Console.WriteLine("Test 90: reading a page out of another PDF");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // --- a document of our own, read back -------------------------------------
            (double W, double H)[] sheets = [(210, 297), (297, 210), (148, 210)];

            var report = new Report { Title = "Something to import", Language = "en-GB" };
            report.Sections.Clear();

            for (int i = 0; i < sheets.Length; i++)
            {
                var section = new Section { Dimension = new() { Width = sheets[i].W, Height = sheets[i].H } };
                section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };

                var body = new Body { Layout = LayoutMode.Stack };
                body.Add(new TextItem { Text = $"This is sheet {i + 1}.", Font = new() { Size = 4 } });
                section.Add(body);
                report.Sections.Add(section);
            }
            report.Reattach();

            var backend = new PdfBackend(library) { Compress = false };
            ReportRenderer.Render(report, backend);
            byte[] ours = backend.ToArray();

            File.WriteAllBytes(Path.Combine(OutputDirectory("import"), "three-sheets.pdf"), ours);

            for (int i = 0; i < sheets.Length; i++)
            {
                IImportedPage read = PdfPageSource.Read(ours, i);
                Console.WriteLine($"    page {i + 1} : {read.Width:0.##} x {read.Height:0.##} mm");

                AssertEqual(testName, $"page {i + 1} comes back its own width", sheets[i].W, read.Width, 0.05);
                AssertEqual(testName, $"page {i + 1} comes back its own height", sheets[i].H, read.Height, 0.05);
            }

            // --- and the copy is complete ---------------------------------------------
            var sink = new Collected();
            int form = PdfPageSource.Read(ours, 1).WriteInto(sink);
            byte[] content = sink.StreamOf(form);

            Console.WriteLine($"    copie : {sink.Count} objet(s), {content.Length} octets de contenu");

            AssertEqual(testName, "the page brought its marks with it", true, content.Length > 0, 0);

            var missing = sink.Named().Where(n => !sink.Bodies.ContainsKey(n)).ToList();
            AssertEqual(testName, "everything the copy names was copied with it",
                "", string.Join(", ", missing), 0);

            AssertEqual(testName, "and it brought a font across", true,
                sink.Bodies.Values.Any(b => b.Contains("/Font", StringComparison.Ordinal)), 0);

            // A landscape sheet, so the form's own box is wider than it is tall. Written
            // the other way round, everything above still passes and the page arrives on
            // its side.
            AssertEqual(testName, "and the form is the shape of the page it came from", true,
                sink.Bodies[form].Contains("/BBox [0 0 841", StringComparison.Ordinal), 0);

            // --- a file written the modern way ----------------------------------------
            byte[] modern = ModernPdf();
            File.WriteAllBytes(Path.Combine(OutputDirectory("import"), "xref-stream.pdf"), modern);

            IImportedPage packed = PdfPageSource.Read(modern, 0);
            var second = new Collected();
            int packedForm = packed.WriteInto(second);
            string packedContent = System.Text.Encoding.Latin1.GetString(second.StreamOf(packedForm));

            Console.WriteLine($"    flux xref : {packed.Width:0.##} x {packed.Height:0.##} mm, "
                            + $"contenu \"{packedContent.Trim()}\"");

            AssertEqual(testName, "a page tree inside an object stream is still a page tree",
                105, packed.Width, 0.05);
            AssertEqual(testName, "and a cross-reference stream leads to its content", true,
                packedContent.Contains("re f", StringComparison.Ordinal), 0);

            // --- a quarter turn, and a box that does not start at the origin ----------
            // Two things nothing else here touches, and both are silent when wrong: a
            // page that ignores /Rotate arrives on its side, and a media box whose
            // corner is not at zero draws its content off the sheet entirely. The second
            // is common - a great many producers write [0 842 595 0] or an offset box -
            // and a reader that assumes the origin imports a blank page.
            byte[] turned = ModernPdf("/Rotate 90 ", "20 30 317.6378 449.5276");
            IImportedPage sideways = PdfPageSource.Read(turned, 0);

            var third = new Collected();
            int turnedForm = sideways.WriteInto(third);

            Console.WriteLine($"    quart de tour : {sideways.Width:0.##} x {sideways.Height:0.##} mm, "
                            + $"{Between(third.Bodies[turnedForm], "/Matrix [", "]")}");

            AssertEqual(testName, "a quarter turn swaps the sheet, across", 148, sideways.Width, 0.05);
            AssertEqual(testName, "and down", 105, sideways.Height, 0.05);

            // The six numbers, derived in the source and checked here rather than trusted:
            // x = py - 30, y = 297.6378 - (px - 20). A turn that is wrong in one sign is
            // a page that arrives upside down, which no assertion on a size can see.
            AssertEqual(testName, "and the transform says exactly that",
                "0 -1 1 0 -30 317.6378", Between(third.Bodies[turnedForm], "/Matrix [", "]"), 0);

            // The form's box is the turned shape, in points.
            AssertEqual(testName, "and the form's box is the turned one", true,
                third.Bodies[turnedForm].Contains("/BBox [0 0 419.5276 297.6378]",
                                                  StringComparison.Ordinal), 0);

            // --- a box that does not start at the origin, and no turn -----------------
            // The common case, and the silent one: a great many producers write a media
            // box offset from zero, and a reader that assumes the origin imports a page
            // whose content is entirely off the sheet. It was only exercised through the
            // quarter turn above, so taking the translation out of the no-turn branch
            // changed nothing anywhere.
            byte[] shifted = ModernPdf("", "20 30 317.6378 449.5276");
            var fourth = new Collected();
            int shiftedForm = PdfPageSource.Read(shifted, 0).WriteInto(fourth);

            Console.WriteLine($"    boite decalee : {Between(fourth.Bodies[shiftedForm], "/Matrix [", "]")}");
            AssertEqual(testName, "an offset box is brought back to the origin",
                "1 0 0 1 -20 -30", Between(fourth.Bodies[shiftedForm], "/Matrix [", "]"), 0);

            // --- what blind mutation cannot reach -------------------------------------
            // Three faults a fuzzer will not build. Test 60 spoils the numbers of a real
            // file, which is what finds things in a font - but a classic cross-reference
            // table writes its counts as ASCII digits, so mutating bytes gives another
            // small number and the guard on it is never approached. A cycle is worse
            // still: it has to be constructed, and a random edit never constructs one.
            //
            // So they are built here. Each must come back - refusing is an answer, hanging
            // is not - and the clock is the assertion, since a guard that is merely slow
            // to give up is a denial of service with extra steps.
            (string what, byte[] bytes)[] hostile =
            [
                ("a table declaring a hundred million entries", HugeTable()),
                ("a stream whose length is itself", LengthLoop()),
                ("a page tree that contains itself", TreeLoop())
            ];

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var slow = new List<string>();

            foreach ((string what, byte[] bytes) in hostile)
            {
                clock.Restart();
                string said = Refused(() => PdfPageSource.Read(bytes, 0).WriteInto(new Collected()));
                long took = clock.ElapsedMilliseconds;

                Console.WriteLine($"    {what,-44} {took,4} ms  "
                                + (said.Length == 0 ? "lu" : "refuse"));

                if (took > 500) slow.Add($"{what} took {took} ms");
            }

            AssertEqual(testName, "a file built to be hostile is answered, not endured",
                "", string.Join("; ", slow), 0);

            // --- and what it refuses --------------------------------------------------
            AssertEqual(testName, "an encrypted file is refused rather than half-read", true,
                Refused(() => PdfPageSource.Read(Encrypted(), 0))
                    .Contains("encrypted", StringComparison.OrdinalIgnoreCase), 0);

            AssertEqual(testName, "and so is a page that is not there", true,
                Refused(() => PdfPageSource.Read(ours, 99)).Length > 0, 0);

            AssertEqual(testName, "and so is something that is not a PDF at all", true,
                Refused(() => PdfPageSource.Read([1, 2, 3], 0)).Length > 0, 0);

            Console.WriteLine();
        }

        /// <summary>A cross-reference table claiming a hundred million entries it has not got.</summary>
        private static byte[] HugeTable()
        {
            const string catalogue = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            const string pages = "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n";
            const string page = "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n";

            string head = "%PDF-1.4\n";
            int table = head.Length + catalogue.Length + pages.Length + page.Length;

            return System.Text.Encoding.Latin1.GetBytes(
                head + catalogue + pages + page
                + "xref\n0 100000000\n0000000000 65535 f \n"
                + "trailer\n<< /Size 4 /Root 1 0 R >>\n"
                + $"startxref\n{table}\n%%EOF\n");
        }

        /// <summary>A stream whose length is an indirect reference to the object it is in.</summary>
        private static byte[] LengthLoop()
        {
            const string catalogue = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            const string pages = "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n";
            const string page = "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] "
                              + "/Contents 4 0 R >>\nendobj\n";
            const string loop = "4 0 obj\n<< /Length 4 0 R >>\nstream\n0 0 10 10 re f\nendstream\nendobj\n";

            string head = "%PDF-1.4\n";
            int one = head.Length;
            int two = one + catalogue.Length;
            int three = two + pages.Length;
            int four = three + page.Length;
            int table = four + loop.Length;

            return System.Text.Encoding.Latin1.GetBytes(
                head + catalogue + pages + page + loop
                + "xref\n0 5\n0000000000 65535 f \n"
                + $"{one:D10} 00000 n \n{two:D10} 00000 n \n{three:D10} 00000 n \n{four:D10} 00000 n \n"
                + "trailer\n<< /Size 5 /Root 1 0 R >>\n"
                + $"startxref\n{table}\n%%EOF\n");
        }

        /// <summary>A page tree whose node is its own child.</summary>
        private static byte[] TreeLoop()
        {
            const string catalogue = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            const string pages = "2 0 obj\n<< /Type /Pages /Kids [2 0 R 2 0 R] /Count 9 >>\nendobj\n";

            string head = "%PDF-1.4\n";
            int one = head.Length;
            int two = one + catalogue.Length;
            int table = two + pages.Length;

            return System.Text.Encoding.Latin1.GetBytes(
                head + catalogue + pages
                + "xref\n0 3\n0000000000 65535 f \n"
                + $"{one:D10} 00000 n \n{two:D10} 00000 n \n"
                + "trailer\n<< /Size 3 /Root 1 0 R >>\n"
                + $"startxref\n{table}\n%%EOF\n");
        }

        private static string Between(string text, string after, string before)
        {
            int start = text.IndexOf(after, StringComparison.Ordinal);
            if (start < 0) return "(absent)";

            start += after.Length;
            int end = text.IndexOf(before, start, StringComparison.Ordinal);
            return end < 0 ? "(unterminated)" : text[start..end];
        }

        /// <summary>
        /// A page of another PDF, laid out and drawn in one of ours.
        ///
        /// The reader is checked next door; this is the other half - that what it read
        /// arrives in the file, is reachable from the page that draws it, and is measured
        /// like anything else on the way.
        ///
        /// The check that matters is the one section 2 keeps asking: every object the
        /// finished document names must be an object it holds. A copied graph is
        /// renumbered on the way in, and a renumbering that drops one entry produces a
        /// file that opens, draws most of itself, and is missing a font - which no
        /// assertion on the presence of anything can see.
        /// </summary>
        [Fact]
        public void Test91_AnImportedPageIsDrawnAndMeasured()
        {
            const string testName = "Test 91";
            Console.WriteLine("Test 91: an imported page, laid out and drawn");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            byte[] elsewhere = File.ReadAllBytes(
                Path.Combine(OutputDirectory("import"), "three-sheets.pdf"));

            static Report Holding(byte[] pdf, int page, params int[] more)
            {
                var report = new Report { Title = "With somebody else's page", Language = "en-GB" };
                report.Section.Dimension = new() { Width = 210, Height = 297 };
                report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
                report.Body = new() { Layout = LayoutMode.Stack };
                report.Body.Add(new TextItem { Text = "Terms attached:", Role = PdfRole.Heading1 });

                foreach (int which in new[] { page }.Concat(more))
                {
                    var brought = new ImportedPageItem
                    {
                        Data = pdf, Page = which,
                        AltText = $"Page {which} of the terms"
                    };
                    brought.Style.Location.WidthValue = 120;
                    report.Body.Add(brought);
                }

                report.Reattach();
                return report;
            }

            // --- nothing has read it yet ----------------------------------------------
            Report unread = Holding(elsewhere, 2);
            var nothing = new PdfBackend(library) { Compress = false };
            ReportRenderer.Render(unread, nothing);

            Console.WriteLine("    sans lecteur : "
                            + $"{CountOccurrences(System.Text.Encoding.Latin1.GetString(nothing.ToArray()), "/Subtype /Form")} forme(s)");

            AssertEqual(testName, "with nobody to read it, nothing is drawn and nothing breaks",
                0, CountOccurrences(System.Text.Encoding.Latin1.GetString(nothing.ToArray()),
                                    "/Subtype /Form"), 0);

            // --- and once something has --------------------------------------------------
            Report report = Holding(elsewhere, 2, 2, 3);
            ImportedPages.Resolve(report, new PdfPageImporter());

            var brought = report.AllItems.OfType<ImportedPageItem>().ToList();
            Console.WriteLine("    lues : " + string.Join(", ", brought.Select(
                b => $"p{b.Page} {b.Resolved?.Width:0.#}x{b.Resolved?.Height:0.#}")));

            AssertEqual(testName, "the page it named is the page it got", 297, brought[0].Resolved!.Width, 0.05);
            AssertEqual(testName, "and the third one is a different sheet", 148, brought[2].Resolved!.Width, 0.05);

            // Named twice, read once: the same file and page share one reading, so nine
            // sections carrying the same terms cost one copy of them rather than nine.
            AssertEqual(testName, "and the same page named twice was read once", true,
                ReferenceEquals(brought[0].Resolved, brought[1].Resolved), 0);

            // --- measured like anything else ------------------------------------------
            LayoutResult laid = report.Resolve(new FontTextMeasurer(library));
            PlacedItem placed = laid.Pages.SelectMany(p => p.Items)
                                    .First(i => i.Source == brought[0]);

            Console.WriteLine($"    placee : {placed.Frame.Width:0.#} x {placed.Frame.Height:0.#} mm");

            // 120 mm wide, and a landscape sheet is 297 x 210, so it is 120 * 210/297 tall.
            // Uniform scaling, which is the same rule and the same code as a picture.
            AssertEqual(testName, "it is given the width it asked for", 120, placed.Frame.Width, 0.5);
            AssertEqual(testName, "and the height its own proportions want",
                120 * 210.0 / 297.0, placed.Frame.Height, 0.5);

            // --- and the fact is stated -----------------------------------------------
            List<TemplateIssue> said = laid.Issues.Where(i => i.Rule == "fits.importedPage").ToList();
            Console.WriteLine($"    note : {said.Count} x {said.FirstOrDefault()?.Level}");

            AssertEqual(testName, "one note per imported page", 3, said.Count, 0);
            AssertEqual(testName, "and it is a warning, not a refusal", true,
                said.All(i => i.Level == IssueLevel.Warning), 0);

            // --- what reaches the file ------------------------------------------------
            var pdf = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b, Language = "en-GB",
                Tagged = true, Compress = false
            };
            ReportRenderer.Render(report, laid, pdf);
            byte[] bytes = pdf.ToArray();
            File.WriteAllBytes(Path.Combine(OutputDirectory("import"), "with-terms.pdf"), bytes);

            string written = System.Text.Encoding.Latin1.GetString(bytes);

            int forms = CountOccurrences(written, "/Subtype /Form");
            int drawn = CountOccurrences(written, " Do");
            Console.WriteLine($"    ecrit : {forms} forme(s), {drawn} pose(s)");

            // Two forms for three items, because two of them are the same page.
            AssertEqual(testName, "one form per page, not per item", 2, forms, 0);
            AssertEqual(testName, "and each item draws one", 3, drawn, 0);

            AssertEqual(testName, "the page's resources name the form it draws", true,
                System.Text.RegularExpressions.Regex.IsMatch(written, @"/XObject <<[^>]*/Fx1 "), 0);

            // The check this whole element is worth: everything the file names, it holds.
            var dangling = Dangling(written);
            AssertEqual(testName, "and every object the document names, it holds",
                "", string.Join(", ", dangling), 0);

            // --- and the path a template names is held to a root -----------------------
            // The path comes from the template, and a template is untrusted the moment it
            // is something a caller was handed rather than wrote. Section 8b's first
            // fault, in a new element: "..\..\secret" climbs out, and an absolute path
            // makes Path.Combine discard the left side altogether - which is the
            // semantics of that method, not a forgotten guard.
            string here = OutputDirectory("import");
            string above = Path.GetDirectoryName(Path.GetFullPath(here)) ?? here;
            File.WriteAllBytes(Path.Combine(above, "outside.pdf"), elsewhere);

            static Report Naming(string path)
            {
                var report = new Report { Title = "Reaching out", Language = "en-GB" };
                report.Section.Dimension = new() { Width = 210, Height = 297 };
                report.Body = new() { Layout = LayoutMode.Stack };
                report.Body.Add(new ImportedPageItem { SourcePath = path, AltText = "x" });
                report.Reattach();
                return report;
            }

            string Reading(string path) =>
                Refused(() => ImportedPages.Resolve(Naming(path), new PdfPageImporter(), here));

            // The control first, and it is what makes the two refusals below mean
            // something: a path INSIDE the root really is read. Without it, a guard that
            // refused everything would pass this test.
            AssertEqual(testName, "a file beside the template is read",
                "", Reading("three-sheets.pdf"), 0);

            string climbing = Reading(Path.Combine("..", "outside.pdf"));
            string rooted = Reading(Path.Combine(above, "outside.pdf"));

            Console.WriteLine($"    en remontant : {(climbing.Length > 0 ? "refuse" : "LU")}");
            Console.WriteLine($"    chemin absolu : {(rooted.Length > 0 ? "refuse" : "LU")}");

            AssertEqual(testName, "one climbing out is refused", true, climbing.Contains("outside"), 0);
            AssertEqual(testName, "and so is an absolute one", true, rooted.Contains("outside"), 0);

            // Named as the author wrote it, never as it resolved: a resolved path
            // describes the machine to whoever sent the template.
            AssertEqual(testName, "and the refusal does not describe the machine",
                false, climbing.Contains(above, StringComparison.OrdinalIgnoreCase), 0);

            // With no root at all - a server handed JSON as text - a path is refused
            // rather than resolved against the working directory.
            AssertEqual(testName, "and with no folder to resolve against, nothing is read",
                true, Refused(() => ImportedPages.Resolve(Naming("three-sheets.pdf"),
                                                          new PdfPageImporter())).Length > 0, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// What an imported page costs a conformance claim, asked of a validator rather
        /// than assumed.
        ///
        /// The rule is the author's: an import puts the claim in question, it is the
        /// responsibility of whoever uses the library, and the engine says so and refuses
        /// nothing. That is only worth stating if both halves of it are true, and neither
        /// is provable by reading the code:
        ///
        /// <list type="bullet">
        /// <item>a page out of a well-formed document costs <b>nothing</b> - so a rule
        /// that refused imports outright would be refusing correct documents;</item>
        /// <item>a page out of a document that is not PDF/A costs <b>the claim</b> - so a
        /// note that nobody wrote would be a file quietly lying about itself.</item>
        /// </list>
        ///
        /// The second is built here rather than found: a page drawing text in Helvetica
        /// with no font file behind it, which is the oldest PDF/A violation there is and
        /// the commonest thing to meet in somebody else's file. Manufacturing it is not
        /// manufacturing the answer - veraPDF gives that, and it does not know where the
        /// page came from.
        /// </summary>
        [SkippableFact]
        public void Test92_AnImportedPageCanCostTheClaim()
        {
            const string testName = "Test 92";
            Console.WriteLine("Test 92: what an imported page costs the claim");
            Console.WriteLine();

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null,
                "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH.");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            string Claimed(byte[] source, string name)
            {
                var report = new Report { Title = "A claim", Language = "en-GB" };
                report.Section.Dimension = new() { Width = 210, Height = 297 };
                report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };
                report.Body = new() { Layout = LayoutMode.Stack };
                report.Body.Add(new TextItem { Text = "Attached:", Role = PdfRole.Heading1 });

                var brought = new ImportedPageItem { Data = source, AltText = "The attached page" };
                brought.Style.Location.WidthValue = 120;
                report.Body.Add(brought);
                report.Reattach();

                ImportedPages.Resolve(report, new PdfPageImporter());

                var pdf = new PdfBackend(library)
                {
                    Conformance = PdfConformance.PdfA3b, Language = "en-GB", Tagged = true
                };
                ReportRenderer.Render(report, pdf);

                string path = Path.Combine(OutputDirectory("import"), name);
                File.WriteAllBytes(path, pdf.ToArray());
                return path;
            }

            string Says(string path, string flavour)
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(verapdf!)
                    {
                        Arguments = $"--format text --flavour {flavour} \"{path}\"",
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        UseShellExecute = false
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(180_000);
                return output.Contains("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL";
            }

            // --- a page out of a well-formed document ---------------------------------
            byte[] sound = File.ReadAllBytes(
                Path.Combine(OutputDirectory("import"), "three-sheets.pdf"));

            string kept = Claimed(sound, "claim-kept.pdf");
            Console.WriteLine($"    page bien formee : {Says(kept, "3b")} 3b, {Says(kept, "ua1")} ua1");

            AssertEqual(testName, "a page out of a sound document costs the claim nothing",
                "PASS", Says(kept, "3b"), 0);

            // --- and one out of a document that is not archivable ---------------------
            string lost = Claimed(NotArchivable(), "claim-lost.pdf");
            string verdict = Says(lost, "3b");
            Console.WriteLine($"    police non embarquee : {verdict} 3b");

            AssertEqual(testName, "and a page carrying an unembedded font costs it",
                "FAIL", verdict, 0);

            // Which is the whole argument for a note rather than a refusal: the same
            // element, the same claim, and the answer depends entirely on bytes this
            // engine did not write and cannot inspect on the author's behalf.
            Console.WriteLine();
        }

        /// <summary>
        /// A page drawing text in Helvetica with no font file behind it.
        ///
        /// The oldest PDF/A violation there is - a viewer substitutes whatever it has, so
        /// the document does not say what it will look like in twenty years, which is the
        /// one thing archiving is for. Built here because it is the commonest thing to
        /// meet in a file somebody else made, and because a file that is merely
        /// well-formed proves only half of what test 92 is about.
        /// </summary>
        private static byte[] NotArchivable()
        {
            const string catalogue = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            const string pages = "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n";
            const string page = "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] "
                              + "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n";
            const string font = "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n";
            const string marks = "BT /F1 24 Tf 20 100 Td (Somebody else's page) Tj ET\n";

            string body = $"5 0 obj\n<< /Length {marks.Length} >>\nstream\n{marks}endstream\nendobj\n";

            string head = "%PDF-1.4\n";
            int one = head.Length;
            int two = one + catalogue.Length;
            int three = two + pages.Length;
            int four = three + page.Length;
            int five = four + font.Length;
            int table = five + body.Length;

            return System.Text.Encoding.Latin1.GetBytes(
                head + catalogue + pages + page + font + body
                + "xref\n0 6\n0000000000 65535 f \n"
                + $"{one:D10} 00000 n \n{two:D10} 00000 n \n{three:D10} 00000 n \n"
                + $"{four:D10} 00000 n \n{five:D10} 00000 n \n"
                + "trailer\n<< /Size 6 /Root 1 0 R >>\n"
                + $"startxref\n{table}\n%%EOF\n");
        }

        /// <summary>
        /// Object numbers a PDF refers to and does not define.
        ///
        /// A copied graph is renumbered on the way in, and a renumbering that drops one
        /// entry gives a file that opens, draws most of itself, and is quietly missing a
        /// font. Nothing about it looks wrong - which is section 2's whole subject, and
        /// the reason this asks whether the parts reach each other rather than whether
        /// they are there.
        /// </summary>
        private static List<int> Dangling(string pdf)
        {
            var defined = System.Text.RegularExpressions.Regex
                .Matches(pdf, @"(?:^|[\r\n])(\d+) 0 obj")
                .Select(m => int.Parse(m.Groups[1].Value))
                .ToHashSet();

            return System.Text.RegularExpressions.Regex
                .Matches(pdf, @"(\d+) 0 R")
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .Where(n => !defined.Contains(n))
                .OrderBy(n => n)
                .ToList();
        }

        private static string Refused(Action what)
        {
            try { what(); return ""; }
            catch (Exception e) { return e.Message.Length > 0 ? e.Message : e.GetType().Name; }
        }

        /// <summary>
        /// A PDF written the way everything since Acrobat 6 writes them: the catalogue,
        /// the page tree and the page packed into one object stream, and a cross-reference
        /// stream - Flate-compressed, with a PNG predictor - in place of the plain table.
        ///
        /// Built here because our own writer emits neither, so without it the reader's
        /// two hardest paths would be exercised by nothing at all.
        /// </summary>
        private static byte[] ModernPdf(string extra = "", string box = "0 0 297.6378 419.5276")
        {
            const string catalogue = "<< /Type /Catalog /Pages 2 0 R >>";
            const string pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
            string page = "<< /Type /Page /Parent 2 0 R " + extra
                        + "/MediaBox [" + box + "] /Contents 5 0 R >>";
            const string drawing = "0 0 1 rg 10 10 50 50 re f\n";

            // The pairs at the head are (object number, where its body starts, counting
            // from /First), so the header has to be built before the bodies it describes.
            string bodies = catalogue + " " + pages + " " + page + " ";
            string head = $"1 0 2 {catalogue.Length + 1} 3 {catalogue.Length + pages.Length + 2} ";

            var file = new MemoryStream();
            void Put(string text) => file.Write(System.Text.Encoding.Latin1.GetBytes(text));
            void PutBytes(byte[] bytes) => file.Write(bytes);

            Put("%PDF-1.5\n");
            file.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

            byte[] packed = System.Text.Encoding.Latin1.GetBytes(head + bodies);
            int atObjStm = (int)file.Length;
            Put($"4 0 obj\n<< /Type /ObjStm /N 3 /First {head.Length} /Length {packed.Length} >>\nstream\n");
            PutBytes(packed);
            Put("\nendstream\nendobj\n");

            byte[] marks = System.Text.Encoding.Latin1.GetBytes(drawing);
            int atContent = (int)file.Length;
            Put($"5 0 obj\n<< /Length {marks.Length} >>\nstream\n");
            PutBytes(marks);
            Put("\nendstream\nendobj\n");

            int atXref = (int)file.Length;

            // Seven entries of five bytes: a type, then two bytes of offset-or-container,
            // then two of generation-or-index. /W [1 2 2].
            // Spelled out rather than written as collection expressions: inside a list
            // initializer C# reads [ ... ] as an indexer and says so in eight ways.
            var rows = new List<byte[]>
            {
                new byte[] { 0, 0, 0, 0, 0 },                           // the free head
                new byte[] { 2, 0, 4, 0, 0 },                           // 1, in stream 4
                new byte[] { 2, 0, 4, 0, 1 },                           // 2, likewise
                new byte[] { 2, 0, 4, 0, 2 },                           // 3, likewise
                new byte[] { 1, (byte)(atObjStm >> 8), (byte)atObjStm, 0, 0 },
                new byte[] { 1, (byte)(atContent >> 8), (byte)atContent, 0, 0 },
                new byte[] { 1, (byte)(atXref >> 8), (byte)atXref, 0, 0 }
            };

            // Predictor 12 is PNG "Up": every row is written as the difference from the
            // one above it, with a tag byte of 2 in front. Exercised on purpose - it is
            // what nearly every real cross-reference stream uses, and it is arithmetic
            // that a reader gets subtly wrong rather than loudly.
            var predicted = new MemoryStream();
            var previous = new byte[5];
            foreach (byte[] row in rows)
            {
                predicted.WriteByte(2);
                for (int i = 0; i < 5; i++)
                    predicted.WriteByte((byte)(row[i] - previous[i]));
                previous = row;
            }

            var squashed = new MemoryStream();
            using (var zlib = new ZLibStream(squashed, CompressionLevel.SmallestSize, leaveOpen: true))
                predicted.WriteTo(zlib);

            byte[] table = squashed.ToArray();
            Put($"6 0 obj\n<< /Type /XRef /Size 7 /W [1 2 2] /Root 1 0 R "
                + "/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 5 >> "
                + $"/Length {table.Length} >>\nstream\n");
            PutBytes(table);
            Put($"\nendstream\nendobj\nstartxref\n{atXref}\n%%EOF\n");

            return file.ToArray();
        }

        /// <summary>The smallest file that says it is encrypted, which is all this has to be.</summary>
        private static byte[] Encrypted()
        {
            const string catalogue = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            const string pages = "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n";

            string head = "%PDF-1.4\n";
            int one = head.Length;
            int two = one + catalogue.Length;
            int table = two + pages.Length;

            string body = head + catalogue + pages
                + "xref\n0 3\n"
                + "0000000000 65535 f \n"
                + $"{one:D10} 00000 n \n"
                + $"{two:D10} 00000 n \n"
                + "trailer\n<< /Size 3 /Root 1 0 R /Encrypt 9 0 R >>\n"
                + $"startxref\n{table}\n%%EOF\n";

            return System.Text.Encoding.Latin1.GetBytes(body);
        }
    }
}
