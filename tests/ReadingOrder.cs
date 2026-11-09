using System.Text;
using System.Text.RegularExpressions;
using Frisket.Import;

namespace test
{
    /// <summary>
    /// A tagged file read the way assistive software reads it: down the structure tree in the
    /// order it says, taking each mark's words from the page that draws them and decoding them
    /// through the font's own map. Nothing here asks the engine anything, and veraPDF cannot do
    /// it for us: a well-formed tree in the wrong order is not something either profile tests.
    /// </summary>
    public partial class EngineTests
    {
        private sealed record Heard(string Tag, string Words, string? Language);

        private sealed class Reading
        {
            /// <summary>What a reader says, element by element, in the order it says it.</summary>
            public List<Heard> Heard { get; } = new();

            /// <summary>What is drawn and marked as decoration, which a reader skips.</summary>
            public List<string> Skipped { get; } = new();

            /// <summary>What is drawn and marked as a pagination artifact, with its subtype.</summary>
            public List<(string Subtype, string Words)> Running { get; } = new();

            /// <summary>Every element of the tree, words or no words, in the order it comes.</summary>
            public List<string> Tags { get; } = new();

            /// <summary>The tags directly under the document.</summary>
            public List<string> AtTheTop { get; } = new();

            /// <summary>
            /// What each element with words is said under, as "Table/TR/TD/H2: words": the tags
            /// from the document down to it, the document left out. The order of the tags alone
            /// cannot tell a TR under a Table from a TR beside one.
            /// </summary>
            public List<string> Lineage { get; } = new();

            /// <summary>The elements each row holds, as "TD TD", in the order the rows come.</summary>
            public List<string> Rows { get; } = new();

            /// <summary>Where the two halves of the tree, or the tree and the page, do not reach each other.</summary>
            public List<string> Faults { get; } = new();
        }

        private static readonly Regex ContentToken = new(
            @"/(?<tag>\w+) <</MCID (?<mcid>\d+)>> BDC"
            + @"|/Artifact <</Type /Pagination /Subtype /(?<running>\w+)>> BDC"
            + @"|(?<artifact>/Artifact BMC)"
            + @"|(?<end>\bEMC\b)"
            + @"|/(?<font>[\w+-]+) [\d.]+ Tf"
            + @"|<(?<hex>[0-9A-Fa-f]*)> Tj"
            + @"|\[(?<array>[^\]]*)\] TJ",
            RegexOptions.Compiled);

        private static Reading ReadAloud(byte[] pdf)
        {
            var reading = new Reading();
            PdfFile file = PdfFile.Open(pdf);
            PdfDictionary Dictionary(PdfObject? value) => (PdfDictionary)file.Resolve(value)!;

            PdfDictionary catalogue = Dictionary(file.Trailer["Root"]);
            List<int> pages = ((PdfArray)file.Resolve(Dictionary(catalogue["Pages"])["Kids"])!).Items
                .Cast<PdfReference>().Select(page => page.Number).ToList();

            // --- what each page draws, mark by mark ---------------------------------------
            var marks = new Dictionary<(int Page, int Mark), string>();
            var marksOnPage = new Dictionary<int, int>();

            for (int index = 0; index < pages.Count; index++)
            {
                PdfDictionary page = file.Page(index);
                PdfDictionary fonts = Dictionary(Dictionary(page["Resources"])["Font"]);
                var maps = new Dictionary<string, Dictionary<string, string>>();

                string content = Encoding.Latin1.GetString(
                    Filters.Decode((PdfStream)file.Resolve(page["Contents"])!, file.Resolve));

                Dictionary<string, string> font = new();
                int open = -1;          // the mark being read, or -2 inside decoration
                string? subtype = null;
                var words = new StringBuilder();
                int count = 0;

                foreach (Match token in ContentToken.Matches(content))
                {
                    if (token.Groups["mcid"].Success)
                    {
                        open = int.Parse(token.Groups["mcid"].Value);
                        words.Clear();
                        count++;
                    }
                    else if (token.Groups["artifact"].Success)
                    {
                        open = -2;
                        subtype = null;
                        words.Clear();
                    }
                    else if (token.Groups["running"].Success)
                    {
                        open = -2;
                        subtype = token.Groups["running"].Value;
                        words.Clear();
                    }
                    else if (token.Groups["end"].Success)
                    {
                        string said = Regex.Replace(words.ToString(), @"\s+", " ").Trim();
                        if (open >= 0)
                            marks[(pages[index], open)] = said;
                        else if (open == -2 && said.Length > 0 && subtype != null)
                            reading.Running.Add((subtype, said));
                        else if (open == -2 && said.Length > 0)
                            reading.Skipped.Add(said);
                        open = -1;
                    }
                    else if (token.Groups["font"].Success)
                    {
                        string name = token.Groups["font"].Value;
                        if (!maps.TryGetValue(name, out Dictionary<string, string>? map))
                            maps[name] = map = fonts[name] is { } entry ? UnicodeOf(Dictionary(entry), file) : new();
                        font = map;
                    }
                    else if (token.Groups["hex"].Success)
                    {
                        words.Append(Decoded(token.Groups["hex"].Value, font)).Append(' ');
                    }
                    else if (token.Groups["array"].Success)
                    {
                        foreach (Match piece in Regex.Matches(token.Groups["array"].Value, "<([0-9A-Fa-f]*)>"))
                            words.Append(Decoded(piece.Groups[1].Value, font)).Append(' ');
                    }
                }

                marksOnPage[pages[index]] = count;
            }

            // --- down the tree, in the order it says ----------------------------------------
            var owners = new Dictionary<(int Page, int Mark), int>();
            var holdingElements = new HashSet<int>();

            void Walk(PdfReference reference, int depth, string above)
            {
                var element = (PdfDictionary)file.Get(reference.Number)!;
                string tag = ((PdfName)file.Resolve(element["S"])!).Value;
                reading.Tags.Add(tag);
                if (depth == 1)
                    reading.AtTheTop.Add(tag);
                string path = depth == 0 ? "" : above.Length == 0 ? tag : above + "/" + tag;

                int page = (element["Pg"] as PdfReference)?.Number ?? 0;
                var own = new List<int>();
                var kids = new List<PdfReference>();

                foreach (PdfObject kid in element["K"] is PdfArray list ? list.Items : element["K"] is { } one ? [one] : [])
                {
                    if (kid is PdfNumber mark)
                        own.Add((int)mark.Value);
                    else if (kid is PdfReference child)
                        kids.Add(child);
                }

                if (kids.Count > 0)
                    holdingElements.Add(reference.Number);

                if (tag == "TR")
                    reading.Rows.Add(string.Join(" ", kids.Select(kid =>
                        ((PdfName)file.Resolve(((PdfDictionary)file.Get(kid.Number)!)["S"])!).Value)));

                foreach (int mark in own)
                {
                    if (!marks.ContainsKey((page, mark)))
                        reading.Faults.Add($"{tag} names mark {mark}, which its page does not draw");
                    owners[(page, mark)] = reference.Number;
                }

                string words = string.Join(" ", own.Select(m => marks.GetValueOrDefault((page, m), ""))
                                                   .Where(said => said.Length > 0));
                if (words.Length > 0)
                {
                    string? language = element["Lang"] is PdfString lang ? Encoding.Latin1.GetString(lang.Bytes) : null;
                    reading.Heard.Add(new Heard(tag, words, language));
                    reading.Lineage.Add($"{path}: {words}");
                }

                foreach (PdfReference child in kids)
                    Walk(child, depth + 1, path);
            }

            PdfDictionary root = Dictionary(catalogue["StructTreeRoot"]);
            Walk((PdfReference)root["K"]!, 0, "");

            foreach (((int page, int mark), string said) in marks)
            {
                if (said.Length > 0 && !owners.ContainsKey((page, mark)))
                    reading.Faults.Add($"\"{said}\" is drawn, and no element reaches it");
            }

            // --- and back up: position n of a page's array is the element that owns mark n ---
            var map2 = new Dictionary<(int Page, int Mark), int>();
            List<PdfObject> numbers = ((PdfArray)Dictionary(root["ParentTree"])["Nums"]!).Items;
            for (int i = 0; i + 1 < numbers.Count; i += 2)
            {
                int key = (int)((PdfNumber)numbers[i]).Value;
                if (key >= pages.Count || numbers[i + 1] is not PdfArray row)
                    continue;

                if (row.Items.Count != marksOnPage[pages[key]])
                    reading.Faults.Add($"page {key + 1} draws {marksOnPage[pages[key]]} marks and its array holds {row.Items.Count}");

                for (int position = 0; position < row.Items.Count; position++)
                    map2[(pages[key], position)] = ((PdfReference)row.Items[position]).Number;
            }

            foreach (((int page, int mark), int element) in owners)
            {
                if (map2.GetValueOrDefault((page, mark)) != element)
                    reading.Faults.Add($"mark {mark} on page object {page} belongs to element {element} and the map says {map2.GetValueOrDefault((page, mark))}");
            }

            // And the other way. An element holding other elements lists those and not the
            // background of its own mark, which is how a container has always been written.
            foreach (((int page, int mark), int element) in map2)
            {
                if (!owners.ContainsKey((page, mark)) && !holdingElements.Contains(element))
                    reading.Faults.Add($"the map gives mark {mark} on page object {page} to element {element}, which does not list it");
            }

            return reading;
        }

        /// <summary>A font's map from glyph to characters, as it stands in the file.</summary>
        private static Dictionary<string, string> UnicodeOf(PdfDictionary font, PdfFile file)
        {
            var map = new Dictionary<string, string>();
            if (font["ToUnicode"] == null)
                return map;

            string cmap = Encoding.Latin1.GetString(Filters.Decode((PdfStream)file.Resolve(font["ToUnicode"])!, file.Resolve));
            foreach (Match block in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
            {
                foreach (Match pair in Regex.Matches(block.Groups[1].Value, @"<([0-9A-Fa-f]{4})>\s*<([0-9A-Fa-f]+)>"))
                {
                    string hex = pair.Groups[2].Value;
                    var text = new StringBuilder();
                    for (int at = 0; at + 4 <= hex.Length; at += 4)
                        text.Append((char)Convert.ToInt32(hex.Substring(at, 4), 16));
                    map[pair.Groups[1].Value.ToUpperInvariant()] = text.ToString();
                }
            }

            return map;
        }

        private static string Decoded(string hex, Dictionary<string, string> font)
        {
            var text = new StringBuilder();
            for (int at = 0; at + 4 <= hex.Length; at += 4)
                text.Append(font.GetValueOrDefault(hex.Substring(at, 4).ToUpperInvariant(), "�"));
            return text.ToString();
        }

        /// <summary>
        /// A tagged file says its words in the order the page shows them.
        ///
        /// The pieces a rich text is drawn through were paragraphs of their own with no parent,
        /// so each stood at the root of the tree: a rich cell's element was empty and its words
        /// came after the table, a rich paragraph came after every plain text on its page, a rich
        /// heading was no heading, and every line and change of style was a paragraph. A file
        /// claiming PDF/UA-1 read like that from the first commit, and passed both profiles.
        /// </summary>
        [Fact]
        public void Test111_ATaggedFileReadsInTheOrderItIsWritten()
        {
            const string testName = "Test 111";
            Console.WriteLine("Test 111: a tagged file, read down its tree");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            static string Numbered(string prefix, int count)
                => string.Join(" ", Enumerable.Range(0, count).Select(i => prefix + i));
            static TextItem Plain(string text) => new() { Text = text };
            static AdvancedTextItem Rich(params string[] runs)
            {
                var item = new AdvancedTextItem();
                foreach (string run in runs)
                    item.Runs.Add(new TextRun { Text = run });
                return item;
            }

            var report = new Report
            {
                Title = "Read aloud",
                Language = "en-GB",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 120, Height = 150 },
                Body = new() { Layout = LayoutMode.Stack },
            };
            BaseItem Add(BaseItem item)
            {
                report.Body!.Add(item);
                return item;
            }

            TextItem opening = Plain("Opening words");
            opening.Role = PdfRole.Heading1;
            Add(opening);

            AdvancedTextItem styled = Rich("Rich ", "bold", " words");
            styled.Runs[1].Font = new TextStyle { Bold = true };
            Add(styled);
            Add(Plain("Plain after it"));

            var table = new TableItem();
            table.AddAutoColumn().AddAutoColumn();
            table.AddHeaderRow(Plain("Plain head"), Rich("Rich head"));
            table.AddRow(Plain("Plain cell"), Rich("Rich cell"));
            Add(table);

            AdvancedTextItem heading = Rich("Rich heading");
            heading.Role = PdfRole.Heading2;
            Add(heading);

            AdvancedTextItem decoration = Rich("Decoration words");
            decoration.Role = PdfRole.Artifact;
            Add(decoration);

            AdvancedTextItem german = Rich("Deutsche Worte");
            german.Language = "de-DE";
            Add(german);

            AdvancedTextItem justified = Rich(Numbered("j", 40));
            justified.Font.HorizontalAlignment = HorizontalAlignment.Justify;
            Add(justified);

            Add(Rich(Numbered("l", 1000)));

            var cut = new TableItem();
            cut.AddAutoColumn().AddAutoColumn();
            cut.AddRow(Plain("Short"), Rich(Numbered("r", 300)));
            Add(cut);

            Add(Plain("Closing words"));
            report.Reattach();

            var pdf = new PdfBackend(library) { Tagged = true, Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            LayoutResult layout = ReportRenderer.Render(report, pdf);
            Reading reading = ReadAloud(pdf.ToArray());

            foreach (Heard said in reading.Heard)
                Console.WriteLine($"    {said.Tag,-3} {(said.Words.Length > 60 ? said.Words[..60] + "..." : said.Words)}");

            AssertEqual(testName, "the two halves of the tree and the page reach each other", "",
                string.Join(" | ", reading.Faults), 0);
            AssertEqual(testName, "and it was asked of more than one page", true, layout.PageCount >= 3, 0);

            // --- in the order it is written, each what it says it is -------------------------
            string[] opens = reading.Heard.Take(9).Select(h => $"{h.Tag}: {h.Words}").ToArray();
            AssertEqual(testName, "what comes first", string.Join(" | ",
                    "H1: Opening words", "P: Rich bold words", "P: Plain after it",
                    "TH: Plain head", "TH: Rich head", "TD: Plain cell", "TD: Rich cell",
                    "H2: Rich heading", "P: Deutsche Worte"),
                string.Join(" | ", opens), 0);

            AssertEqual(testName, "a justified text is one paragraph, word for word", Numbered("j", 40), reading.Heard[9].Words, 0);

            string Joined(string prefix, string tag) => string.Join(" ", reading.Heard
                .Where(h => h.Tag == tag && h.Words.StartsWith(prefix, StringComparison.Ordinal)).Select(h => h.Words));

            AssertEqual(testName, "a rich paragraph over several pages arrives whole and in order", Numbered("l", 1000), Joined("l", "P"), 0);
            AssertEqual(testName, "with one element per page, as a plain one has", true,
                reading.Heard.Count(h => h.Tag == "P" && h.Words.StartsWith('l')) >= 2, 0);
            AssertEqual(testName, "a rich cell over several pages stays in its cells", Numbered("r", 300), Joined("r", "TD"), 0);
            AssertEqual(testName, "and what comes last comes last", "P: Closing words",
                $"{reading.Heard[^1].Tag}: {reading.Heard[^1].Words}", 0);

            // --- what else an element says ----------------------------------------------------
            AssertEqual(testName, "a passage in another language says so on its element", "de-DE",
                reading.Heard.Single(h => h.Words == "Deutsche Worte").Language ?? "", 0);
            AssertEqual(testName, "decoration is in nobody's way", false,
                reading.Heard.Any(h => h.Words.Contains("Decoration", StringComparison.Ordinal)), 0);
            AssertEqual(testName, "and is on the page all the same", true, reading.Skipped.Contains("Decoration words"), 0);
            AssertEqual(testName, "nothing stands loose at the top of the tree", "Sect",
                string.Join(",", reading.AtTheTop.Distinct()), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A report long enough to repeat its bands: a header in plain and rich text, and a
        /// footer holding what the two kinds of document put there. A notice and a page number
        /// on every page, as a report has; on the last page only, an amount due, as an invoice
        /// has; and a table with a row only the last page shows, which is the shape that has to
        /// be read as a whole or refused.
        /// </summary>
        private static Report WithBands(RepeatedReading reading, int words = 1500)
        {
            var report = new Report
            {
                Title = "Bands",
                Language = "en-GB",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 120, Height = 150 },
                Body = new() { Layout = LayoutMode.Stack },
                Header = new() { Layout = LayoutMode.Stack, RepeatedReading = reading },
                Footer = new() { Layout = LayoutMode.Stack, RepeatedReading = reading },
            };

            report.Body!.Add(new TextItem { Text = string.Join(" ", Enumerable.Range(0, words).Select(i => "b" + i)) });

            var richHeader = new AdvancedTextItem();
            richHeader.Runs.Add(new TextRun { Text = "Rich header" });
            report.Header!.Add(new TextItem { Text = "Header words" });
            report.Header!.Add(richHeader);

            report.Footer!.Add(new TextItem { Text = "Confidential words" });
            report.Footer!.Add(new TextItem { Text = "Page {Page} of {Pages}" });

            var amount = new AdvancedTextItem();
            amount.Runs.Add(new TextRun { Text = "AMOUNT DUE" });
            var summary = new ContainerItem { Layout = LayoutMode.Stack };
            summary.Style.PageScope = PageScope.Last;
            summary.Add(amount);
            report.Footer!.Add(summary);

            var fees = new TableItem();
            fees.AddAutoColumn();
            fees.AddRow(new TextItem { Text = "Fee row" });
            fees.AddRow(new TextItem { Text = "Total row" });
            fees.Childs[1].Style.PageScope = PageScope.Last;
            report.Footer!.Add(fees);

            report.Reattach();
            return report;
        }

        /// <summary>
        /// A header or footer is heard as the template says, and after the page it closes.
        ///
        /// Both were placed before the body, which paint order and the body's height need, and
        /// the tree followed: every page opened on its last line, and a sentence over two pages
        /// was broken by whatever both bands said, every time. PDF/UA 7.8 wants what runs marked
        /// as pagination; what appears once is not running, and marking it would hide it. So the
        /// question is asked of each item, and how many pages it landed on decides.
        /// </summary>
        [Fact]
        public void Test112_ABandIsHeardAsTheTemplateSays()
        {
            const string testName = "Test 112";
            Console.WriteLine("Test 112: what a reader hears of a header and a footer");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            (Reading Reading, int Pages) Hear(Report report)
            {
                var pdf = new PdfBackend(library) { Tagged = true, Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
                LayoutResult layout = ReportRenderer.Render(report, pdf);
                return (ReadAloud(pdf.ToArray()), layout.PageCount);
            }
            static string Said(Heard heard) => $"{heard.Tag}: {(heard.Words.StartsWith('b') ? "body" : heard.Words)}";
            static int Count(Reading reading, string subtype, string words)
                => reading.Running.Count(r => r.Subtype == subtype && r.Words == words);

            string[] Opening = ["P: Header words", "P: Rich header", "P: body"];
            string[] Closing(int page, int pages) => ["P: Confidential words", $"P: Page {page} of {pages}", "TD: Fee row"];
            string[] Last(int pages) => ["P: Confidential words", $"P: Page {pages} of {pages}", "P: AMOUNT DUE", "TD: Fee row", "TD: Total row"];

            // --- once, the default ---------------------------------------------------------------
            AssertEqual(testName, "reading once is what a band says when it says nothing", RepeatedReading.Once, new Footer().RepeatedReading, 0);

            (Reading once, int pages) = Hear(WithBands(RepeatedReading.Once));
            Console.WriteLine($"    {pages} pages, once: {string.Join(" | ", once.Heard.Select(Said))}");
            AssertEqual(testName, "the sample runs to more than two pages", true, pages >= 3, 0);
            AssertEqual(testName, "once: everything reaches everything", "", string.Join(" | ", once.Faults), 0);

            var expected = new List<string>();
            expected.AddRange(Opening);
            expected.AddRange(Closing(1, pages));
            for (int page = 2; page < pages; page++)
                expected.Add("P: body");
            expected.AddRange(["P: body", "P: AMOUNT DUE", "TD: Fee row", "TD: Total row"]);
            AssertEqual(testName, "once: heard the first time, after the body, and what is on the last page there",
                string.Join(" | ", expected), string.Join(" | ", once.Heard.Select(Said)), 0);

            AssertEqual(testName, "once: still on the paper of every other page", pages - 1, Count(once, "Footer", "Confidential words"), 0);
            AssertEqual(testName, "once: the page number too", $"Page {pages} of {pages}",
                once.Running.Last(r => r.Words.StartsWith("Page ", StringComparison.Ordinal)).Words, 0);
            AssertEqual(testName, "once: the header says it is a header", pages - 1, Count(once, "Header", "Header words"), 0);
            AssertEqual(testName, "once: and its rich text with it", pages - 1, Count(once, "Header", "Rich header"), 0);

            // --- on every page -------------------------------------------------------------------
            (Reading every, _) = Hear(WithBands(RepeatedReading.EveryPage));
            expected.Clear();
            for (int page = 1; page <= pages; page++)
            {
                expected.AddRange(Opening);
                expected.AddRange(page == pages ? Last(pages) : Closing(page, pages));
            }
            AssertEqual(testName, "on every page: all of it, each page in its order",
                string.Join(" | ", expected), string.Join(" | ", every.Heard.Select(Said)), 0);
            AssertEqual(testName, "on every page: nothing is marked as pagination", 0, every.Running.Count, 0);
            AssertEqual(testName, "on every page: everything reaches everything", "", string.Join(" | ", every.Faults), 0);

            // --- never ---------------------------------------------------------------------------
            (Reading never, _) = Hear(WithBands(RepeatedReading.Never));
            expected.Clear();
            for (int page = 1; page <= pages; page++)
                expected.Add("P: body");
            expected.AddRange(["P: AMOUNT DUE", "TD: Fee row", "TD: Total row"]);
            AssertEqual(testName, "never: the amount due is not repeating, so it is read, and its table whole",
                string.Join(" | ", expected), string.Join(" | ", never.Heard.Select(Said)), 0);
            AssertEqual(testName, "never: what repeats is on the paper of every page", pages, Count(never, "Footer", "Confidential words"), 0);
            AssertEqual(testName, "never: and the header's", pages, Count(never, "Header", "Header words"), 0);
            AssertEqual(testName, "never: everything reaches everything", "", string.Join(" | ", never.Faults), 0);

            // A document of one page repeats nothing.
            (Reading alone, int onePage) = Hear(WithBands(RepeatedReading.Never, words: 3));
            AssertEqual(testName, "the short sample is one page", 1, onePage, 0);
            AssertEqual(testName, "never: on one page nothing repeats, so all of it is read", true,
                alone.Heard.Any(h => h.Words == "Confidential words") && alone.Running.Count == 0, 0);

            // --- what the item says of itself ----------------------------------------------------
            Report silenced = WithBands(RepeatedReading.EveryPage);
            silenced.Footer!.Childs[1].Role = PdfRole.Artifact;
            (Reading quiet, _) = Hear(silenced);
            AssertEqual(testName, "a page number marked Artifact is never read, whatever the band says", false,
                quiet.Heard.Any(h => h.Words.StartsWith("Page ", StringComparison.Ordinal)), 0);
            AssertEqual(testName, "and is a footer's pagination all the same", pages,
                quiet.Running.Count(r => r.Subtype == "Footer" && r.Words.StartsWith("Page ", StringComparison.Ordinal)), 0);

            // Marked as nothing to read, on one page only: it does not open the table around it.
            Report ruled = WithBands(RepeatedReading.Never);
            var ruledTable = new TableItem();
            ruledTable.AddAutoColumn().AddAutoColumn();
            var rule = new LineItem { Role = PdfRole.Artifact };
            rule.Style.PageScope = PageScope.Last;
            ruledTable.AddRow(new TextItem { Text = "Ruled row" }, rule);
            ruled.Footer!.Add(ruledTable);
            ruled.Reattach();
            AssertEqual(testName, "what is not read does not open the table it is in", false,
                Hear(ruled).Reading.Heard.Any(h => h.Words == "Ruled row"), 0);

            // A control is a widget on every page, and a widget with no Form over it is refused.
            Report withField = WithBands(RepeatedReading.Never);
            withField.Footer!.Add(new FieldItem { Name = "initials", Kind = FieldKind.Text, AltText = "Initials" });
            withField.Reattach();
            (Reading fielded, _) = Hear(withField);
            AssertEqual(testName, "a control in a band is read on every page, whatever the band says", pages,
                fielded.Tags.Count(tag => tag == "Form"), 0);

            // --- it belongs to the band, and each section has its own ----------------------------
            Report two = WithBands(RepeatedReading.Once);
            var second = new Section { Dimension = new() { Width = 120, Height = 150 } };
            var secondBody = new Body();
            secondBody.Add(new TextItem { Text = string.Join(" ", Enumerable.Range(0, 1500).Select(i => "b" + i)) });
            var secondFooter = new Footer { Layout = LayoutMode.Stack, RepeatedReading = RepeatedReading.EveryPage };
            secondFooter.Add(new TextItem { Text = "Annex footer" });
            second.Add(secondFooter);
            second.Add(secondBody);
            two.Sections.Add(second);
            two.Reattach();
            (Reading both, _) = Hear(two);
            int annexPages = two.Resolve(new FontTextMeasurer(library)).Sections[1].PageCount;
            AssertEqual(testName, "the second section repeats its footer", true, annexPages >= 2, 0);
            AssertEqual(testName, "a section's own band says for itself", annexPages,
                both.Heard.Count(h => h.Words == "Annex footer"), 0);
            AssertEqual(testName, "and the first section's is not moved by it", 1,
                both.Heard.Count(h => h.Words == "Confidential words"), 0);

            // --- it survives what a template goes through ----------------------------------------
            string written = WithBands(RepeatedReading.EveryPage).ToJson();
            AssertEqual(testName, "written by name", true, written.Contains("\"RepeatedReading\": \"EveryPage\"", StringComparison.Ordinal), 0);
            AssertEqual(testName, "and the default not at all", false,
                WithBands(RepeatedReading.Once).ToJson().Contains("RepeatedReading", StringComparison.Ordinal), 0);
            Report readBack = Report.FromJson(written);
            AssertEqual(testName, "read back", RepeatedReading.EveryPage, readBack.Footer!.RepeatedReading, 0);
            AssertEqual(testName, "and through the binder", RepeatedReading.EveryPage,
                ReportBinder.Bind(readBack, "{}").Footer!.RepeatedReading, 0);
            AssertEqual(testName, "a header's copy keeps it", RepeatedReading.Never,
                ((Header)new Header { RepeatedReading = RepeatedReading.Never }.Clone()).RepeatedReading, 0);
            AssertEqual(testName, "a footer's copy keeps it", RepeatedReading.Never,
                ((Footer)new Footer { RepeatedReading = RepeatedReading.Never }.Clone()).RepeatedReading, 0);

            // --- and it changes nothing that is drawn ---------------------------------------------
            static string Proof(Report report, ITextMeasurer measurer)
            {
                var svg = new SvgBackend(measurer);
                ReportRenderer.Render(report, svg);
                return string.Concat(svg.Pages);
            }
            var measurer = new FontTextMeasurer(library);
            AssertEqual(testName, "the pages are the same whichever is chosen", true,
                Proof(WithBands(RepeatedReading.Once), measurer) == Proof(WithBands(RepeatedReading.Never), measurer)
                && Proof(WithBands(RepeatedReading.Once), measurer) == Proof(WithBands(RepeatedReading.EveryPage), measurer), 0);

            Console.WriteLine();
        }

        /// <summary>The pagination marks and the reordered tree, shown to the validator on both profiles.</summary>
        [SkippableTheory]
        [InlineData("ua1", RepeatedReading.Once)]
        [InlineData("ua1", RepeatedReading.EveryPage)]
        [InlineData("ua1", RepeatedReading.Never)]
        [InlineData("3b", RepeatedReading.Once)]
        public void Test113_ABandIsValidHoweverItIsRead(string flavour, RepeatedReading reading)
        {
            string testName = $"Test 113: veraPDF {flavour}, read {reading}";
            Console.WriteLine($"Running {testName}...");

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null, "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH.");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            Report report = WithBands(reading);
            report.Footer!.Add(new FieldItem { Name = "initials", Kind = FieldKind.Text, AltText = "Initials" });
            report.Reattach();

            var pdf = new PdfBackend(library) { Tagged = true, Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            ReportRenderer.Render(report, pdf);

            string path = Path.Combine(Path.GetTempPath(), $"frisket-bands-{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(path, pdf.ToArray());

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(verapdf!)
                {
                    Arguments = $"--format text --flavour {flavour} \"{path}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                }
            };
            process.Start();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(180_000);
            File.Delete(path);

            Console.WriteLine($"    {output.Trim()}");
            AssertEqual(testName, $"the document is valid {flavour}", true, output.Contains("PASS", StringComparison.Ordinal), 0);
        }

        /// <summary>
        /// Every shape a role takes around a table that the engine writes correctly: decoration
        /// covering what it holds, and a cell that is something more than a cell keeping its
        /// place in the row. Each word says which shape it stands in.
        /// </summary>
        private static Report AroundATable()
        {
            static TextItem Plain(string text, PdfRole role = PdfRole.Auto) => new() { Text = text, Role = role };
            static AdvancedTextItem Rich(PdfRole role, params string[] runs)
            {
                var item = new AdvancedTextItem { Role = role };
                foreach (string run in runs)
                    item.Runs.Add(new TextRun { Text = run });
                return item;
            }

            var report = new Report
            {
                Title = "Around a table",
                Language = "en-GB",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack },
            };
            report.Body!.Add(Plain("Before", PdfRole.Heading1));

            // A grid drawn as a backdrop, as the host that found this wrote it.
            var backdrop = new TableItem { Role = PdfRole.Artifact, Grid = new PenStyle { Width = 0.2 } };
            backdrop.AddColumn(40).AddColumn(40);
            backdrop.AddHeaderRow(Plain("BackHead1"), Plain("BackHead2"));
            backdrop.AddRow(Plain("BackA"), Plain("BackB"));
            report.Body!.Add(backdrop);

            // A group set aside, holding a table, and a control - which is read all the same.
            var group = new ContainerItem { Layout = LayoutMode.Stack, Role = PdfRole.Artifact };
            group.Add(Plain("GroupWords"));
            var inGroup = new TableItem();
            inGroup.AddColumn(30);
            inGroup.AddRow(Plain("GroupCell"));
            group.Add(inGroup);
            group.Add(new FieldItem { Name = "initials", Kind = FieldKind.Text, Tooltip = "Initials" });
            report.Body!.Add(group);

            var table = new TableItem { Grid = new PenStyle { Width = 0.2 } };
            table.AddColumn(60).AddColumn(60);
            table.AddHeaderRow(Plain("Head1"), Plain("Head2"));
            table.AddRow(Plain("Plain1"), Plain("CellHeading", PdfRole.Heading2));
            table.AddRow(Plain("CellDecor", PdfRole.Artifact), Plain("Plain2"));
            table.AddRow(Plain("RowDecor1"), Plain("RowDecor2")).Role = PdfRole.Artifact;

            var nested = new TableItem();
            nested.AddColumn(25).AddColumn(25);
            nested.AddHeaderRow(Plain("NestedHead1"), Plain("NestedHead2"));
            nested.AddRow(Plain("NestedX"), Plain("NestedY"));
            table.AddRow(nested, Rich(PdfRole.Artifact, "Rich", "Decor"));

            var held = new ContainerItem { Layout = LayoutMode.Stack, Role = PdfRole.Section };
            held.Add(Plain("InGroup1"));
            held.Add(Plain("InGroup2"));
            table.AddRow(held, Rich(PdfRole.Paragraph, "RichPara ", "Two"));
            report.Body!.Add(table);

            report.Body!.Add(Plain("After"));
            report.Reattach();
            return report;
        }

        /// <summary>
        /// A role on a table, on what is in one or around one, makes a tree a reader can follow.
        ///
        /// Reported on 9 November 2026 by a host whose backdrop grid was a table marked Artifact:
        /// its rows came out as rows under the section with no table over them, refused by PDF/UA
        /// 7.2, and `frisket check --tagged` said nothing. Decoration had covered the frame of the
        /// item marked and nothing in it. Asking who else had it found a cell given any role of its
        /// own - a heading, decoration, a nested table - leaving its row a cell short or its rows
        /// under a TD, all refused, and a group set aside having every word in it read. What has no
        /// right reading - a table given another role, a row given one - the validator now names.
        /// </summary>
        [Fact]
        public void Test130_ARoleAroundATableMakesATreeAReaderCanFollow()
        {
            const string testName = "Test 130";
            Console.WriteLine("Test 130: roles on a table, in one and around one");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            Report report = AroundATable();
            var pdf = new PdfBackend(library) { Tagged = true, Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            ReportRenderer.Render(report, pdf);
            Reading reading = ReadAloud(pdf.ToArray());

            foreach (string said in reading.Lineage)
                Console.WriteLine($"    {said}");
            Console.WriteLine($"    rows: {string.Join(" | ", reading.Rows)}");
            Console.WriteLine($"    skipped: {string.Join(" | ", reading.Skipped)}");

            AssertEqual(testName, "the two halves of the tree and the page reach each other", "",
                string.Join(" | ", reading.Faults), 0);

            // --- decoration covers what it holds ----------------------------------------------
            string heard = string.Join(" ", reading.Heard.Select(h => h.Words));
            string skipped = string.Join(" ", reading.Skipped);
            foreach (string decor in new[] { "BackHead1", "BackA", "GroupWords", "GroupCell", "CellDecor", "RowDecor1", "Rich Decor" })
            {
                AssertEqual(testName, $"\"{decor}\" is not read", false, heard.Contains(decor, StringComparison.Ordinal), 0);
                AssertEqual(testName, $"and is drawn as decoration", true, skipped.Contains(decor, StringComparison.Ordinal), 0);
            }

            AssertEqual(testName, "only the two tables that are read are tables", 2, reading.Tags.Count(tag => tag == "Table"), 0);
            AssertEqual(testName, "a control set aside is read all the same", 1, reading.Tags.Count(tag => tag == "Form"), 0);

            // --- a cell keeps its place, and what it is goes under it -------------------------
            // The rows in the order the tree gives them: the nested table's two come inside the
            // fourth row of the outer one, and the row set aside is no row at all.
            AssertEqual(testName, "every row of the read tables holds a cell for each column",
                "TH TH | TD TD | TD TD | TD TD | TH TH | TD TD | TD TD",
                string.Join(" | ", reading.Rows), 0);

            bool Said(string lineage) => reading.Lineage.Any(said => said.EndsWith(lineage, StringComparison.Ordinal));
            AssertEqual(testName, "a heading in a cell is a heading in the cell", true, Said("/Table/TR/TD/H2: CellHeading"), 0);
            AssertEqual(testName, "a nested table's rows are under its table, in the cell", true,
                Said("/Table/TR/TD/Table/TR/TD: NestedX"), 0);
            AssertEqual(testName, "what a group in a cell holds is under the group", true,
                Said("/Table/TR/TD/Sect/P: InGroup1"), 0);
            AssertEqual(testName, "and a rich cell's pieces join what it is", true, Said("/Table/TR/TD/P: RichPara Two"), 0);
            AssertEqual(testName, "the text after the tables comes after them", "P: After",
                $"{reading.Heard[^1].Tag}: {reading.Heard[^1].Words}", 0);

            // --- and the validator accuses none of it -------------------------------------------
            IReadOnlyList<TemplateIssue> found = TemplateValidator.Check(report, TemplateProfile.All, new RenderTarget());
            foreach (TemplateIssue issue in found)
                Console.WriteLine($"    check: {issue.Rule} {issue.Where}");
            AssertEqual(testName, "the validator finds nothing wrong with shapes written right", "",
                string.Join(", ", found.Select(i => i.Rule)), 0);

            // Flattened, the control set aside is decoration with the rest and owes no description;
            // what it loses is its filling in, and that is said.
            AssertEqual(testName, "flattened, only the filling in is lost", "can.flattenedFields",
                string.Join(", ", TemplateValidator.Check(report, TemplateProfile.All,
                    new RenderTarget(Tagged: true, Interactive: false, Paged: true)).Select(i => i.Rule)), 0);

            // --- what has no right reading, the validator names --------------------------------
            static (Report Report, BaseItem Item) Shape(Func<TableItem, BaseItem> spoil)
            {
                var report = new Report
                {
                    Title = "t", Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack },
                };
                var table = new TableItem();
                table.AddColumn(40);
                table.AddHeaderRow(new TextItem { Text = "Head" });
                table.AddRow(new TextItem { Text = "Cell" });
                report.Body!.Add(table);
                BaseItem item = spoil(table);
                if (item.Parent == null)
                    report.Body!.Add(item);
                report.Reattach();
                return (report, item);
            }

            var refused = new (string Name, Func<TableItem, BaseItem> Spoil)[]
            {
                ("a table read as a paragraph", t => { t.Role = PdfRole.Paragraph; return t; }),
                ("a table read as a figure", t => { t.Role = PdfRole.Figure; t.AltText = "A grid"; return t; }),
                ("a row read as a paragraph", t => { BaseItem row = t.Childs[^1]; row.Role = PdfRole.Paragraph; return row; }),
                ("a cell outside a row", _ => new TextItem { Text = "Loose", Role = PdfRole.TableCell }),
                ("a group read as a table", _ => new ContainerItem { Layout = LayoutMode.Stack, Role = PdfRole.Table }),
                ("a cell read as a row", t => { BaseItem cell = ((TableRow)t.Childs[^1]).Childs[0]; cell.Role = PdfRole.TableRow; return cell; }),
            };

            foreach ((string name, Func<TableItem, BaseItem> spoil) in refused)
            {
                (Report shape, BaseItem item) = Shape(spoil);
                AssertEqual(testName, $"{name} is named", true, TemplateValidator.Check(shape)
                    .Any(i => i.Rule == "ua.tableRole" && ReferenceEquals(i.Item, item)), 0);
            }

            var allowed = new (string Name, Func<TableItem, BaseItem> Spoil)[]
            {
                ("a table set aside", t => { t.Role = PdfRole.Artifact; return t; }),
                ("a row set aside", t => { BaseItem row = t.Childs[^1]; row.Role = PdfRole.Artifact; return row; }),
                ("a cell heading its row", t => { BaseItem cell = ((TableRow)t.Childs[^1]).Childs[0]; cell.Role = PdfRole.TableHeader; return cell; }),
                ("a cell holding a heading", t => { BaseItem cell = ((TableRow)t.Childs[^1]).Childs[0]; cell.Role = PdfRole.Heading2; return cell; }),
                ("a table given its own role", t => { t.Role = PdfRole.Table; return t; }),
            };

            foreach ((string name, Func<TableItem, BaseItem> spoil) in allowed)
            {
                (Report shape, _) = Shape(spoil);
                AssertEqual(testName, $"{name} is not", "", string.Join(", ", TemplateValidator.Check(shape).Select(i => i.Rule)), 0);
            }

            // A header row set aside announces nothing, so the table is read with no header.
            (Report headless, BaseItem table) = Shape(t => { t.Childs[0].Role = PdfRole.Artifact; return t; });
            AssertEqual(testName, "a header row set aside is no header", true, TemplateValidator.Check(headless)
                .Any(i => i.Rule == "ua.tableHeader" && ReferenceEquals(i.Item, table)), 0);

            Console.WriteLine();
        }

        /// <summary>The same shapes, shown to the validator on both profiles.</summary>
        [SkippableTheory]
        [InlineData("ua1")]
        [InlineData("3b")]
        public void Test131_ARoleAroundATableIsValid(string flavour)
        {
            string testName = $"Test 131: veraPDF {flavour}, roles around a table";
            Console.WriteLine($"Running {testName}...");

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null, "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH.");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // Both outputs: interactive, the control set aside is a Form at the root; flattened,
            // it is decoration with the rest.
            foreach (bool interactive in new[] { true, false })
            {
                var pdf = new PdfBackend(library)
                {
                    Tagged = true, Interactive = interactive, Conformance = PdfConformance.PdfA3b, Language = "en-GB"
                };
                ReportRenderer.Render(AroundATable(), pdf);

                string path = Path.Combine(Path.GetTempPath(), $"frisket-roles-{Guid.NewGuid():N}.pdf");
                File.WriteAllBytes(path, pdf.ToArray());

                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(verapdf!)
                    {
                        Arguments = $"--format text --flavour {flavour} \"{path}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    }
                };
                process.Start();
                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(180_000);
                File.Delete(path);

                string mode = interactive ? "interactive" : "flattened";
                Console.WriteLine($"    {mode}: {output.Trim()}");
                AssertEqual(testName, $"the {mode} document is valid {flavour}", true, output.Contains("PASS", StringComparison.Ordinal), 0);
            }
        }
    }
}
