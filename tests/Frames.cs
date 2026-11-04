using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace test
{
    /// <summary>
    /// An item's frame: which sides of it the border is drawn on, how far each corner is
    /// rounded, and translucent ink that the PDF fades as the proof always did.
    /// </summary>
    public partial class EngineTests
    {
        /// <summary>
        /// A frame asked for nothing new is the rectangle it always was, to the byte, in both
        /// outputs. Asked for sides or corners, the PDF and the proof draw the same marks in the
        /// same places - read back through an interpreter, never looked for as bytes (section 2) -
        /// and some of those numbers are worked out by hand, since two outputs fed by one
        /// geometry can agree on being wrong. The template carries both properties and gives
        /// them back, and the cases are written out for the designer's echo to be held to.
        /// </summary>
        [Fact]
        public void Test116_AFrameDrawsTheSidesAndCornersItIsGiven()
        {
            const string testName = "Test 116";
            Console.WriteLine("Test 116: a frame draws the sides and the corners it is given, and nothing else");
            Console.WriteLine();

            // ---- what asks for nothing new draws what it always drew --------------------
            var box = new Rect(10, 20, 60, 40);
            var solid = new PenStyle { Width = 0.3 };
            var tint = new BrushStyle { Color = new Color(230, 235, 240) };
            var faint = new PenStyle { Color = new Color(0, 0, 255, 128), Width = 0.6, DashStyle = DashStyle.Dash };
            var veil = new BrushStyle { Color = new Color(200, 0, 0, 51) };

            foreach ((string name, PenStyle? pen, BrushStyle? fill) in new[]
            {
                ("a border and a fill", solid, tint),
                ("a border alone", solid, (BrushStyle?)null),
                ("a fill alone", (PenStyle?)null, tint),
                ("in translucent ink", faint, veil)
            })
            {
                AssertEqual(testName, $"{name}, closed all round and square: the PDF draws the rectangle it drew",
                    PdfPageOf(b => b.DrawRectangle(box, pen, fill)),
                    PdfPageOf(b => b.DrawFrame(box, pen, fill, Sides.All, new CornerRadius())), 0);
                AssertEqual(testName, $"{name}, closed all round and square: and so does the proof",
                    SvgPageOf(b => b.DrawRectangle(box, pen, fill)),
                    SvgPageOf(b => b.DrawFrame(box, pen, fill, Sides.All, new CornerRadius())), 0);
            }

            AssertEqual(testName, "a pen with no side to draw is no border at all, in the PDF",
                PdfPageOf(b => b.DrawRectangle(box, null, tint)),
                PdfPageOf(b => b.DrawFrame(box, solid, tint, Sides.None, new CornerRadius())), 0);
            AssertEqual(testName, "and in the proof",
                SvgPageOf(b => b.DrawRectangle(box, null, tint)),
                SvgPageOf(b => b.DrawFrame(box, solid, tint, Sides.None, new CornerRadius())), 0);
            AssertEqual(testName, "a corner too small to see is no corner: a frame of no height cannot be rounded",
                PdfPageOf(b => b.DrawRectangle(new Rect(10, 20, 60, 20), solid, tint)),
                PdfPageOf(b => b.DrawFrame(new Rect(10, 20, 60, 20), solid, tint, Sides.All, new CornerRadius { TopLeft = 3 })), 0);

            // ---- the sheet: both outputs, mark by mark ---------------------------------
            FontLibrary fonts = DrawingFonts();
            Report sheet = FrameSheet(out List<(ContainerItem Frame, int Marks)> frames, out LineItem rule);

            List<Mark> fromPdf = ReadPdfMarks(Encoding.Latin1.GetString(Unpacked(PdfOf(sheet, fonts, compress: false))))
                .Where(m => m.Kind != "text").ToList();
            var bare = new SvgBackend(new FontTextMeasurer(fonts)) { PageBackground = new Color(255, 255, 255, 0) };
            ReportRenderer.Render(sheet, bare);
            List<Mark> fromProof = ReadProofMarks(bare.Pages[0], wholePage: true).Where(m => m.Kind != "text").ToList();

            // The miter limit is left out of the comparison, and only it: a plain rectangle and a
            // rule leave the PDF at its default of 10 where SVG's is 4, and a limit decides only
            // a join sharper than 29 degrees. A frame has right angles and smooth curves.
            foreach (Mark m in fromPdf.Concat(fromProof))
                m.Caps = string.Join(' ', m.Caps.Split(' ').Take(2));

            int expected = frames.Sum(f => f.Marks) + 1;
            Console.WriteLine($"    {fromPdf.Count} marks in the PDF, {fromProof.Count} in the proof, {expected} asked for");
            AssertEqual(testName, "the PDF draws every fill and every border asked for, and no other", expected, fromPdf.Count, 0);
            AssertEqual(testName, "and so does the proof", expected, fromProof.Count, 0);
            for (int i = 0; i < fromPdf.Count; i++)
                AssertEqual(testName, $"mark {i} ({fromPdf[i].Kind}) is the same in both", string.Empty, fromPdf[i].DifferenceFrom(fromProof[i]), 0);

            // Each frame's marks, in the order they were drawn.
            var marksOf = new List<List<Mark>>();
            int at = 0;
            foreach ((ContainerItem frame, int count) in frames)
            {
                marksOf.Add(fromPdf.GetRange(at, count));
                at += count;
            }
            Mark ruleMark = fromPdf[at];

            // ---- and some of it by hand -------------------------------------------------
            const double K = 0.5522847498307936;
            (double L, double T, double R, double B) Edges(int i)
            {
                ReadOnlyFrameDimension f = frames[i].Frame.Style.RenderedFrame;
                return (f.Left, f.Top, f.Right, f.Bottom);
            }
            void At(string what, (double X, double Y) point, double x, double y)
            {
                AssertEqual(testName, what + ", across", x, point.X, 0.002);
                AssertEqual(testName, what + ", down", y, point.Y, 0.002);
            }

            (double L, double T, double R, double B) e = Edges(0);
            AssertEqual(testName, "a square frame is filled, then stroked", "fill stroke", string.Join(' ', marksOf[0].Select(m => m.Kind)), 0);
            At("its fill starts at the frame's corner", marksOf[0][0].Points[0], e.L, e.T);
            At("and reaches the opposite one", marksOf[0][0].Points[2], e.R, e.B);

            AssertEqual(testName, "translucent, the fill is faded in the PDF", 0.2, marksOf[1][0].Alpha, 0.0005);
            AssertEqual(testName, "and the border by its own amount", 0.502, marksOf[1][1].Alpha, 0.0005);
            AssertEqual(testName, "the border keeps its colour", "0 0 1", marksOf[1][1].Colour, 0);

            e = Edges(2);
            List<(double X, double Y)> round = marksOf[2][0].Points;
            At("a rounded corner's straight part starts its radius in", round[0], e.L + 4, e.T);
            At("and stops its radius short of the next corner", round[1], e.R - 4, e.T);
            At("the curve leaves along the top", round[2], e.R - 4 + 4 * K, e.T);
            At("and arrives along the side", round[3], e.R, e.T + 4 - 4 * K);
            At("a radius away from both sides", round[4], e.R, e.T + 4);
            AssertEqual(testName, "the corner left square is the frame's own corner", true,
                round.Any(p => Math.Abs(p.X - e.L) < 0.002 && Math.Abs(p.Y - e.B) < 0.002), 0);
            AssertEqual(testName, "and the border follows the fill exactly", true,
                round.SequenceEqual(marksOf[2][1].Points), 0);

            e = Edges(3);
            List<(double X, double Y)> bend = marksOf[3][0].Points;
            AssertEqual(testName, "two sides that meet are one path, bent once", 6, bend.Count, 0);
            At("it starts at the foot of the left side, which has no corner to stop at", bend[0], e.L, e.B);
            At("goes up to where the corner begins", bend[1], e.L, e.T + 4);
            At("and ends at the far end of the top", bend[5], e.R, e.T);

            e = Edges(4);
            List<(double X, double Y)> facing = marksOf[4][1].Points;
            AssertEqual(testName, "two sides facing each other are two rules in one painting", 4, facing.Count, 0);
            At("the top stops short of its corners", facing[0], e.L + 4, e.T);
            At("at both ends", facing[1], e.R - 4, e.T);
            At("and so does the bottom", facing[2], e.R - 4, e.B);
            AssertEqual(testName, "while the fill keeps every corner", 17, marksOf[4][0].Points.Count, 0);

            e = Edges(5);
            AssertEqual(testName, "one side between two rounded corners is a straight rule", 2, marksOf[5][1].Points.Count, 0);
            At("from where the bottom corner begins", marksOf[5][1].Points[0], e.L, e.B - 3);
            At("to where the top corner begins", marksOf[5][1].Points[1], e.L, e.T + 3);
            AssertEqual(testName, "with the pen it was given", 1.2, marksOf[5][1].Width, 0.002);

            e = Edges(6);
            List<(double X, double Y)> open = marksOf[6][1].Points;
            AssertEqual(testName, "three sides are one path round two corners", 10, open.Count, 0);
            At("from the open end on the left", open[0], e.L, e.B - 3);
            At("to the open end on the right", open[^1], e.R, e.B - 3);

            e = Edges(7);
            double half = (e.B - e.T) / 2;
            At("radii larger than the frame are reduced until they fit, all four alike", marksOf[7][0].Points[0], e.L + half, e.T);
            At("so the ends come out as half circles", marksOf[7][0].Points[4], e.R, e.T + half);

            AssertEqual(testName, "a dashed rounded border keeps its dashes", "2 1", marksOf[8][0].Dash, 0);
            AssertEqual(testName, "a pen with no side to draw leaves the fill alone", "fill", string.Join(' ', marksOf[10].Select(m => m.Kind)), 0);
            AssertEqual(testName, "a rule in translucent ink is faded in the PDF too", 0.302, ruleMark.Alpha, 0.0005);
            AssertEqual(testName, "and it is the rule", rule.Pen.Width, ruleMark.Width, 0.002);

            // ---- a surface that knows only rectangles and lines -------------------------
            var plain = new RectanglesAndLines();
            ((IReportBackend)plain).DrawFrame(box, solid, tint, Sides.Left | Sides.Top, new CornerRadius { TopLeft = 3 });
            AssertEqual(testName, "a backend of somebody else's draws the sides asked for and the corners square",
                "rect fill; line 10 20 60 20; line 10 40 10 20", string.Join("; ", plain.Calls), 0);
            plain.Calls.Clear();
            ((IReportBackend)plain).DrawFrame(box, solid, tint, Sides.All, new CornerRadius { TopLeft = 3 });
            AssertEqual(testName, "and closed all round, the one rectangle", "rect pen fill", string.Join("; ", plain.Calls), 0);

            // ---- the template carries both, and gives them back ------------------------
            const string Template = """
                { "Title": "Frames", "Language": "en-GB",
                  "Sections": [ { "$type": "Section", "Dimension": { "Width": 210, "Height": 297 },
                    "Childs": [ { "$type": "Body", "Layout": "Stack", "Childs": [
                      { "$type": "ContainerItem", "Style": { "Border": { "Width": 0.3 } } },
                      { "$type": "ContainerItem", "Style": { "Border": { "Width": 0.3 }, "BorderSides": "None" } },
                      { "$type": "ContainerItem", "Style": { "Border": { "Width": 0.3 }, "BorderSides": "Left, Top",
                                                             "CornerRadius": { "TopLeft": 4 } } } ] } ] } ] }
                """;
            Report read = Report.FromJson(Template);
            IReadOnlyList<BaseItem> kids = read.Body!.Childs;
            AssertEqual(testName, "a border that names no sides is drawn on all four", Sides.All, kids[0].Style.BorderSides, 0);
            AssertEqual(testName, "None is read, though it is the enum's zero", Sides.None, kids[1].Style.BorderSides, 0);
            AssertEqual(testName, "sides read as a list", Sides.Left | Sides.Top, kids[2].Style.BorderSides, 0);
            AssertEqual(testName, "a frame names no corner and is square", 0.0, kids[0].Style.CornerRadius.TopLeft, 0);
            AssertEqual(testName, "a corner named is read", 4.0, kids[2].Style.CornerRadius.TopLeft, 0);
            AssertEqual(testName, "and the others stay square", 0.0, kids[2].Style.CornerRadius.BottomRight, 0);

            JsonNode written = JsonNode.Parse(read.ToJson())!;
            JsonNode Style(int i) => written["Sections"]![0]!["Childs"]!.AsArray().First(c => (string?)c!["$type"] == "Body")!["Childs"]![i]!["Style"]!;
            AssertEqual(testName, "All is left out of the file, as every default is", false, Style(0).AsObject().ContainsKey("BorderSides"), 0);
            AssertEqual(testName, "and a square frame names no corner", false, Style(0).AsObject().ContainsKey("CornerRadius"), 0);
            AssertEqual(testName, "None is written, or it would come back as All", "None", (string?)Style(1)["BorderSides"] ?? "absent", 0);
            AssertEqual(testName, "a list is written as the engine reads it", "Left, Top", (string?)Style(2)["BorderSides"] ?? "absent", 0);
            AssertEqual(testName, "only the corner rounded is written", "TopLeft",
                string.Join(",", Style(2)["CornerRadius"]!.AsObject().Select(p => p.Key)), 0);

            BaseItem copy = kids[2].Clone();
            AssertEqual(testName, "a copy keeps the sides, as a repeated list makes one", Sides.Left | Sides.Top, copy.Style.BorderSides, 0);
            AssertEqual(testName, "and the corners", 4.0, copy.Style.CornerRadius.TopLeft, 0);
            AssertEqual(testName, "of its own, so rounding one copy rounds no other", false,
                ReferenceEquals(copy.Style.CornerRadius, kids[2].Style.CornerRadius), 0);

            // ---- for the eye, and for the designer's echo --------------------------------
            string folder = OutputDirectory("frames");
            WriteSamplePdf(Path.Combine(folder, "frames.pdf"), PdfOf(sheet, fonts));
            var proof = new SvgBackend(new FontTextMeasurer(fonts)) { EmbeddedFonts = fonts };
            ReportRenderer.Render(sheet, proof);
            File.WriteAllText(Path.Combine(folder, "frames.html"), proof.ToHtml());

            JsonArray cases = FrameCases(frames);
            File.WriteAllText(Path.Combine(folder, "outlines.json"), cases.ToJsonString(new() { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
            Console.WriteLine($"    {cases.Count} outlines written for the echo");
            AssertEqual(testName, "the echo has every combination of sides to answer for", true,
                Enumerable.Range(0, 16).All(mask => cases.Any(c => (string?)c!["style"]!["BorderSides"] == ((Sides)mask).ToString())), 0);

            Console.WriteLine();
        }

        /// <summary>The first page's content stream, after one call on a backend nothing else has touched.</summary>
        private static string PdfPageOf(Action<IReportBackend> draw)
        {
            var pdf = new PdfBackend(new FontLibrary()) { Compress = false };
            pdf.BeginDocument(new Report { Title = "Frames", GeneratedAt = SampleDate });
            pdf.BeginPage(new Page(1, new Rect(0, 0, 210, 297)));
            draw(pdf);
            pdf.EndPage();
            pdf.EndDocument();

            string file = Encoding.Latin1.GetString(Unpacked(pdf.ToArray()));
            int open = System.Text.RegularExpressions.Regex.Match(file, @"stream\n(?=[\d.]+ 0 0 -[\d.]+ 0 [\d.]+ cm\n)").Index + 7;
            return file[open..file.IndexOf("endstream", open, StringComparison.Ordinal)];
        }

        private static string SvgPageOf(Action<IReportBackend> draw)
        {
            var svg = new SvgBackend();
            svg.BeginDocument(new Report { Title = "Frames", GeneratedAt = SampleDate });
            svg.BeginPage(new Page(1, new Rect(0, 0, 210, 297)));
            draw(svg);
            svg.EndPage();
            svg.EndDocument();
            return svg.Pages[0];
        }

        /// <summary>
        /// A page of frames, each saying what it was asked for and what to look at, with the
        /// number of marks each should come to: one for its fill, one for its border.
        /// </summary>
        private static Report FrameSheet(out List<(ContainerItem Frame, int Marks)> frames, out LineItem rule)
        {
            var report = new Report
            {
                Title = "Frames: sides and corners", Language = "en-GB",
                GeneratedAt = SampleDate,
                Dimension = new() { Width = 210, Height = 297 },
                Body = new() { Layout = LayoutMode.Stack }
            };
            report.Body!.Style.Padding = new EdgeSize { Left = 20, Top = 12, Right = 20, Bottom = 12 };

            var heading = new TextItem { Text = "Frames: which sides, which corners", Font = new() { Size = 1.6, Bold = true } };
            heading.Style.Margin = new EdgeSize { Bottom = 3 };
            report.Body.Add(heading);
            var intro = new TextItem
            {
                Text = "Each box below was asked for one thing, and says what it is and what to look at. Opened beside "
                     + "the HTML proof, the two must draw the same lines in the same places: every corner here is worked "
                     + "out once, for both."
            };
            intro.Style.Margin = new EdgeSize { Bottom = 6 };
            report.Body.Add(intro);

            var list = new List<(ContainerItem, int)>();
            var ink = new PenStyle { Width = 0.4 };
            var paper = new BrushStyle { Color = new Color(236, 240, 245) };

            void Frame(string caption, Action<Style> ask)
            {
                var frame = new ContainerItem { Layout = LayoutMode.Stack };
                frame.Style.Location.Width = (170, Units.Millimeter);
                frame.Style.Location.Height = (15, Units.Millimeter);
                frame.Style.Margin = new EdgeSize { Bottom = 4 };
                frame.Style.Padding = new EdgeSize { Left = 5, Top = 3, Right = 5, Bottom = 3 };
                ask(frame.Style);
                frame.Add(new TextItem { Text = caption });
                report.Body!.Add(frame);

                Sides drawn = frame.Style.Border == null ? Sides.None : frame.Style.BorderSides & Sides.All;
                list.Add((frame, (frame.Style.Background != null ? 1 : 0) + (drawn != Sides.None ? 1 : 0)));
            }

            Frame("Closed all round and square: the border every frame had. Nothing about it may have moved.",
                s => { s.Border = ink; s.Background = paper; });
            Frame("Translucent fill and border, square. The PDF fades both as the proof does, and the border lies over the fill.",
                s => { s.Border = new PenStyle { Color = new Color(0, 0, 255, 128), Width = 0.8 }; s.Background = new BrushStyle { Color = new Color(200, 0, 0, 51) }; });
            Frame("Three corners rounded at 4 mm, the bottom left one square.",
                s => { s.Border = ink; s.Background = paper; s.CornerRadius = new CornerRadius { TopLeft = 4, TopRight = 4, BottomRight = 4 }; });
            Frame("Border on the left and the top only, round at the corner where they meet. The other two sides stay open.",
                s => { s.Border = ink; s.BorderSides = Sides.Left | Sides.Top; s.CornerRadius = new CornerRadius { TopLeft = 4 }; });
            Frame("Border on the top and the bottom, every corner rounded: each rule stops where its corners begin, the fill does not.",
                s => { s.Border = ink; s.Background = paper; s.BorderSides = Sides.Top | Sides.Bottom; s.CornerRadius = new CornerRadius { TopLeft = 4, TopRight = 4, BottomRight = 4, BottomLeft = 4 }; });
            Frame("A thick rule on the left only, its two corners rounded: it runs between them, straight.",
                s => { s.Border = new PenStyle { Color = new Color(0, 90, 160), Width = 1.2 }; s.Background = paper; s.BorderSides = Sides.Left; s.CornerRadius = new CornerRadius { TopLeft = 3, BottomLeft = 3 }; });
            Frame("Three sides and every corner rounded at 3 mm, the bottom side left open.",
                s => { s.Border = ink; s.Background = paper; s.BorderSides = Sides.Left | Sides.Top | Sides.Right; s.CornerRadius = new CornerRadius { TopLeft = 3, TopRight = 3, BottomRight = 3, BottomLeft = 3 }; });
            Frame("Every radius at 20 mm on a frame 15 mm high: all four are reduced together, and the ends come out as half circles.",
                s => { s.Border = ink; s.Background = paper; s.CornerRadius = new CornerRadius { TopLeft = 20, TopRight = 20, BottomRight = 20, BottomLeft = 20 }; });
            Frame("Rounded at 5 mm and dashed: the dashes follow the curve.",
                s => { s.Border = new PenStyle { Width = 0.4, DashStyle = DashStyle.Dash }; s.CornerRadius = new CornerRadius { TopLeft = 5, TopRight = 5, BottomRight = 5, BottomLeft = 5 }; });
            Frame("Translucent fill and border, rounded at 6 mm.",
                s => { s.Border = new PenStyle { Color = new Color(0, 120, 0, 140), Width = 1 }; s.Background = new BrushStyle { Color = new Color(255, 170, 0, 70) }; s.CornerRadius = new CornerRadius { TopLeft = 6, TopRight = 6, BottomRight = 6, BottomLeft = 6 }; });
            Frame("A pen with no side to draw: the fill alone, rounded at 3 mm.",
                s => { s.Border = ink; s.Background = paper; s.BorderSides = Sides.None; s.CornerRadius = new CornerRadius { TopLeft = 3, TopRight = 3, BottomRight = 3, BottomLeft = 3 }; });

            report.Body.Add(new TextItem { Text = "Below, a rule in green at 30 %: faded in the PDF as in the proof." });
            rule = new LineItem { Pen = new PenStyle { Color = new Color(0, 128, 0, 77), Width = 1 } };
            rule.Style.Margin = new EdgeSize { Top = 2 };
            report.Body.Add(rule);

            report.Reattach();
            frames = list;
            return report;
        }

        /// <summary>
        /// What the engine makes of each frame, for the designer's echo to come to the same: the
        /// frames of the sheet, then every combination of sides against corners square, rounded
        /// at one, rounded at four, too large to fit and negative, with a fill and without.
        /// </summary>
        private static JsonArray FrameCases(List<(ContainerItem Frame, int Marks)> frames)
        {
            static JsonObject Colour(Color c) => new() { ["R"] = c.R, ["G"] = c.G, ["B"] = c.B, ["A"] = c.A };
            static string D(VectorPath path)
            {
                var d = new StringBuilder();
                int at = 0;
                foreach (VectorVerb verb in path.Verbs)
                {
                    d.Append(verb switch { VectorVerb.Move => 'M', VectorVerb.Line => 'L', VectorVerb.Cubic => 'C', _ => 'Z' });
                    int count = verb switch { VectorVerb.Cubic => 6, VectorVerb.Close => 0, _ => 2 };
                    for (int i = 0; i < count; i++)
                        d.Append(i == 0 ? "" : " ").Append(VectorDrawing.N(path.Points[at++]));
                }
                return d.ToString();
            }

            JsonObject Case(string name, Rect f, Style s)
            {
                var style = new JsonObject
                {
                    ["Border"] = s.Border == null ? null : new JsonObject
                    {
                        ["Color"] = Colour(s.Border.Color), ["Width"] = s.Border.Width, ["DashStyle"] = s.Border.DashStyle.ToString()
                    },
                    ["Background"] = s.Background == null ? null : new JsonObject { ["Color"] = Colour(s.Background.Color) },
                    ["BorderSides"] = s.BorderSides.ToString(),
                    ["CornerRadius"] = new JsonObject
                    {
                        ["TopLeft"] = s.CornerRadius.TopLeft, ["TopRight"] = s.CornerRadius.TopRight,
                        ["BottomRight"] = s.CornerRadius.BottomRight, ["BottomLeft"] = s.CornerRadius.BottomLeft
                    }
                };

                var shapes = new JsonArray();
                foreach (VectorShape shape in FrameOutline.Shapes(f, s.Border, s.Background, s.BorderSides, s.CornerRadius))
                {
                    shapes.Add(new JsonObject
                    {
                        ["d"] = D(shape.Path),
                        ["fill"] = shape.Fill is { } fill ? Colour(fill) : null,
                        ["fillAlpha"] = shape.Fill != null ? shape.FillAlpha : null,
                        ["stroke"] = shape.Stroke is { } stroke ? Colour(stroke) : null,
                        ["strokeAlpha"] = shape.Stroke != null ? shape.StrokeAlpha : null,
                        ["strokeWidth"] = shape.Stroke != null ? shape.StrokeWidth : null,
                        ["dash"] = shape.Stroke != null ? new JsonArray(shape.Dash.Select(x => (JsonNode)x).ToArray()) : null
                    });
                }

                return new JsonObject
                {
                    ["name"] = name,
                    ["frame"] = new JsonObject { ["left"] = f.Left, ["top"] = f.Top, ["right"] = f.Right, ["bottom"] = f.Bottom },
                    ["style"] = style,
                    ["plain"] = FrameOutline.IsPlain(f, s.Border, s.BorderSides, s.CornerRadius),
                    ["shapes"] = shapes
                };
            }

            var cases = new JsonArray();
            for (int i = 0; i < frames.Count; i++)
            {
                ReadOnlyFrameDimension r = frames[i].Frame.Style.RenderedFrame;
                cases.Add(Case($"frame {i + 1} of the sheet", new Rect(r.Left, r.Top, r.Right, r.Bottom), frames[i].Frame.Style));
            }

            var corners = new (string Name, CornerRadius Radii)[]
            {
                ("square", new CornerRadius()),
                ("one corner rounded", new CornerRadius { TopRight = 4 }),
                ("four corners rounded", new CornerRadius { TopLeft = 3, TopRight = 5, BottomRight = 2, BottomLeft = 6 }),
                ("radii too large", new CornerRadius { TopLeft = 30, TopRight = 30, BottomRight = 30, BottomLeft = 12 }),
                ("a negative radius", new CornerRadius { TopLeft = -2, BottomRight = 4 })
            };
            foreach (Sides sides in Enumerable.Range(0, 16).Select(m => (Sides)m))
                foreach ((string name, CornerRadius radii) in corners)
                    foreach (bool filled in new[] { false, true })
                    {
                        var style = new Style
                        {
                            Border = new PenStyle { Color = new Color(20, 40, 60, (byte)(filled ? 200 : 255)), Width = 0.5, DashStyle = (DashStyle)((int)sides % 4) },
                            Background = filled ? new BrushStyle { Color = new Color(250, 240, 200, 120) } : null,
                            BorderSides = sides,
                            CornerRadius = radii
                        };
                        cases.Add(Case($"{sides}, {name}{(filled ? ", filled" : "")}", new Rect(10, 20, 60, 40), style));
                    }

            var tiny = new Style { Border = new PenStyle(), CornerRadius = new CornerRadius { TopLeft = 3, BottomRight = 3 } };
            cases.Add(Case("a frame of no height, rounded", new Rect(10, 20, 60, 20), tiny));
            cases.Add(Case("a frame of no width, rounded", new Rect(10, 20, 10, 40), tiny));
            return cases;
        }

        /// <summary>A backend of somebody else's, which knows rectangles and lines and nothing about frames.</summary>
        private sealed class RectanglesAndLines : IReportBackend
        {
            public readonly List<string> Calls = new();
            public ITextMeasurer Measurer => FixedPitchTextMeasurer.Default;
            public void BeginDocument(Report report) { }
            public void BeginPage(Page page) { }
            public void DrawText(Rect frame, string text, TextStyle style) { }
            public void DrawLine(double x1, double y1, double x2, double y2, PenStyle pen)
                => Calls.Add(string.Create(CultureInfo.InvariantCulture, $"line {x1} {y1} {x2} {y2}"));
            public void DrawRectangle(Rect frame, PenStyle? pen, BrushStyle? fill)
                => Calls.Add("rect" + (pen != null ? " pen" : "") + (fill != null ? " fill" : ""));
            public void DrawImage(Rect frame, ImageSource image) { }
            public void DrawImage(Rect frame, ImageSource image, double opacity) { }
            public void DrawField(Rect frame, FieldItem field, TextStyle font) { }
            public void EndPage() { }
            public void EndDocument() { }
            public void Dispose() { }
        }
    }
}
