namespace test
{
    /// <summary>
    /// What a face becomes inside the PDF: cut to the glyphs drawn, renumbered, with or
    /// without its hinting, and in the shape ISO 32000-1 9.9 allows for its outlines. Read
    /// back out of the file and held to the face it came from, glyph by glyph - never
    /// trusted from what the writer meant to write.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>Accented letters, every one a composite in the faces shipped here.</summary>
        private const string Accented = "Facture n° 2026-114 : montant dû 1 037,50 €, à régler à l'échéance. Ça a été ÉTÉ, À BIENTÔT.";

        /// <summary>
        /// The subset is renumbered and still draws the same glyphs.
        ///
        /// Renumbering is safe only if four things still reach each other: the code on the
        /// page, the CIDToGIDMap that turns it into a glyph of the subset, that glyph's
        /// outline, and its width in the widths array. Each is asked here of the file, and
        /// the outline is compared with the face's own, a composite through its components -
        /// a component left pointing at its old number draws some other glyph's shape, on
        /// accented letters alone, and no validator looks.
        /// </summary>
        [Fact]
        public void Test117_AnEmbeddedFaceIsCutRenumberedAndDrawsTheSameGlyphs()
        {
            const string testName = "Test 117";
            Console.WriteLine("Test 117: an embedded face is cut, renumbered, and draws the same glyphs");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;

            // Two faces: the default, and one whose hmtx stops short of its glyphs - the rest
            // share the last advance and carry only a side bearing, which the subset has to
            // read from another place. Found by that property rather than named, and the
            // property is asked to be reached below, or the second face proves nothing.
            FontFile shortMetrics = library.Fonts.Where(f => !f.Bold && !f.Italic)
                .First(f => new Sfnt(f.Data) is { } s && s.Has("glyf") && s.LongMetrics < s.GlyphCount);
            int pastLongMetrics = 0;

            foreach (FontFile file in new[] { library.Resolve(DmsFont, false, false)!, shortMetrics })
            {
                var face = new Sfnt(file.Data);
                Embedded font = EmbeddedFaces(Rendered(library, Paragraphs(Accented, file.Family), b => { })).Single();
                var subset = new Sfnt(font.Program);

                Console.WriteLine($"    {file.Family}: {face.GlyphCount} glyphs in the face, {subset.GlyphCount} in the subset, "
                                  + $"{font.Codes.Count} codes on the page, {font.Program.Length} bytes");

                // --- the matter: composites drawn, and the numbering really changed ----
                int composites = font.Codes.Count(c => face.IsComposite(c));
                AssertEqual(testName, $"{file.Family}: the page draws composite glyphs", true, composites >= 5, composites);
                AssertEqual(testName, $"{file.Family}: the subset is renumbered", true, font.Map != null, 0);
                AssertEqual(testName, $"{file.Family}: and numbers only what it holds", true,
                    subset.GlyphCount < face.GlyphCount / 5, subset.GlyphCount);

                // --- every code reaches the glyph it was written for --------------------
                foreach (int code in font.Codes)
                {
                    int glyph = font.GlyphOf(code);
                    AssertEqual(testName, $"{file.Family}: code {code} reaches a glyph other than .notdef", true,
                        glyph > 0 && glyph < subset.GlyphCount, glyph);
                    AssertEqual(testName, $"{file.Family}: code {code} draws the face's glyph {code}", true,
                        SameGlyph(face, code, subset, glyph), 0);
                    AssertEqual(testName, $"{file.Family}: code {code} has that advance in the widths array",
                        Thousandths(face, code), font.Widths[code], 0);

                    // The metrics of the glyph and of every component under it.
                    foreach ((int was, int now) in Pairs(face, code, subset, glyph))
                    {
                        AssertEqual(testName, $"{file.Family}: glyph {was} keeps its advance as {now}",
                            face.Advance(was), subset.Advance(now), 0);
                        AssertEqual(testName, $"{file.Family}: glyph {was} keeps its side bearing as {now}",
                            face.Bearing(was), subset.Bearing(now), 0);
                        if (was >= face.LongMetrics)
                            pastLongMetrics++;
                    }
                }

                // --- and nothing else is in it ------------------------------------------
                var reached = new HashSet<int> { 0 };
                var pending = new Queue<int>(font.Codes.Select(font.GlyphOf));
                while (pending.Count > 0)
                {
                    int g = pending.Dequeue();
                    if (reached.Add(g))
                        foreach (int part in subset.Components(g))
                            pending.Enqueue(part);
                }
                AssertEqual(testName, $"{file.Family}: every glyph of the subset is drawn or part of one drawn",
                    subset.GlyphCount, reached.Count, 0);
                AssertEqual(testName, $"{file.Family}: glyph 0 is still .notdef", true, SameGlyph(face, 0, subset, 0), 0);
                AssertEqual(testName, $"{file.Family}: hhea counts every glyph's metrics",
                    subset.GlyphCount, subset.LongMetrics, 0);

                // --- the tables ISO 32000-1 9.9 names, and only those -------------------
                string[] nine = { "head", "hhea", "loca", "maxp", "cvt ", "prep", "glyf", "hmtx", "fpgm" };
                AssertEqual(testName, $"{file.Family}: the subset keeps the nine tables the face has, and no other",
                    string.Join(",", nine.Where(face.Has).Order()), string.Join(",", subset.Tags.Order()), 0);
                AssertEqual(testName, $"{file.Family}: the face has hinting tables for that to be about", true,
                    face.Has("prep"), 0);
                AssertEqual(testName, $"{file.Family}: and they travel unchanged", true,
                    face.Table("prep").SequenceEqual(subset.Table("prep")), 0);
                AssertEqual(testName, $"{file.Family}: a partial font says so: {font.BaseFont}", true,
                    System.Text.RegularExpressions.Regex.IsMatch(font.BaseFont, "^[A-Z]{6}\\+"), 0);

                // --- embedded whole: every glyph, its own numbers, no cmap --------------
                Embedded whole = EmbeddedFaces(Rendered(library, Paragraphs(Accented, file.Family),
                    b => b.Subset = false)).Single();
                var full = new Sfnt(whole.Program);
                AssertEqual(testName, $"{file.Family}: whole, every glyph is there", face.GlyphCount, full.GlyphCount, 0);
                AssertEqual(testName, $"{file.Family}: whole, the codes are the glyphs", true, whole.Map == null, 0);
                AssertEqual(testName, $"{file.Family}: whole, no cmap - ISO 32000-1 9.9 forbids one with a CIDFont",
                    false, full.Has("cmap"), 0);
                AssertEqual(testName, $"{file.Family}: whole, every other table is kept",
                    string.Join(",", face.Tags.Where(t => t is not ("cmap" or "DSIG")).Order()),
                    string.Join(",", full.Tags.Order()), 0);
                AssertEqual(testName, $"{file.Family}: whole, the name takes no subset tag: {whole.BaseFont}",
                    false, whole.BaseFont.Contains('+'), 0);

                Console.WriteLine($"      whole {whole.Program.Length} bytes; {composites} composites, each reaching its own glyph");
            }

            AssertEqual(testName, "a glyph past the long metrics was reached", true, pastLongMetrics > 0, pastLongMetrics);
            Console.WriteLine($"    {pastLongMetrics} glyph(s) read past the long metrics");
            Console.WriteLine();
        }

        /// <summary>A glyph of one face and its counterpart in another, then each component's, all the way down.</summary>
        private static IEnumerable<(int Was, int Now)> Pairs(Sfnt a, int ga, Sfnt b, int gb)
        {
            yield return (ga, gb);
            if (!a.IsComposite(ga) || !b.IsComposite(gb))
                yield break;
            foreach (var (x, y) in a.Placements(ga).Zip(b.Placements(gb)))
                foreach (var pair in Pairs(a, x.Glyph, b, y.Glyph))
                    yield return pair;
        }

        /// <summary>
        /// Leaving the hinting out takes each glyph's instructions and nothing else.
        ///
        /// None of the faces shipped here is hinted, so a check run on them would pass with
        /// the option doing nothing at all. The face is made hinted for the test instead:
        /// a small program in every glyph, and an fpgm and a cvt beside the prep it has. Then
        /// with the hinting left out every glyph has to come back exactly as the face was
        /// before the program went in - outline, components and all - while cvt, fpgm and
        /// prep travel unchanged, because ISO 32000-1 9.9 wants them whenever the face had them.
        /// </summary>
        [Fact]
        public void Test118_HintingCanBeLeftOutAndTheHintingTablesStay()
        {
            const string testName = "Test 118";
            Console.WriteLine("Test 118: the hinting inside each glyph can be left out, and the hinting tables stay");
            Console.WriteLine();

            var shipped = new FontLibrary();
            shipped.RegisterDirectory(FontDirectory());
            byte[] plainBytes = File.ReadAllBytes(shipped.Resolve(DmsFont, false, false)!.Source);
            var plain = new Sfnt(plainBytes);
            var hinted = new Sfnt(Hinted(plainBytes));

            var library = new FontLibrary();
            library.Register(hinted.Data);
            library.DefaultFamily = DmsFont;

            Embedded kept = EmbeddedFaces(Rendered(library, Paragraphs(Accented), b => { })).Single();
            Embedded left = EmbeddedFaces(Rendered(library, Paragraphs(Accented), b => b.Hinting = false)).Single();
            var withHints = new Sfnt(kept.Program);
            var without = new Sfnt(left.Program);

            Console.WriteLine($"    hinting kept {kept.Program.Length} bytes, left out {left.Program.Length} bytes");

            // --- the matter: the face really is hinted, and the default keeps it --------
            AssertEqual(testName, "on by default", true, new PdfBackend(library).Hinting, 0);
            int programs = Enumerable.Range(1, withHints.GlyphCount - 1).Count(g => withHints.Instructions(g) > 0);
            AssertEqual(testName, "kept, every glyph with an outline carries its program", true,
                programs >= withHints.GlyphCount - 2, programs);
            foreach (int code in kept.Codes)
                AssertEqual(testName, $"kept, code {code} is the hinted face's glyph, program and all", true,
                    SameGlyph(hinted, code, withHints, kept.GlyphOf(code)), 0);

            // --- left out: the instructions go, and only they -------------------------
            for (int g = 0; g < without.GlyphCount; g++)
                AssertEqual(testName, $"left out, glyph {g} carries no instructions", 0, without.Instructions(g), 0);
            foreach (int code in left.Codes)
                AssertEqual(testName, $"left out, code {code} is the face as it was before it was hinted", true,
                    SameGlyph(plain, code, without, left.GlyphOf(code)), 0);

            foreach (string table in new[] { "cvt ", "fpgm", "prep" })
            {
                AssertEqual(testName, $"left out, {table.Trim()} stays, unchanged", true,
                    without.Has(table) && hinted.Table(table).SequenceEqual(without.Table(table)), 0);
            }

            // Smaller, and not by a sum of three bytes a glyph: each outline is padded to four,
            // which can swallow a whole program.
            AssertEqual(testName, "left out, the font is smaller", true,
                left.Program.Length < kept.Program.Length, left.Program.Length);

            // --- a face embedded whole is whole -------------------------------------------
            var whole = new Sfnt(EmbeddedFaces(Rendered(library, Paragraphs(Accented),
                b => { b.Subset = false; b.Hinting = false; })).Single().Program);
            int code0 = kept.Codes.First(c => hinted.Instructions(c) > 0);
            AssertEqual(testName, "whole, the hinting stays even when asked to go", hinted.Instructions(code0),
                whole.Instructions(code0), 0);

            Console.WriteLine($"    {programs} glyph programs left out, cvt fpgm prep kept, "
                              + $"{left.Codes.Count} codes drawing the unhinted outlines");
            Console.WriteLine();
        }

        /// <summary>
        /// A face with PostScript outlines goes in as OpenType, the way ISO 32000-1 allows.
        ///
        /// It went in under /FontFile2 for as long as the engine embedded fonts, read by a
        /// CIDFontType2 - which is for TrueType outlines alone - and veraPDF passed it on
        /// both profiles. Readers built on FreeType sniff the format and draw it anyway, which
        /// is how it was never seen; a reader that follows the standard has no reason to.
        ///
        /// And one kind it cannot embed is refused by name: a face keyed by CID chooses its
        /// glyphs through its charset, so the codes would have to be CIDs. No face here is
        /// one, so the test makes one, and makes the same face without the key beside it, so
        /// that the refusal is seen to turn on the key and not on the face being made.
        /// </summary>
        [Fact]
        public void Test119_AFaceWithPostScriptOutlinesGoesInAsOpenType()
        {
            const string testName = "Test 119";
            Console.WriteLine("Test 119: a face with PostScript outlines goes in as OpenType");
            Console.WriteLine();

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            FontFile glametrix = library.Resolve("Glametrix", false, false)!;
            var face = new Sfnt(File.ReadAllBytes(glametrix.Source));
            AssertEqual(testName, "the face has PostScript outlines", true, face.Has("CFF ") && !face.Has("glyf"), 0);

            byte[] pdf = Rendered(library, Paragraphs(Accented, "Glametrix"), b => { });
            Embedded font = EmbeddedFaces(pdf).Single();
            var program = new Sfnt(font.Program);

            Console.WriteLine($"    {font.Kind}, {font.FileKey}: {face.Data.Length} bytes in the face, "
                              + $"{font.Program.Length} embedded");

            AssertEqual(testName, "read by a CIDFontType0", "CIDFontType0", font.Kind, 0);
            AssertEqual(testName, "from /FontFile3", "FontFile3", font.FileKey, 0);
            AssertEqual(testName, "marked OpenType", true, font.OpenType, 0);
            AssertEqual(testName, "with no CIDToGIDMap, which belongs to a CIDFontType2", false,
                font.Descendant.Contains("/CIDToGIDMap"), 0);
            AssertEqual(testName, "an OpenType file with PostScript outlines", "OTTO",
                System.Text.Encoding.ASCII.GetString(font.Program, 0, 4), 0);

            string[] required = { "CFF ", "OS/2", "cmap", "head", "hhea", "hmtx", "maxp", "name", "post" };
            AssertEqual(testName, "holding the tables OpenType requires, and no other",
                string.Join(",", required.Where(face.Has).Order()), string.Join(",", program.Tags.Order()), 0);
            AssertEqual(testName, "the face had kerning to leave out", true, face.Has("kern") && face.Has("GPOS"), 0);
            // All but head's checksum adjustment, which is over the whole file and so has to
            // change with the tables around it.
            static byte[] Unsummed(Sfnt sfnt, string table)
            {
                byte[] bytes = sfnt.Table(table);
                if (table == "head")
                    bytes.AsSpan(8, 4).Clear();
                return bytes;
            }
            foreach (string table in required.Where(face.Has))
                AssertEqual(testName, $"{table.Trim()} travels unchanged", true,
                    Unsummed(face, table).SequenceEqual(Unsummed(program, table)), 0);
            AssertEqual(testName, $"every glyph is there, so no subset tag: {font.BaseFont}", false, font.BaseFont.Contains('+'), 0);
            foreach (int code in font.Codes)
                AssertEqual(testName, $"code {code} has the face's advance", Thousandths(face, code), font.Widths[code], 0);

            Embedded whole = EmbeddedFaces(Rendered(library, Paragraphs(Accented, "Glametrix"), b => b.Subset = false)).Single();
            AssertEqual(testName, "whole, every table but the signature", string.Join(",", face.Tags.Where(t => t != "DSIG").Order()),
                string.Join(",", new Sfnt(whole.Program).Tags.Order()), 0);

            // --- keyed by CID: refused, by name -------------------------------------------
            byte[] keyed = WithTable(face.Data, "CFF ", MinimalCff(cidKeyed: true));
            byte[] named = WithTable(face.Data, "CFF ", MinimalCff(cidKeyed: false));

            Exception? refusal = Record.Exception(() => Rendered(Only(keyed), Paragraphs("Specimen", "Glametrix"), b => { }));
            Console.WriteLine($"    keyed by CID -> {refusal?.Message[..Math.Min(90, refusal.Message.Length)] ?? "written"}...");
            AssertEqual(testName, "a face keyed by CID is refused", true, refusal is InvalidOperationException, 0);
            AssertEqual(testName, "naming the face and what it is", true,
                refusal!.Message.Contains("Glametrix") && refusal.Message.Contains("keyed by CID"), 0);
            AssertEqual(testName, "and the same face without the key is written", "written",
                Record.Exception(() => Rendered(Only(named), Paragraphs("Specimen", "Glametrix"), b => { }))?.Message ?? "written", 0);

            Console.WriteLine();

            static FontLibrary Only(byte[] bytes)
            {
                var only = new FontLibrary();
                only.Register(bytes);
                return only;
            }
        }

        /// <summary>
        /// The three new shapes shown to the validator, in one document: a renumbered
        /// TrueType subset with its glyph map, a face without its glyph programs, and a face
        /// with PostScript outlines as OpenType. veraPDF passed the old PostScript embedding
        /// too, so this proves no more than it says - that the new ones break no rule it
        /// checks, widths against glyphs among them.
        /// </summary>
        [SkippableTheory]
        [InlineData("3b")]
        [InlineData("ua1")]
        public void Test120_TheEmbeddedFacesAreValid(string flavour)
        {
            string testName = $"Test 120: veraPDF {flavour} on the embedded faces";
            Console.WriteLine($"Running {testName}...");

            string? verapdf = FindVeraPdf();
            Skip.If(verapdf == null, "veraPDF was not found. Set FRISKET_VERAPDF to its launcher, or put it on PATH.");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            byte[] hinted = Hinted(File.ReadAllBytes(library.Resolve(DmsFont, false, false)!.Source));
            library.Register(WithNames(hinted, NameTable(new[]
            {
                (3, 1, 0x0409, 1, "Hinted Sans"), (3, 1, 0x0409, 2, "Regular")
            })));

            Report report = Paragraphs(Accented);
            foreach (string family in new[] { "Glametrix", "Hinted Sans" })
                report.Body!.Add(new TextItem { Text = Accented, Font = new() { Family = family } });
            report.Reattach();

            var pdf = new PdfBackend(library)
            {
                Tagged = true, Conformance = PdfConformance.PdfA3b, Language = "fr-FR", Hinting = false, Compress = false
            };
            ReportRenderer.Render(report, pdf);
            byte[] bytes = pdf.ToArray();

            // What is about to be validated has to hold what it is meant to prove (section 4).
            List<Embedded> faces = EmbeddedFaces(bytes);
            AssertEqual(testName, "the document carries a renumbered subset", true, faces.Count(f => f.Map != null) >= 2, 0);
            AssertEqual(testName, "and a face as OpenType", true, faces.Any(f => f.OpenType), 0);
            AssertEqual(testName, "and a subset whose glyphs carry no program", true,
                faces.Where(f => f.BaseFont.Contains("Hinted")).Select(f => new Sfnt(f.Program))
                     .Any(s => s.Has("fpgm") && Enumerable.Range(0, s.GlyphCount).All(g => s.Instructions(g) == 0)), 0);

            string path = Path.Combine(Path.GetTempPath(), $"frisket-fonts-{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(path, bytes);

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

        // ---- the documents ---------------------------------------------------------------

        private static Report Paragraphs(string text, string? family = null)
        {
            var report = new Report
            {
                Title = "Embedded faces", Language = "fr-FR", GeneratedAt = SampleDate,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            report.Body!.Add(new TextItem { Text = text, Font = new() { Family = family } });
            report.Reattach();
            return report;
        }

        /// <summary>A glyph's advance as the widths array writes it, in the same arithmetic order.</summary>
        private static int Thousandths(Sfnt face, int glyph)
            => (int)Math.Round((double)face.Advance(glyph) / face.UnitsPerEm * 1000);

        private static byte[] Rendered(FontLibrary library, Report report, Action<PdfBackend> set)
        {
            var pdf = new PdfBackend(library) { Compress = false };
            set(pdf);
            ReportRenderer.Render(report, pdf);
            return pdf.ToArray();
        }

        // ---- the file, read back -----------------------------------------------------------

        private sealed record Embedded(string BaseFont, string Kind, string FileKey, bool OpenType, string Descendant,
                                       byte[] Program, byte[]? Map, Dictionary<int, int> Widths)
        {
            /// <summary>The codes the page draws with this face: those the widths array lists.</summary>
            public List<int> Codes => Widths.Keys.Order().ToList();

            public int GlyphOf(int code) => Map == null ? code
                : code * 2 + 1 < Map.Length ? (Map[code * 2] << 8) | Map[code * 2 + 1] : 0;
        }

        /// <summary>Every Type0 font of an uncompressed file, followed down to its program.</summary>
        private static List<Embedded> EmbeddedFaces(byte[] pdf)
        {
            string raw = System.Text.Encoding.Latin1.GetString(pdf);
            string Body(int obj)
            {
                int at = raw.IndexOf("\n" + obj + " 0 obj", StringComparison.Ordinal);
                return raw[at..raw.IndexOf("endobj", at, StringComparison.Ordinal)];
            }

            var faces = new List<Embedded>();
            foreach (System.Text.RegularExpressions.Match top in System.Text.RegularExpressions.Regex.Matches(raw,
                         @"/Subtype /Type0 /BaseFont /(\S+) /Encoding /Identity-H /DescendantFonts \[(\d+) 0 R\]"))
            {
                string descendant = Body(int.Parse(top.Groups[2].Value));
                string kind = System.Text.RegularExpressions.Regex.Match(descendant, @"/Subtype /(CIDFontType\d)").Groups[1].Value;
                string descriptor = Body(int.Parse(System.Text.RegularExpressions.Regex.Match(descendant, @"/FontDescriptor (\d+) 0 R").Groups[1].Value));
                var file = System.Text.RegularExpressions.Regex.Match(descriptor, @"/(FontFile\d?) (\d+) 0 R");
                int stream = int.Parse(file.Groups[2].Value);

                var map = System.Text.RegularExpressions.Regex.Match(descendant, @"/CIDToGIDMap (\d+) 0 R");
                // Nothing after the widths array holds "n [m]", so reading on to the end of the
                // dictionary takes the array whole, nested brackets and all.
                var widths = System.Text.RegularExpressions.Regex.Matches(
                        descendant[descendant.IndexOf("/W [", StringComparison.Ordinal)..], @"(\d+) \[(-?\d+)\]")
                    .ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));

                faces.Add(new Embedded(top.Groups[1].Value, kind, file.Groups[1].Value,
                    Body(stream).Contains("/Subtype /OpenType"), descendant, StreamOf(pdf, raw, stream),
                    map.Success ? StreamOf(pdf, raw, int.Parse(map.Groups[1].Value)) : null, widths));
            }

            return faces;
        }

        /// <summary>
        /// Whether a glyph of one face and a glyph of another draw the same thing: the same
        /// bytes for an outline, and for a composite the same placements of components that
        /// are themselves the same, whatever each face numbers them. Padding is not drawing.
        /// </summary>
        private static bool SameGlyph(Sfnt a, int ga, Sfnt b, int gb)
        {
            byte[] x = a.Glyph(ga), y = b.Glyph(gb);
            if (x.Length == 0 || y.Length == 0)
                return x.Length == y.Length;
            if (!a.IsComposite(ga))
                return !b.IsComposite(gb) && Trimmed(x).SequenceEqual(Trimmed(y));
            if (!b.IsComposite(gb))
                return false;

            List<(int Glyph, int At)> pa = a.Placements(ga), pb = b.Placements(gb);
            if (pa.Count != pb.Count || !x.AsSpan(0, 10).SequenceEqual(y.AsSpan(0, 10)))
                return false;

            for (int i = 0; i < pa.Count; i++)
            {
                if (!SameGlyph(a, pa[i].Glyph, b, pb[i].Glyph))
                    return false;

                // The record around the glyph id: flags, then arguments and any scale, up to
                // the next record or the instructions.
                int endA = i + 1 < pa.Count ? pa[i + 1].At : a.ComponentsEnd(ga);
                int endB = i + 1 < pb.Count ? pb[i + 1].At : b.ComponentsEnd(gb);
                if (!x.AsSpan(pa[i].At, 2).SequenceEqual(y.AsSpan(pb[i].At, 2))
                    || !x.AsSpan(pa[i].At + 4, endA - pa[i].At - 4).SequenceEqual(y.AsSpan(pb[i].At + 4, endB - pb[i].At - 4)))
                    return false;
            }

            return Trimmed(x[a.ComponentsEnd(ga)..]).SequenceEqual(Trimmed(y[b.ComponentsEnd(gb)..]));

            static byte[] Trimmed(byte[] bytes)
            {
                int end = bytes.Length;
                while (end > 0 && bytes[end - 1] == 0) end--;
                return bytes[..end];
            }
        }

        /// <summary>Just enough of a TrueType or OpenType file to hold an embedded face to its original.</summary>
        private sealed class Sfnt
        {
            public byte[] Data { get; }
            private readonly Dictionary<string, (int Offset, int Length)> _tables = new();

            public Sfnt(byte[] data)
            {
                Data = data;
                int count = U16(4);
                for (int i = 0; i < count; i++)
                {
                    int r = 12 + i * 16;
                    _tables[System.Text.Encoding.ASCII.GetString(data, r, 4)] = ((int)U32(r + 8), (int)U32(r + 12));
                }
            }

            public IEnumerable<string> Tags => _tables.Keys;
            public bool Has(string tag) => _tables.ContainsKey(tag);
            public byte[] Table(string tag) => Data.AsSpan(_tables[tag].Offset, _tables[tag].Length).ToArray();

            public int GlyphCount => U16(_tables["maxp"].Offset + 4);
            public int UnitsPerEm => U16(_tables["head"].Offset + 18);
            public int LongMetrics => U16(_tables["hhea"].Offset + 34);

            public int Advance(int glyph) => U16(_tables["hmtx"].Offset + Math.Min(glyph, LongMetrics - 1) * 4);

            /// <summary>The left side bearing: beside the advance, or past the advances on its own.</summary>
            public int Bearing(int glyph) => (short)(glyph < LongMetrics
                ? U16(_tables["hmtx"].Offset + glyph * 4 + 2)
                : U16(_tables["hmtx"].Offset + LongMetrics * 4 + (glyph - LongMetrics) * 2));

            public byte[] Glyph(int glyph)
            {
                bool longOffsets = (short)U16(_tables["head"].Offset + 50) == 1;
                int loca = _tables["loca"].Offset;
                int Offset(int g) => longOffsets ? (int)U32(loca + g * 4) : U16(loca + g * 2) * 2;
                int start = Offset(glyph), end = Offset(glyph + 1);
                return Data.AsSpan(_tables["glyf"].Offset + start, Math.Max(0, end - start)).ToArray();
            }

            public bool IsComposite(int glyph)
            {
                byte[] g = Glyph(glyph);
                return g.Length > 0 && (short)((g[0] << 8) | g[1]) < 0;
            }

            /// <summary>Each component's glyph and where its record starts.</summary>
            public List<(int Glyph, int At)> Placements(int glyph)
            {
                byte[] g = Glyph(glyph);
                var found = new List<(int, int)>();
                int p = 10;
                while (true)
                {
                    int flags = (g[p] << 8) | g[p + 1];
                    found.Add(((g[p + 2] << 8) | g[p + 3], p));
                    p += 4 + ((flags & 1) != 0 ? 4 : 2);
                    p += (flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0;
                    if ((flags & 0x20) == 0)
                        return found;
                }
            }

            public IEnumerable<int> Components(int glyph)
                => IsComposite(glyph) ? Placements(glyph).Select(p => p.Glyph) : Enumerable.Empty<int>();

            /// <summary>Where a composite's records end and its instructions, if any, begin.</summary>
            public int ComponentsEnd(int glyph)
            {
                byte[] g = Glyph(glyph);
                int p = Placements(glyph)[^1].At;
                int flags = (g[p] << 8) | g[p + 1];
                p += 4 + ((flags & 1) != 0 ? 4 : 2);
                return p + ((flags & 8) != 0 ? 2 : (flags & 0x40) != 0 ? 4 : (flags & 0x80) != 0 ? 8 : 0);
            }

            /// <summary>How many bytes of instructions a glyph carries.</summary>
            public int Instructions(int glyph)
            {
                byte[] g = Glyph(glyph);
                if (g.Length == 0)
                    return 0;
                if (!IsComposite(glyph))
                {
                    int at = 10 + 2 * (short)((g[0] << 8) | g[1]);
                    return (g[at] << 8) | g[at + 1];
                }

                int last = Placements(glyph)[^1].At;
                if ((((g[last] << 8) | g[last + 1]) & 0x100) == 0)
                    return 0;
                int end = ComponentsEnd(glyph);
                return (g[end] << 8) | g[end + 1];
            }

            private int U16(int at) => (Data[at] << 8) | Data[at + 1];
            private uint U32(int at) => (uint)((Data[at] << 24) | (Data[at + 1] << 16) | (Data[at + 2] << 8) | Data[at + 3]);
        }

        // ---- faces made for a test ---------------------------------------------------------

        /// <summary>PUSHB[0] 7, POP: does nothing, and takes room, which is what a test of its removal needs.</summary>
        private static readonly byte[] GlyphProgram = { 0xB0, 0x07, 0x21 };

        /// <summary>
        /// The same face hinted: the program above in every glyph that has an outline, an
        /// fpgm defining one empty function and a cvt of two values, beside whatever prep it
        /// had, and maxp raised to cover them.
        /// </summary>
        private static byte[] Hinted(byte[] font)
        {
            var face = new Sfnt(font);
            var glyf = new MemoryStream();
            var offsets = new List<uint>();

            for (int g = 0; g < face.GlyphCount; g++)
            {
                offsets.Add((uint)glyf.Length);
                byte[] outline = face.Glyph(g);
                if (outline.Length == 0)
                    continue;

                byte[] hinted;
                if (!face.IsComposite(g))
                {
                    int at = 10 + 2 * (short)((outline[0] << 8) | outline[1]);
                    int length = (outline[at] << 8) | outline[at + 1];
                    hinted = outline[..at]
                        .Concat(new[] { (byte)((length + 3) >> 8), (byte)(length + 3) })
                        .Concat(outline[(at + 2)..(at + 2 + length)]).Concat(GlyphProgram)
                        .Concat(outline[(at + 2 + length)..]).ToArray();
                }
                else if (face.Instructions(g) == 0)
                {
                    int end = face.ComponentsEnd(g);
                    hinted = outline[..end].Concat(new byte[] { 0, 3 }).Concat(GlyphProgram).ToArray();
                    int last = face.Placements(g)[^1].At;
                    hinted[last] |= 0x01;                                   // WE_HAVE_INSTRUCTIONS, 0x100
                }
                else hinted = outline;

                glyf.Write(hinted);
                while (glyf.Length % 4 != 0)
                    glyf.WriteByte(0);
            }
            offsets.Add((uint)glyf.Length);

            var tables = face.Tags.ToDictionary(t => t, face.Table);
            tables["glyf"] = glyf.ToArray();
            tables["loca"] = offsets.SelectMany(o => new[] { (byte)(o >> 24), (byte)(o >> 16), (byte)(o >> 8), (byte)o }).ToArray();
            tables["head"][50] = 0;
            tables["head"][51] = 1;                                        // long loca
            tables["fpgm"] = new byte[] { 0xB0, 0x00, 0x2C, 0x2D };        // PUSHB[0] 0, FDEF, ENDF
            tables["cvt "] = new byte[] { 0x00, 0x10, 0x00, 0x20 };

            byte[] maxp = tables["maxp"];
            void AtLeast(int at, int value)
            {
                int current = (maxp[at] << 8) | maxp[at + 1];
                int set = Math.Max(current, value);
                maxp[at] = (byte)(set >> 8);
                maxp[at + 1] = (byte)set;
            }
            AtLeast(20, 1);                                                // maxFunctionDefs
            AtLeast(24, 2);                                                // maxStackElements
            AtLeast(26, ((maxp[26] << 8) | maxp[27]) + GlyphProgram.Length); // maxSizeOfInstructions

            return Assembled(tables, 0x00010000);
        }

        /// <summary>
        /// The smallest CFF table a reader could open: a header, one name, a Top DICT, two
        /// strings and no subroutines. Keyed by CID when its Top DICT names a registry,
        /// ordering and supplement; the alternative carries a version in that place, so the
        /// two differ by that and nothing else.
        /// </summary>
        private static byte[] MinimalCff(bool cidKeyed)
        {
            byte[] dict = cidKeyed
                ? new byte[] { 0x1C, 0x01, 0x87, 0x1C, 0x01, 0x88, 0x8B, 0x0C, 0x1E }   // 391 392 0 ROS
                : new byte[] { 0x1C, 0x01, 0x87, 0x00 };                               // 391 version

            var cff = new List<byte> { 1, 0, 4, 1 };                                   // header
            cff.AddRange(new byte[] { 0, 1, 1, 1, 2, (byte)'A' });                     // Name INDEX: "A"
            cff.AddRange(new byte[] { 0, 1, 1, 1, (byte)(dict.Length + 1) });          // Top DICT INDEX
            cff.AddRange(dict);
            cff.AddRange(new byte[] { 0, 2, 1, 1, 6, 14 });                            // String INDEX
            cff.AddRange(System.Text.Encoding.ASCII.GetBytes("AdobeIdentity"));
            cff.AddRange(new byte[] { 0, 0 });                                         // no global subroutines
            return cff.ToArray();
        }

        /// <summary>The same font with one table replaced, written out afresh.</summary>
        private static byte[] WithTable(byte[] font, string tag, byte[] table)
        {
            var face = new Sfnt(font);
            var tables = face.Tags.ToDictionary(t => t, face.Table);
            tables[tag] = table;
            return Assembled(tables, (uint)((font[0] << 24) | (font[1] << 16) | (font[2] << 8) | font[3]));
        }

        /// <summary>An sfnt file from its tables. No checksums: nothing here reads them.</summary>
        private static byte[] Assembled(Dictionary<string, byte[]> tables, uint version)
        {
            string[] tags = tables.Keys.Order(StringComparer.Ordinal).ToArray();
            var file = new List<byte>();
            void U16(int v) { file.Add((byte)(v >> 8)); file.Add((byte)v); }
            void U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }

            int selector = (int)Math.Floor(Math.Log2(tags.Length));
            U32(version);
            U16(tags.Length);
            U16(16 << selector);
            U16(selector);
            U16(tags.Length * 16 - (16 << selector));

            int offset = 12 + tags.Length * 16;
            foreach (string tag in tags)
            {
                file.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
                U32(0);
                U32((uint)offset);
                U32((uint)tables[tag].Length);
                offset += (tables[tag].Length + 3) & ~3;
            }

            foreach (string tag in tags)
            {
                file.AddRange(tables[tag]);
                while (file.Count % 4 != 0)
                    file.Add(0);
            }

            return file.ToArray();
        }
    }
}
