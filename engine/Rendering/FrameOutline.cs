namespace Frisket
{
    /// <summary>
    /// What an item's frame comes to once its sides and its corners have had their say: a fill
    /// over the whole frame inside its rounded corners, and a border along the sides asked for.
    ///
    /// Worked out once for both backends, as <see cref="Justification"/> is, and handed to each
    /// as the shapes it already draws for a picture in SVG - so the PDF and the proof can
    /// disagree about nothing but how they write a path down. A frame with square corners and
    /// a border all round, or none, is not handed over at all: it stays the rectangle both
    /// backends have always drawn, and every document that asks for nothing new comes out as
    /// it did, to the byte.
    /// </summary>
    internal static class FrameOutline
    {
        /// <summary>
        /// A quarter circle as one cubic: each control point sits along its tangent at this
        /// share of the radius. The usual constant, 4/3 (sqrt 2 - 1), which puts the curve's
        /// middle on the circle exactly and strays from it by 0.03 % of the radius elsewhere.
        /// </summary>
        public const double Kappa = 0.5522847498307936;

        /// <summary>The sides the border is drawn on: none at all when there is no pen to draw them with.</summary>
        public static Sides Drawn(PenStyle? pen, Sides sides) => pen == null ? Sides.None : sides & Sides.All;

        /// <summary>
        /// The four radii as drawn: a negative or unreadable one is a square corner, and two along
        /// one side that come to more than the side are scaled down until they fit, all four by
        /// the same factor - CSS's rule, so that a frame half as high as its radii asks has round
        /// ends rather than corners that cross.
        /// </summary>
        public static (double TopLeft, double TopRight, double BottomRight, double BottomLeft) Radii(Rect frame, CornerRadius corners)
        {
            static double Clean(double r) => double.IsFinite(r) && r > 0 ? r : 0;

            double tl = Clean(corners.TopLeft), tr = Clean(corners.TopRight);
            double br = Clean(corners.BottomRight), bl = Clean(corners.BottomLeft);
            double wide = double.IsFinite(frame.Width) ? Math.Max(0, frame.Width) : 0;
            double tall = double.IsFinite(frame.Height) ? Math.Max(0, frame.Height) : 0;

            double factor = 1;
            void Fit(double side, double a, double b)
            {
                if (a + b > side)
                    factor = Math.Min(factor, side / (a + b));
            }

            Fit(wide, tl, tr);
            Fit(wide, bl, br);
            Fit(tall, tl, bl);
            Fit(tall, tr, br);

            return (tl * factor, tr * factor, br * factor, bl * factor);
        }

        /// <summary>
        /// Square at every corner, with the border on every side or on none: the rectangle a
        /// backend has always drawn, which it goes on drawing in the same bytes.
        /// </summary>
        public static bool IsPlain(Rect frame, PenStyle? pen, Sides sides, CornerRadius corners)
        {
            Sides drawn = Drawn(pen, sides);
            if (drawn != Sides.None && drawn != Sides.All)
                return false;

            (double tl, double tr, double br, double bl) = Radii(frame, corners);
            return tl == 0 && tr == 0 && br == 0 && bl == 0;
        }

        /// <summary>
        /// The fill, then the border, as the shapes a backend paints in that order. The fill
        /// covers the whole frame whatever sides the border keeps. A border on all four sides
        /// is one closed path, so its corners are joined; on fewer, each run of sides that touch
        /// is one open path, a corner between two of them drawn with them and a side next to
        /// one not drawn stopping where its corner begins - the straight part of a rounded
        /// frame, and the whole side of a square one.
        /// </summary>
        public static List<VectorShape> Shapes(Rect frame, PenStyle? pen, BrushStyle? fill, Sides sides, CornerRadius corners)
        {
            var radii = Radii(frame, corners);
            var shapes = new List<VectorShape>(2);

            if (fill != null)
            {
                shapes.Add(new VectorShape
                {
                    Path = Trace(frame, Sides.All, radii),
                    Fill = fill.Color,
                    FillAlpha = fill.Color.A / 255.0
                });
            }

            Sides drawn = Drawn(pen, sides);
            if (pen != null && drawn != Sides.None)
            {
                shapes.Add(new VectorShape
                {
                    Path = Trace(frame, drawn, radii),
                    Stroke = pen.Color,
                    StrokeAlpha = pen.Color.A / 255.0,
                    StrokeWidth = pen.Width,
                    Dash = Dashes(pen.DashStyle)
                });
            }

            return shapes;
        }

        /// <summary>The same dashes a rule and a rectangle are drawn with, in millimetres.</summary>
        public static double[] Dashes(DashStyle style) => style switch
        {
            DashStyle.Dash => new[] { 2.0, 1.0 },
            DashStyle.Dot => new[] { 0.4, 0.8 },
            DashStyle.DashDot => new[] { 2.0, 1.0, 0.4, 1.0 },
            _ => Array.Empty<double>()
        };

        // The four sides clockwise from the top, as a page reads with y going down, each
        // followed by the corner that ends it: top then top-right, right then bottom-right,
        // bottom then bottom-left, left then top-left.
        private static readonly Sides[] Around = { Sides.Top, Sides.Right, Sides.Bottom, Sides.Left };

        private static VectorPath Trace(Rect f, Sides drawn,
            (double TopLeft, double TopRight, double BottomRight, double BottomLeft) r)
        {
            // Where each side's straight part starts and ends, in the order Around walks them.
            var starts = new (double X, double Y)[]
            {
                (f.Left + r.TopLeft, f.Top), (f.Right, f.Top + r.TopRight),
                (f.Right - r.BottomRight, f.Bottom), (f.Left, f.Bottom - r.BottomLeft)
            };
            var ends = new (double X, double Y)[]
            {
                (f.Right - r.TopRight, f.Top), (f.Right, f.Bottom - r.BottomRight),
                (f.Left + r.BottomLeft, f.Bottom), (f.Left, f.Top + r.TopLeft)
            };
            double[] after = { r.TopRight, r.BottomRight, r.BottomLeft, r.TopLeft };

            var path = new VectorPath();
            (double X, double Y) at = (double.NaN, double.NaN);

            void Line((double X, double Y) to)
            {
                if (to != at)
                    path.LineTo(to.X, to.Y);
                at = to;
            }

            // The corner ending side i, from where that side stops to where the next begins:
            // each control point leaves along its own tangent, which is the side it touches.
            void Corner(int i)
            {
                if (after[i] == 0)
                    return;

                double k = after[i] * Kappa;
                (double X, double Y) from = ends[i], to = starts[(i + 1) % 4];
                (double x1, double y1, double x2, double y2) = i switch
                {
                    0 => (from.X + k, from.Y, to.X, to.Y - k),
                    1 => (from.X, from.Y + k, to.X + k, to.Y),
                    2 => (from.X - k, from.Y, to.X, to.Y + k),
                    _ => (from.X, from.Y - k, to.X - k, to.Y)
                };
                path.CubicTo(x1, y1, x2, y2, to.X, to.Y);
                at = to;
            }

            if (drawn == Sides.All)
            {
                path.MoveTo(starts[0].X, starts[0].Y);
                at = starts[0];
                for (int i = 0; i < 3; i++)
                {
                    Line(ends[i]);
                    Corner(i);
                }

                // The left side and its corner end where the path began. With the corner
                // square, the close is the left side, as it is in a rectangle; rounded, the
                // corner arrives and the close joins the two ends.
                if (r.TopLeft > 0)
                {
                    Line(ends[3]);
                    Corner(3);
                }

                path.Close();
                return path;
            }

            // Start just after a side that is not drawn, so no run is cut in two by the walk,
            // and as near the top as that allows, so a path begins where a reader would.
            int first = new[] { 3, 2, 1, 0 }.First(i => (drawn & Around[i]) == 0);
            bool open = false;
            for (int step = 1; step <= 4; step++)
            {
                int i = (first + step) % 4;
                if ((drawn & Around[i]) == 0)
                {
                    open = false;
                    continue;
                }

                if (!open)
                {
                    path.MoveTo(starts[i].X, starts[i].Y);
                    at = starts[i];
                    open = true;
                }

                Line(ends[i]);
                if ((drawn & Around[(i + 1) % 4]) != 0)
                    Corner(i);
            }

            return path;
        }
    }
}
