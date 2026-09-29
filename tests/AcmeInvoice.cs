namespace test
{
    /// <summary>
    /// The ACME invoice the suite is built on - its bands, its data, its bound copy - and the
    /// whole-document run over it, which also writes the numbers the designer's echo is
    /// measured against.
    ///
    /// Modelled from a photograph of a printed workshop invoice, so the structure and the
    /// proportions are faithful while the exact millimetres are not: a skewed photo cannot
    /// give those. Every value the section would carry is a {token}; the sample values are
    /// invented and only the shape is copied.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>A text pinned to the right edge of its band.</summary>
        private static TextItem AtRight(double right, double top, string text, double rem = 1, bool bold = false)
        {
            TextItem item = T(text, rem, bold, HorizontalAlignment.Right);
            item.Style.Location.Right = (right, Units.Millimeter);
            item.Style.Location.Top = (top, Units.Millimeter);
            return item;
        }

        private static ContainerItem Band(double height, LayoutMode layout = LayoutMode.Absolute)
        {
            var band = new ContainerItem { Layout = layout };
            band.Style.Location.Height = (height, Units.Millimeter);
            band.Style.Location.Dock = Dock.Left | Dock.Right;
            return band;
        }

        /// <summary>
        /// The logo: a sphere paved with tiles, as on the printed original. Drawn rather
        /// than lifted from the scan - a skewed photograph makes a poor logo - and
        /// produced as an 8-bit RGB PNG on white, which is the shape both backends carry
        /// through without decoding a single pixel.
        /// </summary>
        private static byte[] AcmeSphere(int size)
        {
            double radius = size * 0.46;
            double centre = size / 2.0;

            const int Meridians = 20;   // tiles around the equator
            const int Parallels = 11;   // tiles from pole to pole
            const double Grout = 0.13;  // share of a tile taken by the joint

            // Light from the upper left, as in the original.
            double lx = -0.45, ly = -0.55, lz = 0.70;
            double length = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= length; ly /= length; lz /= length;

            return PngEncoder.Rgb(size, size, (x, y) =>
            {
                double dx = x + 0.5 - centre;
                double dy = y + 0.5 - centre;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance > radius + 1)
                    return (255, 255, 255);

                // The surface normal of a sphere seen head on.
                double nx = dx / radius;
                double ny = dy / radius;
                double nz = Math.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));

                // Paving in spherical coordinates rather than on the flat image: the
                // tiles then crowd together towards the silhouette, which is what makes
                // the eye read a ball instead of a circle with a grid over it.
                double longitude = Math.Atan2(nx, nz) / Math.PI;              // -1..1
                double latitude = Math.Asin(Math.Clamp(ny, -1, 1)) / (Math.PI / 2);

                double u = (longitude + 1) * Meridians / 2;
                double v = (latitude + 1) * Parallels / 2;
                bool joint = u - Math.Floor(u) < Grout || v - Math.Floor(v) < Grout;

                double lambert = Math.Max(0, nx * lx + ny * ly + nz * lz);
                double shade = 0.14 + 0.86 * Math.Pow(lambert, 0.9);

                // A small specular, so the tiles look glazed.
                double specular = Math.Pow(lambert, 28) * 0.55;
                shade = Math.Min(1, shade + specular);

                if (joint)
                    shade *= 0.30;

                byte grey = (byte)Math.Clamp(shade * 232, 0, 255);
                byte blue = (byte)Math.Clamp(shade * 255, 0, 255);

                // Soften the rim so the circle does not look sawn out.
                double edge = Math.Clamp(radius - distance, 0, 1);
                return ((byte)(255 + (grey - 255) * edge),
                        (byte)(255 + (grey - 255) * edge),
                        (byte)(255 + (blue - 255) * edge));
            });
        }

        // ------------------------------------------------------------------
        // Header: repeated in full on every page, as French invoicing requires.
        // ------------------------------------------------------------------
        private static void BuildHeader(Header header)
        {
            // --- issuer, logo and legal identifiers --------------------------
            ContainerItem identity = Band(24);

            var logo = new ImageItem
            {
                NaturalWidth = 20,
                NaturalHeight = 20,
                Scaling = ImageScaling.Uniform,
                Source = new() { Data = AcmeSphere(320), MediaType = "image/png", SourcePath = "acme-logo.png" },
                AltText = "ACME Industries logo"
            };
            logo.Style.Location.Left = (0, Units.Millimeter);
            logo.Style.Location.Top = (0, Units.Millimeter);
            logo.Style.Location.Width = (20, Units.Millimeter);
            identity.Add(logo);

            identity.Add(At(23, 1, "{IssuerName}", 1.35, bold: true));
            identity.Add(At(23, 6.5, "{IssuerLine1}", 0.95));
            identity.Add(At(23, 10, "{IssuerLine2}", 0.95));

            // The legal identifiers sit in a labelled column on the right.
            string[] labels = { "Company no.", "VAT no.", "Registry", "Activity", "Share capital" };
            string[] values = { "{IssuerCompanyNo}", "{IssuerVat}", "{IssuerRegistry}", "{IssuerActivity}", "{IssuerCapital}" };
            for (int i = 0; i < labels.Length; i++)
            {
                identity.Add(At(112, 1 + i * 3.4, labels[i], 0.8));
                identity.Add(AtRight(0, 1 + i * 3.4, values[i], 0.8));
            }
            header.Add(identity);

            // --- the black invoice-number band -------------------------------
            ContainerItem stripe = Band(8);
            stripe.Style.Background = new() { Color = new(20, 20, 20) };
            stripe.Style.Padding = new() { Left = 3, Top = 1.6, Right = 3, Bottom = 1.6 };
            stripe.Add(At(0, 0, "Invoice {InvoiceNumber} of {InvoiceDate:dd/MM/yyyy}", 1.3, bold: true,
                          align: HorizontalAlignment.Left));
            var stripeText = (TextItem)stripe.Childs[0];
            stripeText.Font.Color = Color.White;
            stripe.Style.Margin = new() { Top = 2, Bottom = 2.5 };
            header.Add(stripe);

            // --- dossier and intervention ------------------------------------
            TextItem dossier = T("Job {Job} \\ Task {InterventionNo}: {InterventionLabel}", 1.25, bold: true);
            dossier.Style.Margin = new() { Bottom = 1.5 };
            header.Add(dossier);

            // --- the parties: issuer already above, customer here ---------------
            ContainerItem parties = Band(26);

            parties.Add(At(0, 0, "{Customer.Name}", 1.1, bold: true));
            parties.Add(At(0, 4.4, "{Customer.Line1}", 0.95));
            parties.Add(At(0, 7.8, "{Customer.Line2}", 0.95));
            parties.Add(At(0, 12.6, "Company no.: {Customer.CompanyNo}", 0.8));
            parties.Add(At(0, 15.6, "VAT no.: {Customer.Vat}", 0.8));

            parties.Add(At(112, 0, "Account", 0.8));
            parties.Add(AtRight(0, 0, "{Customer.Code}", 0.95, bold: true));
            parties.Add(At(112, 3.6, "Cost centre", 0.8));
            parties.Add(AtRight(0, 3.6, "{Charge}", 0.8));
            parties.Add(At(112, 7.2, "Job", 0.8));
            parties.Add(AtRight(0, 7.2, "{Job} / {InterventionLabel}", 0.8));
            header.Add(parties);

            // --- the vehicle, right across the page -----------------------------
            // It reads across, not down: one line of column titles and one line of
            // values under it, as on the printed original. A table gives the columns
            // without a single coordinate being written out, and it now spans the whole
            // width, between the addresses and the delivery notice.
            TextItem vehicleTitle = T("Equipment", 0.85, bold: true);
            vehicleTitle.Style.Margin = new() { Bottom = 0.6 };
            header.Add(vehicleTitle);

            var vehicle = new TableItem { Grid = null, RepeatHeader = false };
            vehicle.Style.Location.Dock = Dock.Left | Dock.Right;
            vehicle.Style.Background = new() { Color = new(246, 246, 246) };
            vehicle.Style.Padding = new() { Left = 1.5, Top = 1, Right = 1.5, Bottom = 1 };
            vehicle.AddColumn(28);
            vehicle.AddColumn(26);
            vehicle.AddColumn(22);
            vehicle.AddAutoColumn();          // the chassis number, the longest of them
            vehicle.AddColumn(26);
            vehicle.AddColumn(28);

            TableRow vehicleHead = vehicle.AddHeaderRow(
                T("Asset tag", 0.75), T("Make", 0.75), T("Model", 0.75),
                T("Serial no.", 0.75), T("In service", 0.75), T("Hours run", 0.75));
            vehicleHead.Style.Padding = new() { Bottom = 0.6 };

            TableRow vehicleRow = vehicle.AddRow(
                T("{Vehicle.Plate}", 0.95, bold: true), T("{Vehicle.Make}", 0.95),
                T("{Vehicle.Model}", 0.95), T("{Vehicle.Vin}", 0.85),
                T("{Vehicle.FirstRegistration:dd/MM/yyyy}", 0.85),
                T("{Vehicle.Mileage:#,##0} km", 0.85));
            vehicleRow.Style.Padding = new() { Top = 0.4 };
            vehicle.Style.Margin = new() { Bottom = 1.5 };

            header.Add(vehicle);

            // --- section title, just above the lines --------------------------
            TextItem section = T("DELIVERY OF GOODS", 0.85, bold: true);
            section.Style.Margin = new() { Top = 1, Bottom = 1 };
            header.Add(section);

            header.Add(Rule(0.3));
        }

        // ------------------------------------------------------------------
        // Body: the priced lines. The column titles live in the table, not in the
        // page header, so RepeatHeader carries them onto every page - the same
        // result as the original, expressed where it belongs.
        // ------------------------------------------------------------------
        private static void BuildBody(Body body)
        {
            var table = new TableItem
            {
                RepeatHeader = true,
                Grid = new() { Width = 0.1, Color = new(205, 205, 205) }
            };
            table.Style.Margin = new() { Top = 1 };

            table.AddColumn(8);                       // T.V.
            table.AddColumn(26);                      // Reference
            table.AddAutoColumn();                    // Description
            table.AddColumn(15);                      // Qty.
            table.AddColumn(19);                      // Prix Unit.
            table.AddColumn(13);                      // %Rem.
            table.AddColumn(18);                      // €Rem.
            table.AddColumn(19);                      // P.U. Net
            table.AddColumn(21);                      // Total H.T.

            const HorizontalAlignment R = HorizontalAlignment.Right;

            TableRow head = table.AddHeaderRow(
                T("T.V.", 0.8, true), T("Reference", 0.8, true), T("Description", 0.8, true),
                T("Qty", 0.8, true, R), T("Unit price", 0.8, true, R), T("Disc. %", 0.8, true, R),
                T("Disc.", 0.8, true, R), T("Net price", 0.8, true, R), T("Net total", 0.8, true, R));
            head.Style.Background = new() { Color = new(235, 235, 235) };
            head.Style.Padding = new() { Left = 1.2, Top = 1.1, Right = 1.2, Bottom = 1.1 };

            // One prototype row, stamped out once per record. A second prototype would
            // alternate with it, banding the rows.
            table.RepeatOver = "Items";          // the rows of the group around it
            table.EmptyText = "No lines invoiced";

            TableRow prototype = table.AddRow(
                T("{Vat}", 0.85), T("{Ref}", 0.85), T("{Label}", 0.85),
                T("{Qty:#,##0.000}", 0.85, false, R), T("{UnitPrice:#,##0.00}", 0.85, false, R),
                T("{Discount:0.00}", 0.85, false, R), T("{DiscountAmount:#,##0.00}", 0.85, false, R),
                T("{NetPrice:#,##0.00}", 0.85, false, R), T("{Total:#,##0.00}", 0.85, false, R));
            prototype.Style.Padding = new() { Left = 1.2, Top = 0.9, Right = 1.2, Bottom = 0.9 };
            prototype.Style.Splitting = Splitting.KeepTogether;

            // The lines arrive flat and are bucketed by family here, each bucket getting
            // a heading above and its subtotal below.
            var group = new ContainerItem
            {
                Layout = LayoutMode.Stack,
                RepeatOver = "Lines",
                GroupBy = "Family",
                GroupItems = "Items"
            };

            ContainerItem heading = Band(5.5);
            heading.Style.Background = new() { Color = new(246, 246, 246) };
            heading.Style.Padding = new() { Left = 1.2, Top = 1, Right = 1.2, Bottom = 0.6 };
            heading.Add(At(0, 0, "{Key}", 0.9, bold: true));
            heading.Add(AtRight(0, 0, "{Count} line(s)", 0.75));
            // Stays above its rows when the group runs onto the next page.
            heading.Style.RepeatOnBreak = true;
            group.Add(heading);

            group.Add(table);

            ContainerItem subtotal = Band(5);
            subtotal.Style.Padding = new() { Left = 1.2, Top = 0.6, Right = 1.2, Bottom = 1.4 };
            subtotal.Add(AtRight(0, 0, "Subtotal {Key}: {Sum.Total:#,##0.00}", 0.85, bold: true));
            group.Add(subtotal);

            body.Add(group);
        }

        // ------------------------------------------------------------------
        // Footer: complete on the last page, a centred notice on the others.
        // No explicit height - the band sizes itself from the taller variant.
        // ------------------------------------------------------------------
        private static void BuildFooter(Footer footer)
        {
            // --- the variant carried by every page but the last ---------------
            TextItem notice = T("TOTALS ON THE LAST PAGE", 1, true, HorizontalAlignment.Center);
            notice.Style.PageScope = PageScope.ExceptLast;
            notice.Style.Margin = new() { Top = 3, Bottom = 3 };
            footer.Add(notice);

            // --- the full summary, last page only -----------------------------
            var summary = new ContainerItem { Layout = LayoutMode.Stack };
            summary.Style.PageScope = PageScope.Last;

            summary.Add(Rule(0.3));

            // Labour summary, then VAT summary, side by side with the totals. The word
            // is the one printed above them: the code used to call these recaps while the
            // page called them summaries, which is one vocabulary too many for two lines
            // of the same block.
            //
            // The four figures typed here are typed, not bound - they stand in for a
            // breakdown the sample data does not carry. They used to read "22 545.60":
            // French grouping with an English decimal point, a form belonging to no
            // locale at all, sitting beside bound totals that were properly French. Three
            // conventions in one block, none of them chosen.
            ContainerItem summaries = Band(30);

            summaries.Add(At(0, 0, "Labour and parts summary", 0.85, bold: true));
            summaries.Add(At(0, 3.6, "Code", 0.75));
            summaries.Add(At(26, 3.6, "Time", 0.75));
            summaries.Add(At(44, 3.6, "Rate", 0.75));
            summaries.Add(At(62, 3.6, "Amount", 0.75));
            summaries.Add(At(0, 7, "Servicing - S1", 0.8));
            summaries.Add(At(26, 7, "915.00", 0.8));
            summaries.Add(At(44, 7, "28.00", 0.8));
            summaries.Add(At(62, 7, "22,545.60", 0.8));
            summaries.Add(At(0, 10.4, "Servicing - S2", 0.8));
            summaries.Add(At(26, 10.4, "2,458.00", 0.8));
            summaries.Add(At(44, 10.4, "34.00", 0.8));
            summaries.Add(At(62, 10.4, "82,736.28", 0.8));

            summaries.Add(At(0, 15.5, "VAT summary", 0.85, bold: true));
            summaries.Add(At(0, 19, "Base", 0.75));
            summaries.Add(At(26, 19, "Rate", 0.75));
            summaries.Add(At(44, 19, "Amount", 0.75));
            summaries.Add(At(0, 22.4, "{Totals.Ht:#,##0.00}", 0.8));
            summaries.Add(At(26, 22.4, "20.00", 0.8));
            summaries.Add(At(44, 22.4, "{Totals.Vat:#,##0.00}", 0.8));

            // Totals column, right hand side.
            summaries.Add(At(120, 0, "Totals", 0.85, bold: true));
            summaries.Add(At(120, 4, "Net total", 0.85));
            summaries.Add(AtRight(0, 4, "{Totals.Ht:#,##0.00}", 0.85, bold: true));
            summaries.Add(At(120, 7.8, "Total VAT", 0.85));
            summaries.Add(AtRight(0, 7.8, "{Totals.Vat:#,##0.00}", 0.85, bold: true));
            summaries.Add(At(120, 11.6, "Gross total", 0.85));
            summaries.Add(AtRight(0, 11.6, "{Totals.Ttc:#,##0.00}", 0.85, bold: true));

            summaries.Add(At(120, 17, "Discounted net total", 0.8));
            summaries.Add(AtRight(0, 17, "{Totals.DiscountedHt:#,##0.00}", 0.8));
            summaries.Add(At(120, 20.6, "Payment", 0.8));
            summaries.Add(AtRight(0, 20.6, "{Payment.Method}", 0.8));
            summaries.Add(At(120, 24.2, "Due", 0.8));
            summaries.Add(AtRight(0, 24.2, "{Payment.DueDate:dd/MM/yyyy}", 0.8));
            summary.Add(summaries);

            // Net to pay, the one figure a reader looks for.
            ContainerItem net = Band(9);
            net.Style.Background = new() { Color = new(235, 235, 235) };
            net.Style.Padding = new() { Left = 3, Top = 2, Right = 3, Bottom = 2 };
            net.Add(At(0, 0, "AMOUNT DUE", 1.15, bold: true));
            net.Add(AtRight(0, 0, "{Totals.Ttc:#,##0.00} EUR", 1.35, bold: true));
            net.Style.Margin = new() { Top = 1.5, Bottom = 1.5 };
            summary.Add(net);

            footer.Add(summary);

            // --- carried by every page ----------------------------------------
            // Invented, and deliberately not a citation. What was here quoted article
            // L441-6 of the French commercial code - real law, in a sample section
            // that is meant to belong to no country. A reader who recognises it reads
            // the engine as French; a reader who does not is being shown a rule that
            // may not apply to them. Neither is what a specimen is for.
            TextItem legal = T(
                "Where payment is late, the supplier may charge interest on the overdue amount at the " +
                "rate set out in the agreed terms, together with a fixed sum towards the cost of recovery. " +
                "This notice is illustrative and carries no legal effect.",
                0.7);
            legal.Font.Color = new(110, 110, 110);
            legal.Style.Margin = new() { Top = 1 };
            footer.Add(legal);

            ContainerItem line = Band(4.5);
            line.Add(At(0, 0, "{Version}  -  {Date} at {Time}", 0.7));
            line.Add(AtRight(0, 0, "Page {Page} of {Pages}", 0.75, bold: true));
            ((TextItem)line.Childs[0]).Font.Color = new(110, 110, 110);
            footer.Add(line);
        }

        internal static Report BuildAcmeInvoice()
        {
            var report = new Report
            {
                Title = "Invoice {InvoiceNumber}",
                Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                RootFontSize = 1.9,                       // cap height of the body text
                GeneratedAt = new DateTimeOffset(2025, 10, 2, 13, 44, 41, TimeSpan.Zero),
                Header = new() { Layout = LayoutMode.Stack },
                Body = new(),
                Footer = new() { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new() { Left = 9, Top = 8, Right = 9, Bottom = 6 };

            // Named once. Change this line and the whole invoice changes typeface.
            report.DefaultFont = new() { Family = DmsFont };

            BuildHeader(report.Header!);
            BuildBody(report.Body!);
            BuildFooter(report.Footer!);

            return report;
        }

        /// <summary>
        /// The other half of the pair: a data section, in its own file in real use.
        /// Values keep their JSON types - the template decides how they are shown.
        /// </summary>
        internal static string AcmeDataJson(int lines)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("""
            {
              "IssuerName": "ACME Industries",
              "IssuerLine1": "12 Workshop Lane",
              "IssuerLine2": "00001 Newtown",
              "IssuerCompanyNo": "000 000 000 00001",
              "IssuerVat": "XX00 000 000 001",
              "IssuerRegistry": "Newtown B000 00001",
              "IssuerActivity": "0000A",
              "IssuerCapital": "1,000,000.00 EUR",

              "InvoiceNumber": "No. 33250200001",
              "InvoiceDate": "2025-10-02",
              "Job": "ATEL-001",
              "InterventionNo": "01",
              "InterventionLabel": "Servicing",

              "Vehicle": {
                "Plate": "AAA-0000",
                "Make": "ACME",
                "Model": "Type 20",
                "Vin": "ZZ0ACME000000001",
                "FirstRegistration": "2010-10-27",
                "Mileage": 148210
              },

              "Customer": {
                "Code": "411ACM001",
                "Name": "ACME Logistics",
                "Line1": "5 Mill Road",
                "Line2": "00002 Oldford",
                "CompanyNo": "000 000 000 00002",
                "Vat": "XX00 000 000 002"
              },

              "Charge": "Default cost centre",
              "Totals": { "Ht": 11876769.42, "Vat": 2375353.88, "Ttc": 14252123.30, "DiscountedHt": 9160758.58 },
              "Payment": { "Method": "Cheque", "DueDate": "2025-10-02" },
              "Version": "ACME 2.1.0.42",

              "Lines": [
            """);

            for (int i = 1; i <= lines; i++)
            {
                sb.Append(i > 1 ? ",\n" : "\n").Append(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    // "recap 2024" until now, in the Reference column of every line. It
                    // is a de-accented "recap" - the French short for a summary - and as a
                    // line reference it says nothing in either language. A-0001 is the
                    // shape references take everywhere else in this suite.
                    """    {{ "Family": "{3}", "Vat": "A", "Ref": "A-{0:0000}", "Label": "{4}", "Qty": {1}, "UnitPrice": 312.00, "Discount": 42.00, "DiscountAmount": 980310.24, "NetPrice": 180.96, "Total": {2} }}""",
                    i, 1000 + i * 37, 19.90 * i,
                    i % 3 == 0 ? "Labour" : "Parts",
                    i % 3 == 0 ? "fixed price" : "parts"));
            }

            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>The template filled from the data, which is what actually gets printed.</summary>
        internal static Report BoundAcmeInvoice(int lines)
            => ReportBinder.Bind(BuildAcmeInvoice(), AcmeDataJson(lines));

        static TableItem LinesTable(Report report) => Descend(report.Body!).OfType<TableItem>().First();

        // ------------------------------------------------------------------

        [Fact]
        public void Test23_AcmeInvoice()
        {
            var testName = "Test 23: ACME invoice Template";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            Report report = BoundAcmeInvoice(12);

            var backend = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            LayoutResult layout = ReportRenderer.Render(report, backend);
            Page first = layout.Pages[0];

            AssertEqual(testName, "fits on one page", 1, layout.PageCount, 0);

            // The bands sized themselves; nothing states a height for them.
            AssertEqual(testName, "header sized itself", true,
                first.HeaderBand.Height is > 60 and < 110, 0);
            AssertEqual(testName, "footer sized itself", true,
                first.FooterBand.Height is > 30 and < 90, 0);
            AssertEqual(testName, "body band is what is left", true,
                first.BodyBand.Top >= first.HeaderBand.Bottom - 0.01
                && first.BodyBand.Bottom <= first.FooterBand.Top + 0.01, 0);
            AssertEqual(testName, "nothing runs off the page", true,
                first.Items.All(p => double.IsNaN(p.Frame.Bottom) || p.Frame.Bottom <= 297.01), 0);

            // The nine columns fill the printable width exactly.
            TableItem table = LinesTable(report);
            var headRow = (TableRow)table.Childs[0];
            var cells = headRow.Childs
                .Select(c => first.Items.First(p => ReferenceEquals(p.Source, c)).Frame).ToList();
            AssertEqual(testName, "nine columns", 9, cells.Count, 0);
            AssertEqual(testName, "table starts at the left margin", 9, cells[0].Left - 1.2, 0.01);
            AssertEqual(testName, "table ends at the right margin", 201, cells[8].Right + 1.2, 0.01);

            // Tokens resolved through the sample data.
            string svg = backend.Pages[0];

            // Not "these particular ones resolved" but "none was left behind". A token
            // whose data key is renamed goes on rendering as literal braces, on every
            // page, and every assertion below still passes because it asks about the
            // tokens that do work. {Dossier} survived that way for a whole afternoon,
            // fifty-two times across the samples, until it was noticed by eye.
            var leftover = System.Text.RegularExpressions.Regex
                .Matches(string.Join("\n", backend.Pages), @"\{[A-Za-z][A-Za-z0-9_.]*\}")
                .Select(m => m.Value).Distinct().ToList();
            AssertEqual(testName, $"no token left unresolved ({string.Join(", ", leftover)})",
                0, leftover.Count, 0);

            AssertEqual(testName, "issuer resolved", true, svg.Contains("ACME Industries"), 0);
            AssertEqual(testName, "customer resolved", true, svg.Contains("ACME Logistics"), 0);
            AssertEqual(testName, "invoice number resolved", true,
                svg.Contains("Invoice No. 33250200001 of 02/10/2025"), 0);
            AssertEqual(testName, "date formatted by the template", true, svg.Contains("27/10/2010"), 0);
            AssertEqual(testName, "net to pay resolved, in French notation", true,
                svg.Contains("14,252,123.30 EUR"), 0);
            AssertEqual(testName, "page counter resolved", true, svg.Contains("Page 1 of 1"), 0);
            AssertEqual(testName, "logo embedded in the preview", true,
                svg.Contains("<image") && svg.Contains("data:image/png;base64,"), 0);
            AssertEqual(testName, "no token left unresolved", false, svg.Contains("{Issuer"), 0);

            // Single page, so the last-page variant shows and the notice does not.
            AssertEqual(testName, "totals shown on a one-page invoice", true, svg.Contains("AMOUNT DUE"), 0);
            AssertEqual(testName, "notice hidden on a one-page invoice", false,
                svg.Contains("TOTALS ON THE LAST PAGE"), 0);

            // Enough lines to spill over, and the French pattern must hold.
            var many = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            LayoutResult spread = ReportRenderer.Render(BoundAcmeInvoice(60), many);

            AssertEqual(testName, "spills onto several pages", true, spread.PageCount > 1, 0);
            AssertEqual(testName, "header repeated in full", true,
                many.Pages.All(p => p.Contains("ACME Industries") && p.Contains("ACME Logistics")), 0);
            // Every page that carries rows carries the titles above them. A page holding
            // only the closing totals has no table on it and needs none.
            //
            // What this does *not* prove is RepeatHeader, whatever it looks like. This
            // invoice groups its lines, and every page here happens to open a new group,
            // which brings its own header row with it - so the titles would be on page two
            // with RepeatHeader off. Measured: turning it off changes this document not at
            // all. Test 39 is where the property is isolated, on an ungrouped table, and
            // the same sabotage there drops the count from 2 to 1.
            //
            // The marker opens with the angle bracket on purpose: it has to match the
            // start of a text node, not a substring of one. "A-0" alone also matches the
            // equipment plate "AAA-0000", which the header repeats on every page - so it
            // found a row on the totals page, where there is no table at all.
            AssertEqual(testName, "column titles repeated wherever there are rows", true,
                many.Pages.All(p => !p.Contains(">A-0") || p.Contains("Description")), 0);
            AssertEqual(testName, "notice on the pages before the last", true,
                many.Pages.Take(many.Pages.Count - 1).All(p => p.Contains("TOTALS ON THE LAST PAGE")), 0);
            AssertEqual(testName, "no totals before the last page", false,
                many.Pages.Take(many.Pages.Count - 1).Any(p => p.Contains("AMOUNT DUE")), 0);
            AssertEqual(testName, "totals on the last page", true, many.Pages[^1].Contains("AMOUNT DUE"), 0);
            AssertEqual(testName, "footer band constant across pages", true,
                spread.Pages.All(p => Math.Abs(p.FooterBand.Height - spread.Pages[0].FooterBand.Height) < 0.001), 0);

            string dir = OutputDirectory("acme");
            string index = many.SaveTo(dir, "acme");
            File.WriteAllText(Path.Combine(dir, "acme.template.json"), BuildAcmeInvoice().ToJson());
            File.WriteAllText(Path.Combine(dir, "acme.data.json"), AcmeDataJson(12));

            // The bound report and the bands the engine computed for it, so the
            // designer's JavaScript echo can be checked against the real thing without
            // anyone having to keep a second set of numbers up to date by hand.
            Report bound = BoundAcmeInvoice(12);
            LayoutResult reference = bound.Resolve(new FontTextMeasurer(library));
            Page one = reference.Pages[0];

            File.WriteAllText(Path.Combine(dir, "acme.bound.json"), bound.ToJson());
            File.WriteAllText(Path.Combine(dir, "acme.bands.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                pageCount = reference.PageCount,
                header = new { top = one.HeaderBand.Top, bottom = one.HeaderBand.Bottom },
                body = new { top = one.BodyBand.Top, bottom = one.BodyBand.Bottom },
                footer = new { top = one.FooterBand.Top, bottom = one.FooterBand.Bottom }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine($"  🧾 {spread.PageCount} pages -> {index}");
            Console.WriteLine();
        }
    }
}
