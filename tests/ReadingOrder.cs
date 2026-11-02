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

            /// <summary>The tags directly under the document.</summary>
            public List<string> AtTheTop { get; } = new();

            /// <summary>Where the two halves of the tree, or the tree and the page, do not reach each other.</summary>
            public List<string> Faults { get; } = new();
        }

        private static readonly Regex ContentToken = new(
            @"/(?<tag>\w+) <</MCID (?<mcid>\d+)>> BDC"
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
                        words.Clear();
                    }
                    else if (token.Groups["end"].Success)
                    {
                        string said = Regex.Replace(words.ToString(), @"\s+", " ").Trim();
                        if (open >= 0)
                            marks[(pages[index], open)] = said;
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

            void Walk(PdfReference reference, int depth)
            {
                var element = (PdfDictionary)file.Get(reference.Number)!;
                string tag = ((PdfName)file.Resolve(element["S"])!).Value;
                if (depth == 1)
                    reading.AtTheTop.Add(tag);

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
                }

                foreach (PdfReference child in kids)
                    Walk(child, depth + 1);
            }

            PdfDictionary root = Dictionary(catalogue["StructTreeRoot"]);
            Walk((PdfReference)root["K"]!, 0);

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
    }
}
