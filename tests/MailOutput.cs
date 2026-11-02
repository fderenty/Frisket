namespace test
{
    /// <summary>
    /// The flowing HTML of --format mail: a second consumer of the model, parallel to the
    /// resolver rather than a backend, with no pages at all and no embedded fonts - the exact
    /// inverse of the SVG proof next door. Email's rules are older and narrower than the
    /// web's: tables for layout, styles inline, widths in per cent.
    /// </summary>
    public partial class EngineTests
    {
        static int Count(string text, string needle)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(needle, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                n++;
                at += needle.Length;
            }
            return n;
        }

        /// <summary>
        /// A message fills the tokens it has an answer to, and has no page to number.
        ///
        /// The binder leaves the engine's own tokens for the layout, and a message has no layout,
        /// so all of them went out as typed: "{HeadingNumber}. Introduction", "Page {Page} of
        /// {Pages}". Every assertion on this output looked at its shape, and no sample here was
        /// ever read as a person reads a mail.
        /// </summary>
        [Fact]
        public void Test114_AMessageFillsWhatItCanAndHasNoPages()
        {
            const string testName = "Test 114";
            Console.WriteLine("Test 114: the engine's own tokens in a message");
            Console.WriteLine();

            static AdvancedTextItem Rich(params (string Text, bool Bold)[] runs)
            {
                var item = new AdvancedTextItem();
                foreach ((string text, bool bold) in runs)
                    item.Runs.Add(new TextRun { Text = text, Font = bold ? new TextStyle { Bold = true } : null });
                return item;
            }
            static string Mail(string? numbering, params BaseItem[] items)
            {
                var report = new Report
                {
                    Title = "Title <one>",
                    Language = "en-GB",
                    HeadingNumbering = numbering,
                    GeneratedAt = SampleDate,
                    Dimension = new() { Width = 100, Height = 120 },
                    Body = new() { Layout = LayoutMode.Stack },
                };
                report.Fields["Reference"] = "R-12";
                foreach (BaseItem item in items)
                    report.Body!.Add(item);
                report.Reattach();
                return FlowingHtml.Write(report);
            }
            static string Bare(string html)
            {
                int body = html.IndexOf("<body", StringComparison.Ordinal);
                string text = System.Text.RegularExpressions.Regex.Replace(html[body..], "<[^>]+>", "\n");
                return string.Join(" | ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
                    .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
            }
            static string Says(string text) => Bare(Mail("1.1", new TextItem { Text = text }));

            // --- what it has an answer to ---------------------------------------------------------
            AssertEqual(testName, "the title", "Report: Title <one>", Says("Report: {Title}"), 0);
            AssertEqual(testName, "the date", $"Made {SampleDate:yyyy-MM-dd}", Says("Made {Date}"), 0);
            AssertEqual(testName, "with a format", $"Made {SampleDate:dd/MM/yyyy}", Says("Made {Date:dd/MM/yyyy}"), 0);
            AssertEqual(testName, "the time", $"At {SampleDate:HH:mm}", Says("At {Time}"), 0);
            AssertEqual(testName, "a field the report holds", "Ref R-12", Says("Ref {Reference}"), 0);
            AssertEqual(testName, "a brace written out", "{Title}", Says("{{Title}}"), 0);
            AssertEqual(testName, "what is filled is still escaped", true,
                Mail("1.1", new TextItem { Text = "{Title}" }).Contains("Title &lt;one&gt;", StringComparison.Ordinal), 0);
            AssertEqual(testName, "a token nobody can answer stays, as it does on a page", "{Nobody}", Says("{Nobody}"), 0);

            var first = new TextItem { Text = "{HeadingNumber}. First", Role = PdfRole.Heading1 };
            var second = new TextItem { Text = "{HeadingNumber}. Second", Role = PdfRole.Heading2 };
            AdvancedTextItem richHeading = Rich(("{HeadingNumber}. ", false), ("Rich", true));
            richHeading.Role = PdfRole.Heading2;
            var within = new TextItem { Text = "{SectionHeadingNumber}. Within", Role = PdfRole.Heading2 };
            AssertEqual(testName, "heading numbers, plain and rich", "1. First | 1.1. Second | 1.2. | Rich | 1.3. Within",
                Bare(Mail("1.1", first, second, richHeading, within)), 0);
            AssertEqual(testName, "with no format to write them in they stay, as they do on a page", "{HeadingNumber}. Alone",
                Bare(Mail(null, new TextItem { Text = "{HeadingNumber}. Alone", Role = PdfRole.Heading1 })), 0);

            // --- what it has no answer to ----------------------------------------------------------
            foreach (string gone in new[]
                     {
                         "Page {Page} of {Pages}", "Page {Page}/{Pages}", "Page {Page} / {Pages}", "{Pages} pages", "Sheet {SectionPage}",
                         "{SectionPages} sheets", "page {page} of {PAGES}", "Page {Page:000}", "See page {Page}",
                     })
                AssertEqual(testName, $"\"{gone}\" says nothing at all", "", Says(gone), 0);

            AssertEqual(testName, "it leaves no empty paragraph behind", 0,
                Count(Mail("1.1", new TextItem { Text = "Page {Page} of {Pages}" }), "<p "), 0);

            AssertEqual(testName, "what stands beside it stays", "Confidential", Says("Confidential — Page {Page} of {Pages}"), 0);
            AssertEqual(testName, "whichever side", "Confidential", Says("Page {Page} of {Pages} - Confidential"), 0);
            AssertEqual(testName, "and the separator between what stays", "Confidential | Draft",
                Says("Confidential | Page {Page} | Draft"), 0);
            AssertEqual(testName, "on a line of its own", "Confidential", Says("Confidential\nPage {Page}"), 0);
            AssertEqual(testName, "a dash in a word is no separator", "", Says("Well-known Page {Page}"), 0);
            AssertEqual(testName, "nor one with a word against it on the right", "", Says("Due -5 on page {Page}"), 0);
            AssertEqual(testName, "nor one with a word against it on the left", "", Says("Well- Page {Page}"), 0);
            AssertEqual(testName, "a text with no page number is not touched", "A - B / C | D", Says("A - B / C | D"), 0);
            AssertEqual(testName, "a page written out is not a page number", "{Page} of - it", Says("{{Page}} of - it"), 0);

            // Rich text: the number in bold is a run of its own, and what stays keeps its style.
            AssertEqual(testName, "rich: \"Page 3 of 8\" with its numbers in bold says nothing", "",
                Bare(Mail("1.1", Rich(("Page ", false), ("{Page}", true), (" of ", false), ("{Pages}", true)))), 0);
            string kept = Mail("1.1", Rich(("Confidential", true), (" — Page ", false), ("{Page}", true)));
            AssertEqual(testName, "rich: what stays stays", "Confidential", Bare(kept), 0);
            AssertEqual(testName, "rich: in its own style", true,
                System.Text.RegularExpressions.Regex.IsMatch(kept, "<span style=\"font-weight:bold;\">Confidential</span>"), 0);
            AssertEqual(testName, "rich: a text with no page number keeps every run", "A | B",
                Bare(Mail("1.1", Rich(("A", false), ("B", true)))), 0);

            // A cell is still a cell, or a row comes out a column short.
            var table = new TableItem();
            table.AddAutoColumn().AddAutoColumn();
            table.AddRow(new TextItem { Text = "Kept" }, new TextItem { Text = "Page {Page}" });
            AssertEqual(testName, "a cell left with nothing is still a cell", 2,
                Count(Mail("1.1", table), "padding:.3em"), 0);

            // What is shown in place of something else.
            var code = new BarcodeItem { Value = "x", AltText = "Code for {Title} - page {Page}" };
            AssertEqual(testName, "an alternative text shown in the message", "Code for Title <one>", Bare(Mail("1.1", code)), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// The report as a message rather than as a page.
        ///
        /// FlowingHtml is a second consumer of the model, parallel to the resolver
        /// rather than downstream of it: everything reaching a backend has already been
        /// laid out in millimetres on a page, and a message has neither millimetres nor
        /// pages. So what is checked here is not where things landed - nothing landed -
        /// but that the shape an email needs is the shape that came out.
        ///
        /// Written from the showcase, read back through Report.FromFile first, because
        /// that round trip is the only thing proving each kind of element survives being
        /// written and read - the same reason the conformance document takes it.
        /// </summary>
        [Fact]
        public void Test55_FlowingHtml()
        {
            var testName = "Test 55: the report as a message";
            Console.WriteLine($"Running {testName}...");

            Report report = Report.FromFile(Path.Combine(RepositoryRoot(), "examples", "showcase.json"));
            string html = FlowingHtml.Write(report);

            string dir = OutputDirectory("mail");
            string path = Path.Combine(dir, "showcase.mail.html");
            File.WriteAllText(path, html);

            Console.WriteLine($"    {html.Length / 1024} ko -> {path}");

            // What an email cannot carry, and what it must.
            AssertEqual(testName, "no SVG - Gmail strips it outright", 0,
                Count(html, "<svg"), 0);
            AssertEqual(testName, "no embedded fonts - the reader supplies the face", 0,
                Count(html, "@font-face"), 0);
            AssertEqual(testName, "no absolute positioning - there is nothing to be absolute to", 0,
                Count(html, "position:absolute"), 0);
            AssertEqual(testName, "no stylesheet - too many clients discard one", 0,
                Count(html, "<style"), 0);

            AssertEqual(testName, "tables carry the layout, as they have for twenty years", true,
                Count(html, "role=\"presentation\"") > 0, Count(html, "role=\"presentation\""));
            AssertEqual(testName, "and the message is bounded rather than the window's width", true,
                html.Contains("max-width:640px"), 0);

            // Centred, and centred twice over. align="center" on the cell centres inline
            // content and a table is not inline, so without margin:0 auto the whole
            // message sits against one edge; and Outlook lays tables out with Word, which
            // ignores max-width, so the width attribute is what it reads.
            AssertEqual(testName, "the column is centred for a browser", true,
                html.Contains("margin:0 auto"), 0);
            AssertEqual(testName, "and given a width Outlook will read", true,
                html.Contains("width=\"640\""), 0);

            // A picture keeps the size it was given, as a fraction of the band it was a
            // fraction of. Dropping it made a 60mm picture arrive as wide as the whole
            // message: "no wider than the window" is not the same instruction as "this
            // wide", and only the ceiling was being written.
            var sized = new Report { Dimension = new() { Width = 210, Height = 297 } };
            sized.Body = new Body { Layout = LayoutMode.Stack };
            sized.Body.Style.Padding = new EdgeSize { Left = 20, Right = 20 };

            var half = new ImageItem { AltText = "half the band" };
            half.Source = new ImageSource { MediaType = "image/png", Data = new byte[] { 1, 2, 3, 4 } };
            half.Style.Location.WidthValue = 85;        // half of 210 - 20 - 20
            sized.Body!.Add(half);
            sized.Reattach();

            string withWidth = FlowingHtml.Write(sized);
            AssertEqual(testName, "a picture keeps its width, as a share of the band", true,
                withWidth.Contains("width:50%"), 0);

            // And never past the edge, whatever the template asked for.
            half.Style.Location.WidthValue = 400;
            AssertEqual(testName, "and never wider than the message", true,
                FlowingHtml.Write(sized).Contains("width:100%"), 0);

            // The document's own words reach it. Read off the report rather than written
            // down here, so translating the samples cannot make this fail for the wrong
            // reason - the lesson the designer harness already learnt.
            string[] words = report.AllItems.OfType<TextItem>()
                .Select(t => t.Text)
                .Where(t => t.Length > 12 && !t.Contains('{'))
                .Take(3).ToArray();

            AssertEqual(testName, "the showcase has words to look for", true, words.Length > 0, words.Length);
            foreach (string word in words)
            {
                AssertEqual(testName, $"\"{word[..Math.Min(24, word.Length)]}…\" is in the message", true,
                    html.Contains(System.Net.WebUtility.HtmlEncode(word)) || html.Contains(word), 0);
            }

            // A picture travels as its own bytes, and they have to be *bytes*:
            // ImageSource.Data is a byte[] the serializer writes as base64, so it reads
            // like a base64 string everywhere a template is read - and appending it to
            // one gives "System.Byte[]", a data: URI a browser accepts and draws nothing
            // for. Found by opening the file, which no assertion about its length would
            // have found.
            int at = html.IndexOf("base64,", StringComparison.Ordinal);
            AssertEqual(testName, "a picture travels with the message", true, at > 0, 0);
            if (at > 0)
            {
                int end = html.IndexOf('"', at);
                string payload = html[(at + 7)..end];
                AssertEqual(testName, "and its bytes are a picture, not a type name", true,
                    payload.Length > 100 && Convert.FromBase64String(payload)[..4] is
                        [0x89, (byte)'P', (byte)'N', (byte)'G'] or [0xFF, 0xD8, ..],
                    payload.Length);
            }

            // A picture beside a paragraph survives, as the oldest idiom there is: one
            // row, two cells. The first version of this stacked every absolutely laid
            // out group and left a rule to apologise for it, which was too quick - what
            // has no flowing form is an arrangement whose pieces *overlap*, since a cell
            // cannot be half over its neighbour.
            var beside = new Report { Dimension = new() { Width = 210, Height = 297 } };
            beside.Body = new Body { Layout = LayoutMode.Stack };
            beside.Body.Style.Padding = new EdgeSize { Left = 20, Right = 20 };

            var pairGroup = new ContainerItem { Layout = LayoutMode.Absolute };

            var pairPicture = new ImageItem { AltText = "beside" };
            pairPicture.Source = new ImageSource { MediaType = "image/png", Data = new byte[] { 1, 2, 3, 4 } };
            pairPicture.Style.Location.LeftValue = 0;
            pairPicture.Style.Location.WidthValue = 60;

            var pairText = new AdvancedTextItem();
            pairText.Runs.Add(new TextRun { Text = "The paragraph that sits beside it." });
            pairText.Style.Location.LeftValue = 64;
            pairText.Style.Location.Dock = Dock.Right;
            pairText.Style.AlignVertical = ItemAlign.Center;

            pairGroup.Add(pairPicture);
            pairGroup.Add(pairText);
            beside.Body!.Add(pairGroup);
            beside.Reattach();

            string sideBySide = FlowingHtml.Write(beside);

            AssertEqual(testName, "a picture beside a paragraph stays beside it", 2,
                Count(sideBySide, "<td valign="), 0);
            AssertEqual(testName, "the picture's cell keeps its share of the band", true,
                sideBySide.Contains("width:35.3%"), 0);
            AssertEqual(testName, "and a cell told to centre says so once, not twice", 1,
                Count(sideBySide, "vertical-align:middle"), 0);

            // And the rule stays quiet about it, because there is nothing lost. A rule
            // that warns about what the emitter handles perfectly well is a rule people
            // learn to ignore.
            var aboutPair = TemplateValidator.Check(beside, TemplateProfile.All,
                                                    new RenderTarget(Paged: false));
            AssertEqual(testName, "and nothing is reported about it", false,
                aboutPair.Any(i => i.Rule == "can.absoluteInFlow"), 0);

            // And the profile says what is lost before any of this is written. A group
            // whose pieces overlap has no flowing form at all - a cell cannot be half
            // over its neighbour, so the children can only be stacked.
            var pinned = new Report { Dimension = new() { Width = 210, Height = 297 } };
            pinned.Body = new Body { Layout = LayoutMode.Stack };
            var group = new ContainerItem { Layout = LayoutMode.Absolute };
            group.Add(new TextItem { Text = "left" });
            group.Add(new TextItem { Text = "right" });
            pinned.Body!.Add(group);
            pinned.Reattach();

            var toldPaged = TemplateValidator.Check(pinned, TemplateProfile.All, new RenderTarget());
            var toldFlowing = TemplateValidator.Check(pinned, TemplateProfile.All,
                                                     new RenderTarget(Paged: false));

            AssertEqual(testName, "a paged output is not warned about coordinates", false,
                toldPaged.Any(i => i.Rule == "can.absoluteInFlow"), 0);
            AssertEqual(testName, "a message is", true,
                toldFlowing.Any(i => i.Rule == "can.absoluteInFlow"), 0);

            Console.WriteLine();
        }
    }
}
