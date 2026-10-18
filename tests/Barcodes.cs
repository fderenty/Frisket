namespace test
{
    /// <summary>
    /// The barcode encoders held to their published standards rather than to themselves:
    /// Reed-Solomon generator polynomials, QR format bits and free-module capacity per
    /// version, the finder, timing and alignment patterns, and Code 128 symbol widths - then
    /// the sample sheet those numbers draw.
    /// </summary>
    public partial class EngineTests
    {
        [Fact]
        public void Test38_Barcodes()
        {
            var testName = "Test 38: Barcodes";
            Console.WriteLine($"Running {testName}...");

            // The generator polynomial is where a transcription error hides, and it makes
            // a code that looks perfectly plausible and scans as nothing. This one is
            // published, so a mismatch says where the fault is rather than only that
            // there is one.
            byte[] g10 = ReedSolomon.Generator(10);
            int[] expected = { 1, 216, 194, 159, 111, 199, 94, 95, 113, 157, 193 };
            AssertEqual(testName, "the degree-10 generator matches the standard", true,
                g10.Select(b => (int)b).SequenceEqual(expected), 0);

            // The format information is fifteen bits of level and mask, protected by a
            // BCH code and then XORed with a fixed pattern. It is published, so the code
            // written here can be read back off the matrix and compared rather than
            // trusted - which is how the first copy was found written transposed, in a
            // matrix whose finders, timing and alignment were all perfect.
            var formats = new (string Level, int Mask, string Bits)[]
            {
                ("L", 0, "111011111000100"),
                ("M", 0, "101010000010010"),
                ("Q", 0, "011010101011111"),
                ("H", 0, "001011010001001"),
                ("L", 7, "110100101110110"),
                ("M", 7, "100101010100000"),
                ("Q", 7, "010101111101101"),
                ("H", 7, "000100000111011"),
            };

            foreach ((string level, int mask, string bits) in formats)
            {
                var correction = level switch
                {
                    "L" => QrErrorCorrection.Low,
                    "M" => QrErrorCorrection.Medium,
                    "Q" => QrErrorCorrection.Quartile,
                    _ => QrErrorCorrection.High
                };

                AssertEqual(testName, $"format {level}{mask} matches the standard", bits,
                    QrCode.FormatBits(correction, mask), 0);
            }

            // The count of modules left for data is fixed by the standard: eight bits per
            // codeword plus a known remainder. Reserving one module too many around the
            // format areas steals it from the data and shifts every bit after it - which
            // is a code a reader finds and cannot decode, exactly what a scanner showed.
            (int Version, int Codewords, int Remainder)[] capacity =
            {
                (1, 26, 0), (2, 44, 7), (4, 100, 7), (6, 172, 7),
                (7, 196, 0), (10, 346, 0), (14, 581, 3), (21, 1156, 4), (40, 3706, 0)
            };

            foreach ((int version, int codewords, int remainder) in capacity)
            {
                AssertEqual(testName, $"version {version} leaves the right room",
                    codewords * 8 + remainder, QrCode.FreeModules(version), 0);
            }

            // Size follows version: 21 modules at version 1, four more per version after.
            bool[,] small = QrCode.Encode("FRISKET");
            AssertEqual(testName, "a short text fits version 1", 21, small.GetLength(0), 0);
            AssertEqual(testName, "and is square", small.GetLength(0), small.GetLength(1), 0);

            // The three finders, which is what a reader looks for first.
            foreach ((int fx, int fy, string where) in new[]
                     { (0, 0, "haut gauche"), (small.GetLength(0) - 7, 0, "haut droit"),
                       (0, small.GetLength(0) - 7, "bas gauche") })
            {
                bool ring = true;
                for (int i = 0; i < 7; i++)
                    ring &= small[fx + i, fy] && small[fx + i, fy + 6]
                         && small[fx, fy + i] && small[fx + 6, fy + i];

                AssertEqual(testName, $"the {where} finder is there", true, ring, 0);
                AssertEqual(testName, $"and its ring is clear ({where})", false, small[fx + 1, fy + 1], 0);
                AssertEqual(testName, $"and its centre is dark ({where})", true, small[fx + 3, fy + 3], 0);
            }

            // The timing patterns are the ruler a reader measures the grid against: an
            // alternating row and column joining the finders. Get them wrong and every
            // module after is read one place out.
            bool timing = true;
            for (int i = 8; i < small.GetLength(0) - 8; i++)
                timing &= small[i, 6] == (i % 2 == 0) && small[6, i] == (i % 2 == 0);

            AssertEqual(testName, "the timing patterns alternate", true, timing, 0);

            // From version 2 on there is an alignment pattern: a five-by-five ring with a
            // dark centre, which is how a reader corrects for a page that is not flat.
            bool[,] bigger = QrCode.Encode("https://example.invalid/invoice/33250200001");
            AssertEqual(testName, "a longer value takes version 4", 33, bigger.GetLength(0), 0);

            bool ringOk = true, insideOk = true;
            for (int d = -2; d <= 2; d++)
            {
                ringOk &= bigger[26 + d, 24] && bigger[26 + d, 28]
                       && bigger[24, 26 + d] && bigger[28, 26 + d];
                if (d is >= -1 and <= 1 && d != 0)
                    insideOk &= !bigger[26 + d, 25] && !bigger[26 + d, 27];
            }

            AssertEqual(testName, "the alignment ring is closed", true, ringOk, 0);
            AssertEqual(testName, "its ring is clear", true, insideOk, 0);
            AssertEqual(testName, "and its centre is dark", true, bigger[26, 26], 0);

            // Longer text takes a bigger version, and stronger correction takes more room
            // for the same text.
            bool[,] longer = QrCode.Encode(new string('A', 200));
            AssertEqual(testName, "a long text grows the code", true,
                longer.GetLength(0) > small.GetLength(0), 0);

            bool[,] robust = QrCode.Encode(new string('A', 200), QrErrorCorrection.High);
            AssertEqual(testName, "stronger correction costs room", true,
                robust.GetLength(0) > longer.GetLength(0), 0);

            // Code 128. Every symbol totals eleven modules and the stop thirteen, which
            // the encoder checks on first use: one wrong digit in the table is invisible
            // by inspection and fatal at the scanner.
            int[] bars = Code128.Encode("ATEL-001");
            AssertEqual(testName, "code 128 produces bars", true, bars.Length > 20, 0);
            AssertEqual(testName, "every run is one to four modules", true,
                bars.All(w => w is >= 1 and <= 4), 0);

            // Start, data, check and stop: the total is a whole number of symbols of
            // eleven modules, plus the stop's thirteen.
            AssertEqual(testName, "the widths add up to whole symbols", 0,
                (bars.Sum() - 13) % 11, 0);

            // Set C carries two digits per symbol, so the same count of characters comes
            // out visibly shorter as digits than as letters.
            AssertEqual(testName, "digits pack tighter than letters", true,
                Code128.Encode("12345678").Sum() < Code128.Encode("ABCDEFGH").Sum(), 0);

            // --- on a page ---------------------------------------------------------
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // Language declared, and every word on the page in it. The label used to be
            // French on a sheet that said nothing about its language, which a reader
            // would have announced in whatever voice it happened to have.
            var sheet = new Report
            {
                Title = "Barcodes",
                Language = "en",
                Dimension = new() { Width = 210, Height = 297 }
            };
            var body = new Body { Layout = LayoutMode.Absolute };
            body.Style.Location.Dock = Dock.Fill;
            sheet.Body = body;

            // A sample nobody can read is a sample nobody can check. What each code is,
            // and what it should do when scanned - the same reasoning as the conformance
            // document, which is the only one that had it.
            var heading = new TextItem
            {
                Text = "Barcodes",
                Font = new() { Size = 1.6, Bold = true },
                Role = PdfRole.Heading1
            };
            heading.Style.Location.Left = (15, Units.Millimeter);
            heading.Style.Location.Top = (14, Units.Millimeter);
            body.Add(heading);

            var rule = new LineItem { Pen = new PenStyle { Width = 0.2, Color = new Color(150, 150, 150) } };
            rule.Style.Location.Left = (15, Units.Millimeter);
            rule.Style.Location.Top = (21, Units.Millimeter);
            rule.Style.Location.Width = (180, Units.Millimeter);
            body.Add(rule);

            var lead = new TextItem
            {
                Text = "Drawn by the engine rather than embedded as pictures. Scan both: "
                     + "each should give back exactly the value printed beside it.",
                Font = new() { Size = 0.95, Italic = true, Color = new Color(85, 85, 85) }
            };
            lead.Style.Location.Left = (15, Units.Millimeter);
            lead.Style.Location.Top = (24, Units.Millimeter);
            lead.Style.Location.Width = (180, Units.Millimeter);
            body.Add(lead);

            var qrCaption = new TextItem
            {
                Text = "QR · https://example.invalid/invoice/33250200001",
                Font = new() { Size = 0.9, Bold = true }
            };
            qrCaption.Style.Location.Left = (15, Units.Millimeter);
            qrCaption.Style.Location.Top = (34, Units.Millimeter);
            body.Add(qrCaption);

            // Alternative text, because a barcode is a figure: without it PDF/UA refuses
            // the file, and the engine's own validator says so as sound.emptyBarcode.
            // Missing here, in the one sample whose whole subject is barcodes.
            var qr = new BarcodeItem
            {
                Value = "https://example.invalid/invoice/33250200001",
                AltText = "QR code linking to invoice 33250200001"
            };
            qr.Style.Location.Left = (15, Units.Millimeter);
            qr.Style.Location.Top = (40, Units.Millimeter);
            qr.Style.Location.Width = (45, Units.Millimeter);
            qr.Style.Location.Height = (45, Units.Millimeter);
            body.Add(qr);

            var linearCaption = new TextItem
            {
                Text = "Code 128 · ATEL-001, with the value printed under the bars",
                Font = new() { Size = 0.9, Bold = true }
            };
            linearCaption.Style.Location.Left = (15, Units.Millimeter);
            linearCaption.Style.Location.Top = (94, Units.Millimeter);
            body.Add(linearCaption);

            var linear = new BarcodeItem
            {
                Value = "ATEL-001",
                Symbology = Symbology.Code128,
                AltText = "Code 128 of the reference ATEL-001"
            };
            linear.Style.Location.Left = (15, Units.Millimeter);
            linear.Style.Location.Top = (100, Units.Millimeter);
            linear.Style.Location.Width = (90, Units.Millimeter);
            linear.Style.Location.Height = (22, Units.Millimeter);
            body.Add(linear);

            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(sheet, svg);

            AssertEqual(testName, "the value is printed under the bars", true,
                svg.Pages[0].Contains(">ATEL-001<"), 0);

            // The sample has to survive the rules this project holds everyone else to.
            // It did not: two barcodes with no alternative text, in the one document
            // whose whole subject is barcodes.
            IReadOnlyList<TemplateIssue> mine = TemplateValidator.Check(sheet, TemplateProfile.All);
            foreach (TemplateIssue issue in mine)
                Console.WriteLine($"    {issue.Level,-7} {issue.Rule} {issue.Where}");
            AssertEqual(testName, "and the sample itself is faultless", 0, mine.Count,
                0);

            int rects = CountOccurrences(svg.Pages[0], "<rect");
            Console.WriteLine($"    {rects} rectangles dessines");
            AssertEqual(testName, "both codes reached the page", true, rects > 200, 0);

            string dir = OutputDirectory("barcodes");
            svg.SaveTo(dir, "codes");

            // Archivable, like the other samples: a file that claims nothing leaves a
            // validator to guess a flavour, and it guesses PDF/A-1 - which forbids things
            // this engine now writes. Better to say what it is.
            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b };
            sheet.GeneratedAt = SampleDate;
            ReportRenderer.Render(sheet, pdf);
            WriteSamplePdf(Path.Combine(dir, "codes.pdf"), pdf.ToArray());

            // The template carries a token and the data decides what is encoded, which is
            // the whole point of a barcode on an invoice. Left unbound, the brace itself
            // is what the scanner reads back.
            var tokenised = new Report { Dimension = new() { Width = 100, Height = 60 } };
            var holder = new Body { Layout = LayoutMode.Absolute };
            holder.Style.Location.Dock = Dock.Fill;
            tokenised.Body = holder;

            var bound = new BarcodeItem { Value = "{Invoice.Number}", Symbology = Symbology.Code128 };
            bound.Style.Location.Left = (5, Units.Millimeter);
            bound.Style.Location.Top = (5, Units.Millimeter);
            bound.Style.Location.Width = (90, Units.Millimeter);
            bound.Style.Location.Height = (20, Units.Millimeter);
            holder.Add(bound);

            tokenised = ReportBinder.Bind(tokenised, ("{\"Invoice\":{\"Number\":\"FA-2025-0042\"}}"));

            AssertEqual(testName, "the data reaches the barcode", "FA-2025-0042",
                tokenised.AllItems.OfType<BarcodeItem>().Single().Value, 0);

            // And it survives being saved as a template.
            Report reloaded = Report.FromJson(sheet.ToJson());
            AssertEqual(testName, "a barcode survives the round trip", 2,
                reloaded.AllItems.OfType<BarcodeItem>().Count(), 0);
            AssertEqual(testName, "with its symbology", Symbology.Code128,
                reloaded.AllItems.OfType<BarcodeItem>().Last().Symbology, 0);
            AssertEqual(testName, "with its correction level", QrErrorCorrection.Medium,
                reloaded.AllItems.OfType<BarcodeItem>().First().Correction, 0);
            AssertEqual(testName, "and its value", "https://example.invalid/invoice/33250200001",
                reloaded.AllItems.OfType<BarcodeItem>().First().Value, 0);

            Console.WriteLine($"  \u25A0 {Path.Combine(dir, "codes.pdf")}");
            Console.WriteLine();
        }
    }
}
