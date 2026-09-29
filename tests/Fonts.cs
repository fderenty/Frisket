using System.Text.Json.Nodes;

namespace test
{
    /// <summary>
    /// What the engine takes from the outline rather than from what a font says about itself:
    /// a size asked for in millimetres of cap height or in rem, the cascade that resolves it,
    /// measured metrics, the gallery, family names and the clashes between them, and a font
    /// handed over as bytes.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>
        /// A measurer standing in for a typeface whose capitals are unusually small
        /// relative to the em, to check that sizing in millimeters cancels that out.
        /// </summary>
        sealed class NarrowCapMeasurer : ITextMeasurer
        {
            public FontMetrics GetMetrics(TextStyle style)
            {
                double em = style.EmSizeInMillimeters;
                return new FontMetrics(em, em * 0.40, em * 0.30, em * 0.80, em * 0.20, em * (style.LineSpacing ?? 1.2));
            }

            public TextMetrics Measure(string text, TextStyle style, double availableWidth)
            {
                double lineHeight = style.EmSizeInMillimeters * (style.LineSpacing ?? 1.2);
                return new TextMetrics(text.Length * style.EmSizeInMillimeters * 0.5, lineHeight, lineHeight, 1);
            }

            public IReadOnlyList<string> BreakLines(string text, TextStyle style, double availableWidth)
                => new[] { text };
        }

        [Fact]
        public void Test7_FontSizing()
        {
            var testName = "Test 7: Font Sizing in Millimeters";
            Console.WriteLine($"Running {testName}...");

            var r = new Report
            {
                Title = testName,
                Dimension = new() { Width = 210, Height = 297 },
                RootFontSize = 3.0,
                Body = new()
            };

            var millimeters = new TextItem
            {
                Text = "5mm caps",
                Font = new() { Size = 5, SizeUnit = FontSizeUnit.Millimeter },
                Style = new() { Location = new() { Left = (10, Units.Millimeter), Top = (10, Units.Millimeter) } }
            };
            var rem = new TextItem
            {
                Text = "2 rem",
                Font = new() { Size = 2, SizeUnit = FontSizeUnit.Rem },
                Style = new() { Location = new() { Left = (10, Units.Millimeter), Top = (40, Units.Millimeter) } }
            };
            r.Body.Add(millimeters);
            r.Body.Add(rem);

            // Default measurer: cap height is 0.70 em.
            var layout = r.Resolve();
            var placed = layout.Pages[0].Items;

            var mmFont = placed.First(p => ReferenceEquals(p.Source, millimeters)).Font!;
            var remFont = placed.First(p => ReferenceEquals(p.Source, rem)).Font!;

            AssertEqual(testName, "5mm caps resolved to points", 5 / (0.70 * TextStyle.PointsToMillimeters), mmFont.Size!.Value, 0.01);
            AssertEqual(testName, "2 rem == 6mm caps", 6 / (0.70 * TextStyle.PointsToMillimeters), remFont.Size!.Value, 0.01);
            AssertEqual(testName, "resolved unit is Point", FontSizeUnit.Point, mmFont.SizeUnit!.Value, 0);

            // The same 5mm request against a typeface with much smaller capitals must
            // yield a bigger point size, so that the printed capital is still 5mm.
            var narrow = r.Resolve(new NarrowCapMeasurer());
            var narrowFont = narrow.Pages[0].Items.First(p => ReferenceEquals(p.Source, millimeters)).Font!;

            AssertEqual(testName, "narrow font compensates", 5 / (0.40 * TextStyle.PointsToMillimeters), narrowFont.Size!.Value, 0.01);

            double capsDefault = 0.70 * mmFont.EmSizeInMillimeters;
            double capsNarrow = 0.40 * narrowFont.EmSizeInMillimeters;
            AssertEqual(testName, "both print 5mm capitals", capsDefault, capsNarrow, 0.01);

            Console.WriteLine();
        }

        [Fact]
        public void Test19_FontMetrics()
        {
            var testName = "Test 19: Real Font Metrics";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            int loaded = library.RegisterDirectory(FontDirectory());
            Console.WriteLine($"  loaded {loaded} font files");

            foreach (var f in library.Fonts.OrderBy(f => f.Family).ThenBy(f => f.Subfamily))
                Console.WriteLine($"    {f.Family,-18} {f.Subfamily,-14} upem={f.UnitsPerEm,-5} " +
                                  $"cap={f.CapHeight:0.000} x={f.XHeight:0.000} asc={f.Ascender:0.000} " +
                                  $"line={f.LineHeight:0.000}{(f.CapHeightIsEstimated ? "  (cap estimated)" : "")}");

            AssertEqual(testName, "fonts loaded", true, loaded > 0, 0);

            var font = library.Fonts.First(f => !f.Bold && !f.Italic);
            AssertEqual(testName, "units per em is sane", true, font.UnitsPerEm is >= 16 and <= 16384, 0);
            AssertEqual(testName, "cap height is plausible", true, font.CapHeight is > 0.3 and < 1.2, 0);
            AssertEqual(testName, "ascender above baseline", true, font.Ascender > 0, 0);
            AssertEqual(testName, "glyphs are mapped", true, font.HasGlyph('A') && font.HasGlyph('e'), 0);
            AssertEqual(testName, "advances differ between glyphs", true,
                Math.Abs(font.AdvanceOf('i') - font.AdvanceOf('W')) > 0.01, 0);

            var measurer = new FontTextMeasurer(library);
            var style = new TextStyle { Family = font.Family, Size = 5, SizeUnit = FontSizeUnit.Point };

            // The whole point of sizing in millimeters: ask for a 5mm capital and get one.
            var r = new Report { Dimension = new() { Width = 210, Height = 297 }, Body = new() };
            r.Body!.Layout = LayoutMode.Absolute;
            var item = new TextItem
            {
                Text = "Hello",
                Font = new() { Family = font.Family, Size = 5, SizeUnit = FontSizeUnit.Millimeter },
                Style = new() { Location = new() { Left = (10, Units.Millimeter), Top = (10, Units.Millimeter) } }
            };
            r.Body.Add(item);

            var layout = r.Resolve(measurer);
            var placed = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, item));
            double printedCap = placed.Font!.EmSizeInMillimeters * font.CapHeight;
            AssertEqual(testName, "5mm request prints a 5mm capital", 5, printedCap, 0.001);

            // Real advances: proportional text is not a character count.
            double narrow = measurer.WidthOf("iiiii", style);
            double wide = measurer.WidthOf("WWWWW", style);
            AssertEqual(testName, "proportional widths", true, wide > narrow * 1.5, 0);

            // Word wrapping breaks between words, never mid-word when it can be helped.
            var wrapStyle = new TextStyle { Family = font.Family, Size = 10, SizeUnit = FontSizeUnit.Point };
            const string sentence = "Service provided and invoiced to the customer for the month of July two thousand and twenty five";
            var lines = measurer.BreakLines(sentence, wrapStyle, 40);

            AssertEqual(testName, "long text wraps", true, lines.Count > 1, 0);
            AssertEqual(testName, "every line fits", true,
                lines.All(l => measurer.WidthOf(l, wrapStyle) <= 40.001), 0);
            AssertEqual(testName, "no line starts with a space", true, lines.All(l => l.Length == 0 || l[0] != ' '), 0);
            AssertEqual(testName, "words are preserved", sentence,
                string.Join(" ", lines).Replace("  ", " "), 0);

            // A missing family must fail loudly rather than substitute silently.
            bool threw = false;
            try
            {
                measurer.GetMetrics(new TextStyle { Family = "No Such Font" });
            }
            catch (FontNotFoundException)
            {
                threw = true;
            }
            AssertEqual(testName, "missing font is refused", true, threw, 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test29_DocumentDefaults()
        {
            var testName = "Test 29: Section Style Defaults";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            var measurer = new FontTextMeasurer(library);

            var report = new Report
            {
                Dimension = new() { Width = 210, Height = 297 },
                RootFontSize = 3,
                Body = new() { Layout = LayoutMode.Absolute },
                DefaultFont = new()
                {
                    Family = DmsFont, Size = 2, Bold = true,
                    Color = new(20, 40, 90), HorizontalAlignment = HorizontalAlignment.Right
                }
            };

            // Says nothing at all: takes the document's word for everything.
            var silent = new TextItem { Text = "silent" };
            silent.Style.Location.Left = (10, Units.Millimeter);
            silent.Style.Location.Top = (10, Units.Millimeter);

            // Overrides two things and inherits the rest.
            var loud = new TextItem
            {
                Text = "loud",
                Font = new() { Size = 4, Bold = false }
            };
            loud.Style.Location.Left = (10, Units.Millimeter);
            loud.Style.Location.Top = (40, Units.Millimeter);

            report.Body!.Add(silent);
            report.Body.Add(loud);

            LayoutResult layout = report.Resolve(measurer);
            TextStyle a = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, silent)).Font!;
            TextStyle b = layout.Pages[0].Items.First(p => ReferenceEquals(p.Source, loud)).Font!;

            AssertEqual(testName, "the family comes from the section", DmsFont, a.Family!, 0);
            AssertEqual(testName, "so does the weight", true, a.Bold == true, 0);
            AssertEqual(testName, "so does the colour", 90, (int)a.Color!.Value.B, 0);
            AssertEqual(testName, "so does the alignment", HorizontalAlignment.Right,
                a.HorizontalAlignment!.Value, 0);

            AssertEqual(testName, "an override wins", false, b.Bold == true, 0);
            AssertEqual(testName, "and the rest still cascades", DmsFont, b.Family!, 0);
            AssertEqual(testName, "the sizes differ as asked", true,
                b.EmSizeInMillimeters > a.EmSizeInMillimeters * 1.9, 0);

            // A report that says nothing keeps the built-in defaults.
            var bare = new Report { Dimension = new() { Width = 210, Height = 297 }, Body = new() };
            bare.Body!.Add(new TextItem { Text = "bare" });
            TextStyle plain = bare.Resolve(measurer).Pages[0].Items
                .First(p => p.Source is TextItem).Font!;
            AssertEqual(testName, "no default, no surprise", false, plain.Bold == true, 0);
            // 1 rem is RootFontSize of cap height, whichever typeface answered.
            AssertEqual(testName, "and 1 rem is still 1 rem", 3.0,
                measurer.GetMetrics(plain).CapHeight, 0.01);

            // An unset property is left out of the template, which is what makes a
            // template readable: it states what it decides and nothing else.
            string json = report.ToJson();

            // Asked of the item itself, in the parsed tree. This was a search of the text
            // for a line ending in "\n", and the JSON writer ends its lines with the
            // platform's newline - so on Windows, "\r\n", the line it looked for could not
            // occur and the assertion held whatever the item carried. On Linux it found the
            // family the REPORT declares, legitimately, and failed. It never once looked at
            // the silent text. Found the first time the suite ran on Linux.
            //
            // The loud text is the control: it carries a Font of its own, so a walk that
            // failed to find items, or a key named differently, fails here rather than
            // passing for want of matter.
            JsonObject? ItemSaying(JsonNode? node, string text)
            {
                if (node is JsonObject o)
                {
                    if (o["Text"]?.GetValue<string>() == text) return o;
                    foreach (var child in o) if (ItemSaying(child.Value, text) is { } hit) return hit;
                }
                else if (node is JsonArray a)
                {
                    foreach (var child in a) if (ItemSaying(child, text) is { } hit) return hit;
                }
                return null;
            }
            JsonNode? tree = JsonNode.Parse(json);
            JsonObject? silentWritten = ItemSaying(tree, "silent");
            JsonObject? loudWritten = ItemSaying(tree, "loud");

            AssertEqual(testName, "the silent text is in the file", true, silentWritten != null, 0);
            // States nothing about its font - which is not quite "writes no Font": it writes
            // an EMPTY one, "Font": {}, measured before and after Resolve. The object is the
            // serialiser's, its emptiness is the template's, and emptiness is the claim.
            // Resolve leaves the model as it found it; the family it cascades in never
            // reaches the item.
            AssertEqual(testName, "and states nothing about its font", false,
                silentWritten?["Font"] is JsonObject sf && sf.Count > 0, 0);
            AssertEqual(testName, "while the loud one writes the two it overrides", true,
                loudWritten?["Font"] is JsonObject lf && lf.ContainsKey("Size") && lf.ContainsKey("Bold"), 0);
            AssertEqual(testName, "a computed value is not written", false,
                json.Contains("EmSizeInMillimeters"), 0);
            AssertEqual(testName, "the document's default is", true,
                json.Contains("\"DefaultFont\""), 0);

            AssertEqual(testName, "and it survives a round trip", DmsFont,
                Report.FromJson(json).DefaultFont.Family!, 0);

            // --- the preview must actually carry the fonts it promises ------------
            var backend = new SvgBackend(measurer) { EmbeddedFonts = library };
            ReportRenderer.Render(BoundAcmeInvoice(3), backend);
            string html = backend.ToHtml();

            AssertEqual(testName, "the preview embeds the fonts it drew with", true,
                html.Contains("@font-face") && html.Contains(DmsFont), 0);
            AssertEqual(testName, "and only those", true,
                CountOccurrences(html, "@font-face") <= 4, 0);
            AssertEqual(testName, "without them the preview stays lean", false,
                new SvgBackend(measurer).ToHtml().Contains("@font-face"), 0);

            // The sheet must be as wide as the sheet, not as wide as the window.
            AssertEqual(testName, "the page shrinks to the paper", true,
                html.Contains("width:fit-content") && html.Contains("margin:0 auto"), 0);

            Console.WriteLine();
        }

        [Fact]
        public void Test33_FontGallery()
        {
            var testName = "Test 33: The Same Invoice in Every Font";
            Console.WriteLine($"Running {testName}...");

            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());

            string dir = OutputDirectory("fonts");

            // Swept first. A gallery that keeps a page for a face the library no longer
            // holds shows a font nobody can use - and the count of files here is what
            // proves every family was drawn, so a leftover makes it lie.
            foreach (string stale in Directory.GetFiles(dir, "*.pdf"))
                File.Delete(stale);
            Directory.CreateDirectory(dir);

            var measurerFor = new FontTextMeasurer(library);
            var families = library.Fonts.Select(f => f.Family).Distinct()
                                  .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            // The faces this page ends up drawing with. A gallery whose whole purpose is
            // comparing typefaces has to carry them: left out, the browser substitutes one
            // of its own, and since the substitute has its own cap height every comparison
            // is between the wrong things at the wrong size.
            var shown = new List<FontFile>();

            const string css =
                "body{background:#525659;margin:0;padding:24px;font-family:sans-serif;color:#dfe3e8}" +
                "h1{font-size:16px;font-weight:600}" +
                "figure{margin:0 0 28px}figcaption{font-size:13px;margin:6px 0 8px}" +
                ".m{color:#f8b4b4}.sheet{background:#fff;width:fit-content;box-shadow:0 2px 16px rgba(0,0,0,.6)}";

            // The figures, held aside: the @font-face rules can only be written once every
            // face is known, and they belong in the head above them.
            var gallery = new System.Text.StringBuilder();

            Console.WriteLine();
            Console.WriteLine("    {0,-22} {1,9} {2,5} {3,8} {4,7}  {5}",
                              "police", "cap", "pages", "header", "gras", "glyphes manquants");
            Console.WriteLine("    {0,-22} {1,9}", "", "(d) = declared, not measurable");
            Console.WriteLine("    " + new string('-', 78));

            int rendered = 0;

            foreach (string family in families)
            {
                FontFile face = library.Resolve(family, false, false)!;

                Report report = BoundAcmeInvoice(12);
                report.DefaultFont = new() { Family = family };

                var pdf = new PdfBackend(library);
                LayoutResult layout = ReportRenderer.Render(report, pdf);
                byte[] bytes = pdf.ToArray();

                string slug = new string(family.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
                File.WriteAllBytes(Path.Combine(dir, $"acme-{slug}.pdf"), bytes);

                var svg = new SvgBackend(new FontTextMeasurer(library)) { EmbeddedFonts = library };
                ReportRenderer.Render(report, svg);

                bool hasBold = library.Resolve(family, true, false) is { Bold: true };
                string missing = pdf.MissingGlyphs.Count == 0
                    ? "-"
                    : string.Join(" ", pdf.MissingGlyphs.Take(6).Select(c => $"U+{c:X4}"));

                Console.WriteLine("    {0,-22} {1,5:0.000}{2,-4} {3,5} {4,8:0.0} {5,7}  {6}",
                                  family, face.CapHeight, face.CapHeightIsEstimated ? " (d)" : "",
                                  layout.PageCount, layout.Pages[0].HeaderBand.Height,
                                  hasBold ? "oui" : "non", missing);

                gallery.Append("<figure><figcaption>").Append(family)
                       .Append(" — cap ").Append(face.CapHeight.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                       .Append(" · ").Append(layout.PageCount).Append(" page(s)")
                       .Append(hasBold ? "" : " · <span class=\"m\">pas de gras</span>")
                       .Append(pdf.MissingGlyphs.Count == 0 ? ""
                               : $" · <span class=\"m\">{pdf.MissingGlyphs.Count} glyphe(s) manquant(s)</span>")
                       .Append("</figcaption><div class=\"sheet\">")
                       .Append(svg.Pages[0]).Append("</div></figure>\n");

                shown.AddRange(library.Fonts.Where(f => f.Family.Equals(family, StringComparison.OrdinalIgnoreCase)));
                rendered++;
            }

            string html = "<!doctype html>\n<html><head><meta charset=\"utf-8\">"
                        + "<title>The same invoice, typeface by typeface</title><style>\n"
                        + SvgBackend.FontFaceRules(shown) + css
                        + "</style></head><body><h1>The same invoice, typeface by typeface</h1>\n"
                        + gallery + "</body></html>\n";

            string index = Path.Combine(dir, "comparison.html");
            File.WriteAllText(index, html);

            // The defect this guards against was invisible to every other assertion: the
            // page rendered, the geometry was right, and the browser drew it in a font
            // nobody had asked for.
            foreach (string family in families)
                AssertEqual(testName, $"the gallery embeds {family}",
                            true, html.Contains($"@font-face{{font-family:'{family}'"), 0);

            Console.WriteLine();
            AssertEqual(testName, "one document per family", families.Count, rendered, 0);
            AssertEqual(testName, "every family produced pages", true,
                Directory.GetFiles(dir, "*.pdf").Length == families.Count, 0);

            // The point of the exercise: a cap height comes from the outline, never from
            // what the font says about itself.
            //
            // The two faces that showed this best are gone - TimeBurner declared 0.940
            // and drew 0.732, ChopinScript declared 0.640 and drew 0.730 - removed
            // before publication because neither could be shown to allow
            // redistribution. The numbers stay in CLAUDE.md §3 as what was measured.
            //
            // What is still here proves the machinery rather than the motive, and it
            // covers both kinds of outline.
            FontFile trueType = library.Resolve(DmsFont, false, false)!;
            AssertEqual(testName, "a TrueType outline is measured", 0.700, trueType.CapHeight, 0.002);
            AssertEqual(testName, "and not flagged as a guess", false, trueType.CapHeightIsEstimated, 0);

            // Glametrix ships as CFF, so measuring it means walking a Type 2 charstring
            // rather than a glyf table - the other half of the code, and the half that
            // would otherwise fall back to an estimate.
            FontFile glametrix = library.Resolve("Glametrix", false, false)!;
            AssertEqual(testName, "a CFF outline is measured too", 0.480, glametrix.CapHeight, 0.002);
            AssertEqual(testName, "and is not flagged as a guess either", false,
                glametrix.CapHeightIsEstimated, 0);

            // Which is also the reason measuring is worth the trouble: Glametrix really
            // does draw its capitals at 0.480 of the em. They are small, so a 3 mm cap
            // needs a 6.25 mm em and the text comes out wide. That width is the
            // typeface's own, and nothing here should flatten it.
            AssertEqual(testName, "no face in the library is left guessing", false,
                library.Fonts.Any(f => f.CapHeightIsEstimated), 0);

            // The same request gives the same box and the same baseline in every face:
            // the line is a multiple of the cap height, and the cap sits centred in the
            // line. Neither depends on a metric the font is free to choose, which is what
            // stops the text sitting high in one column and low in the next.
            // Through the resolver, as the engine does: GetMetrics wants a size already
            // settled in points, and 3mm of cap is a different point size in every face.
            var boxes = families.Select(f =>
            {
                var probe = new Report { Dimension = new() { Width = 100, Height = 100 }, Body = new() };
                var line = new TextItem
                {
                    Text = "Hxg",
                    Font = new() { Family = f, Size = 3, SizeUnit = FontSizeUnit.Millimeter }
                };
                probe.Body!.Add(line);

                TextStyle placed = probe.Resolve(measurerFor).Pages[0].Items
                    .First(p => ReferenceEquals(p.Source, line)).Font!;
                return measurerFor.GetMetrics(placed);
            }).ToList();

            AssertEqual(testName, "one requested size, one line height", true,
                boxes.All(m => Math.Abs(m.LineHeight - boxes[0].LineHeight) < 0.001), 0);
            AssertEqual(testName, "and one baseline", true,
                boxes.All(m => Math.Abs((m.LineHeight + m.CapHeight) / 2
                                        - (boxes[0].LineHeight + boxes[0].CapHeight) / 2) < 0.001), 0);
            AssertEqual(testName, "a 3mm cap is 3mm everywhere", true,
                boxes.All(m => Math.Abs(m.CapHeight - 3.0) < 0.001), 0);

            // Every face here is measured, so the spread that follows is real rather
            // than an artefact of what each one chose to declare.
            var measured = library.Fonts.Where(f => !f.CapHeightIsEstimated).ToList();
            AssertEqual(testName, "no face is left unmeasured", library.Fonts.Count(), measured.Count, 0);
            AssertEqual(testName, "every measured cap is plausible", true,
                measured.All(f => f.CapHeight is > 0.40 and < 0.80), 0);

            // And the spread is the whole argument for sizing by cap height. A quarter
            // of the em separates the smallest capitals from the largest, so the same
            // point size gives visibly different text - which is what asking for
            // millimetres of capital avoids.
            //
            // This used to assert that three quarters of the library clustered where
            // text faces sit. That was true of a library which was mostly weights of
            // one family, and it stopped being true the moment display faces were
            // added - it described the collection, not typography.
            double smallest = measured.Min(f => f.CapHeight);
            double largest = measured.Max(f => f.CapHeight);

            Console.WriteLine($"    cap heights from {smallest:F3} to {largest:F3}");
            AssertEqual(testName, "the faces really do differ", true, largest - smallest > 0.2, 0);

            // The text faces, which are the ones a document is set in, do cluster.
            //
            // Named rather than computed - "is this a text face" is a judgement, not a
            // measurement - but *looked up* rather than assumed present: which typefaces
            // the library holds is now a choice made with tools/triage.py, and a
            // face dropped there used to end this test on a NullReferenceException with
            // nothing said about which one went missing.
            string[] textFaces =
            {
                "Alan Sans", "Andika Basic", "Elms Sans", DmsFont, "Manrope",
                "Mona Sans", "Mulish", "Murecho", "Nata Sans", "Noto Sans Display",
                "Onest", "Spline Sans Mono", "Zalando Sans",
            };
            var clustered = textFaces.Select(f => (Name: f, Face: library.Resolve(f, false, false)))
                                     .Where(p => p.Face is not null)
                                     .Select(p => (p.Name, p.Face!.CapHeight))
                                     .ToList();

            // Otherwise a library holding one text face - or none - would satisfy every
            // assertion below by having nothing to check.
            AssertEqual(testName, "enough text faces present to say anything", true,
                clustered.Count >= 3, 0);

            foreach ((string family, double cap) in clustered)
                AssertEqual(testName, $"{family} sits where text faces sit", true,
                    cap is > 0.66 and < 0.78, 0);

            // And the point of naming them: they sit together, while the display faces
            // in the same library do not. Measured at 0.680 to 0.728 across the eight
            // kept on 31 July 2026 - a twentieth of the em, against the quarter that
            // separates the library's extremes.
            AssertEqual(testName, "and they cluster, unlike the library at large", true,
                clustered.Max(c => c.CapHeight) - clustered.Min(c => c.CapHeight) < 0.08, 0);

            Console.WriteLine($"  🔤 {rendered} sections -> {index}");
            Console.WriteLine();
        }

        /// <summary>
        /// A cut that is neither bold nor italic has to be reachable, and two files that
        /// land on one name have to say so.
        ///
        /// The library is keyed on family, weight and slant - one string and two flags -
        /// so there is nowhere in the key to put a Light. Glametrix ships three files all
        /// calling themselves "Glametrix", distinguished only by Regular / Bold / Light,
        /// and the third overwrote the first: a template asking for Glametrix got the
        /// Light, and every line in it was set to different widths than the ones it had
        /// been laid out with.
        ///
        /// Nothing showed. The two cuts measure the same cap height - 0.480 both - so
        /// every assertion in the suite went on passing, which is why this went unseen
        /// until a font count of 29 was set beside a library holding 28. Widths are the
        /// one thing the engine never normalises, and the one thing nothing asserts on.
        ///
        /// Two fixes, and they answer different halves. An unrecognised subfamily now
        /// becomes part of the family name, which is what Elms Sans already does of its
        /// own accord - "Elms Sans Light" in the family record. And a slot claimed twice
        /// is recorded rather than silently taken, for the case the first cannot help:
        /// the same file twice, where the subfamilies are identical and only the path
        /// tells them apart.
        /// </summary>
        [Fact]
        public void Test56_FontNamesAndClashes()
        {
            const string testName = "Test 56";
            Console.WriteLine("Test 56: a cut nobody can name, and a name claimed twice");
            Console.WriteLine();

            var library = new FontLibrary();
            int loaded = library.RegisterDirectory(FontDirectory());

            // The count that told, before anything else did. A file loaded and not held
            // is a file no template can reach.
            Console.WriteLine($"    loaded {loaded}, library holds {library.Fonts.Count()}");
            AssertEqual(testName, "every file loaded is a file that can be reached",
                loaded, library.Fonts.Count(), 0);
            AssertEqual(testName, "and nothing was shadowed", 0, library.Clashes.Count, 0);

            // --- the Light is its own family, and the regular is back -----------------
            FontFile? regular = library.Resolve("Glametrix", false, false);
            FontFile? light = library.Resolve("Glametrix Light", false, false);

            Assert.NotNull(regular);
            Assert.NotNull(light);
            Console.WriteLine($"    Glametrix       -> {regular!.Subfamily}");
            Console.WriteLine($"    Glametrix Light -> {light!.Subfamily}");

            AssertEqual(testName, "asking for Glametrix gives the regular",
                "Regular", regular.Subfamily, 0);
            AssertEqual(testName, "and the Light is reachable under a name of its own",
                "Light", light.Subfamily, 0);
            AssertEqual(testName, "which are two different files", false,
                ReferenceEquals(regular, light), 0);

            // Widths, not cap heights: both cuts measure 0.480, so a cap height cannot
            // tell them apart and never could. This is the assertion the suite lacked.
            double capsMatch = Math.Abs(regular.CapHeight - light.CapHeight);
            Console.WriteLine($"    cap heights differ by {capsMatch:0.000} - which is why nothing showed");
            AssertEqual(testName, "the cap heights are the same, so they proved nothing",
                0.0, capsMatch, 0.0005);
            AssertEqual(testName, "but the widths are not", true,
                Math.Abs(regular.AdvanceOf('H') - light.AdvanceOf('H')) > 0.001,
                0);

            // --- a bold is still a bold, and a SemiBold is not -----------------------
            FontFile? bold = library.Resolve("Glametrix", true, false);
            AssertEqual(testName, "a Bold subfamily is still a weight flag",
                "Bold", bold!.Subfamily, 0);

            // The rule this replaced asked whether the subfamily *contained* "bold",
            // which makes a SemiBold a bold and drops it on the real one. Nothing in the
            // library triggers it, which is exactly why it would have stayed unnoticed.
            AssertEqual(testName, "a SemiBold is a family of its own, not a bold", true,
                library.Fonts.Any(f => f.Family == "Elms Sans SemiBold" && !f.Bold), 0);

            // --- and the order a directory is registered in -------------------------
            // It decides which typeface a text that names none gets - the first family
            // registered is the default - and which file wins a clash. It was the file
            // system's order: the same template came out in Elms Sans Black on Windows and
            // in Mulish on Linux. Checked as a function rather than through a real
            // directory, because NTFS already lists alphabetically and a check made that way
            // would pass on Windows whether the sort were there or not.
            //
            // Each name is chosen to catch one part. The input is shuffled, so no sort
            // leaves it as it came. "a/b" against "a0" and "aB": '/' sorts before '0' and
            // '\' after 'B', so without one separator the platforms disagree - and Path.Combine
            // gives each its own. "alpha" given before "Alpha": equal ignoring case, so only
            // the ordinal tie-break puts them in an order at all.
            {
                string root = Path.Combine(Path.GetTempPath(), "fonts");
                string[] given =
                {
                    Path.Combine(root, "Zeta.ttf"),
                    Path.Combine(root, "elms", "ElmsSans-Black.ttf"),
                    Path.Combine(root, "aB.ttf"),
                    Path.Combine(root, "alpha.ttf"),
                    Path.Combine(root, "a", "b.ttf"),
                    Path.Combine(root, "Alpha.ttf"),
                    Path.Combine(root, "a0.ttf"),
                };
                string[] wanted =
                {
                    "a/b.ttf", "a0.ttf", "aB.ttf", "Alpha.ttf", "alpha.ttf",
                    "elms/ElmsSans-Black.ttf", "Zeta.ttf",
                };
                string[] got = FontLibrary.InRegistrationOrder(given, root)
                    .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                    .ToArray();

                AssertEqual(testName, "a directory is registered in one order on every platform",
                    string.Join(" ", wanted), string.Join(" ", got), 0);
            }

            // --- and a slot claimed twice says so ------------------------------------
            // The same file under two names: identical family, identical subfamily. The
            // half above cannot help here, and this is what the other half is for.
            string twice = OutputDirectory("fonts-clash");
            foreach (string old in Directory.GetFiles(twice)) File.Delete(old);

            string source = Directory.GetFiles(FontDirectory(), "Mulish-Regular.ttf",
                                               SearchOption.AllDirectories).First();
            File.Copy(source, Path.Combine(twice, "Mulish-Regular.ttf"), true);
            File.Copy(source, Path.Combine(twice, "a-copy-of-it.ttf"), true);

            // And its licence beside them. These copies are committed under tests/output,
            // and the OFL asks that every copy carry it - THIRD-PARTY-NOTICES.md promises as much. A .txt
            // is not a font, so RegisterDirectory passes over it and the count below is
            // untouched.
            File.Copy(Path.Combine(Path.GetDirectoryName(source)!, "OFL.txt"),
                      Path.Combine(twice, "OFL.txt"), true);

            var second = new FontLibrary();
            second.RegisterDirectory(twice);

            Console.WriteLine($"    {second.Clashes.Count} clash(es): "
                              + string.Join("; ", second.Clashes));

            AssertEqual(testName, "two files on one name is one clash",
                1, second.Clashes.Count, 0);

            // Named by file, because the subfamilies are identical and saying
            // "Regular is hidden by Regular" would say nothing at all.
            AssertEqual(testName, "and it names the file that cannot be reached", true,
                second.Clashes[0].ToString().Contains("a-copy-of-it.ttf"), 0);
            AssertEqual(testName, "as well as the one that won", true,
                second.Clashes[0].ToString().Contains("Mulish-Regular.ttf"), 0);

            Console.WriteLine();
        }

        /// <summary>
        /// A font somebody chose, given to the engine as bytes rather than as a path -
        /// posted over HTTP, read out of a database, picked in a browser.
        ///
        /// The engine could always do this: FontFile.Load(byte[]) has been there from
        /// the start. Nothing above it could - not the CLI, not the C boundary, not the
        /// server - which is section 1 in its purest form: a capability that exists and
        /// cannot be reached.
        ///
        /// The half worth testing is the refusal. A directory scan steps over a file
        /// that is not a font because it was never asked about that file in particular;
        /// a font named on purpose has to be complained about instead, or somebody is
        /// left looking at a list their font is not in with nothing to say why.
        ///
        /// And parsing is the check. There is no cheaper one worth having: a header
        /// sniff would prove the first twelve bytes look right, while getting through
        /// Register proves the tables are there, the cmap reads and the 'H' can be
        /// measured. A second, weaker reader could only agree with this one or
        /// contradict it - and the contradiction is the interesting case, since it means
        /// a font called good and then refused, or refused and perfectly usable.
        /// </summary>
        [Fact]
        public void Test57_FontFromBytes()
        {
            const string testName = "Test 57";
            Console.WriteLine("Test 57: a font chosen by whoever is holding it");
            Console.WriteLine();

            string source = Directory.GetFiles(FontDirectory(), "Mulish-Regular.ttf",
                                               SearchOption.AllDirectories).First();
            byte[] bytes = File.ReadAllBytes(source);

            var library = new FontLibrary();
            FontFile taken = library.Register(bytes);

            Console.WriteLine($"    {bytes.Length} bytes -> {taken.Family} {taken.Subfamily}");
            AssertEqual(testName, "bytes alone are enough", "Mulish", taken.Family, 0);
            AssertEqual(testName, "and it is reachable by name", true,
                library.Resolve("Mulish", false, false) != null, 0);

            // No file, so no path to name it by - and the clash message already knows to
            // say so rather than print an empty string.
            AssertEqual(testName, "a font with no file says so", "<memory>", taken.Source, 0);

            // Measured, not declared: the same cut loaded from its path has to come out
            // identical, or the two doors lead somewhere different.
            FontFile fromPath = FontFile.Load(source);
            AssertEqual(testName, "the same font either way", fromPath.CapHeight, taken.CapHeight, 1e-9);
            AssertEqual(testName, "down to the widths", fromPath.AdvanceOf('H'), taken.AdvanceOf('H'), 1e-9);

            // --- and what is not a font is refused, loudly ---------------------------
            byte[] notAFont = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "README.md"));

            var why = Assert.Throws<InvalidDataException>(() => library.Register(notAFont));
            Console.WriteLine($"    refused: {why.Message}");
            AssertEqual(testName, "and it says what it looked at", true,
                why.Message.Contains("not a TrueType or OpenType font"), 0);

            // The distinction the whole thing rests on: a scan of the same bytes on disk
            // shrugs, and a font named on purpose does not.
            string strewn = OutputDirectory("fonts-strewn");
            foreach (string old in Directory.GetFiles(strewn)) File.Delete(old);
            File.WriteAllBytes(Path.Combine(strewn, "pretend.ttf"), notAFont);

            var scanned = new FontLibrary();
            int found = scanned.RegisterDirectory(strewn);

            Console.WriteLine($"    the same bytes in a directory: {found} taken, no complaint");
            AssertEqual(testName, "a scan steps over it without a word", 0, found, 0);
            AssertEqual(testName, "so the two doors differ on purpose", true,
                found == 0 && why.Message.Length > 0, 0);

            Console.WriteLine();
        }
    }
}
