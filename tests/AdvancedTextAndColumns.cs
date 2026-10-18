namespace test
{
    /// <summary>
    /// A paragraph laid out: the mixed-style runs of an <see cref="Frisket.AdvancedTextItem"/>
    /// sharing one baseline, justification stretching the same lines by the same millimetres
    /// in both outputs, and a stacked container divided into columns that fill in order and
    /// then balance.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test50_AdvancedText()
        {
            var testName = "Test 50: One paragraph, several styles";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            AdvancedTextItem Paragraph(params TextRun[] runs)
            {
                var item = new AdvancedTextItem();
                item.Runs.AddRange(runs);
                return item;
            }

            Report OnePage(AdvancedTextItem item)
            {
                var r = new Report { Dimension = new() { Width = 210, Height = 297 },
                                     Body = new() { Layout = LayoutMode.Stack } };
                r.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };
                r.Body!.Add(item);
                r.Reattach();
                return r;
            }

            // --- round trip: runs and their overrides survive the file --------------
            var original = Paragraph(
                new TextRun { Text = "Plain, " },
                new TextRun { Text = "bold", Font = new TextStyle { Bold = true } },
                new TextRun { Text = " and " },
                new TextRun { Text = "coloured", Font = new TextStyle { Color = new Color(192, 57, 43) } });

            Report reloaded = Report.FromJson(OnePage(original).ToJson());
            var reloadedItem = (AdvancedTextItem)reloaded.Body!.Childs[0];

            AssertEqual(testName, "every run survives", 4, reloadedItem.Runs.Count, 0);
            AssertEqual(testName, "a run's own override survives", true,
                reloadedItem.Runs[1].Font?.Bold == true, 0);
            AssertEqual(testName, "a run with no override stays null, not a copy of the default",
                true, reloadedItem.Runs[0].Font == null, 0);

            // --- one short line, several styles ---------------------------------------
            var oneLine = Paragraph(
                new TextRun { Text = "The " },
                new TextRun { Text = "total", Font = new TextStyle { Bold = true } },
                new TextRun { Text = " is " },
                new TextRun { Text = "cancelled", Font = new TextStyle { Strikethrough = true, Color = new Color(192, 57, 43) } },
                new TextRun { Text = " and final." });

            Report oneLineReport = OnePage(oneLine);
            BaseItem[] saidOnStyles = Demonstrates(oneLineReport.Body!,
                "One paragraph, several styles",
                "Bold, struck through and coloured inside a single sentence. Look for every "
                + "piece sitting on one baseline rather than each being centred in its own "
                + "height, for the pieces touching - the next starting exactly where the last "
                + "one's measured width put it, not a millimetre short or proud - and for the "
                + "line through crossing only the words that asked for it.");

            var svg1 = new SvgBackend(new FontTextMeasurer(library));
            LayoutResult layout1 = ReportRenderer.Render(oneLineReport, svg1);

            // Everything except what the caption above is made of. Counting all the text
            // on the page would count the heading and the caption too, and five would
            // stop meaning "one piece per differently-styled run".
            var pieces = layout1.Pages[0].Items
                .Where(i => i.Source is TextItem && !saidOnStyles.Contains(i.Source))
                .OrderBy(i => i.Frame.Left).ToList();
            AssertEqual(testName, "one piece per differently-styled run", 5, pieces.Count, 0);
            AssertEqual(testName, "the pieces read back in order",
                "The total is cancelled and final.",
                string.Join("", pieces.Select(p => p.Fragment)), 0);

            // Left to right with no gap: the next piece starts exactly where the last
            // one's measured width put it, not a millimetre short or a millimetre proud.
            for (int i = 1; i < pieces.Count; i++)
            {
                AssertEqual(testName, $"piece {i} touches piece {i - 1}",
                    pieces[i - 1].Frame.Right, pieces[i].Frame.Left, 0.01);
            }

            AssertEqual(testName, "the bold run is bold", true, pieces[1].Font?.Bold == true, 0);
            AssertEqual(testName, "and only the bold run", false, pieces[0].Font?.Bold == true, 0);
            AssertEqual(testName, "the struck-through run carries it", true,
                pieces[3].Font?.Strikethrough == true, 0);
            AssertEqual(testName, "and carries its own colour", true,
                pieces[3].Font?.Color is { R: 192, G: 57, B: 43 }, 0);

            string svgText1 = svg1.Pages[0];
            AssertEqual(testName, "strikethrough reaches the SVG", true, svgText1.Contains("line-through"), 0);

            var pdf1 = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            oneLineReport.GeneratedAt = SampleDate;
            ReportRenderer.Render(Report.FromJson(oneLineReport.ToJson()), pdf1);
            string pdf1Text = System.Text.Encoding.Latin1.GetString(pdf1.ToArray());
            AssertEqual(testName, "and the PDF is well-formed", true, pdf1Text.StartsWith("%PDF-1.7"), 0);

            // The glyphs are composite-font codes, not the literal letters, so the words
            // themselves cannot be found in the bytes - but five pieces means five show-
            // text operators, which a blank page (Add() having silently refused to attach
            // an item already parented elsewhere, say) would not have.
            var readablePdf1 = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b,
                                                         Language = "en-GB", Compress = false };
            ReportRenderer.Render(Report.FromJson(oneLineReport.ToJson()), readablePdf1);
            string readable1 = System.Text.Encoding.Latin1.GetString(readablePdf1.ToArray());
            int showTextOps = System.Text.RegularExpressions.Regex.Matches(readable1, @"\bTj\b").Count;
            Console.WriteLine($"    {showTextOps} Tj operators in the PDF");
            AssertEqual(testName, "and the PDF actually carries five pieces of text, not a blank page",
                true, showTextOps >= 5, 0);

            // --- baseline alignment across mixed sizes --------------------------------
            var mixedSize = Paragraph(
                new TextRun { Text = "Small " },
                new TextRun { Text = "BIG", Font = new TextStyle { Size = 3, SizeUnit = FontSizeUnit.Rem } },
                new TextRun { Text = " small" });

            var svg2 = new SvgBackend(new FontTextMeasurer(library));
            LayoutResult layout2 = ReportRenderer.Render(OnePage(mixedSize), svg2);
            var mixedPieces = layout2.Pages[0].Items.Where(i => i.Source is TextItem)
                .OrderBy(i => i.Frame.Left).ToList();

            double BaselineOf(PlacedItem p)
            {
                FontMetrics m = new FontTextMeasurer(library).GetMetrics(p.Font!);
                return p.Frame.Top + (m.LineHeight + m.CapHeight) / 2;
            }

            Console.WriteLine($"    lignes de base : {string.Join(" / ", mixedPieces.Select(p => BaselineOf(p).ToString("F3")))}");
            AssertEqual(testName, "three different sizes on one line", 3, mixedPieces.Count, 0);
            for (int i = 1; i < mixedPieces.Count; i++)
            {
                AssertEqual(testName, $"piece {i} shares the line's baseline",
                    BaselineOf(mixedPieces[0]), BaselineOf(mixedPieces[i]), 0.01);
            }
            AssertEqual(testName, "the big run is visibly taller than the small ones", true,
                mixedPieces[1].Frame.Height > mixedPieces[0].Frame.Height * 1.5, 0);

            // --- wrapping across lines, and pagination --------------------------------
            var longRun = Paragraph(new TextRun { Text = string.Join(" ", Enumerable.Repeat(
                "The supplier undertakes to carry out the work to the standards of the 'art", 150)) });

            Report longReport = OnePage(longRun);
            BaseItem[] saidOnWrap = Demonstrates(longReport.Body!,
                "A styled paragraph that wraps and paginates",
                "The same kind of paragraph, long enough to fill many lines and to run past "
                + "the foot of the page. Look for the breaks falling between words and never "
                + "inside one, for no line being sliced across the page break - a line is the "
                + "unit here, and one that does not fit moves whole - and for the text "
                + "resuming on the next page rather than starting again.");

            var svg3 = new SvgBackend(new FontTextMeasurer(library));
            LayoutResult layout3 = ReportRenderer.Render(longReport, svg3);

            var linesByTop = layout3.Pages.SelectMany(p => p.Items)
                .Where(i => i.Source is TextItem)
                .Select(i => Math.Round(i.Frame.Top, 1)).Distinct().ToList();
            Console.WriteLine($"    {linesByTop.Count} lignes, {layout3.PageCount} pages");
            AssertEqual(testName, "a long run wraps into several lines", true, linesByTop.Count > 1, 0);
            AssertEqual(testName, "and spills onto more than one page", true, layout3.PageCount > 1, 0);

            // A trailing space is trimmed at every line break on purpose - that is one
            // space fewer than the original had, once per wrap, not a dropped word - so
            // words are compared, not raw characters.
            string[] originalWords = string.Join(" ", Enumerable.Repeat(
                    "The supplier undertakes to carry out the work to the standards of the 'art", 150))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] rebuiltWords = layout3.Pages.SelectMany(p => p.Items)
                .Where(i => i.Source is TextItem && !saidOnWrap.Contains(i.Source))
                .SelectMany(i => (i.Fragment ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
            AssertEqual(testName, "every word survives the wrap, in order, none dropped or duplicated",
                string.Join("|", originalWords), string.Join("|", rebuiltWords), 0);

            // --- tokens resolve per run, like a plain text --------------------------
            var tokened = Paragraph(
                new TextRun { Text = "Client : " },
                new TextRun { Text = "{Client.Name}", Font = new TextStyle { Bold = true } });
            Report bound = ReportBinder.Bind(OnePage(tokened), """{ "Client": { "Name": "ACME" } }""");
            var boundItem = (AdvancedTextItem)bound.Body!.Childs[0];
            AssertEqual(testName, "the token in a styled run resolves", "ACME", boundItem.Runs[1].Text, 0);

            // --- the validator ---------------------------------------------------------
            var emptyIssues = TemplateValidator.Check(OnePage(Paragraph(new TextRun { Text = "" })),
                TemplateProfile.Sound);
            AssertEqual(testName, "a run with nothing in it is refused", true,
                emptyIssues.Any(i => i.Rule == "sound.emptyAdvancedText"), 0);

            // Justify used to be refused here, because the resolver placed each run's
            // own box and never touched the gaps between them. It stretches them now,
            // so the check is that it *works* rather than that it is reported - and
            // the way to know is that a stretched line ends exactly where a
            // right-aligned one does, since both reach the same margin.
            var justified = new AdvancedTextItem();
            justified.Font.HorizontalAlignment = HorizontalAlignment.Justify;
            justified.Runs.Add(new TextRun { Text = "The last line of a block is never stretched, " });
            justified.Runs.Add(new TextRun { Text = "because a closing line", Font = new TextStyle { Bold = true } });
            justified.Runs.Add(new TextRun { Text = " pulled out to the margin is what tells a reader it has ended." });

            var ragged = new AdvancedTextItem();
            ragged.Font.HorizontalAlignment = HorizontalAlignment.Right;
            foreach (TextRun run in justified.Runs)
                ragged.Runs.Add(new TextRun { Text = run.Text, Font = run.Font });

            LayoutResult stretched = OnePage(justified).Resolve(new FontTextMeasurer(library));
            LayoutResult flushed = OnePage(ragged).Resolve(new FontTextMeasurer(library));

            // The first line of each, and the piece furthest right on it.
            double EndOfFirstLine(LayoutResult result)
            {
                double firstTop = result.Pages[0].Items
                    .Where(p => p.Fragment != null).Min(p => p.Frame.Top);
                return result.Pages[0].Items
                    .Where(p => p.Fragment != null && Math.Abs(p.Frame.Top - firstTop) < 0.01)
                    .Max(p => p.Frame.Right);
            }

            AssertEqual(testName, "a justified line reaches the same margin as a flushed one",
                EndOfFirstLine(flushed), EndOfFirstLine(stretched), 0.01);

            // And the closing line is left alone, which is what says the paragraph is
            // over. Its last piece stops well short of the margin.
            double lastTop = stretched.Pages[0].Items.Where(p => p.Fragment != null).Max(p => p.Frame.Top);
            double lastEnd = stretched.Pages[0].Items
                .Where(p => p.Fragment != null && Math.Abs(p.Frame.Top - lastTop) < 0.01)
                .Max(p => p.Frame.Right);
            AssertEqual(testName, "and the closing line is not stretched", true,
                lastEnd < EndOfFirstLine(stretched) - 1, lastEnd);

            // --- a no-break space holds its neighbours together ------------------------
            // The same rule as plain text (test 19), asked of the breaker runs go through.
            // The width is taken off the engine's own layout of the words, so the window is
            // right whatever the font measures: room for everything up to "12" and not for
            // the amount. The plain space must break inside the amount there first.
            double LaidWidth(string text)
            {
                LayoutResult laid = OnePage(Paragraph(new TextRun { Text = text })).Resolve(new FontTextMeasurer(library));
                var pieces = laid.Pages[0].Items.Where(p => p.Fragment != null).ToList();
                return pieces.Max(p => p.Frame.Right) - pieces.Min(p => p.Frame.Left);
            }

            const string amountLead = "Le total est de 12";
            double amountWindow = (LaidWidth(amountLead) + LaidWidth(amountLead + " 480,00")) / 2;

            List<string> LaidLines(char separator)
            {
                var item = Paragraph(new TextRun { Text = amountLead + separator + "480,00 EUR" });
                item.Style.Location.WidthValue = amountWindow;
                LayoutResult laid = OnePage(item).Resolve(new FontTextMeasurer(library));
                return laid.Pages[0].Items.Where(p => p.Fragment != null)
                    .GroupBy(p => Math.Round(p.Frame.Top, 2)).OrderBy(g => g.Key)
                    .Select(g => string.Concat(g.OrderBy(p => p.Frame.Left).Select(p => p.Fragment)).TrimEnd())
                    .ToList();
            }

            List<string> plainRun = LaidLines(' ');
            Console.WriteLine($"    plain space:  {string.Join(" | ", plainRun)}");
            AssertEqual(testName, "at that width a plain space breaks inside the amount", true,
                plainRun.Any(l => l.EndsWith("12")), 0);

            foreach (char glue in LineBreaks.Glue)
            {
                List<string> held = LaidLines(glue);
                Console.WriteLine($"    U+{(int)glue:X4}:       {string.Join(" | ", held).Replace(glue, '~')}");
                AssertEqual(testName, $"U+{(int)glue:X4} holds the amount together in a run", true,
                    !held.Any(l => l.EndsWith("12")) && held.Any(l => l.Contains("12" + glue + "480,00")), 0);
            }

            var pageCounted = Paragraph(new TextRun { Text = "Page {Page} sur {Pages}" });
            var pageCountIssues = TemplateValidator.Check(OnePage(pageCounted), TemplateProfile.Sound);
            AssertEqual(testName, "{Pages} in a run is flagged, not silently wrong", true,
                pageCountIssues.Any(i => i.Rule == "sound.advancedTextPageCount"), 0);

            // A picture beside a paragraph: an absolutely laid out container, the
            // picture at a coordinate with a width and the paragraph starting after it.
            // This is the arrangement the flow editor's dock produces, and both of the
            // faults it found were in the rare corner CLAUDE.md §7 is about.
            var pair = new ContainerItem { Layout = LayoutMode.Absolute };

            var picture = new ImageItem { NaturalWidth = 60, NaturalHeight = 45 };
            picture.Style.Location.LeftValue = 0;
            picture.Style.Location.WidthValue = 60;

            var beside = new AdvancedTextItem();
            beside.Runs.Add(new TextRun { Text = "A paragraph set beside a picture, long enough to take more than one line." });
            beside.Style.Location.LeftValue = 64;
            beside.Style.Location.Dock = Dock.Right;

            pair.Add(picture);
            pair.Add(beside);

            var withPair = new Report { Dimension = new() { Width = 210, Height = 297 } };
            withPair.Body = new Body { Layout = LayoutMode.Stack };
            withPair.Body.Add(pair);
            withPair.Reattach();

            LayoutResult paired = withPair.Resolve(new FontTextMeasurer(library));
            var marks = paired.Pages[0].Items.Where(p => p.Fragment != null).ToList();

            // It drew nothing at all before: an advanced text is drawn through the
            // synthetic texts PlaceAdvancedLines makes, and Arrange never called it, so
            // the item was placed as an empty rectangle and silently produced no marks.
            AssertEqual(testName, "an advanced text placed absolutely is drawn", true,
                marks.Count > 0, marks.Count);

            PlacedItem placedPicture = paired.Pages[0].Items.First(p => p.Source == picture);
            AssertEqual(testName, "and the picture keeps the width it asked for",
                60, placedPicture.Frame.Width, 0.01);

            AssertEqual(testName, "the paragraph starts past the picture", true,
                marks.All(m => m.Frame.Left >= placedPicture.Frame.Right - 0.01),
                marks.Min(m => m.Frame.Left));

            // The group was twice as tall as anything in it: a child with a width of its
            // own was measured as if it spanned the container, so a 60mm picture came
            // back at the height of a 140mm one.
            PlacedItem placedPair = paired.Pages[0].Items.First(p => p.Source == pair);
            AssertEqual(testName, "and the group is as tall as its tallest child, not wider",
                45, placedPair.Frame.Height, 1.0);

            // Where the short half sits down the tall one. Three renders of one document,
            // because the only way to know an alignment works is that changing it moves
            // something: all three were identical before, and the property had been
            // legal and inert since it was written.
            double TopOfText(ItemAlign how)
            {
                var group = new ContainerItem { Layout = LayoutMode.Absolute };

                var shot = new ImageItem { NaturalWidth = 60, NaturalHeight = 45 };
                shot.Style.Location.LeftValue = 0;
                shot.Style.Location.WidthValue = 60;

                var note = new AdvancedTextItem();
                note.Runs.Add(new TextRun { Text = "Short." });
                note.Style.Location.LeftValue = 64;
                note.Style.Location.Dock = Dock.Right;
                note.Style.AlignVertical = how;

                group.Add(shot);
                group.Add(note);

                var one = new Report { Dimension = new() { Width = 210, Height = 297 } };
                one.Body = new Body { Layout = LayoutMode.Stack };
                one.Body.Add(group);
                one.Reattach();

                LayoutResult laid = one.Resolve(new FontTextMeasurer(library));
                return laid.Pages[0].Items.Where(p => p.Fragment != null).Min(p => p.Frame.Top);
            }

            double atTop = TopOfText(ItemAlign.Start);
            double atMiddle = TopOfText(ItemAlign.Center);
            double atBottom = TopOfText(ItemAlign.End);

            Console.WriteLine($"    top {atTop:0.0}  middle {atMiddle:0.0}  bottom {atBottom:0.0}");

            AssertEqual(testName, "centring a short half moves it down", true,
                atMiddle > atTop + 5, 0);
            AssertEqual(testName, "and sending it to the bottom moves it further", true,
                atBottom > atMiddle + 5, 0);

            // It could only ever have worked once the item stopped filling: an alignment
            // is a statement that the item does not span the space, and stretching first
            // left nothing to move.
            AssertEqual(testName, "an aligned half no longer fills the group", true,
                atBottom < 77, 0);

            var soundOne = Paragraph(new TextRun { Text = "texte normal" });
            var soundIssues = TemplateValidator.Check(OnePage(soundOne), TemplateProfile.Sound);
            AssertEqual(testName, "a well-formed paragraph raises nothing", 0, soundIssues.Count, 0);

            string dir = OutputDirectory("advanced-text");
            svg1.SaveTo(dir, "styles");
            svg3.SaveTo(dir, "wrap");
            WriteSamplePdf(Path.Combine(dir, "styles.pdf"), pdf1.ToArray());
            Console.WriteLine($"  ▤ {Path.Combine(dir, "styles.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// The line before a hard break really is stretched, which is what makes
        /// <c>sound.justifiedBreak</c> a rule worth having rather than one that accuses a
        /// template that is right.
        ///
        /// Section 8f is why this exists: a rule that no longer describes the engine
        /// teaches people to ignore the panel, and the only way to know it still
        /// describes it is to measure what the engine does. The rule had no assertion of
        /// any kind until 25 August 2026 - it lived in both validators, in both
        /// languages, and nothing anywhere built the shape it fires on.
        /// </summary>
        [Fact]
        public void Test74_AHardBreakIsStretchedLikeAWrappedLine()
        {
            const string testName = "Test 74";
            Console.WriteLine("Test 74: a hard break in justified text is stretched like a wrapped line");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            // The word-spacing the SVG backend writes on a stretched line (section 10),
            // which is the engine saying out loud which lines it stretched. Read off the
            // drawing rather than computed: a computation here would be a second
            // implementation of the very thing under test.
            static List<double> Drawn(string page) =>
                System.Text.RegularExpressions.Regex.Matches(page, "<text[^>]*>")
                    .Select(tag => System.Text.RegularExpressions.Regex.Match(tag.Value, @"word-spacing=""(-?[0-9.]+)"""))
                    .Select(m => m.Success
                        ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                        : 0)
                    .ToList();

            // One short line, then a break, then enough to wrap. The short line is the
            // one the rule is about: broken by hand it is the middle of a paragraph as
            // far as the engine can tell, so it is stretched to the margin; put in a
            // text of its own - which is exactly what the message recommends - it is a
            // last line and is left alone.
            const string opening = "Short first line.";
            const string rest = "The supplier undertakes to deliver every part listed above "
                              + "and to invoice them monthly without further notice.";

            List<double> Spacings(HorizontalAlignment how, params string[] texts)
            {
                var report = new Report
                {
                    Title = "Justified", Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };

                foreach (string text in texts)
                {
                    var item = new TextItem { Text = text, Font = new() { Size = 3 } };
                    item.Font.HorizontalAlignment = how;
                    report.Body!.Add(item);
                }
                report.Reattach();

                var svg = new SvgBackend(measurer) { EmbeddedFonts = library };
                ReportRenderer.Render(report, svg);

                return Drawn(svg.Pages[0]);
            }

            var broken = Spacings(HorizontalAlignment.Justify, opening + "\n" + rest);
            var apart = Spacings(HorizontalAlignment.Justify, opening, rest);
            var plain = Spacings(HorizontalAlignment.Left, opening + "\n" + rest);

            Console.WriteLine($"    coupe a la main {string.Join(", ", broken.Select(v => $"{v:0.00}"))}");
            Console.WriteLine($"    deux textes     {string.Join(", ", apart.Select(v => $"{v:0.00}"))}");
            Console.WriteLine($"    aligne a gauche {string.Join(", ", plain.Select(v => $"{v:0.00}"))}");

            // The control first, and it is not a formality: without it every assertion
            // below could hold because nothing is stretched anywhere, and the check
            // would be about an absence.
            AssertEqual(testName, "left-aligned, nothing is stretched at all",
                true, plain.Count > 1 && plain.All(v => v == 0), 0);

            // The fault, and it is not subtle: seventeen characters pulled across a
            // hundred and seventy millimetres.
            AssertEqual(testName, "the line before a hand-typed break is stretched",
                true, broken[0] > 10, 0);

            // And the remedy the message names really is one: the same words in a text
            // of their own are a last line, which is never stretched.
            AssertEqual(testName, "and in a text of its own it is left alone",
                0.0, apart[0], 0.0001);

            // The rest of the document is not disturbed by either arrangement - so the
            // difference above is about that one line and not about a re-flow.
            AssertEqual(testName, "with the same number of lines drawn either way",
                apart.Count, broken.Count, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test46_Justified()
        {
            var testName = "Test 46: Justified text, and the two backends agreeing on it";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            const double Width = 120;

            Report Paragraph(HorizontalAlignment how)
            {
                var report = new Report
                {
                    Title = "Justification",
                    Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 70, Bottom = 20 };

                report.Body!.Add(new TextItem
                {
                    Text = string.Join(" ", Enumerable.Repeat(
                        "the supplier carries out the work to the standards of the trade", 6)) + ".",
                    Font = new()
                    {
                        Size = 10,
                        SizeUnit = FontSizeUnit.Point,
                        LineSpacing = 1.4,
                        HorizontalAlignment = how
                    }
                });

                report.Reattach();
                return report;
            }

            // Left first, to know what the lines measure when nothing stretches them.
            var plain = new SvgBackend(measurer);
            ReportRenderer.Render(Report.FromJson(Paragraph(HorizontalAlignment.Left).ToJson()), plain);

            var svg = new SvgBackend(measurer);
            ReportRenderer.Render(Report.FromJson(Paragraph(HorizontalAlignment.Justify).ToJson()), svg);

            var spacings = System.Text.RegularExpressions.Regex
                .Matches(svg.Pages[0], @"word-spacing=""([\d.]+)""")
                .Select(m => double.Parse(m.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            int lines = System.Text.RegularExpressions.Regex.Matches(plain.Pages[0], "<text ").Count;
            Console.WriteLine($"    {lines} lines, {spacings.Count} stretched: "
                              + string.Join(", ", spacings.Select(v => v.ToString("F3"))));

            AssertEqual(testName, "left alignment stretches nothing", 0,
                System.Text.RegularExpressions.Regex.Matches(plain.Pages[0], "word-spacing").Count, 0);

            // Every line but the last. A closing line pulled out to the margin is what
            // tells a reader the paragraph has ended.
            AssertEqual(testName, "every line but the last is stretched", lines - 1, spacings.Count, 0);
            AssertEqual(testName, "and every stretch is a real one", true, spacings.All(v => v > 0), 0);

            // The point of justifying: the line reaches the far margin.
            var text = System.Text.RegularExpressions.Regex
                .Matches(svg.Pages[0], @"<text [^>]*word-spacing=""([\d.]+)""[^>]*>([^<]*)</text>");

            var style = new TextStyle
            {
                Family = DmsFont, Size = 10, SizeUnit = FontSizeUnit.Point
            };

            foreach (System.Text.RegularExpressions.Match m in text)
            {
                double padding = double.Parse(m.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                string line = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value);
                int spaces = line.Count(c => c == ' ');

                double natural = measurer.Measure(line, style, double.PositiveInfinity).Width;
                AssertEqual(testName, "the stretched line reaches the margin",
                    Width, natural + padding * spaces, 0.05);
            }

            // --- and the other backend says the same -------------------------
            // Each file is consistent with itself whatever it does; only comparing
            // them catches one stretching and the other not, or by a different amount.
            var pdf = new PdfBackend(library) { Language = "en-GB", Compress = false };
            ReportRenderer.Render(Report.FromJson(Paragraph(HorizontalAlignment.Justify).ToJson()), pdf);
            string stream = System.Text.Encoding.Latin1.GetString(pdf.ToArray());

            var adjustments = System.Text.RegularExpressions.Regex
                .Matches(stream, @"\]\s*TJ")
                .Count;

            AssertEqual(testName, "the PDF stretches the same lines", spacings.Count, adjustments, 0);

            // A TJ number is thousandths of the text-space unit, taken off the
            // position - so widening a gap is a negative one, and the millimetres it
            // stands for must match what the SVG asked the viewer for.
            var numbers = System.Text.RegularExpressions.Regex
                .Matches(stream, @">(-[\d.]+)<")
                .Select(m => double.Parse(m.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture))
                .Distinct().ToList();

            double em = 10 * TextStyle.PointsToMillimeters;
            var asMillimetres = numbers.Select(v => -v * em / 1000).OrderBy(v => v).ToList();
            var asked = spacings.Distinct().OrderBy(v => v).ToList();

            Console.WriteLine($"    SVG {string.Join(", ", asked.Select(v => v.ToString("F3")))} mm"
                              + $"  |  PDF {string.Join(", ", asMillimetres.Select(v => v.ToString("F3")))} mm");

            AssertEqual(testName, "the two backends stretch by the same amount",
                asked.Count, asMillimetres.Count, 0);

            for (int i = 0; i < asked.Count; i++)
                AssertEqual(testName, "to the same millimetre", asked[i], asMillimetres[i], 0.002);

            Console.WriteLine();
        }

        [Fact]
        public void Test45_Columns()
        {
            var testName = "Test 45: Text flowing into columns";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // Terms and conditions: the case columns exist for. Long, uniform, and far
            // too much for one page in one column.
            Report Terms(int columns, int articles)
            {
                var report = new Report
                {
                    Title = "Terms and conditions",
                    Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack, ColumnCount = columns, ColumnGap = 8 }
                };
                report.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };

                for (int i = 1; i <= articles; i++)
                {
                    var article = new TextItem
                    {
                        Text = $"Clause {i}. " + string.Join(" ", Enumerable.Repeat(
                            "The supplier undertakes to carry out the work to the standards of the 'art", 3)) + ".",
                        Font = new() { Size = 9, SizeUnit = FontSizeUnit.Point, LineSpacing = 1.4 }
                    };
                    article.Style.Margin = new() { Bottom = 2 };
                    report.Body!.Add(article);
                }

                // Said in the one-column build as well as the two, so the comparison
                // between them stays a comparison of the same text.
                //
                // Shorter than the other samples carry, because this one flows into the
                // columns with everything else: there is no full-width band above them to
                // write in, and a caption of the usual length took ten lines of the very
                // column it was asking the reader to look at.
                Demonstrates(report.Body!,
                    columns == 1 ? "One column" : columns + " columns",
                    "Look for the first being filled to the foot before the next begins, and "
                    + "for what neither takes going over the page. This saves no paper - the "
                    + "same words at the same size cover the same area - what it changes is "
                    + "the measure.");

                report.Reattach();
                return report;
            }

            // The template is round tripped on the way to every section, so the setting
            // has to survive that before anything else is worth measuring.
            Report reloaded = Report.FromJson(Terms(2, 40).ToJson());
            AssertEqual(testName, "the column count survives the file", 2, reloaded.Body!.ColumnCount, 0);
            AssertEqual(testName, "and so does the gap", 8.0, reloaded.Body!.ColumnGap, 0.001);

            var backend = new SvgBackend(new FontTextMeasurer(library));
            LayoutResult layout = ReportRenderer.Render(reloaded, backend);

            // Where the lines actually landed. Two columns means two bands of x, and
            // nothing in between: a column that merely narrows the text would give one.
            var lefts = layout.Pages[0].Items
                .Where(i => i.Source is TextItem)
                .Select(i => Math.Round(i.Frame.Left, 1))
                .Distinct().OrderBy(x => x).ToList();

            Console.WriteLine($"    page 1 : colonnes en x = {string.Join(", ", lefts)}");
            AssertEqual(testName, "two columns, and only two", 2, lefts.Count, 0);
            AssertEqual(testName, "the first at the left margin", 15.0, lefts[0], 0.01);

            // Left margin 15, page 210, so 180 of room; two columns and an 8 mm gap put
            // the second at 15 + (180 - 8) / 2 + 8.
            AssertEqual(testName, "the second past the first plus the gap", 109.0, lefts[1], 0.01);

            // Filled in order, not balanced: the first column is full before the second
            // begins. That is what a page of terms wants.
            double firstBottom = layout.Pages[0].Items
                .Where(i => i.Source is TextItem && Math.Abs(i.Frame.Left - lefts[0]) < 0.1)
                .Max(i => i.Frame.Bottom);

            Console.WriteLine($"    the first column runs down to {firstBottom:F1} mm");
            AssertEqual(testName, "the first column is filled to the foot", true, firstBottom > 270, 0);

            // What neither column could take goes to the next page, through the very
            // machinery that always carried it there.
            AssertEqual(testName, "and the rest goes over the page", true, layout.PageCount > 1, 0);

            // What columns are and are not for. The same text at the same size takes
            // the same area however it is divided, so this saves no paper: what it
            // changes is the measure. A line 180 mm long is tiring to read; the same
            // words in two columns of 86 mm are not, and more of them fit on the sheet
            // because each line is shorter than the space it was given.
            var single = new SvgBackend(new FontTextMeasurer(library));
            ReportRenderer.Render(Report.FromJson(Terms(1, 40).ToJson()), single);

            int OnPage(SvgBackend svg, int page)
                => System.Text.RegularExpressions.Regex.Matches(svg.Pages[page], "<text ").Count;

            Console.WriteLine($"    first page: {OnPage(single, 0)} lines in one column, "
                              + $"{OnPage(backend, 0)} sur deux");
            AssertEqual(testName, "two columns hold more on the first page", true,
                OnPage(backend, 0) > OnPage(single, 0), 0);

            // Every line is somewhere. Columns that quietly lost text would still look
            // entirely convincing.
            int Lines(SvgBackend svg) => svg.Pages.Sum(page =>
                System.Text.RegularExpressions.Regex.Matches(page, "<text ").Count);

            Console.WriteLine($"    {Lines(single)} lines in one column, {Lines(backend)} in two");
            AssertEqual(testName, "and nothing was dropped on the way", true,
                Lines(backend) >= Lines(single), 0);

            // --- balancing ---------------------------------------------------
            // Filling in order leaves the last page lopsided: the first column full,
            // the last nearly empty. On a load that fits, the columns should end
            // together instead.
            // Twenty, and the number is not free. Balancing only has anything to give on
            // a load that fits in less than two full columns: below the window there is
            // nothing to look at, and at 26 the columns are both full, balanced and loose
            // give the same 278/276, and a check comparing them would pass while proving
            // nothing at all.
            //
            // Found by sweeping rather than guessed, and swept twice: the first attempt
            // read the default for the unbalanced case, and BalanceColumns defaults to
            // true - so it measured balanced against balanced and every calibre looked
            // identical. Unbalanced, the window is 14 to 23; twenty sits in the middle of
            // it at 278 against 171.
            //
            // It used to be 25, which stopped working the day this document gained three
            // lines saying what it demonstrates. Three lines of caption are enough to
            // move a calibrated sample out of its window, which is the whole reason the
            // number is swept for and not remembered.
            Report Fitting(bool balance)
            {
                Report terms = Terms(2, 20);
                terms.Body!.BalanceColumns = balance;
                return Report.FromJson(terms.ToJson());
            }

            double[] Feet(Report report)
            {
                var svg = new SvgBackend(new FontTextMeasurer(library));
                LayoutResult result = ReportRenderer.Render(report, svg);

                return result.Pages[0].Items
                    .Where(i => i.Source is TextItem)
                    .GroupBy(i => Math.Round(i.Frame.Left, 1))
                    .OrderBy(g => g.Key)
                    .Select(g => g.Max(i => i.Frame.Bottom))
                    .ToArray();
            }

            double[] evened = Feet(Fitting(true));
            double[] lopsided = Feet(Fitting(false));

            Console.WriteLine($"    balanced: {string.Join(" / ", evened.Select(v => v.ToString("F0")))} mm"
                              + $"   sans : {string.Join(" / ", lopsided.Select(v => v.ToString("F0")))} mm");

            AssertEqual(testName, "balanced, both columns still exist", 2, evened.Length, 0);
            AssertEqual(testName, "and they end together", true,
                Math.Abs(evened[0] - evened[1]) < 6, 0);

            AssertEqual(testName, "unbalanced, the first runs to the foot", true,
                lopsided[0] > 270, 0);
            AssertEqual(testName, "and the second stops well short", true,
                lopsided[1] < lopsided[0] - 50, 0);

            // Balancing shortens the columns; it must not lose or move anything else.
            AssertEqual(testName, "balancing does not spill onto another page", true,
                evened[0] < lopsided[0], 0);

            // Written out, because two columns is a thing to look at rather than count.
            string dir = OutputDirectory("columns");
            backend.SaveTo(dir, "terms");

            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            reloaded.GeneratedAt = SampleDate;
            ReportRenderer.Render(reloaded, pdf);
            WriteSamplePdf(Path.Combine(dir, "terms.pdf"), pdf.ToArray());

            Console.WriteLine($"  ▤ {Path.Combine(dir, "terms.pdf")}");
            Console.WriteLine();
        }
    }
}
