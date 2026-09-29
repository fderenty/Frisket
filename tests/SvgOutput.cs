namespace test
{
    /// <summary>
    /// SVG pages with the fonts embedded, and bare fragments: this is the engine's own proof
    /// of what the printer will do, which is why it is held to the PDF millimetre for
    /// millimetre. Not to be confused with the flowing HTML next door - that one is a
    /// delivery, this one is a picture of a page.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>
        /// A media type reaches the drawing as text and never as markup.
        ///
        /// <c>SvgBackend</c> routed every string it writes through <c>Escape</c> except
        /// one - the media type of an image - and that one is not the engine's:
        /// <c>ReportBinder</c> took whatever sat between <c>data:</c> and the first
        /// comma, so a canvas posting <c>data:image/png" onerror="..."</c> put a second
        /// attribute into the tag. Section 4b is explicit that this is the shape a
        /// browser posts when it captures a signature.
        ///
        /// Mended at both ends and checked at both, because the two are reachable
        /// separately: the binder now stores only what is shaped like a media type, and
        /// a template that carries a hostile one in its own JSON never goes through the
        /// binder at all.
        ///
        /// Read with an XML reader rather than with Contains. Section 2: an assertion
        /// that looks for bytes proves nothing - what matters is what a parser makes of
        /// them, and "the string is in there somewhere" is true of the safe version too.
        /// </summary>
        [Fact]
        public void Test81_AMediaTypeIsTextAndNeverMarkup()
        {
            const string testName = "Test 81";
            Console.WriteLine("Test 81: a media type reaches the drawing as text, never as markup");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            string png = Convert.ToBase64String(PngEncoder.Rgb(8, 8, (x, y) => (200, 30, 30)));
            const string payload = "image/png\" onerror=\"boom()\" q=";

            static Report Sheet(Action<Report> fill)
            {
                var report = new Report
                {
                    Title = "Injected", Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                fill(report);
                report.Reattach();
                return report;
            }

            string Draw(Report report)
            {
                var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
                ReportRenderer.Render(report, svg);
                return svg.Pages[0];
            }

            // How many attributes the <image> really has, asked of a parser.
            static (List<string> Names, string Href) ImageTag(string page)
            {
                // A page that is no longer XML is the loudest possible answer, and it
                // arrived as a stack trace until this catch: an unescaped quote closes
                // the attribute and the rest of the tag is nonsense. Reported in the
                // test's own words instead, so the assertion below fires by name.
                try
                {
                    return ReadImage(page);
                }
                catch (System.Xml.XmlException)
                {
                    return (new List<string>(), "");
                }
            }

            static (List<string> Names, string Href) ReadImage(string page)
            {
                using var reader = System.Xml.XmlReader.Create(new StringReader(page));
                while (reader.Read())
                {
                    if (reader.NodeType != System.Xml.XmlNodeType.Element || reader.Name != "image")
                        continue;

                    var names = new List<string>();
                    for (int i = 0; i < reader.AttributeCount; i++)
                    {
                        reader.MoveToAttribute(i);
                        names.Add(reader.Name);
                    }
                    reader.MoveToElement();

                    string href = reader.GetAttribute("href")
                                  ?? reader.GetAttribute("xlink:href") ?? "";
                    return (names, href);
                }
                return (new List<string>(), "");
            }

            // --- the template's own JSON, which never meets the binder ---------------
            var carried = Sheet(report =>
            {
                var picture = new ImageItem
                {
                    AltText = "a picture",
                    Source = new ImageSource { MediaType = payload, Data = Convert.FromBase64String(png) },
                    NaturalWidth = 20, NaturalHeight = 20
                };
                picture.Style.Location.WidthValue = 20;
                picture.Style.Location.HeightValue = 20;
                report.Body!.Add(picture);
            });

            string drawn = Draw(carried);
            var tag = ImageTag(drawn);
            Console.WriteLine($"    depuis le gabarit : {string.Join(", ", tag.Names)}");

            AssertEqual(testName, "the page is still well-formed XML", true, tag.Names.Count > 0, 0);

            // Asked of the parser, not of the string. `onerror=` IS in the file - escaped,
            // as text inside the href - so a Contains here would fail on the mended
            // version and pass on nothing. Section 2, landing on this very check while it
            // was being written.
            AssertEqual(testName, "and the tag gained no attribute of its own",
                false, tag.Names.Any(n => n.Equals("onerror", StringComparison.OrdinalIgnoreCase)), 0);

            // Six: x, y, width, height, preserveAspectRatio, href. Counted as well as
            // named, so any smuggled attribute fails this and not only the one tried.
            AssertEqual(testName, "and no attribute at all beyond the six it writes",
                6, tag.Names.Count, 0);

            AssertEqual(testName, "the payload is inside the href, as text",
                true, tag.Href.Contains("onerror"), 0);

            // --- the same thing arriving from the data -------------------------------
            var bound = ReportBinder.Bind(Sheet(report =>
            {
                var picture = new ImageItem { SourceField = "Sig", AltText = "a signature",
                                              NaturalWidth = 20, NaturalHeight = 20 };
                picture.Style.Location.WidthValue = 20;
                picture.Style.Location.HeightValue = 20;
                report.Body!.Add(picture);
            // The payload carries quotes, so the JSON that transports it escapes them -
            // written with a serializer rather than by hand, because a hand-built string
            // was invalid JSON and this test failed on its own data for three runs while
            // I read the output for assertion failures and never for exceptions.
            }), System.Text.Json.JsonSerializer.Serialize(
                   new Dictionary<string, string> { ["Sig"] = $"data:{payload},{png}" }));

            var fromData = bound.AllItems.OfType<ImageItem>().First();
            Console.WriteLine($"    depuis les donnees : MediaType = {fromData.Source.MediaType}");

            // The binder keeps its default rather than storing something that only looks
            // like a media type - so the nonsense never reaches the model at all.
            AssertEqual(testName, "what is not a media type is not stored as one",
                "image/png", fromData.Source.MediaType, 0);

            AssertEqual(testName, "and nothing of it reaches the drawing",
                false, Draw(bound).Contains("onerror"), 0);

            // An ordinary one is untouched, or the guard above would be "refuse
            // everything", which passes every check here and breaks every JPEG.
            var ordinary = ReportBinder.Bind(Sheet(report =>
            {
                var picture = new ImageItem { SourceField = "Sig", AltText = "a signature",
                                              NaturalWidth = 20, NaturalHeight = 20 };
                report.Body!.Add(picture);
            }), System.Text.Json.JsonSerializer.Serialize(
                   new Dictionary<string, string> { ["Sig"] = $"data:image/jpeg;base64,{png}" }));

            AssertEqual(testName, "while a real one passes through",
                "image/jpeg", ordinary.AllItems.OfType<ImageItem>().First().Source.MediaType, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test17_SvgInvoice()
        {
            var testName = "Test 17: SVG Invoice";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = "Invoice F-2025-0142",
                Dimension = new() { Width = 210, Height = 297 },
                RootFontSize = 3.0,
                GeneratedAt = new DateTimeOffset(2025, 7, 24, 9, 30, 0, TimeSpan.Zero),
                Header = new(),
                Footer = new(),
                Body = new()
            };
            r.Fields["Customer"] = "ACME Industries";
            r.Section.Style.Padding = new() { Left = 15, Right = 15, Top = 12, Bottom = 12 };

            // --- header -----------------------------------------------------
            r.Header!.Style.Location.Height = (32, Units.Millimeter);
            r.Header.Add(new TextItem
            {
                Text = "{Title}",
                Font = new() { Family = InvoiceFont, Size = 2, SizeUnit = FontSizeUnit.Rem, Bold = true },
                Style = new() { Location = new() { Left = (0, Units.Millimeter), Top = (0, Units.Millimeter) } }
            });
            r.Header.Add(new TextItem
            {
                Text = "{Customer}\nEmise le {Date}",
                Style = new() { Location = new() { Right = (0, Units.Millimeter), Top = (0, Units.Millimeter) } },
                Font = new() { Family = InvoiceFont, HorizontalAlignment = HorizontalAlignment.Right }
            });
            r.Header.Add(new LineItem
            {
                Pen = new() { Width = 0.4 },
                Style = new() { Location = new() { Bottom = (2, Units.Millimeter), Dock = Dock.Left | Dock.Right } }
            });

            // --- footer -----------------------------------------------------
            r.Footer!.Style.Location.Height = (14, Units.Millimeter);
            r.Footer.Add(new LineItem
            {
                Pen = new() { Width = 0.2, Color = new(160, 160, 160) },
                Style = new() { Location = new() { Top = (0, Units.Millimeter), Dock = Dock.Left | Dock.Right } }
            });
            r.Footer.Add(new TextItem
            {
                Text = "Page {Page} / {Pages}",
                Font = new() { Family = InvoiceFont, Size = 0.85, SizeUnit = FontSizeUnit.Rem,
                               HorizontalAlignment = HorizontalAlignment.Right, Color = new(90, 90, 90) },
                Style = new() { Location = new() { Right = (0, Units.Millimeter), Bottom = (0, Units.Millimeter) } }
            });

            // --- body -------------------------------------------------------
            var table = new TableItem
            {
                Grid = new() { Width = 0.15, Color = new(180, 180, 180) },
                RepeatHeader = true
            };
            table.AddColumn(22);
            table.AddAutoColumn();
            table.AddColumn(18);
            table.AddColumn(26);

            var head = table.AddHeaderRow(
                Label("Ref", bold: true),
                Label("Description", bold: true),
                Label("Qty", bold: true, align: HorizontalAlignment.Right),
                Label("Total HT", bold: true, align: HorizontalAlignment.Right));
            head.Style.Background = new() { Color = new(238, 238, 238) };
            head.Style.Padding = new() { Left = 1.5, Right = 1.5, Top = 1.5, Bottom = 1.5 };

            for (int i = 1; i <= 45; i++)
            {
                var row = table.AddRow(
                    Label($"A-{i:000}"),
                    Label($"Service no. {i}"),
                    Label((i % 7 + 1).ToString(), align: HorizontalAlignment.Right),
                    Label($"{(i * 19.9):0.00} €", align: HorizontalAlignment.Right));
                row.Style.Padding = new() { Left = 1.5, Right = 1.5, Top = 1.2, Bottom = 1.2 };
            }
            r.Body!.Add(table);

            // --- render -----------------------------------------------------
            // Real metrics, and the same font embedded in the preview so the browser
            // draws with what the layout measured.
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            var backend = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            var layout = ReportRenderer.Render(r, backend);

            AssertEqual(testName, "several pages", true, layout.PageCount > 1, 0);
            AssertEqual(testName, "one svg per page", layout.PageCount, backend.Pages.Count, 0);

            string first = backend.Pages[0];
            AssertEqual(testName, "svg is millimeter based", true, first.Contains("width=\"210mm\""), 0);
            AssertEqual(testName, "title token resolved", true, first.Contains("Invoice F-2025-0142"), 0);
            AssertEqual(testName, "custom field resolved", true, first.Contains("ACME Industries"), 0);
            AssertEqual(testName, "page counter resolved", true,
                first.Contains($"Page 1 / {layout.PageCount}"), 0);
            AssertEqual(testName, "grid drawn", true, first.Contains("stroke-width=\"0.15\""), 0);
            AssertEqual(testName, "header row shaded", true, first.Contains("#EEEEEE"), 0);
            AssertEqual(testName, "header repeated", true, backend.Pages[1].Contains("Description"), 0);
            AssertEqual(testName, "escaping works", false, first.Contains("n°<"), 0);

            var outputDir = OutputDirectory("invoice");
            string index = backend.SaveTo(outputDir, "invoice");
            Console.WriteLine($"  🖨  {layout.PageCount} pages -> {index}");

            Console.WriteLine();
        }

        [Fact]
        public void Test41_SvgFragment()
        {
            var testName = "Test 41: The SVG as pieces to embed";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            Report report = BoundAcmeInvoice(30);
            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(report, svg);

            SvgBackend.SvgFragment fragment = svg.ToFragment();

            Console.WriteLine($"    {fragment.Pages.Count} pages, css {fragment.FontCss.Length / 1024} ko, "
                              + $"page {fragment.PageWidth}x{fragment.PageHeight} mm");

            AssertEqual(testName, "one element per page", svg.Pages.Count, fragment.Pages.Count, 0);
            AssertEqual(testName, "and the sheet size travels", 210.0, fragment.PageWidth, 0.01);

            // Each page is a plain element, with nothing wrapped around it: that is what
            // makes it droppable into a layout somebody else owns.
            foreach (string page in fragment.Pages)
            {
                AssertEqual(testName, "a page is one svg element", true,
                    page.StartsWith("<svg", StringComparison.Ordinal)
                    && page.TrimEnd().EndsWith("</svg>", StringComparison.Ordinal), 0);

                AssertEqual(testName, "carrying no section of its own", false,
                    page.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
                    || page.Contains("<html", StringComparison.OrdinalIgnoreCase), 0);

                // The fonts are deliberately not in here: a page holding twenty invoices
                // needs them once, not twenty times.
                AssertEqual(testName, "and no font of its own", false, page.Contains("@font-face"), 0);
            }

            // Which means the caller has to be given them separately, or the text renders
            // in whatever the browser feels like - still readable, no longer the section
            // that was laid out.
            AssertEqual(testName, "the fonts come separately", true,
                fragment.FontCss.Contains("@font-face") && fragment.FontCss.Contains(DmsFont), 0);

            // The standalone file is the same pieces, assembled.
            string html = svg.ToHtml();
            AssertEqual(testName, "the whole file uses the same pieces", true,
                html.Contains(fragment.FontCss) && html.Contains(fragment.Pages[0]), 0);

            // The other way in: a frame needs a section of its own, so the standalone
            // file is what goes there - without the viewer's grey surround, which inside
            // a frame reads as a band of somebody else's page.
            string viewer = svg.ToHtml();
            string bare = svg.ToHtml(SvgBackend.HtmlChrome.Bare);

            AssertEqual(testName, "both forms are whole sections", true,
                viewer.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase)
                && bare.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase), 0);

            AssertEqual(testName, "the viewer has its surround", true, viewer.Contains("#525659"), 0);
            AssertEqual(testName, "the bare one has none", false, bare.Contains("#525659"), 0);
            // The print stylesheet says box-shadow:none, so the test asks about a shadow
            // being drawn rather than about the word appearing.
            AssertEqual(testName, "nor a shadow", false, bare.Contains("box-shadow:0"), 0);
            AssertEqual(testName, "and carries the same pages", true, bare.Contains(fragment.Pages[0]), 0);

            // What embedding actually looks like, written out so it can be opened.
            string dir = OutputDirectory("fragment");
            File.WriteAllText(Path.Combine(dir, "cadre.html"), bare);
            var host = new System.Text.StringBuilder();
            host.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\">")
                .Append("<title>Embedded in an existing page</title>\n<style>\n")
                .Append(fragment.FontCss)
                .Append("body{font-family:system-ui;margin:0;padding:24px;background:#eef1f4}\n")
                .Append(".mine{max-width:900px;margin:0 auto}\n")
                .Append(".mine svg{width:100%;height:auto;box-shadow:0 1px 6px rgba(0,0,0,.25);")
                .Append("background:#fff;margin-bottom:16px;display:block}\n")
                .Append("</style></head><body><div class=\"mine\">\n")
                .Append("<h1>A page of the application</h1>\n")
                .Append("<p>The report sits here, with no page of its own.</p>\n");

            foreach (string page in fragment.Pages)
                host.Append(page).Append('\n');

            host.Append("<h2>Or inside a frame</h2>\n")
                .Append("<iframe src=\"cadre.html\" style=\"width:100%;height:420px;border:1px solid #ccd\"")
                .Append(" title=\"the same report, inside a frame\"></iframe>\n")
                .Append("</div></body></html>\n");
            File.WriteAllText(Path.Combine(dir, "integre.html"), host.ToString());

            Console.WriteLine($"  □ {Path.Combine(dir, "integre.html")}");
            Console.WriteLine();
        }
    }
}
