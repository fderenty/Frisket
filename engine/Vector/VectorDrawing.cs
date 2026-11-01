using System.Globalization;

namespace Frisket
{
    /// <summary>
    /// A drawing as the engine holds it: what <see cref="SvgReader"/> makes of an SVG, and the
    /// one thing both backends draw.
    ///
    /// One list for two outputs on purpose. A proof that recopied the bytes it was given would
    /// be the browser reading them and the PDF reading them, two readers of one file - and that
    /// is exactly how a chart came to show on the exact preview and be refused by the PDF. Here
    /// neither output sees the SVG at all, so they can disagree about nothing but arithmetic.
    /// </summary>
    internal sealed class VectorDrawing
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public List<VectorNode> Nodes { get; } = new();

        /// <summary>Width over height, which is all a drawing says about its size.</summary>
        public double Ratio => Width / Height;

        /// <summary>
        /// A number in a drawing's own units. More places than a page's millimetres need,
        /// because a drawing chooses its own scale: in a viewBox of 0 0 1 1 three decimals
        /// would be a thousandth of the whole picture.
        /// </summary>
        public static string N(double value)
            => double.IsNaN(value) || double.IsInfinity(value)
                ? "0"
                : value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    /// <summary>The six numbers of an SVG matrix, in SVG's order, which is also PDF's.</summary>
    internal readonly record struct VectorMatrix(double A, double B, double C, double D, double E, double F)
    {
        public static VectorMatrix Identity => new(1, 0, 0, 1, 0, 0);

        public bool IsIdentity => this == Identity;

        /// <summary>This matrix applied after <paramref name="next"/>: a point goes through
        /// <paramref name="next"/> first, as in SVG's "this next" written left to right.</summary>
        public VectorMatrix Times(VectorMatrix next) => new(
            A * next.A + C * next.B, B * next.A + D * next.B,
            A * next.C + C * next.D, B * next.C + D * next.D,
            A * next.E + C * next.F + E, B * next.E + D * next.F + F);

        public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    }

    internal enum VectorVerb : byte { Move, Line, Cubic, Close }

    /// <summary>
    /// Absolute moves, lines, cubics and closes, and nothing else: relative coordinates,
    /// smooth shorthands, quadratics, arcs and the basic shapes all come down to these on the
    /// way in, so a backend needs four operators.
    /// </summary>
    internal sealed class VectorPath
    {
        public List<VectorVerb> Verbs { get; } = new();
        public List<double> Points { get; } = new();

        public bool IsEmpty => Verbs.Count == 0;

        public void MoveTo(double x, double y)
        {
            Verbs.Add(VectorVerb.Move);
            Points.Add(x); Points.Add(y);
        }

        public void LineTo(double x, double y)
        {
            Verbs.Add(VectorVerb.Line);
            Points.Add(x); Points.Add(y);
        }

        public void CubicTo(double x1, double y1, double x2, double y2, double x, double y)
        {
            Verbs.Add(VectorVerb.Cubic);
            Points.Add(x1); Points.Add(y1);
            Points.Add(x2); Points.Add(y2);
            Points.Add(x); Points.Add(y);
        }

        public void Close() => Verbs.Add(VectorVerb.Close);

        /// <summary>
        /// The same path with its points moved. Only a clip needs it: a clipPath's own
        /// transform has to go into the points, since a path under construction cannot have a
        /// matrix of its own in PDF.
        /// </summary>
        public VectorPath Transformed(VectorMatrix matrix)
        {
            var moved = new VectorPath();
            moved.Verbs.AddRange(Verbs);

            for (int i = 0; i + 1 < Points.Count; i += 2)
            {
                (double x, double y) = matrix.Apply(Points[i], Points[i + 1]);
                moved.Points.Add(x);
                moved.Points.Add(y);
            }

            return moved;
        }
    }

    /// <summary>What a shape, a group or a text may have around it.</summary>
    internal abstract class VectorNode
    {
        public VectorMatrix Transform { get; set; } = VectorMatrix.Identity;
        public VectorClip? Clip { get; set; }
    }

    internal sealed class VectorClip
    {
        public required VectorPath Path { get; init; }
        public bool EvenOdd { get; init; }
    }

    internal sealed class VectorGroup : VectorNode
    {
        public List<VectorNode> Children { get; } = new();
    }

    /// <summary>
    /// A path and how it is painted. The opacities are folded in on the way down, an SVG
    /// group's opacity included, so a backend reads one number per paint. That is not what
    /// SVG says of a group whose children overlap - it composites them first - and it is what
    /// both outputs here do, because the alternative in PDF is a transparency group per
    /// element to answer a case a chart does not have.
    /// </summary>
    internal sealed class VectorShape : VectorNode
    {
        public required VectorPath Path { get; init; }

        public Color? Fill { get; init; }
        public double FillAlpha { get; init; } = 1;
        public bool EvenOdd { get; init; }

        public Color? Stroke { get; init; }
        public double StrokeAlpha { get; init; } = 1;
        public double StrokeWidth { get; init; } = 1;
        public double[] Dash { get; init; } = Array.Empty<double>();
        public double DashOffset { get; init; }

        /// <summary>0 butt, 1 round, 2 square - PDF's own numbers.</summary>
        public int Cap { get; init; }

        /// <summary>0 miter, 1 round, 2 bevel - PDF's own numbers.</summary>
        public int Join { get; init; }

        public double MiterLimit { get; init; } = 4;
    }

    internal enum VectorAnchor : byte { Start, Middle, End }

    /// <summary>One piece of text with one look. Null X or Y continues from the piece before.</summary>
    internal sealed class VectorRun
    {
        public required string Text { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double Dx { get; init; }
        public double Dy { get; init; }
        public VectorAnchor Anchor { get; init; }
        public required TextStyle Style { get; init; }
        public double Alpha { get; init; } = 1;
    }

    /// <summary>A run with its place worked out, which is what a backend draws.</summary>
    internal readonly record struct PlacedRun(double X, double Y, string Text, TextStyle Style, double Alpha);

    /// <summary>
    /// A text element. It stays pieces until it is drawn because where a piece starts depends
    /// on how wide the pieces before it are, and only a backend has a measurer.
    /// </summary>
    internal sealed class VectorText : VectorNode
    {
        public List<VectorRun> Runs { get; } = new();

        /// <summary>
        /// Where each piece goes, in the drawing's own units.
        ///
        /// Worked out here and not left to text-anchor, which would have the proof's browser
        /// measuring with its metrics and the PDF with the engine's: the same label would sit
        /// a little differently in each, the difference section 2 exists to catch. A size in
        /// the drawing's units reads through <see cref="TextStyle.EmSizeInMillimeters"/>, so
        /// the measurer's millimetres come back in those units too.
        ///
        /// A piece naming no family takes the document's, as any text does: a label in the
        /// library's default inside a report set in something else would be a chart that
        /// looks pasted in.
        /// </summary>
        public List<PlacedRun> Place(ITextMeasurer measurer, string? documentFamily)
        {
            var placed = new List<PlacedRun>(Runs.Count);
            double penX = 0, penY = 0;

            for (int start = 0; start < Runs.Count;)
            {
                // A chunk runs to the next piece that states a position of its own, and
                // text-anchor moves the chunk as a whole.
                int end = start + 1;
                while (end < Runs.Count && Runs[end].X == null && Runs[end].Y == null)
                    end++;

                var styles = new TextStyle[end - start];
                var widths = new double[end - start];
                double total = 0;
                for (int i = start; i < end; i++)
                {
                    TextStyle style = Runs[i].Style;
                    if (style.Family == null && documentFamily != null)
                    {
                        style = style.Clone();
                        style.Family = documentFamily;
                    }

                    styles[i - start] = style;
                    widths[i - start] = measurer.Measure(Runs[i].Text, style, double.PositiveInfinity).Width;
                    total += Runs[i].Dx + widths[i - start];
                }

                double x = (Runs[start].X ?? penX) + Runs[start].Anchor switch
                {
                    VectorAnchor.Middle => -total / 2,
                    VectorAnchor.End => -total,
                    _ => 0
                };
                double y = Runs[start].Y ?? penY;

                for (int i = start; i < end; i++)
                {
                    x += Runs[i].Dx;
                    y += Runs[i].Dy;
                    placed.Add(new PlacedRun(x, y, Runs[i].Text, styles[i - start], Runs[i].Alpha));
                    x += widths[i - start];
                }

                penX = x;
                penY = y;
                start = end;
            }

            return placed;
        }
    }
}
