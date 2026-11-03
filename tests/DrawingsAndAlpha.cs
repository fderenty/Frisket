using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace test
{
    /// <summary>
    /// Pictures that are not plain bitmaps: an SVG drawing, read by the engine and written out by
    /// both backends, and a PNG with an alpha channel. A chart from another application is what
    /// asked for the first, and what such an application exports by default for the second.
    /// </summary>
    public partial class EngineTests
    {
        private static string ChartSample()
            => File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "charts", "attendance.svg")).ReplaceLineEndings("\n");

        private static FontLibrary DrawingFonts()
        {
            var library = new FontLibrary();
            library.RegisterDirectory(FontDirectory());
            library.DefaultFamily = DmsFont;
            return library;
        }

        /// <summary>A sheet holding one picture, read from the template's own bytes.</summary>
        private static Report SheetWith(byte[] picture, string mediaType, double width = 100, double height = 50)
        {
            var image = new ImageItem
            {
                Source = new ImageSource { Data = picture, MediaType = mediaType },
                AltText = "A picture for a test"
            };
            image.Style.Location.Left = (20, Units.Millimeter);
            image.Style.Location.Top = (30, Units.Millimeter);
            image.Style.Location.Width = (width, Units.Millimeter);
            image.Style.Location.Height = (height, Units.Millimeter);

            var report = new Report
            {
                Title = "A picture", Language = "en-GB",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Absolute }
            };
            report.Body!.Add(image);
            report.Reattach();
            return report;
        }

        private static byte[] PdfOf(Report report, FontLibrary fonts, bool compress = true)
        {
            var pdf = new PdfBackend(fonts) { Compress = compress };
            ReportRenderer.Render(report, pdf);
            return pdf.ToArray();
        }

        private static SvgBackend ProofOf(Report report, FontLibrary fonts)
        {
            var svg = new SvgBackend(new FontTextMeasurer(fonts));
            ReportRenderer.Render(report, svg);
            return svg;
        }

        // ---- the alpha channel --------------------------------------------------------------

        /// <summary>
        /// An alpha channel, which is what a canvas and most tools export. It was refused, at
        /// the end of the file, by a message suggesting the picture be flattened by hand.
        ///
        /// Read back the way a reader finds it, through the reference from the image to its mask
        /// (section 2): "/SMask" appearing in the file proves nothing. The sample uses all five
        /// of PNG's row filters, and a sample written with none would pass with Paeth wrong.
        /// </summary>
        [Fact]
        public void Test99_AnAlphaChannelBecomesASoftMask()
        {
            const string testName = "Test 99";
            Console.WriteLine("Test 99: a PNG with an alpha channel reaches the PDF as colours and a soft mask");
            Console.WriteLine();

            const int w = 23, h = 17;

            // A few levels and no gradient. A smooth ramp never makes above and the corner equally
            // near the estimate, and which of them wins is the part of Paeth a decoder gets wrong.
            // Left against above has no such case: equally near and different, the corner is
            // nearer still, which a sabotage swapping those two found by going green.
            static (byte R, byte G, byte B, byte A) Shade(int x, int y)
                => ((byte)((x * 3 + y) % 5 * 50), (byte)((x * 13 + y * 29) % 4 * 60),
                    (byte)((x * y) % 3 * 90), (byte)((x * 7 + y * 13) % 5 * 60));

            FontLibrary fonts = DrawingFonts();
            byte[] rgba = PngEncoder.WithAlpha(w, h, Shade);

            AssertEqual(testName, "the sample holds every row filter", 5, PngEncoder.FiltersUsed(rgba), 0);

            int ties = 0;
            for (int y = 4; y < h; y += 5)
            {
                for (int x = 1; x < w; x++)
                {
                    int left = Shade(x - 1, y).R, up = Shade(x, y - 1).R, corner = Shade(x - 1, y - 1).R;
                    int estimate = left + up - corner;
                    if (up != corner && Math.Abs(estimate - up) == Math.Abs(estimate - corner)
                        && Math.Abs(estimate - left) > Math.Abs(estimate - up))
                        ties++;
                }
            }
            AssertEqual(testName, "and rows where Paeth has a tie to break", true, ties > 0, 0);

            string file = Encoding.Latin1.GetString(PdfOf(SheetWith(rgba, "image/png"), fonts));
            byte[] bytes = Encoding.Latin1.GetBytes(file);

            Match image = Regex.Match(file, @"(\d+) 0 obj\s*<<[^>]*?/Subtype /Image[^>]*?/SMask (\d+) 0 R");
            AssertEqual(testName, "the image names a soft mask", true, image.Success, 0);

            int number = int.Parse(image.Groups[1].Value, CultureInfo.InvariantCulture);
            byte[] colour = PdfStreamOf(bytes, file, number);
            byte[] mask = PdfStreamOf(bytes, file, int.Parse(image.Groups[2].Value, CultureInfo.InvariantCulture));
            string maskDictionary = DictionaryOf(file, int.Parse(image.Groups[2].Value, CultureInfo.InvariantCulture));

            AssertEqual(testName, "the mask is gray", true, maskDictionary.Contains("/ColorSpace /DeviceGray"), 0);
            AssertEqual(testName, "the mask is as wide as the image", true, maskDictionary.Contains($"/Width {w} "), 0);
            AssertEqual(testName, "the colours are three to a pixel", w * h * 3, colour.Length, 0);
            AssertEqual(testName, "the mask is one to a pixel", w * h, mask.Length, 0);

            int wrong = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    (byte r, byte g, byte b, byte a) = Shade(x, y);
                    int at = y * w + x;
                    if (colour[at * 3] != r || colour[at * 3 + 1] != g || colour[at * 3 + 2] != b || mask[at] != a)
                        wrong++;
                }
            }
            Console.WriteLine($"    {w * h} pixels read back through all five filters, {wrong} wrong");
            AssertEqual(testName, "every pixel crosses unchanged", 0, wrong, 0);

            // Gray with alpha takes the same road, with one colour to a pixel.
            byte[] gray = PngEncoder.WithAlpha(w, h, Shade, gray: true);
            string grayFile = Encoding.Latin1.GetString(PdfOf(SheetWith(gray, "image/png"), fonts));
            Match grayImage = Regex.Match(grayFile, @"(\d+) 0 obj\s*<<[^>]*?/Subtype /Image[^>]*?/SMask \d+ 0 R");
            AssertEqual(testName, "gray with alpha has a mask too", true, grayImage.Success, 0);
            AssertEqual(testName, "and one colour to a pixel", w * h,
                PdfStreamOf(Encoding.Latin1.GetBytes(grayFile), grayFile, int.Parse(grayImage.Groups[1].Value, CultureInfo.InvariantCulture)).Length, 0);

            // Opaque RGBA, which is what a canvas usually exports, has nothing to mask.
            byte[] opaque = PngEncoder.WithAlpha(w, h, (x, y) => (10, 20, 30, 255));
            string opaqueFile = Encoding.Latin1.GetString(PdfOf(SheetWith(opaque, "image/png"), fonts));
            AssertEqual(testName, "an opaque picture draws", true, Regex.IsMatch(opaqueFile, "/Subtype /Image"), 0);
            AssertEqual(testName, "and carries no mask", false, opaqueFile.Contains("/SMask"), 0);

            // Plain RGB still crosses untouched: the decoding is for what needs it.
            string plain = Encoding.Latin1.GetString(PdfOf(SheetWith(PngEncoder.Rgb(8, 8, (x, y) => (1, 2, 3)), "image/png"), fonts));
            AssertEqual(testName, "plain RGB keeps its predictor", true, plain.Contains("/Predictor 15"), 0);

            // Spoiled ones say so, in words, and from the header where they can.
            byte[] liar = PngEncoder.WithAlpha(w, h, Shade);
            liar[16] = 0x7F;                                  // a width of two billion
            AssertEqual(testName, "a size its data could not hold is refused unread",
                false, PngImage.TryRead(liar, out _), 0);

            byte[] tall = PngEncoder.WithAlpha(1, 1, (x, y) => (0, 0, 0, 0));
            tall[22] = 0x7F;                                  // a height of many thousands, for one pixel
            AssertEqual(testName, "and so is one that merely claims too many rows",
                false, PngImage.TryRead(tall, out _), 0);

            byte[] filter = PngEncoder.WithAlpha(w, h, Shade, badFilter: true);
            AssertEqual(testName, "a row filter that does not exist reads as a bad file", true,
                Record.Exception(() =>
                {
                    Assert.True(PngImage.TryRead(filter, out PngImage read));
                    read.Planes();
                }) is InvalidDataException, 0);

            // What is still not taken says what to do, in the one voice all three outputs use.
            byte[] palette = PngEncoder.WithHeader(PngEncoder.Rgb(8, 8, (x, y) => (1, 2, 3)), colourType: 3);
            byte[] interlaced = PngEncoder.WithHeader(PngEncoder.Rgb(8, 8, (x, y) => (1, 2, 3)), interlace: 1);

            foreach ((string name, byte[] data, string says) in new[]
            {
                ("a palette", palette, "palette"),
                ("interlacing", interlaced, "interlaced"),
                ("bytes that are nothing", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "not a JPEG")
            })
            {
                Report sheet = SheetWith(data, "image/png");
                Exception? fromPdf = Record.Exception(() => PdfOf(sheet, fonts));
                Exception? fromProof = Record.Exception(() => ProofOf(sheet, fonts));
                TemplateIssue? fromCheck = TemplateValidator.Check(sheet).FirstOrDefault(i => i.Rule == "sound.unreadablePicture");

                Console.WriteLine($"    {name}: {fromPdf?.Message}");
                AssertEqual(testName, $"{name} is refused by the PDF, in words", true, fromPdf?.Message.Contains(says) == true, 0);
                AssertEqual(testName, $"{name} is refused by the proof, in the same words", fromPdf?.Message!, fromProof?.Message!, 0);
                AssertEqual(testName, $"{name} is said by the validator, in the same words", fromPdf?.Message!, fromCheck?.Message!, 0);
            }

            AssertEqual(testName, "a picture that can be drawn is not reported", 0,
                TemplateValidator.Check(SheetWith(rgba, "image/png")).Count(i => i.Rule == "sound.unreadablePicture"), 0);

            Console.WriteLine();
        }

        private static string DictionaryOf(string file, int number)
        {
            int at = file.IndexOf($"\n{number} 0 obj", StringComparison.Ordinal);
            return at < 0 ? string.Empty : file[at..file.IndexOf(">>", at, StringComparison.Ordinal)];
        }

        /// <summary>An object's stream, inflated if it says it is deflated.</summary>
        private static byte[] PdfStreamOf(byte[] bytes, string file, int number)
        {
            int at = file.IndexOf($"\n{number} 0 obj", StringComparison.Ordinal);
            int open = file.IndexOf("stream\n", at, StringComparison.Ordinal) + 7;
            int end = file.IndexOf("\nendstream", open, StringComparison.Ordinal);
            byte[] body = bytes[open..end];

            if (!DictionaryOf(file, number).Contains("/FlateDecode"))
                return body;

            using var inflate = new System.IO.Compression.ZLibStream(
                new MemoryStream(body), System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflate.CopyTo(output);
            return output.ToArray();
        }

        // ---- what is refused ----------------------------------------------------------------

        private const string Well = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 50'>";

        /// <summary>
        /// Each fault beside the same file with the fault mended, so a reader that refused
        /// everything fails as surely as one that refused nothing. They go to
        /// tests/output/drawings/readings.json for the designer's reader to answer in turn.
        /// </summary>
        private static readonly (string Name, string Refused, string Mended)[] Refusals =
        {
            ("a script", Well + "<script>boom()</script></svg>", Well + "</svg>"),
            ("a style sheet", Well + "<style>rect{fill:red}</style><rect width='9' height='9'/></svg>", Well + "<rect width='9' height='9' fill='red'/></svg>"),
            ("an image", Well + "<image href='file:///etc/passwd' width='9' height='9'/></svg>", Well + "</svg>"),
            ("use", Well + "<defs><rect id='r' width='9' height='9'/></defs><use href='#r'/></svg>", Well + "<rect width='9' height='9'/></svg>"),
            ("foreignObject", Well + "<foreignObject><div xmlns='http://www.w3.org/1999/xhtml'>x</div></foreignObject></svg>", Well + "</svg>"),
            ("a gradient", Well + "<defs><linearGradient id='g'/></defs></svg>", Well + "<defs/></svg>"),
            ("a fill from a gradient", Well + "<rect width='9' height='9' fill='url(#g)'/></svg>", Well + "<rect width='9' height='9' fill='#336'/></svg>"),
            ("a nested svg", Well + "<svg viewBox='0 0 1 1'/></svg>", Well + "<g/></svg>"),
            ("an event", Well + "<rect width='9' height='9' onclick='boom()'/></svg>", Well + "<rect width='9' height='9'/></svg>"),
            ("an unknown attribute", Well + "<rect width='9' height='9' mix-blend-mode='multiply'/></svg>", Well + "<rect width='9' height='9'/></svg>"),
            ("an unknown style property", Well + "<rect width='9' height='9' style='filter:blur(2px)'/></svg>", Well + "<rect width='9' height='9' style='fill:red'/></svg>"),
            ("a percentage", Well + "<rect width='50%' height='9'/></svg>", Well + "<rect width='50' height='9'/></svg>"),
            ("a negative radius", Well + "<circle r='-4'/></svg>", Well + "<circle r='4'/></svg>"),
            ("bad path data", Well + "<path d='M 0 0 L 5'/></svg>", Well + "<path d='M 0 0 L 5 5'/></svg>"),
            ("a path with no move", Well + "<path d='L 5 5'/></svg>", Well + "<path d='M 5 5'/></svg>"),
            ("an odd list of points", Well + "<polyline points='1 2 3'/></svg>", Well + "<polyline points='1 2 3 4'/></svg>"),
            ("a number past reason", Well + "<rect width='1e400' height='9'/></svg>", Well + "<rect width='1e4' height='9'/></svg>"),
            ("a transform it cannot read", Well + "<g transform='perspective(3)'/></svg>", Well + "<g transform='rotate(3)'/></svg>"),
            ("a clip that is not there", Well + "<rect width='9' height='9' clip-path='url(#nope)'/></svg>", Well + "<clipPath id='nope'><rect width='5' height='5'/></clipPath><rect width='9' height='9' clip-path='url(#nope)'/></svg>"),
            ("a clip of two shapes", Well + "<clipPath id='c'><rect width='5' height='5'/><rect width='6' height='6'/></clipPath></svg>", Well + "<clipPath id='c'><rect width='5' height='5'/></clipPath></svg>"),
            ("a clip of a text", Well + "<clipPath id='c'><text>x</text></clipPath></svg>", Well + "<clipPath id='c'><rect width='5' height='5'/></clipPath></svg>"),
            ("a stroked text", Well + "<text stroke='red'>x</text></svg>", Well + "<text fill='red'>x</text></svg>"),
            ("a text that inherits a stroke", Well + "<g stroke='red'><text>x</text></g></svg>", Well + "<g stroke='red'><text stroke='none'>x</text></g></svg>"),
            ("a position for each letter", Well + "<text x='1 2 3'>abc</text></svg>", Well + "<text x='1'>abc</text></svg>"),
            ("a baseline to work out", Well + "<text dominant-baseline='middle'>x</text></svg>", Well + "<text dy='.35em'>x</text></svg>"),
            ("text outside a text", Well + "loose words</svg>", Well + "</svg>"),
            ("no size at all", "<svg xmlns='http://www.w3.org/2000/svg'></svg>", "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='5'></svg>"),
            ("a viewBox with no width", "<svg viewBox='0 0 0 5'></svg>", "<svg viewBox='0 0 1 5'></svg>"),
            ("a tree too deep", "<svg viewBox='0 0 1 1'>" + string.Concat(Enumerable.Repeat("<g>", 70)) + string.Concat(Enumerable.Repeat("</g>", 70)) + "</svg>",
                                "<svg viewBox='0 0 1 1'>" + string.Concat(Enumerable.Repeat("<g>", 60)) + string.Concat(Enumerable.Repeat("</g>", 60)) + "</svg>"),
            // What every entity attack is written in.
            ("an internal subset", "<!DOCTYPE svg [<!ENTITY a 'x'>]>" + Well + "</svg>", "<!DOCTYPE svg PUBLIC '-//W3C//DTD SVG 1.1//EN' 'http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd'>" + Well + "</svg>"),
            ("billion laughs", "<!DOCTYPE lolz [<!ENTITY a 'lol'><!ENTITY b '&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;'>]>" + Well + "<text>&b;</text></svg>", Well + "<text>lol</text></svg>"),
            ("an external entity", "<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>" + Well + "<text>&x;</text></svg>", Well + "<text>x</text></svg>"),
            ("an entity nobody declared", Well + "<text>&x;</text></svg>", Well + "<text>&amp;&#233;&#xE9;</text></svg>"),
            ("a tag never closed", Well + "<g>", Well + "<g/></svg>"),
            ("a tag closed wrongly", Well + "<g></rect></svg>", Well + "<g></g></svg>"),
            ("an attribute given twice", Well + "<rect width='1' width='2'/></svg>", Well + "<rect width='2'/></svg>"),
            ("a second root", Well + "</svg><svg viewBox='0 0 1 1'/>", Well + "</svg>"),
            ("not svg at all", "<html><body/></html>", Well + "</svg>"),
        };

        /// <summary>
        /// A drawing is drawn as asked or refused by name. Each fault here is something a browser
        /// would have drawn differently, or something a printed document has no business doing;
        /// each is seen firing, and seen falling silent on the file with that one thing mended.
        /// </summary>
        [Fact]
        public void Test100_ADrawingOutsideWhatIsTakenIsRefused()
        {
            const string testName = "Test 100";
            Console.WriteLine("Test 100: what is outside the drawing's subset is refused by name, never skipped");
            Console.WriteLine();

            FontLibrary fonts = DrawingFonts();

            foreach ((string name, string refused, string mended) in Refusals)
            {
                IReadOnlyList<TemplateIssue> found = SvgDrawing.Check(refused);
                Console.WriteLine($"    {name,-30} {(found.Count == 0 ? "NOT REFUSED" : found[0].Message)}");

                AssertEqual(testName, $"{name} is refused", true, found.Count > 0, 0);
                AssertEqual(testName, $"{name} is an error", true, found.All(i => i.Level == IssueLevel.Error), 0);
                AssertEqual(testName, $"{name}, mended, says nothing", string.Empty,
                    string.Join(" / ", SvgDrawing.Check(mended).Select(i => i.Message)), 0);

                // One answer from every door. Refused here, or it would be the exact preview
                // showing what the PDF stops at, which is the fault all of this began from.
                Report sheet = SheetWith(Encoding.UTF8.GetBytes(refused), "image/svg+xml");
                Exception? fromPdf = Record.Exception(() => PdfOf(sheet, fonts));
                Exception? fromProof = Record.Exception(() => ProofOf(sheet, fonts));
                TemplateIssue? fromCheck = TemplateValidator.Check(sheet).FirstOrDefault(i => i.Rule == "sound.unreadablePicture");

                AssertEqual(testName, $"{name} stops the PDF", true, fromPdf is NotSupportedException, 0);
                AssertEqual(testName, $"{name} stops the proof, in the same words", fromPdf?.Message!, fromProof?.Message!, 0);
                AssertEqual(testName, $"{name} is said by the validator, in the same words", fromPdf?.Message!, fromCheck?.Message!, 0);

                Report fine = SheetWith(Encoding.UTF8.GetBytes(mended), "image/svg+xml");
                AssertEqual(testName, $"{name}, mended, reaches the PDF", null!, Record.Exception(() => PdfOf(fine, fonts))?.Message!, 0);
            }

            // What cannot change the picture is let through, or every editor's file is refused.
            const string harmless =
                "<?xml version='1.0'?><!-- a comment --><svg xmlns='http://www.w3.org/2000/svg' "
                + "xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape' xmlns:xlink='http://www.w3.org/1999/xlink' "
                + "version='1.1' baseProfile='full' viewBox='0 0 9 9' width='100%' preserveAspectRatio='xMidYMid' "
                + "id='a' class='b' role='img' aria-label='c' data-d='e' inkscape:f='g' shape-rendering='crispEdges'>"
                + "<title>t</title><desc>d</desc><metadata><rdf:RDF xmlns:rdf='r'><script/></rdf:RDF></metadata>"
                + "<rect width='5' height='5' tabindex='0' pointer-events='none' cursor='pointer'/></svg>";
            AssertEqual(testName, "what changes nothing passes", string.Empty,
                string.Join(" / ", SvgDrawing.Check(harmless).Select(i => i.Message)), 0);

            // A family is asked of the library that will draw it, by whoever holds one.
            string named = Well + "<text font-family=\"'Not A Font', sans-serif\">x</text></svg>";
            AssertEqual(testName, "with no library a family is not looked for", 0, SvgDrawing.Check(named).Count, 0);
            AssertEqual(testName, "with one it is", "svg.unknownFamily",
                SvgDrawing.Check(named, fonts).FirstOrDefault()?.Rule!, 0);
            AssertEqual(testName, "and drawing it is refused like any text's", true,
                Record.Exception(() => PdfOf(SheetWith(Encoding.UTF8.GetBytes(named), "image/svg+xml"), fonts)) is FontNotFoundException, 0);
            AssertEqual(testName, "a generic name asks for the default", 0,
                SvgDrawing.Check(Well + "<text font-family='sans-serif'>x</text></svg>", fonts).Count, 0);
            AssertEqual(testName, "the sample chart draws", string.Empty,
                string.Join(" / ", SvgDrawing.Check(ChartSample(), fonts).Select(i => i.Message)), 0);

            WriteReadings();
            Console.WriteLine();
        }

        // ---- the contract -------------------------------------------------------------------

        /// <summary>
        /// The page that tells a generator what it may write, held to the reader's own lists. A
        /// list in prose is a claim, and this one is the contract with another application.
        /// Asked in both directions: a name the reader takes and the page omits leaves a
        /// generator short, and one the page promises and the reader refuses breaks it.
        /// </summary>
        [Fact]
        public void Test101_TheContractPageNamesWhatIsTaken()
        {
            const string testName = "Test 101";
            Console.WriteLine("Test 101: examples/charts/README.md says what the reader takes");
            Console.WriteLine();

            string page = File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "charts", "README.md"));
            string taken = page[page.IndexOf("## What is taken", StringComparison.Ordinal)..page.IndexOf("## What is not", StringComparison.Ordinal)];
            var named = Regex.Matches(taken, "`([^`]+)`").Select(m => m.Groups[1].Value).ToHashSet();

            foreach (string[] list in new[] { SvgReader.Drawn, SvgReader.NotDrawn, SvgReader.Properties, SvgReader.Ignored })
            {
                foreach (string name in list)
                    AssertEqual(testName, $"the page names {name}", true, named.Contains(name), 0);
            }

            // Every element the page lists, drawn in a file of its own. Asked of the reader
            // rather than of its list, so a name left in the list and dropped from the code falls.
            foreach (Match row in Regex.Matches(taken[..taken.IndexOf("Properties, as attributes", StringComparison.Ordinal)], @"^\| `(\w+)`", RegexOptions.Multiline))
            {
                string name = row.Groups[1].Value;
                AssertEqual(testName, $"the page's {name} is one the reader knows", true,
                    SvgReader.Drawn.Contains(name) || SvgReader.NotDrawn.Contains(name), 0);
            }

            // And what the page says is refused, is.
            foreach (string name in new[] { "dominant-baseline", "alignment-baseline" })
            {
                AssertEqual(testName, $"{name} is refused as the page says", true,
                    SvgDrawing.Check(Well + $"<text {name}='middle'>x</text></svg>").Count > 0, 0);
            }

            // A page nobody can find is not a contract. The schema cannot look inside a picture,
            // so what it owes is to say where the limit is written, in the whole address: whoever
            // holds only the package has no examples/ beside them.
            const string address = "github.com/fderenty/Frisket/tree/main/examples/charts";
            string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

            // From the generators and not from the disk: test 47 rewrites these files, and read here
            // before it runs they would answer for the comments of the previous run (section 4).
            JsonNode schema = JsonNode.Parse(TemplateSchema.Build())!;
            JsonNode Definition(string name) => schema["$defs"]![name]!;

            AssertEqual(testName, "the schema's picture says where the limit is", true,
                ((string?)Definition("ImageItem")["description"])?.Contains(address) == true, 0);
            AssertEqual(testName, "and that it is limited", true,
                ((string?)Definition("ImageItem")["description"])?.Contains("limited SVG") == true, 0);
            AssertEqual(testName, "so does what carries the bytes", true,
                ((string?)Definition("ImageSource")["description"])?.Contains(address) == true, 0);
            AssertEqual(testName, "and how a drawing is encoded", true,
                ((string?)Definition("ImageSource")["properties"]!["Data"]!["description"])?.Contains("UTF-8") == true, 0);
            AssertEqual(testName, "a media type does not claim to decide what is drawn", true,
                ((string?)Definition("ImageSource")["properties"]!["MediaType"]!["description"])?.Contains("does not change what is drawn") == true, 0);
            AssertEqual(testName, "the reference page carries the address too", true,
                TemplateReference.Build(XmlDocs.Beside()!).Contains(address), 0);
            AssertEqual(testName, "and the comment on the public check", true,
                Read("engine", "Vector", "SvgDrawing.cs").Contains(address), 0);

            foreach ((string door, string[] where, string link) in new[]
            {
                ("the repository's page", new[] { "README.md" }, "examples/charts/README.md"),
                ("the package's page", new[] { "engine", "README.md" }, address),
                ("the guide for a model", new[] { "examples", "ai", "README.md" }, "../charts/README.md"),
                ("the schema's page", new[] { "examples", "schema", "README.md" }, "../charts/README.md"),
                ("the faces' page", new[] { "apps", "designer", "README.md" }, "examples/charts/README.md"),
            })
            {
                AssertEqual(testName, $"{door} names the contract", true, Read(where).Contains(link), 0);
            }

            // Every way of asking the page offers has to work, or the contract sends people to a
            // door that is shut. test 104 and 105 go through them; here, that they are offered.
            foreach (string way in new[] { "SvgDrawing.Check", "TemplateData.Check", "frisket check -t", "POST /check" })
                AssertEqual(testName, $"the page offers {way}", true, page.Contains(way), 0);

            Console.WriteLine();
        }

        // ---- the two outputs against each other ---------------------------------------------

        /// <summary>
        /// The same drawing in both outputs, read back mark by mark and compared in millimetres
        /// on the page: where every point lands through every matrix, what clips it, its colour,
        /// its opacity, its thickness and its dashes. Section 2's fourth check. Nothing here looks
        /// for bytes - each file goes through an interpreter that keeps a transform stack, as a
        /// reader does. Then some of the same numbers worked out by hand, since two outputs fed
        /// by one list can agree on being wrong.
        /// </summary>
        [Fact]
        public void Test102_BothOutputsDrawTheSameDrawing()
        {
            const string testName = "Test 102";
            Console.WriteLine("Test 102: the PDF and the proof put a drawing's marks in the same places");
            Console.WriteLine();

            FontLibrary fonts = DrawingFonts();

            const string Shapes = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='10 5 200 100'>"
                + "<clipPath id='c' transform='translate(5 5)'><rect x='20' y='10' width='150' height='70' transform='translate(1 1)'/></clipPath>"
                + "<g transform='translate(3 4) scale(1.5)' opacity='.5'>"
                + "<rect x='20' y='10' width='30' height='20' rx='4' fill='#336699' stroke='crimson' stroke-width='2' stroke-opacity='.4' stroke-dasharray='3 1' stroke-dashoffset='1' stroke-linecap='round' stroke-linejoin='bevel'/>"
                + "<circle cx='70' cy='30' r='8' fill='rgba(10,20,30,.5)' fill-opacity='.5'/>"
                + "<ellipse cx='100' cy='30' rx='9' ry='4' style='fill:teal'/>"
                + "</g>"
                // A transform beside the clip: the clip is read in coordinates that include it, and
                // with neither on one element the order of the two proved nothing.
                + "<g clip-path='url(#c)' transform='translate(7 3) scale(1.2)'><polyline points='0,0 200,100 0,100' fill='none' stroke='#000' stroke-miterlimit='7'/></g>"
                + "<path fill-rule='evenodd' fill='gold' d='M 40 60 h 30 v 20 h -30 z m 5 5 h 10 v 10 h -10 z'/>"
                + "<path fill='none' stroke='black' d='M 90 60 q 10 -20 20 0 t 20 0 a 10 5 30 0 1 15 10 c 1 2 3 4 5 6 s 2 3 4 4 Z'/>"
                + "<line x1='5' y1='5' x2='60' y2='5' stroke='#555' fill='red'/>"
                + "<polygon points='150,60 170,60 160,80' fill='#0a0'/>"
                + "<g transform='rotate(30 100 50)' font-size='12' font-family='sans-serif'>"
                + "<text x='100' y='50' text-anchor='middle' fill='navy'>middle <tspan font-weight='bold' fill='#900'>bold</tspan> end</text>"
                + "<text x='190' y='70' text-anchor='end' fill-opacity='.5' font-style='italic'>to the end</text>"
                + "<text x='20' y='90' dx='2' dy='1em'>first<tspan x='20' dy='1.2em'>second</tspan></text>"
                + "</g></svg>";

            foreach ((string name, string svg, double wide, double tall) in new[]
            {
                ("the shapes, at their own proportions", Shapes, 100.0, 50.0),
                ("the shapes, stretched", Shapes, 150.0, 40.0),
                ("the sample chart", ChartSample(), 170.0, 85.0)
            })
            {
                Report sheet = SheetWith(Encoding.UTF8.GetBytes(svg), "image/svg+xml", wide, tall);
                sheet.Body!.Childs[0].As<ImageItem>().Scaling = ImageScaling.Stretch;

                string pdf = Encoding.Latin1.GetString(PdfOf(sheet, fonts, compress: false));
                List<Mark> fromPdf = ReadPdfMarks(pdf);
                List<Mark> fromProof = ReadProofMarks(ProofOf(sheet, fonts).Pages[0]);

                Console.WriteLine($"    {name}: {fromPdf.Count} marks in the PDF, {fromProof.Count} in the proof");
                AssertEqual(testName, $"{name}: there are marks", true, fromPdf.Count > 3, 0);
                AssertEqual(testName, $"{name}: the same number of marks", fromProof.Count, fromPdf.Count, 0);

                for (int i = 0; i < fromPdf.Count; i++)
                {
                    string difference = fromPdf[i].DifferenceFrom(fromProof[i]);
                    AssertEqual(testName, $"{name}: mark {i} ({fromPdf[i].Kind})", string.Empty, difference, 0);
                }
            }

            // By hand. A frame at (20,30) of 100 x 50 over a viewBox of 200 x 100 from (10,5).
            List<Mark> marks = ReadPdfMarks(Encoding.Latin1.GetString(PdfOf(
                SheetWith(Encoding.UTF8.GetBytes(
                    "<svg viewBox='10 5 200 100'><rect x='20' y='15' width='40' height='20' fill='#ff0000'/>"
                    + "<g transform='rotate(90 110 55)'><line x1='110' y1='55' x2='130' y2='55' stroke='blue' stroke-width='4'/></g>"
                    + "<path d='M 110 55 A 20 20 0 0 1 130 75' fill='none' stroke='lime'/></svg>"), "image/svg+xml"), fonts, compress: false)));

            AssertEqual(testName, "three shapes", 3, marks.Count, 0);
            (double X, double Y) corner = marks[0].Points[0];
            AssertEqual(testName, "the rectangle's corner lands where the viewBox puts it, across", 20 + (20 - 10) / 2.0, corner.X, 0.01);
            AssertEqual(testName, "and down", 30 + (15 - 5) / 2.0, corner.Y, 0.01);
            AssertEqual(testName, "its far side", 20 + (60 - 10) / 2.0, marks[0].Points[2].X, 0.01);
            AssertEqual(testName, "its colour", "1 0 0", marks[0].Colour, 0);

            // A quarter turn clockwise on screen takes the line from pointing right to pointing down.
            AssertEqual(testName, "a turned line starts at its pivot", 20 + (110 - 10) / 2.0, marks[1].Points[0].X, 0.01);
            AssertEqual(testName, "and goes down the page, not across", marks[1].Points[0].X, marks[1].Points[1].X, 0.01);
            AssertEqual(testName, "by its length, scaled", marks[1].Points[0].Y + 20 / 2.0, marks[1].Points[1].Y, 0.01);
            AssertEqual(testName, "the stroke is scaled with the drawing", 2, marks[1].Width, 0.01);

            // An arc from (110,55) to (130,75) of radius 20, sweeping clockwise on screen, is the
            // quarter circle about (110,75): its middle is 20 from there.
            (double X, double Y) end = marks[2].Points[^1];
            AssertEqual(testName, "an arc ends where it says", 20 + (130 - 10) / 2.0, end.X, 0.01);
            AssertEqual(testName, "and down", 30 + (75 - 5) / 2.0, end.Y, 0.01);
            (double X, double Y) control = marks[2].Points[1];
            AssertEqual(testName, "its first control leaves along the tangent", marks[2].Points[0].Y, control.Y, 0.01);

            // A label by hand too: its width is the engine's own, and where it starts follows.
            // The em is 20 units of a viewBox drawn at half scale, so 10 mm on the page.
            var labels = new FontTextMeasurer(fonts);
            double Wide(string words) => labels.Measure(words, new TextStyle
            {
                Size = 20 / TextStyle.PointsToMillimeters, SizeUnit = FontSizeUnit.Point
            }, double.PositiveInfinity).Width;

            List<Mark> set = ReadPdfMarks(Encoding.Latin1.GetString(PdfOf(
                SheetWith(Encoding.UTF8.GetBytes(
                    "<svg viewBox='10 5 200 100' font-size='20'>"
                    + "<text x='100' y='50'>Start</text>"
                    + "<text x='100' y='50' text-anchor='middle'>Middle</text>"
                    + "<text x='100' y='50' text-anchor='end'>End</text>"
                    + "<text x='100' y='50' dx='5' dy='.5em'>Shift</text>"
                    + "<text x='30' y='20'>One<tspan x='30' dy='1.5em'>Two</tspan><tspan dx='2'>Three</tspan></text>"
                    // The position on the text and the words in its only tspan, as generators write.
                    + "<text x='60' y='70'><tspan fill='#900'>Inside</tspan></text>"
                    + "</svg>"), "image/svg+xml"), fonts, compress: false)));

            double OnPageX(double x) => 20 + (x - 10) / 2.0;
            double OnPageY(double y) => 30 + (y - 5) / 2.0;

            AssertEqual(testName, "eight pieces of text", 8, set.Count, 0);
            AssertEqual(testName, "a text's x reaches the tspan that opens it", OnPageX(60), set[7].Points[0].X, 0.01);
            AssertEqual(testName, "and its y", OnPageY(70), set[7].Points[0].Y, 0.01);
            AssertEqual(testName, "a label starts at its x", OnPageX(100), set[0].Points[0].X, 0.01);
            AssertEqual(testName, "on its baseline", OnPageY(50), set[0].Points[0].Y, 0.01);
            AssertEqual(testName, "its em is scaled with the drawing", 10, set[0].Size, 0.01);
            AssertEqual(testName, "text-anchor middle takes half its width", OnPageX(100 - Wide("Middle") / 2), set[1].Points[0].X, 0.01);
            AssertEqual(testName, "text-anchor end takes all of it", OnPageX(100 - Wide("End")), set[2].Points[0].X, 0.01);
            AssertEqual(testName, "dx moves it along", OnPageX(105), set[3].Points[0].X, 0.01);
            AssertEqual(testName, "dy in em moves it down by the size", OnPageY(60), set[3].Points[0].Y, 0.01);
            AssertEqual(testName, "a tspan with its own x starts a line", OnPageX(30), set[5].Points[0].X, 0.01);
            AssertEqual(testName, "and dy puts it under", OnPageY(50), set[5].Points[0].Y, 0.01);
            AssertEqual(testName, "a tspan with neither follows what came before", OnPageX(30 + Wide("Two") + 2), set[6].Points[0].X, 0.01);
            AssertEqual(testName, "on the same line", set[5].Points[0].Y, set[6].Points[0].Y, 0.01);

            Console.WriteLine();
        }

        /// <summary>One painting, in millimetres on the page, as read back from a file.</summary>
        private sealed class Mark
        {
            public string Kind = "";
            public List<(double X, double Y)> Points = new();
            public List<List<(double X, double Y)>> Clips = new();
            public string Colour = "";
            public double Alpha = 1;
            public double Width;
            public string Dash = "";
            public double DashOffset;
            public string Caps = "";
            public bool EvenOdd;
            public double Size;
            public string Text = "";

            public string DifferenceFrom(Mark o)
            {
                const double near = 0.015;
                if (Kind != o.Kind) return $"a {Kind} against a {o.Kind}";
                if (Points.Count != o.Points.Count) return $"{Points.Count} points against {o.Points.Count}";
                for (int i = 0; i < Points.Count; i++)
                {
                    if (Math.Abs(Points[i].X - o.Points[i].X) > near || Math.Abs(Points[i].Y - o.Points[i].Y) > near)
                        return $"point {i}: {Points[i]} against {o.Points[i]}";
                }
                if (Clips.Count != o.Clips.Count) return $"{Clips.Count} clips against {o.Clips.Count}";
                for (int c = 0; c < Clips.Count; c++)
                {
                    if (Clips[c].Count != o.Clips[c].Count) return $"clip {c}: {Clips[c].Count} points against {o.Clips[c].Count}";
                    for (int i = 0; i < Clips[c].Count; i++)
                    {
                        if (Math.Abs(Clips[c][i].X - o.Clips[c][i].X) > near || Math.Abs(Clips[c][i].Y - o.Clips[c][i].Y) > near)
                            return $"clip {c}, point {i}: {Clips[c][i]} against {o.Clips[c][i]}";
                    }
                }
                if (Colour != o.Colour) return $"colour {Colour} against {o.Colour}";
                if (Math.Abs(Alpha - o.Alpha) > 0.002) return $"alpha {Alpha} against {o.Alpha}";
                if (Math.Abs(Width - o.Width) > near) return $"width {Width} against {o.Width}";
                if (Dash != o.Dash) return $"dashes {Dash} against {o.Dash}";
                if (Math.Abs(DashOffset - o.DashOffset) > near) return $"dash offset {DashOffset} against {o.DashOffset}";
                if (Caps != o.Caps) return $"caps and joins {Caps} against {o.Caps}";
                if (EvenOdd != o.EvenOdd) return $"even-odd {EvenOdd} against {o.EvenOdd}";
                if (Math.Abs(Size - o.Size) > near) return $"size {Size} against {o.Size}";
                if (Text != o.Text) return $"text {Text} against {o.Text}";
                return string.Empty;
            }
        }

        private static string Fmt(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

        private static double Scale(VectorMatrix m) => Math.Sqrt(Math.Abs(m.A * m.D - m.B * m.C));

        /// <summary>A small content-stream interpreter: a graphics state stack and the operators the drawing uses.</summary>
        private static List<Mark> ReadPdfMarks(string file)
        {
            // Alpha is on the named states, in the page's resources.
            var alphas = new Dictionary<string, double>();
            foreach (Match state in Regex.Matches(file, @"/(GS\d+) << /Type /ExtGState /BM /Normal /ca ([\d.]+)"))
                alphas[state.Groups[1].Value] = double.Parse(state.Groups[2].Value, CultureInfo.InvariantCulture);

            int open = Regex.Match(file, @"stream\n(?=[\d.]+ 0 0 -[\d.]+ 0 [\d.]+ cm\n)").Index + 7;
            int close = file.IndexOf("endstream", open, StringComparison.Ordinal);
            string content = file[open..close];

            var marks = new List<Mark>();
            var state2 = new PdfState();
            var stack = new Stack<PdfState>();
            var operands = new List<string>();
            var path = new List<(double, double)>();
            bool first = true;
            List<(double X, double Y)>? pendingClip = null;
            VectorMatrix textMatrix = VectorMatrix.Identity;
            double fontSize = 0;

            foreach (Match token in Regex.Matches(content, @"<<|>>|\[[^\]]*\]|<[0-9A-Fa-f]*>|/[^\s/\[\]<>]+|[^\s\[\]<>/]+"))
            {
                string t = token.Value;
                if (char.IsDigit(t[0]) || t[0] is '-' or '.' or '/' or '<' or '[')
                {
                    operands.Add(t);
                    continue;
                }

                double N(int i) => double.Parse(operands[i], CultureInfo.InvariantCulture);
                (double, double) At(double x, double y) => state2.Matrix.Apply(x, y);

                void Paint(string kind, bool evenOdd)
                {
                    if (path.Count > 0 && kind != "none")
                    {
                        marks.Add(new Mark
                        {
                            Kind = kind,
                            Points = new(path),
                            Clips = state2.Clips.Select(c => new List<(double, double)>(c)).ToList(),
                            Colour = kind == "stroke" ? state2.Stroke : state2.Fill,
                            Alpha = state2.Alpha,
                            Width = kind == "stroke" ? state2.Width * Scale(state2.Matrix) : 0,
                            Dash = kind == "stroke" ? string.Join(" ", state2.Dash.Select(d => Fmt(d * Scale(state2.Matrix)))) : "",
                            DashOffset = kind == "stroke" ? state2.DashOffset * Scale(state2.Matrix) : 0,
                            Caps = kind == "stroke" ? state2.Caps : "",
                            EvenOdd = evenOdd
                        });
                    }

                    if (pendingClip != null)
                    {
                        state2.Clips = new(state2.Clips) { pendingClip };
                        pendingClip = null;
                    }

                    path.Clear();
                }

                switch (t)
                {
                    case "q": stack.Push(state2.Copy()); break;
                    case "Q": state2 = stack.Pop(); break;
                    case "cm":
                        // The first is the page's own, millimetres to points: what is read back
                        // here is millimetres, so it is left out rather than undone.
                        if (first) first = false;
                        else state2.Matrix = state2.Matrix.Times(new VectorMatrix(N(0), N(1), N(2), N(3), N(4), N(5)));
                        break;
                    case "m": case "l": path.Add(At(N(0), N(1))); break;
                    case "c":
                        path.Add(At(N(0), N(1)));
                        path.Add(At(N(2), N(3)));
                        path.Add(At(N(4), N(5)));
                        break;
                    case "re":
                        path.Add(At(N(0), N(1)));
                        path.Add(At(N(0) + N(2), N(1)));
                        path.Add(At(N(0) + N(2), N(1) + N(3)));
                        path.Add(At(N(0), N(1) + N(3)));
                        break;
                    case "h": break;
                    case "W": case "W*": pendingClip = new(path); break;
                    case "n": Paint("none", false); break;
                    case "f": Paint("fill", false); break;
                    case "f*": Paint("fill", true); break;
                    case "S": Paint("stroke", false); break;
                    case "B":
                    {
                        // A frame's rectangle, filled then stroked in one operator: two paintings
                        // of one path, as the proof writes them on one element.
                        var kept = new List<(double, double)>(path);
                        Paint("fill", false);
                        path.AddRange(kept);
                        Paint("stroke", false);
                        break;
                    }
                    case "rg": state2.Fill = $"{Fmt(N(0))} {Fmt(N(1))} {Fmt(N(2))}"; break;
                    case "RG": state2.Stroke = $"{Fmt(N(0))} {Fmt(N(1))} {Fmt(N(2))}"; break;
                    case "w": state2.Width = N(0); break;
                    case "J": state2.Cap = (int)N(0); state2.Caps = $"{state2.Cap} {state2.Join} {Fmt(state2.Miter)}"; break;
                    case "j": state2.Join = (int)N(0); state2.Caps = $"{state2.Cap} {state2.Join} {Fmt(state2.Miter)}"; break;
                    case "M": state2.Miter = N(0); state2.Caps = $"{state2.Cap} {state2.Join} {Fmt(state2.Miter)}"; break;
                    case "d":
                        state2.Dash = operands[0].Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                        state2.DashOffset = N(1);
                        break;
                    case "gs": state2.Alpha = alphas[operands[0][1..]]; break;
                    case "Tf": fontSize = N(1); break;
                    case "Tm": textMatrix = new VectorMatrix(N(0), N(1), N(2), N(3), N(4), N(5)); break;
                    case "Tj":
                    {
                        (double x, double y) = At(textMatrix.E, textMatrix.F);
                        marks.Add(new Mark
                        {
                            Kind = "text",
                            Points = { (x, y) },
                            Clips = state2.Clips.Select(c => new List<(double, double)>(c)).ToList(),
                            Colour = state2.Fill,
                            Alpha = state2.Alpha,
                            Size = fontSize * Scale(state2.Matrix),
                            Text = (operands[0].Length - 2) / 4 + " glyphs"
                        });
                        break;
                    }
                }

                operands.Clear();
            }

            return marks;
        }

        private sealed class PdfState
        {
            public VectorMatrix Matrix = VectorMatrix.Identity;
            public List<List<(double X, double Y)>> Clips = new();
            public string Fill = "0 0 0", Stroke = "0 0 0";
            public double Alpha = 1, Width = 1, DashOffset, Miter = 10;
            public double[] Dash = Array.Empty<double>();
            public int Cap, Join;
            public string Caps = "0 0 10";

            public PdfState Copy() => (PdfState)MemberwiseClone();
        }

        /// <summary>
        /// The same reading of the proof: nested svg, groups, clip references, paths and text -
        /// and with <paramref name="wholePage"/>, every mark on the page rather than what is
        /// inside a drawing, rectangles and lines included, for an item's frame.
        /// </summary>
        private static List<Mark> ReadProofMarks(string page, bool wholePage = false)
        {
            XDocument document = XDocument.Parse(page);
            XNamespace svg = "http://www.w3.org/2000/svg";
            var marks = new List<Mark>();

            var clips = document.Descendants(svg + "clipPath")
                .ToDictionary(c => (string)c.Attribute("id")!, c => (string)c.Element(svg + "path")!.Attribute("d")!);

            static double D(XElement e, string name, double absent = 0)
                => e.Attribute(name) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : absent;

            static List<(double X, double Y)> Trace(string d, VectorMatrix m)
            {
                var points = new List<(double X, double Y)>();
                foreach (Match pair in Regex.Matches(d, @"(-?[\d.]+) (-?[\d.]+)"))
                {
                    points.Add(m.Apply(double.Parse(pair.Groups[1].Value, CultureInfo.InvariantCulture),
                                       double.Parse(pair.Groups[2].Value, CultureInfo.InvariantCulture)));
                }
                return points;
            }

            static string Colour(string hex) => string.Join(" ", new[] { 1, 3, 5 }.Select(i =>
                Fmt(Math.Round(Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0, 4))));

            void Walk(XElement element, VectorMatrix m, List<List<(double, double)>> around)
            {
                foreach (XElement child in element.Elements())
                {
                    string name = child.Name.LocalName;

                    if (name == "svg")
                    {
                        double x = D(child, "x"), y = D(child, "y"), w = D(child, "width"), h = D(child, "height");
                        double[] box = child.Attribute("viewBox")!.Value.Split(' ').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                        var within = new List<List<(double, double)>>(around)
                        {
                            new() { m.Apply(x, y), m.Apply(x + w, y), m.Apply(x + w, y + h), m.Apply(x, y + h) }
                        };
                        double sx = w / box[2], sy = h / box[3];
                        Walk(child, m.Times(new VectorMatrix(sx, 0, 0, sy, x - box[0] * sx, y - box[1] * sy)), within);
                    }
                    else if (name == "g")
                    {
                        VectorMatrix inner = m;
                        if (child.Attribute("transform") is { } transform)
                        {
                            double[] v = Regex.Matches(transform.Value, @"-?[\d.]+(?:[eE]-?\d+)?").Select(s => double.Parse(s.Value, CultureInfo.InvariantCulture)).ToArray();
                            inner = m.Times(new VectorMatrix(v[0], v[1], v[2], v[3], v[4], v[5]));
                        }

                        var within = around;
                        if (child.Attribute("clip-path") is { } clip)
                        {
                            string id = clip.Value[5..^1];
                            within = new(around) { Trace(clips[id], inner).Select(p => (p.X, p.Y)).ToList() };
                        }

                        Walk(child, inner, within);
                    }
                    else if ((name == "path" || name == "rect" || name == "line") && child.Parent!.Name.LocalName != "clipPath")
                    {
                        // A rectangle's corners in the order PDF's re gives them, and a line's two ends.
                        List<(double X, double Y)> points = name switch
                        {
                            "rect" => new()
                            {
                                m.Apply(D(child, "x"), D(child, "y")),
                                m.Apply(D(child, "x") + D(child, "width"), D(child, "y")),
                                m.Apply(D(child, "x") + D(child, "width"), D(child, "y") + D(child, "height")),
                                m.Apply(D(child, "x"), D(child, "y") + D(child, "height"))
                            },
                            "line" => new() { m.Apply(D(child, "x1"), D(child, "y1")), m.Apply(D(child, "x2"), D(child, "y2")) },
                            _ => Trace((string)child.Attribute("d")!, m)
                        };
                        List<List<(double, double)>> Copied() => around.Select(c => new List<(double, double)>(c)).ToList();

                        if ((string?)child.Attribute("fill") is { } fill && fill != "none")
                        {
                            marks.Add(new Mark
                            {
                                Kind = "fill", Points = points, Clips = Copied(), Colour = Colour(fill),
                                Alpha = D(child, "fill-opacity", 1), EvenOdd = (string?)child.Attribute("fill-rule") == "evenodd"
                            });
                        }

                        if ((string?)child.Attribute("stroke") is { } stroke)
                        {
                            double caps = child.Attribute("stroke-linecap")?.Value switch { "round" => 1, "square" => 2, _ => 0 };
                            double joins = child.Attribute("stroke-linejoin")?.Value switch { "round" => 1, "bevel" => 2, _ => 0 };
                            double dashes = Scale(m);
                            marks.Add(new Mark
                            {
                                Kind = "stroke", Points = points, Clips = Copied(), Colour = Colour(stroke),
                                Alpha = D(child, "stroke-opacity", 1), Width = D(child, "stroke-width", 1) * Scale(m),
                                Dash = child.Attribute("stroke-dasharray") is { } dash
                                    ? string.Join(" ", dash.Value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => Fmt(double.Parse(s, CultureInfo.InvariantCulture) * dashes)))
                                    : "",
                                DashOffset = D(child, "stroke-dashoffset") * dashes,
                                Caps = $"{caps} {joins} {Fmt(D(child, "stroke-miterlimit", 4))}"
                            });
                        }
                    }
                    else if (name == "text")
                    {
                        marks.Add(new Mark
                        {
                            Kind = "text",
                            Points = { m.Apply(D(child, "x"), D(child, "y")) },
                            Clips = around.Select(c => new List<(double, double)>(c)).ToList(),
                            Colour = Colour((string)child.Attribute("fill")!),
                            Alpha = D(child, "fill-opacity", 1),
                            Size = D(child, "font-size") * Scale(m),
                            Text = child.Value.Length + " glyphs"
                        });
                    }
                }
            }

            // Only what is inside a drawing: the page's own background and title are not marks here.
            if (wholePage)
                Walk(document.Root!, VectorMatrix.Identity, new());
            else
                foreach (XElement drawing in document.Root!.Elements(svg + "svg"))
                    Walk(new XElement(svg + "holder", drawing), VectorMatrix.Identity, new());

            return marks;
        }

        // ---- a chart in a document ----------------------------------------------------------

        /// <summary>
        /// The road a host takes: a template that never changes, a chart in the data. The
        /// proportions come from the drawing, two charts calling their plot area the same keep
        /// their own, and the labels are real text in the file - and a mail says what it did.
        /// </summary>
        [Fact]
        public void Test103_AChartFromTheData()
        {
            const string testName = "Test 103";
            Console.WriteLine("Test 103: a chart reaches a document through the data");
            Console.WriteLine();

            FontLibrary fonts = DrawingFonts();
            string chart = ChartSample();

            string Uri(string svg) => "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            string Bare(string svg) => Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

            // A second chart using the same id for a plot area of a different size.
            string other = Well + "<clipPath id='plot'><rect x='0' y='0' width='10' height='10'/></clipPath>"
                         + "<rect clip-path='url(#plot)' width='100' height='50' fill='#a0a'/></svg>";

            string templateJson = $$"""
                {
                  "Title": "Attendance", "Language": "en-GB",
                  "Sections": [ { "Dimension": { "Width": 210, "Height": 297 },
                    "Childs": [ { "$type": "Body", "Layout": "Stack", "Childs": [
                      { "$type": "ImageItem", "SourceField": "Charts.Attendance", "AltText": "Visitors over the year" },
                      { "$type": "TextItem", "Text": "Under the chart" },
                      { "$type": "ImageItem", "SourceField": "Charts.Other", "AltText": "Another" },
                      { "$type": "ImageItem", "SourceField": "Charts.Absent", "AltText": "Nothing today" }
                    ] } ] } ]
                }
                """;
            string data = new JsonObject
            {
                ["Charts"] = new JsonObject { ["Attendance"] = Uri(chart), ["Other"] = Bare(other) }
            }.ToJsonString();

            Report template = Report.FromJson(templateJson);
            AssertEqual(testName, "the template says nothing about charts it cannot see", 0,
                TemplateValidator.Check(template, TemplateProfile.Sound).Count(i => i.Rule.StartsWith("sound.unreadable")), 0);

            Report bound = ReportBinder.Bind(template, data);
            bound.GeneratedAt = SampleDate;
            LayoutResult layout = bound.Resolve(new FontTextMeasurer(fonts));

            // Proportions from the viewBox: nobody declared a natural size, and without this a
            // picture in a flow has no height and draws nothing, in silence.
            List<PlacedItem> pictures = layout.Pages[0].Items.Where(p => p.Source is ImageItem && p.Source.Style.Visibility == Visibility.Visible).ToList();
            AssertEqual(testName, "both charts are placed, and the absent one is gone", 2, pictures.Count, 0);
            AssertEqual(testName, "the first keeps its 2:1", pictures[0].Frame.Width / 2, pictures[0].Frame.Height, 0.01);
            AssertEqual(testName, "and takes the column", true, pictures[0].Frame.Width > 150, 0);

            string dir = OutputDirectory("drawings");
            File.WriteAllText(Path.Combine(dir, "attendance.template.json"), templateJson.ReplaceLineEndings("\n"));

            var proof = new SvgBackend(new FontTextMeasurer(fonts)) { EmbeddedFonts = fonts };
            ReportRenderer.Render(bound, proof);
            File.WriteAllText(Path.Combine(dir, "attendance.html"), proof.ToHtml());

            byte[] pdf = PdfOf(bound, fonts);
            WriteSamplePdf(Path.Combine(dir, "attendance.pdf"), pdf);

            // The labels are text: the proof names them, and the PDF's marks count them.
            string page = proof.Pages[0];
            AssertEqual(testName, "the proof holds no picture of the chart", false, page.Contains("<image"), 0);
            AssertEqual(testName, "and no sign of the file it came from", false, page.Contains("id=\"plot\""), 0);
            AssertEqual(testName, "the title is text, accent and all", true, page.Contains(">Fréquentation 2026</text>"), 0);

            string uncompressed = Encoding.Latin1.GetString(PdfOf(bound, fonts, compress: false));
            AssertEqual(testName, "the PDF holds no image for it", false, uncompressed.Contains("/Subtype /Image"), 0);
            AssertEqual(testName, "and draws its labels as text", true,
                ReadPdfMarks(uncompressed).Count(m => m.Kind == "text") > 20, 0);

            // Two ids alike. An HTML proof is one document, so a repeated id is the first one's.
            string[] ids = Regex.Matches(page, "<clipPath id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
            AssertEqual(testName, "each chart has a clip", 2, ids.Length, 0);
            AssertEqual(testName, "and no two share an id", 2, ids.Distinct().Count(), 0);
            AssertEqual(testName, "and each is used", true, ids.All(id => page.Contains($"clip-path=\"url(#{id})\"")), 0);

            // The same picture on two pages must not repeat an id across the file either.
            Report twice = ReportBinder.Bind(Report.FromJson(templateJson.Replace("\"Under the chart\"", "\"Under\"")), data);
            var repeated = new SvgBackend(new FontTextMeasurer(fonts));
            ReportRenderer.Render(twice, repeated);
            string[] again = Regex.Matches(repeated.Pages[0], "<clipPath id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
            AssertEqual(testName, "a second render numbers them the same", string.Join(",", ids), string.Join(",", again), 0);

            // A label naming no family is set in the document's, as any text naming none is.
            Report styled = ReportBinder.Bind(Report.FromJson(templateJson), data);
            styled.DefaultFont = new TextStyle { Family = "Noto Sans Display" };
            var inFamily = new SvgBackend(new FontTextMeasurer(fonts));
            ReportRenderer.Render(styled, inFamily);
            AssertEqual(testName, "a label takes the document's family", true,
                Regex.IsMatch(inFamily.Pages[0], "<text [^>]*font-family=\"Noto Sans Display\"[^>]*>Fréquentation 2026<"), 0);
            AssertEqual(testName, "and the PDF is drawn with it", null!,
                Record.Exception(() => PdfOf(styled, fonts))?.Message!, 0);

            // And mail, which cannot show it, says so and writes what stands for it.
            string mail = FlowingHtml.Write(bound);
            AssertEqual(testName, "a mail holds no drawing", false, mail.Contains("image/svg+xml") || mail.Contains("<svg"), 0);
            AssertEqual(testName, "and its alternative text in its place", true, mail.Contains("Visitors over the year"), 0);

            var flowing = new RenderTarget(Tagged: true, Interactive: true, Paged: false);
            AssertEqual(testName, "the check says so before anything is sent", 2,
                TemplateValidator.Check(bound, TemplateProfile.All, flowing).Count(i => i.Rule == "can.drawingInFlow"), 0);
            AssertEqual(testName, "and says nothing for a page", 0,
                TemplateValidator.Check(bound, TemplateProfile.All, new RenderTarget(Tagged: true, Interactive: true, Paged: true))
                    .Count(i => i.Rule == "can.drawingInFlow"), 0);

            // A faded backdrop multiplies through to every mark, rather than the first state it meets.
            var sheet = new Report
            {
                Title = "Paper", Language = "en-GB", GeneratedAt = SampleDate,
                Dimension = new() { Width = 100, Height = 100 },
                Body = new() { Layout = LayoutMode.Stack },
                Backdrop = new Backdrop(new ImageSource
                {
                    Data = Encoding.UTF8.GetBytes(Well + "<rect width='100' height='50' fill='#000' fill-opacity='.5'/></svg>"),
                    MediaType = "image/svg+xml"
                }) { Opacity = 0.5 }
            };
            sheet.Reattach();
            AssertEqual(testName, "a faded drawing fades, on what it already was", 0.25,
                ReadPdfMarks(Encoding.Latin1.GetString(PdfOf(sheet, fonts, compress: false))).Single().Alpha, 0.002);
            AssertEqual(testName, "a backdrop it could not draw is said", true,
                TemplateValidator.Check(BackdropOf("<svg/>")).Any(i => i.Rule == "sound.unreadablePicture"), 0);

            Console.WriteLine();
        }

        // ---- what only the data can say -----------------------------------------------------

        private const string ChartTemplate = """
            {
              "Title": "Dossier", "Language": "fr-FR",
              "Sections": [ { "Dimension": { "Width": 210, "Height": 297 },
                "Childs": [ { "$type": "Body", "Layout": "Stack", "Childs": [
                  { "$type": "TextItem", "Text": "Rapport" },
                  { "$type": "ImageItem", "SourceField": "Charts.Attendance", "AltText": "Fréquentation" },
                  { "$type": "ContainerItem", "Layout": "Stack", "RepeatOver": "Sites", "Childs": [
                    { "$type": "ImageItem", "SourceField": "Plan", "AltText": "Plan du site" }
                  ] }
                ] } ] } ]
            }
            """;

        /// <summary>
        /// A chart the data brings, which the template knows only by name. Told beforehand, by the
        /// validator's own rules, with a pointer a program can follow: it is followed here, into
        /// the template's text, and has to arrive at the item naming the field.
        /// </summary>
        [Fact]
        public void Test104_WhatOnlyTheDataCanSay()
        {
            const string testName = "Test 104";
            Console.WriteLine("Test 104: a template and its data, asked together before anything is drawn");
            Console.WriteLine();

            static string Uri(string svg) => "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            static string Data(string chart, params string[] plans) => new JsonObject
            {
                ["Charts"] = new JsonObject { ["Attendance"] = chart },
                ["Sites"] = new JsonArray(plans.Select(p => (JsonNode)new JsonObject { ["Plan"] = p }).ToArray())
            }.ToJsonString();

            string good = Uri(Well + "<rect width='9' height='9'/></svg>");
            string bad = Uri(Well + "<script>boom()</script></svg>");
            string rgba = Convert.ToBase64String(PngEncoder.WithAlpha(4, 4, (x, y) => (1, 2, 3, 100)));
            string palette = Convert.ToBase64String(PngEncoder.WithHeader(PngEncoder.Rgb(4, 4, (x, y) => (1, 2, 3)), colourType: 3));

            Report template = Report.FromJson(ChartTemplate);

            AssertEqual(testName, "the validator cannot see any of this", 0,
                TemplateValidator.Check(template, TemplateProfile.Sound).Count(i => i.Rule == "sound.unreadablePicture"), 0);

            AssertEqual(testName, "good pictures say nothing", string.Empty,
                string.Join(" / ", TemplateData.Check(template, Data(good, rgba, good)).Select(i => i.Message)), 0);

            IReadOnlyList<TemplateIssue> chart = TemplateData.Check(template, Data(bad, rgba));
            TemplateIssue refused = chart.Single();
            Console.WriteLine($"    {refused.Rule} at {refused.Pointer}: {refused.Message}");
            AssertEqual(testName, "a chart it brings is refused beforehand", "sound.unreadablePicture", refused.Rule, 0);
            AssertEqual(testName, "as an error", IssueLevel.Error, refused.Level, 0);
            AssertEqual(testName, "in the words the document would have stopped with", true,
                refused.Message.Contains("<script>") && refused.Message.Contains("Charts.Attendance"), 0);

            Report filled = ReportBinder.Bind(template, Data(bad, rgba));
            string stopped = Record.Exception(() => PdfOf(filled, DrawingFonts()))!.Message;
            AssertEqual(testName, "the same refusal the PDF would give", true, refused.Message.EndsWith(stopped), 0);

            // The pointer, followed: into the template and not into a copy whose rows multiplied.
            JsonNode Follow(string pointer)
            {
                JsonNode at = JsonNode.Parse(ChartTemplate)!;
                foreach (string step in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    at = int.TryParse(step, out int index) ? at[index]! : at[step]!;
                return at;
            }

            AssertEqual(testName, "the pointer leads to the item naming the field", "Charts.Attendance",
                (string?)Follow(refused.Pointer!)["SourceField"] ?? "", 0);
            AssertEqual(testName, "and the item is the template's", true,
                template.AllItems.Contains(refused.Item!), 0);

            // Repeated: a fault in every entry is one place to go, and a bitmap's fault is the same fault.
            IReadOnlyList<TemplateIssue> plans = TemplateData.Check(template, Data(good, palette, palette, palette));
            AssertEqual(testName, "the same fault in a list is said once", 1, plans.Count, 0);
            AssertEqual(testName, "and leads inside the repeated block", "Plan",
                (string?)Follow(plans[0].Pointer!)["SourceField"] ?? "", 0);
            AssertEqual(testName, "two different faults in a list are two", 2,
                TemplateData.Check(template, Data(good, palette, Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }))).Count, 0);

            // What it asks about the output, only when asked.
            RenderTarget mail = new(Tagged: true, Interactive: true, Paged: false);
            AssertEqual(testName, "a mail cannot show what the data drew", "can.drawingInFlow",
                TemplateData.Check(template, Data(good), mail).Single().Rule, 0);
            AssertEqual(testName, "and a bitmap it can", 0,
                TemplateData.Check(template, Data(rgba), mail).Count, 0);

            // Nothing at that path is not a fault: an unsigned quote has no signature.
            AssertEqual(testName, "a picture the data leaves out says nothing", 0,
                TemplateData.Check(template, "{ \"Sites\": [] }").Count, 0);

            AssertEqual(testName, "data that is not JSON is said", "data.invalid",
                TemplateData.Check(template, "{ not json").Single().Rule, 0);
            AssertEqual(testName, "and so is a value that is not base64", "data.invalid",
                TemplateData.Check(template, Data("not base64 at all !!", rgba)).Single().Rule, 0);

            Console.WriteLine();
        }

        /// <summary>
        /// The same, through the command line, as a program would start it: the door for an
        /// application that is not in .NET, which the contract page sends people to.
        /// </summary>
        [SkippableFact]
        public void Test105_CheckHearsAboutTheData()
        {
            const string testName = "Test 105";
            Console.WriteLine("Test 105: frisket check -d");

            string configuration = AppContext.BaseDirectory.Contains(
                Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar) ? "Debug" : "Release";
            string cli = Path.Combine(RepositoryRoot(), "apps", "cli", "bin", configuration, "net10.0", "Frisket.Cli.dll");
            Skip.If(!File.Exists(cli), $"the CLI is not built at {cli}.");

            string dir = Path.Combine(OutputDirectory("drawings"), "cli");
            Directory.CreateDirectory(dir);
            string template = Path.Combine(dir, "template.json");
            string bad = Path.Combine(dir, "bad.json");
            string good = Path.Combine(dir, "good.json");
            File.WriteAllText(template, ChartTemplate.ReplaceLineEndings("\n"));

            static string Data(string svg) => new JsonObject
            {
                ["Charts"] = new JsonObject { ["Attendance"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)) }
            }.ToJsonString();

            File.WriteAllText(bad, Data(Well + "<script>boom()</script></svg>"));
            File.WriteAllText(good, Data(Well + "<rect width='9' height='9'/></svg>"));

            (int Exit, string Said) Frisket(params string[] arguments)
            {
                var run = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    },
                };
                run.StartInfo.ArgumentList.Add(cli);
                foreach (string argument in arguments)
                    run.StartInfo.ArgumentList.Add(argument);

                run.Start();
                using var bytes = new MemoryStream();
                run.StandardOutput.BaseStream.CopyTo(bytes);
                run.WaitForExit();
                return (run.ExitCode, Encoding.UTF8.GetString(bytes.ToArray()));
            }

            (int exit, string said) = Frisket("check", "-t", template, "-d", bad, "--json");
            Console.WriteLine(said);
            JsonNode found = JsonNode.Parse(said)!.AsArray().Single(f => (string?)f!["rule"] == "sound.unreadablePicture")!;
            AssertEqual(testName, "a chart the data brings is refused", 3, exit, 0);
            AssertEqual(testName, "with a pointer into the template", "/Sections/0/Childs/0/Childs/1", (string?)found["pointer"] ?? "", 0);
            AssertEqual(testName, "and its reason, unmangled", true, ((string?)found["where"])?.Contains('›') == true, 0);

            AssertEqual(testName, "good data is not a fault", 0, Frisket("check", "-t", template, "-d", good, "--json").Exit, 0);
            AssertEqual(testName, "and no data asks nothing of it", 0, Frisket("check", "-t", template, "--json").Exit, 0);

            string both = Frisket("check", "-t", template, "-d", good, "-d", bad, "--json").Said;
            AssertEqual(testName, "of several files, it says which", true,
                ((string?)JsonNode.Parse(both)!.AsArray().Single(f => (string?)f!["rule"] == "sound.unreadablePicture")!["where"])?.StartsWith("bad.json") == true, 0);

            Console.WriteLine();
        }

        private static Report BackdropOf(string svg) => new()
        {
            Title = "Paper", Language = "en-GB",
            Dimension = new() { Width = 100, Height = 100 },
            Body = new() { Layout = LayoutMode.Stack },
            Backdrop = new Backdrop(new ImageSource { Data = Encoding.UTF8.GetBytes(svg), MediaType = "image/svg+xml" })
        };

        // ---- what the designer's reader answers to -------------------------------------------

        /// <summary>
        /// Every sample with what the engine makes of it, for the designer's own reader to be
        /// held to (check-against-engine.js): a browser would draw these whatever they hold, so
        /// the echo reads them itself, and the two have to agree to the rule and to the point.
        /// </summary>
        private static void WriteReadings()
        {
            var samples = new List<(string Name, string Svg)>();
            foreach ((string name, string refused, string mended) in Refusals)
            {
                samples.Add((name, refused));
                samples.Add((name + ", mended", mended));
            }

            samples.Add(("the sample chart", ChartSample()));
            samples.Add(("every path command", Well + "<path d='M1 2 3 4 m1 1 1 1 L5 6 l1 1 H7 h1 V8 v1 C1 2 3 4 5 6 c1 1 2 2 3 3 S1 2 3 4 s1 1 2 2 Q1 2 3 4 q1 1 2 2 T5 5 t1 1 A5 3 30 1 0 9 9 a2 2 0 0 1 1.5.5 Z z L1 1'/></svg>"));
            samples.Add(("numbers touching", Well + "<path d='M1.5.5-2-3e1L.5-.5'/><polyline points='1,2 3-4'/></svg>"));
            samples.Add(("arcs", Well + "<path d='M0 0 A 1 1 0 0 0 50 50 A 0 5 0 0 0 60 60 A 5 5 0 0 0 60 60 A 30 10 45 1 1 10 10'/></svg>"));
            samples.Add(("basic shapes", Well + "<rect x='1' y='2' width='3' height='4' rx='1'/><rect width='9' height='9' ry='20'/><rect width='0' height='9'/><circle cx='1' cy='2' r='3'/><ellipse cx='1' cy='2' rx='3' ry='4'/><ellipse rx='3'/><line x1='1' y1='2' x2='3' y2='4' fill='red' stroke='red'/><polyline points='1,2,3,4,5,6'/><polygon points='1 2 3 4 5 6'/><polyline points=''/></svg>"));
            samples.Add(("lengths", Well + "<rect x='1px' y='2pt' width='3pc' height='4mm' rx='5cm' ry='.1in'/></svg>"));
            samples.Add(("colours", Well + "<g fill='#abc'><rect width='1' height='1' fill='#abcd'/><rect width='1' height='1' fill='#a1b2c3'/><rect width='1' height='1' fill='#a1b2c380'/><rect width='1' height='1' fill='rgb(1,2,3)'/><rect width='1' height='1' fill='rgb(1 2 3 / .5)'/><rect width='1' height='1' fill='rgba(1,2,3,50%)'/><rect width='1' height='1' fill='rgb(10%,20%,30%)'/><rect width='1' height='1' fill='SteelBlue'/><rect width='1' height='1' fill='transparent'/><rect width='1' height='1' fill='none'/><rect width='1' height='1'/></g><g color='red'><rect width='1' height='1' fill='currentColor' stroke='CURRENTCOLOR'/><g fill='currentColor'><rect width='1' height='1' color='blue'/></g></g></svg>"));
            samples.Add(("every colour name", Well + string.Concat(SvgValues.Names.Keys.Select(n => $"<rect width='1' height='1' fill='{n}'/>")) + "</svg>"));
            samples.Add(("strokes", Well + "<g stroke='#000' stroke-width='2' stroke-dasharray='1 2' stroke-dashoffset='3' stroke-linecap='round' stroke-linejoin='bevel' stroke-miterlimit='6' stroke-opacity='50%' fill='none'><line x2='9'/><line x2='9' stroke-dasharray='none'/><line x2='9' stroke-dasharray='0 0'/><line x2='9' stroke-width='0'/></g></svg>"));
            samples.Add(("style wins", Well + "<rect width='1' height='1' fill='red' style='fill:blue !important; opacity:.5;;'/></svg>"));
            samples.Add(("inherit", Well + "<g fill='red'><rect width='1' height='1' fill='inherit'/></g></svg>"));
            samples.Add(("opacity multiplies", Well + "<g opacity='.5'><g opacity='.5'><rect width='1' height='1' fill-opacity='.5' opacity='.5'/></g></g></svg>"));
            samples.Add(("transforms", Well + "<g transform='translate(1) translate(1,2) scale(2) scale(2 3) rotate(30) rotate(30 1 2) skewX(10) skewY(20) matrix(1 2 3 4 5 6)'><rect width='1' height='1'/></g></svg>"));
            samples.Add(("clips", Well + "<g clip-path='url(#a)' transform='scale(2)'><rect width='1' height='1'/></g><rect width='1' height='1' clip-path='url(\"#b\")' clip-rule='evenodd'/><text clip-path=\"url('#a')\">x</text><defs><clipPath id='a' transform='scale(2)'><circle r='5' transform='translate(1 1)'/></clipPath><clipPath id='b'><path clip-rule='evenodd' d='M0 0H1V1Z'/></clipPath></defs></svg>"));
            samples.Add(("hidden things", Well + "<g display='none'><rect width='1' height='1'/></g><rect width='1' height='1' display='none'/><rect width='1' height='1' visibility='hidden'/><g visibility='hidden'><rect width='1' height='1' visibility='visible'/></g><text visibility='hidden'>x</text></svg>"));
            samples.Add(("text", Well + "<text x='1' y='2'>  plain   words\n and more  </text><text x='1' y='2' dx='3' dy='4'><tspan>first</tspan> <tspan fill='red'>second</tspan></text><text font-family=\"'Mulish', serif\" font-size='12pt' font-weight='700' font-style='oblique' text-anchor='end' x='9'>a<tspan x='3' dy='1em' font-weight='400' font-style='normal' font-size='2mm' text-anchor='middle' dx='.5em'>b</tspan>c</text><text xml:space='preserve'>  kept   spaces  </text><g xml:space='preserve'><text> also <tspan xml:space='default'> not </tspan></text></g><text>&lt;&amp;&gt;&quot;&apos;&#233;&#xE9;<![CDATA[<raw&>]]></text><text>   </text><text fill='none'>gone</text><text font-size='0'>gone</text></svg>"));
            samples.Add(("no viewBox", "<svg width='10mm' height='5mm'><rect width='1' height='1'/></svg>"));
            samples.Add(("the root draws too", "<svg viewBox='0 0 9 9' transform='scale(2)' fill='red' opacity='.5'><rect width='1' height='1'/></svg>"));
            samples.Add(("a byte order mark", "﻿" + Well + "</svg>"));

            var written = new JsonArray();
            foreach ((string name, string svg) in samples)
            {
                SvgReading reading = SvgReader.Read(Encoding.UTF8.GetBytes(svg));
                written.Add(new JsonObject
                {
                    ["name"] = name,
                    ["svg"] = svg,
                    ["rules"] = new JsonArray(reading.Issues.Select(i => (JsonNode)JsonValue.Create(i.Rule)!).ToArray()),
                    ["drawing"] = reading.Drawing == null ? null : DescribeDrawing(reading.Drawing)
                });
            }

            // And the bitmaps, which the designer's echo answers about from their headers.
            byte[] rgb = PngEncoder.Rgb(8, 8, (x, y) => (1, 2, 3));
            byte[] wide = PngEncoder.WithAlpha(8, 8, (x, y) => (1, 2, 3, 200));
            wide[16] = 0x7F;
            byte[] truncated = rgb[..40];

            var pictures = new JsonArray();
            foreach ((string name, byte[] data) in new[]
            {
                ("RGB", rgb),
                ("RGB with alpha", PngEncoder.WithAlpha(8, 8, (x, y) => (1, 2, 3, 200))),
                ("gray with alpha", PngEncoder.WithAlpha(8, 8, (x, y) => (1, 2, 3, 200), gray: true)),
                ("a palette", PngEncoder.WithHeader(rgb, colourType: 3)),
                ("interlacing", PngEncoder.WithHeader(rgb, interlace: 1)),
                ("16 bits", PngEncoder.WithHeader(rgb, depth: 16)),
                ("a size its data cannot hold", wide),
                ("a file cut short", truncated),
                ("a JPEG, by its opening", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 74, 70, 73, 70 }),
                ("bytes that are nothing", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }),
                ("a refused drawing", Encoding.UTF8.GetBytes(Well + "<script/></svg>")),
                ("a drawing that is not UTF-8", new byte[] { 0x3C, 0x73, 0xFF, 0x3E }),
                ("a drawing", Encoding.UTF8.GetBytes(Well + "</svg>")),
            })
            {
                pictures.Add(new JsonObject
                {
                    ["name"] = name,
                    ["base64"] = Convert.ToBase64String(data),
                    ["refused"] = Pictures.Refusal(new ImageSource { Data = data }) != null
                });
            }

            File.WriteAllText(Path.Combine(OutputDirectory("drawings"), "pictures.json"),
                pictures.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n"));

            File.WriteAllText(Path.Combine(OutputDirectory("drawings"), "readings.json"),
                written.ToJsonString(new JsonSerializerOptions { WriteIndented = true, MaxDepth = 512 }).ReplaceLineEndings("\n"));
        }

        private static JsonNode Round(double value) => JsonValue.Create(Math.Round(value, 4) + 0.0)!;

        private static string? Hex(Color? c) => c is { } k ? $"#{k.R:X2}{k.G:X2}{k.B:X2}" : null;

        private static string Described(VectorPath path)
        {
            var d = new StringBuilder();
            int at = 0;
            foreach (VectorVerb verb in path.Verbs)
            {
                d.Append(verb switch { VectorVerb.Move => 'M', VectorVerb.Line => 'L', VectorVerb.Cubic => 'C', _ => 'Z' });
                int count = verb switch { VectorVerb.Cubic => 6, VectorVerb.Close => 0, _ => 2 };
                for (int i = 0; i < count; i++)
                    d.Append(i == 0 ? "" : " ").Append(Math.Round(path.Points[at++], 4).ToString(CultureInfo.InvariantCulture));
            }
            return d.ToString();
        }

        private static JsonObject DescribeDrawing(VectorDrawing drawing) => new()
        {
            ["box"] = new JsonArray(Round(drawing.X), Round(drawing.Y), Round(drawing.Width), Round(drawing.Height)),
            ["nodes"] = new JsonArray(drawing.Nodes.Select(DescribeNode).ToArray())
        };

        private static JsonNode DescribeNode(VectorNode node)
        {
            VectorMatrix m = node.Transform;
            var written = new JsonObject
            {
                ["m"] = new JsonArray(Round(m.A), Round(m.B), Round(m.C), Round(m.D), Round(m.E), Round(m.F)),
                ["clip"] = node.Clip == null ? null : new JsonObject { ["d"] = Described(node.Clip.Path), ["evenOdd"] = node.Clip.EvenOdd }
            };

            switch (node)
            {
                case VectorGroup group:
                    written["kind"] = "group";
                    written["children"] = new JsonArray(group.Children.Select(DescribeNode).ToArray());
                    break;

                case VectorShape shape:
                    written["kind"] = "shape";
                    written["d"] = Described(shape.Path);
                    written["fill"] = Hex(shape.Fill);
                    written["fillAlpha"] = Round(shape.Fill == null ? 0 : shape.FillAlpha);
                    written["evenOdd"] = shape.EvenOdd;
                    written["stroke"] = Hex(shape.Stroke);
                    written["strokeAlpha"] = Round(shape.Stroke == null ? 0 : shape.StrokeAlpha);
                    written["strokeWidth"] = Round(shape.StrokeWidth);
                    written["dash"] = new JsonArray(shape.Dash.Select(Round).ToArray());
                    written["dashOffset"] = Round(shape.DashOffset);
                    written["cap"] = shape.Cap;
                    written["join"] = shape.Join;
                    written["miter"] = Round(shape.MiterLimit);
                    break;

                case VectorText text:
                    written["kind"] = "text";
                    written["runs"] = new JsonArray(text.Runs.Select(run => (JsonNode)new JsonObject
                    {
                        ["text"] = run.Text,
                        ["x"] = run.X is { } x ? Round(x) : null,
                        ["y"] = run.Y is { } y ? Round(y) : null,
                        ["dx"] = Round(run.Dx),
                        ["dy"] = Round(run.Dy),
                        ["anchor"] = (int)run.Anchor,
                        ["family"] = run.Style.Family,
                        ["size"] = Round(run.Style.EmSizeInMillimeters),
                        ["bold"] = run.Style.Bold == true,
                        ["italic"] = run.Style.Italic == true,
                        ["fill"] = Hex(run.Style.Color),
                        ["alpha"] = Round(run.Alpha)
                    }).ToArray());
                    break;
            }

            return written;
        }
    }

    internal static class ItemCast
    {
        public static T As<T>(this BaseItem item) where T : BaseItem => (T)item;
    }
}
