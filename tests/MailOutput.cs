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
