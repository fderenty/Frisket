using System.Text;

namespace Frisket
{
    /// <summary>
    /// What a picture's bytes came to, once. Kept on the <see cref="ImageSource"/> because the
    /// same drawing is asked about by the layout, for its proportions, and by a backend, for
    /// its shapes - and a table of a hundred rows shares one source.
    /// </summary>
    internal sealed record SvgReading(byte[] Data, VectorDrawing? Drawing, IReadOnlyList<TemplateIssue> Issues)
    {
        /// <summary>The drawing, or the refusal in the engine's words.</summary>
        public VectorDrawing Require() => Issues.Count == 0 && Drawing != null
            ? Drawing
            : throw new NotSupportedException("This drawing cannot be drawn. " + Pictures.Say(Issues));
    }

    /// <summary>
    /// Makes a <see cref="VectorDrawing"/> of an SVG, or says why not.
    ///
    /// A limited SVG rather than the whole of it, and limited in the open: everything that
    /// changes the picture is either drawn as a browser would draw it or refused by name.
    /// Skipping what it does not know would be a chart with a piece missing and nobody told,
    /// the same reasoning as a font a library does not hold. What has no effect on the picture
    /// - an id, a class with no sheet to apply it, an accessibility hint - is let through, and
    /// that list is closed too: what is on neither list is refused, so the day SVG grows a word
    /// it is refused until somebody has decided what it means here.
    ///
    /// The lists are the contract with whoever generates the file, and they are written out
    /// in https://github.com/fderenty/Frisket/tree/main/examples/charts; test 101 holds that page to them.
    /// </summary>
    internal sealed class SvgReader
    {
        internal const string Invalid = "svg.invalid";
        internal const string Unsupported = "svg.unsupported";

        /// <summary>A file wrong everywhere gets the first of it, which is plenty to act on.</summary>
        private const int MostIssues = 20;

        internal static readonly string[] Drawn =
            { "svg", "g", "defs", "clipPath", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "tspan" };

        internal static readonly string[] NotDrawn = { "title", "desc", "metadata" };

        /// <summary>Attributes that may equally be written in a style attribute.</summary>
        internal static readonly string[] Properties =
        {
            "color", "fill", "fill-opacity", "fill-rule", "stroke", "stroke-opacity", "stroke-width",
            "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
            "opacity", "clip-path", "clip-rule", "font-family", "font-size", "font-weight", "font-style",
            "text-anchor", "display", "visibility"
        };

        /// <summary>What has no effect on the picture. "aria-", "data-" and any foreign prefix also pass.</summary>
        internal static readonly string[] Ignored =
        {
            "id", "class", "lang", "tabindex", "role", "focusable", "xmlns", "shape-rendering",
            "text-rendering", "color-rendering", "image-rendering", "color-interpolation",
            "pointer-events", "cursor", "font-kerning", "font-variant-ligatures"
        };

        private static readonly Dictionary<string, string[]> OwnAttributes = new()
        {
            ["svg"] = new[] { "viewBox", "width", "height", "x", "y", "preserveAspectRatio", "version", "baseProfile" },
            ["g"] = Array.Empty<string>(),
            ["path"] = new[] { "d" },
            ["rect"] = new[] { "x", "y", "width", "height", "rx", "ry" },
            ["circle"] = new[] { "cx", "cy", "r" },
            ["ellipse"] = new[] { "cx", "cy", "rx", "ry" },
            ["line"] = new[] { "x1", "y1", "x2", "y2" },
            ["polyline"] = new[] { "points" },
            ["polygon"] = new[] { "points" },
            ["text"] = new[] { "x", "y", "dx", "dy" },
            ["tspan"] = new[] { "x", "y", "dx", "dy" },
            ["clipPath"] = new[] { "clipPathUnits" },
            ["defs"] = Array.Empty<string>(),
        };

        private static readonly HashSet<string> GenericFamilies = new(StringComparer.OrdinalIgnoreCase)
        {
            "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui",
            "ui-sans-serif", "ui-serif", "ui-monospace", "ui-rounded"
        };

        private readonly List<TemplateIssue> _issues = new();
        private readonly Dictionary<string, VectorClip> _clips = new();

        public static SvgReading Read(byte[] data)
        {
            var reader = new SvgReader();
            VectorDrawing? drawing = null;

            try
            {
                string text = new UTF8Encoding(false, true).GetString(data);
                drawing = reader.Drawing(SvgMarkup.Parse(text));
            }
            catch (SvgMalformedException e)
            {
                return new SvgReading(data, null, new[] { Issue(Invalid, e.Message, "svg") });
            }
            catch (DecoderFallbackException)
            {
                return new SvgReading(data, null, new[] { Issue(Invalid, "This file is not UTF-8.", "svg") });
            }

            return new SvgReading(data, reader._issues.Count == 0 ? drawing : null, reader._issues);
        }

        private static TemplateIssue Issue(string rule, string message, string where)
            => new(rule, IssueLevel.Error, message, null, where);

        private void Refuse(string where, string message)
        {
            if (_issues.Count < MostIssues)
                _issues.Add(Issue(Unsupported, message, where));
        }

        // ---- the root -----------------------------------------------------------------------

        private VectorDrawing? Drawing(SvgElement root)
        {
            if (root.Name != "svg")
                throw new SvgMalformedException($"The root element is <{root.Name}>, not <svg>.");

            CollectClips(root, "svg");

            (double x, double y, double width, double height)? box = ViewBox(root);
            var paint = new Paint();
            Declared declared = Declare(root, paint, "svg");

            var group = new VectorGroup();
            Frame(group, root, declared, "svg");
            Children(root, paint, group.Children, "svg");

            if (box == null)
                return null;

            var drawing = new VectorDrawing { X = box.Value.x, Y = box.Value.y, Width = box.Value.width, Height = box.Value.height };
            drawing.Nodes.Add(group);
            return drawing;
        }

        /// <summary>
        /// The drawing's own coordinates, and with them its proportions. Without a viewBox a
        /// plain width and height say the same; a percentage says nothing about a picture that
        /// has no page of its own, and it is refused.
        /// </summary>
        private (double, double, double, double)? ViewBox(SvgElement root)
        {
            string? stated = root.Attribute("viewBox");
            if (stated != null)
            {
                List<double>? box = SvgValues.Numbers(stated);
                if (box is { Count: 4 } && box[2] > 0 && box[3] > 0)
                    return (box[0], box[1], box[2], box[3]);

                Refuse("svg", $"The viewBox \"{stated}\" is not four numbers with a width and a height above zero.");
                return null;
            }

            double? width = SvgValues.Length(root.Attribute("width"));
            double? height = SvgValues.Length(root.Attribute("height"));
            if (width > 0 && height > 0)
                return (0, 0, width.Value, height.Value);

            Refuse("svg", "This drawing has no viewBox and no width and height in plain lengths, so nothing says how big it is.");
            return null;
        }

        // ---- clips --------------------------------------------------------------------------

        /// <summary>
        /// Every clipPath in the file, before anything is drawn: a reference may come before
        /// what it names, and generators put their defs at either end.
        /// </summary>
        private void CollectClips(SvgElement element, string where)
        {
            foreach (object child in element.Children)
            {
                if (child is not SvgElement inner)
                    continue;

                string here = where + " › " + inner.Name;
                if (inner.Name == "clipPath")
                    CollectClip(inner, here);
                else if (inner.Name != "metadata")
                    CollectClips(inner, here);
            }
        }

        /// <summary>
        /// One shape to a clip, which is what a plot area is. Several would be their union in
        /// SVG and the wrong answer here wherever their windings cancel, so they are refused
        /// rather than drawn differently in the two outputs.
        /// </summary>
        private void CollectClip(SvgElement clip, string where)
        {
            string id = clip.Attribute("id") ?? string.Empty;

            if (clip.Attribute("clipPathUnits") is { } units && units != "userSpaceOnUse")
                Refuse(where, $"clipPathUnits=\"{units}\" is not taken: only userSpaceOnUse, the default.");

            var paint = new Paint();
            Declared declared = Declare(clip, paint, where);
            if (declared.Has("clip-path"))
                Refuse(where, "A clip on a clipPath is not taken.");

            VectorMatrix own = Matrix(clip, where);

            var shapes = new List<SvgElement>();
            foreach (object child in clip.Children)
            {
                if (child is not SvgElement inner)
                {
                    if (!string.IsNullOrWhiteSpace((string)child))
                        Refuse(where, "A clipPath holds shapes, and this one holds text.");
                }
                else if (!Array.Exists(NotDrawn, n => n == inner.Name))
                {
                    shapes.Add(inner);
                }
            }

            if (shapes.Count != 1)
            {
                Refuse(where, $"A clipPath must hold exactly one shape, and this one holds {shapes.Count}.");
                return;
            }

            SvgElement shape = shapes[0];
            string inside = where + " › " + shape.Name;

            if (!IsShape(shape.Name))
            {
                Refuse(inside, $"<{shape.Name}> in a clipPath is not taken: it holds one shape.");
                return;
            }

            Paint shapePaint = paint.Clone();
            Declared shapeDeclared = Declare(shape, shapePaint, inside);
            if (shapeDeclared.Has("clip-path"))
                Refuse(inside, "A clip on a clipPath's shape is not taken.");

            VectorPath? path = Geometry(shape, inside);
            if (path == null)
                return;

            VectorMatrix whole = own.Times(Matrix(shape, inside));
            if (id.Length > 0 && !_clips.ContainsKey(id))
                _clips[id] = new VectorClip { Path = path.Transformed(whole), EvenOdd = shapePaint.ClipEvenOdd };
        }

        // ---- elements -----------------------------------------------------------------------

        private static bool IsShape(string name)
            => name is "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon";

        private void Children(SvgElement parent, Paint paint, List<VectorNode> into, string where)
        {
            foreach (object child in parent.Children)
            {
                if (child is SvgElement element)
                    Element(element, paint, into, where + " › " + element.Name);
                else if (!string.IsNullOrWhiteSpace((string)child))
                    Refuse(where, $"<{parent.Name}> holds text, which only a <text> draws.");
            }
        }

        private void Element(SvgElement element, Paint inherited, List<VectorNode> into, string where)
        {
            string name = element.Name;

            if (Array.Exists(NotDrawn, n => n == name) || name == "clipPath")
                return;                                       // described, or taken already

            if (name == "defs")
            {
                Declare(element, inherited.Clone(), where);
                foreach (object child in element.Children)
                {
                    if (child is SvgElement inner && inner.Name != "clipPath" && !Array.Exists(NotDrawn, n => n == inner.Name))
                        Refuse(where + " › " + inner.Name, Named(inner.Name) + " Only a clipPath is taken in defs.");
                }
                return;
            }

            if (name == "g")
            {
                Paint paint = inherited.Clone();
                Declared declared = Declare(element, paint, where);
                var group = new VectorGroup();
                Frame(group, element, declared, where);
                Children(element, paint, group.Children, where);

                if (!declared.IsNone("display"))
                    into.Add(group);
                return;
            }

            if (IsShape(name))
            {
                Shape(element, inherited, into, where);
                return;
            }

            if (name == "text")
            {
                Text(element, inherited, into, where);
                return;
            }

            Refuse(where, Named(name));
        }

        /// <summary>The refusal of an element, with what to do instead for the ones met most.</summary>
        private static string Named(string name) => name switch
        {
            "svg" => "An <svg> inside an <svg> is not taken: use a <g> with a transform.",
            "style" => "A <style> sheet is not taken: write the properties on the elements, as attributes or in a style attribute.",
            "script" => "A <script> has no place in a printed document.",
            "image" => "An <image> is not taken: a drawing draws, and a picture beside it is an ImageItem of its own.",
            "use" => "<use> is not taken: write the shape out.",
            "foreignObject" => "<foreignObject> is not taken: there is no browser here to draw it.",
            "linearGradient" or "radialGradient" or "pattern" => $"<{name}> is not taken: a fill is a colour.",
            "filter" or "mask" or "marker" or "symbol" or "switch" or "a" or "textPath"
                => $"<{name}> is not taken.",
            _ => $"<{name}> is not something this reader draws, and leaving it out would change the picture."
        };

        private void Shape(SvgElement element, Paint inherited, List<VectorNode> into, string where)
        {
            Paint paint = inherited.Clone();
            Declared declared = Declare(element, paint, where);
            var holder = new VectorGroup();
            Frame(holder, element, declared, where);

            VectorPath? path = Geometry(element, where);
            if (path == null || path.IsEmpty || declared.IsNone("display") || !paint.Visible)
                return;

            // A line has no inside: SVG fills it with nothing rather than refusing the fill.
            Color? fill = element.Name == "line" ? null : paint.FillColour();
            Color? stroke = paint.StrokeWidth > 0 ? paint.StrokeColour() : null;
            double fillAlpha = (fill?.A ?? 255) / 255.0 * paint.FillOpacity * paint.Opacity;
            double strokeAlpha = (stroke?.A ?? 255) / 255.0 * paint.StrokeOpacity * paint.Opacity;

            if (fillAlpha <= 0) fill = null;
            if (strokeAlpha <= 0) stroke = null;
            if (fill == null && stroke == null)
                return;

            into.Add(new VectorShape
            {
                Transform = holder.Transform,
                Clip = holder.Clip,
                Path = path,
                Fill = fill,
                FillAlpha = fillAlpha,
                EvenOdd = paint.FillEvenOdd,
                Stroke = stroke,
                StrokeAlpha = strokeAlpha,
                StrokeWidth = paint.StrokeWidth,
                Dash = paint.Dash,
                DashOffset = paint.DashOffset,
                Cap = paint.Cap,
                Join = paint.Join,
                MiterLimit = paint.Miter
            });
        }

        // ---- geometry -----------------------------------------------------------------------

        /// <summary>A shape as a path. Null when an attribute could not be read, which has been said.</summary>
        private VectorPath? Geometry(SvgElement element, string where)
        {
            double? Length(string name, double absent = 0)
            {
                string? value = element.Attribute(name);
                if (value == null)
                    return absent;

                double? read = SvgValues.Length(value);
                if (read == null)
                    Refuse(where, $"{name}=\"{value}\" is not a length this reader takes: a number, or one in px, pt, pc, mm, cm or in.");
                return read;
            }

            double? Radius(string name)
            {
                double? read = Length(name);
                if (read < 0)
                {
                    Refuse(where, $"{name} is negative.");
                    return null;
                }
                return read;
            }

            var path = new VectorPath();

            switch (element.Name)
            {
                case "path":
                {
                    string d = element.Attribute("d") ?? string.Empty;
                    VectorPath? read = SvgValues.Path(d);
                    if (read == null)
                        Refuse(where, $"The path data \"{Shorten(d)}\" is not well formed.");
                    return read;
                }

                case "rect":
                {
                    double? x = Length("x"), y = Length("y"), w = Radius("width"), h = Radius("height");
                    double? rx = Radius("rx"), ry = Radius("ry");
                    if (x == null || y == null || w == null || h == null || rx == null || ry == null)
                        return null;

                    if (w > 0 && h > 0)
                    {
                        // One radius stands for both, and neither passes half the side.
                        double across = element.Attribute("rx") == null ? ry.Value : rx.Value;
                        double down = element.Attribute("ry") == null ? rx.Value : ry.Value;
                        Rectangle(path, x.Value, y.Value, w.Value, h.Value,
                                  Math.Min(across, w.Value / 2), Math.Min(down, h.Value / 2));
                    }
                    return path;
                }

                case "circle":
                {
                    double? cx = Length("cx"), cy = Length("cy"), r = Radius("r");
                    if (cx == null || cy == null || r == null)
                        return null;

                    if (r > 0)
                        Ellipse(path, cx.Value, cy.Value, r.Value, r.Value);
                    return path;
                }

                case "ellipse":
                {
                    double? cx = Length("cx"), cy = Length("cy"), rx = Radius("rx"), ry = Radius("ry");
                    if (cx == null || cy == null || rx == null || ry == null)
                        return null;

                    if (rx > 0 && ry > 0)
                        Ellipse(path, cx.Value, cy.Value, rx.Value, ry.Value);
                    return path;
                }

                case "line":
                {
                    double? x1 = Length("x1"), y1 = Length("y1"), x2 = Length("x2"), y2 = Length("y2");
                    if (x1 == null || y1 == null || x2 == null || y2 == null)
                        return null;

                    path.MoveTo(x1.Value, y1.Value);
                    path.LineTo(x2.Value, y2.Value);
                    return path;
                }

                default:                                      // polyline, polygon
                {
                    string points = element.Attribute("points") ?? string.Empty;
                    List<double>? read = SvgValues.Numbers(points);
                    if (read == null || read.Count % 2 != 0)
                    {
                        Refuse(where, $"The points \"{Shorten(points)}\" are not pairs of numbers.");
                        return null;
                    }

                    for (int i = 0; i + 1 < read.Count; i += 2)
                    {
                        if (i == 0)
                            path.MoveTo(read[i], read[i + 1]);
                        else
                            path.LineTo(read[i], read[i + 1]);
                    }

                    if (element.Name == "polygon" && !path.IsEmpty)
                        path.Close();
                    return path;
                }
            }
        }

        private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "...";

        /// <summary>The fraction of a radius that puts a cubic on a quarter circle.</summary>
        private const double Kappa = 0.5522847498307936;

        private static void Rectangle(VectorPath path, double x, double y, double w, double h, double rx, double ry)
        {
            if (rx <= 0 || ry <= 0)
            {
                path.MoveTo(x, y);
                path.LineTo(x + w, y);
                path.LineTo(x + w, y + h);
                path.LineTo(x, y + h);
                path.Close();
                return;
            }

            double kx = rx * Kappa, ky = ry * Kappa;
            double right = x + w, bottom = y + h;

            path.MoveTo(x + rx, y);
            path.LineTo(right - rx, y);
            path.CubicTo(right - rx + kx, y, right, y + ry - ky, right, y + ry);
            path.LineTo(right, bottom - ry);
            path.CubicTo(right, bottom - ry + ky, right - rx + kx, bottom, right - rx, bottom);
            path.LineTo(x + rx, bottom);
            path.CubicTo(x + rx - kx, bottom, x, bottom - ry + ky, x, bottom - ry);
            path.LineTo(x, y + ry);
            path.CubicTo(x, y + ry - ky, x + rx - kx, y, x + rx, y);
            path.Close();
        }

        private static void Ellipse(VectorPath path, double cx, double cy, double rx, double ry)
        {
            double kx = rx * Kappa, ky = ry * Kappa;

            path.MoveTo(cx + rx, cy);
            path.CubicTo(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry);
            path.CubicTo(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy);
            path.CubicTo(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry);
            path.CubicTo(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy);
            path.Close();
        }

        // ---- text ---------------------------------------------------------------------------

        private void Text(SvgElement element, Paint inherited, List<VectorNode> into, string where)
        {
            Paint paint = inherited.Clone();
            Declared declared = Declare(element, paint, where);

            var text = new VectorText();
            Frame(text, element, declared, where);

            var collapse = new Collapser();
            Spans(element, paint, text, collapse, where);
            collapse.Finish(text);

            if (text.Runs.Count > 0 && !declared.IsNone("display"))
                into.Add(text);
        }

        /// <summary>
        /// Walks a text and its tspans into pieces. White space goes as SVG says by default -
        /// runs of it become one space, and the ends of the whole text are trimmed - across
        /// the pieces, since "A &lt;tspan&gt;B&lt;/tspan&gt;" keeps its one space.
        /// </summary>
        private void Spans(SvgElement span, Paint paint, VectorText text, Collapser collapse, string where)
        {
            collapse.Arrive(Position(span, paint, where));

            foreach (object child in span.Children)
            {
                if (child is string words)
                {
                    collapse.Add(words, paint, text, this, where);
                }
                else if (child is SvgElement inner)
                {
                    string here = where + " › " + inner.Name;
                    if (inner.Name == "tspan")
                    {
                        Paint own = paint.Clone();
                        Declared declared = Declare(inner, own, here);
                        if (declared.Has("clip-path") || inner.Attribute("transform") != null)
                            Refuse(here, "A clip or a transform on a tspan is not taken: put it on the text.");
                        Spans(inner, own, text, collapse, here);
                    }
                    else if (!Array.Exists(NotDrawn, n => n == inner.Name))
                    {
                        Refuse(here, Named(inner.Name));
                    }
                }
            }
        }

        private Offsets Position(SvgElement span, Paint paint, string where)
        {
            double? Absolute(string name)
            {
                string? value = span.Attribute(name);
                if (value == null)
                    return null;

                double? read = SvgValues.Length(value);
                if (read == null)
                    Refuse(where, $"{name}=\"{value}\" is not taken: one position, as a plain length. A list places each letter, which is not drawn.");
                return read;
            }

            double Relative(string name)
            {
                string? value = span.Attribute(name);
                if (value == null)
                    return 0;

                double? read = SvgValues.LengthOrEm(value, paint.FontSize);
                if (read == null)
                    Refuse(where, $"{name}=\"{value}\" is not taken: one shift, as a plain length or in em.");
                return read ?? 0;
            }

            return new Offsets(Absolute("x"), Absolute("y"), Relative("dx"), Relative("dy"));
        }

        private readonly record struct Offsets(double? X, double? Y, double Dx, double Dy);

        /// <summary>Holds the white space between pieces, and where the next piece starts.</summary>
        private sealed class Collapser
        {
            private Offsets? _pending;
            private bool _preserve;
            private StringBuilder? _words;
            private Paint? _paint;
            private Offsets _offsets;
            private bool _seen;
            private bool _lastSpace;
            private string _where = string.Empty;
            private SvgReader? _reader;

            /// <summary>
            /// A span's position, for the next words to come. It joins whatever has not been
            /// used yet rather than replacing it: in &lt;text x="10"&gt;&lt;tspan&gt;Label the
            /// text's x belongs to the tspan's first letter, and that is how most generators
            /// write a label.
            /// </summary>
            public void Arrive(Offsets offsets)
            {
                Offsets held = _pending ?? default;
                _pending = new Offsets(offsets.X ?? held.X, offsets.Y ?? held.Y,
                                       held.Dx + offsets.Dx, held.Dy + offsets.Dy);
            }

            public void Add(string words, Paint paint, VectorText text, SvgReader reader, string where)
            {
                // A new look or a new position starts a new piece.
                if (_words != null && ((_pending != null && _pending.Value != default) || !ReferenceEquals(paint, _paint)))
                    Flush(text);

                _preserve = paint.Preserve;

                foreach (char raw in words)
                {
                    char c = raw is '\n' or '\r' or '\t' ? ' ' : raw;

                    if (c == ' ' && !_preserve)
                    {
                        if (_lastSpace || !_seen)
                            continue;
                        _lastSpace = true;
                    }
                    else
                    {
                        _lastSpace = false;
                    }

                    if (_words == null)
                    {
                        _words = new StringBuilder();
                        _paint = paint;
                        _offsets = _pending ?? default;
                        _pending = null;
                        _where = where;
                        _reader = reader;
                    }

                    _words.Append(c);
                    _seen = true;
                }
            }

            public void Finish(VectorText text)
            {
                if (_words != null && _lastSpace && !_preserve && _words.Length > 0 && _words[^1] == ' ')
                    _words.Length--;

                Flush(text);
            }

            private void Flush(VectorText text)
            {
                if (_words == null || _paint == null || _reader == null)
                    return;

                string words = _words.ToString();
                _words = null;
                if (words.Length > 0)
                    _reader.AddRun(text, words, _offsets, _paint, _where);
            }
        }

        private void AddRun(VectorText text, string words, Offsets offsets, Paint paint, string where)
        {
            if (paint.StrokeColour() != null && paint.StrokeWidth > 0)
                Refuse(where, "A stroked text is not taken: a text is filled. Set stroke=\"none\" on it, or on the group it inherits from.");

            Color? fill = paint.FillColour();
            double alpha = (fill?.A ?? 255) / 255.0 * paint.FillOpacity * paint.Opacity;

            if (fill == null || alpha <= 0 || paint.FontSize <= 0 || !paint.Visible)
                return;

            text.Runs.Add(new VectorRun
            {
                Text = words,
                X = offsets.X,
                Y = offsets.Y,
                Dx = offsets.Dx,
                Dy = offsets.Dy,
                Anchor = paint.Anchor,
                Alpha = alpha,
                Style = new TextStyle
                {
                    Family = paint.Family,
                    // In points as a measurer wants it, chosen so that the em comes back in
                    // the drawing's own units: font-size is an em there, as SVG says, and not
                    // the cap height a size means everywhere else in this engine. The same
                    // file then reads the same on the host's screen as on the page.
                    Size = paint.FontSize / TextStyle.PointsToMillimeters,
                    SizeUnit = FontSizeUnit.Point,
                    Bold = paint.Bold,
                    Italic = paint.Italic,
                    Color = new Color(fill.Value.R, fill.Value.G, fill.Value.B),
                    Wrap = false
                }
            });
        }

        // ---- what surrounds an element ------------------------------------------------------

        private VectorMatrix Matrix(SvgElement element, string where)
        {
            string? value = element.Attribute("transform");
            if (value == null)
                return VectorMatrix.Identity;

            VectorMatrix? read = SvgValues.Transform(value);
            if (read == null)
                Refuse(where, $"The transform \"{Shorten(value)}\" is not one this reader takes: matrix, translate, scale, rotate, skewX or skewY, in plain numbers.");
            return read ?? VectorMatrix.Identity;
        }

        private void Frame(VectorNode node, SvgElement element, Declared declared, string where)
        {
            node.Transform = Matrix(element, where);

            string? clip = declared.Get("clip-path");
            if (clip == null || clip == "none" || clip == "inherit")
                return;

            string inner = clip.Trim();
            if (inner.StartsWith("url(", StringComparison.Ordinal) && inner.EndsWith(')'))
            {
                string reference = inner[4..^1].Trim().Trim('"', '\'');
                if (reference.StartsWith('#'))
                {
                    if (_clips.TryGetValue(reference[1..], out VectorClip? found))
                        node.Clip = found;
                    else
                        Refuse(where, $"clip-path names \"{reference}\", and no usable clipPath has that id.");
                    return;
                }
            }

            Refuse(where, $"clip-path=\"{clip}\" is not taken: only url(#id), naming a clipPath in this file.");
        }

        /// <summary>The properties an element states, from its attributes and its style attribute.</summary>
        private sealed class Declared
        {
            private readonly Dictionary<string, string> _values = new();

            public void Set(string name, string value) => _values[name] = value;
            public string? Get(string name) => _values.TryGetValue(name, out string? value) ? value.Trim() : null;
            public bool Has(string name) => Get(name) is { } value && value != "none";
            public bool IsNone(string name) => Get(name) == "none";
        }

        /// <summary>
        /// Everything an element says, sorted: its own geometry, the properties that change
        /// what it paints, what does not matter, and the refused rest. The style attribute wins
        /// over the attributes, as it does in a browser.
        /// </summary>
        private Declared Declare(SvgElement element, Paint paint, string where)
        {
            var declared = new Declared();
            OwnAttributes.TryGetValue(element.Name, out string[]? own);

            foreach ((string name, string value) in element.Attributes)
            {
                if (Array.Exists(Properties, p => p == name))
                    declared.Set(name, value);
                else if (name == "xml:space")
                    paint.Preserve = value == "preserve";
                else if (name is "transform" or "style")
                    continue;
                else if (own != null && Array.Exists(own, a => a == name))
                    continue;
                else if (!IsIgnored(name))
                    Refuse(where, $"The attribute {name} is not taken, and leaving it out could change the picture.");
            }

            if (element.Attribute("style") is { } style)
            {
                foreach (string declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = declaration.IndexOf(':');
                    if (colon <= 0)
                    {
                        if (!string.IsNullOrWhiteSpace(declaration))
                            Refuse(where, $"\"{Shorten(declaration.Trim())}\" in a style attribute is not a property and a value.");
                        continue;
                    }

                    string name = declaration[..colon].Trim();
                    string value = declaration[(colon + 1)..].Replace("!important", "", StringComparison.Ordinal).Trim();

                    if (Array.Exists(Properties, p => p == name))
                        declared.Set(name, value);
                    else if (!IsIgnored(name))
                        Refuse(where, $"The style property {name} is not taken, and leaving it out could change the picture.");
                }
            }

            Apply(declared, paint, where);
            return declared;
        }

        private static bool IsIgnored(string name)
        {
            if (Array.Exists(Ignored, i => i == name)
                || name.StartsWith("aria-", StringComparison.Ordinal)
                || name.StartsWith("data-", StringComparison.Ordinal))
                return true;

            // A namespace of its own does nothing in SVG, by SVG's own rule; this is what an
            // editor's sodipodi:nodetypes is. xml: is the one prefix that is SVG's business, and
            // xlink: only ever points at something fetched, which nothing here draws.
            int colon = name.IndexOf(':');
            if (colon <= 0)
                return false;

            string prefix = name[..colon];
            return prefix == "xmlns" || name == "xml:lang" || (prefix is not ("xml" or "xlink"));
        }

        private void Apply(Declared declared, Paint paint, string where)
        {
            void Bad(string name, string value, string takes)
                => Refuse(where, $"{name}=\"{Shorten(value)}\" is not taken: {takes}.");

            // color first: a fill of currentColor on this very element reads it.
            if (declared.Get("color") is { } color && color != "inherit")
            {
                if (SvgValues.Colour(color) is { } read)
                    paint.Current = read;
                else if (!string.Equals(color, "currentcolor", StringComparison.OrdinalIgnoreCase))
                    Bad("color", color, "a colour");
            }

            if (declared.Get("fill") is { } fill && fill != "inherit")
                paint.SetFill(fill, Bad);

            if (declared.Get("stroke") is { } stroke && stroke != "inherit")
                paint.SetStroke(stroke, Bad);

            Fraction(declared, "fill-opacity", v => paint.FillOpacity = v, Bad);
            Fraction(declared, "stroke-opacity", v => paint.StrokeOpacity = v, Bad);
            Fraction(declared, "opacity", v => paint.Opacity *= v, Bad);

            if (declared.Get("fill-rule") is { } rule && rule != "inherit")
            {
                if (rule is "nonzero" or "evenodd")
                    paint.FillEvenOdd = rule == "evenodd";
                else
                    Bad("fill-rule", rule, "nonzero or evenodd");
            }

            if (declared.Get("clip-rule") is { } clipRule && clipRule != "inherit")
            {
                if (clipRule is "nonzero" or "evenodd")
                    paint.ClipEvenOdd = clipRule == "evenodd";
                else
                    Bad("clip-rule", clipRule, "nonzero or evenodd");
            }

            if (declared.Get("stroke-width") is { } width && width != "inherit")
            {
                double? read = SvgValues.Length(width);
                if (read >= 0)
                    paint.StrokeWidth = read.Value;
                else
                    Bad("stroke-width", width, "a length of zero or more");
            }

            if (declared.Get("stroke-dasharray") is { } dash && dash != "inherit")
            {
                if (dash == "none")
                {
                    paint.Dash = Array.Empty<double>();
                }
                else
                {
                    List<double>? read = SvgValues.Numbers(dash);
                    if (read == null || read.Count == 0 || read.Exists(n => n < 0))
                        Bad("stroke-dasharray", dash, "none, or plain lengths of zero or more");
                    else
                        paint.Dash = read.Sum() > 0 ? read.ToArray() : Array.Empty<double>();
                }
            }

            if (declared.Get("stroke-dashoffset") is { } offset && offset != "inherit")
            {
                double? read = SvgValues.Length(offset);
                if (read != null)
                    paint.DashOffset = read.Value;
                else
                    Bad("stroke-dashoffset", offset, "a plain length");
            }

            if (declared.Get("stroke-linecap") is { } cap && cap != "inherit")
            {
                int found = Array.IndexOf(new[] { "butt", "round", "square" }, cap);
                if (found >= 0)
                    paint.Cap = found;
                else
                    Bad("stroke-linecap", cap, "butt, round or square");
            }

            if (declared.Get("stroke-linejoin") is { } join && join != "inherit")
            {
                int found = Array.IndexOf(new[] { "miter", "round", "bevel" }, join);
                if (found >= 0)
                    paint.Join = found;
                else
                    Bad("stroke-linejoin", join, "miter, round or bevel");
            }

            if (declared.Get("stroke-miterlimit") is { } miter && miter != "inherit")
            {
                double? read = SvgValues.Number(miter);
                if (read >= 1)
                    paint.Miter = read.Value;
                else
                    Bad("stroke-miterlimit", miter, "a number of one or more");
            }

            if (declared.Get("font-family") is { } family && family != "inherit")
                paint.Family = FamilyOf(family);

            if (declared.Get("font-size") is { } size && size != "inherit")
            {
                double? read = SvgValues.Length(size);
                if (read >= 0)
                    paint.FontSize = read.Value;
                else
                    Bad("font-size", size, "a plain length, in px, pt, pc, mm, cm or in");
            }

            if (declared.Get("font-weight") is { } weight && weight != "inherit")
            {
                if (weight == "bold" || (SvgValues.Number(weight) is { } heavy && heavy >= 600 && heavy <= 1000))
                    paint.Bold = true;
                else if (weight == "normal" || SvgValues.Number(weight) is >= 1 and < 600)
                    paint.Bold = false;
                else
                    Bad("font-weight", weight, "normal, bold or a number");
            }

            if (declared.Get("font-style") is { } slant && slant != "inherit")
            {
                if (slant is "italic" or "oblique")
                    paint.Italic = true;
                else if (slant == "normal")
                    paint.Italic = false;
                else
                    Bad("font-style", slant, "normal, italic or oblique");
            }

            if (declared.Get("text-anchor") is { } anchor && anchor != "inherit")
            {
                int found = Array.IndexOf(new[] { "start", "middle", "end" }, anchor);
                if (found >= 0)
                    paint.Anchor = (VectorAnchor)found;
                else
                    Bad("text-anchor", anchor, "start, middle or end");
            }

            if (declared.Get("visibility") is { } visibility && visibility != "inherit")
            {
                if (visibility is "visible" or "hidden" or "collapse")
                    paint.Visible = visibility == "visible";
                else
                    Bad("visibility", visibility, "visible or hidden");
            }

            if (declared.Get("display") is { } display && display is not ("inline" or "block" or "none" or "inherit"))
                Bad("display", display, "inline or none");
        }

        private static void Fraction(Declared declared, string name, Action<double> set, Action<string, string, string> bad)
        {
            if (declared.Get(name) is not { } value || value == "inherit")
                return;

            if (SvgValues.Opacity(value) is { } read)
                set(read);
            else
                bad(name, value, "a number or a percentage");
        }

        /// <summary>
        /// The first name of a font-family list, or null for the document's own family when that
        /// name is a generic one. The first and not the first found: a fallback list is a browser
        /// looking for something near enough, and this engine does not take near enough - a
        /// family the library does not hold is refused, by name, as anywhere else in a template.
        /// </summary>
        private static string? FamilyOf(string value)
        {
            string first = value.Split(',')[0].Trim().Trim('"', '\'').Trim();
            return first.Length == 0 || GenericFamilies.Contains(first) ? null : first;
        }

        /// <summary>What is inherited down the tree, starting from SVG's own initial values.</summary>
        private sealed class Paint
        {
            public Color? Fill = Color.Black;
            public bool FillIsCurrent;
            public double FillOpacity = 1;
            public bool FillEvenOdd;
            public Color? Stroke;
            public bool StrokeIsCurrent;
            public double StrokeOpacity = 1;
            public double StrokeWidth = 1;
            public double[] Dash = Array.Empty<double>();
            public double DashOffset;
            public int Cap;
            public int Join;
            public double Miter = 4;
            public Color Current = Color.Black;
            public bool ClipEvenOdd;
            public string? Family;
            public double FontSize = 16;
            public bool Bold;
            public bool Italic;
            public VectorAnchor Anchor;
            public bool Visible = true;
            public bool Preserve;

            /// <summary>Not inherited in SVG but multiplied down, which is what both outputs do with a group.</summary>
            public double Opacity = 1;

            public Paint Clone() => (Paint)MemberwiseClone();

            public Color? FillColour() => FillIsCurrent ? Current : Fill;
            public Color? StrokeColour() => StrokeIsCurrent ? Current : Stroke;

            public void SetFill(string value, Action<string, string, string> bad)
            {
                FillIsCurrent = IsCurrent(value);
                if (!FillIsCurrent)
                    Fill = Read("fill", value, Fill, bad);
            }

            public void SetStroke(string value, Action<string, string, string> bad)
            {
                StrokeIsCurrent = IsCurrent(value);
                if (!StrokeIsCurrent)
                    Stroke = Read("stroke", value, Stroke, bad);
            }

            private static bool IsCurrent(string value)
                => string.Equals(value, "currentcolor", StringComparison.OrdinalIgnoreCase);

            private static Color? Read(string name, string value, Color? keep, Action<string, string, string> bad)
            {
                if (value is "none" or "transparent")
                    return null;

                if (SvgValues.Colour(value) is { } read)
                    return read;

                bad(name, value, "none, or a colour: a hash, rgb(), rgba() or a CSS name. A gradient or a pattern is not taken");
                return keep;
            }
        }
    }
}
