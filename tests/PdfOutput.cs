namespace test
{
    /// <summary>
    /// The written PDF read back: structure and references that reach each other, subset
    /// fonts that kept every drawn outline, PDF/A-3B with Factur-X attached, the tagged
    /// conformance document, and veraPDF run over it.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>
        /// A picture bound from the data is that record's picture, in the file as well as
        /// in the model.
        ///
        /// The PDF caches an image so that one drawn on forty pages is embedded once. It
        /// keyed that cache on <c>SourcePath</c>, and ReportBinder stamped the template's
        /// field path there - the same string for every record - so sixty signatures were
        /// one signature and the conformance document's six photographs were one
        /// photograph carrying six alternative texts. It shipped: two image objects for
        /// seven pictures, while the SVG beside it carried seven.
        ///
        /// So this counts pictures in BOTH outputs and sets them against each other. The
        /// binding was never wrong - checking the model would have said everything was
        /// fine, which is what nothing noticing it for months looks like.
        /// </summary>
        [Fact]
        public void Test78_EachRecordKeepsItsOwnPicture()
        {
            const string testName = "Test 78";
            Console.WriteLine("Test 78: a picture bound from the data is that record's picture");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // Three pictures that differ in their bytes and in what they look like. Same
            // size and same length on purpose: the cache's old fallback key was the first
            // sixty-four bytes and the length, which for a PNG is the signature and the
            // header - so three pictures of one size would have collided there too.
            static string Png(byte r, byte g, byte b) =>
                Convert.ToBase64String(PngEncoder.Rgb(24, 24, (x, y) => (r, g, b)));

            var report = new Report
            {
                Title = "Signatures", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new() { Left = 15, Top = 15, Right = 15, Bottom = 15 };

            var run = new ContainerItem { Layout = LayoutMode.Stack, RepeatOver = "Records" };
            var picture = new ImageItem
            {
                SourceField = "Sig", AltText = "signature",
                NaturalWidth = 30, NaturalHeight = 30
            };
            picture.Style.Location.WidthValue = 30;
            picture.Style.Location.HeightValue = 30;
            run.Add(picture);
            report.Body!.Add(run);
            report.Reattach();

            string data = $$"""
            { "Records": [
              { "Sig": "data:image/png;base64,{{Png(220, 30, 30)}}" },
              { "Sig": "data:image/png;base64,{{Png(30, 190, 60)}}" },
              { "Sig": "data:image/png;base64,{{Png(40, 60, 210)}}" }
            ] }
            """;

            Report bound = ReportBinder.Bind(report, data);

            // The model first, so a failure below cannot be blamed on the binder - which
            // is where nobody looked, because it was right all along.
            var sources = bound.AllItems.OfType<ImageItem>()
                .Select(i => Convert.ToBase64String(i.Source.Data)).ToList();
            Console.WriteLine($"    modele : {sources.Count} images, {sources.Distinct().Count()} distinctes");
            AssertEqual(testName, "the binder gives each record its own bytes",
                3, sources.Distinct().Count(), 0);

            // The PDF: one object per distinct picture, and one drawing per record.
            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Compress = false };
            ReportRenderer.Render(bound, pdf);
            string written = System.Text.Encoding.Latin1.GetString(pdf.ToArray());

            int objects = CountOccurrences(written, "/Subtype /Image");
            var drawn = System.Text.RegularExpressions.Regex.Matches(written, @"/(Im\d+) Do")
                .Select(m => m.Groups[1].Value).ToList();

            Console.WriteLine($"    PDF    : {objects} objets, {drawn.Count} dessins, "
                              + $"{drawn.Distinct().Count()} distincts ({string.Join(" ", drawn)})");

            AssertEqual(testName, "the PDF embeds one object per distinct picture", 3, objects, 0);
            AssertEqual(testName, "draws one per record", 3, drawn.Count, 0);
            AssertEqual(testName, "and never the same one twice", 3, drawn.Distinct().Count(), 0);

            // The SVG has no cache at all and embeds inline, so it was right throughout -
            // which is exactly why the two had to be set against each other. Each output
            // was consistent with itself.
            var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(bound, svg);
            var inSvg = System.Text.RegularExpressions.Regex
                .Matches(string.Join("", svg.Pages), "base64,([A-Za-z0-9+/=]+)")
                .Select(m => m.Groups[1].Value).Distinct().Count();

            Console.WriteLine($"    SVG    : {inSvg} images distinctes");
            AssertEqual(testName, "and the two outputs agree on how many pictures there are",
                inSvg, objects, 0);

            // The cache still earns its keep: the same picture three times is embedded
            // once. Without this the fix could have been "never cache", which would make
            // a contact sheet of one photograph forty times as large as it needs to be.
            Report same = ReportBinder.Bind(report, $$"""
            { "Records": [
              { "Sig": "data:image/png;base64,{{Png(10, 10, 10)}}" },
              { "Sig": "data:image/png;base64,{{Png(10, 10, 10)}}" },
              { "Sig": "data:image/png;base64,{{Png(10, 10, 10)}}" }
            ] }
            """);
            var shared = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Compress = false };
            ReportRenderer.Render(same, shared);
            string once = System.Text.Encoding.Latin1.GetString(shared.ToArray());

            Console.WriteLine($"    une seule image, trois fois : "
                              + $"{CountOccurrences(once, "/Subtype /Image")} objet(s), "
                              + $"{System.Text.RegularExpressions.Regex.Matches(once, @"/Im\d+ Do").Count} dessins");
            AssertEqual(testName, "the same picture three times is embedded once",
                1, CountOccurrences(once, "/Subtype /Image"), 0);
            AssertEqual(testName, "and still drawn three times", 3,
                System.Text.RegularExpressions.Regex.Matches(once, @"/Im\d+ Do").Count, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// The map from a mark to its element, read position by position.
        ///
        /// A tagged page carries two halves that have to agree: the tree, which says what
        /// each element is, and the number tree, which is indexed BY MARK - position n is
        /// the element that owns mark n on that page (ISO 32000-1, 14.7.4.4). A tree that
        /// is right and a map that is wrong is not a broken file: every reader opens it,
        /// the reading order is correct, and only the questions asked from the other end -
        /// what is under my cursor, what did I just select - answer with the wrong element.
        ///
        /// It was wrong on every tagged interactive page holding a field. A Form owns no
        /// marked content at all - it is reached through the object reference under its
        /// own key - and it was listed in the page's array all the same, so it took a slot
        /// no mark claims and shifted everything after it by one. Measured on the
        /// conformance document: a reader over the H1 was told it was the signature box.
        ///
        /// Nothing saw it. The suite asserted on the tree and never on the map; veraPDF
        /// passes both profiles, because a well-formed array of the wrong length is not
        /// something either profile tests. Hence a check that compares the two halves
        /// against each other rather than either against itself.
        /// </summary>
        [Fact]
        public void Test85_EveryMarkFindsItsOwnElement()
        {
            const string testName = "Test 85";
            Console.WriteLine("Test 85: the map from a mark to its element");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            var report = new Report
            {
                Title = "A page with a control on it", Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            report.Section.Style.Padding = new() { Left = 20, Top = 20, Right = 20, Bottom = 20 };

            report.Body!.Add(new TextItem { Text = "Before", Role = PdfRole.Heading1,
                                            Font = new() { Size = 5 } });

            // The control in the middle, which is the whole point: it owns no mark, so
            // everything declared after it is what moves when the map counts it.
            var field = new FieldItem
            {
                Name = "customer.name", Kind = FieldKind.Text,
                Tooltip = "Your name", AltText = "The customer's name", Value = "J. Smith"
            };
            field.Style.Location.HeightValue = 8;
            report.Body!.Add(field);

            report.Body!.Add(new TextItem { Text = "After the control" });
            report.Body!.Add(new TextItem { Text = "And after that", Role = PdfRole.Heading2 });
            report.Reattach();

            var backend = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b, Language = "en-GB",
                Tagged = true, Interactive = true, Compress = false
            };
            ReportRenderer.Render(report, backend);
            string pdf = System.Text.Encoding.Latin1.GetString(backend.ToArray());

            // --- what each structure element says it owns ----------------------------
            var owns = new Dictionary<int, int>();          // object number -> its mark
            var kind = new Dictionary<int, string>();       // object number -> its tag
            var unmarked = new HashSet<int>();              // owns no mark at all
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         pdf, @"(\d+) 0 obj\s*(<< /Type /StructElem .*?)endobj",
                         System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                int number = int.Parse(m.Groups[1].Value);
                string body = m.Groups[2].Value;

                var tag = System.Text.RegularExpressions.Regex.Match(body, @"/S /(\w+)");
                if (tag.Success) kind[number] = tag.Groups[1].Value;

                // Three shapes of /K, and they are not equally readable from here.
                //
                // "/K <n>" names the mark outright. "/K [ ... ]" is a container holding
                // its children - it allocated a mark of its own all the same, and drops
                // the reference to it, so from the object alone there is no telling WHICH
                // mark it owns. "/K << /Type /OBJR >>" is a control, and that one owns no
                // mark at all: it is the only shape that has no business in the map.
                if (System.Text.RegularExpressions.Regex.IsMatch(body, @"/K << /Type /OBJR"))
                    unmarked.Add(number);

                var mark = System.Text.RegularExpressions.Regex.Match(body, @"/K (\d+)(?![\d])");
                if (mark.Success) owns[number] = int.Parse(mark.Groups[1].Value);
            }

            // --- what the page's array says ------------------------------------------
            var nums = System.Text.RegularExpressions.Regex.Match(pdf, @"/Nums \[0 \[([^\]]*)\]");
            AssertEqual(testName, "the first page has an array of its own", true, nums.Success, 0);

            List<int> row = System.Text.RegularExpressions.Regex
                .Matches(nums.Groups[1].Value, @"(\d+) 0 R")
                .Select(m => int.Parse(m.Groups[1].Value))
                .ToList();

            // The control first, or the rest of this test is about a page that never had
            // the problem: a page with no Form on it could not misalign either way.
            AssertEqual(testName, "a control really was written into the tree", true,
                kind.Values.Any(t => t == "Form"), 0);

            int marks = System.Text.RegularExpressions.Regex
                .Matches(pdf, @"<</MCID (\d+)>> BDC").Count;

            Console.WriteLine($"    marques sur la page   : {marks}");
            Console.WriteLine($"    entrees dans la carte : {row.Count}");
            for (int i = 0; i < row.Count; i++)
            {
                string says = owns.TryGetValue(row[i], out int k) ? k.ToString() : "aucune";
                Console.WriteLine($"      [{i,2}] objet {row[i],3}  {kind.GetValueOrDefault(row[i], "?"),-10} dit {says}");
            }

            AssertEqual(testName, "one entry per mark, and no more", marks, row.Count, 0);

            // Nothing in the map that owns no mark. This is the fault itself, stated
            // directly: a Form is reached through the object reference under its own key
            // and belongs in no page array, and one listed here displaces every element
            // after it.
            AssertEqual(testName, "and nothing in the map owns no mark at all",
                "", string.Join("; ", row.Where(unmarked.Contains)
                                         .Select(o => $"object {o} ({kind.GetValueOrDefault(o, "?")})")), 0);

            // Position by position, for every entry that names its mark outright.
            // Comparing the counts alone would pass an array of the right length in the
            // wrong order.
            var wrong = new List<string>();
            for (int i = 0; i < row.Count; i++)
            {
                if (owns.TryGetValue(row[i], out int claimed) && claimed != i)
                    wrong.Add($"[{i}] object {row[i]} says it owns mark {claimed}");
            }

            AssertEqual(testName, "and every entry that names its mark names its own position",
                "", string.Join("; ", wrong), 0);

            // The check above is worth only what it reads: a container hides its mark
            // behind its children, so it cannot be questioned. Say how many were.
            int named = row.Count(owns.ContainsKey);
            Console.WriteLine($"    dont interrogeables : {named}");
            AssertEqual(testName, "and there were entries to question", true, named >= 3, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test24_Pdf()
        {
            var testName = "Test 24: PDF Backend";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            Report report = BoundAcmeInvoice(45);

            var pdf = new PdfBackend(library);
            LayoutResult layout = ReportRenderer.Render(report, pdf);
            byte[] bytes = pdf.ToArray();

            string text = System.Text.Encoding.Latin1.GetString(bytes);

            // --- the file is a PDF, and a well-formed one ---------------------
            AssertEqual(testName, "starts with a PDF header", true, text.StartsWith("%PDF-1.7"), 0);
            AssertEqual(testName, "ends with the end-of-file marker", true, text.TrimEnd().EndsWith("%%EOF"), 0);
            AssertEqual(testName, "has a catalog", true, text.Contains("/Type /Catalog"), 0);
            AssertEqual(testName, "one page object per page", layout.PageCount,
                CountOccurrences(text, "/Type /Page "), 0);
            AssertEqual(testName, "several pages", true, layout.PageCount > 1, 0);

            // The cross-reference table must point at real objects, or no reader opens it.
            int xrefAt = text.LastIndexOf("startxref", StringComparison.Ordinal);
            int declared = int.Parse(text[(xrefAt + 9)..].Trim().Split('\n')[0].Trim());
            AssertEqual(testName, "startxref points at the xref table", true,
                declared > 0 && declared < bytes.Length && text.Substring(declared, 4) == "xref", 0);

            int size = int.Parse(text[(text.IndexOf("/Size ", StringComparison.Ordinal) + 6)..].Split(' ')[0]);
            AssertEqual(testName, "every object is numbered", true,
                CountOccurrences(text, " 0 obj") == size - 1, 0);

            // --- the fonts travel with the section ---------------------------
            AssertEqual(testName, "text is Type0 for full Unicode", true, text.Contains("/Subtype /Type0"), 0);
            AssertEqual(testName, "identity encoding", true, text.Contains("/Encoding /Identity-H"), 0);
            AssertEqual(testName, "TrueType file embedded", true, text.Contains("/FontFile2"), 0);
            AssertEqual(testName, "text stays selectable", true, text.Contains("/ToUnicode"), 0);
            AssertEqual(testName, "regular and bold both embedded", true,
                CountOccurrences(text, "/FontFile2") >= 2, 0);
            // Read off the face rather than written down: the number belongs to whichever
            // family DmsFont names, and naming it here meant this failed the day the
            // library was triaged rather than the day the writer broke.
            int declaredCap = (int)Math.Round(library.Resolve(DmsFont, false, false)!.CapHeight * 1000);
            AssertEqual(testName, "cap height declared from the font", true,
                text.Contains($"/CapHeight {declaredCap}"), 0);

            // --- the page really carries drawing instructions ------------------
            AssertEqual(testName, "content streams compressed", true, text.Contains("/Filter /FlateDecode"), 0);

            var plain = new PdfBackend(library) { Compress = false };
            ReportRenderer.Render(report, plain);
            string readable = System.Text.Encoding.Latin1.GetString(plain.ToArray());

            AssertEqual(testName, "millimetres converted once per page", true,
                readable.Contains("2.8346 0 0 -2.8346 0 841.8898 cm"), 0);
            AssertEqual(testName, "text drawn", true, readable.Contains(" Tj"), 0);
            AssertEqual(testName, "rules drawn", true, readable.Contains(" l\nS"), 0);
            AssertEqual(testName, "filled boxes drawn", true, readable.Contains(" re\nf"), 0);
            AssertEqual(testName, "A4 media box in points", true,
                readable.Contains("/MediaBox [0 0 595.2756 841.8898]"), 0);

            // The logo travels as an image XObject, and its PNG data crosses over
            // untouched: PNG's row filtering is PDF's Predictor 15.
            AssertEqual(testName, "logo embedded as an image", true, text.Contains("/Subtype /Image"), 0);
            AssertEqual(testName, "PNG passed through, not decoded", true,
                text.Contains("/Predictor 15") && text.Contains("/ColorSpace /DeviceRGB"), 0);
            AssertEqual(testName, "logo drawn on the page", true, readable.Contains("/Im1 Do"), 0);
            AssertEqual(testName, "logo repeated on every page without a second copy", 1,
                CountOccurrences(text, "/Subtype /Image"), 0);

            // Not one character fell through to .notdef, which PDF/A forbids outright.
            AssertEqual(testName, "every character had a glyph", 0, pdf.MissingGlyphs.Count, 0);

            string dir = OutputDirectory("acme");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "acme.pdf");
            File.WriteAllBytes(path, bytes);

            Console.WriteLine($"  📄 {layout.PageCount} pages, {bytes.Length / 1024} kB -> {path}");
            Console.WriteLine();
        }

        static string Field(string raw, int obj, string pattern)
        {
            int at = raw.IndexOf(obj + " 0 obj", StringComparison.Ordinal);
            return System.Text.RegularExpressions.Regex
                .Match(raw.Substring(at, Math.Min(600, raw.Length - at)), pattern).Groups[1].Value;
        }

        static byte[] StreamOf(byte[] pdf, string raw, int obj)
        {
            int at = raw.IndexOf(obj + " 0 obj", StringComparison.Ordinal);
            int start = raw.IndexOf("stream", at, StringComparison.Ordinal) + 6;
            if (pdf[start] == 13) start++;   // CR
            if (pdf[start] == 10) start++;   // LF
            int end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            return pdf.Skip(start).Take(end - start).ToArray();
        }

        /// <summary>The glyphs a composite is assembled from; empty for a simple outline.</summary>
        static IEnumerable<int> ComponentsOf(byte[] font, int at)
        {
            int Read16(int i) => (font[i] << 8) | font[i + 1];

            if (unchecked((short)Read16(at)) >= 0)
                yield break;                                  // simple outline

            int p = at + 10;
            while (true)
            {
                int flags = Read16(p);
                yield return Read16(p + 2);

                p += 4;
                p += (flags & 1) != 0 ? 4 : 2;
                if ((flags & 8) != 0) p += 2;
                else if ((flags & 0x40) != 0) p += 4;
                else if ((flags & 0x80) != 0) p += 8;

                if ((flags & 0x20) == 0)
                    yield break;
            }
        }

        static int TableOf(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];
            for (int i = 0; i < count; i++)
            {
                int r = 12 + i * 16;
                if (System.Text.Encoding.ASCII.GetString(font, r, 4) == tag)
                    return (font[r + 8] << 24) | (font[r + 9] << 16) | (font[r + 10] << 8) | font[r + 11];
            }
            return 0;
        }

        static int[] LocaOf(byte[] font)
        {
            int Read16(int at) => (font[at] << 8) | font[at + 1];
            int Read32(int at) => (font[at] << 24) | (font[at + 1] << 16) | (font[at + 2] << 8) | font[at + 3];

            var tables = new Dictionary<string, int>();
            int count = Read16(4);
            for (int i = 0; i < count; i++)
            {
                int r = 12 + i * 16;
                tables[System.Text.Encoding.ASCII.GetString(font, r, 4)] = Read32(r + 8);
            }

            if (!tables.TryGetValue("loca", out int loca) || !tables.TryGetValue("maxp", out int maxp))
                return Array.Empty<int>();

            int glyphs = Read16(maxp + 4);
            var offsets = new int[glyphs + 1];
            for (int i = 0; i <= glyphs; i++)
                offsets[i] = Read32(loca + i * 4);       // the subset always writes long offsets

            return offsets;
        }

        static IEnumerable<int> GlyphsDrawnWith(string raw, string font)
        {
            string current = "";
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex
                     .Matches(raw, @"/(F\d+) [\d.]+ Tf|<([0-9A-Fa-f]+)> Tj"))
            {
                if (m.Groups[1].Success) { current = m.Groups[1].Value; continue; }
                if (current != font) continue;

                string hex = m.Groups[2].Value;
                for (int i = 0; i + 3 < hex.Length; i += 4)
                    yield return Convert.ToInt32(hex.Substring(i, 4), 16);
            }
        }

        /// <summary>
        /// For every glyph the first page draws, whether the embedded subset still holds
        /// an outline for it. Reads the file back rather than trusting what was written.
        /// </summary>
        static List<(int Gid, bool Present)> DrawnGlyphOutlines(byte[] pdf)
        {
            string raw = System.Text.Encoding.Latin1.GetString(pdf);
            var found = new List<(int, bool)>();

            // /Fn -> the subset embedded for it
            foreach (var use in System.Text.RegularExpressions.Regex
                     .Matches(raw, @"/(F\d+) [\d.]+ Tf")
                     .Select(m => m.Groups[1].Value).Distinct())
            {
                int top = int.Parse(System.Text.RegularExpressions.Regex
                    .Match(raw, @"/" + use + @" (\d+) 0 R").Groups[1].Value);
                int descendant = int.Parse(Field(raw, top, @"/DescendantFonts \[(\d+)"));
                int descriptor = int.Parse(Field(raw, descendant, @"/FontDescriptor (\d+)"));
                int stream = int.Parse(Field(raw, descriptor, @"/FontFile2 (\d+)"));

                byte[] font = StreamOf(pdf, raw, stream);
                int[] loca = LocaOf(font);
                if (loca.Length == 0) continue;

                int glyf = TableOf(font, "glyf");

                foreach (int gid in GlyphsDrawnWith(raw, use))
                {
                    if (gid + 1 >= loca.Length)
                        continue;

                    bool present = loca[gid + 1] > loca[gid];
                    found.Add((gid, present));

                    // A composite - e acute, say - is only the instruction to place other
                    // glyphs, and it keeps its own entry whether or not they came along.
                    // Checking the drawn glyph alone therefore passes while the accent is
                    // missing from the page, which is the one mistake worth catching here.
                    if (!present)
                        continue;

                    foreach (int part in ComponentsOf(font, glyf + loca[gid]))
                        if (part + 1 < loca.Length)
                            found.Add((part, loca[part + 1] > loca[part]));
                }
            }

            return found;
        }

        [Fact]
        public void Test36_FontSubset()
        {
            var testName = "Test 36: Font subsetting";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            Report report = BoundAcmeInvoice(12);

            var whole = new PdfBackend(library) { Subset = false, Compress = false };
            ReportRenderer.Render(report, whole);
            int full = whole.ToArray().Length;

            var cut = new PdfBackend(library) { Compress = false };
            ReportRenderer.Render(report, cut);
            byte[] bytes = cut.ToArray();

            Console.WriteLine($"    entier {full} o -> sous-ensemble {bytes.Length} o "
                              + $"({100 - 100 * bytes.Length / full}% de moins)");

            AssertEqual(testName, "the file gets markedly smaller", true, bytes.Length < full * 3 / 4, 0);

            string raw = System.Text.Encoding.Latin1.GetString(bytes);
            AssertEqual(testName, "a partial font says so", true,
                System.Text.RegularExpressions.Regex.IsMatch(raw, @"/BaseFont /[A-Z]{6}\+"), 0);

            // Size proves nothing on its own: a font that lost the wrong glyphs is
            // smaller too, and shows up as blanks that only appear on accented words.
            // Every glyph the page asked for has to still have an outline.
            var outlines = DrawnGlyphOutlines(bytes);

            // The loop below proves nothing if it finds nothing to loop over, and this
            // whole check reads the PDF back through several indirections that a change
            // to the writer could quietly break.
            AssertEqual(testName, "the glyphs were found to check", true, outlines.Count > 40,
                outlines.Count);

            // A space carries no outline and never did; every other glyph must. Asked of
            // the font rather than written down - the id is whatever the face numbers it,
            // and a hardcoded one silently starts excusing a real glyph the moment the
            // family changes.
            ushort blank = library.Resolve(DmsFont, false, false)!.GlyphOf(' ');

            foreach ((int gid, bool present) in outlines)
            {
                if (gid == blank) continue;
                AssertEqual(testName, $"glyph {gid} kept its outline", true, present, 0);
            }

            Console.WriteLine($"    {outlines.Count} glyphes verifies, contours presents");

            Console.WriteLine();
        }

        /// <summary>A minimal Cross Industry Invoice, enough to exercise the attachment.</summary>
        static byte[] FacturXXml() => System.Text.Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <rsm:CrossIndustryInvoice
                xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
                xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100">
              <rsm:ExchangedDocument>
                <ram:ID>33250200001</ram:ID>
                <ram:TypeCode>380</ram:TypeCode>
                <ram:IssueDateTime>20251002</ram:IssueDateTime>
              </rsm:ExchangedDocument>
              <rsm:SupplyChainTradeTransaction>
                <ram:ApplicableHeaderTradeSettlement>
                  <ram:InvoiceCurrencyCode>EUR</ram:InvoiceCurrencyCode>
                  <ram:SpecifiedTradeSettlementHeaderMonetarySummation>
                    <ram:GrandTotalAmount>14252123.30</ram:GrandTotalAmount>
                  </ram:SpecifiedTradeSettlementHeaderMonetarySummation>
                </ram:ApplicableHeaderTradeSettlement>
              </rsm:SupplyChainTradeTransaction>
            </rsm:CrossIndustryInvoice>
            """);

        [Fact]
        public void Test25_FacturX()
        {
            var testName = "Test 25: PDF/A-3 and Factur-X";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            Report report = BoundAcmeInvoice(12);
            byte[] xml = FacturXXml();

            var pdf = new PdfBackend(library) { Author = "ACME Industries" };
            pdf.AttachFacturX(xml);

            ReportRenderer.Render(report, pdf);
            byte[] bytes = pdf.ToArray();
            string text = System.Text.Encoding.Latin1.GetString(bytes);

            // Attaching the invoice implies the archivable profile it rides on.
            AssertEqual(testName, "conformance switched to PDF/A-3", PdfConformance.PdfA3b, pdf.Conformance, 0);

            // --- what makes it PDF/A ------------------------------------------
            AssertEqual(testName, "declares PDF/A part 3", true, text.Contains("<pdfaid:part>3</pdfaid:part>"), 0);
            AssertEqual(testName, "conformance level B", true,
                text.Contains("<pdfaid:conformance>B</pdfaid:conformance>"), 0);
            AssertEqual(testName, "XMP packet present and readable", true,
                text.Contains("/Type /Metadata /Subtype /XML") && text.Contains("<?xpacket begin="), 0);
            AssertEqual(testName, "output intent", true,
                text.Contains("/OutputIntent") && text.Contains("/S /GTS_PDFA1"), 0);
            AssertEqual(testName, "colour profile embedded", true, text.Contains("/DestOutputProfile"), 0);
            AssertEqual(testName, "file identifier", true, text.Contains("/ID [<"), 0);
            AssertEqual(testName, "title agrees with the info dictionary", true, text.Contains("/Title ("), 0);
            AssertEqual(testName, "fonts still embedded", true, text.Contains("/FontFile2"), 0);

            // --- what makes it Factur-X ----------------------------------------
            AssertEqual(testName, "the XML travels inside", true, text.Contains("/Type /EmbeddedFile"), 0);
            AssertEqual(testName, "named factur-x.xml", true, text.Contains("(factur-x.xml)"), 0);
            AssertEqual(testName, "declared as the document's data", true, text.Contains("/AFRelationship /Data"), 0);
            AssertEqual(testName, "associated with the section", true, text.Contains("/AF ["), 0);
            AssertEqual(testName, "listed in the attachment pane", true, text.Contains("/EmbeddedFiles"), 0);
            AssertEqual(testName, "media type is xml", true, text.Contains("/Subtype /text#2Fxml"), 0);
            AssertEqual(testName, "declared in the metadata", true,
                text.Contains("<fx:DocumentFileName>factur-x.xml</fx:DocumentFileName>")
                && text.Contains("<fx:ConformanceLevel>EN 16931</fx:ConformanceLevel>"), 0);
            AssertEqual(testName, "extension schema declared", true,
                text.Contains("Factur-X PDFA Extension Schema"), 0);

            AssertEqual(testName, "no .notdef anywhere", 0, pdf.MissingGlyphs.Count, 0);

            // French grouping uses a narrow no-break space, which most fonts do not carry.
            // It has to fall back to a plain space, not to an empty box: a validator
            // rejects the whole file over a single reference to .notdef.
            AssertEqual(testName, "the narrow space folded to a plain one", true,
                library.Fonts.First(f => f.Family == DmsFont && !f.Bold).GlyphOf(' ')
                == library.Fonts.First(f => f.Family == DmsFont && !f.Bold).GlyphOf(' '), 0);

            // Through the writer as well, because a glyph index proves the lookup and not
            // what reaches the file. The invoice used to carry this for free by being
            // bound French; it binds neutral now, so the MissingGlyphs count above stopped
            // covering U+202F the day the numbers stopped being French - the check kept
            // passing, on a document that no longer contains the character it was written
            // for.
            var narrowReport = new Report
            {
                Language = "en-GB",
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            narrowReport.Body!.Add(new TextItem { Text = "1 234 567,89 EUR" });

            var narrowPdf = new PdfBackend(library);
            ReportRenderer.Render(narrowReport, narrowPdf);
            Console.WriteLine($"    espace fine ecrite, {narrowPdf.MissingGlyphs.Count} glyphe(s) manquant(s)");
            AssertEqual(testName, "a document grouped the French way writes no .notdef",
                0, narrowPdf.MissingGlyphs.Count, 0);

            // A character no font here carries must be refused rather than written out.
            var strict = new PdfBackend(library);
            strict.AttachFacturX(xml);
            var exotic = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                Body = new()
            };
            exotic.Body!.Add(new TextItem { Text = "中文", Font = new() { Family = DmsFont } });
            ReportRenderer.Render(exotic, strict);

            bool refused = false;
            try
            {
                strict.ToArray();
            }
            catch (InvalidOperationException e)
            {
                refused = e.Message.Contains("U+4E2D");
            }
            AssertEqual(testName, "an unsupported character is refused, not drawn as a box", true, refused, 0);

            // The profile stream is compressed, so its contents are checked outside; here
            // we can at least see it declares the three components of an RGB profile.
            AssertEqual(testName, "profile declares three components", true, text.Contains("<< /N 3 "), 0);

            // Rendering twice must give the same bytes, identifier included.
            var again = new PdfBackend(library) { Author = "ACME Industries" };
            again.AttachFacturX(xml);
            ReportRenderer.Render(BoundAcmeInvoice(12), again);
            AssertEqual(testName, "output is reproducible", true, again.ToArray().SequenceEqual(bytes), 0);

            string dir = OutputDirectory("acme");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "acme.factur-x.pdf");
            File.WriteAllBytes(path, bytes);

            Console.WriteLine($"  🇫🇷 PDF/A-3 + Factur-X, {bytes.Length / 1024} kB -> {path}");
            Console.WriteLine();
        }

        static double Number(string text)
            => double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The measurements table as data, read off the bound section rather than
        /// rebuilt from the source: an attachment that disagrees with the page it is
        /// attached to is worse than no attachment, and rebuilding it is exactly how
        /// the two drift apart.
        /// </summary>
        static string ReadingsCsv(Report report)
        {
            var csv = new System.Text.StringBuilder("Site,Observation,Reading,Status\n");

            TableItem table = report.AllItems.OfType<TableItem>().First();
            foreach (TableRow row in table.Childs.OfType<TableRow>())
            {
                if (row.IsHeader) continue;

                IEnumerable<string> cells = row.Childs.OfType<TextItem>()
                    .Select(t => t.Text.Replace("\"", "\"\"").Replace("\n", " "));
                csv.Append('"').Append(string.Join("\",\"", cells)).Append("\"\n");
            }

            return csv.ToString();
        }

        /// <summary>
        /// Drawing operations sitting outside any marked content, across every page
        /// stream of an uncompressed section.
        /// </summary>
        static int LooseDrawing(string pdf, out int examined)
        {
            int loose = 0;
            examined = 0;

            // Streams are delimited by the keyword and one newline; the page bodies
            // this looks at are written uncompressed by the caller.
            const string pattern = "stream\r?\n(.*?)endstream";

            foreach (System.Text.RegularExpressions.Match stream in System.Text.RegularExpressions.Regex
                     .Matches(pdf, pattern, System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                string content = stream.Groups[1].Value;
                if (!content.Contains("BDC") && !content.Contains("BMC"))
                    continue;                                  // not a tagged page stream

                examined++;
                int depth = 0;

                foreach (string line in content.Split('\n'))
                {
                    string text = line.Trim();

                    if (text.EndsWith("BDC", StringComparison.Ordinal)
                        || text.EndsWith("BMC", StringComparison.Ordinal))
                        depth++;
                    else if (text == "EMC")
                        depth--;
                    else if (depth == 0 && System.Text.RegularExpressions.Regex
                             .IsMatch(text, "\\b(Tj|re|m|l|Do)\\b"))
                        loose++;
                }
            }

            return loose;
        }

        /// <summary>
        /// True when the first Table found holds rows, and those rows hold cells. Reads
        /// the objects back rather than trusting the tags: the shape is the point.
        /// </summary>
        static bool RowHoldsCells(string pdf)
        {
            var objects = new Dictionary<int, string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex
                     .Matches(pdf, @"(\d+) 0 obj\s*(<<.*?>>)\s*endobj",
                              System.Text.RegularExpressions.RegexOptions.Singleline))
                objects[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;

            string Kind(int n) => objects.TryGetValue(n, out string? body)
                ? System.Text.RegularExpressions.Regex.Match(body, @"/S /(\w+)").Groups[1].Value
                : "";

            List<int> Children(int n)
            {
                var list = System.Text.RegularExpressions.Regex.Match(objects[n], @"/K \[([^\]]*)\]");
                return list.Success
                    ? System.Text.RegularExpressions.Regex.Matches(list.Groups[1].Value, @"(\d+) 0 R")
                        .Select(m => int.Parse(m.Groups[1].Value)).ToList()
                    : new List<int>();
            }

            int table = objects.Keys.FirstOrDefault(n => Kind(n) == "Table");
            if (table == 0)
                return false;

            List<int> rows = Children(table);
            if (rows.Count == 0 || rows.Any(r => Kind(r) != "TR"))
                return false;

            List<int> cells = Children(rows[0]);
            return cells.Count > 0 && cells.All(c => Kind(c) is "TH" or "TD");
        }

        [Fact]
        public void Test40_Everything()
        {
            var testName = "Test 40: One section with everything in it";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // The example is written to examples/ and committed. That is the point of
            // it being JSON: someone can open it in the designer or hand it to the CLI
            // without building anything, and what they open is the very section this
            // test validates - not a cousin of it.
            string examples = Path.Combine(RepositoryRoot(), "examples");
            string templatePath = Showcase.WriteFiles(examples);

            // Read back from disk rather than used as built. This is the path the CLI and
            // the native library take, it resolves the appendix from its own file, and it
            // is the only check that every element kind survives being written and re-read
            // - a report handed straight to the renderer never proves that.
            Report report = Report.FromFile(templatePath);

            AssertEqual(testName, "the reloaded template has a body", true, report.Body != null, 0);

            report = ReportBinder.Bind(report,
                File.ReadAllText(Path.Combine(examples, Showcase.DataFile)));

            // The table the outline points at, and the one the structure checks read.
            TableItem firstTable = report.AllItems.OfType<TableItem>().First();

            var pdf = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Tagged = true };

            // PDF/A-3 lets a file carry its own machine-readable companion. Factur-X is
            // one shape of that and has its own test; what has to be covered here is the
            // general mechanism - an attachment reachable through /AF.
            pdf.Attach(new PdfAttachment
            {
                Name = "readings.csv",
                MediaType = "text/csv",
                Relationship = "Data",
                Description = "The measurements table, as data",
                Data = System.Text.Encoding.UTF8.GetBytes(ReadingsCsv(report))
            });
            LayoutResult layout = ReportRenderer.Render(report, pdf);
            byte[] bytes = pdf.ToArray();

            string dir = OutputDirectory("conformance");
            File.WriteAllBytes(Path.Combine(dir, "conformance.pdf"), bytes);

            // The same section on screen. Two ways of looking at one thing catch
            // different faults: the PDF is what a validator reads, this is what an eye
            // reads, and today the eye found the watermark running its letters together.
            var onScreen = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
            ReportRenderer.Render(report, onScreen);
            File.WriteAllText(Path.Combine(dir, "conformance.html"), onScreen.ToHtml());

            string raw = System.Text.Encoding.Latin1.GetString(bytes);

            // Each feature has to be in there, or the one file to validate is not the one
            // file that covers everything - and a validator passing it would say nothing.
            (string What, bool Present)[] carried =
            {
                ("PDF/A-3B",              raw.Contains("<pdfaid:part>3</pdfaid:part>")),
                ("an attached file",     raw.Contains("readings.csv") && raw.Contains("/AF ")),
                ("an output intent",     raw.Contains("/OutputIntents")),
                ("several pages",        layout.PageCount > 1),
                ("back pages",           layout.SheetCount == layout.PageCount * 2),
                ("a translucent mark",   raw.Contains("/ExtGState") && raw.Contains("/BM /Normal")),
                ("a form",               raw.Contains("/AcroForm")),
                ("a digital signature",  raw.Contains("/FT /Sig")),
                ("subset fonts",         System.Text.RegularExpressions.Regex.IsMatch(raw, @"/BaseFont /[A-Z]{6}\+")),
                ("an image",             raw.Contains("/Subtype /Image")),
                ("a declared language",  raw.Contains("/Lang (en-GB)")),
                ("fields named for a reader", raw.Contains("/TU (")),
                ("the title shown",      raw.Contains("/DisplayDocTitle true")),
                ("a tagged section",    raw.Contains("/MarkInfo << /Marked true >>")),
                ("a structure tree",     raw.Contains("/Type /StructTreeRoot")),

            };

            // A PDF literal string carries one byte per character, so anything past
            // U+00FF cannot go in one - and the narrow no-break space this engine groups
            // French numbers with is U+202F. Written as a literal it became a question
            // mark, silently, in a title or in the text a screen reader announces.
            var narrow = new Report { Title = "Facture 33 260 200 001" };
            narrow.Body = new Body();
            narrow.Body.Style.Location.Dock = Dock.Fill;
            narrow.Body.Add(new TextItem { Text = "x" });

            var wide = new PdfBackend(library);
            ReportRenderer.Render(narrow, wide);
            string written = System.Text.Encoding.Latin1.GetString(wide.ToArray());

            AssertEqual(testName, "a title outside PDFDocEncoding goes out as UTF-16", true,
                written.Contains("/Title <FEFF"), 0);
            AssertEqual(testName, "and nothing is replaced by a question mark", false,
                written.Contains("/Title (") && written.Contains("?"), 0);

            // Metadata has to be in both the info dictionary and the XMP packet, and say
            // the same in each: a validator reads both and refuses a file whose two
            // accounts of itself disagree.
            AssertEqual(testName, "the section carries: author (info)", true,
                raw.Contains("/Author (ACME Corporation)"), 0);
            AssertEqual(testName, "the section carries: author (xmp)", true,
                raw.Contains("<dc:creator>") && raw.Contains("ACME Corporation"), 0);
            AssertEqual(testName, "the section carries: subject on both sides", true,
                raw.Contains("/Subject (") && raw.Contains("<dc:description>"), 0);
            AssertEqual(testName, "the section carries: keywords on both sides", true,
                raw.Contains("/Keywords (") && raw.Contains("<pdf:Keywords>"), 0);

            // And the metadata went through the data like anything else.
            AssertEqual(testName, "the metadata went through the binder", false,
                report.Subject!.Contains('{') || report.Keywords!.Contains('{'), 0);

            // The outline: one entry per bookmark, nested as asked, each pointing at a
            // page that exists.
            foreach (OutlineEntry entry in layout.Outline)
                Console.WriteLine($"    outline: {new string(' ', entry.Level * 2)}{entry.Title} -> sheet {entry.Sheet + 1}");

            // The report, then one per demonstration: Summary, Measurements, Readings,
            // Site photographs, Appendix, References, Sign-off, Not visible. Counted
            // rather than sampled, so a heading silently losing its bookmark shows up
            // here as a number rather than as nothing at all.
            AssertEqual(testName, "the section carries: an outline", 9, layout.Outline.Count, 0);
            AssertEqual(testName, "and all but the first sit under it", 8,
                layout.Outline.Count(e => e.Level == 1), 0);
            AssertEqual(testName, "every entry points at a page that exists", true,
                layout.Outline.All(e => e.Sheet >= 0 && e.Sheet < layout.SheetCount), 0);

            // The first entry names the section, and names it from the data: an outline
            // whose headings still show braces is one nobody bound.
            AssertEqual(testName, "and the first one names the report", true,
                layout.Outline[0].Title.StartsWith("Report ")
                && !layout.Outline[0].Title.Contains('{'), 0);
            AssertEqual(testName, "and no entry keeps a token", false,
                layout.Outline.Any(e => e.Title.Contains('{')), 0);

            AssertEqual(testName, "the PDF writes the outline", true,
                raw.Contains("/Type /Outlines") && raw.Contains("/PageMode /UseOutlines"), 0);

            // The marks live in the page stream, which is compressed in the file that
            // gets shipped - so they are read from an uncompressed copy of the same
            // section rather than from the bytes.
            var readable = new PdfBackend(library) { Conformance = PdfConformance.PdfA3b, Tagged = true, Compress = false };
            readable.AttachFacturX(FacturXXml());
            ReportRenderer.Render(report, readable);
            string streams = System.Text.Encoding.Latin1.GetString(readable.ToArray());

            AssertEqual(testName, "the section carries: marked content", true, streams.Contains("BDC"), 0);
            AssertEqual(testName, "the section carries: decoration set aside", true,
                streams.Contains("/Artifact BMC"), 0);
            AssertEqual(testName, "every mark is closed", true,
                CountOccurrences(streams, "EMC") ==
                CountOccurrences(streams, "BDC") + CountOccurrences(streams, "BMC"), 0);

            // A table read aloud is only navigable if its shape survives: the reader
            // announces the column heading before each value, which needs Table holding
            // rows holding cells, not a flat run of paragraphs.
            AssertEqual(testName, "tables are tagged as tables", true,
                streams.Contains("/S /Table") && streams.Contains("/S /TR")
                && streams.Contains("/S /TH") && streams.Contains("/S /TD"), 0);

            AssertEqual(testName, "and headers say what they head", true,
                streams.Contains("/Scope /Column"), 0);

            // Nesting, not just presence: a Table whose children are cells rather than
            // rows names all the right tags and describes nothing.
            AssertEqual(testName, "a row holds its cells", true, RowHoldsCells(streams), 0);

            // --- the back page, set the way a back page is set ------------------
            // A file that is meant to hold everything only proves anything if it
            // really does. Two columns and justified text both leave a mark in the
            // stream, so both can be looked for rather than assumed.
            var backTexts = layout.Pages.Where(page => page.IsBack)
                .SelectMany(page => page.Items.Where(i => i.Source is TextItem))
                .ToList();

            var backColumns = backTexts.Select(i => Math.Round(i.Frame.Left, 1))
                                       .Distinct().OrderBy(x => x).ToList();

            Console.WriteLine($"    back page: columns at x = {string.Join(", ", backColumns)}");
            AssertEqual(testName, "the section carries: a back page in two columns", 2, backColumns.Count, 0);

            // Balanced: the two columns of the back end together rather than the
            // first running to the foot and the second stopping halfway.
            double[] backFeet = backTexts.GroupBy(i => Math.Round(i.Frame.Left, 1))
                                         .OrderBy(g => g.Key)
                                         .Select(g => g.Max(i => i.Frame.Bottom))
                                         .ToArray();

            Console.WriteLine($"    back page: columns end at "
                              + string.Join(" et ", backFeet.Select(v => v.ToString("F0"))) + " mm");
            AssertEqual(testName, "and they are balanced", true,
                Math.Abs(backFeet[0] - backFeet[1]) < 10, 0);

            // Justification shows up as a TJ array: the gaps between words carry an
            // adjustment. Tw would have been the obvious operator and does nothing at
            // all with a two-byte encoding, so its absence here is the point.
            int stretched = System.Text.RegularExpressions.Regex.Matches(streams, @"\]\s*TJ").Count;
            Console.WriteLine($"    {stretched} justified lines in the file");
            AssertEqual(testName, "the section carries: justified text", true, stretched > 10, 0);

            // The requirement that matters: nothing is drawn outside a mark. Content that
            // is neither tagged nor set aside as decoration is content a reader cannot
            // account for, and it is the one failure a valid-looking tree hides.
            int loose = LooseDrawing(streams, out int pagesRead);

            // The check has to have looked at something. A regex that matches nothing
            // reports success just as loudly as a section with no fault in it.
            AssertEqual(testName, "the page streams were actually read", true, pagesRead >= 4, pagesRead);
            AssertEqual(testName, "nothing is drawn outside a mark", 0, loose, 0);

            // A figure has to say what it is, or a reader announces an empty box.
            AssertEqual(testName, "figures carry their alternative text", true,
                streams.Contains("/S /Figure") && streams.Contains("/Alt (QR code"), 0);

            foreach ((string what, bool present) in carried)
                AssertEqual(testName, $"the section carries: {what}", true, present, 0);

            // The barcodes and the split row are geometry, not bytes, so they are counted
            // on the layout instead.
            AssertEqual(testName, "the section carries: two barcodes", 2,
                layout.Pages.Sum(p => p.Items.Count(i => i.Source is BarcodeItem)), 0);

            // These three draw as ordinary images and text once bound - by design, since
            // reusing what already renders a plain TextItem/ImageItem is the whole point
            // - so there is no byte in the PDF that says "this came from a grid" the way
            // /Subtype /Widget says "this is a form field". What is checked instead is
            // that binding kept the elements themselves, on the tree it actually built.
            AssertEqual(testName, "the section carries: an image grid", true,
                report.AllItems.OfType<ImageGridItem>().Any(g => g.Childs.Count == 6), 0);
            // It used to read "A. Method", a letter typed into the included file. The
            // heading is numbered by the report that splices it in now - which is only
            // possible because expansion happens before anything else, so by the time
            // the numbering walk runs there is no sub-template left, only a flat tree.
            AssertEqual(testName, "the section carries: a resolved sub-template", true,
                report.AllItems.OfType<SubTemplateItem>()
                    .Any(s => s.Childs.OfType<TextItem>()
                        .Any(t => t.Text.Contains("Method", StringComparison.Ordinal)
                               && t.Text.Contains("{HeadingNumber}", StringComparison.Ordinal))), 0);
            AssertEqual(testName, "the section carries: a mixed-style text", true,
                report.AllItems.OfType<AdvancedTextItem>()
                    .Any(a => a.Runs.Any(r => r.Font?.Bold == true)
                           && a.Runs.Any(r => r.Font?.Strikethrough == true)), 0);
            AssertEqual(testName, "and its token was resolved like any other", true,
                report.AllItems.OfType<AdvancedTextItem>()
                    .Any(a => a.Runs.Any(r => r.Text.StartsWith("Note for ") && !r.Text.Contains('{'))), 0);

            // Binding rebuilds the tree, so the row is found by what it holds rather than
            // by the reference the test started with.
            var longCell = report.AllItems.OfType<TextItem>()
                .OrderByDescending(t => t.Text.Length).First();

            // Long enough to outrun a whole page. Anything shorter can land as the first
            // item of a fresh page, where the flow places it whole rather than loop for
            // ever - and a row that was never asked to split proves nothing about
            // splitting.
            Console.WriteLine($"    the long row fits in "
                + $"{layout.Pages.Sum(p => p.Items.Count(i => ReferenceEquals(i.Source, longCell)))} morceaux");

            AssertEqual(testName, "the section carries: a split row", true,
                layout.Pages.Sum(p => p.Items.Count(i => ReferenceEquals(i.Source, longCell))) >= 2, 0);

            // And the half of it on the second page is still a whole row. The short
            // cell has no lines left there, so it used to be left out - which makes a
            // row of one cell in a table of two, and veraPDF is right to refuse it.
            ContainerItem splitRow = longCell.Parent!;
            var widths = layout.Pages
                .Select(page => splitRow.Childs.Count(cell =>
                    page.Items.Any(i => ReferenceEquals(i.Source, cell))))
                .Where(n => n > 0)
                .ToList();

            Console.WriteLine($"    the split row shows {string.Join(" puis ", widths)} cellules");
            AssertEqual(testName, "and each half keeps its cells", true,
                widths.Count >= 2 && widths.All(n => n == splitRow.Childs.Count), 0);

            // The two backends must turn the mark the same way. They did not: the SVG
            // rotated one way and the PDF matrix the other, so the preview ascended and
            // the printed page descended. Both were self-consistent, and every assertion
            // held, because nothing compared one output against the other.
            string svgPage = onScreen.Pages[0];
            double svgAngle = double.Parse(
                System.Text.RegularExpressions.Regex.Match(svgPage, @"rotate\((-?[\d.]+)\)").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);

            // SVG turns clockwise for a positive angle; the engine's own angle is the
            // other way round, hence the sign.
            double radians = -svgAngle * Math.PI / 180.0;
            double alongX = Math.Cos(radians), alongY = -Math.Sin(radians);

            var turned = System.Text.RegularExpressions.Regex
                .Matches(streams, @"([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) [-\d.]+ [-\d.]+ Tm")
                .Where(m => Math.Abs(Number(m.Groups[2].Value)) > 0.01)
                .ToList();

            AssertEqual(testName, "the PDF turns the watermark the right way", true, turned.Count > 20, turned.Count);
            AssertEqual(testName, "and the same way the SVG does", true,
                Math.Abs(Number(turned[0].Groups[1].Value) - alongX) < 0.01
                && Math.Abs(Number(turned[0].Groups[2].Value) - alongY) < 0.01, 0);

            // The alt text is what a reader announces in place of a picture, and it is
            // the one string nobody proofreads because nobody sees it - so it has to be
            // bound like everything else.
            var described = report.AllItems.OfType<BarcodeItem>().ToList();
            AssertEqual(testName, "every code has alternative text", described.Count,
                described.Count(b => !string.IsNullOrWhiteSpace(b.AltText)), 0);
            AssertEqual(testName, "and it carries the data", false,
                described.Any(b => b.AltText!.Contains('{')), 0);

            // Roles are declared, not guessed. A barcode is a figure whatever its font.
            AssertEqual(testName, "a barcode is tagged as a figure", PdfRole.Figure,
                PdfRoles.Resolve(described[0]), 0);

            // And the data reached the codes rather than the braces.
            AssertEqual(testName, "the codes carry the data", false,
                report.AllItems.OfType<BarcodeItem>().Any(b => b.Value.Contains('{')), 0);

            Console.WriteLine($"    {layout.PageCount} pages numérotées, {layout.SheetCount} faces, {bytes.Length / 1024} ko");
            Console.WriteLine($"  \u2714 one file to validate -> {Path.Combine(dir, "conformance.pdf")}");
            Console.WriteLine();
        }

        /// <summary>
        /// Where veraPDF might be. The environment variable first, so a machine can say
        /// so without this list growing; then the launcher it installs beside the home
        /// directory, under both names it uses.
        /// </summary>
        static string? FindVeraPdf()
        {
            string? named = Environment.GetEnvironmentVariable("FRISKET_VERAPDF");
            if (!string.IsNullOrWhiteSpace(named) && File.Exists(named))
                return named;

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string candidate in new[]
                     {
                         Path.Combine(home, "verapdf", "verapdf.bat"),   // the Windows installer
                         Path.Combine(home, "verapdf", "verapdf"),       // the same, elsewhere
                         "/usr/local/bin/verapdf",
                         "/opt/verapdf/verapdf"
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// The one check that has ever caught this class of fault, run by the suite
        /// rather than by whoever remembers to.
        ///
        /// Four defects reached a committed document with all the other tests green:
        /// an /Annots array pointing at object 0, a figure with no alternative text,
        /// headings starting at H2, and - the day this was written - a structure tree
        /// root reading /S /Section, which is not a type ISO 32000-1 defines. Every one
        /// was found by veraPDF, and CLAUDE.md &#167;4 asked a human to run it. That is a
        /// discipline, not a guard.
        ///
        /// Skipped rather than failed where veraPDF is not installed: it is a Java
        /// application at a path this machine chose, and a test that fails on a fresh
        /// clone teaches people to ignore it. The runner then says "skipped", which is
        /// the truth, where a silent pass would be the vacuous check this project
        /// refuses everywhere else.
        /// </summary>
        /// <param name="interactive">
        /// Whether the document keeps its form or is flattened into a picture of one.
        ///
        /// Both, since 25 August 2026, and the reason is the fault that got past this
        /// test: flattened output writes no widget, a Form structure element then has
        /// nothing under it, and PDF/UA 7.18.4 refuses the file - while the XMP goes on
        /// claiming PDF/UA-1 and the 3b profile passes. This test rendered only the
        /// fillable document, so the whole of one output mode was outside the only check
        /// that reads the file from outside.
        ///
        /// Four runs instead of two. That is eight seconds, against a class of fault
        /// nothing else here can see.
        /// </param>
        [SkippableTheory]
        [InlineData("3b", true)]
        [InlineData("ua1", true)]
        [InlineData("3b", false)]
        [InlineData("ua1", false)]
        public void Test53_VeraPdf(string flavour, bool interactive)
        {
            var testName = $"Test 53: veraPDF {flavour} {(interactive ? "fillable" : "flattened")}";
            Console.WriteLine($"Running {testName}...");

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null,
                "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH, "
                + "to have this run. See CLAUDE.md \u00a74.");

            // Rendered here rather than read off the disk. Validating whatever test 40
            // last wrote makes this depend on test order, and - worse - a stale file
            // passes: the first sabotage of this test was green because the document on
            // disk predated the defect being reinstated.
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            string examples = Path.Combine(RepositoryRoot(), "examples");
            Report report = Report.FromFile(Showcase.WriteFiles(examples));
            report = ReportBinder.Bind(report,
                File.ReadAllText(Path.Combine(examples, Showcase.DataFile)));

            var backend = new PdfBackend(library)
            {
                Conformance = PdfConformance.PdfA3b, Tagged = true, Interactive = interactive
            };

            // The attachment, which is the whole reason this profile is 3B and not 2B -
            // and which this test validated for three weeks without carrying. It produces
            // its own document rather than reading whichever one is lying around, which
            // is right and was written for a good reason (a stale file passed the first
            // sabotage of this test); the document it produced was simply a lesser one
            // than the proof that ships. A single file to validate is only worth
            // something if it really carries everything, and the shapes it lacks are
            // exactly the ones no profile can refuse.
            backend.AttachFacturX(FacturXXml());

            ReportRenderer.Render(report, backend);

            string pdf = Path.Combine(OutputDirectory("conformance"),
                $"validated-{flavour}-{(interactive ? "fillable" : "flat")}.pdf");
            File.WriteAllBytes(pdf, backend.ToArray());

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(verapdf!)
                {
                    // --format text prints one line: PASS or FAIL, then the path.
                    Arguments = $"--format text --flavour {flavour} \"{pdf}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(180_000);

            Console.WriteLine($"    {output.Trim()}");

            // The word, not the exit code: veraPDF exits non-zero for a refused document
            // and for a missing one alike, and the two are not the same news.
            AssertEqual(testName, $"the document is valid {flavour}", true,
                output.Contains("PASS", StringComparison.Ordinal), 0);
        }
    }
}
