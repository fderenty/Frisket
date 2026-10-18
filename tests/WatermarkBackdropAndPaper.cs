namespace test
{
    /// <summary>
    /// What is laid over and behind the page - a tiled or single watermark, a picture as
    /// printed stationery - confronted between the SVG and the PDF, and the proof paper
    /// colour that reaches neither.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>
        /// The furthest any point of the sheet sits from the nearest watermark, in
        /// millimetres. Each copy is treated as the segment its text runs along.
        /// </summary>
        static double WorstGap(string svg, double width, double height, double run)
        {
            var marks = System.Text.RegularExpressions.Regex
                .Matches(svg, @"translate\(([-\d.]+) ([-\d.]+)\) rotate\((-?[\d.]+)\)")
                .Select(m =>
                {
                    double x = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double y = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    double a = -double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)
                             * Math.PI / 180.0;
                    return (X: x, Y: y, EndX: x + run * Math.Cos(a), EndY: y - run * Math.Sin(a));
                }).ToList();

            if (marks.Count == 0)
                return double.PositiveInfinity;

            double worst = 0;
            for (double px = 0; px <= width; px += 5)
            {
                for (double py = 0; py <= height; py += 5)
                {
                    double nearest = double.MaxValue;
                    foreach (var m in marks)
                    {
                        double dx = m.EndX - m.X, dy = m.EndY - m.Y;
                        double len = dx * dx + dy * dy;
                        double t = len <= 0 ? 0 : Math.Clamp(((px - m.X) * dx + (py - m.Y) * dy) / len, 0, 1);
                        nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(px - (m.X + t * dx), 2)
                                                            + Math.Pow(py - (m.Y + t * dy), 2)));
                    }
                    worst = Math.Max(worst, nearest);
                }
            }

            return worst;
        }

        [Fact]
        public void Test35_Watermark()
        {
            var testName = "Test 35: Watermark";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // The word, with its trailing space, written once. Typed into each of the
            // six assertions below it would be six chances to lose a character nobody
            // can see - section 2b, which is about exactly this and which cost an
            // afternoon the last time: an invisible character is read back in the file,
            // never in the terminal.
            const string draft = "DRAFT ";
            AssertEqual(testName, "the mark ends in a space, which is what opens the grid",
                true, draft.EndsWith(" ") && draft.Trim() == "DRAFT", 0);

            Report report = BoundAcmeInvoice(12);
            // DRAFT, like the conformance document and like the single copy below. The
            // folder used to hold two samples of one feature saying two different words,
            // with the file names following the words rather than the modes - so nothing
            // in it told a reader that tiled and single are the thing being compared.
            //
            // The word is DRAFT on purpose and stays DRAFT: it is what the whole folder
            // says, and a sample that changed its word to suit an assertion would be
            // the fault this folder was mended for. It is also what Watermark.Text now
            // defaults to, so passing it to the constructor proves nothing about the
            // argument reaching the page - that is a separate assertion, further down,
            // on a word nothing hands out for free.
            report.Watermark = new Watermark(draft);

            BaseItem[] said = Demonstrates(report.Body!,
                "Watermark - tiled",
                "A translucent mark, turned and tiled across the whole sheet. Look for it "
                + "reaching every corner rather than clustering in the middle - a hundred "
                + "copies in one place leave the rest of the page clean to work on - for it "
                + "sitting in front of the table rather than behind, since a shaded header "
                + "painted over it would clear the mark exactly where someone retouching a "
                + "screenshot would start, and for the invoice underneath staying readable "
                + "through it.");

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            LayoutResult layout = ReportRenderer.Render(report, svg);

            string page = svg.Pages[0];
            int copies = CountOccurrences(page, $">{draft}</text>");

            Console.WriteLine($"    tuiles page 1 : {copies}");
            AssertEqual(testName, "the page is tiled with it", true, copies > 20, 0);
            AssertEqual(testName, "each copy is turned", true, page.Contains("rotate(-45)"), 0);

            // The sample's own word is the default, so the line above would pass on a
            // report that never read the constructor argument at all. A word nothing
            // hands out, rendered on its own, is what says the argument arrives.
            var named = BoundAcmeInvoice(2);
            named.Watermark = new Watermark("COMPTE RENDU");
            var namedSvg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(named, namedSvg);
            AssertEqual(testName, "and the word asked for is the word drawn", true,
                namedSvg.Pages[0].Contains(">COMPTE RENDU</text>")
                && new Watermark().Text != "COMPTE RENDU", 0);

            // Counting copies proves nothing about where they land: a hundred marks in one
            // corner leave the rest of the sheet clean to work on. What matters is that no
            // part of the page is far from one, so that is what is measured.
            double gap = WorstGap(page, 210, 297,
                                  new FontTextMeasurer(library)
                                      .Measure(draft, layout.WatermarkFont!, double.PositiveInfinity).Width);

            Console.WriteLine($"    ecart max : {gap:0.0} mm");
            AssertEqual(testName, "no corner of the sheet is left clear", true, gap < 20, 0);

            // Drawn, not a background: a browser drops background graphics when printing,
            // and a mark absent from the printout is missing from the copy that matters.
            AssertEqual(testName, "it is drawn as content", true, page.Contains($">{draft}</text>"), 0);

            string dir = OutputDirectory("watermark");
            svg.SaveTo(dir, "draft");

            var pdf = new PdfBackend(library) { Compress = false };
            ReportRenderer.Render(report, pdf);
            byte[] bytes = pdf.ToArray();
            WriteSamplePdf(Path.Combine(dir, "draft.pdf"), bytes);

            string raw = System.Text.Encoding.Latin1.GetString(bytes);
            AssertEqual(testName, "and the PDF carries it too", true, raw.Contains(" Tm"), 0);

            // In front, and translucent. Behind, a filled title bar or a shaded table
            // header paints over the mark and clears it exactly there - which is where
            // someone retouching a screenshot would start. Both need the mark to stay
            // readable, so it goes on top with an opacity rather than underneath opaque.
            AssertEqual(testName, "the mark sits on top by default", WatermarkLayer.InFront,
                new Watermark().Layer, 0);
            AssertEqual(testName, "the marks are drawn after the content", true,
                page.IndexOf($">{draft}</text>", StringComparison.Ordinal) > page.LastIndexOf("<rect", StringComparison.Ordinal), 0);
            AssertEqual(testName, "the SVG carries the opacity", true, page.Contains("fill-opacity"), 0);
            AssertEqual(testName, "and the PDF states one too", true,
                raw.Contains("/ExtGState") && raw.Contains("/ca "), 0);

            // The same section as an archivable file. Transparency is allowed from
            // PDF/A-2 on and forbidden in PDF/A-1, and this is the first thing here to
            // use any - so there has to be a conforming file that actually carries some,
            // or the claim is untested.
            var archived = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b };
            ReportRenderer.Render(report, archived);
            byte[] archivable = archived.ToArray();
            WriteSamplePdf(Path.Combine(dir, "draft.pdfa.pdf"), archivable);

            string conforming = System.Text.Encoding.Latin1.GetString(archivable);
            AssertEqual(testName, "an archivable file carries the mark", true,
                conforming.Contains("<pdfaid:part>3</pdfaid:part>"), 0);
            AssertEqual(testName, "and its transparency with it", true,
                conforming.Contains("/ExtGState") && conforming.Contains("/BM /Normal"), 0);

            // Every page, not just the first.
            for (int i = 0; i < layout.PageCount; i++)
            {
                AssertEqual(testName, $"page {i + 1} is marked", true,
                    svg.Pages[i].Contains($">{draft}</text>"), 0);
            }

            // One large copy instead, for a section where the mark is a courtesy.
            //
            // The second file says what the second file shows. It is the same report with
            // the mark swapped, so without this it would go out carrying the caption for
            // the tiled one - a document describing the demonstration next to it, which is
            // worse than a document describing nothing.
            ((TextItem)said[0]).Text = "Watermark - a single copy";
            ((TextItem)said[2]).Text =
                "The same page and the same mark, written once across the middle instead of "
                + "tiled - for a document where the mark is a courtesy rather than a defence. "
                + "Look for exactly one copy, turned the same way as the tiled ones, and for "
                + "it still being drawn as content rather than as a background: a browser "
                + "drops background graphics when printing, and a mark missing from the "
                + "printout is missing from the copy that matters.";

            // The same word as the tiled sheet, on purpose: the two files differ in the
            // mode and in nothing else, so anything else differing would read as part of
            // what is being shown. It used to say BROUILLON, in a file called draft.html,
            // beside a file called specimen.html saying SPECIMEN - three names for one
            // feature, none of them naming the thing that actually differs.
            report.Watermark = new Watermark(draft) { Mode = WatermarkMode.Single };
            var once = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(report, once);
            AssertEqual(testName, "single mode writes it once", 1,
                CountOccurrences(once.Pages[0], $">{draft}</text>"), 0);
            once.SaveTo(dir, "draft-single");

            Console.WriteLine($"  💧 {Path.Combine(dir, "draft.pdf")}");
            Console.WriteLine();
        }

        /// <summary>The JSON key section 1 says a non-default default must not lose.</summary>
        private const string OPACITY_KEY = "\"Opacity\"";

        [Fact]
        public void Test73_Backdrop()
        {
            const string testName = "Test 73";
            Console.WriteLine("Test 73: the picture behind the page, in both outputs and out of the tree");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            // Stationery that darkens down the sheet, so a page which lost it - or drew it
            // upside down - cannot be mistaken for a page that carried it properly.
            byte[] stock = PngEncoder.Rgb(64, 90, (x, y) => ((byte)(235 - y), (byte)(228 - y / 2), 205));
            ImageSource Paper() => new() { MediaType = "image/png", Data = stock };

            Report Sheet(Backdrop? paper, int paragraphs = 3, bool header = false)
            {
                var report = new Report
                {
                    Title = "Letterhead", Language = "en-GB",
                    GeneratedAt = new DateTimeOffset(2026, 3, 9, 14, 5, 9, TimeSpan.Zero),
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack },
                    Backdrop = paper
                };
                report.Section.Style.Padding = new() { Left = 20, Top = 25, Right = 15, Bottom = 30 };

                if (header)
                {
                    report.Header = new() { Layout = LayoutMode.Stack };
                    report.Header!.Add(new TextItem { Text = "Ashcombe and Rill", Font = new() { Size = 4 } });
                }

                for (int i = 0; i < paragraphs; i++)
                    report.Body!.Add(new TextItem
                    {
                        Text = string.Join(" ", Enumerable.Repeat("The supplier undertakes.", 40)),
                        Font = new() { Size = 2 }
                    });

                report.Reattach();
                return report;
            }

            string Svg(Report report)
            {
                var svg = new SvgBackend(measurer) { EmbeddedFonts = library };
                ReportRenderer.Render(report, svg);
                return string.Join("\n", svg.Pages);
            }

            // Uncompressed, because the marks and the matrix are read out of the page
            // stream. The shipped file compresses them; this is the same section.
            string Pdf(Report report)
            {
                var pdf = new PdfBackend(library)
                {
                    Conformance = PdfConformance.PdfA3b, Language = "en-GB",
                    Tagged = true, Compress = false
                };
                ReportRenderer.Render(report, pdf);
                return System.Text.Encoding.Latin1.GetString(pdf.ToArray());
            }

            static double Num(string text) =>
                double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

            static System.Text.RegularExpressions.Match Find(string haystack, string pattern) =>
                System.Text.RegularExpressions.Regex.Match(haystack, pattern);

            // Both outputs write millimetres: the SVG because its viewBox is the page in
            // millimetres, the PDF because one matrix at the top of every page converts the
            // whole stream. So the two sets of numbers are directly comparable, which is the
            // only reason this test is able to confront them at all.
            static (double X, double Y, double W, double H) SvgBox(string svg)
            {
                var m = Find(svg, @"<image x=""(-?[\d.]+)"" y=""(-?[\d.]+)"" width=""(-?[\d.]+)"" height=""(-?[\d.]+)""");
                return m.Success
                    ? (Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value))
                    : (double.NaN, double.NaN, double.NaN, double.NaN);
            }

            static (double X, double Y, double W, double H) PdfBox(string pdf)
            {
                var m = Find(pdf, @"(-?[\d.]+) 0 0 (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) cm\n/Im\d+ Do");
                if (!m.Success) return (double.NaN, double.NaN, double.NaN, double.NaN);

                // The height is written negative to undo the flip the page matrix applies to
                // bitmaps, and the anchor is the frame's bottom edge - so the top is the one
                // plus the other.
                double w = Num(m.Groups[1].Value), h = -Num(m.Groups[2].Value);
                return (Num(m.Groups[3].Value), Num(m.Groups[4].Value) - h, w, h);
            }

            // --- the whole sheet ------------------------------------------------------
            var full = new Backdrop(Paper());
            string svgFull = Svg(Sheet(full)), pdfFull = Pdf(Sheet(full));

            var sheetSvg = SvgBox(svgFull);
            var sheetPdf = PdfBox(pdfFull);
            Console.WriteLine($"    feuille  SVG {sheetSvg}");
            Console.WriteLine($"             PDF {sheetPdf}");

            AssertEqual(testName, "the SVG draws it", true, svgFull.Contains("<image "), 0);
            AssertEqual(testName, "and so does the PDF", true,
                System.Text.RegularExpressions.Regex.IsMatch(pdfFull, @"/Im\d+ Do"), 0);

            AssertEqual(testName, "it covers the sheet - left", 0.0, sheetSvg.X, 0.01);
            AssertEqual(testName, "it covers the sheet - top", 0.0, sheetSvg.Y, 0.01);
            AssertEqual(testName, "it covers the sheet - width", 210.0, sheetSvg.W, 0.01);
            AssertEqual(testName, "it covers the sheet - height", 297.0, sheetSvg.H, 0.01);

            // The confrontation this test exists for. Each output was self-consistent on the
            // day the watermark turned one way in the SVG and the other way in the PDF, and
            // no assertion put the two side by side. Millimetre for millimetre, then.
            AssertEqual(testName, "the two outputs agree - left", sheetSvg.X, sheetPdf.X, 0.01);
            AssertEqual(testName, "the two outputs agree - top", sheetSvg.Y, sheetPdf.Y, 0.01);
            AssertEqual(testName, "the two outputs agree - width", sheetSvg.W, sheetPdf.W, 0.01);
            AssertEqual(testName, "the two outputs agree - height", sheetSvg.H, sheetPdf.H, 0.01);

            // --- behind, which is what the word means ---------------------------------
            AssertEqual(testName, "the SVG draws it before the text it sits behind", true,
                svgFull.IndexOf("<image ", StringComparison.Ordinal)
                < svgFull.IndexOf("<text ", StringComparison.Ordinal), 0);
            AssertEqual(testName, "and so does the PDF", true,
                pdfFull.IndexOf(" Do", StringComparison.Ordinal)
                < pdfFull.IndexOf("BT\n", StringComparison.Ordinal), 0);

            // --- and out of the structure tree ----------------------------------------
            // Not "the file contains /Artifact BMC somewhere" - section 2 is about exactly
            // that kind of assertion. The mark has to open immediately before this picture
            // and close immediately after it, with nothing else in between.
            AssertEqual(testName, "the picture is set aside as decoration, not tagged", true,
                System.Text.RegularExpressions.Regex.IsMatch(pdfFull,
                    @"/Artifact BMC\nq\n(/GS\d+ gs\n)?-?[\d.]+ 0 0 -?[\d.]+ -?[\d.]+ -?[\d.]+ cm\n/Im\d+ Do\nQ\nEMC"), 0);

            // --- and it can keep its proportions --------------------------------------
            // The type had no scaling mode at first, on the reasoning that the engine
            // decodes nothing and so cannot know a picture's shape. True of the type as
            // it stood, and untrue the moment it could be told - which is what a natural
            // size is for, and what an ImageItem has always had.
            //
            // Two calibres, because Fit has two branches and only one of them centres:
            // a wide picture is limited by the width and sits flush, a tall one is
            // limited by the height and has to be put in the middle of what is left.
            var wide = new Backdrop(Paper())
            {
                Scaling = ImageScaling.Uniform, NaturalWidth = 200, NaturalHeight = 100
            };
            var wideBox = SvgBox(Svg(Sheet(wide)));
            Console.WriteLine($"    2:1      SVG {wideBox}");

            AssertEqual(testName, "a wide picture takes the sheet's width", 210.0, wideBox.W, 0.01);
            AssertEqual(testName, "and half of it in height", 105.0, wideBox.H, 0.01);
            AssertEqual(testName, "sitting flush at the left", 0.0, wideBox.X, 0.01);
            // Centred down the sheet, which is the default and the only thing Fit does
            // not decide: an image in the flow goes to the top of the frame the flow
            // gave it, and a backdrop's frame is the whole sheet, so nothing has
            // decided anything until the backdrop says.
            AssertEqual(testName, "and centred down the sheet", 96.0, wideBox.Y, 0.01);

            // The nine positions, out of the two axes the model already has. Corners
            // rather than edges: the top-left and the bottom-right cannot both be right
            // by accident, which an edge could be.
            var cornered = new Backdrop(Paper())
            {
                Scaling = ImageScaling.Uniform, NaturalWidth = 200, NaturalHeight = 100,
                AlignHorizontal = ItemAlign.Start, AlignVertical = ItemAlign.Start
            };
            var topLeft = SvgBox(Svg(Sheet(cornered)));
            Console.WriteLine($"    coin HG  SVG {topLeft}");
            AssertEqual(testName, "placed top-left, it is at the top", 0.0, topLeft.Y, 0.01);

            cornered.AlignHorizontal = ItemAlign.End;
            cornered.AlignVertical = ItemAlign.End;
            var bottomRight = SvgBox(Svg(Sheet(cornered)));
            Console.WriteLine($"    coin BD  SVG {bottomRight}");
            AssertEqual(testName, "and placed bottom-right, at the bottom",
                297.0 - 105.0, bottomRight.Y, 0.01);
            // Its width is the sheet's, so there is no slack across and the corner is
            // the same on that axis - which is worth saying rather than leaving as a
            // number that happens to match.
            AssertEqual(testName, "with no slack across for it to move in",
                0.0, bottomRight.X, 0.01);

            var tall = new Backdrop(Paper())
            {
                Scaling = ImageScaling.Uniform, NaturalWidth = 100, NaturalHeight = 200
            };
            var tallBox = SvgBox(Svg(Sheet(tall)));
            Console.WriteLine($"    1:2      SVG {tallBox}");

            AssertEqual(testName, "a tall picture is limited by the height", 297.0, tallBox.H, 0.01);
            AssertEqual(testName, "keeping its ratio", 148.5, tallBox.W, 0.01);
            AssertEqual(testName, "and is centred across what is left", 30.75, tallBox.X, 0.01);
            AssertEqual(testName, "with no slack down for it to move in", 0.0, tallBox.Y, 0.01);

            // The two outputs have to agree here too - this is the branch where they
            // could differ, since the SVG could as easily have been told to fit the
            // picture itself and would then have chosen its own arithmetic.
            var tallPdf = PdfBox(Pdf(Sheet(tall)));
            Console.WriteLine($"             PDF {tallPdf}");
            AssertEqual(testName, "the two outputs agree on the fitted width", tallBox.W, tallPdf.W, 0.01);
            AssertEqual(testName, "and on where it is centred", tallBox.X, tallPdf.X, 0.01);

            // Asked to keep proportions with none declared, it fills the rectangle. The
            // honest answer: a quarter of a page, drawn confidently, would be worse than
            // a page - and there is nothing to preserve.
            var unmeasured = new Backdrop(Paper()) { Scaling = ImageScaling.Uniform };
            var unmeasuredBox = SvgBox(Svg(Sheet(unmeasured)));
            Console.WriteLine($"    sans     SVG {unmeasuredBox}");
            AssertEqual(testName, "with no natural size it fills the sheet",
                true, Math.Abs(unmeasuredBox.W - 210) < 0.01 && Math.Abs(unmeasuredBox.H - 297) < 0.01, 0);

            // And the measurements survive being saved, or an editor that measured the
            // file once would lose it the moment the template was reopened.
            Report keptRatio = Report.FromJson(Sheet(tall).ToJson());
            AssertEqual(testName, "the natural size survives the round trip", true,
                keptRatio.Backdrop is { Scaling: ImageScaling.Uniform } r
                && Math.Abs(r.NaturalWidth - 100) < 0.01 && Math.Abs(r.NaturalHeight - 200) < 0.01, 0);

            // --- what the margins leave -----------------------------------------------
            var inside = new Backdrop(Paper()) { Area = BackdropArea.Margins };
            var insideBox = SvgBox(Svg(Sheet(inside)));
            Console.WriteLine($"    marges   SVG {insideBox}");

            AssertEqual(testName, "the margin backdrop starts at the left margin", 20.0, insideBox.X, 0.01);
            AssertEqual(testName, "and at the top margin", 25.0, insideBox.Y, 0.01);
            AssertEqual(testName, "and stops at the right one", 175.0, insideBox.W, 0.01);
            AssertEqual(testName, "and at the bottom one", 242.0, insideBox.H, 0.01);

            // A header takes a band out of the body, so a top read off the body band would
            // start the backdrop below the letterhead - the one place the two bands differ,
            // and the reason this reads the header band when there is one.
            var headed = SvgBox(Svg(Sheet(inside, header: true)));
            Console.WriteLine($"    en-tete  SVG {headed}");
            AssertEqual(testName, "a header does not push the backdrop down", 25.0, headed.Y, 0.01);
            AssertEqual(testName, "and it still reaches the bottom margin", 242.0, headed.H, 0.01);

            // --- faded ----------------------------------------------------------------
            var faint = new Backdrop(Paper()) { Opacity = 0.2 };
            string svgFaint = Svg(Sheet(faint)), pdfFaint = Pdf(Sheet(faint));

            var svgAlpha = Find(svgFaint, @"<image[^>]*opacity=""([\d.]+)""");
            string state = Find(pdfFaint, @"/Artifact BMC\nq\n/(GS\d+) gs").Groups[1].Value;
            var pdfAlpha = Find(pdfFaint, "/" + state + @" << /Type /ExtGState /BM /Normal /ca ([\d.]+)");

            AssertEqual(testName, "the SVG fades it", true, svgAlpha.Success, 0);
            AssertEqual(testName, "the PDF names a graphics state for it", true, pdfAlpha.Success, 0);

            Console.WriteLine($"    voile    SVG {svgAlpha.Groups[1].Value}  PDF {pdfAlpha.Groups[1].Value}");
            AssertEqual(testName, "and both fade it by the same amount",
                Num(svgAlpha.Groups[1].Value), Num(pdfAlpha.Groups[1].Value), 0.001);

            // Full strength costs nothing in either file: an opacity attribute of 1 and a
            // graphics state that changes nothing are both noise a reader has to discount.
            AssertEqual(testName, "full strength writes no SVG attribute", false,
                System.Text.RegularExpressions.Regex.IsMatch(svgFull, @"<image[^>]*opacity="), 0);
            AssertEqual(testName, "and no PDF graphics state", false,
                System.Text.RegularExpressions.Regex.IsMatch(pdfFull, @"/Artifact BMC\nq\n/GS\d+ gs"), 0);

            // --- which pages ----------------------------------------------------------
            string everyPage = Svg(Sheet(full, paragraphs: 12));
            string firstOnly = Svg(Sheet(new Backdrop(Paper()) { PageScope = PageScope.First }, paragraphs: 12));

            int pages = CountOccurrences(everyPage, "<svg ");
            int drawnEverywhere = CountOccurrences(everyPage, "<image ");
            int drawnOnce = CountOccurrences(firstOnly, "<image ");
            Console.WriteLine($"    pages {pages}, fonds {drawnEverywhere} / premiere seule {drawnOnce}");

            AssertEqual(testName, "the sample runs to several pages", true, pages > 1, 0);
            AssertEqual(testName, "every page carries it by default", pages, drawnEverywhere, 0);
            AssertEqual(testName, "and a narrowed scope carries it once", 1, drawnOnce, 0);

            // --- nothing to draw ------------------------------------------------------
            // A backdrop carrying no bytes draws nothing and sets nothing aside either: an
            // empty artifact would be a mark around no marks, which says nothing to anybody
            // and which a validator is entitled to take a view on.
            AssertEqual(testName, "an empty backdrop draws nothing", false,
                Svg(Sheet(new Backdrop())).Contains("<image "), 0);
            AssertEqual(testName, "and opens no artifact", false,
                Pdf(Sheet(new Backdrop())).Contains("/Artifact BMC"), 0);

            // --- it survives being saved ----------------------------------------------
            Report reloaded = Report.FromJson(Sheet(inside).ToJson());
            AssertEqual(testName, "it survives the round trip", true,
                reloaded.Backdrop is { Area: BackdropArea.Margins } kept
                && kept.Source.Data.Length == stock.Length, 0);

            // Opacity's default is 1 and default(double) is 0, so the property the template
            // leaves out is the one set to zero - a backdrop kept in the file and turned
            // off. Read back it would return as the constructor left it, at full strength:
            // turning a letterhead off would turn it on. Hence the JsonIgnore(Never), and
            // hence this assertion being about zero rather than about one, which the
            // serializer writes anyway and which proves nothing.
            string muted = Sheet(new Backdrop(Paper()) { Opacity = 0 }).ToJson();
            bool statesIt = muted.Contains(OPACITY_KEY);
            Console.WriteLine($"    un fond eteint s'ecrit dans le fichier : {statesIt}");
            AssertEqual(testName, "a backdrop turned off says so rather than going silent",
                true, statesIt, 0);
            AssertEqual(testName, "so it is still off after a save",
                0.0, Report.FromJson(muted).Backdrop!.Opacity, 0.0001);
            AssertEqual(testName, "and full strength survives too",
                1.0, Report.FromJson(Sheet(full).ToJson()).Backdrop!.Opacity, 0.0001);

            // --- and a sample somebody can open ---------------------------------------
            string dir = OutputDirectory("backdrop");
            Report sample = Sheet(new Backdrop(Paper()) { Opacity = 0.35 }, paragraphs: 2);
            Demonstrates(sample.Body!, "Backdrop - printed stationery",
                "A picture drawn behind every page, faded to about a third. Look for it "
                + "reaching all four edges of the sheet, under the margins included, for the "
                + "text staying readable over it, and for it being the same picture in the "
                + "same place in the PDF and in the SVG beside it - each file was consistent "
                + "with itself on the day the watermark turned one way in one and the other "
                + "way in the other. It is decoration, so a reader speaking the page aloud "
                + "says nothing about it: that half shows in a validator, never on the page.");
            sample.Reattach();

            var proof = new SvgBackend(measurer) { EmbeddedFonts = library };
            ReportRenderer.Render(sample, proof);
            File.WriteAllText(Path.Combine(dir, "stationery.html"), proof.ToHtml());

            var archived = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            ReportRenderer.Render(sample, archived);
            WriteSamplePdf(Path.Combine(dir, "stationery.pdf"), archived.ToArray());

            Console.WriteLine($"    ecrit dans {dir}");
            Console.WriteLine();
        }

/// <summary>
        /// The paper colour is carried and never printed.
        ///
        /// Report.ProofBackground says what stock the document is meant for, so an editor
        /// can show the page against it. Nothing in the engine draws it, and that is a
        /// claim rather than a fact until something renders the same report with it and
        /// without and finds the bytes identical.
        ///
        /// The SVG matters most here. It is the engine's own evidence of what the PDF
        /// will contain, and the two are held to each other; a colour in one and not the
        /// other is exactly the divergence that pairing exists to catch. So the proof
        /// colour is in neither.
        /// </summary>
        [Fact]
        public void Test72_PaperIsCarriedNotPrinted()
        {
            const string testName = "Test 72";
            Console.WriteLine("Test 72: the paper colour is carried, never printed");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static Report Sheet(Color? paper)
            {
                var report = new Report
                {
                    Title = "Proof", Language = "en-GB",
                    GeneratedAt = new DateTimeOffset(2026, 3, 9, 14, 5, 9, TimeSpan.Zero),
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack },
                    ProofBackground = paper
                };
                report.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };
                report.Body!.Add(new TextItem { Text = "On cream stock", Font = new() { Size = 3 } });
                report.Body!.Add(new TextItem
                {
                    Text = string.Join(" ", Enumerable.Repeat("The supplier undertakes.", 40)),
                    Font = new() { Size = 2 }
                });
                report.Reattach();
                return report;
            }

            var cream = new Color(250, 244, 227);

            byte[] Pdf(Report report)
            {
                var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
                ReportRenderer.Render(report, pdf);
                return pdf.ToArray();
            }

            string Svg(Report report)
            {
                var svg = new SvgBackend(measurer);
                ReportRenderer.Render(report, svg);
                return string.Join("\n", svg.Pages);
            }

            byte[] withPaper = Pdf(Sheet(cream)), without = Pdf(Sheet(null));
            string svgWith = Svg(Sheet(cream)), svgWithout = Svg(Sheet(null));
            string mailWith = FlowingHtml.Write(Sheet(cream));
            string mailWithout = FlowingHtml.Write(Sheet(null));

            Console.WriteLine($"    PDF  {withPaper.Length} contre {without.Length} octets");
            Console.WriteLine($"    SVG  {svgWith.Length} contre {svgWithout.Length} caracteres");
            Console.WriteLine($"    mail {mailWith.Length} contre {mailWithout.Length} caracteres");

            AssertEqual(testName, "the PDF is the same file either way",
                true, withPaper.AsSpan().SequenceEqual(without), 0);
            AssertEqual(testName, "so is the SVG proof, which the PDF is held to",
                svgWithout, svgWith, 0);
            AssertEqual(testName, "and the flowing HTML, which is a delivery and not a page",
                mailWithout, mailWith, 0);

            // The colour is not silently dropped either: it survives the file, or an
            // editor loses it the moment a template is saved and reopened - which is the
            // whole reason it lives on the report rather than in an editor's settings.
            Report reloaded = Report.FromJson(Sheet(cream).ToJson());
            Console.WriteLine($"    apres l'aller-retour : {reloaded.ProofBackground?.R},"
                              + $"{reloaded.ProofBackground?.G},{reloaded.ProofBackground?.B}");
            AssertEqual(testName, "the paper survives the round trip", true,
                reloaded.ProofBackground is { R: 250, G: 244, B: 227 }, 0);

            // And a report that says nothing keeps saying nothing, rather than acquiring
            // a white it never asked for - null is what an editor reads as "no proof".
            AssertEqual(testName, "and a report that states none still states none",
                true, Report.FromJson(Sheet(null).ToJson()).ProofBackground == null, 0);

            // Report.Swatches is the same kind of thing and gets the same treatment: a
            // list of colours an editor offers back in one click, which is a record of
            // choices and not a mark on a page. Held here rather than in its own test
            // because "carried, never printed" is one rule and a second test would be a
            // second place to keep it.
            Report inked = Sheet(null), bare = Sheet(null);
            inked.Swatches.Add(new Color(200, 30, 40));
            inked.Swatches.Add(new Color(30, 60, 200));

            Console.WriteLine($"    PDF avec {inked.Swatches.Count} couleurs memorisees : "
                              + $"{Pdf(inked).Length} contre {Pdf(bare).Length} octets");
            AssertEqual(testName, "a remembered colour reaches no output either",
                true, Pdf(inked).AsSpan().SequenceEqual(Pdf(bare))
                      && Svg(inked) == Svg(bare)
                      && FlowingHtml.Write(inked) == FlowingHtml.Write(bare), 0);

            Report backAgain = Report.FromJson(inked.ToJson());
            AssertEqual(testName, "and it survives the round trip, in order",
                true, backAgain.Swatches.Count == 2
                      && backAgain.Swatches[0] is { R: 200, G: 30, B: 40 }
                      && backAgain.Swatches[1] is { R: 30, G: 60, B: 200 }, 0);

            Console.WriteLine();
        }
    }
}
