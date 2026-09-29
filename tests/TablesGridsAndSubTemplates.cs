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
            ReportRenderer.Render(report, pdf);
            File.WriteAllBytes(Path.Combine(dir, "split-row.pdf"), pdf.ToArray());

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
            ReportRenderer.Render(bound, pdf);
            File.WriteAllBytes(Path.Combine(dir, "grid.pdf"), pdf.ToArray());

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
                ReportRenderer.Render(report, pdf);
                return System.Text.Encoding.Latin1.GetString(pdf.ToArray());
            }

            string mixed = Written(joined);
            string uniform = Written(same);

            // Kept, because a document in two languages is exactly the kind of file a
            // validator has an opinion about and this side does not. Test 53 validates
            // the conformance document; this one is here to be pointed at by hand.
            File.WriteAllBytes(Path.Combine(OutputDirectory("subtemplate"), "appended.pdf"),
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
            ReportRenderer.Render(loaded, pdf);
            File.WriteAllBytes(Path.Combine(dir, "compose.pdf"), pdf.ToArray());

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
    }
}
