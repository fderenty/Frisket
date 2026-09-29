using System.Globalization;
using System.Text.Json.Serialization;

namespace Frisket
{
    /// <summary>
    /// Rectangle in section space. All coordinates are in millimeters, absolute,
    /// origin at the top-left corner of the page. Converting to device units is the
    /// backend's job: the engine itself never knows about pixels or DPI.
    /// </summary>
    public readonly struct Rect
    {
        public double Left { get; }
        public double Top { get; }
        public double Right { get; }
        public double Bottom { get; }

        public double Width => Right - Left;
        public double Height => Bottom - Top;

        public bool IsEmpty => !(Width > 0) || !(Height > 0);

        public Rect(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public static Rect FromSize(double left, double top, double width, double height)
            => new(left, top, left + width, top + height);

        /// <summary>Shrinks the rectangle by an edge size (margin or padding).</summary>
        public Rect Deflate(EdgeSize edges)
            => new(Left + edges.Left, Top + edges.Top, Right - edges.Right, Bottom - edges.Bottom);

        /// <summary>Same rectangle with a different top edge, used to carve out bands.</summary>
        public Rect WithTop(double top) => new(Left, top, Right, Bottom);

        /// <summary>Same rectangle with a different bottom edge, used to carve out bands.</summary>
        public Rect WithBottom(double bottom) => new(Left, Top, Right, bottom);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture,
                "[{0:0.##},{1:0.##} {2:0.##}x{3:0.##}]", Left, Top, Width, Height);
    }

    /// <summary>Straight RGBA colour, so the engine does not depend on System.Drawing.</summary>
    public readonly struct Color
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        // A struct has an implicit parameterless constructor, which the serializer would
        // otherwise prefer - reading every colour back as transparent black.
        [JsonConstructor]
        public Color(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static Color Black => new(0, 0, 0);
        public static Color White => new(255, 255, 255);
        public static Color Transparent => new(0, 0, 0, 0);

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", A, R, G, B);
    }
}
