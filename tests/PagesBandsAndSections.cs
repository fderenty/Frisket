namespace test
{
    /// <summary>
    /// How a document is divided: header, body and footer bands, the flow across pages,
    /// breaks and splitting, keeping a block whole or keeping it with what follows, back
    /// pages, page size, and sections.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test3_AnchorLayouts()
        {
            var testName = "Test 3: Anchor Layouts";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                Header = new(),
                Body = new(),
                Footer = new()
            };

            // Header: anchored to top, with height
            r.Header.Style.Location.Height = (50, Units.Millimeter);

            // Footer: anchored to bottom, with height
            r.Footer.Style.Location.Height = (50, Units.Millimeter);

            r.Resolve();
            SaveReport(r, "test3_output.json");

            AssertEqual(testName, "Header.Left", 0, r.Header.Style.RenderedFrame.Left, 0.1);
            AssertEqual(testName, "Header.Top", 0, r.Header.Style.RenderedFrame.Top, 0.1);
            AssertEqual(testName, "Header.Right", 210, r.Header.Style.RenderedFrame.Right, 0.1);
            AssertEqual(testName, "Header.Height", 50, r.Header.Style.RenderedFrame.Height, 0.1);

            AssertEqual(testName, "Body.Left", 0, r.Body.Style.RenderedFrame.Left, 0.1);
            AssertEqual(testName, "Body.Right", 210, r.Body.Style.RenderedFrame.Right, 0.1);

            // The body gets what is left once the header and the footer are reserved:
            // 297 - 50 - 50. It used to cover the whole page and overlap both.
            AssertEqual(testName, "Body.Top below header", 50, r.Body.Style.RenderedFrame.Top, 0.1);
            AssertEqual(testName, "Body.Bottom above footer", 247, r.Body.Style.RenderedFrame.Bottom, 0.1);
            AssertEqual(testName, "Body height between bands", 197, r.Body.Style.RenderedFrame.Height, 0.1);

            AssertEqual(testName, "Footer.Bottom", 297, r.Footer.Style.RenderedFrame.Bottom, 0.1);
            AssertEqual(testName, "Footer.Height", 50, r.Footer.Style.RenderedFrame.Height, 0.1);
            
            Console.WriteLine();
        }

        [Fact]
        public void Test22_AutoSizedBands()
        {
            var testName = "Test 22: Auto-sized Bands";
            Console.WriteLine($"Running {testName}...");

            // The French invoice footer: totals on the last page, a centred notice on the
            // others. No height is stated - the band must size itself, and identically on
            // every page of the section.
            var r = new Report
            {
                Title = "Facture",
                Dimension = new() { Width = 210, Height = 297 },
                Header = new(),
                Footer = new(),
                Body = new()
            };

            r.Header!.Add(Label("Full header, repeated on every page", 1.5, bold: true));
            r.Header.Add(Label("Customer address"));

            r.Footer!.Layout = LayoutMode.Stack;

            var notice = Label("TOTALS ON THE LAST PAGE", 1, false, HorizontalAlignment.Center);
            notice.Style.PageScope = PageScope.ExceptLast;
            r.Footer.Add(notice);

            var totals = new ContainerItem { Layout = LayoutMode.Stack };
            totals.Style.PageScope = PageScope.Last;
            totals.Add(Label("Total HT      1 234,00 €", 1, false, HorizontalAlignment.Right));
            totals.Add(Label("TVA 20%         246,80 €", 1, false, HorizontalAlignment.Right));
            totals.Add(Label("Total TTC     1 480,80 €", 1, true, HorizontalAlignment.Right));
            r.Footer.Add(totals);

            for (int i = 0; i < 8; i++)
                r.Body!.Add(Block($"Bloc {i}", 60));

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            var layout = r.Resolve(new FontTextMeasurer(library));

            AssertEqual(testName, "several pages", true, layout.PageCount > 1, 0);

            // The header sized itself from its two lines.
            var head = layout.Pages[0].HeaderBand;
            AssertEqual(testName, "header sized itself", true, head.Height > 0 && head.Height < 30,
                0);
            AssertEqual(testName, "header starts at the top", 0, head.Top, 0.01);

            // The footer reserved room for the tallest variant - the three totals lines -
            // not for the single-line notice that page one actually shows.
            var footerBands = layout.Pages.Select(p => p.FooterBand).ToList();
            AssertEqual(testName, "footer band identical on every page", true,
                footerBands.All(b => Math.Abs(b.Height - footerBands[0].Height) < 0.001), 0);

            // The band was reserved for the taller variant - the totals block - not for
            // the single-line notice that page one happens to show.
            int lastPage = layout.PageCount - 1;
            double totalsHeight = layout.Pages[lastPage].Items
                .First(p => ReferenceEquals(p.Source, totals)).Frame.Height;
            double noticeHeight = layout.Pages[0].Items
                .First(p => ReferenceEquals(p.Source, notice)).Frame.Height;

            AssertEqual(testName, "footer sized for the tallest variant", totalsHeight, footerBands[0].Height, 0.01);
            AssertEqual(testName, "the notice alone would have been shorter", true, noticeHeight < totalsHeight - 1, 0);
            AssertEqual(testName, "both variants start at the same edge", 
                layout.Pages[lastPage].Items.First(p => ReferenceEquals(p.Source, totals)).Frame.Top,
                layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, notice)).Frame.Top, 0.01);

            AssertEqual(testName, "footer sits at the page bottom", 297, footerBands[0].Bottom, 0.01);
            AssertEqual(testName, "body band stops above the footer",
                footerBands[0].Top, layout.Pages[0].BodyBand.Bottom, 0.01);

            // Each page shows the right variant.
            int last = lastPage;
            bool CountOnPage(Page p, BaseItem src) => p.Items.Any(i => ReferenceEquals(i.Source, src));

            AssertEqual(testName, "notice on the first page", true, CountOnPage(layout.Pages[0], notice), 0);
            AssertEqual(testName, "no totals on the first page", false, CountOnPage(layout.Pages[0], totals), 0);
            AssertEqual(testName, "totals on the last page", true, CountOnPage(layout.Pages[last], totals), 0);
            AssertEqual(testName, "no notice on the last page", false, CountOnPage(layout.Pages[last], notice), 0);
            AssertEqual(testName, "header repeated on every page", true,
                layout.Pages.All(p => CountOnPage(p, r.Header)), 0);

            // A single-page invoice needs no special case: page one is the last page.
            var solo = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                Header = new(), Footer = new(), Body = new()
            };
            solo.Footer!.Layout = LayoutMode.Stack;
            var soloNotice = Label("TOTALS ON THE LAST PAGE");
            soloNotice.Style.PageScope = PageScope.ExceptLast;
            var soloTotals = Label("Total TTC 1 480,80 €");
            soloTotals.Style.PageScope = PageScope.Last;
            solo.Footer.Add(soloNotice);
            solo.Footer.Add(soloTotals);
            solo.Body!.Add(Block("a single line", 40));

            var one = solo.Resolve(new FontTextMeasurer(library));
            AssertEqual(testName, "single page", 1, one.PageCount, 0);
            AssertEqual(testName, "totals shown when page 1 is the last", true,
                one.Pages[0].Items.Any(i => ReferenceEquals(i.Source, soloTotals)), 0);
            AssertEqual(testName, "notice hidden when page 1 is the last", false,
                one.Pages[0].Items.Any(i => ReferenceEquals(i.Source, soloNotice)), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test8_Pagination()
        {
            var testName = "Test 8: Pagination";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);
            var blocks = new List<TextItem>();
            for (int i = 0; i < 5; i++)
            {
                var block = Block($"Block {i}", 100);
                blocks.Add(block);
                r.Body!.Add(block);
            }

            var layout = r.Resolve();

            // Band is 257mm tall, blocks are 100mm: two per page, so five need three pages.
            AssertEqual(testName, "page count", 3, layout.PageCount, 0);
            AssertEqual(testName, "body band top", 20, layout.Pages[0].BodyBand.Top, 0.1);
            AssertEqual(testName, "body band bottom", 277, layout.Pages[0].BodyBand.Bottom, 0.1);

            AssertEqual(testName, "block 0 on page 1", 1, CountOn(layout.Pages[0], blocks[0]), 0);
            AssertEqual(testName, "block 1 on page 1", 1, CountOn(layout.Pages[0], blocks[1]), 0);
            AssertEqual(testName, "block 2 not on page 1", 0, CountOn(layout.Pages[0], blocks[2]), 0);
            AssertEqual(testName, "block 2 on page 2", 1, CountOn(layout.Pages[1], blocks[2]), 0);
            AssertEqual(testName, "block 4 on page 3", 1, CountOn(layout.Pages[2], blocks[4]), 0);

            // The flow restarts at the top of the band on every page.
            var first = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, blocks[0]));
            var third = layout.Pages[1].Items.First(p => ReferenceEquals(p.Source, blocks[2]));
            AssertEqual(testName, "first block starts at band top", 20, first.Frame.Top, 0.1);
            AssertEqual(testName, "page 2 restarts at band top", 20, third.Frame.Top, 0.1);
            AssertEqual(testName, "second block stacks below", 120, layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, blocks[1])).Frame.Top, 0.1);

            // Header and footer repeat on every page.
            foreach (var page in layout.Pages)
            {
                AssertEqual(testName, $"header on page {page.Number}", 1, CountOn(page, r.Header!), 0);
                AssertEqual(testName, $"footer on page {page.Number}", 1, CountOn(page, r.Footer!), 0);
            }

            Console.WriteLine();
        }

        [Fact]
        public void Test9_PageBreaks()
        {
            var testName = "Test 9: Forced Page Breaks";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);
            var a = Block("A", 30);
            var b = Block("B", 30);
            var c = Block("C", 30);
            b.Style.PageBreak = PageBreak.Before;
            r.Body!.Add(a);
            r.Body.Add(b);
            r.Body.Add(c);

            var layout = r.Resolve();

            // All three would fit on one page; the break splits them 1 + 2.
            AssertEqual(testName, "page count", 2, layout.PageCount, 0);
            AssertEqual(testName, "A on page 1", 1, CountOn(layout.Pages[0], a), 0);
            AssertEqual(testName, "B pushed to page 2", 1, CountOn(layout.Pages[1], b), 0);
            AssertEqual(testName, "C follows B", 1, CountOn(layout.Pages[1], c), 0);
            AssertEqual(testName, "B at band top", 20, layout.Pages[1].Items.First(p => ReferenceEquals(p.Source, b)).Frame.Top, 0.1);

            Console.WriteLine();
        }

        [Fact]
        public void Test10_TextSplitting()
        {
            var testName = "Test 10: Text Splitting";
            Console.WriteLine($"Running {testName}...");

            const int lineCount = 120;
            var r = BandedReport(testName);
            var paragraph = new TextItem
            {
                Text = string.Join("\n", Enumerable.Range(0, lineCount).Select(i => $"line {i}"))
            };
            r.Body!.Add(paragraph);

            var layout = r.Resolve();

            AssertEqual(testName, "paragraph spans several pages", true, layout.PageCount > 1, 0);

            // Every line must appear exactly once, in order, across the fragments.
            var rebuilt = new List<string>();
            foreach (var page in layout.Pages)
                foreach (var placed in page.Items.Where(p => ReferenceEquals(p.Source, paragraph)))
                    rebuilt.AddRange(placed.Fragment!.Split('\n'));

            AssertEqual(testName, "no line lost or duplicated", lineCount, rebuilt.Count, 0);
            AssertEqual(testName, "lines stay in order", true, rebuilt.SequenceEqual(paragraph.Text.Split('\n')), 0);

            foreach (var page in layout.Pages)
            {
                var placed = page.Items.First(p => ReferenceEquals(p.Source, paragraph));
                AssertEqual(testName, $"page {page.Number} fragment inside band", true,
                    placed.Frame.Top >= page.BodyBand.Top - 0.01 && placed.Frame.Bottom <= page.BodyBand.Bottom + 0.01, 0);
            }

            // KeepTogether must refuse to split: the whole paragraph goes on one page.
            paragraph.Style.Splitting = Splitting.KeepTogether;
            var whole = r.Resolve();
            AssertEqual(testName, "KeepTogether stays on one page", 1, whole.PageCount, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test11_ContainerSplittingAndScope()
        {
            var testName = "Test 11: Container Splitting and Page Scope";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);
            var table = new ContainerItem { Layout = LayoutMode.Stack };
            r.Body!.Add(table);

            var rows = new List<TextItem>();
            for (int i = 0; i < 8; i++)
            {
                var row = Block($"Row {i}", 50);
                rows.Add(row);
                table.Add(row);
            }

            // A watermark hung off the section, shown on the last page only.
            var watermark = new TextItem
            {
                Text = "LAST",
                Style = new() { PageScope = PageScope.Last, Location = new() { Top = (100, Units.Millimeter) } }
            };
            r.Section.Add(watermark);

            var layout = r.Resolve();

            // 8 rows of 50mm in a 257mm band: five fit, then three.
            AssertEqual(testName, "page count", 2, layout.PageCount, 0);
            AssertEqual(testName, "rows on page 1", 5, layout.Pages[0].Items.Count(p => rows.Contains(p.Source)), 0);
            AssertEqual(testName, "rows on page 2", 3, layout.Pages[1].Items.Count(p => rows.Contains(p.Source)), 0);
            AssertEqual(testName, "container appears on both pages", 1, CountOn(layout.Pages[1], table), 0);
            AssertEqual(testName, "row 5 restarts at band top", 20,
                layout.Pages[1].Items.First(p => ReferenceEquals(p.Source, rows[5])).Frame.Top, 0.1);

            AssertEqual(testName, "watermark absent from page 1", 0, CountOn(layout.Pages[0], watermark), 0);
            AssertEqual(testName, "watermark on last page", 1, CountOn(layout.Pages[1], watermark), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A container told to stay whole stays whole.
        ///
        /// Splitting.KeepTogether was read in the two text paths and nowhere else, so on
        /// a stacked container it was a legal value with no effect: the block split
        /// exactly as it would have anyway. Section 10, in a new place - and found the
        /// way that one was, by asking for something reasonable and watching it not
        /// happen. A heading and its caption were asked to travel with the barcodes they
        /// describe; the barcodes arrived on a page of their own with nothing to say
        /// what they were.
        ///
        /// The check that matters is the comparison. "It did not split" proves nothing
        /// on its own, since a block that fits never splits: what proves it is the same
        /// block splitting under Auto and moving whole under KeepTogether.
        /// </summary>
        [Fact]
        public void Test64_KeepTogetherKeepsAContainerWhole()
        {
            const string testName = "Test 64";
            Console.WriteLine("Test 64: a container told to stay whole");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            // Filler enough that the block starts near the foot of the page, and a block
            // long enough that the remainder cannot hold it - so Auto has somewhere to
            // cut. Both halves matter: a block that fits proves nothing either way.
            static Report Build(Splitting how)
            {
                var report = new Report { Title = "t", Language = "en", Body = new Body { Layout = LayoutMode.Stack } };
                // Tall enough that the block comfortably fits a fresh page, with filler
                // enough that it cannot fit where it starts. Both are needed: a block
                // taller than any page has to split, and one that fits anywhere never
                // splits - neither says anything about KeepTogether.
                report.Section.Dimension = new DocumentDimension { Width = 210, Height = 200 };

                // 27 rather than a round number: found by sweeping, because a sample
                // has to land in the window where Auto splits and a fresh page still
                // holds the block. Outside it the comparison is between two things that
                // both fit, or two that both cannot - and says nothing either way.
                for (int i = 0; i < 27; i++)
                    report.Body!.Add(new TextItem { Text = $"Filler {i}", Font = new TextStyle { Size = 3 } });

                var block = new ContainerItem { Layout = LayoutMode.Stack };
                block.Style.Splitting = how;
                block.Add(new TextItem { Text = "HEADING", Font = new TextStyle { Size = 3, Bold = true } });
                block.Add(new TextItem { Text = "CAPTION", Font = new TextStyle { Size = 3 } });
                for (int i = 0; i < 6; i++)
                    block.Add(new TextItem { Text = $"body {i}", Font = new TextStyle { Size = 3 } });

                report.Body!.Add(block);
                return report;
            }

            static Dictionary<string, int> Where(Report report, FontTextMeasurer measurer)
            {
                var found = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Page page in report.Resolve(measurer).Pages)
                    foreach (PlacedItem placed in page.Items)
                        if (placed.Fragment is { Length: > 0 } text && !found.ContainsKey(text))
                            found[text] = page.Number;
                return found;
            }

            Dictionary<string, int> loose = Where(Build(Splitting.Auto), measurer);
            Dictionary<string, int> whole = Where(Build(Splitting.KeepTogether), measurer);

            Console.WriteLine($"    Auto          HEADING p{loose["HEADING"]}, last line p{loose["body 5"]}");
            Console.WriteLine($"    KeepTogether  HEADING p{whole["HEADING"]}, last line p{whole["body 5"]}");

            // The premise: without it the comparison below is between two things that
            // both happen to fit, and says nothing.
            AssertEqual(testName, "Auto splits this block, so there is something to keep together",
                true, loose["HEADING"] != loose["body 5"], 0);

            AssertEqual(testName, "and KeepTogether puts every part on one page",
                true, whole["HEADING"] == whole["body 5"] && whole["CAPTION"] == whole["HEADING"], 0);

            // Moved rather than squeezed: it goes to a fresh page, it does not shrink.
            AssertEqual(testName, "by moving the whole block on, not by fitting it in",
                true, whole["HEADING"] > loose["HEADING"], 0);

            Console.WriteLine();
        }

        /// <summary>
        /// <summary>
        /// A heading is not left at the foot of a page.
        ///
        /// Its own property rather than a third Splitting value, because the two are
        /// orthogonal: "do not split me" and "do not leave me at the bottom" are
        /// different requests, and a heading - one line - never needs the first. An enum
        /// growing by combinations is the start of a matrix nobody keeps up to date.
        ///
        /// Two things have to be true at once for this to mean anything, and they are
        /// what the sample is sized for: the break must fall between the heading and
        /// what follows without the property, and the page after must have room. Outside
        /// that window there is nothing to move, and a test that says "they are on the
        /// same page" is describing a document that never had a problem.
        ///
        /// The two traps are what the last three checks are for. Walking back one step
        /// strands whatever is above it - the same fault one item higher - and pushing
        /// back with no floor lets a run of items shove each other off the page forever.
        /// </summary>
        [Fact]
        public void Test65_AHeadingStaysWithWhatItAnnounces()
        {
            const string testName = "Test 65";
            Console.WriteLine("Test 65: a heading stays with what it announces");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static Report Build(int filler, bool[] keep, int tail)
            {
                var report = new Report { Title = "t", Language = "en", Body = new Body { Layout = LayoutMode.Stack } };
                report.Section.Dimension = new DocumentDimension { Width = 210, Height = 200 };

                for (int i = 0; i < filler; i++)
                    report.Body!.Add(new TextItem { Text = $"Filler {i}", Font = new TextStyle { Size = 3 } });

                string[] names = { "HEADING", "RULE", "CAPTION" };
                for (int i = 0; i < names.Length; i++)
                {
                    var lead = new TextItem { Text = names[i], Font = new TextStyle { Size = 3 } };
                    lead.Style.KeepWithNext = keep[i];
                    report.Body!.Add(lead);
                }

                for (int i = 0; i < tail; i++)
                    report.Body!.Add(new TextItem { Text = $"CONTENT {i}", Font = new TextStyle { Size = 3 } });

                return report;
            }

            Dictionary<string, int> Pages(Report report)
            {
                var found = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Page page in report.Resolve(measurer).Pages)
                    foreach (PlacedItem placed in page.Items)
                        if (placed.Fragment is { Length: > 0 } text && !found.ContainsKey(text))
                            found[text] = page.Number;
                return found;
            }

            bool[] none = { false, false, false };
            bool[] all = { true, true, true };

            // --- the premise, without which nothing below means anything -------------
            // 25 rather than 27, and found by sweeping. At 27 the break already falls
            // between the heading and the rule, so walking back one step is enough and
            // the cascade is never exercised - a sabotage replacing the loop with a
            // single step passed. 25 is the only width where all three lead-in items sit
            // on the page and only the content spills, which is what makes walking back
            // three of them necessary.
            Dictionary<string, int> loose = Pages(Build(25, none, 6));
            Console.WriteLine($"    unmarked   HEADING p{loose["HEADING"]}, CONTENT p{loose["CONTENT 0"]}");
            AssertEqual(testName, "without it the heading is stranded above the break",
                true, loose["HEADING"] < loose["CONTENT 0"], 0);

            // --- and with it, the whole lead-in follows -------------------------------
            Dictionary<string, int> kept = Pages(Build(25, all, 6));
            Console.WriteLine($"    marked     HEADING p{kept["HEADING"]}, CONTENT p{kept["CONTENT 0"]}");
            AssertEqual(testName, "with it the heading arrives with what it announces",
                kept["CONTENT 0"], kept["HEADING"], 0);

            // Walked back, not stepped back once: three in a row all ask, and moving only
            // the last would strand the other two.
            AssertEqual(testName, "and so do the rule and the caption between them",
                true, kept["RULE"] == kept["HEADING"] && kept["CAPTION"] == kept["HEADING"], 0);

            // --- one asking alone moves only itself ----------------------------------
            Dictionary<string, int> onlyCaption = Pages(Build(27, new[] { false, false, true }, 6));
            Console.WriteLine($"    caption only  HEADING p{onlyCaption["HEADING"]}, "
                              + $"CAPTION p{onlyCaption["CAPTION"]}");
            AssertEqual(testName, "an item that did not ask is left where it fell",
                true, onlyCaption["HEADING"] < onlyCaption["CAPTION"], 0);

            // --- and it terminates ---------------------------------------------------
            // *Every* item asking, filler included - marking only the three lead-in items
            // proves nothing, since the walk back stops at the first one that did not ask
            // and never reaches the floor at all. That was the first version of this
            // check, and removing the floor left it green.
            //
            // Without a floor at the first item on a page, each item pushes the one
            // before it off, the page empties, and the layout never converges - it runs
            // into MaxPages and throws.
            var crowded = new Report { Title = "t", Language = "en", Body = new Body { Layout = LayoutMode.Stack } };
            crowded.Section.Dimension = new DocumentDimension { Width = 210, Height = 200 };
            for (int i = 0; i < 120; i++)
            {
                var line = new TextItem { Text = $"Every line asks {i}", Font = new TextStyle { Size = 3 } };
                line.Style.KeepWithNext = true;
                crowded.Body!.Add(line);
            }

            Exception? ranAway = Record.Exception(() => crowded.Resolve(measurer));
            Console.WriteLine($"    everything asking -> {ranAway?.GetType().Name ?? "laid out"}");
            AssertEqual(testName, "a page where everything asks still terminates",
                true, ranAway is null, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test37_BackPages()
        {
            var testName = "Test 37: Back pages";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            Report report = BoundAcmeInvoice(60);          // long enough to run to several pages

            Demonstrates(report.Body!,
                "Back pages",
                "Conditions of sale printed on the reverse of every sheet. Look for the "
                + "faces alternating front, back, front, back rather than the backs arriving "
                + "in a block at the end - that order is what makes duplex printing come out "
                + "right - for each back naming the sheet it belongs to, and for the numbering "
                + "on the front ignoring the backs entirely: an invoice reading page 1 of 2 "
                + "has four printed faces, and the reader counts what is written on the front.");

            var terms = new ContainerItem { Layout = LayoutMode.Stack };

            var title = new TextItem
            {
                Text = "TERMS AND CONDITIONS OF SALE",
                Font = new() { Size = 1.1, Bold = true }
            };
            terms.Add(title);

            for (int article = 1; article <= 6; article++)
            {
                var clause = new TextItem
                {
                    Text = $"Clause {article} - These terms apply to page "
                         + "{Page} of {Pages} of the invoice. They are taken to be known and "
                         + "accepted by the customer from the moment an order is placed.",
                    Font = new() { Size = 0.75 }
                };
                clause.Style.Margin.Top = 2;
                terms.Add(clause);
            }

            report.BackPage = new BackPage { Content = terms };

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            LayoutResult layout = ReportRenderer.Render(report, svg);

            Console.WriteLine($"    {layout.PageCount} page(s) numerotee(s), {layout.SheetCount} face(s) imprimee(s)");

            AssertEqual(testName, "there are fronts to back", true, layout.PageCount > 1, 0);
            AssertEqual(testName, "one back per front", layout.PageCount * 2, layout.SheetCount, 0);

            // The order is what makes duplex printing work: a back has to follow its own
            // front, not sit in a block at the end.
            var faces = layout.Pages.ToList();
            for (int i = 0; i < faces.Count; i += 2)
            {
                AssertEqual(testName, $"face {i + 1} is a front", false, faces[i].IsBack, 0);
                AssertEqual(testName, $"face {i + 2} is its back", true, faces[i + 1].IsBack, 0);
                AssertEqual(testName, $"and carries sheet {faces[i].Number}",
                    faces[i].Number, faces[i + 1].Number, 0);
            }

            // The clauses must actually stack. Sent down the absolute path a stacked
            // container gives every child the position it never set, so all of them land
            // at the top of the sheet printed over one another - a back page that reads
            // as a single black smear, and every assertion above still holds.
            var lines = faces[1].Items
                .Where(i => i.Source is TextItem)
                .Select(i => Math.Round(i.Frame.Top, 2))
                .ToList();

            AssertEqual(testName, "every clause is on the sheet", terms.Childs.Count, lines.Count, 0);
            AssertEqual(testName, "and none is printed over another", lines.Count, lines.Distinct().Count(), 0);
            AssertEqual(testName, "they run down the page", true,
                lines.Zip(lines.Skip(1), (a, b) => b > a).All(ordered => ordered), 0);

            // Not counted. An invoice reading "page 1 / 2" has four printed faces, and
            // the reader counts what is written on the front.
            AssertEqual(testName, "the count ignores the backs", true,
                svg.Pages[0].Contains($"Page 1 of {layout.PageCount}"), 0);

            // A back knows which sheet it belongs to, which is the point of letting it
            // see {Page} at all.
            AssertEqual(testName, "the back names its own sheet", true,
                svg.Pages[1].Contains("page 1 of " + layout.PageCount), 0);
            AssertEqual(testName, "and the next one names the next", true,
                svg.Pages[3].Contains("page 2 of " + layout.PageCount), 0);

            // A scanned page instead of typed clauses: an image is an item like any
            // other, so a back can be one picture filling the sheet.
            var scanned = new ContainerItem();
            var sheet = new ImageItem { Source = new() { Data = AcmeSphere(320), MediaType = "image/png" }, Scaling = ImageScaling.Uniform };
            sheet.Style.Location.Dock = Dock.Fill;
            scanned.Add(sheet);

            Report scan = BoundAcmeInvoice(60);
            scan.BackPage = new BackPage { Content = scanned };
            LayoutResult scannedOut = scan.Resolve(new FontTextMeasurer(library));

            AssertEqual(testName, "an image works as a back too", layout.PageCount * 2, scannedOut.SheetCount, 0);
            AssertEqual(testName, "and it covers the sheet", true,
                scannedOut.Pages[1].Items.Any(i => i.Source is ImageItem && i.Frame.Height > 200), 0);

            // It has to survive being written and read back, or a template holding
            // conditions of sale loses them the first time it is saved.
            Report reloaded = Report.FromJson(report.ToJson());
            AssertEqual(testName, "the back survives the round trip", true, reloaded.BackPage != null, 0);
            AssertEqual(testName, "with its clauses", terms.Childs.Count, reloaded.BackPage!.Content.Childs.Count, 0);
            AssertEqual(testName, "and lays out the same", layout.SheetCount,
                reloaded.Resolve(new FontTextMeasurer(library)).SheetCount, 0);

            string dir = OutputDirectory("backpage");
            svg.SaveTo(dir, "terms");

            var pdf = new PdfBackend(library);
            ReportRenderer.Render(report, pdf);
            WriteSamplePdf(Path.Combine(dir, "terms.pdf"), pdf.ToArray());

            // Only the last sheet, for a section whose conditions go once at the end.
            report.BackPage.Scope = BackPageScope.LastPage;
            LayoutResult once = report.Resolve(new FontTextMeasurer(library));
            AssertEqual(testName, "one back only, when asked", layout.PageCount + 1, once.SheetCount, 0);
            AssertEqual(testName, "and it backs the last sheet", once.PageCount,
                once.Pages.First(p => p.IsBack).Number, 0);

            Console.WriteLine($"  \U0001F4C4 {Path.Combine(dir, "terms.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// A sheet wider than it is tall.
        ///
        /// Nothing in the engine had to learn this - a page is whatever
        /// <see cref="DocumentDimension"/> says and no axis is privileged - which is
        /// exactly why it was worth a test: a capability nobody has exercised is a
        /// claim, not a feature, and the two look identical until someone tries.
        ///
        /// What it pins down is that the width really is used as width all the way
        /// through: the sheet, the flow that wraps inside it, the MediaBox a reader
        /// obeys, and the SVG a browser draws.
        /// </summary>
        [Fact]
        public void Test51_Landscape()
        {
            var testName = "Test 51: Landscape";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            const double Wide = 297, Tall = 210;

            var report = new Report
            {
                Title = "Landscape",
                Language = "en-GB",
                Dimension = new DocumentDimension { Width = Wide, Height = Tall },
                GeneratedAt = new DateTimeOffset(2025, 8, 1, 9, 0, 0, TimeSpan.Zero),
                DefaultFont = new TextStyle { Family = DmsFont },
                Body = new Body { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new EdgeSize { Left = 10, Top = 10, Right = 10, Bottom = 10 };

            // Enough prose to wrap many times and run past one sheet, so the width is
            // load-bearing rather than merely declared.
            for (int i = 1; i <= 24; i++)
                report.Body!.Add(new TextItem
                {
                    Text = $"Paragraph {i}. " + string.Join(" ", Enumerable.Repeat(
                        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do "
                        + "eiusmod tempor incididunt ut labore et dolore magna aliqua.", 6)),
                    Font = new TextStyle { Size = 1, HorizontalAlignment = HorizontalAlignment.Justify },
                    Style = { Margin = { Top = 2 } }
                });

            LayoutResult layout = report.Resolve(new FontTextMeasurer(library));

            AssertEqual(testName, "the sheet is wider than it is tall", true,
                layout.Pages[0].Frame.Width > layout.Pages[0].Frame.Height, 0);
            AssertEqual(testName, "and it is the size that was asked for", Wide,
                layout.Pages[0].Frame.Width, 0.001);
            AssertEqual(testName, "it took more than one sheet", true, layout.PageCount > 1,
                layout.PageCount);

            // The width is used, not merely declared: text reaches out past where a
            // portrait sheet would have ended, which is the whole difference.
            double rightmost = layout.Pages[0].Items
                .Where(i => i.Source is TextItem)
                .Max(i => i.Frame.Right);
            Console.WriteLine($"    text reaches {rightmost:F1} mm on a {Wide:F0} mm sheet");
            AssertEqual(testName, "text uses the extra width", true, rightmost > Tall, rightmost);
            AssertEqual(testName, "and stays inside the margin", true, rightmost <= Wide - 10 + 0.01, rightmost);

            // What a reader obeys. A MediaBox that disagrees with the layout prints the
            // page cropped, and nothing in the tree would say so.
            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Compress = false };
            ReportRenderer.Render(report, pdf);
            byte[] bytes = pdf.ToArray();
            string raw = System.Text.Encoding.Latin1.GetString(bytes);

            var boxes = System.Text.RegularExpressions.Regex
                .Matches(raw, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]")
                .Select(m => (W: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                              H: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            AssertEqual(testName, "every page declares a box", layout.PageCount, boxes.Count, 0);
            AssertEqual(testName, "the box is landscape too", true, boxes.All(b => b.W > b.H), 0);
            AssertEqual(testName, "297 mm in points", 841.89, boxes[0].W, 0.01);
            AssertEqual(testName, "210 mm in points", 595.28, boxes[0].H, 0.01);

            // And what a browser draws, from the same layout.
            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(report, svg);
            AssertEqual(testName, "the SVG is landscape as well", true,
                svg.Pages[0].Contains($"width=\"{Wide:0}mm\"") &&
                svg.Pages[0].Contains($"height=\"{Tall:0}mm\""), 0);

            string dir = OutputDirectory("landscape");
            svg.SaveTo(dir, "landscape");
            WriteSamplePdf(Path.Combine(dir, "landscape.pdf"), bytes);
            Console.WriteLine($"  ▭ {Path.Combine(dir, "landscape.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// One file, three page setups: A4 portrait, A4 landscape, A3 landscape.
        ///
        /// A report holds a list of sections and each carries its own
        /// <see cref="DocumentDimension"/>, which is how PDF has always worked - the size
        /// is <c>/MediaBox</c> on the page object, not a property of the file. What this
        /// pins down is that the size reaches every layer without being flattened
        /// somewhere along the way, which is precisely what used to happen: the loop
        /// writing /MediaBox ran after every page was drawn and read two fields holding
        /// whatever the last page happened to be.
        ///
        /// And the rule that makes it tractable: nothing flows from one document into the
        /// next. Content that does not fit adds pages to its own section.
        /// </summary>
        /// <summary>
        /// What is written about a position on a page is measured against THAT page.
        ///
        /// Section 16 records this fault for <c>/MediaBox</c>: the size was "filled from
        /// <c>_pageWidth</c>/<c>_pageHeight</c>, two fields holding what the LAST page
        /// had left. Invisible while a report has one page size, wrong the moment it has
        /// two." It was fixed for the page box and the same two fields went on being read
        /// for widget rectangles and bookmark destinations, three lines away from the
        /// per-page list that fixed it.
        ///
        /// A misplaced annotation is valid PDF, so veraPDF passes it; <c>/P</c>,
        /// <c>/Annots</c>, <c>/Fields</c> and <c>/AP</c> all resolve, so every
        /// reachability assertion stayed green. Only the arithmetic was wrong, and only
        /// when sections differ in height - which is why a suite of single-section
        /// samples never saw it.
        /// </summary>
        [Fact]
        public void Test79_APositionIsMeasuredAgainstItsOwnPage()
        {
            const string testName = "Test 79";
            Console.WriteLine("Test 79: a widget and a bookmark are measured against their own page");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // Two sections, the second much shorter, so a height taken from the end of
            // the file is unmistakably not the height of the first page.
            const double tall = 297, shortSheet = 148;

            var report = new Report { Title = "Two sizes", Language = "en-GB" };

            Section first = report.Section;
            first.Dimension = new() { Width = 210, Height = tall };
            first.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };

            var firstBody = new Body { Layout = LayoutMode.Stack };
            var heading = new TextItem { Text = "Where to sign", Role = PdfRole.Heading1,
                                         Font = new() { Size = 4 }, Bookmark = "Where to sign" };
            firstBody.Add(heading);

            var field = new FieldItem
            {
                Name = "customer.sign", Kind = FieldKind.Signature,
                Signing = Signing.Digital, Tooltip = "Sign here"
            };
            field.Style.Location.HeightValue = 18;
            firstBody.Add(field);
            first.Add(firstBody);

            Section second = report.AddSection();
            second.Dimension = new() { Width = 210, Height = shortSheet };
            second.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };
            var secondBody = new Body { Layout = LayoutMode.Stack };
            secondBody.Add(new TextItem { Text = "A shorter sheet", Font = new() { Size = 3 } });
            second.Add(secondBody);

            report.Reattach();

            // Where the engine says the field is, before anything is written down.
            LayoutResult layout = report.Resolve(new FontTextMeasurer(library));
            PlacedItem placed = layout.Pages[0].Items.First(p => p.Source == field);
            Console.WriteLine($"    pose de {placed.Frame.Top:0.00} a {placed.Frame.Bottom:0.00} mm "
                              + $"sur une page de {tall:0} mm");

            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            ReportRenderer.Render(report, pdf);
            string written = System.Text.Encoding.Latin1.GetString(pdf.ToArray());

            const double toPoints = 72.0 / 25.4;

            var rect = System.Text.RegularExpressions.Regex.Match(written,
                @"/Rect \[([-0-9.]+) ([-0-9.]+) ([-0-9.]+) ([-0-9.]+)\]");
            AssertEqual(testName, "a widget was written at all", true, rect.Success, 0);

            double bottom = double.Parse(rect.Groups[2].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            double wanted = (tall - placed.Frame.Bottom) * toPoints;
            double ifLast = (shortSheet - placed.Frame.Bottom) * toPoints;

            Console.WriteLine($"    /Rect bas {bottom:0.00} pt   sa page {wanted:0.00}   "
                              + $"la derniere {ifLast:0.00}");

            AssertEqual(testName, "the widget is flipped against the page it is on",
                wanted, bottom, 0.01);

            // Said separately, because the two numbers must not be allowed to coincide:
            // if the sample ever loses its second, shorter section this assertion is what
            // notices, rather than the one above passing for want of a difference.
            AssertEqual(testName, "and the two heights really do differ here",
                true, Math.Abs(wanted - ifLast) > 100, 0);

            // The same correction, the same file, three hundred lines apart.
            var dest = System.Text.RegularExpressions.Regex.Match(written,
                @"/Dest \[\d+ 0 R /XYZ null ([-0-9.]+) null\]");
            AssertEqual(testName, "a bookmark was written at all", true, dest.Success, 0);

            PlacedItem head = layout.Pages[0].Items.First(p => p.Source == heading);
            double wantedTop = (tall - head.Frame.Top) * toPoints;
            double destTop = double.Parse(dest.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);

            Console.WriteLine($"    /Dest {destTop:0.00} pt   sa page {wantedTop:0.00}   "
                              + $"la derniere {(shortSheet - head.Frame.Top) * toPoints:0.00}");

            AssertEqual(testName, "and so is the bookmark it points at",
                wantedTop, destTop, 0.01);

            Console.WriteLine();
        }

        [Fact]
        public void Test52_Sections()
        {
            var testName = "Test 52: Sections";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            var report = new Report
            {
                Title = "Assembled",
                Language = "en-GB",
                GeneratedAt = new DateTimeOffset(2025, 8, 1, 9, 0, 0, TimeSpan.Zero),
                DefaultFont = new TextStyle { Family = DmsFont }
            };

            // Three sizes, in order. The first is the one Report() already made.
            (string Name, double W, double H, int Paragraphs, int? Limit)[] wanted =
            {
                ("A4 portrait",   210, 297, 14, null),
                ("A4 landscape",  297, 210,  6, null),
                ("A3 landscape",  420, 297, 26, 1),      // deliberately more than it allows
            };

            for (int s = 0; s < wanted.Length; s++)
            {
                Section section = s == 0 ? report.Section : report.AddSection();
                section.Dimension = new DocumentDimension { Width = wanted[s].W, Height = wanted[s].H };
                section.PageLimit = wanted[s].Limit;
                section.Style.Padding = new EdgeSize { Left = 10, Top = 10, Right = 10, Bottom = 10 };

                var footer = new Footer { Layout = LayoutMode.Stack };
                footer.Add(new TextItem
                {
                    // Both countings on one line, which is the whole point of having two.
                    Text = wanted[s].Name + " — page {SectionPage} of {SectionPages}"
                         + " (sheet {Page} of {Pages})",
                    Font = new TextStyle { Size = 0.8 }
                });
                section.Add(footer);

                var body = new Body { Layout = LayoutMode.Stack };
                for (int i = 1; i <= wanted[s].Paragraphs; i++)
                    body.Add(new TextItem
                    {
                        Text = $"{wanted[s].Name}, paragraph {i}. " + string.Join(" ", Enumerable.Repeat(
                            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod "
                            + "tempor incididunt ut labore et dolore magna aliqua.", 8)),
                        Font = new TextStyle { Size = 1, HorizontalAlignment = HorizontalAlignment.Justify },
                        Style = { Margin = { Top = 2 } }
                    });
                // Each section says which one it is. The whole subject is three page
                // setups in one file, and a reader with one page on screen cannot see the
                // other two to compare it against.
                Demonstrates(body,
                    $"Section {s + 1} of {wanted.Length} - {wanted[s].Name}",
                    $"Set to {wanted[s].W:0} x {wanted[s].H:0} mm, and the section after it is "
                    + "not. Look for the sheet changing size and orientation at the boundary "
                    + "rather than the whole file taking one size, for the footer counting "
                    + "twice - page n of this section, sheet n of the file - and for nothing "
                    + "flowing across the boundary: what does not fit adds pages to its own "
                    + "section, which is what makes a table changing width mid-continuation "
                    + "inexpressible rather than merely discouraged."
                    + (wanted[s].Limit is int limit
                        ? $" This one also declares a limit of {limit} page(s) and needs more. "
                          + "The layout reports that and refuses nothing - composing and "
                          + "drawing are two calls, so the caller looks and then decides."
                        : ""));

                section.Add(body);
            }

            AssertEqual(testName, "three sections", 3, report.Sections.Count, 0);
            AssertEqual(testName, "and Report.Section still means the first", true,
                ReferenceEquals(report.Section, report.Sections[0]), 0);

            // --- through JSON, because that is the path a template takes ------------
            Report reloaded = Report.FromJson(report.ToJson());
            AssertEqual(testName, "the list survives a round trip", 3, reloaded.Sections.Count, 0);
            AssertEqual(testName, "and so does each page size", true,
                reloaded.Sections.Select(d => (d.Dimension.Width, d.Dimension.Height))
                        .SequenceEqual(wanted.Select(w => (w.W, w.H))), 0);
            AssertEqual(testName, "and the page limit", 1, reloaded.Sections[2].PageLimit ?? -1, 0);

            LayoutResult layout = reloaded.Resolve(new FontTextMeasurer(library));

            AssertEqual(testName, "one section laid out per section", 3, layout.Sections.Count, 0);
            foreach (LaidOutSection section in layout.Sections)
                Console.WriteLine($"    section: {section.PageCount} page(s) from index {section.FirstPage}"
                                + $" at {section.Section.Dimension.Width:0} x {section.Section.Dimension.Height:0}");

            // Every page is the size of the section it belongs to - not of the last one.
            foreach (LaidOutSection section in layout.Sections)
                for (int p = section.FirstPage; p < section.FirstPage + section.PageCount; p++)
                    AssertEqual(testName, $"page {p + 1} is its own section's size", true,
                        Math.Abs(layout.Pages[p].Frame.Width - section.Section.Dimension.Width) < 0.001
                        && Math.Abs(layout.Pages[p].Frame.Height - section.Section.Dimension.Height) < 0.001, 0);

            var widths = layout.Sections.Select(s => s.Section.Dimension.Width).ToList();
            AssertEqual(testName, "the three sizes really differ", 3, widths.Distinct().Count(), 0);

            // The first section runs to more than one page; nothing of it appears in the
            // second, which is the rule that makes the whole thing tractable.
            AssertEqual(testName, "the first section took several pages", true,
                layout.Sections[0].PageCount > 1, layout.Sections[0].PageCount);

            // --- the declared limit, reported and not enforced -----------------------
            AssertEqual(testName, "the layout reports what it found", 1, layout.Issues.Count, 0);
            AssertEqual(testName, "and it is the page limit that was exceeded", "fits.pageLimit",
                layout.Issues[0].Rule, 0);
            Console.WriteLine($"    issue: {layout.Issues[0].Where} — {layout.Issues[0].Message}");

            // Nothing was refused: the document is there, in full, for the caller to
            // decide about. That is the whole of the dry run.
            AssertEqual(testName, "and nothing was refused", true,
                layout.Sections[2].PageCount >= 1, 0);

            // --- what the tokens say -------------------------------------------------
            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Compress = false };
            ReportRenderer.Render(reloaded, layout, pdf);
            byte[] bytes = pdf.ToArray();

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(reloaded, svg);
            string all = string.Join("\n", svg.Pages);

            // The second section's first page is sheet 1 of its own section and a later
            // sheet of the file - the two numbers differ, which is what proves they are
            // two numbers.
            int secondStart = layout.Sections[1].FirstPage;
            AssertEqual(testName, "a section counts from its own first page", true,
                svg.Pages[secondStart].Contains("page 1 of "), 0);
            AssertEqual(testName, "while the file keeps counting", true,
                svg.Pages[secondStart].Contains($"sheet {secondStart + 1} of {layout.PageCount}"), 0);
            AssertEqual(testName, "no token was left behind", false, all.Contains("{Section"), 0);

            // --- and what a reader obeys ---------------------------------------------
            string raw = System.Text.Encoding.Latin1.GetString(bytes);
            var boxes = System.Text.RegularExpressions.Regex
                .Matches(raw, @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]")
                .Select(m => Math.Round(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            AssertEqual(testName, "a box per page", layout.PageCount, boxes.Count, 0);
            Console.WriteLine($"    MediaBox widths: {string.Join(", ", boxes.Distinct())}");
            AssertEqual(testName, "and three different ones among them", 3, boxes.Distinct().Count(), 0);

            string dir = OutputDirectory("sections");
            svg.SaveTo(dir, "sections");
            WriteSamplePdf(Path.Combine(dir, "sections.pdf"), bytes);
            Console.WriteLine($"  ⛶ {Path.Combine(dir, "sections.pdf")}");
            Console.WriteLine();
        }
    }
}
