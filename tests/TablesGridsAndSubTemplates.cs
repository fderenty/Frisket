namespace test
{
    /// <summary>
    /// Containers that repeat or are spliced in: column widths, table pagination, a split
    /// row, group bands and their subtotals, image grids, and another template's body
    /// spliced in - including the files it may not read.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test15_TableGeometry()
        {
            var testName = "Test 15: Table Geometry";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);

            // 30mm fixed, 25% of 210 = 52.5mm, then two auto columns share what is left.
            var table = new TableItem();
            table.AddColumn(30);
            table.AddColumn(25, Units.Percentage);
            table.AddAutoColumn();
            table.AddAutoColumn();

            var header = table.AddHeaderRow(Cell("Ref"), Cell("Label"), Cell("Qty"), Cell("Total"));
            var row = table.AddRow(Cell("A-1"), Cell("Widget"), Cell("2"), Cell("19.90"));

            // The table is built standalone and attached afterwards.
            r.Body!.Add(table);

            var layout = r.Resolve();
            var items = layout.Pages[0].Items;

            Rect FrameOf(BaseItem item) => items.First(p => ReferenceEquals(p.Source, item)).Frame;

            var c0 = FrameOf(header.Childs[0]);
            var c1 = FrameOf(header.Childs[1]);
            var c2 = FrameOf(header.Childs[2]);
            var c3 = FrameOf(header.Childs[3]);

            AssertEqual(testName, "fixed column", 30, c0.Width, 0.01);
            AssertEqual(testName, "percentage column", 52.5, c1.Width, 0.01);
            AssertEqual(testName, "auto columns share the rest", 63.75, c2.Width, 0.01);
            AssertEqual(testName, "auto columns are equal", c2.Width, c3.Width, 0.01);
            AssertEqual(testName, "columns are contiguous", c0.Right, c1.Left, 0.01);
            AssertEqual(testName, "columns fill the width", 210, c3.Right, 0.01);

            AssertEqual(testName, "header row at band top", 20, FrameOf(header).Top, 0.1);
            AssertEqual(testName, "data row follows header", FrameOf(header).Bottom, FrameOf(row).Top, 0.01);

            Console.WriteLine();
        }

        [Fact]
        public void Test16_TablePagination()
        {
            var testName = "Test 16: Table Pagination";
            Console.WriteLine($"Running {testName}...");

            var r = BandedReport(testName);
            var table = new TableItem { RepeatHeader = true };
            table.AddColumn(50);
            table.AddAutoColumn();

            var header = table.AddHeaderRow(Cell("Ref"), Cell("Label"));
            var rows = new List<TableRow>();
            for (int i = 0; i < 40; i++)
            {
                var row = table.AddRow(Cell($"A-{i}"), Cell($"Item {i}"));
                row.Style.Location.Height = (20, Units.Millimeter);
                rows.Add(row);
            }
            r.Body!.Add(table);

            var layout = r.Resolve();

            AssertEqual(testName, "table spans several pages", true, layout.PageCount > 1, 0);

            // The header is repeated at the top of every page the table reaches.
            foreach (var page in layout.Pages)
                AssertEqual(testName, $"header repeated on page {page.Number}", 1, CountOn(page, header), 0);

            // Every row appears exactly once, in order, and none is cut in half.
            var seen = new List<TableRow>();
            foreach (var page in layout.Pages)
            {
                foreach (var placed in page.Items)
                {
                    if (placed.Source is TableRow tr && !tr.IsHeader)
                        seen.Add(tr);
                }
            }
            AssertEqual(testName, "every row placed once", 40, seen.Count, 0);
            AssertEqual(testName, "rows stay in order", true, seen.SequenceEqual(rows), 0);

            bool allInside = layout.Pages.All(page =>
                page.Items.Where(p => p.Source is TableRow)
                          .All(p => p.Frame.Bottom <= page.BodyBand.Bottom + 0.01));
            AssertEqual(testName, "no row overflows its band", true, allInside, 0);

            // Turning repetition off leaves the header on the first page only.
            table.RepeatHeader = false;
            var once = r.Resolve();
            AssertEqual(testName, "header on page 1 without repeat", 1, CountOn(once.Pages[0], header), 0);
            AssertEqual(testName, "no header on page 2 without repeat", 0, CountOn(once.Pages[1], header), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test39_TableRowSplits()
        {
            var testName = "Test 39: A table row split across pages";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // A short page, so the split happens without needing a hundred rows.
            var report = new Report { Title = "Coupure", Dimension = new() { Width = 210, Height = 90 } };
            var body = new Body { Layout = LayoutMode.Stack };
            body.Style.Location.Dock = Dock.Fill;
            report.Body = body;

            var table = new TableItem { RepeatHeader = true };
            table.Columns.Add(new TableColumn { Width = 30, WidthUnit = Units.Millimeter });
            table.Columns.Add(new TableColumn());
            table.Grid = new PenStyle { Width = 0.15, Color = new(150, 150, 150) };

            var head = new TableRow { IsHeader = true };
            head.Add(new TextItem { Text = "Reference", Font = new() { Bold = true, Size = 0.9 } });
            head.Add(new TextItem { Text = "Description", Font = new() { Bold = true, Size = 0.9 } });
            table.Add(head);

            // Two ordinary rows, then one whose description runs to many lines: it cannot
            // fit whole in what is left, and whole is what it used to demand.
            for (int i = 1; i <= 2; i++)
            {
                var row = new TableRow();
                row.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
                row.Add(new TextItem { Text = $"REF-{i:000}", Font = new() { Size = 0.85 } });
                row.Add(new TextItem { Text = "Standard service", Font = new() { Size = 0.85 } });
                table.Add(row);
            }

            var longRow = new TableRow();
            longRow.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
            longRow.Add(new TextItem { Text = "REF-LONG", Font = new() { Size = 0.85 } });
            longRow.Add(new TextItem
            {
                Text = string.Join(" ", Enumerable.Repeat(
                    "Full replacement of the sub-assembly, clearances checked and settings reset", 30)),
                Font = new() { Size = 0.85 }
            });
            table.Add(longRow);

            body.Add(table);

            Demonstrates(body,
                "A table row split across pages",
                "One row too tall for what is left of the page, continued on the next. Look "
                + "for the description carrying on where it stopped rather than restarting or "
                + "dropping a line, for the grid being redrawn around each half so no text "
                + "floats outside the table, for the short cell beside it still being drawn "
                + "though it has nothing left to say - a row showing fewer cells than the "
                + "table has columns is refused by veraPDF and unreadable aloud - and for the "
                + "header repeating above the continuation.");

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            LayoutResult layout = ReportRenderer.Render(report, svg);

            var parts = layout.Pages
                .SelectMany(page => page.Items.Select(item => (page.Number, Item: item)))
                .Where(x => ReferenceEquals(x.Item.Source, longRow.Childs[1]))
                .OrderBy(x => x.Number)
                .ToList();

            Console.WriteLine($"    {layout.PageCount} pages, the long row in {parts.Count} piece(s)");

            AssertEqual(testName, "the long row was split", true, parts.Count >= 2, 0);
            AssertEqual(testName, "and its parts are on different pages", parts.Count,
                parts.Select(x => x.Number).Distinct().Count(), 0);

            // Nothing lost and nothing repeated: the fragments run on from one another.
            int expected = 0;
            foreach ((int number, PlacedItem item) in parts)
            {
                AssertEqual(testName, $"page {number} resumes where the last stopped",
                    expected, item.LineStart, 0);
                AssertEqual(testName, $"page {number} carries lines", true, item.LineCount > 0, 0);
                expected += item.LineCount;
            }

            // The grid has to be redrawn around each part, or the second page shows text
            // floating outside the table.
            foreach ((int number, PlacedItem item) in parts)
            {
                bool framed = layout.Pages.First(p => p.Number == number).Items
                    .Any(r => r.Source is TableRow && r.GridEdges is { Length: > 1 }
                              && r.Frame.Top <= item.Frame.Top + 0.01
                              && r.Frame.Bottom >= item.Frame.Bottom - 0.01);

                AssertEqual(testName, $"page {number} draws the grid around it", true, framed, 0);
            }

            // The header repeats, which is what makes the continuation readable.
            AssertEqual(testName, "the header is on every page", layout.PageCount,
                layout.Pages.Count(p => p.Items.Any(i => i.Source is TableRow { IsHeader: true })), 0);

            // KeepTogether still means what it says.
            longRow.Style.Splitting = Splitting.KeepTogether;
            LayoutResult kept = report.Resolve(new FontTextMeasurer(library));
            int pieces = kept.Pages.Sum(p => p.Items.Count(i => ReferenceEquals(i.Source, longRow.Childs[1])));
            AssertEqual(testName, "KeepTogether keeps it together", 1, pieces, 0);

            longRow.Style.Splitting = Splitting.Auto;

            string dir = OutputDirectory("split");
            svg.SaveTo(dir, "split-row");

            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b };
            report.GeneratedAt = SampleDate;
            ReportRenderer.Render(report, pdf);
            WriteSamplePdf(Path.Combine(dir, "split-row.pdf"), pdf.ToArray());

            Console.WriteLine($"  \u2702 {Path.Combine(dir, "split-row.pdf")}");
            Console.WriteLine();
        }

        static string HeadingOf(Report report, int index)
        {
            var headings = report.AllItems.OfType<ContainerItem>()
                                 .Where(c => c.Style.RepeatOnBreak).ToList();
            return string.Join(" ", headings[index].Childs.OfType<TextItem>().Select(t => t.Text));
        }

        [Fact]
        public void Test27_GroupBands()
        {
            var testName = "Test 27: Group Bands";
            Console.WriteLine($"Running {testName}...");

            // The lines arrive flat, with a Family on each; the template groups them.
            Report report = BoundAcmeInvoice(12);
            var groups = Descend(report.Body!).OfType<TableItem>().ToList();

            // Twelve lines, every third one labour: two families, so two groups.
            AssertEqual(testName, "one table per group", 2, groups.Count, 0);
            AssertEqual(testName, "groups keep first-seen order", true,
                HeadingOf(report, 0).Contains("Parts") && HeadingOf(report, 1).Contains("Labour"), 0);
            AssertEqual(testName, "rows split between the groups", 12,
                groups.Sum(g => g.Childs.Count(c => c is TableRow { IsHeader: false })), 0);
            AssertEqual(testName, "eight in the first", 8,
                groups[0].Childs.Count(c => c is TableRow { IsHeader: false }), 0);
            AssertEqual(testName, "four in the second", 4,
                groups[1].Childs.Count(c => c is TableRow { IsHeader: false }), 0);
            AssertEqual(testName, "each group keeps its own column titles", true,
                groups.All(g => g.Childs.Any(c => c is TableRow { IsHeader: true })), 0);

            // Count and subtotal come from the grouping itself, not from the data.
            AssertEqual(testName, "the count is computed", true, HeadingOf(report, 0).Contains("8 line(s)"), 0);

            double expected = Enumerable.Range(1, 12).Where(i => i % 3 != 0).Sum(i => 19.90 * i);
            string subtotal = report.AllItems.OfType<TextItem>()
                                    .First(t => t.Text.StartsWith("Subtotal Parts")).Text;
            AssertEqual(testName, "and so is the subtotal",
                $"Subtotal Parts: {expected.ToString("#,##0.00", NumberFormats.Invariant)}", subtotal, 0);

            // --- a group heading follows its rows onto the next page ---------------
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            var backend = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };

            LayoutResult layout = ReportRenderer.Render(BoundAcmeInvoice(90), backend);
            AssertEqual(testName, "several pages", true, layout.PageCount > 2, 0);

            var heading = report.AllItems.OfType<TextItem>().First(t => t.Text == "Parts").Parent!;
            AssertEqual(testName, "the heading is marked to repeat", true, heading.Style.RepeatOnBreak, 0);

            Report big = BoundAcmeInvoice(90);
            LayoutResult spread = big.Resolve(new FontTextMeasurer(library));
            ContainerItem firstHeading = big.AllItems.OfType<TextItem>().First(t => t.Text == "Parts").Parent!;

            int appearances = spread.Pages.Count(p => p.Items.Any(i => ReferenceEquals(i.Source, firstHeading)));
            AssertEqual(testName, "it comes back at the top of the pages its group runs onto",
                true, appearances > 1, 0);

            // ...at the top of the band, which is the whole point of repeating it: the
            // reader opening page four still knows which family the rows belong to.
            bool atTheTop = spread.Pages
                .Where(page => page.Items.Any(i => ReferenceEquals(i.Source, firstHeading)))
                .All(page => Math.Abs(page.Items.First(i => ReferenceEquals(i.Source, firstHeading)).Frame.Top
                                      - page.BodyBand.Top) < 0.01);
            AssertEqual(testName, "at the top of the band each time", true, atTheTop, 0);

            // And only while its own group is still running: once a group is closed off
            // by its subtotal, its heading must not follow the next group around.
            TableItem firstTable = Descend(big.Body!).OfType<TableItem>().First();
            bool onlyWithItsRows = spread.Pages.All(page =>
                !page.Items.Any(i => ReferenceEquals(i.Source, firstHeading))
                || page.Items.Any(i => i.Source is TableRow { IsHeader: false } row
                                       && ReferenceEquals(row.Parent, firstTable)));
            AssertEqual(testName, "never on a page its group has left", true, onlyWithItsRows, 0);

            string dir = OutputDirectory("acme");
            string index = backend.SaveTo(dir, "acme-grouped");
            Console.WriteLine($"  🗂  {layout.PageCount} pages -> {index}");
            Console.WriteLine();
        }

        /// <summary>A three-column table whose first column is sized to its content.</summary>
        static Report FittedTable(double fitMin, double fitMax)
        {
            var report = new Report { Dimension = new() { Width = 210, Height = 297 }, Body = new() };
            report.Section.Style.Padding = new() { Left = 9, Right = 9 };

            var table = new TableItem();
            table.AddFittedColumn(fitMin, fitMax);
            table.AddColumn(30);
            table.AddAutoColumn();

            foreach (string first in new[] { "short", "a much longer value", "mid" })
            {
                table.AddRow(
                    new TextItem { Text = first, Font = new() { Family = DmsFont, Size = 3, SizeUnit = FontSizeUnit.Millimeter } },
                    new TextItem { Text = "fixed", Font = new() { Family = DmsFont } },
                    new TextItem { Text = "rest", Font = new() { Family = DmsFont } });
            }

            report.Body!.Add(table);
            return report;
        }

        static double[] ColumnWidths(Report report, LayoutResult layout)
        {
            TableItem table = LinesTable(report);
            var row = (TableRow)table.Childs[0];
            return row.Childs
                      .Select(c => layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, c)))
                      .Select(p => p.Frame.Width)
                      .ToArray();
        }

        static double[] ColumnWidths(Report report, ITextMeasurer measurer)
            => ColumnWidths(report, report.Resolve(measurer));

        /// <summary>Families holding VAT rates holding lines: two levels of grouping.</summary>
        static Report NestedGroups()
        {
            var report = new Report { Dimension = new() { Width = 210, Height = 297 }, Body = new() };

            var byFamily = new ContainerItem
            {
                Layout = LayoutMode.Stack, RepeatOver = "Lines", GroupBy = "Family", GroupItems = "Items"
            };
            byFamily.Add(new TextItem { Text = "Family {Key}", Font = new() { Family = DmsFont } });

            var byRate = new ContainerItem
            {
                Layout = LayoutMode.Stack, RepeatOver = "Items", GroupBy = "Vat", GroupItems = "Rows"
            };
            // Reaches its own key, the family around it, and the section beyond.
            byRate.Add(new TextItem { Text = "VAT {Key} of {Family} in {Currency}", Font = new() { Family = DmsFont } });

            var lines = new TableItem { RepeatOver = "Rows" };
            lines.AddAutoColumn();
            lines.AddRow(new TextItem { Text = "{Ref}", Font = new() { Family = DmsFont } });
            byRate.Add(lines);

            byRate.Add(new TextItem { Text = "Subtotal {Sum.Total:0}", Font = new() { Family = DmsFont } });
            byFamily.Add(byRate);
            byFamily.Add(new TextItem { Text = "Subtotal {Sum.Total:0}", Font = new() { Family = DmsFont } });

            report.Body!.Add(byFamily);
            return report;
        }

        [Fact]
        public void Test28_FittedColumnsAndNestedGroups()
        {
            var testName = "Test 28: Fitted Columns and Nested Groups";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            var measurer = new FontTextMeasurer(library);

            // --- a column as wide as its widest cell ---------------------------
            Report report = FittedTable(fitMin: 0, fitMax: 0);
            LayoutResult layout = report.Resolve(measurer);
            double[] widths = ColumnWidths(report, layout);

            // Measured with the font the layout actually resolved, not with a style whose
            // size is still in millimetres - WidthOf takes points.
            TextStyle placed = layout.Pages[0].Items
                .First(p => p.Source is TextItem t && t.Text == "a much longer value").Font!;
            double widest = measurer.WidthOf("a much longer value", placed);

            AssertEqual(testName, "the fitted column took its widest cell", widest, widths[0], 0.05);
            AssertEqual(testName, "the fixed column is untouched", 30, widths[1], 0.01);
            AssertEqual(testName, "the auto column swallowed the rest", 192 - widths[0] - 30, widths[2], 0.05);
            AssertEqual(testName, "the row still spans the table", 192,
                widths[0] + widths[1] + widths[2], 0.01);

            // Bounds hold it in check either way.
            double[] floored = ColumnWidths(FittedTable(fitMin: 60, fitMax: 0), measurer);
            AssertEqual(testName, "a floor widens it", 60, floored[0], 0.01);

            double[] capped = ColumnWidths(FittedTable(fitMin: 0, fitMax: 12), measurer);
            AssertEqual(testName, "a ceiling reins it in", 12, capped[0], 0.01);

            // Asked for more than there is, the fitted columns share what there is.
            double[] squeezed = ColumnWidths(FittedTable(fitMin: 400, fitMax: 0), measurer);
            AssertEqual(testName, "an impossible demand is scaled back", 192 - 30, squeezed[0], 0.01);
            AssertEqual(testName, "and the table still ends where it should", 192,
                squeezed.Sum(), 0.01);

            // --- a group inside a group -----------------------------------------
            const string data = """
            {
              "Currency": "EUR",
              "Lines": [
                { "Family": "Parts",       "Vat": "20", "Ref": "P-1", "Total": 10 },
                { "Family": "Parts",       "Vat": "20", "Ref": "P-2", "Total": 20 },
                { "Family": "Parts",       "Vat": "5",  "Ref": "P-3", "Total": 30 },
                { "Family": "Labour","Vat": "20", "Ref": "M-1", "Total": 40 }
              ]
            }
            """;

            Report nested = ReportBinder.Bind(NestedGroups(), data);

            // Two families, and inside the first two VAT rates.
            var families = nested.AllItems.OfType<TextItem>()
                                 .Where(t => t.Text.StartsWith("Family ")).Select(t => t.Text).ToList();
            AssertEqual(testName, "the outer grouping", 2, families.Count, 0);
            AssertEqual(testName, "outer keys", "Family Parts | Family Labour",
                string.Join(" | ", families), 0);

            var rates = nested.AllItems.OfType<TextItem>()
                              .Where(t => t.Text.StartsWith("VAT ")).Select(t => t.Text).ToList();
            AssertEqual(testName, "the inner grouping", 3, rates.Count, 0);

            // The inner heading reaches its own key, the family around it, and the
            // section beyond that - all three levels of the chain at once.
            AssertEqual(testName, "the scope chains outwards",
                "VAT 20 of Parts in EUR", rates[0], 0);

            // Subtotals are computed at each level.
            var sums = nested.AllItems.OfType<TextItem>()
                             .Where(t => t.Text.StartsWith("Subtotal")).Select(t => t.Text).ToList();
            AssertEqual(testName, "a subtotal per inner group", true, sums.Contains("Subtotal 30"), 0);
            AssertEqual(testName, "and one for the family", true, sums.Contains("Subtotal 60"), 0);

            AssertEqual(testName, "every line landed once", 4,
                nested.AllItems.OfType<TextItem>().Count(t => t.Text.StartsWith("P-") || t.Text.StartsWith("M-")), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test48_ImageGrid()
        {
            var testName = "Test 48: A contact sheet of images, spanning pages";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // A minimal, valid PNG - a single red pixel - standing in for a real photo.
            // What matters here is the arrangement, not what is inside the picture.
            const string pixel =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";

            Report Sheet()
            {
                // Titled in the language it declares. It used to say "Planche de photos"
                // on a document announcing en-GB, which a reader would have read aloud in
                // the wrong voice - the same mismatch the barcode sample carried.
                var report = new Report
                {
                    Title = "Contact sheet",
                    Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };

                Demonstrates(report.Body!,
                    "Contact sheet",
                    "One picture is described in the template and repeated for every record "
                    + "in the data. Look for whole rows moving to the next page rather than "
                    + "a row being sliced across the break, for each picture keeping its "
                    + "proportions inside its cell, and for the records carrying no "
                    + "photograph leaving no empty frame behind.");

                var grid = new ImageGridItem { GridColumns = 3, RowHeight = 40, Gap = 4, RepeatOver = "Photos" };

                // Alternative text, because a picture is a figure: PDF/UA refuses one
                // without it, and the engine's own validator says so. Missing here, in a
                // sample whose whole subject is pictures.
                grid.Add(new ImageItem
                {
                    SourceField = "Photo",
                    NaturalWidth = 40,
                    NaturalHeight = 30,
                    AltText = "Photograph of site {Ref}"
                });
                report.Body!.Add(grid);

                report.Reattach();
                return report;
            }

            // Zero happens to be int/double's own default, which is exactly the value a
            // naive [JsonIgnore(WhenWritingDefault)] drops from the file - read back, it
            // would silently become 3 and 4 again rather than staying the broken 0 the
            // template actually asked for. Caught nowhere else: the validator checks below
            // read the in-memory object directly, never through a file.
            var zeroedReport = new Report { Body = new() { Layout = LayoutMode.Stack } };
            zeroedReport.Body!.Add(new ImageGridItem { GridColumns = 0, Gap = 0 });
            var zeroedBack = (ImageGridItem)Report.FromJson(zeroedReport.ToJson()).Body!.Childs[0];
            AssertEqual(testName, "a zero column count is not silently the default",
                0, zeroedBack.GridColumns, 0);
            AssertEqual(testName, "nor is a zero gap", 0.0, zeroedBack.Gap, 0.001);

            // The template is round tripped on the way to every section, so the three
            // settings have to survive that before anything else is worth measuring.
            Report reloaded = Report.FromJson(Sheet().ToJson());
            var reloadedGrid = reloaded.Body!.Childs.OfType<ImageGridItem>().First();
            AssertEqual(testName, "the column count survives the file", 3, reloadedGrid.GridColumns, 0);
            AssertEqual(testName, "and the row height", 40.0, reloadedGrid.RowHeight, 0.001);
            AssertEqual(testName, "and the gap", 4.0, reloadedGrid.Gap, 0.001);

            // Sixty records - twenty rows of three - is far more than one page of 267 mm
            // of body at 44 mm a row (40 tall, 4 of gap) can hold, so this is a genuine
            // multi-page contact sheet rather than a single row of thumbnails.
            var photos = string.Join(",\n", Enumerable.Range(1, 60)
                .Select(i => i % 17 == 0 ? $$"""{ "Ref": {{i}} }""" // one in seventeen unsigned
                                          : $$"""{ "Ref": {{i}}, "Photo": "{{pixel}}" }"""));
            string data = $$"""{ "Photos": [ {{photos}} ] }""";

            Report bound = ReportBinder.Bind(Sheet(), data);
            var backend = new SvgBackend(new FontTextMeasurer(library));
            LayoutResult layout = ReportRenderer.Render(bound, backend);

            AssertEqual(testName, "more than one page was needed", true, layout.PageCount > 1, 0);

            int expectedShown = Enumerable.Range(1, 60).Count(i => i % 17 != 0);
            int actuallyPlaced = layout.Pages.SelectMany(p => p.Items).Count(i => i.Source is ImageItem);
            Console.WriteLine($"    {expectedShown} signed of 60, {actuallyPlaced} placed, "
                              + $"{layout.PageCount} pages");
            AssertEqual(testName, "every signed photo is placed, and only those", expectedShown, actuallyPlaced, 0);

            // Three columns, wherever a row lands - not two, not spread across the page
            // at the images' own widths.
            var leftsPage1 = layout.Pages[0].Items
                .Where(i => i.Source is ImageItem)
                .Select(i => Math.Round(i.Frame.Left, 1))
                .Distinct().OrderBy(x => x).ToList();
            AssertEqual(testName, "three columns, and only three", 3, leftsPage1.Count, 0);
            AssertEqual(testName, "the first column at the left margin", 15.0, leftsPage1[0], 0.01);

            // A row is never cut in half: whatever lands on the first page is whole rows,
            // never a partial one left dangling at the foot.
            int onPage1 = layout.Pages[0].Items.Count(i => i.Source is ImageItem);
            Console.WriteLine($"    {onPage1} images on the first page");
            AssertEqual(testName, "a whole number of rows fits on the page", 0, onPage1 % 3, 0);

            // The unsigned one in seventeen is gone rather than an empty frame - the same
            // reading BindImage already gives a lone signature field.
            AssertEqual(testName, "an unsigned photo takes no place at all", true,
                layout.Pages.SelectMany(p => p.Items).Count(i => i.Source is ImageItem) < 60, 0);

            // --- the validator ------------------------------------------------------
            var brokenColumns = Sheet();
            brokenColumns.Body!.Childs.OfType<ImageGridItem>().First().RepeatOver = null;
            brokenColumns.Body!.Childs.OfType<ImageGridItem>().First().GridColumns = 0;
            var brokenIssues = TemplateValidator.Check(brokenColumns, TemplateProfile.Sound);
            AssertEqual(testName, "fewer than one column is refused", true,
                brokenIssues.Any(i => i.Rule == "sound.emptyImageGrid"), 0);

            var sound = Sheet();
            sound.Body!.Childs.OfType<ImageGridItem>().First().RepeatOver = null;
            var soundIssues = TemplateValidator.Check(sound, TemplateProfile.Sound);
            AssertEqual(testName, "a well-formed grid raises nothing", false,
                soundIssues.Any(i => i.Rule == "sound.emptyImageGrid"), 0);

            // ContainerItem.ColumnCount means something for an ordinary stacking
            // container, but FlowImageGrid never looks at it - inherited, and silently
            // inert, exactly the shape sound.columnsIgnored exists to catch.
            var columnedGrid = Sheet();
            columnedGrid.Body!.Childs.OfType<ImageGridItem>().First().RepeatOver = null;
            columnedGrid.Body!.Childs.OfType<ImageGridItem>().First().ColumnCount = 2;
            var columnedIssues = TemplateValidator.Check(columnedGrid, TemplateProfile.Sound);
            AssertEqual(testName, "columns asked of an image grid are refused too", true,
                columnedIssues.Any(i => i.Rule == "sound.columnsIgnored"), 0);

            string dir = OutputDirectory("gallery");
            backend.SaveTo(dir, "grid");

            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            bound.GeneratedAt = SampleDate;
            ReportRenderer.Render(bound, pdf);
            WriteSamplePdf(Path.Combine(dir, "grid.pdf"), pdf.ToArray());

            Console.WriteLine($"  ▤ {Path.Combine(dir, "grid.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// A sub-template that brings a document rather than a block.
        ///
        /// Splice takes the other template's Body and drops its header, footer, page
        /// size and margins - the only honest answer when a report had one document,
        /// because there was nowhere to put them. With a run of sections there is:
        /// Append takes the piece whole, and a dossier of self-contained reports becomes
        /// expressible without either of them being rewritten to suit the other.
        ///
        /// Both are checked here, and the splice half is not decoration: a test that
        /// only exercised the new mode would pass on an engine where the old one had
        /// stopped working.
        ///
        /// What the file says about itself is the question assembly raises and
        /// composition never did. Title, author, subject and keywords are the
        /// information dictionary, of which a PDF has one - so the pieces' are dropped.
        /// The language is not like them: PDF lets an element carry its own, so a piece
        /// that declares one keeps it, and a German appendix in an English dossier is
        /// announced in German instead of being read aloud with English phonemes.
        /// </summary>
        [Fact]
        public void Test88_AppendBringsTheWholeDocument()
        {
            const string testName = "Test 88";
            Console.WriteLine("Test 88: a sub-template that brings a document");
            Console.WriteLine();

            // A piece that is a report in its own right: its own sheet, its own footer,
            // its own title and its own language. Splice keeps none of that.
            static string Piece(string lang)
            {
                var report = new Report
                {
                    Title = "The piece's own title",
                    Author = "Somebody else",
                    Language = lang,
                    Body = new() { Layout = LayoutMode.Stack },
                    Footer = new()
                };

                // Landscape, so the sheet it keeps is visibly not the host's.
                report.Section.Dimension = new() { Width = 297, Height = 210 };
                report.Body!.Add(new TextItem { Text = "the appendix" });
                report.Footer!.Add(new TextItem { Text = "appendix footer" });
                report.Reattach();
                return report.ToJson();
            }

            static Report Host(SubTemplateMode mode, string lang, bool trailing)
            {
                var report = new Report
                {
                    Title = "The dossier", Language = "en-GB",
                    Body = new() { Layout = LayoutMode.Stack }
                };
                report.Section.Dimension = new() { Width = 210, Height = 297 };
                report.Body!.Add(new TextItem { Text = "the covering note" });
                report.Body!.Add(new SubTemplateItem
                {
                    Mode = mode,
                    TemplateData = System.Text.Encoding.UTF8.GetBytes(Piece(lang))
                });
                if (trailing)
                    report.Body!.Add(new TextItem { Text = "after the include" });
                report.Reattach();
                return report;
            }

            // --- splice: the block, and nothing of the document around it ------------
            Report spliced = Host(SubTemplateMode.Splice, "de-DE", trailing: false);
            spliced.ExpandSubTemplates();

            Console.WriteLine($"    splice : {spliced.Sections.Count} section(s), "
                            + $"{spliced.Section.Dimension.Width} mm de large");

            AssertEqual(testName, "splicing adds no section", 1, spliced.Sections.Count, 0);
            AssertEqual(testName, "and the host's sheet is untouched",
                210, spliced.Section.Dimension.Width, 0.01);
            AssertEqual(testName, "and the block is in the host's flow", true,
                spliced.AllItems.OfType<TextItem>().Any(t => t.Text == "the appendix"), 0);

            // --- append: the document, whole -----------------------------------------
            Report joined = Host(SubTemplateMode.Append, "de-DE", trailing: false);
            joined.ExpandSubTemplates();

            Console.WriteLine($"    append : {joined.Sections.Count} section(s), "
                            + string.Join(" puis ", joined.Sections.Select(
                                d => $"{d.Dimension.Width}x{d.Dimension.Height}")));

            AssertEqual(testName, "appending adds the piece's sections", 2, joined.Sections.Count, 0);
            AssertEqual(testName, "the host's sheet is still its own",
                210, joined.Sections[0].Dimension.Width, 0.01);

            // The thing splice cannot do, and the reason this mode exists.
            AssertEqual(testName, "and the piece keeps the sheet it was drawn for",
                297, joined.Sections[1].Dimension.Width, 0.01);
            AssertEqual(testName, "and the footer it was drawn with", true,
                joined.Sections[1].Childs.OfType<Footer>().Any(), 0);

            // The item is a container; left in place it would lay out as a band of no
            // height and be tagged as a section standing for a document that is now
            // somewhere else entirely.
            AssertEqual(testName, "and the include itself leaves nothing behind",
                0, joined.AllItems.OfType<SubTemplateItem>().Count(), 0);

            // --- what a file has one of ----------------------------------------------
            AssertEqual(testName, "the dossier keeps its own title", "The dossier", joined.Title ?? "", 0);
            AssertEqual(testName, "and the piece's author does not cross",
                true, string.IsNullOrEmpty(joined.Author), 0);

            // --- and the one a file may have several of -------------------------------
            AssertEqual(testName, "the file's language is the host's", "en-GB", joined.Language ?? "", 0);
            AssertEqual(testName, "and the piece says its own on the section it became",
                "de-DE", joined.Sections[1].Language ?? "(none)", 0);

            // Saying nothing where there is nothing to say: a piece in the host's own
            // language must not put a /Lang on its section, or the entries that mean
            // something are lost among entries that do not.
            Report same = Host(SubTemplateMode.Append, "en-GB", trailing: false);
            same.ExpandSubTemplates();
            Console.WriteLine($"    meme langue : {same.Sections[1].Language ?? "(rien)"}");

            // It is carried at the model level either way - the section really is in
            // en-GB - and the backend is what declines to write a /Lang that repeats the
            // file's. Both halves are checked, the second below.
            AssertEqual(testName, "the piece in the host's language still records it",
                "en-GB", same.Sections[1].Language ?? "(none)", 0);

            // Report.Descendants is private and AllItems flattens every section into
            // one list, which is exactly the distinction under test here.
            static IEnumerable<TextItem> Texts(ContainerItem container)
            {
                foreach (BaseItem child in container.Childs)
                {
                    if (child is TextItem text) yield return text;
                    if (child is ContainerItem inner)
                        foreach (TextItem deep in Texts(inner)) yield return deep;
                }
            }

            // --- and what reaches the file --------------------------------------------
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            string Written(Report report)
            {
                var pdf = new PdfBackend(library)
                {
                    Conformance = PdfConformance.PdfA3b, Tagged = true, Compress = false
                };
                report.GeneratedAt = SampleDate;
                ReportRenderer.Render(report, pdf);
                return System.Text.Encoding.Latin1.GetString(Unpacked(pdf.ToArray()));
            }

            string mixed = Written(joined);
            string uniform = Written(same);

            // Kept, because a document in two languages is exactly the kind of file a
            // validator has an opinion about and this side does not. Test 53 validates
            // the conformance document; this one is here to be pointed at by hand.
            WriteSamplePdf(Path.Combine(OutputDirectory("subtemplate"), "appended.pdf"),
                               System.Text.Encoding.Latin1.GetBytes(mixed));

            Console.WriteLine($"    /Lang (de-DE) : {CountOccurrences(mixed, "/Lang (de-DE)")} melange, "
                            + $"{CountOccurrences(uniform, "/Lang (de-DE)")} uniforme");

            AssertEqual(testName, "the file declares the host's language", true,
                mixed.Contains("/Lang (en-GB)"), 0);
            AssertEqual(testName, "and the appended section declares its own", true,
                mixed.Contains("/Lang (de-DE)"), 0);

            // The half that is easy to leave out: a /Lang repeating the file's says
            // nothing and hides the ones that do. The whole point of marking a passage
            // is that it is marked and its neighbours are not.
            AssertEqual(testName, "and a piece in the file's own language marks nothing",
                1, CountOccurrences(uniform, "/Lang ("), 0);

            // --- where the item sits, and what that does and does not decide ----------
            // A document is not a flow item, so the position decides only which section
            // the piece follows. Anything declared after the include stays in the host's
            // section - and therefore comes out BEFORE the piece, which is worth a rule
            // rather than a surprise (sound.appendNotLast).
            Report trailing = Host(SubTemplateMode.Append, "de-DE", trailing: true);
            trailing.ExpandSubTemplates();

            Console.WriteLine("    apres l'include : "
                + string.Join(" | ", trailing.Sections.Select(d =>
                    string.Join(",", Texts(d).Select(t => t.Text)))));

            AssertEqual(testName, "what follows the include stays in the host's section",
                true, Texts(trailing.Sections[0])
                              .Any(t => t.Text == "after the include"), 0);
            AssertEqual(testName, "and the piece is still a section of its own",
                2, trailing.Sections.Count, 0);

            // --- a sub-template in a section that is not the first --------------------
            // Expansion read Report.Section - the facade over Sections[0] - so an include
            // anywhere else was never expanded: no error, no splice, an empty container
            // where a block was meant to be. Invisible while a report had one section,
            // and this mode makes several of them ordinary.
            var second = new Report
            {
                Title = "Two sections", Language = "en-GB",
                Body = new() { Layout = LayoutMode.Stack }
            };
            second.Section.Dimension = new() { Width = 210, Height = 297 };
            second.Body!.Add(new TextItem { Text = "the first section" });

            var later = new Section { Dimension = new() { Width = 210, Height = 297 } };
            var laterBody = new Body { Layout = LayoutMode.Stack };
            laterBody.Add(new SubTemplateItem
            {
                Mode = SubTemplateMode.Splice,
                TemplateData = System.Text.Encoding.UTF8.GetBytes(Piece("en-GB"))
            });
            later.Add(laterBody);
            second.Sections.Add(later);
            second.Reattach();

            second.ExpandSubTemplates();

            Console.WriteLine("    deuxieme section : "
                + string.Join(",", Texts(second.Sections[1])
                                         .Select(t => t.Text)));

            AssertEqual(testName, "an include in the second section is expanded too",
                true, Texts(second.Sections[1])
                            .Any(t => t.Text == "the appendix"), 0);

            // --- and the shape a dossier actually uses: files on disk ------------------
            // Carried bytes prove the mode; a path proves the path. It is a different
            // door in ExpandOne - the only one that touches a disk, and therefore the
            // only one held to a root - so a mode that worked on bytes and not on files
            // would be a mode nobody could use.
            string dir = OutputDirectory("subtemplate");
            string pieceFile = Path.Combine(dir, "appended-piece.json");
            string hostFile = Path.Combine(dir, "appended-host.json");

            File.WriteAllText(pieceFile, Piece("de-DE"));

            var onDisk = new Report
            {
                Title = "The dossier", Language = "en-GB",
                Body = new() { Layout = LayoutMode.Stack }
            };
            onDisk.Section.Dimension = new() { Width = 210, Height = 297 };
            onDisk.Body!.Add(new TextItem { Text = "the covering note" });
            onDisk.Body!.Add(new SubTemplateItem
            {
                Mode = SubTemplateMode.Append,
                TemplatePath = "appended-piece.json"
            });
            onDisk.Reattach();
            File.WriteAllText(hostFile, onDisk.ToJson());

            // FromFile is what the CLI and the native library both call, and what
            // resolves a relative path against the file that named it.
            Report loaded = Report.FromFile(hostFile);

            Console.WriteLine($"    depuis un fichier : {loaded.Sections.Count} section(s), "
                            + $"la seconde en {loaded.Sections.ElementAtOrDefault(1)?.Language ?? "(rien)"}");

            AssertEqual(testName, "a piece named by path is appended whole",
                2, loaded.Sections.Count, 0);
            AssertEqual(testName, "with the sheet it was drawn for",
                297, loaded.Sections[1].Dimension.Width, 0.01);
            AssertEqual(testName, "and the language it was written in",
                "de-DE", loaded.Sections[1].Language ?? "(none)", 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test49_SubTemplate()
        {
            var testName = "Test 49: Composing a section out of sub-templates";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            string dir = OutputDirectory("subtemplate");

            // The included file. Deliberately carrying a page size and a page-sized
            // margin of its own, neither of which the host may honour - only the body's
            // children are ever taken.
            Report Included(int paragraphs)
            {
                var r = new Report
                {
                    Title = "Fragment, never opened on its own",
                    Dimension = new() { Width = 999, Height = 999 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                r.Section.Style.Padding = new() { Left = 50, Top = 50, Right = 50, Bottom = 50 };
                for (int i = 1; i <= paragraphs; i++)
                    r.Body!.Add(new TextItem { Text = $"Paragraphe {i} du sous-gabarit." });
                r.Reattach();
                return r;
            }

            // The including file: one paragraph, the sub-template, one more paragraph -
            // so whatever happens to the middle one is visible from what surrounds it.
            Report Host()
            {
                var r = new Report
                {
                    Title = "Composed section",
                    Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                r.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };
                r.Body!.Add(new TextItem { Text = "Avant l'inclusion." });
                r.Body!.Add(new SubTemplateItem { TemplatePath = "included.json" });
                r.Body!.Add(new TextItem { Text = "After the inclusion." });
                r.Reattach();
                return r;
            }

            File.WriteAllText(Path.Combine(dir, "included.json"), Included(150).ToJson());
            File.WriteAllText(Path.Combine(dir, "host.json"), Host().ToJson());

            Report loaded = Report.FromFile(Path.Combine(dir, "host.json"));

            // --- the splice itself ---------------------------------------------------
            AssertEqual(testName, "the sub-template stays exactly where it was put",
                3, loaded.Body!.Childs.Count, 0);

            var sub = (SubTemplateItem)loaded.Body!.Childs[1];
            AssertEqual(testName, "and its children are the included body's",
                150, sub.Childs.Count, 0);
            AssertEqual(testName, "the first paragraph read correctly",
                "Paragraphe 1 du sous-gabarit.", ((TextItem)sub.Childs[0]).Text, 0);

            // --- no margin of its own --------------------------------------------------
            AssertEqual(testName, "the included report's own padding is not carried over",
                0.0, sub.Style.Padding.Left, 0.001);
            AssertEqual(testName, "the host's own page size is untouched",
                210.0, loaded.Dimension.Width, 0.001);

            // --- continuation flows into the parent's own pagination -------------------
            var backend = new SvgBackend(new FontTextMeasurer(library));
            LayoutResult layout = ReportRenderer.Render(loaded, backend);

            AssertEqual(testName, "a hundred and fifty paragraphs need more than one page",
                true, layout.PageCount > 1, 0);

            bool Has(int page, string text) => layout.Pages[page].Items
                .Any(i => i.Source is TextItem t && (i.Fragment ?? t.Text) == text);

            AssertEqual(testName, "the paragraph before the include starts the first page",
                true, Has(0, "Avant l'inclusion."), 0);
            AssertEqual(testName, "and the one after lands on the last page, past the overflow",
                true, Has(layout.PageCount - 1, "After the inclusion."), 0);

            int paragraphsSeen = layout.Pages.SelectMany(p => p.Items)
                .Count(i => i.Source is TextItem t && t.Text.StartsWith("Paragraphe ", StringComparison.Ordinal));
            Console.WriteLine($"    {layout.PageCount} pages, {paragraphsSeen} paragraphes du sous-gabarit retrouvés");
            AssertEqual(testName, "every included paragraph is somewhere in the section",
                150, paragraphsSeen, 0);

            // --- inserted conditionally, like any other element -----------------------
            Report ConditionalHost()
            {
                var r = new Report { Dimension = new() { Width = 210, Height = 297 },
                                     Body = new() { Layout = LayoutMode.Stack } };
                r.Body!.Add(new SubTemplateItem { TemplatePath = "included.json", ShowWhen = "Client.Vat" });
                r.Reattach();
                return r;
            }

            File.WriteAllText(Path.Combine(dir, "conditional.json"), ConditionalHost().ToJson());
            Report conditionalTemplate = Report.FromFile(Path.Combine(dir, "conditional.json"));

            Report shown = ReportBinder.Bind(conditionalTemplate, """{ "Client": { "Vat": "FR123" } }""");
            Report hidden = ReportBinder.Bind(conditionalTemplate, """{ "Client": { } }""");

            AssertEqual(testName, "shown when the condition holds",
                Visibility.Visible, shown.Body!.Childs[0].Style.Visibility, 0);
            AssertEqual(testName, "gone when it does not - like any other element",
                Visibility.Gone, hidden.Body!.Childs[0].Style.Visibility, 0);

            // --- a cycle is refused, not looped forever --------------------------------
            string cycleDir = OutputDirectory("subtemplate", "cycle");
            Report PointingAt(string path)
            {
                var r = new Report { Dimension = new() { Width = 210, Height = 297 },
                                     Body = new() { Layout = LayoutMode.Stack } };
                r.Body!.Add(new SubTemplateItem { TemplatePath = path });
                r.Reattach();
                return r;
            }

            File.WriteAllText(Path.Combine(cycleDir, "a.json"), PointingAt("b.json").ToJson());
            File.WriteAllText(Path.Combine(cycleDir, "b.json"), PointingAt("a.json").ToJson());
            File.WriteAllText(Path.Combine(cycleDir, "self.json"), PointingAt("self.json").ToJson());
            File.WriteAllText(Path.Combine(cycleDir, "missing-ref.json"), PointingAt("nowhere.json").ToJson());

            bool ThrowsLoading(string file)
            {
                try { Report.FromFile(Path.Combine(cycleDir, file)); return false; }
                catch { return true; }
            }

            AssertEqual(testName, "two files including each other are refused",
                true, ThrowsLoading("a.json"), 0);
            AssertEqual(testName, "a file including itself is refused",
                true, ThrowsLoading("self.json"), 0);
            AssertEqual(testName, "a sub-template that names no real file is refused clearly",
                true, ThrowsLoading("missing-ref.json"), 0);

            // --- the validator ----------------------------------------------------------
            var blank = new Report { Dimension = new() { Width = 210, Height = 297 },
                                     Body = new() { Layout = LayoutMode.Stack } };
            blank.Body!.Add(new SubTemplateItem());
            blank.Reattach();
            var blankIssues = TemplateValidator.Check(blank, TemplateProfile.Sound);
            AssertEqual(testName, "a sub-template naming no file is refused by the validator",
                true, blankIssues.Any(i => i.Rule == "sound.emptySubTemplate"), 0);

            var named = new Report { Dimension = new() { Width = 210, Height = 297 },
                                     Body = new() { Layout = LayoutMode.Stack } };
            named.Body!.Add(new SubTemplateItem { TemplatePath = "included.json" });
            named.Reattach();
            var namedIssues = TemplateValidator.Check(named, TemplateProfile.Sound);
            AssertEqual(testName, "one naming a file raises nothing",
                false, namedIssues.Any(i => i.Rule == "sound.emptySubTemplate"), 0);

            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            loaded.GeneratedAt = SampleDate;
            ReportRenderer.Render(loaded, pdf);
            WriteSamplePdf(Path.Combine(dir, "compose.pdf"), pdf.ToArray());

            Console.WriteLine($"  ▤ {Path.Combine(dir, "compose.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// A template names the files the engine reads, so a template it was handed
        /// rather than wrote cannot be allowed to name any of them.
        ///
        /// Path.Combine discards its left side when the right is rooted, so an absolute
        /// TemplatePath made baseDirectory vanish; and ".." climbed out of the directory
        /// besides. Either way the file was read, its body composed into the document,
        /// and - when it was not JSON - its first byte quoted back in the parse error.
        ///
        /// The control matters more here than anywhere else in this suite. Written the
        /// first time without one, every traversal came back "nothing spliced" and it
        /// read as safety; the control was failing too, for an unrelated reason, and the
        /// finding was nearly written off. A refusal is only evidence when the thing it
        /// refuses is known to work.
        /// </summary>
        /// <summary>
        /// A sub-template with no file behind it: carried inside the template, or named
        /// for a caller to find.
        ///
        /// Both exist because a file is exactly what a server and an editor do not have.
        /// CLAUDE.md section 14 records the consequence - Report.FromFile was wired into
        /// the CLI and the native boundary and "deliberately not into apps/server nor the
        /// designer's local preview: both receive only JSON as text, with no file of
        /// their own to resolve anything relative against". That was true of the type as
        /// it stood; it stopped being true when the type could be told another way.
        /// </summary>
        [Fact]
        public void Test76_SubTemplatesWithoutAFile()
        {
            const string testName = "Test 76";
            Console.WriteLine("Test 76: a sub-template carried, or named for a caller to find");
            Console.WriteLine();

            const string included = """
            {
              "Sections": [ { "$type": "Section", "Childs": [ { "$type": "Body", "Childs": [
                { "$type": "TextItem", "Text": "spliced in" }
              ] } ] } ]
            }
            """;

            static Report Holder(Action<SubTemplateItem> say)
            {
                var report = new Report
                {
                    Title = "Composed", Language = "en-GB",
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack }
                };
                var slot = new SubTemplateItem();
                say(slot);
                report.Body!.Add(slot);
                report.Reattach();
                return report;
            }

            static List<string> TextIn(Report report) =>
                report.AllItems.OfType<TextItem>().Select(t => t.Text ?? "").ToList();

            // --- carried ---------------------------------------------------------
            // Bytes, so base64 in the file, the same shape as an embedded image. The
            // whole template is then one file and can be handed to something with no
            // file system at all.
            Report carried = Holder(slot =>
                slot.TemplateData = System.Text.Encoding.UTF8.GetBytes(included));

            AssertEqual(testName, "nothing is spliced until it is asked for",
                false, TextIn(carried).Contains("spliced in"), 0);

            carried.ExpandSubTemplates();
            Console.WriteLine($"    porte : {string.Join(", ", TextIn(carried))}");
            AssertEqual(testName, "carried bytes need no file and no source",
                true, TextIn(carried).Contains("spliced in"), 0);

            // And it survives being written down, which is the point of carrying it.
            Report reloaded = Report.FromJson(Holder(slot =>
                slot.TemplateData = System.Text.Encoding.UTF8.GetBytes(included)).ToJson());
            reloaded.ExpandSubTemplates();
            AssertEqual(testName, "and survives the round trip",
                true, TextIn(reloaded).Contains("spliced in"), 0);

            // --- named -----------------------------------------------------------
            var shelf = new Shelf(("letterhead", included));

            Report named = Holder(slot => slot.TemplatePath = "st://letterhead");
            named.ExpandSubTemplates(shelf);
            Console.WriteLine($"    nomme : {string.Join(", ", TextIn(named))}, "
                              + $"demande {shelf.Asked.Count} fois : {string.Join("/", shelf.Asked)}");

            AssertEqual(testName, "a reference is served by the caller",
                true, TextIn(named).Contains("spliced in"), 0);
            AssertEqual(testName, "which is asked for it by name, without the scheme",
                "letterhead", shelf.Asked.SingleOrDefault() ?? "(not asked)", 0);

            // --- and the two refusals --------------------------------------------
            // A reference with nobody to serve it is refused, never quietly turned into
            // a relative path: Path.Combine would make "st://x" into something a file
            // system interprets, and a reference becoming a path is section 8b's fault
            // in a new door.
            string lonely = Refusal(() => Holder(slot => slot.TemplatePath = "st://letterhead")
                .ExpandSubTemplates());
            Console.WriteLine($"    sans source : {lonely}");
            AssertEqual(testName, "a reference with no source is refused",
                true, lonely.Contains("no source was given"), 0);

            // A path with no file behind the report is refused for the same reason and
            // it is the sharper of the two: Path.Combine("", x) is x, so GetFullPath
            // would measure it from the working directory. Written, nearly shipped, and
            // caught by reading the doc comment back against the code.
            string rooted = Refusal(() => Holder(slot => slot.TemplatePath = "../../secrets.json")
                .ExpandSubTemplates());
            Console.WriteLine($"    chemin sans fichier : {rooted}");
            AssertEqual(testName, "and so is a path, when there is no directory to use",
                true, rooted.Contains("no directory to resolve it against"), 0);

            // The message names what the author wrote and nothing about this machine -
            // the rule the file branch already follows.
            AssertEqual(testName, "and neither message describes the machine",
                false, rooted.Contains(RepositoryRoot()) || lonely.Contains(RepositoryRoot()), 0);

            // --- a reference nobody has ------------------------------------------
            string missing = Refusal(() => Holder(slot => slot.TemplatePath = "st://nothing")
                .ExpandSubTemplates(new Shelf()));
            AssertEqual(testName, "a reference the source does not have is refused too",
                true, missing.Contains("not found"), 0);

            // --- the validator counts all three ----------------------------------
            Report saysNothing = Holder(_ => { });
            AssertEqual(testName, "a slot that names nothing and carries nothing is a fault",
                true, TemplateValidator.Check(saysNothing).Any(i => i.Rule == "sound.emptySubTemplate"), 0);
            AssertEqual(testName, "and one that carries something is not",
                false, TemplateValidator.Check(Holder(slot =>
                    slot.TemplateData = System.Text.Encoding.UTF8.GetBytes(included)))
                    .Any(i => i.Rule == "sound.emptySubTemplate"), 0);

            Console.WriteLine();
        }

        /// <summary>What a call threw, or "(nothing thrown)" - which is itself a failure.</summary>
        private static string Refusal(Action act)
        {
            try
            {
                act();
                return "(nothing thrown)";
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>
        /// A source of templates by name, standing in for whatever a caller really has -
        /// a row in a database, a blob in a store. It records what it was asked, because
        /// "the reference reached the source, without its scheme" is half of what the
        /// test is about.
        /// </summary>
        private sealed class Shelf : ISubTemplates
        {
            private readonly Dictionary<string, string> _held;

            internal Shelf(params (string Name, string Json)[] held) =>
                _held = held.ToDictionary(h => h.Name, h => h.Json);

            internal List<string> Asked { get; } = new();

            public string? Find(string reference)
            {
                Asked.Add(reference);
                return _held.TryGetValue(reference, out string? json) ? json : null;
            }
        }

        [Fact]
        public void Test58_SubTemplatesStayHome()
        {
            const string testName = "Test 58";
            Console.WriteLine("Test 58: a template does not choose which files are read");
            Console.WriteLine();

            string root = OutputDirectory("traversal");
            string inside = Path.Combine(root, "safe");
            Directory.CreateDirectory(inside);
            foreach (string old in Directory.GetFiles(root)) File.Delete(old);
            foreach (string old in Directory.GetFiles(inside)) File.Delete(old);

            const string Marker = "MarkerFromOutsideTheTemplateDirectory";

            var outside = new Report { Title = "outside", Body = new Body() };
            outside.Body!.Add(new TextItem { Text = Marker });
            File.WriteAllText(Path.Combine(root, "outside.json"), outside.ToJson());
            File.WriteAllText(Path.Combine(inside, "neighbour.json"), outside.ToJson());

            // Something that is not JSON at all, to see what a parse failure gives away.
            File.WriteAllText(Path.Combine(root, "secrets.ini"), "; the first byte of a secret\n");

            string Load(string templatePath)
            {
                var main = new Report { Title = "main", Body = new Body() };
                main.Body!.Add(new SubTemplateItem { TemplatePath = templatePath, Layout = LayoutMode.Stack });

                string file = Path.Combine(inside, "main.json");
                File.WriteAllText(file, main.ToJson());

                try
                {
                    return Report.FromFile(file).ToJson().Contains(Marker) ? "spliced" : "loaded, nothing spliced";
                }
                catch (Exception e)
                {
                    return "refused: " + e.Message;
                }
            }

            // --- the control, first and always ---------------------------------------
            string control = Load("neighbour.json");
            Console.WriteLine($"    a legitimate neighbour  -> {control}");
            AssertEqual(testName, "a neighbour beside the template still works", "spliced", control, 0);

            // A sub-directory is below the root, so it is allowed too - the rule is a
            // boundary, not a flat directory.
            Directory.CreateDirectory(Path.Combine(inside, "parts"));
            File.WriteAllText(Path.Combine(inside, "parts", "deeper.json"), outside.ToJson());
            AssertEqual(testName, "and so does one in a sub-directory",
                "spliced", Load(Path.Combine("parts", "deeper.json")), 0);

            // --- and everything that leaves is refused -------------------------------
            foreach ((string what, string path) in new[]
            {
                ("one level up",        "../outside.json"),
                ("one level up, backslash", @"..\outside.json"),
                ("an absolute path",    Path.Combine(root, "outside.json")),
                ("a file that is not JSON", Path.Combine(root, "secrets.ini")),
                ("a deep climb",        @"..\..\..\..\..\..\..\..\..\..\Windows\win.ini"),
            })
            {
                string outcome = Load(path);
                Console.WriteLine($"    {what,-24} -> {outcome.Split('\n')[0]}");

                AssertEqual(testName, $"{what} is refused", true,
                    outcome.StartsWith("refused:", StringComparison.Ordinal), 0);
                AssertEqual(testName, $"{what} splices nothing", false,
                    outcome.Contains(Marker), 0);

                // The message says what the author wrote, never what it resolved to: a
                // resolved path describes the machine to whoever sent the template.
                // Asked only of a relative path - when the author wrote an absolute one,
                // repeating it back tells them nothing they did not already type.
                if (!Path.IsPathRooted(path))
                {
                    AssertEqual(testName, $"{what} does not hand back a resolved path", false,
                        outcome.Contains(root, StringComparison.OrdinalIgnoreCase), 0);
                }
            }

            // A sibling directory whose name merely starts with the root's is not below
            // it. String prefixes say it is; Path.GetRelativePath does not.
            //
            // Sibling of *inside*, which is the root here - Report.FromFile takes the
            // template's own directory as the boundary. Built beside the wrong directory
            // the first time, this passed with the boundary replaced by a string prefix,
            // and the sabotage is what said so.
            string twin = inside + "-evil";
            Directory.CreateDirectory(twin);
            File.WriteAllText(Path.Combine(twin, "outside.json"), outside.ToJson());
            string sibling = Load(Path.Combine(twin, "outside.json"));
            Console.WriteLine($"    a same-prefixed sibling  -> {sibling.Split('\n')[0]}");
            AssertEqual(testName, "a directory that only shares a prefix is still outside",
                true, sibling.StartsWith("refused:", StringComparison.Ordinal), 0);

            // --- and a chain of includes cannot run forever --------------------------
            // The cycle guard sees a file including itself. It does not see a thousand
            // distinct files in a line, which is a different way to walk the stack down.
            for (int i = 0; i < 40; i++)
            {
                var link = new Report { Title = $"link{i}", Body = new Body() };
                link.Body!.Add(new SubTemplateItem { TemplatePath = $"link{i + 1}.json", Layout = LayoutMode.Stack });
                File.WriteAllText(Path.Combine(inside, $"link{i}.json"), link.ToJson());
            }
            File.WriteAllText(Path.Combine(inside, "link40.json"), outside.ToJson());

            string chained = Load("link0.json");
            Console.WriteLine($"    a chain forty long       -> {chained.Split('\n')[0]}");
            AssertEqual(testName, "a chain too long is refused rather than followed",
                true, chained.StartsWith("refused:", StringComparison.Ordinal), 0);
            AssertEqual(testName, "and it says so in those terms", true,
                chained.Contains("nested deeper", StringComparison.Ordinal), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A master document assembled from sub-documents a host program serves by name: a
        /// legal article in the body, the recipient's address in a block placed on the right
        /// of the header, a logo taken from the data, and the footer's mentions. Every piece
        /// arrives through st://, every merge field in it is filled from the master's data,
        /// and the header and footer pieces are on every page.
        ///
        /// Before this only a sub-template in the body was tested. The expander walks the
        /// header and the footer as well, and a block placed at a coordinate is the path
        /// CLAUDE.md section 7 says faults sleep in. Written when the author described the
        /// shape wanted - an expert report whose shared pieces are written once, in the
        /// content editor, and change everywhere when changed once.
        /// </summary>
        [Fact]
        public void Test95_SubDocumentsAnywhere()
        {
            const string testName = "Test 95";
            Console.WriteLine("Test 95: sub-documents served by name, anywhere in a master");
            Console.WriteLine();

            // --- the shared pieces, shaped as the content editor writes them ---------------
            // A Body in Stack, rich text as runs, a run's own Font for the part in bold.
            static string Piece(params BaseItem[] items)
            {
                var piece = new Report { Body = new() { Layout = LayoutMode.Stack } };
                foreach (BaseItem item in items) piece.Body!.Add(item);
                piece.Reattach();
                return piece.ToJson();
            }
            static AdvancedTextItem Rich(params (string Text, bool Bold)[] runs)
            {
                var item = new AdvancedTextItem();
                foreach (var (text, bold) in runs)
                    item.Runs.Add(new TextRun { Text = text, Font = bold ? new TextStyle { Bold = true } : null });
                return item;
            }
            static string Article(string wording) => Piece(Rich(("Art. 1", true), (" - " + wording, false)));

            static ImageItem Logo()
            {
                var logo = new ImageItem { SourceField = "Logo", AltText = "Logo du cabinet" };
                logo.Style.Location.Width = (28, Units.Millimeter);
                logo.Style.Location.Height = (14, Units.Millimeter);
                return logo;
            }

            const string before = "L'expert accomplit sa mission avec conscience, objectivité et impartialité.";
            const string after = "L'expert remplit personnellement la mission qui lui est confiée.";
            Shelf Store(string wording) => new(
                ("loi/art-1", Article(wording)),
                ("destinataire", Piece(Rich(("{Destinataire.Nom}", true),
                                            ("\n{Destinataire.Rue}\n{Destinataire.Ville}", false)))),
                ("logo", Piece(Logo())),
                ("mentions", Piece(Rich(("Rapport {Dossier.Numero} - page {Page} sur {Pages}", false)))));

            // --- the master, as the template designer lays it out --------------------------
            static SubTemplateItem Slot(string reference)
                => new() { TemplatePath = "st://" + reference };
            static SubTemplateItem PlacedSlot(string reference, double left, double top, double width)
            {
                SubTemplateItem slot = Slot(reference);
                slot.Style.Location.Left = (left, Units.Millimeter);
                slot.Style.Location.Top = (top, Units.Millimeter);
                slot.Style.Location.Width = (width, Units.Millimeter);
                return slot;
            }

            Report Master()
            {
                var master = new Report
                {
                    Title = "Rapport d'expertise",
                    Language = "fr-FR",
                    GeneratedAt = SampleDate,
                    Dimension = new() { Width = 210, Height = 297 },
                    Header = new(),
                    Footer = new(),
                    Body = new() { Layout = LayoutMode.Stack }
                };
                master.Header!.Style.Location.Height = (40, Units.Millimeter);
                master.Footer!.Style.Location.Height = (12, Units.Millimeter);

                master.Header.Add(PlacedSlot("logo", 0, 0, 30));
                master.Header.Add(PlacedSlot("destinataire", 110, 4, 80));
                SubTemplateItem mentions = Slot("mentions");
                mentions.Style.Location.Dock = Dock.Left | Dock.Right;
                master.Footer.Add(mentions);

                Demonstrates(master.Body!, "Sous-documents servis par nom",
                    "L'adresse à droite, le logo, l'article en gras et le pied de page ne sont pas dans ce "
                    + "gabarit : chacun est un sous-document que le programme hôte sert par st://, rempli avec "
                    + "les données du dossier. Changer l'article une fois change tous les rapports qui le citent.");
                master.Body.Add(T("Le présent rapport répond à la mission confiée par ordonnance du 2 septembre 2026."));
                master.Body.Add(Slot("loi/art-1"));
                master.Body.Add(T("Montant des honoraires : {Honoraires}."));
                for (int i = 1; i <= 45; i++)
                    master.Body.Add(T($"Constat {i}. Les éléments examinés sur place ont été relevés, photographiés "
                                      + "et rapprochés des pièces communiquées par les parties."));
                master.Reattach();
                return master;
            }

            // --- the case's data, every value already written the way it prints ------------
            string nnbsp = ((char)0x202F).ToString(), nbsp = ((char)0x00A0).ToString();
            string fee = "1" + nnbsp + "235" + nbsp + "F" + nbsp + "CFP";
            string data = new System.Text.Json.Nodes.JsonObject
            {
                ["Dossier"] = new System.Text.Json.Nodes.JsonObject { ["Numero"] = "EXP-2026-0412" },
                ["Destinataire"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["Nom"] = "Maître Jeanne Dupont",
                    ["Rue"] = "12 rue des Lilas",
                    ["Ville"] = "98800 Nouméa"
                },
                ["Honoraires"] = fee,
                ["Logo"] = Convert.ToBase64String(
                    PngEncoder.Rgb(60, 30, (x, y) => ((byte)(30 + x), (byte)(60 + y), 140)))
            }.ToJsonString();

            // --- composed the way a host does it: expand, bind, lay out --------------------
            Shelf shelf = Store(before);
            Report template = Master();
            template.ExpandSubTemplates(shelf);
            Report bound = ReportBinder.Bind(template, data);
            var library = new FontLibrary();
            LayoutResult layout = bound.Resolve(new FontTextMeasurer(library));

            static string TextOf(PlacedItem p) => p.Fragment ?? (p.Source as TextItem)?.Text ?? "";
            Console.WriteLine($"    demandé : {string.Join(", ", shelf.Asked)} ; {layout.PageCount} pages");

            AssertEqual(testName, "every piece is asked for by name, once",
                "destinataire,logo,loi/art-1,mentions",
                string.Join(",", shelf.Asked.OrderBy(s => s, StringComparer.Ordinal)), 0);
            AssertEqual(testName, "the body runs onto a second page, so the bands repeat",
                true, layout.PageCount >= 2, 0);

            foreach (Page page in layout.Pages)
            {
                var texts = page.Items.Where(i => i.Source is TextItem).ToList();
                PlacedItem? Find(string text) => texts.FirstOrDefault(i => TextOf(i).Contains(text));
                string on = $" (page {page.Number})";

                // The address: filled, inside the block the designer placed, stacked down it.
                PlacedItem? name = Find("Maître Jeanne Dupont"), city = Find("98800 Nouméa");
                Console.WriteLine($"    page {page.Number} : adresse à x={name?.Frame.Left:0.0}, "
                                  + $"ville en dessous à y={city?.Frame.Top:0.0}");
                AssertEqual(testName, "the recipient's name is printed" + on, true, name != null, 0);
                AssertEqual(testName, "and the town" + on, true, city != null, 0);
                if (name == null || city == null) continue;
                AssertEqual(testName, "where the block was placed, 110mm in" + on, 110, name.Frame.Left, 0.01);
                AssertEqual(testName, "the town below the name, in the same block" + on,
                    true, city.Frame.Top > name.Frame.Top && Math.Abs(city.Frame.Left - 110) < 0.01, 0);
                AssertEqual(testName, "and inside the header" + on, true, city.Frame.Bottom <= 40, 0);
                AssertEqual(testName, "the name in bold, as the piece says" + on,
                    true, ((TextItem)name.Source).Font?.Bold == true, 0);

                // The logo: the picture from the data, where its piece was placed.
                PlacedItem? picture = page.Items.FirstOrDefault(i => i.Source is ImageItem img && img.AltText == "Logo du cabinet");
                AssertEqual(testName, "the logo comes from the data" + on,
                    true, picture != null && ((ImageItem)picture.Source).Source.Data.Length > 0, 0);
                AssertEqual(testName, "at the top left of the header" + on,
                    true, picture != null && picture.Frame.Left < 0.01 && picture.Frame.Bottom <= 40, 0);

                // The footer: the case number and the page's own number, out of the total.
                string expected = $"Rapport EXP-2026-0412 - page {page.Number} sur {layout.PageCount}";
                Console.WriteLine($"    page {page.Number} : pied « {string.Join(" ", texts.Where(i => i.Frame.Top > 280).Select(TextOf))} »");
                AssertEqual(testName, "the footer's mentions, numbered" + on, true,
                    string.Join(" ", texts.Where(i => i.Frame.Top > 280).Select(TextOf)).Contains(expected), 0);

                AssertEqual(testName, "and no merge field left unfilled" + on,
                    "", string.Join(" | ", texts.Select(TextOf).Where(t => t.Contains('{'))), 0);
            }

            // The article: in the flow, between the paragraphs around it, its label in bold.
            var first = layout.Pages[0].Items.Where(i => i.Source is TextItem).ToList();
            PlacedItem? intro = first.FirstOrDefault(i => TextOf(i).StartsWith("Le présent rapport"));
            PlacedItem? label = first.FirstOrDefault(i => TextOf(i) == "Art. 1");
            PlacedItem? wording = first.FirstOrDefault(i => TextOf(i).Contains("objectivité"));
            PlacedItem? fees = first.FirstOrDefault(i => TextOf(i).StartsWith("Montant des honoraires"));
            AssertEqual(testName, "the article sits where it was named, after the paragraph before it",
                true, intro != null && label != null && fees != null
                      && intro.Frame.Top < label.Frame.Top && label.Frame.Top < fees.Frame.Top, 0);
            AssertEqual(testName, "its label in bold and its wording not",
                true, label != null && wording != null && ((TextItem)label.Source).Font?.Bold == true
                      && ((TextItem)wording.Source).Font?.Bold != true, 0);

            // A pre-formatted amount reaches the page exactly as it was written.
            AssertEqual(testName, "a pre-formatted amount prints as the host wrote it",
                true, fees != null && TextOf(fees).Contains(fee), 0);

            // --- changed once, printed everywhere -------------------------------------------
            // Two documents name the same article; the source changes; both print the change.
            Shelf amended = Store(after);
            var letter = new Report
            {
                Title = "Lettre", Language = "fr-FR",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            letter.Body!.Add(T("Madame, Monsieur,"));
            letter.Body.Add(Slot("loi/art-1"));
            letter.Reattach();

            string Printed(Report master)
            {
                master.ExpandSubTemplates(amended);
                LayoutResult again = ReportBinder.Bind(master, data).Resolve(new FontTextMeasurer(library));
                return string.Join(" ", again.Pages.SelectMany(p => p.Items).Select(TextOf));
            }
            string report2 = Printed(Master()), letter2 = Printed(letter);
            AssertEqual(testName, "the report prints the article as it now reads",
                true, report2.Contains("personnellement") && !report2.Contains("objectivité"), 0);
            AssertEqual(testName, "and so does every other document that names it",
                true, letter2.Contains("personnellement") && !letter2.Contains("objectivité"), 0);

            // --- and the file, for the eye ---------------------------------------------------
            var pdf = new PdfBackend(library) { Author = "Cabinet d'expertise" };
            ReportRenderer.Render(bound, layout, pdf);
            string path = Path.Combine(OutputDirectory("subdocuments"), "expertise.pdf");
            WriteSamplePdf(path, pdf.ToArray());
            Console.WriteLine($"    -> {path}");

            // And the proof beside it, to set the two side by side (CLAUDE.md section 2).
            var proof = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(bound, layout, proof);
            File.WriteAllText(Path.Combine(OutputDirectory("subdocuments"), "expertise.html"), proof.ToHtml());
            Console.WriteLine();
        }

        /// <summary>
        /// A table filled from a list in the data, in the shape the content editor writes it:
        /// rich text in every cell, a header row of labels, one prototype row of tokens, and
        /// the table repeating over the list. Read back from JSON first, as a host reads what
        /// the editor saved.
        ///
        /// The engine has repeated tables since the ACME invoice, whose cells are plain
        /// TextItems; what is new is the editor writing them, so this asks the binder the
        /// three things that shape depends on - tokens inside runs resolved per record, a
        /// token the record does not hold found at the root, and a list with nothing in it
        /// taking its header away with it rather than leaving it stranded.
        /// </summary>
        [Fact]
        public void Test96_EditorTableFromAList()
        {
            const string testName = "Test 96";
            Console.WriteLine("Test 96: a table the content editor writes, repeated over a list");
            Console.WriteLine();

            static AdvancedTextItem Rich(string text)
            {
                var item = new AdvancedTextItem();
                item.Runs.Add(new TextRun { Text = text });
                return item;
            }
            static TableItem ListTable(string list, (string Label, string Token)[] columns)
            {
                var table = new TableItem { RepeatOver = list };
                var head = new TableRow(isHeader: true);
                var line = new TableRow(isHeader: false);
                foreach (var (label, token) in columns)
                {
                    table.AddAutoColumn();
                    head.Add(Rich(label));
                    line.Add(Rich(token));
                }
                table.Add(head);
                table.Add(line);
                return table;
            }

            var written = new Report
            {
                Title = "Facture",
                Language = "fr-FR",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack },
            };
            written.Body!.Add(Rich("Facture {Dossier.Numero}"));
            written.Body.Add(ListTable("Lignes", new[] { ("Référence", "{Ref}"), ("Montant", "{Montant} {Devise}") }));
            written.Body.Add(ListTable("Remises", new[] { ("Remise accordée", "{Libelle}") }));
            written.Reattach();

            // Formatted by the host, as every value this editor inserts is.
            string data = new System.Text.Json.Nodes.JsonObject
            {
                ["Dossier"] = new System.Text.Json.Nodes.JsonObject { ["Numero"] = "2026-0417" },
                ["Devise"] = "EUR",
                ["Lignes"] = new System.Text.Json.Nodes.JsonArray(
                    new System.Text.Json.Nodes.JsonObject { ["Ref"] = "A-1", ["Montant"] = "1 200,00" },
                    new System.Text.Json.Nodes.JsonObject { ["Ref"] = "A-2", ["Montant"] = "350,00" },
                    new System.Text.Json.Nodes.JsonObject { ["Ref"] = "A-3", ["Montant"] = "12 480,00" }),
                ["Remises"] = new System.Text.Json.Nodes.JsonArray(),
            }.ToJsonString();

            Report template = Report.FromJson(written.ToJson());
            Report bound = ReportBinder.Bind(template, data);
            var library = new FontLibrary();
            LayoutResult layout = bound.Resolve(new FontTextMeasurer(library));

            static string TextOf(PlacedItem p) => p.Fragment ?? (p.Source as TextItem)?.Text ?? "";
            var texts = layout.Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem).ToList();
            List<PlacedItem> Saying(string text) => texts.Where(i => TextOf(i) == text).ToList();
            Console.WriteLine($"    {string.Join(" | ", texts.Select(TextOf))}");

            foreach (string reference in new[] { "A-1", "A-2", "A-3" })
                AssertEqual(testName, $"line {reference} is printed, once", 1, Saying(reference).Count, 0);

            AssertEqual(testName, "the lines come in the list's order, top to bottom", true,
                Saying("A-1")[0].Frame.Top < Saying("A-2")[0].Frame.Top
                && Saying("A-2")[0].Frame.Top < Saying("A-3")[0].Frame.Top, 0);

            AssertEqual(testName, "a token the line does not hold is found at the root",
                1, Saying("12 480,00 EUR").Count, 0);

            AssertEqual(testName, "the header is printed once, above the lines", true,
                Saying("Référence").Count == 1 && Saying("Référence")[0].Frame.Top < Saying("A-1")[0].Frame.Top, 0);

            AssertEqual(testName, "a list with nothing in it takes its header with it",
                0, Saying("Remise accordée").Count, 0);

            AssertEqual(testName, "and no merge field is left unfilled",
                "", string.Join(" | ", texts.Select(TextOf).Where(t => t.Contains('{'))), 0);
            Console.WriteLine();
        }

        /// <summary>
        /// What a list with nothing in it leaves, when the template says what to show: a whole
        /// table. Reported by another application on 0.4.0. The header and the message went out
        /// with their tokens as typed, and the message was one cell in the first column, at the
        /// document's size rather than the table's - broken a word at a time, and refused by
        /// PDF/UA 7.2 for a row narrower than the others.
        ///
        /// No sample had the shape: test 26's table has one column and no header, and test 96's
        /// empty list says nothing. The conformance document carries one now, which is what puts
        /// it in front of veraPDF (test 53).
        /// </summary>
        [Fact]
        public void Test108_AListWithNothingInIt()
        {
            const string testName = "Test 108";
            Console.WriteLine("Test 108: a list with nothing in it, and a message to show");
            Console.WriteLine();

            const double lineSize = 0.7;
            const string none = """{ "Client": "ACME", "Lines": [] }""";
            const string one = """{ "Client": "ACME", "Lines": [ { "Ref": "a", "Name": "b", "Total": "c" } ] }""";

            static TextItem Cell(string text, double? size = null)
                => new() { Text = text, Font = new TextStyle { Size = size } };

            static TableItem Listing(string? message, string heading = "Lines of {Client}", bool fitted = false)
            {
                var table = new TableItem { RepeatOver = "Lines", EmptyText = message, Grid = new PenStyle { Width = 0.2 } };
                if (fitted)
                    table.AddFittedColumn();
                else
                    table.AddColumn(12);
                table.AddColumn(90).AddColumn(30);
                table.AddHeaderRow(Cell("Ref"), Cell(heading), Cell("Total"));
                table.AddRow(Cell("{Ref}", lineSize), Cell("{Name}", lineSize), Cell("{Total}", lineSize));
                return table;
            }

            static Report Sheet(BaseItem content)
            {
                var report = new Report
                {
                    Title = "An empty list",
                    Language = "en-GB",
                    GeneratedAt = SampleDate,
                    Dimension = new() { Width = 210, Height = 297 },
                    Body = new() { Layout = LayoutMode.Stack },
                };
                report.Body!.Add(content);
                report.Reattach();
                return report;
            }

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static string TextOf(PlacedItem p) => p.Fragment ?? ((TextItem)p.Source).Text;
            List<PlacedItem> Placed(Report bound)
                => bound.Resolve(measurer).Pages.SelectMany(p => p.Items).Where(i => i.Source is TextItem).ToList();
            List<PlacedItem> Bound(BaseItem content, string data) => Placed(ReportBinder.Bind(Sheet(content), data));
            static PlacedItem Saying(List<PlacedItem> placed, string text) => placed.First(i => TextOf(i) == text);

            // --- what goes on the page ----------------------------------------------------
            List<PlacedItem> empty = Bound(Listing("Nothing for {Client}"), none);
            Console.WriteLine($"    {string.Join(" | ", empty.Select(TextOf))}");

            AssertEqual(testName, "the header is filled", 1, empty.Count(i => TextOf(i) == "Lines of ACME"), 0);
            AssertEqual(testName, "and the message, on one line across the table", 1,
                empty.Count(i => TextOf(i) == "Nothing for ACME"), 0);
            AssertEqual(testName, "with no token left anywhere", "",
                string.Join(" | ", empty.Select(TextOf).Where(t => t.Contains('{'))), 0);

            PlacedItem said = Saying(empty, "Nothing for ACME");
            AssertEqual(testName, "it starts where the first column does",
                Saying(empty, "Ref").Frame.Left, said.Frame.Left, 0.001);
            AssertEqual(testName, "and ends where the last one does",
                Saying(empty, "Total").Frame.Right, said.Frame.Right, 0.001);
            AssertEqual(testName, "centred", true, said.Font!.HorizontalAlignment == HorizontalAlignment.Center, 0);

            AssertEqual(testName, "a list that is not there says the same", 1,
                Bound(Listing("Nothing for {Client}"), """{ "Client": "ACME" }""").Count(i => TextOf(i) == "Nothing for ACME"), 0);

            // --- it reads like the lines it stands in for ---------------------------------
            double LineSize(BaseItem content) => Saying(Bound(content, one), "a").Font!.Size!.Value;
            double line = LineSize(Listing("x"));

            AssertEqual(testName, "the lines are not at the size of the header, or this proves nothing", true,
                Math.Abs(line - Saying(empty, "Ref").Font!.Size!.Value) > 1, 0);
            AssertEqual(testName, "the message is at the size of the lines", line, said.Font.Size!.Value, 0.001);

            double TableFoot(string data)
                => ReportBinder.Bind(Sheet(Listing("Nothing for {Client}")), data).Resolve(measurer)
                               .Pages[0].Items.First(i => i.Source is TableItem).Frame.Bottom;
            AssertEqual(testName, "and its row is as tall as a line's", TableFoot(one), TableFoot(none), 0.001);

            static TableItem RichFirstCell(TextStyle paragraph, TextStyle? run)
            {
                var rich = new AdvancedTextItem { Font = paragraph };
                rich.Runs.Add(new TextRun { Text = "{Ref}", Font = run });
                var table = Listing("Nothing for {Client}");
                var row = new TableRow();
                row.Add(rich);
                row.Add(Cell("{Name}"));
                row.Add(Cell("{Total}"));
                table.Remove(table.Childs.OfType<TableRow>().Last());
                table.Add(row);
                return table;
            }

            AssertEqual(testName, "a rich first cell gives its paragraph's size", line,
                Saying(Bound(RichFirstCell(new TextStyle { Size = lineSize }, null), none), "Nothing for ACME").Font!.Size!.Value, 0.001);
            AssertEqual(testName, "and its first run's when that says otherwise", line,
                Saying(Bound(RichFirstCell(new TextStyle { Size = 3 }, new TextStyle { Size = lineSize }), none), "Nothing for ACME").Font!.Size!.Value, 0.001);

            // --- read outside the list, in whatever encloses it ----------------------------
            var families = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Families" };
            families.Add(Listing("Nothing in {Family} for {Client}", "Lines of {Family}"));
            List<PlacedItem> nested = Bound(families, """
                { "Client": "ACME", "Families": [
                    { "Family": "tools", "Lines": [ { "Ref": "a", "Name": "b", "Total": "c" } ] },
                    { "Family": "toys", "Lines": [] } ] }
                """);

            AssertEqual(testName, "nested, the message reads the entry around it, and the root", 1,
                nested.Count(i => TextOf(i) == "Nothing in toys for ACME"), 0);
            AssertEqual(testName, "and the header too", 1, nested.Count(i => TextOf(i) == "Lines of toys"), 0);
            AssertEqual(testName, "the family with lines has no message", 0, nested.Count(i => TextOf(i) == "Nothing in tools for ACME"), 0);

            // --- filled as everything else is ----------------------------------------------
            AssertEqual(testName, "an unknown token stays visible by default", 1,
                Bound(Listing("Nothing for {Nope}"), none).Count(i => TextOf(i) == "Nothing for {Nope}"), 0);
            AssertEqual(testName, "or is blanked on request", 1,
                Placed(ReportBinder.Bind(Sheet(Listing("Nothing for {Nope}")), none, new BindingOptions { MissingField = MissingField.Blank }))
                    .Count(i => TextOf(i).Trim() == "Nothing for"), 0);

            bool Refused(TableItem table)
            {
                try
                {
                    ReportBinder.Bind(Sheet(table), none, new BindingOptions { MissingField = MissingField.Fail });
                    return false;
                }
                catch (MissingDataException)
                {
                    return true;
                }
            }

            AssertEqual(testName, "or refused, in the message", true, Refused(Listing("Nothing for {Nope}")), 0);
            AssertEqual(testName, "and in the header", true, Refused(Listing("Nothing", "Lines of {Nope}")), 0);

            TableItem hidden = Listing("Nothing for {Client}");
            hidden.Childs.OfType<TableRow>().First().ShowWhen = "Nope";
            List<PlacedItem> noHeader = Bound(hidden, none);
            AssertEqual(testName, "a header's own condition still holds", 0, noHeader.Count(i => TextOf(i) == "Ref"), 0);
            AssertEqual(testName, "with the message still there", 1, noHeader.Count(i => TextOf(i) == "Nothing for ACME"), 0);

            // --- a container that is not a table -------------------------------------------
            var block = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Lines", EmptyText = "Nothing for {Client}" };
            block.Add(Cell("{Ref}", lineSize));
            AssertEqual(testName, "any other container fills its message and gives it its font", line,
                Saying(Bound(block, none), "Nothing for ACME").Font!.Size!.Value, 0.001);

            // --- the shape of the row, wherever the table is -------------------------------
            // A fitted column measures its cells, and a message wider than any column is not one.
            double FirstColumnEnds(string message)
                => Saying(Bound(Listing(message, fitted: true), none), "Ref").Frame.Right;
            const string longer = "Nothing at all here, and a good deal more to say about that than any one column holds";
            AssertEqual(testName, "the message does not widen a fitted column", FirstColumnEnds("x"), FirstColumnEnds(longer), 0.001);

            // In a band a table is laid where it was put, by another road than the flow's.
            Report banded = Sheet(Cell("Body"));
            banded.Header = new Header();
            banded.Header.Style.Location.HeightValue = 40;
            banded.Header.Add(Listing("Nothing for {Client}"));
            banded.Reattach();
            List<PlacedItem> inBand = Placed(ReportBinder.Bind(banded, none));
            AssertEqual(testName, "in a band too the message is on one line", 1,
                inBand.Count(i => TextOf(i) == "Nothing for ACME"), 0);
            AssertEqual(testName, "and ends where the last column does",
                Saying(inBand, "Total").Frame.Right, Saying(inBand, "Nothing for ACME").Frame.Right, 0.001);

            // Too long for a page, so it splits: each part still takes the whole table.
            string endless = string.Join(" ", Enumerable.Repeat("Nothing to show here.", 500));
            Report lengthy = Sheet(Cell("Before"));
            lengthy.Body!.Add(Listing(endless));
            lengthy.Reattach();
            List<PlacedItem> split = Placed(ReportBinder.Bind(lengthy, none));
            List<PlacedItem> parts = split.Where(i => TextOf(i).Contains("show", StringComparison.Ordinal)).ToList();
            Console.WriteLine($"    a message too long for a page comes in {parts.Count} parts");
            AssertEqual(testName, "a message too long for a page splits", true, parts.Count >= 2, parts.Count);
            AssertEqual(testName, "each part across the table", true,
                parts.All(p => Math.Abs(p.Frame.Right - Saying(split, "Total").Frame.Right) < 0.001
                            && Math.Abs(p.Frame.Left - Saying(split, "Ref").Frame.Left) < 0.001), 0);
            AssertEqual(testName, "and every word of it arrives", 500,
                System.Text.RegularExpressions.Regex.Matches(string.Concat(parts.Select(TextOf)), "here").Count, 0);

            // --- the outputs say so, each in its own way --------------------------------
            string Proof(string data)
            {
                var svg = new SvgBackend(measurer);
                ReportRenderer.Render(ReportBinder.Bind(Sheet(Listing("Nothing for {Client}")), data), svg);
                return string.Concat(svg.Pages);
            }
            static int Rectangles(string svg) => System.Text.RegularExpressions.Regex.Matches(svg, "<rect").Count;
            AssertEqual(testName, "the grid strokes one box across the row where a line has three", 2,
                Rectangles(Proof(one)) - Rectangles(Proof(none)), 0);

            string Tagged(string data)
            {
                var pdf = new PdfBackend(library)
                {
                    Conformance = PdfConformance.PdfA3b, Language = "en-GB", Tagged = true, Compress = false
                };
                ReportRenderer.Render(ReportBinder.Bind(Sheet(Listing("Nothing for {Client}")), data), pdf);
                return System.Text.Encoding.Latin1.GetString(Unpacked(pdf.ToArray()));
            }
            static int Spans(string pdf) => System.Text.RegularExpressions.Regex.Matches(pdf, "/ColSpan").Count;
            AssertEqual(testName, "the cell says how many columns it takes", 1,
                System.Text.RegularExpressions.Regex.Matches(Tagged(none), @"/S /TD[^>]*/A << /O /Table /ColSpan 3 >>").Count, 0);
            AssertEqual(testName, "and nothing else does", 1, Spans(Tagged(none)), 0);
            AssertEqual(testName, "and a line of a list does not", 0, Spans(Tagged(one)), 0);

            string Mail(string data) => FlowingHtml.Write(ReportBinder.Bind(Sheet(Listing("Nothing for {Client}")), data));
            AssertEqual(testName, "the mail spans the same columns", true,
                System.Text.RegularExpressions.Regex.IsMatch(Mail(none), "<td colspan=\"3\"[^>]*>.*Nothing for ACME"), 0);
            AssertEqual(testName, "and only there", false, Mail(one).Contains("colspan", StringComparison.Ordinal), 0);

            // --- and the checks ------------------------------------------------------------
            static IEnumerable<string> Ragged(Report report)
                => TemplateValidator.Check(report, TemplateProfile.Sound).Where(i => i.Rule == "sound.raggedTable").Select(i => i.Message);

            Report filled = ReportBinder.Bind(Sheet(Listing("Nothing")), none);
            AssertEqual(testName, "the row the binder made is not called ragged", "",
                string.Join(" | ", Ragged(filled)), 0);
            AssertEqual(testName, "and a copy of it still takes every column", 3,
                ((TableRow)filled.AllItems.OfType<TableRow>().Last().Clone()).SpansColumns ? 3 : 1, 0);

            TableItem shortRow = Listing(null);
            TableRow written = shortRow.Childs.OfType<TableRow>().Last();
            written.Remove(written.Childs[2]);
            written.Remove(written.Childs[1]);
            AssertEqual(testName, "but a row written with one cell still is: no template can ask for this", 1,
                Ragged(Sheet(shortRow)).Count(), 0);

            const string catalogue = """[ "Client", { "path": "Lines", "type": "list", "fields": [ "Ref", "Name", "Total" ] } ]""";
            List<TemplateIssue> Unknown(string message)
                => TemplateFields.Check(Sheet(Listing(message)), catalogue).Where(i => i.Rule == "fields.unknown").ToList();

            AssertEqual(testName, "a message the data answers has nothing said of it", 0, Unknown("Nothing for {Client}").Count, 0);
            List<TemplateIssue> misspelt = Unknown("Nothing for {Clent}");
            AssertEqual(testName, "a misspelt one is found, at its place", true,
                misspelt.Count == 1 && misspelt[0].Pointer?.EndsWith("/EmptyText", StringComparison.Ordinal) == true, 0);
            AssertEqual(testName, "with the name meant", true, misspelt[0].Message.Contains("\"Client\"", StringComparison.Ordinal), 0);
            AssertEqual(testName, "and a field of an entry is one too: there is no entry to read", 1, Unknown("Nothing for {Ref}").Count, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test109_NothingGoesOffTheFootInSilence()
        {
            const string testName = "Test 109";
            Console.WriteLine("Test 109: what does not fit goes overleaf, or says it cannot");
            Console.WriteLine();

            const double foot = 120;

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static string[] Words(string prefix, int count)
                => Enumerable.Range(0, count).Select(i => prefix + i).ToArray();
            static TextItem Plain(string text) => new() { Text = text };
            static AdvancedTextItem Rich(params string[] runs)
            {
                var item = new AdvancedTextItem();
                foreach (string run in runs)
                    item.Runs.Add(new TextRun { Text = run });
                return item;
            }
            static TableItem Table(params BaseItem[] cells)
            {
                var table = new TableItem();
                foreach (BaseItem _ in cells)
                    table.AddAutoColumn();
                table.AddRow(cells);
                return table;
            }
            static ContainerItem Group(params BaseItem[] items)
            {
                var group = new ContainerItem { Layout = LayoutMode.Stack };
                foreach (BaseItem item in items)
                    group.Add(item);
                return group;
            }
            static TextItem Tall(double height, string text = "Tall")
            {
                TextItem item = Plain(text);
                item.Style.Location.Height = (height, Units.Millimeter);
                return item;
            }
            static ImageItem Picture(double height) => new()
            {
                NaturalWidth = 50, NaturalHeight = height, AltText = "A picture", Scaling = ImageScaling.None,
            };
            static Report Sheet(params BaseItem[] items)
            {
                var report = new Report
                {
                    Title = "Past the foot",
                    Language = "en-GB",
                    GeneratedAt = SampleDate,
                    Dimension = new() { Width = 100, Height = foot },
                    Body = new() { Layout = LayoutMode.Stack },
                };
                foreach (BaseItem item in items)
                    report.Body!.Add(item);
                report.Reattach();
                return report;
            }

            string long1 = string.Join(" ", Words("a", 300));
            string long2 = string.Join(" ", Words("b", 300));

            static IEnumerable<string> Said(PlacedItem placed)
                => placed.Source is TextItem text ? (placed.Fragment ?? text.Text).Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries) : [];

            // Every word of a column, on the paper, once, in order, and nothing said about it.
            void Arrives(string what, Report report, params (string Prefix, int Count)[] columns)
            {
                LayoutResult layout = report.Resolve(measurer);
                Console.WriteLine($"    {what}: {layout.PageCount} page(s)");

                List<PlacedItem> placed = layout.Pages.SelectMany(p => p.Items).ToList();
                foreach ((string prefix, int count) in columns)
                    AssertEqual(testName, $"{what}: every {prefix} word, once and in order", string.Join(" ", Words(prefix, count)),
                        string.Join(" ", placed.SelectMany(Said).Where(w => w.StartsWith(prefix, StringComparison.Ordinal))), 0);

                AssertEqual(testName, $"{what}: nothing past the foot", "",
                    string.Join(" | ", placed.Where(p => p.Source is not Body && p.Frame.Bottom > foot + 0.01)
                                             .Select(p => $"{p.Source.GetType().Name} to {p.Frame.Bottom:0.0}")), 0);
                AssertEqual(testName, $"{what}: and nothing to say", 0, layout.Issues.Count, 0);
            }

            // --- a row taller than a page is cut, wherever it stands --------------------------
            Arrives("a text cell, first on its page", Sheet(Table(Plain(long1))), ("a", 300));
            Arrives("a text cell, after something", Sheet(Plain("Before"), Table(Plain(long1))), ("a", 300));
            Arrives("a rich cell, first on its page", Sheet(Table(Rich(long1))), ("a", 300));
            Arrives("a rich cell, after something", Sheet(Plain("Before"), Table(Rich(long1))), ("a", 300));
            Arrives("a text cell beside a rich one", Sheet(Table(Plain(long1), Rich(long2))), ("a", 300), ("b", 300));
            Arrives("a rich cell in two columns", Sheet(new Func<ContainerItem>(() =>
            {
                ContainerItem columns = Group(Table(Rich(long1)));
                columns.ColumnCount = 2;
                return columns;
            })()), ("a", 300));

            TableItem headed = Table(Rich(long1));
            headed.AddHeaderRow(Plain("Heading"));
            headed.RepeatHeader = true;
            Arrives("under a header that comes back", Sheet(headed), ("a", 300));

            // A header and a row that each fit, and do not fit together, was the same fault.
            TableItem crowded = Table(Plain(string.Join(" ", Words("a", 150))));
            crowded.AddHeaderRow(Plain(string.Join(" ", Enumerable.Repeat("head", 100))));
            Arrives("a row that only overflows its header", Sheet(crowded), ("a", 150));

            // What the box adds has to be counted, or the last line of a part goes past the foot.
            TableItem padded = Table(Rich(long1));
            TableRow paddedRow = padded.Childs.OfType<TableRow>().Single();
            paddedRow.Style.Padding = new() { Top = 9, Bottom = 9 };
            paddedRow.Childs[0].Style.Padding = new() { Top = 7, Bottom = 7 };
            paddedRow.Childs[0].Style.Margin = new() { Top = 5, Bottom = 5 };
            Arrives("a padded row", Sheet(padded), ("a", 300));

            // Lines of a rich text have heights of their own, so a part is not lines times one.
            AdvancedTextItem mixed = Rich(long1);
            mixed.Runs.Insert(0, new TextRun { Text = "Large opening words\n", Font = new TextStyle { Size = 3 } });
            mixed.Runs.Add(new TextRun { Text = "\nLarge closing words", Font = new TextStyle { Size = 3 } });
            Arrives("a rich cell of two sizes", Sheet(Table(mixed)), ("a", 300));

            // --- each part is still a row ------------------------------------------------------
            AdvancedTextItem shortCell = Rich("Short");
            TableItem uneven = Table(Rich(long1), shortCell);
            LayoutResult unevenLaid = Sheet(uneven).Resolve(measurer);
            var unevenRow = uneven.Childs.OfType<TableRow>().Single();
            AssertEqual(testName, "the row was cut", true, unevenLaid.PageCount >= 2, 0);
            foreach (Page page in unevenLaid.Pages)
            {
                AssertEqual(testName, $"page {page.Number} shows both its cells", 2,
                    unevenRow.Childs.Count(cell => page.Items.Any(i => ReferenceEquals(i.Source, cell))), 0);

                PlacedItem rowPart = page.Items.Single(i => ReferenceEquals(i.Source, unevenRow));
                AssertEqual(testName, $"page {page.Number}: the cell is inside its row", true,
                    page.Items.Where(i => ReferenceEquals(i.Source, unevenRow.Childs[0]))
                        .All(i => i.Frame.Top >= rowPart.Frame.Top - 0.01 && i.Frame.Bottom <= rowPart.Frame.Bottom + 0.01), 0);
            }
            AssertEqual(testName, "and the short one says what it says once", 1,
                unevenLaid.Pages.SelectMany(p => p.Items).SelectMany(Said).Count(w => w == "Short"), 0);

            TableItem total = Table(Rich(long1, " of {Pages}"));
            LayoutResult totalLaid = Sheet(total).Resolve(measurer);
            AssertEqual(testName, "the page total in a rich cell that was cut is the real one", totalLaid.PageCount.ToString(),
                totalLaid.Pages.SelectMany(p => p.Items).SelectMany(Said).Last(), 0);

            // --- only a row with a page to itself goes down as it stands -----------------------
            TableItem keptSecond = new();
            keptSecond.AddAutoColumn();
            keptSecond.AddRow(Plain("a0 first row"));
            keptSecond.AddRow(Plain(string.Join(" ", Words("k", 100))));
            keptSecond.Childs[0].Style.Location.Height = (105, Units.Millimeter);
            keptSecond.Childs[1].Style.Splitting = Splitting.KeepTogether;
            LayoutResult keptLaid = Sheet(keptSecond).Resolve(measurer);
            AssertEqual(testName, "a kept row that is not the first moves to the next page", true,
                keptLaid.PageCount == 2
                && keptLaid.Pages[1].Items.Any(i => ReferenceEquals(i.Source, keptSecond.Childs[1]))
                && !keptLaid.Pages[0].Items.Any(i => ReferenceEquals(i.Source, keptSecond.Childs[1])), 0);

            // A line taller than any page, part way down a row: what is left of the row goes
            // down from where it had got to. It used to start the row again, words and all.
            AdvancedTextItem giant = Rich("a0\n", "a1\n", "a2");
            giant.Runs[1].Font = new TextStyle { Size = 150 };
            TableItem afterGiant = Table(giant);
            afterGiant.AddRow(Plain("c0"));
            LayoutResult giantLaid = Sheet(afterGiant).Resolve(measurer);
            AssertEqual(testName, "what no page can hold goes down once, and from where it had got to", "a0 a1 a2 c0",
                string.Join(" ", giantLaid.Pages.SelectMany(p => p.Items).SelectMany(Said)), 0);
            AssertEqual(testName, "and is said", "fits.overflow",
                string.Join(",", giantLaid.Issues.Select(i => i.Rule)), 0);

            // --- a group lower down a page has a page to go to ---------------------------------
            void Moves(string what, BaseItem first)
            {
                ContainerItem group = Group(first);
                LayoutResult layout = Sheet(Tall(100), group).Resolve(measurer);
                PlacedItem[] on1 = layout.Pages[0].Items.Where(i => i.Source is not (Body or Header or Footer)).ToArray();
                Console.WriteLine($"    {what}: {layout.PageCount} page(s)");

                AssertEqual(testName, $"{what}: nothing past the foot", 0d, Math.Max(0, on1.Max(i => i.Frame.Bottom) - foot), 0.01);
                AssertEqual(testName, $"{what}: it opens the next page", true,
                    layout.PageCount >= 2 && layout.Pages[1].Items.Any(i => ReferenceEquals(i.Source, first)), 0);
                AssertEqual(testName, $"{what}: and leaves no empty group behind", false,
                    layout.Pages[0].Items.Any(i => ReferenceEquals(i.Source, group)), 0);
                AssertEqual(testName, $"{what}: and nothing to say", 0, layout.Issues.Count, 0);
            }

            Moves("a picture in a group", Picture(50));
            Moves("a grid in a group", new Func<ImageGridItem>(() =>
            {
                var grid = new ImageGridItem { GridColumns = 1, RowHeight = 50 };
                grid.Add(Picture(40));
                return grid;
            })());
            TableItem keptInGroup = Table(Plain(string.Join(" ", Words("k", 60))));
            keptInGroup.Childs[0].Style.Splitting = Splitting.KeepTogether;
            Moves("a kept row in a group", keptInGroup);

            // In columns too: each column starts at the group's own top, not on a fresh page.
            ImageItem inColumns = Picture(50);
            ContainerItem columned = Group(inColumns);
            columned.ColumnCount = 2;
            LayoutResult columnedLaid = Sheet(Tall(100), columned).Resolve(measurer);
            AssertEqual(testName, "a group in columns moves the same", true,
                columnedLaid.PageCount == 2 && columnedLaid.Pages[1].Items.Any(i => ReferenceEquals(i.Source, inColumns)), 0);

            // A table in a group that is cut is still cut, and carries its first row's lines here.
            ContainerItem cutGroup = Group(Table(Plain(string.Join(" ", Words("a", 200)))));
            Arrives("a long row in a group lower down", Sheet(Tall(90), cutGroup), ("a", 200));

            // What goes first with it goes with it, which is what KeepWithNext was for.
            TextItem heading = Plain("Heading");
            heading.Style.KeepWithNext = true;
            LayoutResult withHeading = Sheet(Tall(100), Plain("Standing"), heading, Group(Picture(50))).Resolve(measurer);
            AssertEqual(testName, "a heading follows the group it heads", true,
                withHeading.Pages[1].Items.Any(i => ReferenceEquals(i.Source, heading)), 0);

            // A stated height that fits here has nothing to gain from another page.
            ContainerItem box = Group(Picture(30));
            box.Style.Location.Height = (10, Units.Millimeter);
            LayoutResult boxed = Sheet(Tall(60), box).Resolve(measurer);
            AssertEqual(testName, "a stated box that fits stays where it was put", true,
                boxed.PageCount == 1 && boxed.Pages[0].Items.Any(i => ReferenceEquals(i.Source, box)), 0);

            // --- a band has no next page ------------------------------------------------------
            TableItem footed = new();
            footed.AddAutoColumn();
            footed.AddRow(Plain(string.Join(" ", Words("a", 60))));
            footed.AddRow(Plain("b0"));
            Report bandReport = Sheet(Plain("Body"));
            var footer = new Footer { Layout = LayoutMode.Stack };
            footer.Style.Location.Height = (10, Units.Millimeter);
            footer.Add(footed);
            bandReport.Section!.Add(footer);
            bandReport.Reattach();
            LayoutResult bandLaid = bandReport.Resolve(measurer);
            List<string> bandWords = bandLaid.Pages[0].Items.SelectMany(Said).ToList();
            AssertEqual(testName, "a table in a band too small for it loses nothing", 61,
                bandWords.Count(w => w != "Body"), 0);
            AssertEqual(testName, "and all of it stays on the one page", 1, bandLaid.PageCount, 0);

            // The band is resolved first on every page, and what it knows stops with it: the
            // body's own long row still has a next page.
            Report bandAndBody = Sheet(Table(Rich(long1)));
            var plainFooter = new Footer { Layout = LayoutMode.Stack };
            plainFooter.Add(Table(Plain("Footer")));
            bandAndBody.Section!.Add(plainFooter);
            bandAndBody.Reattach();
            Arrives("a long row under a stacked footer", bandAndBody, ("a", 300));

            // --- what cannot be cut says so ----------------------------------------------------
            List<TemplateIssue> Told(params BaseItem[] items) => Sheet(items).Resolve(measurer).Issues.ToList();
            void Says(string what, List<TemplateIssue> told, BaseItem culprit, string where)
            {
                Console.WriteLine($"    {what}: {string.Join(" | ", told.Select(i => $"{i.Rule} at {i.Where}"))}");
                AssertEqual(testName, $"{what}: one thing to put right", "fits.overflow",
                    string.Join(",", told.Select(i => i.Rule)), 0);
                AssertEqual(testName, $"{what}: and which", true, ReferenceEquals(told[0].Item, culprit), 0);
                AssertEqual(testName, $"{what}: and where", where, told[0].Where, 0);
                AssertEqual(testName, $"{what}: a warning", IssueLevel.Warning, told[0].Level, 0);
            }

            ImageItem tower = Picture(400);
            Says("a picture taller than the page", Told(tower), tower, "section 1, page 1");

            ImageItem later = Picture(400);
            Says("the same further on", Told(Plain("Before"), later), later, "section 1, page 2");

            ContainerItem groupCell = Group(Enumerable.Range(0, 200).Select(i => (BaseItem)Plain("g" + i)).ToArray());
            TableItem groupTable = Table(groupCell);
            Says("a group in a cell", Told(groupTable), groupTable.Childs[0], "section 1, page 1");

            TableItem keptRich = Table(Rich(long1));
            keptRich.Childs[0].Style.Splitting = Splitting.KeepTogether;
            Says("a rich row asked to stay whole", Told(keptRich), keptRich.Childs[0], "section 1, page 1");

            TextItem keptText = Plain(long1);
            keptText.Style.Splitting = Splitting.KeepTogether;
            Says("a text asked to stay whole", Told(keptText), keptText, "section 1, page 1");

            ImageItem nested = Picture(400);
            Says("a picture inside a group", Told(Group(nested)), nested, "section 1, page 1");

            ImageItem secret = Picture(400);
            secret.Style.Visibility = Visibility.Invisible;
            AssertEqual(testName, "what is not drawn is not lost", 0, Told(secret).Count, 0);

            // Composing starts over when the page total takes another digit, and has to end up
            // having said this once, in the attempt that counts.
            ImageItem counted = Picture(400);
            Report restarted = Sheet(counted, Plain(string.Join(" ", Enumerable.Repeat("words", 4000))), Plain("{Pages}"));
            AssertEqual(testName, "said once however often composing starts over", 1, restarted.Resolve(measurer).Issues.Count, 0);

            // And the command line hands it on: nothing else there reads a layout's issues.
            string directory = Directory.CreateTempSubdirectory("frisket-overflow").FullName;
            string template = Path.Combine(directory, "tower.json");
            File.WriteAllText(template, Sheet(Picture(400)).ToJson());
            string configuration = AppContext.BaseDirectory.Contains(
                Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar) ? "Debug" : "Release";
            string cli = Path.Combine(RepositoryRoot(), "apps", "cli", "bin", configuration, "net10.0", "Frisket.Cli.dll");
            var run = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    ArgumentList = { cli, "render", "-t", template, "-o", directory, "--format", "html" },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                },
            };
            run.Start();
            run.StandardOutput.ReadToEnd();
            string errors = run.StandardError.ReadToEnd();
            run.WaitForExit();
            AssertEqual(testName, "the command line says it on standard error", true,
                errors.Contains("fits.overflow, section 1, page 1", StringComparison.Ordinal), 0);
            AssertEqual(testName, "and still writes the document", 0, run.ExitCode, 0);
            Directory.Delete(directory, recursive: true);

            Console.WriteLine();
        }

        /// <summary>The table tests 128 and 129 ask about: Ref fixed, Name sharing what is left, Rebate fixed, Total fixed.</summary>
        private static TableItem RebateTable(Action<TableColumn>? rebate = null, bool shareNothing = false,
                                             string? empty = null, string list = "Lines")
        {
            static TextItem Cell(string text) => new() { Text = text };

            var table = new TableItem { RepeatOver = list, EmptyText = empty, Grid = new PenStyle { Width = 0.2 } };
            table.AddColumn(15);
            if (shareNothing)
                table.AddColumn(60);
            else
                table.AddAutoColumn();
            table.AddColumn(20).AddColumn(25);
            rebate?.Invoke(table.Columns[2]);
            table.AddHeaderRow(Cell("Ref"), Cell("Name"), Cell("Rebate"), Cell("Total"));
            table.AddRow(Cell("{Ref}"), Cell("{Name}"), Cell("{Rebate}"), Cell("{Total}"));
            return table;
        }

        /// <summary>A page holding <paramref name="content"/> and nothing else.</summary>
        private static Report RebateSheet(params BaseItem[] content)
        {
            var report = new Report
            {
                Title = "A column that goes",
                Language = "en-GB",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack },
            };
            foreach (BaseItem item in content)
                report.Body!.Add(item);
            report.Reattach();
            return report;
        }

        private const string WithRebate = """
            { "Client": "ACME", "Discount": true,
              "Lines": [ { "Ref": "a1", "Name": "Hammer", "Rebate": "-10% on a first order\nof the year", "Total": "9.00" },
                         { "Ref": "a2", "Name": "Nails", "Rebate": "-5%", "Total": "4.75" } ] }
            """;

        private static readonly string WithoutRebate = WithRebate.Replace("\"Discount\": true", "\"Discount\": false");

        [Fact]
        public void Test128_AColumnShowsKeepsItsRoomOrGoes()
        {
            const string testName = "Test 128";
            Console.WriteLine("Test 128: a column shows, keeps its room or goes - by itself, or as the data says");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            var measurer = new FontTextMeasurer(library);

            static string TextOf(PlacedItem p) => p.Fragment ?? ((TextItem)p.Source).Text;
            static List<string> Words(List<PlacedItem> placed) => placed.Where(p => p.Source is TextItem).Select(TextOf).ToList();
            static int Rebates(List<PlacedItem> placed)
                => Words(placed).Count(w => w.Contains("Rebate", StringComparison.Ordinal) || w.StartsWith('-'));
            static Rect At(List<PlacedItem> placed, string text) => placed.First(p => p.Source is TextItem && TextOf(p) == text).Frame;
            static double Foot(List<PlacedItem> placed) => placed.First(p => p.Source is TableItem).Frame.Bottom;

            List<PlacedItem> Placed(Report report) => report.Resolve(measurer).Pages.SelectMany(p => p.Items).ToList();
            List<PlacedItem> Bound(string data, params BaseItem[] content) => Placed(ReportBinder.Bind(RebateSheet(content), data));

            // --- the data decides ------------------------------------------------------------
            List<PlacedItem> shown = Bound(WithRebate, RebateTable(c => c.ShowWhen = "Discount"));
            List<PlacedItem> gone = Bound(WithoutRebate, RebateTable(c => c.ShowWhen = "Discount"));
            Console.WriteLine($"    with a discount: {string.Join(" | ", Words(shown))}");
            Console.WriteLine($"    without one:     {string.Join(" | ", Words(gone))}");

            AssertEqual(testName, "with a discount the column is there, its header and every line", true, Rebates(shown) >= 3, Rebates(shown));
            AssertEqual(testName, "without one, nothing of it is", 0, Rebates(gone), 0);
            AssertEqual(testName, "and everything else is", 9, Words(gone).Count, 0);

            AssertEqual(testName, "the shared column takes the rebate's 20 mm",
                At(shown, "Name").Width + 20, At(gone, "Name").Width, 0.001);
            AssertEqual(testName, "and reaches the total, with no gap where the rebate was",
                At(gone, "Total").Left, At(gone, "Name").Right, 0.001);
            AssertEqual(testName, "the total stays at the table's end", At(shown, "Total").Right, At(gone, "Total").Right, 0.001);
            AssertEqual(testName, "at its own width", 25, At(gone, "Total").Width, 0.001);
            // The rebate holds a line break of its own, so it is two lines tall at any width - at
            // none too, which is the width a Gone column has. Asked of the table's foot, a cell
            // of no width that counted anyway would have passed, being one word to a line there.
            static double[] RowHeights(List<PlacedItem> placed)
                => placed.Where(p => p.Source is TableRow).Select(p => p.Frame.Height).ToArray();
            AssertEqual(testName, "the long rebate made the first line taller, or the next comparison proves nothing", true,
                RowHeights(shown)[1] > RowHeights(shown)[2] + 3, 0);
            AssertEqual(testName, "gone, it no longer makes its line taller: both lines are one line tall",
                RowHeights(gone)[2], RowHeights(gone)[1], 0.001);

            List<PlacedItem> hidden = Bound(WithRebate, RebateTable(c => c.HideWhen = "Discount"));
            AssertEqual(testName, "HideWhen is the other way round", 0, Rebates(hidden), 0);
            AssertEqual(testName, "with the same table", At(gone, "Name").Width, At(hidden, "Name").Width, 0.001);

            TableItem template = RebateTable(c => c.ShowWhen = "Discount");
            ReportBinder.Bind(RebateSheet(template), WithoutRebate);
            AssertEqual(testName, "the template itself is left as it was", true,
                template.Columns[2].Visibility == Visibility.Visible, 0);

            // --- said once and for all, with no data at all ---------------------------------
            List<PlacedItem> unbound = Placed(RebateSheet(RebateTable(c => c.Visibility = Visibility.Gone)));
            AssertEqual(testName, "Gone on the column itself, unbound: none of it", 0, Rebates(unbound), 0);
            AssertEqual(testName, "laid out as the data would have", At(gone, "Name").Width, At(unbound, "Name").Width, 0.001);

            // --- with every width stated, nothing absorbs it -----------------------------------
            List<PlacedItem> fixedShown = Bound(WithRebate, RebateTable(c => c.ShowWhen = "Discount", shareNothing: true));
            List<PlacedItem> fixedGone = Bound(WithoutRebate, RebateTable(c => c.ShowWhen = "Discount", shareNothing: true));
            AssertEqual(testName, "every width stated, the table ends 20 mm sooner",
                At(fixedShown, "Total").Right - 20, At(fixedGone, "Total").Right, 0.001);
            AssertEqual(testName, "and the others keep theirs", 60, At(fixedGone, "Name").Width, 0.001);

            // --- Invisible keeps the room ------------------------------------------------------
            List<PlacedItem> blank = Bound(WithRebate, RebateTable(c => c.Visibility = Visibility.Invisible));
            AssertEqual(testName, "an invisible column draws none of its cells", 0, Rebates(blank), 0);
            AssertEqual(testName, "and keeps its width", At(shown, "Total").Left, At(blank, "Total").Left, 0.001);
            AssertEqual(testName, "the shared column does not take it", At(shown, "Name").Width, At(blank, "Name").Width, 0.001);
            AssertEqual(testName, "nor does a line lose the height its rebate gave it", Foot(shown), Foot(blank), 0.001);

            List<PlacedItem> both = Bound(WithoutRebate, RebateTable(c => { c.Visibility = Visibility.Invisible; c.ShowWhen = "Discount"; }));
            AssertEqual(testName, "invisible and not shown by the data is gone: the more hidden wins",
                At(gone, "Name").Width, At(both, "Name").Width, 0.001);

            // A cell keeps its own say in a column that shows. Nothing in the suite had placed a
            // cell gone on its own in a table before, so this is the first check that it stays gone.
            // Three lines tall, so that counting it anywhere would show.
            static TableItem Nameless()
            {
                TableItem table = RebateTable();
                var cell = (TextItem)table.Childs.OfType<TableRow>().Last().Childs[1];
                cell.Text = "{Name}\nsecond line\nthird line";
                cell.ShowWhen = "Nope";
                return table;
            }
            List<PlacedItem> namelessPlaced = Bound(WithRebate, Nameless());
            AssertEqual(testName, "a cell gone on its own stays gone in a column that shows", 0,
                Words(namelessPlaced).Count(w => w.Contains("Hammer") || w.Contains("Nails") || w.Contains("{Name}")), 0);
            AssertEqual(testName, "and the rest of its line is there", true, Words(namelessPlaced).Contains("9.00"), 0);
            AssertEqual(testName, "and it makes its line no taller: the more hidden of cell and column wins",
                RowHeights(shown)[2], RowHeights(namelessPlaced)[2], 0.001);

            // --- a line cut across pages is cut by what shows ----------------------------------
            // A Gone column's text, longer than anything that shows, must not decide where a line
            // too tall for a page is cut, nor how many pages it takes.
            static TableItem Long(string hidden)
            {
                var table = new TableItem();
                table.AddAutoColumn().AddColumn(20);
                table.Columns[1].Visibility = Visibility.Gone;
                table.AddHeaderRow(new TextItem { Text = "Article" }, new TextItem { Text = "Hidden" });
                table.AddRow(new TextItem { Text = string.Join("\n", Enumerable.Range(1, 120).Select(i => $"Line {i}")) },
                             new TextItem { Text = hidden });
                return table;
            }
            int Pages(TableItem table) => RebateSheet(table).Resolve(measurer).Pages.Count;
            int alone = Pages(Long(""));
            AssertEqual(testName, "a line of 120 lines goes over pages, or this proves nothing", true, alone >= 2, alone);
            AssertEqual(testName, "and a Gone column of 400 lines beside it changes nothing", alone,
                Pages(Long(string.Join("\n", Enumerable.Range(1, 400).Select(i => $"Hidden {i}")))), 0);

            // --- read where the table stands -----------------------------------------------
            var invoices = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Invoices" };
            invoices.Add(RebateTable(c => c.ShowWhen = "Discount"));
            List<PlacedItem> each = Bound("""
                { "Invoices": [
                    { "Discount": true,  "Lines": [ { "Ref": "a1", "Name": "Hammer", "Rebate": "-10%", "Total": "9.00" } ] },
                    { "Discount": false, "Lines": [ { "Ref": "b1", "Name": "Saw", "Rebate": "-3%", "Total": "20.00" } ] } ] }
                """, invoices);
            AssertEqual(testName, "in a list of invoices, each table asks its own invoice", "Rebate | -10%",
                string.Join(" | ", Words(each).Where(w => w is "Rebate" or "-10%" or "-3%")), 0);

            AssertEqual(testName, "and no line decides it: a field of the entries is not read for a column", 0,
                Rebates(Bound(WithRebate, RebateTable(c => c.ShowWhen = "Rebate"))), 0);

            // --- the grid ---------------------------------------------------------------
            static int Rows(List<PlacedItem> placed) => placed.Count(p => p.Source is TableRow);
            static int Boxes(List<PlacedItem> placed) => placed.Where(p => p.Source is TableRow).Sum(p => p.GridEdges!.Length - 1);
            AssertEqual(testName, "shown, the grid strokes four boxes a row", 4 * Rows(shown), Boxes(shown), 0);
            AssertEqual(testName, "gone, three", 3 * Rows(gone), Boxes(gone), 0);
            AssertEqual(testName, "and none of them over another", true,
                gone.Where(p => p.Source is TableRow).All(p => p.GridEdges!.Zip(p.GridEdges!.Skip(1)).All(e => e.Second > e.First)), 0);
            AssertEqual(testName, "invisible, the grid still strokes its box", 4 * Rows(blank), Boxes(blank), 0);

            int Rectangles(string data, TableItem table)
            {
                var svg = new SvgBackend(measurer);
                ReportRenderer.Render(ReportBinder.Bind(RebateSheet(table), data), svg);
                return System.Text.RegularExpressions.Regex.Matches(string.Concat(svg.Pages), "<rect").Count;
            }
            AssertEqual(testName, "and the proof draws one rectangle fewer a row", Rows(gone),
                Rectangles(WithRebate, RebateTable(c => c.ShowWhen = "Discount"))
                - Rectangles(WithoutRebate, RebateTable(c => c.ShowWhen = "Discount")), 0);

            // --- what a reader hears ----------------------------------------------------
            byte[] Pdf(string data, params BaseItem[] content)
            {
                var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Language = "en-GB", Tagged = true };
                ReportRenderer.Render(ReportBinder.Bind(RebateSheet(content), data), pdf);
                return pdf.ToArray();
            }

            Reading heardShown = ReadAloud(Pdf(WithRebate, RebateTable(c => c.ShowWhen = "Discount")));
            Reading heardGone = ReadAloud(Pdf(WithoutRebate, RebateTable(c => c.ShowWhen = "Discount")));
            Reading heardBlank = ReadAloud(Pdf(WithRebate, RebateTable(c => c.Visibility = Visibility.Invisible)));
            static string Cells(Reading r) => $"{r.Tags.Count(t => t == "TH")} TH, {r.Tags.Count(t => t == "TD")} TD";

            Console.WriteLine($"    tagged: shown {Cells(heardShown)}, gone {Cells(heardGone)}, invisible {Cells(heardBlank)}");
            AssertEqual(testName, "shown, a reader is given four headers and four cells a line", "4 TH, 8 TD", Cells(heardShown), 0);
            AssertEqual(testName, "gone, three and three", "3 TH, 6 TD", Cells(heardGone), 0);
            AssertEqual(testName, "invisible, three and three too: there is nothing to read in it", "3 TH, 6 TD", Cells(heardBlank), 0);
            AssertEqual(testName, "and never the rebate", false,
                heardGone.Heard.Concat(heardBlank.Heard).Any(h => h.Words.Contains("Rebate", StringComparison.Ordinal)
                                                                 || h.Words.Contains('%')), 0);
            AssertEqual(testName, "with the tree and the pages reaching each other", "",
                string.Join(" | ", heardShown.Faults.Concat(heardGone.Faults).Concat(heardBlank.Faults)), 0);

            // The row standing in for an empty list spans the columns that show.
            string Spans(string data, Action<TableColumn> rebate)
            {
                string pdf = System.Text.Encoding.Latin1.GetString(Unpacked(Pdf(data, RebateTable(rebate, empty: "Nothing for {Client}"))));
                return string.Join(",", System.Text.RegularExpressions.Regex.Matches(pdf, @"/ColSpan (\d+)").Select(m => m.Groups[1].Value));
            }
            const string none = """{ "Client": "ACME", "Discount": false, "Lines": [] }""";
            AssertEqual(testName, "an empty list's message spans the four columns that show", "4", Spans(none, c => { }), 0);
            AssertEqual(testName, "three when one is gone", "3", Spans(none, c => c.ShowWhen = "Discount"), 0);
            AssertEqual(testName, "and three when one keeps its room and says nothing", "3", Spans(none, c => c.Visibility = Visibility.Invisible), 0);

            // --- the mail ---------------------------------------------------------------
            string Mail(string data, TableItem table) => FlowingHtml.Write(ReportBinder.Bind(RebateSheet(table), data));
            // The cells of the table, and not those the message is itself laid out in.
            static int Count(string html, string tag)
                => System.Text.RegularExpressions.Regex.Matches(html, "<" + tag + @"( colspan=""\d+"")? style=""padding:\.3em \.5em").Count;
            static string Shape(string html) => $"{Count(html, "th")} th, {Count(html, "td")} td";

            string mailShown = Mail(WithRebate, RebateTable(c => c.ShowWhen = "Discount"));
            string mailGone = Mail(WithoutRebate, RebateTable(c => c.ShowWhen = "Discount"));
            string mailBlank = Mail(WithRebate, RebateTable(c => c.Visibility = Visibility.Invisible));
            AssertEqual(testName, "the mail writes the column when it shows", "4 th, 8 td", Shape(mailShown), 0);
            AssertEqual(testName, "and none of it when it is gone", "3 th, 6 td", Shape(mailGone), 0);
            AssertEqual(testName, "nor when it is invisible: a message keeps no room", "3 th, 6 td", Shape(mailBlank), 0);
            AssertEqual(testName, "and never its words", false,
                new[] { "Rebate", "-10%", "-5%" }.Any(w => (mailGone + mailBlank).Contains(w, StringComparison.Ordinal)), 0);
            AssertEqual(testName, "the empty list's message spans three", true,
                Mail(none, RebateTable(c => c.ShowWhen = "Discount", empty: "Nothing for {Client}"))
                    .Contains("colspan=\"3\"", StringComparison.Ordinal), 0);

            // A cell hidden on its own keeps its place in the mail, and says nothing. It was written
            // there while the page left it out - and with its token as typed, since the binder fills
            // nothing that is not going to appear: "{Name}", never "Hammer".
            string mailNameless = Mail(WithRebate, Nameless());
            AssertEqual(testName, "a cell gone on its own is an empty cell in the mail", 8, Count(mailNameless, "td"), 0);
            AssertEqual(testName, "with none of its words, nor its token", false,
                mailNameless.Contains("Hammer", StringComparison.Ordinal) || mailNameless.Contains("{Name}", StringComparison.Ordinal), 0);

            // --- the file -------------------------------------------------------------------
            string json = RebateSheet(RebateTable(c => { c.Visibility = Visibility.Invisible; c.ShowWhen = "Discount"; c.HideWhen = "Waived"; })).ToJson();
            System.Text.Json.Nodes.JsonNode columns = System.Text.Json.Nodes.JsonNode.Parse(json)!["Sections"]![0]!["Childs"]!.AsArray()
                .First(child => (string?)child!["$type"] == "Body")!["Childs"]![0]!["Columns"]!;
            AssertEqual(testName, "the file names the visibility", "Invisible", (string?)columns[2]!["Visibility"] ?? "", 0);
            AssertEqual(testName, "and holds both paths", "Discount/Waived",
                $"{(string?)columns[2]!["ShowWhen"]}/{(string?)columns[2]!["HideWhen"]}", 0);
            AssertEqual(testName, "a column that shows says nothing about it", false, ((System.Text.Json.Nodes.JsonObject)columns[0]!).ContainsKey("Visibility"), 0);

            TableColumn back = Report.FromJson(json).AllItems.OfType<TableItem>().Single().Columns[2];
            AssertEqual(testName, "and reads back as it was written", "Invisible Discount Waived",
                $"{back.Visibility} {back.ShowWhen} {back.HideWhen}", 0);
            TableColumn copied = ((TableItem)RebateTable(c => c.HideWhen = "Waived").Clone()).Columns[2];
            AssertEqual(testName, "a copied table keeps it", "Waived", copied.HideWhen ?? "", 0);

            // --- and the host's catalogue -----------------------------------------------------
            const string catalogue = """[ "Client", "Discount", { "path": "Lines", "type": "list", "fields": [ "Ref", "Name", "Rebate", "Total" ] } ]""";
            List<TemplateIssue> Unknown(string path)
                => TemplateFields.Check(RebateSheet(RebateTable(c => c.ShowWhen = path)), catalogue).Where(i => i.Rule == "fields.unknown").ToList();

            AssertEqual(testName, "a path the data answers has nothing said of it", 0, Unknown("Discount").Count, 0);
            List<TemplateIssue> misspelt = Unknown("Discont");
            AssertEqual(testName, "a misspelt one is found, at the column", true,
                misspelt.Count == 1 && misspelt[0].Pointer?.EndsWith("/Columns/2/ShowWhen", StringComparison.Ordinal) == true, 0);
            AssertEqual(testName, "with the name meant", true, misspelt[0].Message.Contains("\"Discount\"", StringComparison.Ordinal), 0);
            AssertEqual(testName, "and a field of the entries is one too: a column is read outside the list", 1, Unknown("Rebate").Count, 0);

            // --- what the designer's echo is held to ------------------------------------------
            // Where the grid falls and which headers are drawn, for each way a column can show:
            // the harness lays the same template out with the same data and has to agree.
            var cases = new System.Text.Json.Nodes.JsonArray();
            void Case(string name, TableItem table, string? data)
            {
                Report report = RebateSheet(table);
                string written = report.ToJson();
                List<PlacedItem> placed = Placed(data == null ? report : ReportBinder.Bind(report, data));
                PlacedItem first = placed.First(p => p.Source is TableRow);
                var header = placed.Where(p => p.Source is TextItem && ReferenceEquals(p.Source.Parent, first.Source))
                                   .OrderBy(p => p.Frame.Left).Select(p => (System.Text.Json.Nodes.JsonNode?)TextOf(p));
                cases.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["name"] = name,
                    ["report"] = System.Text.Json.Nodes.JsonNode.Parse(written),
                    ["data"] = data,
                    ["edges"] = new System.Text.Json.Nodes.JsonArray(first.GridEdges!.Select(e => (System.Text.Json.Nodes.JsonNode?)Math.Round(e, 4)).ToArray()),
                    ["header"] = new System.Text.Json.Nodes.JsonArray(header.ToArray()),
                });
            }

            Case("shown by the data", RebateTable(c => c.ShowWhen = "Discount"), WithRebate);
            Case("gone by the data", RebateTable(c => c.ShowWhen = "Discount"), WithoutRebate);
            Case("gone by HideWhen", RebateTable(c => c.HideWhen = "Discount"), WithRebate);
            Case("gone on its own", RebateTable(c => c.Visibility = Visibility.Gone), null);
            Case("invisible", RebateTable(c => c.Visibility = Visibility.Invisible), null);
            Case("every width stated, gone", RebateTable(c => c.ShowWhen = "Discount", shareNothing: true), WithoutRebate);
            Case("a condition and no data to read it", RebateTable(c => c.ShowWhen = "Discount"), null);
            Case("invisible, and not shown by the data", RebateTable(c => { c.Visibility = Visibility.Invisible; c.ShowWhen = "Discount"; }), WithoutRebate);
            TableItem firstGone = RebateTable();
            firstGone.Columns[0].Visibility = Visibility.Gone;
            Case("the first column gone", firstGone, null);

            File.WriteAllText(Path.Combine(OutputDirectory("tables"), "columns.json"),
                cases.ToJsonString(new() { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
            Console.WriteLine($"    {cases.Count} tables written for the echo");

            Console.WriteLine();
        }

        /// <summary>A table with a column gone or keeping its room, and an empty list beside it, shown to the validator.</summary>
        [SkippableTheory]
        [InlineData("ua1", Visibility.Gone)]
        [InlineData("ua1", Visibility.Invisible)]
        [InlineData("3b", Visibility.Gone)]
        public void Test129_AHiddenColumnIsValid(string flavour, Visibility rebate)
        {
            string testName = $"Test 129: veraPDF {flavour}, the rebate column {rebate}";
            Console.WriteLine($"Running {testName}...");

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null, "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH.");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // The lines with the column hidden, and an empty list whose message spans what is left.
            Report report = ReportBinder.Bind(
                RebateSheet(RebateTable(c => c.Visibility = rebate),
                            RebateTable(c => c.Visibility = rebate, empty: "Nothing withheld from {Client}", list: "Withheld")),
                WithRebate);

            var pdf = new PdfBackend(library) { Tagged = true, Conformance = PdfConformance.PdfA3b, Language = "en-GB" };
            ReportRenderer.Render(report, pdf);

            string path = Path.Combine(Path.GetTempPath(), $"frisket-columns-{Guid.NewGuid():N}.pdf");
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
    }
}
